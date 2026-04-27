using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Represents a GoogleSQL TOKENLIST value — an in-memory approximation used by the
/// full-text search functions (SEARCH, SCORE, SNIPPET, etc.).
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions
///   "A TOKENLIST is a collection of tokens produced by one of the TOKENIZE_* functions."
///
/// This is an approximate implementation: real Cloud Spanner uses inverted indexes
/// with positional information, BM25 scoring, and NLP tokenization. This emulator
/// uses case-insensitive word tokenization and simple term-frequency scoring,
/// following the same pragmatic approach as the CosmosDB In-Memory Emulator.
/// </summary>
internal sealed class SpannerTokenList
{
	/// <summary>The kind of tokenization applied.</summary>
	public TokenListKind Kind { get; }

	/// <summary>
	/// The original source text (or string representation of the value) that was tokenised.
	/// Needed by SCORE (to count hits), SNIPPET (to locate matches), and DEBUG_TOKENLIST.
	/// </summary>
	public string? SourceText { get; }

	/// <summary>
	/// The set of distinct, lowercased tokens produced by the tokenization function.
	/// For full-text: individual words.  For substring/ngrams: n-gram character sequences.
	/// For exact-match (TOKEN): the verbatim lowercased value(s).
	/// </summary>
	public HashSet<string> Tokens { get; }

	/// <summary>Maximum n-gram size (relevant for Substring and Ngrams kinds).</summary>
	public int NgramSizeMax { get; }

	/// <summary>Minimum n-gram size (relevant for Substring and Ngrams kinds).</summary>
	public int NgramSizeMin { get; }

	private SpannerTokenList(TokenListKind kind, string? sourceText, HashSet<string> tokens,
		int ngramSizeMin = 1, int ngramSizeMax = 4)
	{
		Kind = kind;
		SourceText = sourceText;
		Tokens = tokens;
		NgramSizeMin = ngramSizeMin;
		NgramSizeMax = ngramSizeMax;
	}

	// ────────────────────────────────────────────────────────────
	//  Factory methods (one per TOKENIZE_* function)
	// ────────────────────────────────────────────────────────────

	/// <summary>
	/// TOKEN(value) — exact-match tokenization. The value is stored verbatim (lowercased).
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#token
	/// </summary>
	public static SpannerTokenList FromToken(object? value)
	{
		if (value is null) return Null;
		var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (value is IList<object?> list)
		{
			foreach (var item in list)
				if (item is string s) tokens.Add(s.ToLowerInvariant());
		}
		else
		{
			tokens.Add(value.ToString()!.ToLowerInvariant());
		}
		return new SpannerTokenList(TokenListKind.Exact, value.ToString(), tokens);
	}

