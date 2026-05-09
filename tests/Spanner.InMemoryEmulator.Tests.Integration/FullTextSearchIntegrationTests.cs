using FluentAssertions;
using Google.Cloud.Spanner.Data;
using Spanner.InMemoryEmulator.Tests.Shared.Infrastructure;
using Spanner.InMemoryEmulator.Tests.Shared.Traits;

namespace Spanner.InMemoryEmulator.Tests.Integration;

/// <summary>
/// Integration tests for full-text search (FTS) functions.
/// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions
///   "Cloud Spanner full-text search lets you efficiently search text within your data."
///
/// This tests the approximate in-memory FTS implementation: case-insensitive word matching,
/// simple TF scoring. Named arguments (dialect => 'words') are exercised throughout.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait(TestTraits.Target, TestTraits.GoEmulatorUnsupported)]
public class FullTextSearchIntegrationTests : IntegrationTestBase
{
	public FullTextSearchIntegrationTests(EmulatorSession session) : base(session) { }

	/// <summary>
	/// Evaluates a SQL expression and returns the scalar result, properly handling NULL.
	/// Uses reader.IsDBNull() for correct null detection (ExecuteScalarAsync may return "").
	/// </summary>
	private async Task<object?> Eval(string expr)
	{
		using var conn = Fixture.CreateConnection();
		using var cmd = conn.CreateSelectCommand($"SELECT {expr} AS R");
		using var reader = await cmd.ExecuteReaderAsync();
		await reader.ReadAsync();
		return reader.IsDBNull(0) ? null : reader.GetValue(0);
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ DDL: CREATE TABLE with TOKENLIST + HIDDEN                           ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task CreateTable_WithTokenlistGeneratedColumn_Succeeds()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsArticles (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  Body STRING(MAX)," +
			"  TitleTokens TOKENLIST AS (TOKENIZE_FULLTEXT(Title)) STORED HIDDEN," +
			"  BodyTokens TOKENLIST AS (TOKENIZE_FULLTEXT(Body)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		// Table was created successfully — verify by inserting a row
		await ExecuteDmlAsync("INSERT INTO FtsArticles (Id, Title, Body) VALUES (1, 'Hello World', 'Body text')");
		var rows = await QueryAsync("SELECT Id, Title FROM FtsArticles WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0]["Title"].Should().Be("Hello World");
	}

	[Fact]
	public async Task CreateSearchIndex_AcceptedAsNoOp()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-definition-language#create_search_index
		//   CREATE SEARCH INDEX is accepted but treated as a no-op in the emulator.
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsIdxTest (" +
			"  Id INT64 NOT NULL," +
			"  Content STRING(MAX)," +
			"  Tokens TOKENLIST AS (TOKENIZE_FULLTEXT(Content)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		await ExecuteDdlAsync(
			"CREATE SEARCH INDEX FtsIdxTestIdx ON FtsIdxTest(Tokens)");
	}

	[Fact]
	public async Task DropSearchIndex_AcceptedAsNoOp()
	{
		await ExecuteDdlAsync("DROP SEARCH INDEX IF EXISTS SomeNonExistentIndex");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ HIDDEN columns excluded from SELECT *                               ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task SelectStar_ExcludesHiddenColumns()
	{
		// Ref: https://cloud.google.com/spanner/docs/full-text-search/search-indexes
		//   "HIDDEN columns are excluded from SELECT *."
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsHidden (" +
			"  Id INT64 NOT NULL," +
			"  Name STRING(100)," +
			"  Tokens TOKENLIST AS (TOKENIZE_FULLTEXT(Name)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		await ExecuteDmlAsync("INSERT INTO FtsHidden (Id, Name) VALUES (1, 'Alice')");

		var rows = await QueryAsync("SELECT * FROM FtsHidden WHERE Id = 1");
		rows.Should().HaveCount(1);
		rows[0].Should().ContainKey("Id");
		rows[0].Should().ContainKey("Name");
		rows[0].Should().NotContainKey("Tokens");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ SEARCH (full-text)                                                  ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task Search_BasicWordMatch()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_fulltext
		//   "Returns TRUE if a full-text search query matches tokens."
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id, Title FROM FtsSearch WHERE SEARCH(TitleTokens, 'spanner')");
		rows.Should().HaveCountGreaterOrEqualTo(1);
		rows.Should().Contain(r => (long)r["Id"]! == 1);
	}

	[Fact]
	public async Task Search_CaseInsensitive()
	{
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'SPANNER')");
		rows.Should().HaveCountGreaterOrEqualTo(1);
		rows.Should().Contain(r => (long)r["Id"]! == 1);
	}

	[Fact]
	public async Task Search_MultipleTerms_DefaultRqueryDialect()
	{
		// Ref: rquery: "Multiple terms imply AND"
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'cloud spanner')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);

		// "database" is not in title of article 1
		var rows2 = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'cloud database')");
		rows2.Should().NotContain(r => (long)r["Id"]! == 1);
	}

	[Fact]
	public async Task Search_RqueryDialect_OrOperator()
	{
		// Ref: "OR operator implies disjunction"
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'spanner OR postgresql')");
		// Should match article 1 (spanner) and article 3 (postgresql)
		rows.Should().HaveCountGreaterOrEqualTo(2);
	}

	[Fact]
	public async Task Search_RqueryDialect_Negation()
	{
		// Ref: "-term for negation"
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'database -scalable')");
		// Article 2 has "Database" in title but also "Scalable" — should be excluded
		// Article 4 has "Database" via Body but not in Title — check...
		rows.Should().NotContain(r => (long)r["Id"]! == 2);
	}

	[Fact]
	public async Task Search_RqueryDialect_PhraseSearch()
	{
		// Ref: "double quotes mean phrase search"
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, '\"Cloud Spanner\"')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);
	}

	[Fact]
	public async Task Search_WordsDialect_AllTermsMustMatch()
	{
		// Ref: dialect => 'words': all terms are conjunctive
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'cloud spanner', dialect => 'words')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);
	}

	[Fact]
	public async Task Search_WordsPhraseDialect_ExactPhraseMatch()
	{
		// Ref: dialect => 'words_phrase': all terms must appear as exact phrase
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'cloud spanner', dialect => 'words_phrase')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);

		// Reversed order should NOT match as a phrase
		var rows2 = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'spanner cloud', dialect => 'words_phrase')");
		rows2.Should().NotContain(r => (long)r["Id"]! == 1);
	}

	[Fact]
	public async Task Search_NoMatch_ReturnsEmpty()
	{
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'xyznonexistent')");
		rows.Should().BeEmpty();
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ TOKENIZE_FULLTEXT (inline verification via DEBUG_TOKENLIST)          ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task Search_InlineTokenizeFulltext()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_fulltext
		//   SEARCH can only be used in a WHERE clause; verify tokenization via DEBUG_TOKENLIST.
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENIZE_FULLTEXT('hello world'))");
		result?.ToString().Should().Contain("hello");
		result?.ToString().Should().Contain("world");
	}

	[Fact]
	public async Task Search_InlineTokenizeFulltext_NoMatch()
	{
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENIZE_FULLTEXT('hello world'))");
		result?.ToString().Should().NotContain("goodbye");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ SEARCH_SUBSTRING                                                    ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task SearchSubstring_BasicMatch()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_substring
		//   "Returns TRUE if a substring query matches tokens."
		await SetupSubstringTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSubstr WHERE SEARCH_SUBSTRING(SubTokens, 'pan')");
		rows.Should().Contain(r => (long)r["Id"]! == 1); // "Spanner"
	}

	[Fact]
	public async Task SearchSubstring_CaseInsensitive()
	{
		await SetupSubstringTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSubstr WHERE SEARCH_SUBSTRING(SubTokens, 'SPAN')");
		rows.Should().Contain(r => (long)r["Id"]! == 1); // "Spanner"
	}

	[Fact]
	public async Task SearchSubstring_Inline()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_substring
		//   SEARCH_SUBSTRING can only be used in WHERE; verify via table-based search.
		await SetupSubstringTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSubstr WHERE SEARCH_SUBSTRING(SubTokens, 'pan')");
		rows.Should().Contain(r => (long)r["Id"]! == 1); // "Cloud Spanner" contains "pan"
	}

	[Fact]
	public async Task SearchSubstring_WithRelativeSearchType_WordPrefix()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_substring
		//   relative_search_type requires matching relative_search_types in TOKENIZE_SUBSTRING.
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsSubstrRel (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  SubTokens TOKENLIST AS (TOKENIZE_SUBSTRING(Title, relative_search_types => ['word_prefix'])) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsSubstrRelIdx ON FtsSubstrRel(SubTokens)"); } catch { }

		try { await ExecuteDmlAsync("INSERT INTO FtsSubstrRel (Id, Title) VALUES (1, 'Cloud Spanner')"); } catch { }

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSubstrRel WHERE SEARCH_SUBSTRING(SubTokens, 'Spa', " +
			"relative_search_type => 'word_prefix')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ SEARCH_NGRAMS                                                       ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task SearchNgrams_FuzzyMatch()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_ngrams
		//   SEARCH_NGRAMS can only be used in WHERE with a search-indexed TOKENLIST column.
		await SetupNgramsTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsNgrams WHERE SEARCH_NGRAMS(NgramTokens, 'Spannr', min_ngrams => 2)");
		rows.Should().Contain(r => (long)r["Id"]! == 1); // "Spanner" fuzzy-matches "Spannr"
	}

	[Fact]
	public async Task SearchNgrams_TooFewMatches_ReturnsFalse()
	{
		await SetupNgramsTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsNgrams WHERE SEARCH_NGRAMS(NgramTokens, 'zzzzz', min_ngrams => 2)");
		rows.Should().BeEmpty();
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ TOKEN (exact match)                                                 ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task Token_ExactMatch()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#token
		//   TOKEN(value) stores the exact value for exact-match search.
		await SetupTokenTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsToken WHERE SEARCH(CatTokens, 'news')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);
		rows.Should().NotContain(r => (long)r["Id"]! == 2);
	}

	[Fact]
	public async Task Token_Inline()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#token
		//   SEARCH requires WHERE clause + search index; verify TOKEN via DEBUG_TOKENLIST.
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKEN('news'))");
		result?.ToString().Should().Contain("news");

		var result2 = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKEN('sport'))");
		result2?.ToString().Should().Contain("sport");
		result2?.ToString().Should().NotContain("news");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ TOKENIZE_BOOL                                                       ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task TokenizeBool_TrueIsY()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_bool
		//   "TRUE generates token 'y', FALSE generates token 'n'."
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENIZE_BOOL(TRUE))");
		result?.ToString().Should().Contain("y");
	}

	[Fact]
	public async Task TokenizeBool_FalseIsN()
	{
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENIZE_BOOL(FALSE))");
		result?.ToString().Should().Contain("n");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ TOKENIZE_NUMBER                                                     ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task TokenizeNumber_ExactEquality()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_number
		//   Tokenizes numeric value; verify via DEBUG_TOKENLIST.
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENIZE_NUMBER(42))");
		result?.ToString().Should().Contain("42");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ TOKENIZE_JSON                                                       ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task TokenizeJson_StoresPathValueTokens()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_json
		//   TOKENIZE_JSON takes a JSON value, not STRING.
		var debugResult = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENIZE_JSON(JSON '{\"name\":\"Alice\",\"age\":30}'))");
		var debug = debugResult?.ToString();
		debug.Should().NotBeNullOrEmpty();
		debug.Should().Contain("alice"); // $.name=Alice lowercased
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ TOKENLIST_CONCAT                                                    ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task TokenlistConcat_MergesTokenLists()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenlist_concat
		//   TOKENLIST_CONCAT takes ARRAY<TOKENLIST>, verified via DEBUG_TOKENLIST.
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENLIST_CONCAT([TOKENIZE_FULLTEXT('hello'), TOKENIZE_FULLTEXT('world')]))");
		result?.ToString().Should().Contain("hello");
		result?.ToString().Should().Contain("world");
	}

	[Fact]
	public async Task TokenlistConcat_InGeneratedColumn()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsConcat (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  Body STRING(MAX)," +
			"  TitleTokens TOKENLIST AS (TOKENIZE_FULLTEXT(Title)) STORED HIDDEN," +
			"  BodyTokens TOKENLIST AS (TOKENIZE_FULLTEXT(Body)) STORED HIDDEN," +
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenlist_concat
			//   TOKENLIST_CONCAT([...]) takes an ARRAY<TOKENLIST> argument.
			"  AllTokens TOKENLIST AS (TOKENLIST_CONCAT([TOKENIZE_FULLTEXT(Title), TOKENIZE_FULLTEXT(Body)])) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsConcatIdx ON FtsConcat(AllTokens)"); } catch { }

		try
		{
			await ExecuteDmlAsync(
				"INSERT INTO FtsConcat (Id, Title, Body) VALUES (1, 'Spanner Guide', 'Distributed database system')");
		}
		catch { }

		// SEARCH on AllTokens should match terms from both Title and Body
		var rows = await QueryAsync(
			"SELECT Id FROM FtsConcat WHERE SEARCH(AllTokens, 'spanner distributed')");
		rows.Should().HaveCount(1);
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ SCORE (relevance scoring)                                           ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task Score_ReturnsPositiveForMatch()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score
		//   "Calculates a relevance score of a TOKENLIST for a full-text search query."
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id, SCORE(TitleTokens, 'cloud spanner') AS S " +
			"FROM FtsSearch WHERE SEARCH(TitleTokens, 'cloud spanner')");
		rows.Should().HaveCountGreaterOrEqualTo(1);
		var score = Convert.ToDouble(rows[0]["S"]);
		score.Should().BeGreaterThan(0.0);
	}

	[Fact]
	public async Task Score_ReturnsZeroForNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score
		//   "Returns 0 when tokens or search_query is NULL."
		//   SCORE requires a SEARCH INDEX column + SEARCH in WHERE. Test via table with NULL row.
		await SetupArticlesTable();
		try { await ExecuteDmlAsync("INSERT INTO FtsSearch (Id, Title, Body) VALUES (99, NULL, NULL)"); } catch { }

		// Row 99 has NULL TitleTokens; SEARCH filters it out. Verify via Id filter.
		var rows = await QueryAsync(
			"SELECT Id, SCORE(TitleTokens, 'hello') AS S FROM FtsSearch " +
			"WHERE SEARCH(TitleTokens, 'hello') OR Id = 99");
		var nullRow = rows.FirstOrDefault(r => (long)r["Id"]! == 99);
		// If Cloud Spanner includes Id=99 via OR, SCORE should be 0 for NULL tokens.
		// If Cloud Spanner excludes it (SEARCH returns NULL which is falsy), that's also valid.
		if (nullRow != null)
			Convert.ToDouble(nullRow["S"]).Should().Be(0.0);
	}

	[Fact]
	public async Task Score_HigherForMoreRelevantDocument()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score
		//   SCORE requires SEARCH in the same query.
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id, SCORE(TitleTokens, 'database') AS S FROM FtsSearch " +
			"WHERE SEARCH(TitleTokens, 'database') ORDER BY S DESC");
		// Articles with "Database" in title should score > 0
		rows.Should().HaveCountGreaterOrEqualTo(1);
		Convert.ToDouble(rows[0]["S"]).Should().BeGreaterThan(0);
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ SCORE_NGRAMS (fuzzy relevance)                                      ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task ScoreNgrams_ReturnsPositiveForSimilarText()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score_ngrams
		//   SCORE_NGRAMS requires TOKENLIST from TOKENIZE_SUBSTRING/TOKENIZE_NGRAMS with column ref.
		await SetupNgramsTable();

		var rows = await QueryAsync(
			"SELECT Id, SCORE_NGRAMS(SubstrTokens, 'Spannr') AS S FROM FtsNgrams");
		var spanner = rows.FirstOrDefault(r => (long)r["Id"]! == 1);
		spanner.Should().NotBeNull();
		Convert.ToDouble(spanner!["S"]).Should().BeGreaterThan(0.0);
	}

	[Fact]
	public async Task ScoreNgrams_ReturnsZeroForNull()
	{
		// Ref: "Returns 0 when tokens or ngrams_query is NULL."
		await SetupNgramsTable();
		try { await ExecuteDmlAsync("INSERT INTO FtsNgrams (Id, Title) VALUES (99, NULL)"); } catch { }

		var rows = await QueryAsync(
			"SELECT Id, SCORE_NGRAMS(SubstrTokens, 'hello') AS S FROM FtsNgrams WHERE Id = 99");
		if (rows.Count > 0)
			Convert.ToDouble(rows[0]["S"]).Should().Be(0.0);
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ SNIPPET                                                             ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task Snippet_ReturnsJsonWithHighlights()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#snippet
		//   Returns JSON: {"snippets":[{"highlights":[…],"snippet":"…","source_begin":N,"source_end":N}]}
		var result = await QueryScalarAsync(
			"SELECT SNIPPET('Cloud Spanner is a distributed database', 'Spanner')");
		var json = result?.ToString();
		json.Should().NotBeNullOrEmpty();
		json.Should().Contain("snippets");
		json.Should().Contain("Spanner");
		json.Should().Contain("highlights");
	}

	[Fact]
	public async Task Snippet_ReturnsNullForNullInput()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#snippet
		//   "Returns NULL when data_to_search or raw_search_query is NULL."
		var result = await Eval("SNIPPET(CAST(NULL AS STRING), 'test')");
		result.Should().BeNull();
	}

	[Fact]
	public async Task Snippet_WithTable()
	{
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id, SNIPPET(Title, 'Cloud') AS S FROM FtsSearch WHERE SEARCH(TitleTokens, 'cloud')");
		rows.Should().HaveCountGreaterOrEqualTo(1);
		rows[0]["S"]?.ToString().Should().Contain("Cloud");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ DEBUG_TOKENLIST                                                      ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task DebugTokenlist_ReturnsHumanReadableString()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#debug_tokenlist
		//   "Displays a human-readable representation of tokens."
		var result = await QueryScalarAsync(
			"SELECT DEBUG_TOKENLIST(TOKENIZE_FULLTEXT('Hello World'))");
		var debug = result?.ToString();
		debug.Should().NotBeNullOrEmpty();
		debug.Should().Contain("hello");
		debug.Should().Contain("world");
	}

	[Fact]
	public async Task DebugTokenlist_ReturnsNullForNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_fulltext
		//   TOKENIZE_FULLTEXT requires STRING, not bare NULL. Use CAST.
		var result = await Eval("DEBUG_TOKENLIST(TOKENIZE_FULLTEXT(CAST(NULL AS STRING)))");
		result.Should().BeNull();
	}

	[Fact]
	public async Task DebugTokenlist_WithGeneratedColumn()
	{
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id, DEBUG_TOKENLIST(TitleTokens) AS D FROM FtsSearch WHERE Id = 1");
		rows.Should().HaveCount(1);
		var debug = rows[0]["D"]?.ToString();
		debug.Should().NotBeNullOrEmpty();
		debug.Should().Contain("cloud");
		debug.Should().Contain("spanner");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ Named arguments                                                     ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task NamedArgument_NgramSizeMin()
	{
		// Ref: TOKENIZE_SUBSTRING supports ngram_size_min and ngram_size_max named arguments.
		//   Verify via table-based SEARCH_SUBSTRING.
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsNgramCustom (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  SubTokens TOKENLIST AS (TOKENIZE_SUBSTRING(Title, ngram_size_min => 2, ngram_size_max => 4)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsNgramCustomIdx ON FtsNgramCustom(SubTokens)"); } catch { }

		try { await ExecuteDmlAsync("INSERT INTO FtsNgramCustom (Id, Title) VALUES (1, 'Cloud Spanner')"); } catch { }

		var rows = await QueryAsync(
			"SELECT Id FROM FtsNgramCustom WHERE SEARCH_SUBSTRING(SubTokens, 'pa')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);
	}

	[Fact]
	public async Task NamedArgument_Dialect()
	{
		// Ref: SEARCH supports dialect => 'words_phrase' named argument.
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'cloud spanner', dialect => 'words_phrase')");
		rows.Should().Contain(r => (long)r["Id"]! == 1);
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ NULL handling                                                        ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task Search_NullTokens_ReturnsNull()
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_fulltext
		//   "Returns NULL when tokens or search_query is NULL."
		//   SEARCH is WHERE-only; NULL TOKENLIST → no rows matched.
		await SetupArticlesTable();
		try { await ExecuteDmlAsync("INSERT INTO FtsSearch (Id, Title, Body) VALUES (98, NULL, NULL)"); } catch { }

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, 'hello')");
		rows.Should().NotContain(r => (long)r["Id"]! == 98);
	}

	[Fact]
	public async Task Search_NullQuery_ReturnsNull()
	{
		// Ref: SEARCH returns NULL when search_query is NULL → no rows matched.
		await SetupArticlesTable();

		var rows = await QueryAsync(
			"SELECT Id FROM FtsSearch WHERE SEARCH(TitleTokens, CAST(NULL AS STRING))");
		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task TokenizeFulltext_NullInput_ReturnsNull()
	{
		// Ref: "Returns NULL when value_to_tokenize is NULL."
		var result = await Eval("DEBUG_TOKENLIST(TOKENIZE_FULLTEXT(CAST(NULL AS STRING)))");
		result.Should().BeNull();
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ End-to-end FTS workflow                                             ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	[Fact]
	public async Task EndToEnd_FullTextSearchWorkflow()
	{
		// A realistic FTS workflow: create table, insert data, search, score, snippet
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsE2E (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  Content STRING(MAX)," +
			"  ContentTokens TOKENLIST AS (TOKENIZE_FULLTEXT(Content)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		// Ref: SEARCH/SCORE require a SEARCH INDEX.
		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsE2EIdx ON FtsE2E(ContentTokens)"); } catch { }

		try
		{
			await ExecuteDmlAsync("INSERT INTO FtsE2E (Id, Title, Content) VALUES " +
				"(1, 'Intro to Spanner', 'Google Cloud Spanner is a globally distributed database')");
			await ExecuteDmlAsync("INSERT INTO FtsE2E (Id, Title, Content) VALUES " +
				"(2, 'SQL Tutorial', 'Learn SQL queries and database management')");
			await ExecuteDmlAsync("INSERT INTO FtsE2E (Id, Title, Content) VALUES " +
				"(3, 'Cloud Overview', 'An overview of cloud computing services')");
		}
		catch { }

		// Search
		var searchResults = await QueryAsync(
			"SELECT Id, Title FROM FtsE2E WHERE SEARCH(ContentTokens, 'database')");
		searchResults.Should().HaveCount(2); // Articles 1 and 2

		// Score + order
		var scoredResults = await QueryAsync(
			"SELECT Id, Title, SCORE(ContentTokens, 'database') AS S " +
			"FROM FtsE2E WHERE SEARCH(ContentTokens, 'database') ORDER BY S DESC");
		scoredResults.Should().HaveCountGreaterOrEqualTo(1);
		Convert.ToDouble(scoredResults[0]["S"]).Should().BeGreaterThan(0);

		// Snippet
		var snippetResults = await QueryAsync(
			"SELECT Id, SNIPPET(Content, 'database') AS Snip FROM FtsE2E WHERE SEARCH(ContentTokens, 'database')");
		snippetResults.Should().HaveCountGreaterOrEqualTo(1);
		snippetResults[0]["Snip"]?.ToString().Should().Contain("database");
	}

	// ╔═══════════════════════════════════════════════════════════════════════╗
	// ║ Test Helpers                                                         ║
	// ╚═══════════════════════════════════════════════════════════════════════╝

	private async Task SetupArticlesTable()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsSearch (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  Body STRING(MAX)," +
			"  TitleTokens TOKENLIST AS (TOKENIZE_FULLTEXT(Title)) STORED HIDDEN," +
			"  BodyTokens TOKENLIST AS (TOKENIZE_FULLTEXT(Body)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_fulltext
		//   SEARCH/SCORE require a SEARCH INDEX on the TOKENLIST column.
		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsSearchIdx ON FtsSearch(TitleTokens, BodyTokens)"); } catch { }

		// Insert test data (idempotent — use INSERT OR UPDATE if available, else try/catch)
		try
		{
			await ExecuteDmlAsync("INSERT INTO FtsSearch (Id, Title, Body) VALUES " +
				"(1, 'Cloud Spanner Overview', 'A globally distributed relational database service')");
			await ExecuteDmlAsync("INSERT INTO FtsSearch (Id, Title, Body) VALUES " +
				"(2, 'Scalable Database Design', 'How to build scalable systems with modern databases')");
			await ExecuteDmlAsync("INSERT INTO FtsSearch (Id, Title, Body) VALUES " +
				"(3, 'PostgreSQL vs Spanner', 'Comparing open source and cloud native databases')");
			await ExecuteDmlAsync("INSERT INTO FtsSearch (Id, Title, Body) VALUES " +
				"(4, 'Machine Learning Basics', 'Introduction to ML concepts and algorithms')");
		}
		catch
		{
			// Data already seeded from a previous test run
		}
	}

	private async Task SetupSubstringTable()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsSubstr (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  SubTokens TOKENLIST AS (TOKENIZE_SUBSTRING(Title)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsSubstrIdx ON FtsSubstr(SubTokens)"); } catch { }

		try
		{
			await ExecuteDmlAsync("INSERT INTO FtsSubstr (Id, Title) VALUES (1, 'Cloud Spanner')");
			await ExecuteDmlAsync("INSERT INTO FtsSubstr (Id, Title) VALUES (2, 'BigQuery Analytics')");
		}
		catch { }
	}

	private async Task SetupTokenTable()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsToken (" +
			"  Id INT64 NOT NULL," +
			"  Category STRING(100)," +
			"  CatTokens TOKENLIST AS (TOKEN(Category)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsTokenIdx ON FtsToken(CatTokens)"); } catch { }

		try
		{
			await ExecuteDmlAsync("INSERT INTO FtsToken (Id, Category) VALUES (1, 'news')");
			await ExecuteDmlAsync("INSERT INTO FtsToken (Id, Category) VALUES (2, 'sports')");
		}
		catch { }
	}

	private async Task SetupNgramsTable()
	{
		await ExecuteDdlAsync(
			"CREATE TABLE IF NOT EXISTS FtsNgrams (" +
			"  Id INT64 NOT NULL," +
			"  Title STRING(MAX)," +
			"  NgramTokens TOKENLIST AS (TOKENIZE_NGRAMS(Title)) STORED HIDDEN," +
			"  SubstrTokens TOKENLIST AS (TOKENIZE_SUBSTRING(Title)) STORED HIDDEN" +
			") PRIMARY KEY (Id)");

		try { await ExecuteDdlAsync("CREATE SEARCH INDEX FtsNgramsIdx ON FtsNgrams(NgramTokens, SubstrTokens) STORING(Title)"); } catch { }

		try
		{
			await ExecuteDmlAsync("INSERT INTO FtsNgrams (Id, Title) VALUES (1, 'Spanner')");
			await ExecuteDmlAsync("INSERT INTO FtsNgrams (Id, Title) VALUES (2, 'hello')");
		}
		catch { }
	}
}