	/// <summary>
	/// TOKENIZE_FULLTEXT(value) — splits text into lowercase words, strips punctuation.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_fulltext
	///   "Constructs a full-text TOKENLIST value by tokenizing text for full-text matching."
	///   Capitalization and delimiters are removed during tokenization.
	/// </summary>
	public static SpannerTokenList FromFullText(string? text)
	{
		if (text is null) return Null;
		var words = TokenizeToWords(text);
		return new SpannerTokenList(TokenListKind.FullText, text,
			new HashSet<string>(words, StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>
	/// TOKENIZE_SUBSTRING(value) — splits into words, then generates n-grams per word.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_substring
	///   "value_to_tokenize is split into lower-cased words first, then n-gram tokens
	///    are generated from each word."
	/// </summary>
	public static SpannerTokenList FromSubstring(string? text, int ngramSizeMin = 1, int ngramSizeMax = 4)
	{
		if (text is null) return Null;
		var words = TokenizeToWords(text);
		var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var word in words)
		{
			// Whole words are always included (even if shorter than ngramSizeMin)
			tokens.Add(word);
			GenerateNgrams(word, ngramSizeMin, ngramSizeMax, tokens);
		}
		return new SpannerTokenList(TokenListKind.Substring, text, tokens, ngramSizeMin, ngramSizeMax);
	}

	/// <summary>
	/// TOKENIZE_NGRAMS(value) — generates n-grams from the raw text (no word splitting).
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_ngrams
	///   All characters, including whitespace, are used for n-gram generation.
	/// </summary>
	public static SpannerTokenList FromNgrams(string? text, int ngramSizeMin = 1, int ngramSizeMax = 4)
	{
		if (text is null) return Null;
		var lower = text.ToLowerInvariant();
		var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		GenerateNgrams(lower, ngramSizeMin, ngramSizeMax, tokens);
		return new SpannerTokenList(TokenListKind.Ngrams, text, tokens, ngramSizeMin, ngramSizeMax);
	}

	/// <summary>
	/// TOKENIZE_NUMBER(value) — stores a string representation of the number.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_number
	///   In the real system, this generates range-tree tokens. Here we store a simple
	///   equality token so that numeric equality queries work.
	/// </summary>
	public static SpannerTokenList FromNumber(object? value)
	{
		if (value is null) return Null;
		var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (value is IList<object?> list)
		{
			foreach (var item in list)
				if (item is not null)
					tokens.Add(Convert.ToDouble(item).ToString("R", CultureInfo.InvariantCulture));
		}
		else
		{
			tokens.Add(Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture));
		}
		return new SpannerTokenList(TokenListKind.Number, value.ToString(), tokens);
	}

	/// <summary>
	/// TOKENIZE_BOOL(value) — stores "y" for TRUE, "n" for FALSE.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_bool
	///   "IsAwarded with TRUE generates IsAwardedToken with value 'y'."
	///   "IsAwarded with FALSE generates IsAwardedToken with value 'n'."
	/// </summary>
	public static SpannerTokenList FromBool(object? value)
	{
		if (value is null) return Null;
		var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (value is IList<object?> list)
		{
			foreach (var item in list)
				if (item is bool b) tokens.Add(b ? "y" : "n");
		}
		else
		{
			tokens.Add(Convert.ToBoolean(value) ? "y" : "n");
		}
		return new SpannerTokenList(TokenListKind.Bool, value.ToString(), tokens);
	}

	/// <summary>
	/// TOKENIZE_JSON(value) — tokenizes JSON paths and values.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_json
	///   Real Spanner tokenizes JSON for predicate acceleration. This approximation
	///   stores flattened path=value tokens.
	/// </summary>
	public static SpannerTokenList FromJson(string? json)
	{
		if (json is null) return Null;
		var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using var doc = JsonDocument.Parse(json);
			FlattenJson("$", doc.RootElement, tokens);
		}
		catch (JsonException)
		{
			// If the JSON is invalid, return an empty token list
		}
		return new SpannerTokenList(TokenListKind.Json, json, tokens);
	}

	/// <summary>
	/// TOKENLIST_CONCAT(list1, list2, ...) — merges multiple token lists.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenlist_concat
	/// </summary>
	public static SpannerTokenList Concat(IEnumerable<SpannerTokenList?> lists)
	{
		var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var sourceBuilder = new StringBuilder();
		TokenListKind? kind = null;
		foreach (var tl in lists)
		{
			if (tl is null || tl == Null) continue;
			kind ??= tl.Kind;
			foreach (var t in tl.Tokens) merged.Add(t);
			if (tl.SourceText is not null)
			{
				if (sourceBuilder.Length > 0) sourceBuilder.Append(' ');
				sourceBuilder.Append(tl.SourceText);
			}
		}
		return new SpannerTokenList(kind ?? TokenListKind.FullText, sourceBuilder.ToString(), merged);
	}

	/// <summary>Represents a NULL TOKENLIST.</summary>
	public static readonly SpannerTokenList Null = new(TokenListKind.FullText, null, new HashSet<string>());

	public bool IsNull => SourceText is null;

	// ────────────────────────────────────────────────────────────
	//  Search methods
	// ────────────────────────────────────────────────────────────

	/// <summary>
	/// SEARCH: full-text search using approximate "words" dialect.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_fulltext
	///   "Returns TRUE if a full-text search query matches tokens."
	///   Default dialect is "rquery": multiple terms imply AND, OR is disjunction,
	///   double-quotes mean phrase, dash means negation. Search is case-insensitive.
	///
	/// This approximation implements:
	///   - rquery: AND by default, OR for disjunction, -term for negation, "phrase" for phrases
	///   - words: all terms must be present (conjunctive)
	///   - words_phrase: exact phrase match
	/// </summary>
	public bool Search(string? query, string? dialect = null)
	{
		if (query is null || IsNull) return false;
		dialect ??= "rquery";
		return dialect.ToLowerInvariant() switch
		{
			"words" => SearchWords(query),
			"words_phrase" => SearchPhrase(query),
			_ => SearchRquery(query) // "rquery" or any default
		};
	}

	/// <summary>
	/// SEARCH_SUBSTRING: substring match against tokens.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_substring
	///   "Returns TRUE if a substring query matches tokens."
	///   The query is split into words, and ALL words must appear as substrings.
	/// </summary>
	public bool SearchSubstring(string? query, string? relativeSearchType = null)
	{
		if (query is null || IsNull) return false;
		var source = SourceText!.ToLowerInvariant();
		var queryTerms = TokenizeToWords(query);
		if (queryTerms.Count == 0) return true;

		return relativeSearchType?.ToLowerInvariant() switch
		{
			"value_prefix" => queryTerms.All(t => source.StartsWith(t, StringComparison.OrdinalIgnoreCase)),
			"value_suffix" => queryTerms.All(t => source.EndsWith(t, StringComparison.OrdinalIgnoreCase)),
			"word_prefix" => queryTerms.All(t =>
				TokenizeToWords(SourceText!).Any(w => w.StartsWith(t, StringComparison.OrdinalIgnoreCase))),
			"word_suffix" => queryTerms.All(t =>
				TokenizeToWords(SourceText!).Any(w => w.EndsWith(t, StringComparison.OrdinalIgnoreCase))),
			"phrase" => SearchPhrase(query),
			_ => queryTerms.All(t => source.Contains(t, StringComparison.OrdinalIgnoreCase))
		};
	}

	/// <summary>
	/// SEARCH_NGRAMS: fuzzy n-gram search.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_ngrams
	///   "Checks whether enough n-grams match the tokens in a fuzzy search."
	///   Generates n-grams from query and checks if at least min_ngrams match.
	/// </summary>
	public bool SearchNgrams(string? query, int minNgrams = 2, double? minNgramsPercent = null)
	{
		if (query is null || IsNull) return false;
		if (query.Length < NgramSizeMin) return false;

		// Generate query n-grams using the same method as the tokenizer
		var queryNgrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (Kind == TokenListKind.Substring)
		{
			// TOKENIZE_SUBSTRING splits into words first
			var words = TokenizeToWords(query);
			foreach (var word in words)
				GenerateNgrams(word, NgramSizeMin, NgramSizeMax, queryNgrams);
		}
		else
		{
			// TOKENIZE_NGRAMS uses raw text
			GenerateNgrams(query.ToLowerInvariant(), NgramSizeMin, NgramSizeMax, queryNgrams);
		}

		if (queryNgrams.Count == 0) return false;

		var matchCount = queryNgrams.Count(ng => Tokens.Contains(ng));

		if (minNgramsPercent.HasValue)
		{
			var requiredPercent = minNgramsPercent.Value;
			var actualPercent = (double)matchCount / queryNgrams.Count * 100.0;
			return actualPercent >= requiredPercent;
		}

		return matchCount >= minNgrams;
	}

	// ────────────────────────────────────────────────────────────
	//  Scoring methods
	// ────────────────────────────────────────────────────────────

	/// <summary>
	/// SCORE: approximate relevance score for full-text search.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score
	///   "Calculates a relevance score of a TOKENLIST for a full-text search query."
	///   Returns 0 when tokens or search_query is NULL.
	///
	/// Approximation: we compute a simple term-frequency score.
	/// Adjacent term pairs (bigrams) get a bonus (default weight 2.0).
	/// </summary>
	public double Score(string? query)
	{
		if (query is null || IsNull) return 0.0;
		var queryTerms = TokenizeToWords(query);
		if (queryTerms.Count == 0) return 0.0;
		if (SourceText is null) return 0.0;

		var sourceWords = TokenizeToWords(SourceText);
		if (sourceWords.Count == 0) return 0.0;

		// Term frequency: count how many query terms appear in the source
		double score = 0.0;
		var matchedTerms = 0;
		foreach (var qt in queryTerms)
		{
			var count = sourceWords.Count(sw => sw.Equals(qt, StringComparison.OrdinalIgnoreCase));
			if (count > 0)
			{
				matchedTerms++;
				// Log-scaled TF: 1 + log(count)
				score += 1.0 + Math.Log(count);
			}
		}

		if (matchedTerms == 0) return 0.0;

		// Bigram bonus: boost for adjacent query terms found adjacent in source
		const double bigramWeight = 2.0;
		for (var i = 0; i < queryTerms.Count - 1; i++)
		{
			var term1 = queryTerms[i];
			var term2 = queryTerms[i + 1];
			for (var j = 0; j < sourceWords.Count - 1; j++)
			{
				if (sourceWords[j].Equals(term1, StringComparison.OrdinalIgnoreCase) &&
					sourceWords[j + 1].Equals(term2, StringComparison.OrdinalIgnoreCase))
				{
					score += bigramWeight;
					break;
				}
			}
		}

		// Normalize by number of query terms to keep scores reasonable
		return score / queryTerms.Count;
	}

	/// <summary>
	/// SCORE_NGRAMS: trigram-based fuzzy matching score.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score_ngrams
	///   "The score is roughly calculated as (match_count / (query_trigrams + source_trigrams - match_count))."
	///   Returns 0 when tokens or ngrams_query is NULL.
	/// </summary>
	public double ScoreNgrams(string? query)
	{
		if (query is null || IsNull) return 0.0;
		if (SourceText is null) return 0.0;

		// Generate trigrams from query
		var queryTrigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (Kind == TokenListKind.Substring)
		{
			foreach (var word in TokenizeToWords(query))
				GenerateNgrams(word, 3, 3, queryTrigrams);
		}
		else
		{
			GenerateNgrams(query.ToLowerInvariant(), 3, 3, queryTrigrams);
		}

		// Generate trigrams from source
		var sourceTrigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (Kind == TokenListKind.Substring)
		{
			foreach (var word in TokenizeToWords(SourceText))
				GenerateNgrams(word, 3, 3, sourceTrigrams);
		}
		else
		{
			GenerateNgrams(SourceText.ToLowerInvariant(), 3, 3, sourceTrigrams);
		}

		if (queryTrigrams.Count == 0 || sourceTrigrams.Count == 0) return 0.0;

		var matchCount = queryTrigrams.Count(ng => sourceTrigrams.Contains(ng));
		// Jaccard-like coefficient
		// Ref: "score is roughly calculated as (match_count / (query_trigrams + source_trigrams - match_count))"
		var denominator = queryTrigrams.Count + sourceTrigrams.Count - matchCount;
		return denominator > 0 ? (double)matchCount / denominator : 0.0;
	}

	// ────────────────────────────────────────────────────────────
	//  SNIPPET
	// ────────────────────────────────────────────────────────────

	/// <summary>
	/// SNIPPET: extract matching snippets with highlight positions.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#snippet
	///   Returns JSON with format: {"snippets":[{"highlights":[{"begin":N,"end":N}],"snippet":"...","source_begin":N,"source_end":N}]}
	///   Returns NULL when data_to_search or raw_search_query is NULL.
	/// </summary>
	public static string? Snippet(string? dataToSearch, string? query, int maxSnippetWidth = 200, int maxSnippets = 1)
	{
		if (dataToSearch is null || query is null) return null;
		var queryTerms = TokenizeToWords(query);
		if (queryTerms.Count == 0)
			return JsonSerializer.Serialize(new { snippets = Array.Empty<object>() });

		var snippets = new List<object>();
		// Find all term occurrences in the source text
		var text = dataToSearch;
		var highlights = new List<object>();

		foreach (var term in queryTerms)
		{
			var idx = 0;
			while (idx < text.Length)
			{
				var pos = text.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase);
				if (pos < 0) break;
				// Use 1-based positions to match GCP Spanner behavior
				highlights.Add(new { begin = (pos + 1).ToString(), end = (pos + term.Length + 1).ToString() });
				idx = pos + 1;
			}
		}

		// Create a snippet covering the text (truncated to maxSnippetWidth)
		var snippetText = text.Length > maxSnippetWidth ? text[..maxSnippetWidth] : text;
		snippets.Add(new
		{
			highlights,
			snippet = snippetText,
			source_begin = 1,
			source_end = snippetText.Length + 1
		});

		if (snippets.Count > maxSnippets)
			snippets = snippets.Take(maxSnippets).ToList();

		return JsonSerializer.Serialize(new { snippets });
	}

	// ────────────────────────────────────────────────────────────
	//  DEBUG_TOKENLIST
	// ────────────────────────────────────────────────────────────

	/// <summary>
	/// DEBUG_TOKENLIST: human-readable representation of tokens.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#debug_tokenlist
	///   "Displays a human-readable representation of tokens present in a TOKENLIST value."
	/// </summary>
	public string DebugString()
	{
		if (IsNull) return "";
		return string.Join(", ", Tokens.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
	}

	// ────────────────────────────────────────────────────────────
	//  Internal search helpers
	// ────────────────────────────────────────────────────────────

	/// <summary>
	/// rquery dialect: AND by default, OR for disjunction, -term for negation, "phrase" for phrases.
	/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_fulltext
	///   "Multiple terms imply AND", "OR operator implies disjunction", "-term" for negation,
	///   "double quotes mean phrase search", "search is case insensitive".
	/// </summary>
	private bool SearchRquery(string query)
	{
		// Parse rquery into groups: (term|term) AND (term|term) ...
		// Split on whitespace, handle OR/| between adjacent terms
		var rawTokens = Regex.Matches(query, @"""[^""]*""|\S+")
			.Select(m => m.Value)
			.ToList();

		// Build AND-groups: each group is set of OR'd terms
		var andGroups = new List<List<(string term, bool negated, bool isPhrase)>>();
		var currentGroup = new List<(string term, bool negated, bool isPhrase)>();
		var expectOr = false;

		foreach (var raw in rawTokens)
		{
			if (raw.Equals("OR", StringComparison.Ordinal) || raw == "|")
			{
				expectOr = true;
				continue;
			}

			var negated = false;
			var token = raw;
			if (token.StartsWith('-') && token.Length > 1)
			{
				negated = true;
				token = token[1..];
			}

			var isPhrase = token.StartsWith('"') && token.EndsWith('"');
			if (isPhrase) token = token[1..^1];

			if (!expectOr && currentGroup.Count > 0)
			{
				andGroups.Add(currentGroup);
				currentGroup = new List<(string, bool, bool)>();
			}
			expectOr = false;
			currentGroup.Add((token, negated, isPhrase));
		}
		if (currentGroup.Count > 0) andGroups.Add(currentGroup);

		// All AND-groups must match: at least one alternative in each group must be true
		return andGroups.All(group => group.Any(alt =>
		{
			var match = alt.isPhrase
				? SourceText!.Contains(alt.term, StringComparison.OrdinalIgnoreCase)
				: Tokens.Contains(alt.term.ToLowerInvariant());
			return alt.negated ? !match : match;
		}));
	}

	/// <summary>
	/// "words" dialect: all terms must be present (conjunctive, case-insensitive).
	/// Ref: "Multiple terms imply AND."
	/// </summary>
	private bool SearchWords(string query)
	{
		var terms = TokenizeToWords(query);
		return terms.All(t => Tokens.Contains(t));
	}

	/// <summary>
	/// "words_phrase" dialect: all terms must appear adjacent and in order.
	/// Ref: "Multiple terms imply a phrase."
	/// </summary>
	private bool SearchPhrase(string query)
	{
		var terms = TokenizeToWords(query);
		if (terms.Count == 0) return true;
		var sourceWords = TokenizeToWords(SourceText!);
		for (var i = 0; i <= sourceWords.Count - terms.Count; i++)
		{
			var match = true;
			for (var j = 0; j < terms.Count; j++)
			{
				if (!sourceWords[i + j].Equals(terms[j], StringComparison.OrdinalIgnoreCase))
				{
					match = false;
					break;
				}
			}
			if (match) return true;
		}
		return false;
	}

	// ────────────────────────────────────────────────────────────
	//  Static tokenization helpers
	// ────────────────────────────────────────────────────────────

	/// <summary>
	/// Splits text into lowercase words by stripping punctuation and splitting on whitespace.
	/// </summary>
	internal static List<string> TokenizeToWords(string text)
	{
		// Remove punctuation (keep alphanumeric, whitespace, and #)
		var cleaned = Regex.Replace(text, @"[^\w\s#]", " ");
		return cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
			.Select(w => w.ToLowerInvariant())
			.ToList();
	}

	/// <summary>
	/// Generates n-grams of sizes [min..max] from the input string.
	/// </summary>
	internal static void GenerateNgrams(string input, int min, int max, HashSet<string> output)
	{
		for (var size = min; size <= max; size++)
		{
			for (var i = 0; i <= input.Length - size; i++)
			{
				output.Add(input.Substring(i, size));
			}
		}
	}

	/// <summary>
	/// Recursively flatten a JSON element into path=value tokens for TOKENIZE_JSON.
	/// </summary>
	private static void FlattenJson(string path, JsonElement element, HashSet<string> tokens)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var prop in element.EnumerateObject())
					FlattenJson($"{path}.{prop.Name}", prop.Value, tokens);
				break;
			case JsonValueKind.Array:
				var index = 0;
				foreach (var item in element.EnumerateArray())
				{
					FlattenJson($"{path}[{index}]", item, tokens);
					index++;
				}
				break;
			default:
				tokens.Add($"{path}={element}".ToLowerInvariant());
				break;
		}
	}

	public override string ToString() => DebugString();
}

/// <summary>
/// The kind of tokenization that produced this TOKENLIST.
/// </summary>
internal enum TokenListKind
{
	FullText,
	Substring,
	Ngrams,
	Number,
	Bool,
	Exact,
	Json
}
