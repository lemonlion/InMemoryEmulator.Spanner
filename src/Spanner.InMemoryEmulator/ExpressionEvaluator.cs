using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Google.Cloud.Spanner.V1;
using Spanner.InMemoryEmulator.Parsing;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Spanner.InMemoryEmulator;

/// <summary>
/// Evaluates SQL expressions against a row context, handling operators,
/// functions, parameters, and column references.
/// </summary>
internal class ExpressionEvaluator
{
	private readonly IDictionary<string, object?>? _parameters;
	private readonly QueryExecutor? _queryExecutor;
	private readonly Dictionary<string, QueryBody>? _cteMap;
	private readonly Dictionary<string, object?>? _outerRow;

	public ExpressionEvaluator(IDictionary<string, object?>? parameters = null,
		QueryExecutor? queryExecutor = null,
		Dictionary<string, QueryBody>? cteMap = null,
		Dictionary<string, object?>? outerRow = null)
	{
		_parameters = parameters;
		_queryExecutor = queryExecutor;
		_cteMap = cteMap;
		_outerRow = outerRow;
	}

	/// <summary>
	/// Evaluates a SQL expression against the given row.
	/// </summary>
	public object? Evaluate(SqlExpression expr, Dictionary<string, object?> row)
	{
		return expr switch
		{
			LiteralExpr lit => lit.Value,
			ColumnRefExpr col => EvalColumnRef(col, row),
			ParameterExpr param => EvalParameter(param),
			BinaryExpr bin => EvalBinary(bin, row),
			UnaryExpr unary => EvalUnary(unary, row),
			IsNullExpr isNull => EvalIsNull(isNull, row),
			InExpr inExpr => EvalIn(inExpr, row),
			BetweenExpr between => EvalBetween(between, row),
			FunctionCallExpr func => EvalFunction(func, row),
			CastExpr cast => EvalCast(cast, row),
			CaseExpr caseExpr => EvalCase(caseExpr, row),
			ScalarSubqueryExpr sub => EvalScalarSubquery(sub, row),
			ExistsExpr exists => EvalExists(exists, row),
			InSubqueryExpr inSub => EvalInSubquery(inSub, row),
			InUnnestExpr inUnnest => EvalInUnnest(inUnnest, row),
			ArraySubqueryExpr arraySub => EvalArraySubquery(arraySub, row),
			ArrayLiteralExpr arrayLit => arrayLit.Elements.Select(e => Evaluate(e, row)).ToList(),
			ArrayAccessExpr access => EvalArrayAccess(access, row),
			WindowExpr win => EvalWindowExpr(win, row),
			StructExpr structExpr => EvalStructExpr(structExpr, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#struct_field_access_operator
			//   Field access on NULL struct returns NULL.
			StructFieldAccessExpr sfa => Evaluate(sfa.Struct, row) is Dictionary<string, object?> structDict
				? structDict[sfa.FieldName]
				: null,
			StructExpandExpr => throw new InvalidOperationException("StructExpandExpr must be expanded in ProjectColumns, not evaluated as a scalar."),
			NamedArgExpr named => Evaluate(named.Value, row),
			StarExpr => throw new InvalidOperationException("Star expression cannot be evaluated as a value."),
			CountStarExpr => row.TryGetValue("COUNT(*)", out var countVal) ? countVal
				: throw new InvalidOperationException("COUNT(*) should be evaluated in aggregate context."),
			_ => throw new NotSupportedException($"Expression type not supported: {expr.GetType().Name}")
		};
	}

	/// <summary>
	/// Evaluates a SQL expression as a boolean predicate (WHERE / HAVING).
	/// </summary>
	public bool EvaluateAsBool(SqlExpression expr, Dictionary<string, object?> row)
	{
		var result = Evaluate(expr, row);
		return result is true;
	}

	private object? EvalColumnRef(ColumnRefExpr col, Dictionary<string, object?> row)
	{
		// When a table alias is specified (e.g., s.SingerId), prefer the qualified
		// name first so JOINs with overlapping column names resolve correctly.
		if (col.TableAlias != null)
		{
			var qualifiedName = $"{col.TableAlias}.{col.Column}";
			if (row.TryGetValue(qualifiedName, out var qualifiedValue))
				return qualifiedValue;
			// Check outer row for correlated subqueries
			if (_outerRow != null && _outerRow.TryGetValue(qualifiedName, out qualifiedValue))
				return qualifiedValue;
		}

		// Try unqualified name
		if (row.TryGetValue(col.Column, out var value))
			return value;

		// Case-insensitive fallback
		var key = row.Keys.FirstOrDefault(k => string.Equals(k, col.Column, StringComparison.OrdinalIgnoreCase));
		if (key != null)
			return row[key];

		// Fallback to outer row for correlated subqueries
		if (_outerRow != null)
		{
			if (_outerRow.TryGetValue(col.Column, out value))
				return value;
			key = _outerRow.Keys.FirstOrDefault(k => string.Equals(k, col.Column, StringComparison.OrdinalIgnoreCase));
			if (key != null)
				return _outerRow[key];
		}

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#current_timestamp
		//   "CURRENT_TIMESTAMP() ... Parentheses are optional."
		if (string.Equals(col.Column, "CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
			return DateTime.UtcNow;

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#current_date
		//   "CURRENT_DATE ... Parentheses are optional."
		if (string.Equals(col.Column, "CURRENT_DATE", StringComparison.OrdinalIgnoreCase))
			return DateTime.UtcNow.Date;

		throw new InvalidOperationException($"Column '{col.Column}' not found.");
	}

	private object? EvalParameter(ParameterExpr param)
	{
		if (_parameters == null)
			throw new InvalidOperationException($"Parameter '@{param.Name}' referenced but no parameters provided.");

		// Try with and without @ prefix
		if (_parameters.TryGetValue(param.Name, out var value))
			return value;
		if (_parameters.TryGetValue("@" + param.Name, out value))
			return value;

		// Case-insensitive
		var key = _parameters.Keys.FirstOrDefault(k =>
			string.Equals(k, param.Name, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, "@" + param.Name, StringComparison.OrdinalIgnoreCase));
		if (key != null)
			return _parameters[key];

		throw new InvalidOperationException($"Parameter '@{param.Name}' not found.");
	}

	private object? EvalBinary(BinaryExpr bin, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
		//   GCP Spanner rejects literal NULL as an operand of comparison, arithmetic,
		//   and concatenation operators at parse time. AND/OR use three-valued logic
		//   and are exempt; IS NULL is a separate expression type.
		if (bin.Op is not (BinaryOp.And or BinaryOp.Or))
		{
			var opSymbol = bin.Op switch
			{
				BinaryOp.Equal => "=",
				BinaryOp.NotEqual => "!=",
				BinaryOp.LessThan => "<",
				BinaryOp.GreaterThan => ">",
				BinaryOp.LessThanOrEqual => "<=",
				BinaryOp.GreaterThanOrEqual => ">=",
				BinaryOp.Add => "+",
				BinaryOp.Subtract => "-",
				BinaryOp.Multiply => "*",
				BinaryOp.Divide => "/",
				BinaryOp.Modulo => "%",
				BinaryOp.Concat => "||",
				_ => null
			};
			if (opSymbol != null)
			{
				if (bin.Left is LiteralExpr { Value: null } || bin.Right is LiteralExpr { Value: null })
					throw new InvalidOperationException($"Operands of {opSymbol} cannot be literal NULL");
			}
		}

		// Three-valued logic for AND/OR
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
		if (bin.Op == BinaryOp.And)
		{
			var left = Evaluate(bin.Left, row);
			if (left is false) return false;
			var right = Evaluate(bin.Right, row);
			if (right is false) return false;
			if (left is null || right is null) return null;
			return true;
		}
		if (bin.Op == BinaryOp.Or)
		{
			var left = Evaluate(bin.Left, row);
			if (left is true) return true;
			var right = Evaluate(bin.Right, row);
			if (right is true) return true;
			if (left is null || right is null) return null;
			return false;
		}

		var lval = Evaluate(bin.Left, row);
		var rval = Evaluate(bin.Right, row);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators
		//   "NULL comparison: any comparison with NULL returns NULL."
		if (bin.Op is BinaryOp.Equal or BinaryOp.NotEqual or BinaryOp.LessThan
			or BinaryOp.GreaterThan or BinaryOp.LessThanOrEqual or BinaryOp.GreaterThanOrEqual)
		{
			if (lval is null || rval is null)
				return null;

			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
			//   "All comparisons with NaN return FALSE, except for != and <>, which return TRUE."
			if (IsNaN(lval) || IsNaN(rval))
				return bin.Op == BinaryOp.NotEqual;
		}

		return bin.Op switch
		{
			BinaryOp.Equal => CompareValues(lval, rval) == 0,
			BinaryOp.NotEqual => CompareValues(lval, rval) != 0,
			BinaryOp.LessThan => CompareValues(lval, rval) < 0,
			BinaryOp.GreaterThan => CompareValues(lval, rval) > 0,
			BinaryOp.LessThanOrEqual => CompareValues(lval, rval) <= 0,
			BinaryOp.GreaterThanOrEqual => CompareValues(lval, rval) >= 0,
			BinaryOp.Add => ArithmeticOp(lval, rval, '+'),
			BinaryOp.Subtract => ArithmeticOp(lval, rval, '-'),
			BinaryOp.Multiply => ArithmeticOp(lval, rval, '*'),
			BinaryOp.Divide => ArithmeticOp(lval, rval, '/'),
			BinaryOp.Modulo => ArithmeticOp(lval, rval, '%'),
			BinaryOp.Concat => ConcatValues(lval, rval),
			_ => throw new NotSupportedException($"Binary operator not supported: {bin.Op}")
		};
	}

	private object? EvalUnary(UnaryExpr unary, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#logical_operators
		//   NOT NULL evaluates to NULL per SQL three-valued logic.
		var operand = Evaluate(unary.Operand, row);
		return unary.Op switch
		{
			UnaryOp.Not => operand is null ? null : !(bool)operand,
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
			//   Negating long.MinValue overflows — no positive INT64 representation.
			UnaryOp.Negate => operand switch
			{
				null => null,
				long l when l == long.MinValue => throw new InvalidOperationException("INT64 overflow during unary negation."),
				long l => -l,
				double d when d == -(double)long.MinValue => (object)long.MinValue,
				double d => -d,
				float f => -f,
				decimal dec => -dec,
				_ => throw new InvalidOperationException($"Cannot negate {operand.GetType().Name}")
			},
			_ => throw new NotSupportedException($"Unary operator not supported: {unary.Op}")
		};
	}

	private object? EvalIsNull(IsNullExpr isNull, Dictionary<string, object?> row)
	{
		var value = Evaluate(isNull.Value, row);
		return isNull.IsNegated ? value is not null : value is null;
	}

	private object? EvalIn(InExpr inExpr, Dictionary<string, object?> row)
	{
		var value = Evaluate(inExpr.Value, row);
		if (value is null) return null;

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
		//   x IN (a, b, NULL) is equivalent to (x=a) OR (x=b) OR (x=NULL).
		//   If x doesn't match any non-null value but NULL is present, result is NULL.
		bool found = false;
		bool hasNull = false;
		foreach (var item in inExpr.List)
		{
			var itemValue = Evaluate(item, row);
			if (itemValue is null) { hasNull = true; continue; }
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
			//   "All comparisons with NaN return FALSE" — NaN never equals anything
			if (!IsNaN(value) && !IsNaN(itemValue) && CompareValues(value, itemValue) == 0) { found = true; break; }
		}

		if (found) return !inExpr.IsNegated;
		if (hasNull) return null;
		return inExpr.IsNegated;
	}

	private object? EvalBetween(BetweenExpr between, Dictionary<string, object?> row)
	{
		var value = Evaluate(between.Value, row);
		var low = Evaluate(between.Low, row);
		var high = Evaluate(between.High, row);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
		//   "X BETWEEN Y AND Z is equivalent to Y <= X AND X <= Z"
		//   Three-valued AND: FALSE AND NULL = FALSE, NULL AND FALSE = FALSE
		//   "All comparisons with NaN return FALSE"
		bool? lowCmp = (value is null || low is null) ? null
			: (IsNaN(value) || IsNaN(low)) ? false
			: CompareValues(value, low) >= 0;
		bool? highCmp = (value is null || high is null) ? null
			: (IsNaN(value) || IsNaN(high)) ? false
			: CompareValues(value, high) <= 0;

		bool? inRange;
		if (lowCmp == false || highCmp == false) inRange = false;
		else if (lowCmp == true && highCmp == true) inRange = true;
		else inRange = null;

		if (inRange == null) return null;
		return between.IsNegated ? !inRange : inRange;
	}

	private object? EvalFunction(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators
		var funcName = func.Name.ToUpperInvariant();

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#safe_prefix
		//   "SAFE. prefix: If the function would error, returns NULL instead."
		if (funcName.StartsWith("SAFE."))
		{
			var innerName = funcName.Substring(5);
			var innerFunc = func with { Name = innerName };
			try
			{
				return EvalFunction(innerFunc, row);
			}
			catch
			{
				return null;
			}
		}

		// Aggregate functions may have been pre-computed and stored in the row
		// (e.g., during GROUP BY processing or HAVING evaluation).
		if (funcName is "SUM" or "AVG" or "COUNT" or "MIN" or "MAX"
			or "ARRAY_AGG" or "STRING_AGG" or "COUNTIF" or "ANY_VALUE"
			or "LOGICAL_AND" or "LOGICAL_OR"
			or "BIT_AND" or "BIT_OR" or "BIT_XOR"
			or "STDDEV" or "STDDEV_SAMP" or "VAR_SAMP" or "VARIANCE"
			or "ARRAY_CONCAT_AGG")
		{
			if (row.TryGetValue(funcName, out var precomputed))
				return precomputed;
			// Also try full canonical name like "SUM(col)"
			var canonName = QueryExecutor.InferColumnNameStatic(func);
			if (canonName != funcName && row.TryGetValue(canonName, out precomputed))
				return precomputed;
		}

		return funcName switch
		{
			// Conditional
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions
			"COALESCE" => EvalCoalesce(func, row),
			"IFNULL" => EvalIfNull(func, row),
			"NULLIF" => EvalNullIf(func, row),
			"IF" => EvalIf(func, row),

			// String functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
			"LENGTH" or "CHAR_LENGTH" or "CHARACTER_LENGTH" => EvalStringFunc1(func, row, s => (long)s.Length),
			"LOWER" or "LCASE" => EvalStringFunc1(func, row, s => s.ToLowerInvariant()),
			"UPPER" or "UCASE" => EvalStringFunc1(func, row, s => s.ToUpperInvariant()),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#trim
			"TRIM" => EvalTrim(func, row, TrimMode.Both),
			"LTRIM" => EvalTrim(func, row, TrimMode.Start),
			"RTRIM" => EvalTrim(func, row, TrimMode.End),
			"CONCAT" => EvalConcat(func, row),
			"STARTS_WITH" => EvalStringFunc2Bool(func, row, (s, prefix) => s.StartsWith(prefix, StringComparison.Ordinal)),
			"ENDS_WITH" => EvalStringFunc2Bool(func, row, (s, suffix) => s.EndsWith(suffix, StringComparison.Ordinal)),
			"SUBSTR" or "SUBSTRING" => EvalSubstr(func, row),
			"REPLACE" => EvalReplace(func, row),
			"REVERSE" => EvalStringFunc1(func, row, s => new string(s.Reverse().ToArray())),
			"STRPOS" => EvalStrPos(func, row),
			"SPLIT" => EvalSplit(func, row),
			"LPAD" => EvalPad(func, row, padLeft: true),
			"RPAD" => EvalPad(func, row, padLeft: false),
			"REPEAT" => EvalRepeat(func, row),
			"FORMAT" => EvalFormat(func, row),
			"REGEXP_CONTAINS" => EvalRegexpContains(func, row),
			"REGEXP_EXTRACT" => EvalRegexpExtract(func, row),
			"REGEXP_REPLACE" => EvalRegexpReplace(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
			//   LIKE is parsed as FunctionCallExpr("LIKE", [value, pattern]).
			//   % matches any number of characters; _ matches a single character.
			"LIKE" => EvalLike(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
			//   LIKE ANY/SOME — true if value matches any pattern in the list.
			//   LIKE ALL — true if value matches all patterns in the list.
			"LIKE_ANY" => EvalLikeQuantified(func, row, any: true),
			"LIKE_ALL" => EvalLikeQuantified(func, row, any: false),
			// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
			//   LEFT and RIGHT do not exist in GCP Spanner. Use SUBSTR instead.
			"LEFT" => throw new NotSupportedException($"Unsupported built-in function: {func.Name}."),
			"RIGHT" => throw new NotSupportedException($"Unsupported built-in function: {func.Name}."),
			"BYTE_LENGTH" => EvalByteLength(func, row),
			"TO_HEX" => EvalToHex(func, row),
			"FROM_HEX" => EvalFromHex(func, row),
			// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
			//   ASCII does not exist in GCP Spanner. Use TO_CODE_POINTS instead.
			"ASCII" => throw new NotSupportedException($"Unsupported built-in function: {func.Name.ToLowerInvariant()}."),
			//   CHR does not exist in GCP Spanner. Use CODE_POINTS_TO_STRING instead.
			"CHR" => throw new NotSupportedException($"Unsupported built-in function: {func.Name.ToLowerInvariant()}."),
			"CODE_POINTS_TO_STRING" => EvalChr(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#contains_substr
			//   CONTAINS_SUBSTR(expression, search_value_literal) — case-insensitive substring search.
			"CONTAINS_SUBSTR" => EvalContainsSubstr(func, row),
			"SOUNDEX" => EvalStringFunc1(func, row, EvalSoundex),
			"UNICODE" => EvalStringFunc1(func, row, s => s.Length > 0 ? (long)char.ConvertToUtf32(s, 0) : 0L),
			// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
			//   INITCAP, TRANSLATE, INSTR do not exist in GCP Spanner.
			"INITCAP" => throw new InvalidOperationException($"Function not found: {func.Name}"),
			"TRANSLATE" => throw new InvalidOperationException($"Function not found: {func.Name}"),
			"INSTR" => throw new InvalidOperationException($"Function not found: {func.Name}"),

			// Math functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
			"ABS" => EvalMathFunc1(func, row, Math.Abs, Math.Abs),
			"MOD" => EvalMod(func, row),
			"CEIL" or "CEILING" => EvalMathFunc1Double(func, row, Math.Ceiling),
			"FLOOR" => EvalMathFunc1Double(func, row, Math.Floor),
			"ROUND" => EvalRound(func, row),
			"TRUNC" => EvalTrunc(func, row),
			"GREATEST" => EvalGreatestLeast(func, row, greatest: true),
			"LEAST" => EvalGreatestLeast(func, row, greatest: false),
			"SIGN" => EvalSign(func, row),
			"DIV" => EvalDiv(func, row),
			"IEEE_DIVIDE" => EvalIeeeDivide(func, row),
			"SAFE_DIVIDE" => EvalSafeDivide(func, row),
			"SAFE_NEGATE" => EvalSafeNegate(func, row),
			"SAFE_ADD" => EvalSafeArith(func, row, "ADD"),
			"SAFE_SUBTRACT" => EvalSafeArith(func, row, "SUB"),
			"SAFE_MULTIPLY" => EvalSafeArith(func, row, "MUL"),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sqrt
			//   "Generates an error if X is less than 0."
			"SQRT" => EvalMathFunc1Checked(func, row, Math.Sqrt,
				v => v < 0, "SQRT of negative number"),
			"POW" or "POWER" => EvalPow(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#exp
			//   "Generates an error if the result overflows."
			"EXP" => EvalExp(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ln
			//   "Generates an error if X is less than or equal to zero."
			"LN" => EvalMathFunc1Checked(func, row, Math.Log,
				v => v <= 0, "LN of non-positive number"),
			"LOG" => EvalLog(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#log10
			//   "Similar to LOG. X <= 0 → Error."
			"LOG10" => EvalMathFunc1Checked(func, row, Math.Log10,
				v => v <= 0, "LOG10 of non-positive number"),
			"IS_NAN" => EvalIsNan(func, row),
			"IS_INF" => EvalIsInf(func, row),
			// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
			//   RAND and RANGE_BUCKET do not exist in GCP Spanner.
			"RAND" => throw new NotSupportedException($"Unsupported built-in function: {func.Name.ToLowerInvariant()}."),
			"RANGE_BUCKET" => throw new NotSupportedException($"Unsupported built-in function: {func.Name.ToLowerInvariant()}."),

			// Date/Time
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
			"CURRENT_TIMESTAMP" => DateTime.UtcNow,
			"CURRENT_DATE" => DateTime.UtcNow.Date,
			"TIMESTAMP" => EvalTimestampCtor(func, row),
			"DATE" => EvalDateCtor(func, row),
			"EXTRACT" => EvalExtract(func, row),
			"TIMESTAMP_ADD" => EvalTimestampAdd(func, row),
			"TIMESTAMP_SUB" => EvalTimestampSub(func, row),
			"TIMESTAMP_DIFF" => EvalTimestampDiff(func, row),
			"TIMESTAMP_TRUNC" => EvalTimestampTrunc(func, row),
			"DATE_ADD" => EvalDateAdd(func, row),
			"DATE_SUB" => EvalDateSub(func, row),
			"DATE_DIFF" => EvalDateDiff(func, row),
			"DATE_TRUNC" => EvalDateTrunc(func, row),
			"FORMAT_TIMESTAMP" => EvalFormatTimestamp(func, row),
			"PARSE_TIMESTAMP" => EvalParseTimestamp(func, row),
			"FORMAT_DATE" => EvalFormatDate(func, row),
			"PARSE_DATE" => EvalParseDate(func, row),
			"UNIX_SECONDS" => EvalUnixSeconds(func, row),
			"UNIX_MILLIS" => EvalUnixMillis(func, row),
			"UNIX_MICROS" => EvalUnixMicros(func, row),
			"TIMESTAMP_SECONDS" => EvalTimestampFromUnix(func, row, "SECONDS"),
			"TIMESTAMP_MILLIS" => EvalTimestampFromUnix(func, row, "MILLIS"),
			"TIMESTAMP_MICROS" => EvalTimestampFromUnix(func, row, "MICROS"),
			"PENDING_COMMIT_TIMESTAMP" => DateTime.UtcNow,

			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions
			"__INTERVAL__" => EvalIntervalLiteral(func, row),
			"MAKE_INTERVAL" => EvalMakeInterval(func, row),
			"JUSTIFY_DAYS" => EvalJustifyDays(func, row),
			"JUSTIFY_HOURS" => EvalJustifyHours(func, row),
			"JUSTIFY_INTERVAL" => EvalJustifyInterval(func, row),

			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#get_next_sequence_value
			"GET_NEXT_SEQUENCE_VALUE" => EvalGetNextSequenceValue(func),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#get_internal_sequence_state
			"GET_INTERNAL_SEQUENCE_STATE" => EvalGetInternalSequenceState(func),

			// Conversion
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions
			// Note: CAST/SAFE_CAST are handled via CastExpr, not as functions
			"TO_JSON" or "TO_JSON_STRING" => EvalToJson(func, row),
			"PARSE_JSON" => EvalParseJson(func, row),

			// Array functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
			"ARRAY_LENGTH" => EvalArrayLength(func, row),
			"ARRAY_CONCAT" => EvalArrayConcat(func, row),
			"ARRAY_TO_STRING" => EvalArrayToString(func, row),
			"ARRAY_REVERSE" => EvalArrayFunc(func, row, list => { var r = new List<object?>(list); r.Reverse(); return r; }),
			"GENERATE_ARRAY" => EvalGenerateArray(func, row),
			"ARRAY_INCLUDES" => EvalArrayIncludes(func, row),

			// JSON functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
			"JSON_VALUE" => EvalJsonValue(func, row),
			"JSON_QUERY" => EvalJsonQuery(func, row),
			"JSON_QUERY_ARRAY" => EvalJsonQueryArray(func, row),
			"JSON_TYPE" => EvalJsonType(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_extract
			//   "JSON_EXTRACT is equivalent to the JSON_QUERY function"
			"JSON_EXTRACT" => EvalJsonQuery(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_extract_scalar
			//   "JSON_EXTRACT_SCALAR is equivalent to the JSON_VALUE function"
			"JSON_EXTRACT_SCALAR" => EvalJsonValue(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_extract_array
			//   "JSON_EXTRACT_ARRAY is equivalent to the JSON_QUERY_ARRAY function"
			"JSON_EXTRACT_ARRAY" => EvalJsonQueryArray(func, row),

			// Hash functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/hash_functions
			"SHA1" => EvalHash(func, row, System.Security.Cryptography.SHA1.HashData),
			"SHA256" => EvalHash(func, row, System.Security.Cryptography.SHA256.HashData),
			"SHA512" => EvalHash(func, row, System.Security.Cryptography.SHA512.HashData),
			"MD5" => EvalHash(func, row, System.Security.Cryptography.MD5.HashData),
			"FARM_FINGERPRINT" => EvalFarmFingerprint(func, row),

			// Additional array functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
			"ARRAY_FIRST" => EvalArrayFirst(func, row),
			"ARRAY_LAST" => EvalArrayLast(func, row),
			"ARRAY_MIN" => EvalArrayMinMax(func, row, isMin: true),
			"ARRAY_MAX" => EvalArrayMinMax(func, row, isMin: false),
			"ARRAY_SUM" => EvalArrayAggregate(func, row, "SUM"),
			"ARRAY_AVG" => EvalArrayAggregate(func, row, "AVG"),
			"ARRAY_SLICE" => EvalArraySlice(func, row),
			"ARRAY_INCLUDES_ANY" => EvalArrayIncludesAnyAll(func, row, any: true),
			"ARRAY_INCLUDES_ALL" => EvalArrayIncludesAnyAll(func, row, any: false),
			"ARRAY_FILTER" => EvalArrayFilter(func, row),
			"ARRAY_TRANSFORM" => EvalArrayTransform(func, row),

			// Additional date/time functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions
			"GENERATE_DATE_ARRAY" => EvalGenerateDateArray(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions
			"GENERATE_TIMESTAMP_ARRAY" => EvalGenerateTimestampArray(func, row),
			"UNIX_DATE" => EvalUnixDate(func, row),
			"DATE_FROM_UNIX_DATE" => EvalDateFromUnixDate(func, row),

			// Additional string functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
			"NORMALIZE" => EvalNormalize(func, row),
			"NORMALIZE_AND_CASEFOLD" => EvalNormalizeAndCasefold(func, row),
			"TO_CODE_POINTS" => EvalToCodePoints(func, row),
			"CODE_POINTS_TO_BYTES" => EvalCodePointsToBytes(func, row),
			"REGEXP_EXTRACT_ALL" => EvalRegexpExtractAll(func, row),
			"REGEXP_INSTR" => EvalRegexpInstr(func, row),
			"OCTET_LENGTH" => EvalOctetLength(func, row),
			"SAFE_CONVERT_BYTES_TO_STRING" => EvalSafeConvertBytesToString(func, row),

			// Additional math functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions
			"BIT_COUNT" => EvalBitCount(func, row),

			// Trigonometric functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
			"SIN" => EvalTrig(func, row, Math.Sin),
			"COS" => EvalTrig(func, row, Math.Cos),
			"TAN" => EvalTrig(func, row, Math.Tan),
			"ASIN" => EvalTrig(func, row, Math.Asin),
			"ACOS" => EvalTrig(func, row, Math.Acos),
			"ATAN" => EvalTrig(func, row, Math.Atan),
			"ATAN2" => EvalTrig2(func, row, Math.Atan2),
			"SINH" => EvalTrig(func, row, Math.Sinh),
			"COSH" => EvalTrig(func, row, Math.Cosh),
			"TANH" => EvalTrig(func, row, Math.Tanh),
			"ASINH" => EvalTrig(func, row, Math.Asinh),
			"ACOSH" => EvalTrig(func, row, Math.Acosh),
			"ATANH" => EvalTrig(func, row, Math.Atanh),

			// Statistical aggregate functions — these are pre-computed by QueryExecutor and
			// should be found in the row context via the aggregate lookup above.
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/aggregate_functions
			"STDDEV" or "STDDEV_SAMP" or "VAR_SAMP" or "VARIANCE" or "ARRAY_CONCAT_AGG" =>
				throw new InvalidOperationException($"Aggregate function '{func.Name}' used outside of aggregate context."),

			// UUID generation
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#generate_uuid
			"GENERATE_UUID" => Guid.NewGuid().ToString(),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions#new_uuid
			//   "NEW_UUID() returns a UUID value."
			"NEW_UUID" => Guid.NewGuid().ToString(),

			// Base64 encoding/decoding
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#from_base64
			"FROM_BASE64" => EvalFromBase64(func, row),
			"TO_BASE64" => EvalToBase64(func, row),
			"FROM_BASE32" => EvalFromBase32(func, row),
			"TO_BASE32" => EvalToBase32(func, row),

			// JSON conversion functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_conversion_functions
			"BOOL" => EvalJsonToBool(func, row),
			"INT64" or "INT32" => EvalJsonToInt64(func, row),
			"FLOAT64" or "FLOAT32" => EvalJsonToFloat64(func, row),
			"STRING" => EvalJsonToString(func, row),
			"JSON_ARRAY" => EvalJsonArray(func, row),
			"JSON_OBJECT" => EvalJsonObject(func, row),
			"JSON_SET" => EvalJsonSet(func, row),
			"JSON_STRIP_NULLS" => EvalJsonStripNulls(func, row),
			"JSON_KEYS" => EvalJsonKeys(func, row),

			// LAX conversion functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#lax_conversion
			"LAX_BOOL" => EvalLaxBool(func, row),
			"LAX_INT64" or "LAX_INT32" => EvalLaxInt64(func, row),
			"LAX_FLOAT64" or "LAX_FLOAT32" => EvalLaxFloat64(func, row),
			"LAX_STRING" => EvalLaxString(func, row),

			// Array distinct check
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_is_distinct
			"ARRAY_IS_DISTINCT" => EvalArrayIsDistinct(func, row),

			// Vector distance functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosine_distance
			"COSINE_DISTANCE" => EvalCosineDistance(func, row),
			"EUCLIDEAN_DISTANCE" => EvalEuclideanDistance(func, row),
			"DOT_PRODUCT" => EvalDotProduct(func, row),
			// Approximate vector distance functions — compute exact (brute-force) in the emulator.
			// The `options` named argument is accepted but ignored.
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#approx_cosine_distance
			"APPROX_COSINE_DISTANCE" => EvalCosineDistance(func, row),
			"APPROX_EUCLIDEAN_DISTANCE" => EvalEuclideanDistance(func, row),
			"APPROX_DOT_PRODUCT" => EvalDotProduct(func, row),

			// Net functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions
			"NET.HOST" => EvalNetHost(func, row),
			"NET.REG_DOMAIN" => EvalNetRegDomain(func, row),
			"NET.PUBLIC_SUFFIX" => EvalNetPublicSuffix(func, row),
			"NET.IP_FROM_STRING" => EvalNetIpFromString(func, row),
			"NET.IP_TO_STRING" => EvalNetIpToString(func, row),
			"NET.SAFE_IP_FROM_STRING" => EvalNetSafeIpFromString(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netip_net_mask
			"NET.IP_NET_MASK" => EvalNetIpNetMask(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netip_trunc
			"NET.IP_TRUNC" => EvalNetIpTrunc(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netipv4_from_int64
			"NET.IPV4_FROM_INT64" => EvalNetIpv4FromInt64(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netipv4_to_int64
			"NET.IPV4_TO_INT64" => EvalNetIpv4ToInt64(func, row),

			// Date/Time aliases
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#adddate
			"ADDDATE" => EvalDateAdd(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#subdate
			"SUBDATE" => EvalDateSub(func, row),

			// Additional string functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split_substr
			"SPLIT_SUBSTR" => EvalSplitSubstr(func, row),

			// Additional math functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions#bit_reverse
			"BIT_REVERSE" => EvalBitReverse(func, row),

			// Additional JSON functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value_array
			"JSON_VALUE_ARRAY" => EvalJsonValueArray(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_append
			"JSON_ARRAY_APPEND" => EvalJsonArrayAppend(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_insert
			"JSON_ARRAY_INSERT" => EvalJsonArrayInsert(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_contains
			"JSON_CONTAINS" => EvalJsonContains(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_remove
			"JSON_REMOVE" => EvalJsonRemove(func, row),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#safe_to_json
			"SAFE_TO_JSON" => EvalSafeToJson(func, row),

			// JSON array conversion functions
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#float64_array
			"FLOAT64_ARRAY" => EvalJsonToTypedArray(func, row, "FLOAT64"),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#float32_array
			"FLOAT32_ARRAY" => EvalJsonToTypedArray(func, row, "FLOAT32"),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#int64_array
			"INT64_ARRAY" => EvalJsonToTypedArray(func, row, "INT64"),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#bool_array
			"BOOL_ARRAY" => EvalJsonToTypedArray(func, row, "BOOL"),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#string_array
			"STRING_ARRAY" => EvalJsonToTypedArray(func, row, "STRING"),

			// Conditional: ERROR
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/debugging_functions
			"ERROR" => throw new InvalidOperationException(
				func.Arguments.Count > 0 ? Evaluate(func.Arguments[0], row)?.ToString() ?? "ERROR" : "ERROR"),

			// ── Full-Text Search: Tokenization Functions ──
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions
			"TOKEN" => EvalToken(func, row),
			"TOKENIZE_FULLTEXT" => EvalTokenizeFulltext(func, row),
			"TOKENIZE_SUBSTRING" => EvalTokenizeSubstring(func, row),
			"TOKENIZE_NGRAMS" => EvalTokenizeNgrams(func, row),
			"TOKENIZE_NUMBER" => EvalTokenizeNumber(func, row),
			"TOKENIZE_BOOL" => EvalTokenizeBool(func, row),
			"TOKENIZE_JSON" => EvalTokenizeJson(func, row),
			"TOKENLIST_CONCAT" => EvalTokenlistConcat(func, row),

			// ── Full-Text Search: Retrieval Functions ──
			"SEARCH" => EvalSearch(func, row),
			"SEARCH_SUBSTRING" => EvalSearchSubstring(func, row),
			"SEARCH_NGRAMS" => EvalSearchNgrams(func, row),
			"SCORE" => EvalScore(func, row),
			"SCORE_NGRAMS" => EvalScoreNgrams(func, row),
			"SNIPPET" => EvalSnippet(func, row),

			// ── Full-Text Search: Debugging ──
			"DEBUG_TOKENLIST" => EvalDebugTokenlist(func, row),

			// ── Compression Functions ──
			// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
			//   ZSTD_COMPRESS — compresses STRING or BYTES input into BYTES output using Zstandard.
			"ZSTD_COMPRESS" => EvalZstdCompress(func, row),
			//   ZSTD_DECOMPRESS_TO_BYTES — decompresses BYTES input into BYTES output using Zstandard.
			"ZSTD_DECOMPRESS_TO_BYTES" => EvalZstdDecompressToBytes(func, row),
			//   ZSTD_DECOMPRESS_TO_STRING — decompresses BYTES input into STRING output using Zstandard.
			"ZSTD_DECOMPRESS_TO_STRING" => EvalZstdDecompressToString(func, row),

			_ => throw new NotSupportedException($"Function '{func.Name}' is not supported.")
		};
	}

	private object? EvalNullIf(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
		//   "All comparisons with NaN return FALSE" — NaN = NaN is FALSE so NULLIF(NaN, NaN) returns NaN
		if (a != null && b != null && !IsNaN(a) && !IsNaN(b) && CompareValues(a, b) == 0) return null;
		return a;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#coalesce
	//   "Returns the value of the first non-NULL expression, if any, otherwise NULL.
	//    The remaining expressions aren't evaluated."
	private object? EvalCoalesce(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		object? result = null;
		foreach (var arg in func.Arguments)
		{
			result = Evaluate(arg, row);
			if (result != null) return result;
		}
		return null;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#ifnull
	//   "If expr doesn't evaluate to NULL, null_result isn't evaluated."
	private object? EvalIfNull(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		if (a != null) return a;
		return Evaluate(func.Arguments[1], row);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conditional_expressions#if
	//   "else_result isn't evaluated if expr evaluates to TRUE.
	//    true_result isn't evaluated if expr evaluates to FALSE or NULL."
	private object? EvalIf(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var condition = Evaluate(func.Arguments[0], row);
		return condition is true
			? Evaluate(func.Arguments[1], row)
			: Evaluate(func.Arguments[2], row);
	}

	private object? EvalStringFunc1(FunctionCallExpr func, Dictionary<string, object?> row, Func<string, object> fn)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		return fn((string)val);
	}

	private enum TrimMode { Both, Start, End }

	private object? EvalTrim(FunctionCallExpr func, Dictionary<string, object?> row, TrimMode mode)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		var str = (string)val;
		if (func.Arguments.Count > 1)
		{
			// 2-arg form: trim characters from the second argument
			var charsVal = Evaluate(func.Arguments[1], row);
			if (charsVal is null) return null;
			var chars = Convert.ToString(charsVal)!.ToCharArray();
			return mode switch
			{
				TrimMode.Both => str.Trim(chars),
				TrimMode.Start => str.TrimStart(chars),
				TrimMode.End => str.TrimEnd(chars),
				_ => str
			};
		}
		return mode switch
		{
			TrimMode.Both => str.Trim(),
			TrimMode.Start => str.TrimStart(),
			TrimMode.End => str.TrimEnd(),
			_ => str
		};
	}

	private object? EvalStringFunc2Bool(FunctionCallExpr func, Dictionary<string, object?> row, Func<string, string, bool> fn)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a is null || b is null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#starts_with
		//   "Takes two STRING or BYTES values."
		if (a is byte[] ba && b is byte[] bb)
		{
			var funcName = func.Name.ToUpperInvariant();
			if (funcName == "STARTS_WITH")
				return ba.Length >= bb.Length && ba.AsSpan(0, bb.Length).SequenceEqual(bb);
			if (funcName == "ENDS_WITH")
				return ba.Length >= bb.Length && ba.AsSpan(ba.Length - bb.Length).SequenceEqual(bb);
			return fn(Convert.ToBase64String(ba), Convert.ToBase64String(bb));
		}
		return fn((string)a, (string)b);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#contains_substr
	//   CONTAINS_SUBSTR performs a normalized, case-insensitive substring search.
	private object? EvalContainsSubstr(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a is null || b is null) return null;
		var haystack = a.ToString()!;
		var needle = (string)b;
		return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
	}

	private object? EvalConcat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var parts = func.Arguments.Select(a => Evaluate(a, row)).ToList();
		if (parts.Any(p => p is null)) return null;
		return string.Concat(parts.Select(p => p!.ToString()));
	}

	private object? EvalSubstr(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var pos = Evaluate(func.Arguments[1], row);
		if (s is null || pos is null) return null;

		var str = (string)s;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
		//   Position is 1-based. If position is negative, counts from the end
		//   with -1 indicating the last character.
		//   If position is 0 or less than -LENGTH(value), it is set to 1.
		var position = Convert.ToInt32(pos);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#substr
		//   "If position is 0 or less than -LENGTH(value), position is set to 1"
		if (position == 0 || position < -str.Length)
			position = 1;

		int startIndex;

		if (func.Arguments.Count > 2)
		{
			var len = Evaluate(func.Arguments[2], row);
			if (len is null) return null;
			var length = Convert.ToInt32(len);
			if (length < 0) throw new InvalidOperationException("SUBSTR length must be non-negative.");

			// Negative position: count from end of string
			startIndex = position < 0 ? str.Length + position : position - 1;
			// Adjust length if startIndex is before string start
			if (startIndex < 0)
			{
				length += startIndex;
				startIndex = 0;
			}
			if (length <= 0 || startIndex >= str.Length) return "";
			return str.Substring(startIndex, Math.Min(length, str.Length - startIndex));
		}

		// No length argument — return from position to end
		startIndex = position < 0 ? str.Length + position : position - 1;
		if (startIndex < 0) startIndex = 0;
		if (startIndex >= str.Length) return "";
		return str[startIndex..];
	}

	private object? EvalReplace(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var from = Evaluate(func.Arguments[1], row);
		var to = Evaluate(func.Arguments[2], row);
		if (s is null || from is null || to is null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#replace
		//   REPLACE with empty from_str returns the original string.
		var fromStr = (string)from;
		if (fromStr.Length == 0) return s;
		return ((string)s).Replace(fromStr, (string)to);
	}

	private object? EvalStrPos(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var sub = Evaluate(func.Arguments[1], row);
		if (s is null || sub is null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#strpos
		//   "Returns the 1-based position of the first occurrence, or 0 if not found."
		var idx = ((string)s).IndexOf((string)sub, StringComparison.Ordinal);
		return (long)(idx + 1);
	}

	private object? EvalMathFunc1(FunctionCallExpr func, Dictionary<string, object?> row,
		Func<long, long> longFn, Func<double, double> doubleFn, Func<decimal, decimal>? decimalFn = null)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		return val switch
		{
			long l => longFn(l),
			double d => doubleFn(d),
			float f => (float)doubleFn(f),
			decimal dec => (decimalFn ?? Math.Abs)(dec),
			_ => throw new InvalidOperationException($"Cannot apply math function to {val.GetType().Name}")
		};
	}

	private object? EvalMathFunc1Double(FunctionCallExpr func, Dictionary<string, object?> row, Func<double, double> fn)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		return fn(Convert.ToDouble(val));
	}

	private object? EvalMathFunc1Checked(FunctionCallExpr func, Dictionary<string, object?> row,
		Func<double, double> fn, Func<double, bool> errorCheck, string errorMsg)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		var d = Convert.ToDouble(val);
		if (errorCheck(d))
			throw new InvalidOperationException(errorMsg);
		return fn(d);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#sign
	//   "Returns -1, 0, or +1 for negative, zero and positive arguments respectively."
	//   "| NaN | NaN |"
	private object? EvalSign(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		return val switch
		{
			long l => (long)Math.Sign(l),
			double d => double.IsNaN(d) ? d : (double)Math.Sign(d),
			float f => double.IsNaN(f) ? (double)f : (double)Math.Sign(f),
			decimal dec => (decimal)Math.Sign(dec),
			_ => throw new InvalidOperationException($"Cannot apply SIGN to {val.GetType().Name}")
		};
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#exp
	//   "Generates an error if the result overflows."
	//   "| -inf | 0.0 |"
	private object? EvalExp(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		var d = Convert.ToDouble(val);
		var result = Math.Exp(d);
		if (double.IsInfinity(result) && !double.IsInfinity(d))
			throw new InvalidOperationException("EXP overflow: result is not representable as FLOAT64.");
		return result;
	}

	private object? EvalMod(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a is null || b is null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#mod
		//   "An error is generated if Y is 0."
		return a switch
		{
			long la when b is long lb => lb == 0
				? throw new InvalidOperationException("Division by zero in MOD.")
				: la % lb,
			double da when b is double db => db == 0.0
				? throw new InvalidOperationException("Division by zero in MOD.")
				: da % db,
			_ => Convert.ToDouble(b) == 0.0
				? throw new InvalidOperationException("Division by zero in MOD.")
				: Convert.ToDouble(a) % Convert.ToDouble(b)
		};
	}

	private object? EvalRound(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#round
		//   "Rounds halfway cases away from zero. If N is negative, rounds off digits to the left of the decimal point."
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;

		int digits = 0;
		if (func.Arguments.Count > 1)
		{
			var d = Evaluate(func.Arguments[1], row);
			if (d == null) return null;
			digits = Convert.ToInt32(d);
		}

		var dv = Convert.ToDouble(val);
		if (digits < 0)
		{
			var factor = Math.Pow(10, -digits);
			return Math.Round(dv / factor, MidpointRounding.AwayFromZero) * factor;
		}

		return val switch
		{
			double d2 => Math.Round(d2, digits, MidpointRounding.AwayFromZero),
			float fv => (double)Math.Round((double)fv, digits, MidpointRounding.AwayFromZero),
			decimal dec => (double)Math.Round(dec, digits, MidpointRounding.AwayFromZero),
			long l => (double)l,
			_ => throw new InvalidOperationException($"Cannot round {val.GetType().Name}")
		};
	}

	private object? EvalGreatestLeast(FunctionCallExpr func, Dictionary<string, object?> row, bool greatest)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#greatest
		//   "Returns NULL if any of the inputs is NULL."
		//   "in the case of floating-point arguments, if any argument is NaN, returns NaN"
		var values = func.Arguments.Select(a => Evaluate(a, row)).ToList();
		if (values.Any(v => v is null)) return null;
		if (values.Count == 0) return null;
		if (values.Any(v => v is double d && double.IsNaN(d))) return double.NaN;

		return values.Aggregate((a, b) =>
		{
			var cmp = CompareValues(a, b);
			return greatest ? (cmp >= 0 ? a : b) : (cmp <= 0 ? a : b);
		});
	}

	private object? EvalCast(CastExpr cast, Dictionary<string, object?> row)
	{
		var value = Evaluate(cast.Value, row);
		if (value is null) return null;

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
		// SAFE_CAST returns NULL on conversion failure instead of throwing.
		try
		{
			return cast.TargetType switch
			{
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
				//   CAST(float AS INT64) rounds halfway cases away from zero.
				//   CAST('0x1A' AS INT64) parses hexadecimal string to INT64.
				//   CAST(NaN/Inf AS INT64) should throw an error.
				TypeCode.Int64 => value is double d
					? (double.IsNaN(d) || double.IsInfinity(d) || d > (double)long.MaxValue || d < (double)long.MinValue
						? throw new InvalidOperationException($"Could not cast '{d}' to type INT64")
						: (long)Math.Round(d, MidpointRounding.AwayFromZero))
					: value is float f
					? (float.IsNaN(f) || float.IsInfinity(f) || f > (float)long.MaxValue || f < (float)long.MinValue
						? throw new InvalidOperationException($"Could not cast '{f}' to type INT64")
						: (long)Math.Round(f, MidpointRounding.AwayFromZero))
					: value is string s && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
						? Convert.ToInt64(s[2..], 16)
						: Convert.ToInt64(value),
				TypeCode.Float64 => ConvertToFloat64(value),
				TypeCode.Float32 => ConvertToFloat32(value),
				TypeCode.Bool => Convert.ToBoolean(value),
				TypeCode.String => value switch
				{
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
					//   CAST(bool AS STRING) returns lowercase "true" / "false"
					//   CAST(timestamp AS STRING) preserves sub-second precision
					bool bv => bv ? "true" : "false",
					DateTime dt => dt.Date == dt ? dt.ToString("yyyy-MM-dd") : FormatTimestampCanonical(dt),
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
					//   CAST(FLOAT64 AS STRING): inf/-inf/nan are returned as "inf"/"-inf"/"nan"
					double dv when double.IsPositiveInfinity(dv) => "inf",
					double dv when double.IsNegativeInfinity(dv) => "-inf",
					double dv when double.IsNaN(dv) => "nan",
					float fv when float.IsPositiveInfinity(fv) => "inf",
					float fv when float.IsNegativeInfinity(fv) => "-inf",
					float fv when float.IsNaN(fv) => "nan",
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
					//   CAST(BYTES AS STRING) interprets bytes as UTF-8.
					byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
					_ => value.ToString()
				},
				TypeCode.Numeric => Convert.ToDecimal(value),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
				TypeCode.Date => value is DateTime dt2 ? dt2.Date
					: DateTime.Parse(Convert.ToString(value)!, System.Globalization.CultureInfo.InvariantCulture).Date,
				TypeCode.Timestamp => value is DateTime dt3 ? dt3
					: DateTime.Parse(Convert.ToString(value)!, System.Globalization.CultureInfo.InvariantCulture,
						System.Globalization.DateTimeStyles.AdjustToUniversal),
				TypeCode.Bytes => value is byte[] b ? b : System.Text.Encoding.UTF8.GetBytes(Convert.ToString(value)!),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
				//   Casting between arrays with incompatible element types is not supported in GCP Spanner.
				//   Since CastExpr discards the element type, reject cross-type array casts by checking
				//   if the source is already an array (the only valid CAST is identity).
				TypeCode.Array => value is IList<object?> arr
					? (arr.FirstOrDefault(x => x != null) is string or byte[] or null
						? value
						: throw new InvalidOperationException(
							"Casting between arrays with incompatible element types is not supported"))
					: new List<object?> { value },
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/uuid_functions
				//   "CAST(string_expr AS UUID) converts a valid UUID string to UUID type."
				(TypeCode)17 => value is string s ? s : throw new InvalidOperationException($"Cannot cast {value.GetType().Name} to UUID"),
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
				//   CAST(expr AS JSON): STRING values are parsed as JSON; other types are wrapped as JSON scalars.
				TypeCode.Json => value switch
				{
					string sv => sv, // String is already treated as JSON text
					bool bv => bv ? "true" : "false",
					double dv => dv.ToString(System.Globalization.CultureInfo.InvariantCulture),
					float fv => fv.ToString(System.Globalization.CultureInfo.InvariantCulture),
					long lv => lv.ToString(),
					decimal dv => dv.ToString(System.Globalization.CultureInfo.InvariantCulture),
					_ => value.ToString()
				},
				_ => throw new NotSupportedException($"CAST to {cast.TargetType} not supported.")
			};
		}
		catch when (cast.Safe)
		{
			return null;
		}
	}

	private object? EvalCase(CaseExpr caseExpr, Dictionary<string, object?> row)
	{
		if (caseExpr.Operand != null)
		{
			// Simple CASE: CASE expr WHEN val THEN result ...
			var operand = Evaluate(caseExpr.Operand, row);
			foreach (var when in caseExpr.Whens)
			{
				var whenVal = Evaluate(when.Condition, row);
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
				//   "All comparisons with NaN return FALSE"
				if (operand != null && whenVal != null && !IsNaN(operand) && !IsNaN(whenVal) && CompareValues(operand, whenVal) == 0)
				{
					return Evaluate(when.Result, row);
				}
			}
		}
		else
		{
			// Searched CASE: CASE WHEN condition THEN result ...
			foreach (var when in caseExpr.Whens)
			{
				if (EvaluateAsBool(when.Condition, row))
				{
					return Evaluate(when.Result, row);
				}
			}
		}

		return caseExpr.Else != null ? Evaluate(caseExpr.Else, row) : null;
	}

	private Dictionary<string, object?> EvalStructExpr(StructExpr structExpr, Dictionary<string, object?> row)
	{
		var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < structExpr.Fields.Count; i++)
		{
			var field = structExpr.Fields[i];
			var key = field.Name ?? $"${i}";
			dict[key] = Evaluate(field.Value, row);
		}
		return dict;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_subscript_operator
	private object? EvalArrayAccess(ArrayAccessExpr access, Dictionary<string, object?> row)
	{
		var arrayVal = Evaluate(access.Array, row);
		if (arrayVal is not IList<object?> list)
			return null;

		var indexVal = Evaluate(access.Index, row);
		if (indexVal == null)
			return null;

		var idx = Convert.ToInt32(indexVal);

		// ORDINAL is 1-based, OFFSET is 0-based
		if (access.Mode is ArrayAccessMode.Ordinal or ArrayAccessMode.SafeOrdinal)
			idx -= 1;

		if (idx < 0 || idx >= list.Count)
		{
			if (access.Mode is ArrayAccessMode.SafeOffset or ArrayAccessMode.SafeOrdinal)
				return null;
			throw new InvalidOperationException($"Array index out of bounds: {idx}");
		}

		return list[idx];
	}

	// Window expressions are pre-computed by QueryExecutor, look up by the generated column name
	private object? EvalWindowExpr(WindowExpr win, Dictionary<string, object?> row)
	{
		// The QueryExecutor stores window results keyed by alias or InferColumnName(win)
		var key = win.Function switch
		{
			FunctionCallExpr f => f.Name,
			CountStarExpr => "COUNT(*)",
			_ => ""
		};

		// Try exact key first (handles SELECT ... AS rk -> stored as "rk" or "RANK")
		if (row.TryGetValue(key, out var val))
			return val;

		// Try the full canonical name via InferColumnNameStatic
		var canonicalKey = QueryExecutor.InferColumnNameStatic(win);
		if (row.TryGetValue(canonicalKey, out val))
			return val;

		throw new InvalidOperationException($"Window function result '{key}' not found. Window functions must be pre-computed by QueryExecutor.");
	}

	// ── Subquery evaluation ──

	private List<Dictionary<string, object?>> RunSubquery(QueryBody subquery,
		Dictionary<string, object?>? outerRow = null)
	{
		if (_queryExecutor == null)
			throw new InvalidOperationException("Subqueries require a QueryExecutor context.");
		return _queryExecutor.ExecuteSubquery(subquery, _parameters, _cteMap, outerRow);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#scalar_subquery_concepts
	//   "A scalar subquery produces at most one row. If the subquery returns zero rows, the result is NULL."
	private object? EvalScalarSubquery(ScalarSubqueryExpr sub, Dictionary<string, object?> row)
	{
		var rows = RunSubquery(sub.Subquery, row);
		if (rows.Count == 0) return null;
		if (rows.Count > 1)
			throw new InvalidOperationException("Scalar subquery returned more than one row.");
		return rows[0].Values.FirstOrDefault();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#exists_subquery_concepts
	private object? EvalExists(ExistsExpr exists, Dictionary<string, object?> row)
	{
		var rows = RunSubquery(exists.Subquery, row);
		var result = rows.Count > 0;
		return exists.IsNegated ? !result : result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#in_subquery_concepts
	private object? EvalInSubquery(InSubqueryExpr inSub, Dictionary<string, object?> row)
	{
		var value = Evaluate(inSub.Value, row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
		//   "x IN (a, b, NULL) is equivalent to (x=a) OR (x=b) OR (x=NULL)."
		//   NULL IN (...) → NULL (three-valued logic).
		if (value == null) return null;

		var subRows = RunSubquery(inSub.Subquery, row);
		bool hasNull = false;
		bool found = false;
		foreach (var r in subRows)
		{
			var subVal = r.Values.FirstOrDefault();
			if (subVal == null)
			{
				hasNull = true;
				continue;
			}
			if (!IsNaN(value) && !IsNaN(subVal) && CompareValues(value, subVal) == 0)
			{
				found = true;
				break;
			}
		}
		if (found) return !inSub.IsNegated;
		if (hasNull) return null; // No match but NULLs present → NULL (three-valued logic)
		return inSub.IsNegated;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	//   "value [NOT] IN UNNEST(array_expression)"
	private object? EvalInUnnest(InUnnestExpr inUnnest, Dictionary<string, object?> row)
	{
		var value = Evaluate(inUnnest.Value, row);
		var array = Evaluate(inUnnest.ArrayExpr, row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
		//   NULL array → NULL, NULL value → NULL (three-valued logic).
		if (array == null || value == null) return null;

		bool found = false;
		bool hasNull = false;
		if (array is System.Collections.IEnumerable enumerable)
		{
			foreach (var item in enumerable)
			{
				if (item == null) { hasNull = true; continue; }
				if (!IsNaN(value) && !IsNaN(item) && CompareValues(value, item) == 0)
				{
					found = true;
					break;
				}
			}
		}
		if (found) return !inUnnest.IsNegated;
		if (hasNull) return null; // Three-valued: unknown whether value matches a NULL element
		return inUnnest.IsNegated;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#array_subquery_concepts
	private object? EvalArraySubquery(ArraySubqueryExpr arraySub, Dictionary<string, object?> row)
	{
		var rows = RunSubquery(arraySub.Subquery, row);
		return rows.Select(r => r.Values.FirstOrDefault()).ToList();
	}

	// ── Comparison helper ──

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
	//   WEEK: range [0,53]. Weeks begin with Sunday; dates before the first Sunday are week 0.
	private static long ComputeSpannerWeek(DateTime dt)
	{
		// Find first Sunday of the year
		var jan1 = new DateTime(dt.Year, 1, 1);
		var daysUntilFirstSunday = ((int)DayOfWeek.Sunday - (int)jan1.DayOfWeek + 7) % 7;
		var firstSunday = jan1.AddDays(daysUntilFirstSunday);
		if (dt < firstSunday) return 0L;
		return (long)((dt - firstSunday).Days / 7 + 1);
	}

	internal static int CompareValues(object? a, object? b)
	{
		if (a is null && b is null) return 0;
		if (a is null) return -1;
		if (b is null) return 1;

		// Normalize: if both are numeric, compare as numeric
		if (IsNumeric(a) && IsNumeric(b))
		{
			return ToDouble(a).CompareTo(ToDouble(b));
		}

		if (a is bool ba && b is bool bb)
			return ba.CompareTo(bb);

		if (a is string sa && b is string sb)
			return string.Compare(sa, sb, StringComparison.Ordinal);

		if (a is DateTime da && b is DateTime db)
			return da.CompareTo(db);

		if (a is DateTimeOffset doa && b is DateTimeOffset dob)
			return doa.CompareTo(dob);

		if (a is byte[] bytesA && b is byte[] bytesB)
			return CompareBytes(bytesA, bytesB);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
		//   "STRUCT comparisons are performed field by field in the ordinal order of the fields."
		if (a is Dictionary<string, object?> structA && b is Dictionary<string, object?> structB)
			return CompareStructs(structA, structB);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#comparison_operators
		//   "ARRAY comparisons are performed element by element."
		if (a is List<object?> arrA && b is List<object?> arrB)
			return CompareArrays(arrA, arrB);

		// Fallback: convert to string
		return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
	}

	private static bool IsNumeric(object? v) =>
		v is long or double or float or decimal or int or short or byte;

	private static bool IsNaN(object? v) =>
		(v is double d && double.IsNaN(d)) || (v is float f && float.IsNaN(f));

	private static double ToDouble(object v) => Convert.ToDouble(v);

	private static int CompareBytes(byte[] a, byte[] b)
	{
		var len = Math.Min(a.Length, b.Length);
		for (int i = 0; i < len; i++)
		{
			if (a[i] != b[i]) return a[i].CompareTo(b[i]);
		}
		return a.Length.CompareTo(b.Length);
	}

	private static int CompareStructs(Dictionary<string, object?> a, Dictionary<string, object?> b)
	{
		// Compare field by field in ordinal order
		var keysA = a.Keys.ToList();
		var keysB = b.Keys.ToList();
		var len = Math.Min(keysA.Count, keysB.Count);
		for (int i = 0; i < len; i++)
		{
			var cmp = CompareValues(a[keysA[i]], b[keysB[i]]);
			if (cmp != 0) return cmp;
		}
		return keysA.Count.CompareTo(keysB.Count);
	}

	private static int CompareArrays(List<object?> a, List<object?> b)
	{
		// Compare element by element; shorter array is less if all shared elements equal
		var len = Math.Min(a.Count, b.Count);
		for (int i = 0; i < len; i++)
		{
			var cmp = CompareValues(a[i], b[i]);
			if (cmp != 0) return cmp;
		}
		return a.Count.CompareTo(b.Count);
	}

	private static object? ConcatValues(object? a, object? b)
	{
		if (a is null || b is null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#concatenation_operator
		//   || is overloaded: STRING || STRING, BYTES || BYTES, ARRAY<T> || ARRAY<T>
		if (a is IList<object?> arrA && b is IList<object?> arrB)
		{
			var result = new List<object?>(arrA.Count + arrB.Count);
			result.AddRange(arrA);
			result.AddRange(arrB);
			return result;
		}
		return a.ToString() + b.ToString();
	}

	private static object? ArithmeticOp(object? a, object? b, char op)
	{
		if (a is null || b is null) return null;

		if (a is long la && b is long lb)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
			//   INT64 overflow produces an error.
			//   "INT64 / INT64 → FLOAT64"
			if (op == '/')
			{
				if (lb == 0) throw new InvalidOperationException("Division by zero.");
				return (double)la / (double)lb;
			}
			try
			{
				return op switch
				{
					'+' => checked(la + lb),
					'-' => checked(la - lb),
					'*' => checked(la * lb),
					'%' => lb == 0 ? throw new InvalidOperationException("Division by zero.") : la % lb,
					_ => throw new NotSupportedException()
				};
			}
			catch (OverflowException)
			{
				throw new InvalidOperationException("INT64 overflow during arithmetic operation.");
			}
		}

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
		//   NUMERIC arithmetic preserves precision within NUMERIC type.
		if (a is decimal || b is decimal)
		{
			var da = Convert.ToDecimal(a);
			var db = Convert.ToDecimal(b);
			return op switch
			{
				'+' => da + db,
				'-' => da - db,
				'*' => da * db,
				'/' => db == 0m ? throw new InvalidOperationException("Division by zero.") : da / db,
				'%' => db == 0m ? throw new InvalidOperationException("Division by zero.") : da % db,
				_ => throw new NotSupportedException()
			};
		}

		var dda = Convert.ToDouble(a);
		var ddb = Convert.ToDouble(b);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#arithmetic_operators
		//   "Divide by zero operations return an error. To return a different result,
		//    consider the IEEE_DIVIDE or SAFE_DIVIDE functions."
		return op switch
		{
			'+' => dda + ddb,
			'-' => dda - ddb,
			'*' => dda * ddb,
			'/' => ddb == 0.0 ? throw new InvalidOperationException("Division by zero.") : dda / ddb,
			'%' => ddb == 0.0 ? throw new InvalidOperationException("Division by zero.") : dda % ddb,
			_ => throw new NotSupportedException()
		};
	}

	// ──────────────────────────────────────────
	// String function helpers
	// ──────────────────────────────────────────

	private object? EvalSplit(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		string delimiter;
		if (func.Arguments.Count > 1)
		{
			var delimVal = Evaluate(func.Arguments[1], row);
			// Ref: Standard SQL NULL propagation — NULL delimiter → NULL result
			if (delimVal is null) return null;
			delimiter = Convert.ToString(delimVal) ?? ",";
		}
		else
		{
			delimiter = ",";
		}
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split
		//   Empty delimiter splits the string into individual characters (no leading empty string).
		if (delimiter.Length == 0)
		{
			var result = new List<object?>();
			foreach (var ch in str)
				result.Add(ch.ToString());
			return result;
		}
		return str.Split(delimiter).Cast<object?>().ToList();
	}

	private object? EvalPad(FunctionCallExpr func, Dictionary<string, object?> row, bool padLeft)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
		var lenVal = Evaluate(func.Arguments[1], row);
		if (lenVal == null) return null;
		var len = Convert.ToInt32(lenVal);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
		//   "This function returns an error if: return_length is negative"
		if (len < 0) throw new InvalidOperationException($"{(padLeft ? "LPAD" : "RPAD")}: return_length must not be negative.");
		// Truncate if length is less than the string length
		if (len == 0) return "";
		if (len <= str.Length) return str[..len];
		var padVal = func.Arguments.Count > 2 ? Evaluate(func.Arguments[2], row) : (object)" ";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
		//   "If original_value, return_length, or pattern is NULL, this function returns NULL."
		if (padVal == null) return null;
		var pad = Convert.ToString(padVal) ?? " ";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#lpad
		//   "This function returns an error if: pattern is empty"
		if (pad.Length == 0) throw new InvalidOperationException($"{(padLeft ? "LPAD" : "RPAD")}: pattern must not be empty.");
		var sb = new System.Text.StringBuilder(len);
		if (!padLeft) sb.Append(str);
		var needed = len - str.Length;
		while (sb.Length < (padLeft ? needed : len))
		{
			var remaining = (padLeft ? needed : len) - sb.Length;
			sb.Append(pad[..Math.Min(pad.Length, remaining)]);
		}
		if (padLeft) sb.Append(str);
		return sb.ToString();
	}

	private object? EvalRepeat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#repeat
		//   "This function returns an error if the repetitions value is negative."
		var countVal = Evaluate(func.Arguments[1], row);
		if (countVal == null) return null;
		var count = Convert.ToInt32(countVal);
		if (count < 0) throw new InvalidOperationException("REPEAT: repetitions must not be negative.");
		return string.Concat(Enumerable.Repeat(str, count));
	}

	private object? EvalFormat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
		var fmt = Evaluate(func.Arguments[0], row);
		if (fmt == null) return null;
		var fmtStr = Convert.ToString(fmt) ?? "";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
		// NULL arguments are formatted as the literal string "NULL"
		var args = func.Arguments.Skip(1).Select(a => Evaluate(a, row)).ToArray();
		try { return SpannerFormat(fmtStr, args); }
		catch { return fmtStr; }
	}

	private static string SpannerFormat(string fmt, object?[] args)
	{
		var sb = new System.Text.StringBuilder();
		int argIdx = 0;
		int i = 0;
		while (i < fmt.Length)
		{
			if (fmt[i] == '%' && i + 1 < fmt.Length)
			{
				i++;
				if (fmt[i] == '%') { sb.Append('%'); i++; continue; }
				var flags = "";
				while (i < fmt.Length && "-+ 0#".Contains(fmt[i]))
					flags += fmt[i++];
				var widthStr = "";
				while (i < fmt.Length && char.IsDigit(fmt[i]))
					widthStr += fmt[i++];
				string? precision = null;
				if (i < fmt.Length && fmt[i] == '.')
				{
					i++;
					precision = "";
					while (i < fmt.Length && char.IsDigit(fmt[i]))
						precision += fmt[i++];
				}
				if (i >= fmt.Length) break;
				char conv = fmt[i++];
				if (argIdx >= args.Length) break;
				var arg = args[argIdx++];
				// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
				//   "NULL values are formatted as the string 'NULL'"
				if (arg == null) { sb.Append("NULL"); continue; }
				var width = widthStr.Length > 0 ? int.Parse(widthStr) : 0;
				switch (conv)
				{
					case 'd' or 'i':
						var lv = Convert.ToInt64(arg);
						var ds = lv.ToString();
						if (flags.Contains('0') && width > 0)
							ds = (lv < 0 ? "-" + (-lv).ToString().PadLeft(width - 1, '0') : ds.PadLeft(width, '0'));
						else if (width > 0)
							ds = ds.PadLeft(width);
						sb.Append(ds);
						break;
					case 'f':
						var dv = Convert.ToDouble(arg);
						var prec = precision != null && precision.Length > 0 ? int.Parse(precision) : 6;
						sb.Append(dv.ToString("F" + prec));
						break;
					case 'e':
						sb.Append(Convert.ToDouble(arg).ToString("E" + (precision ?? "6")));
						break;
					case 'g':
						sb.Append(Convert.ToDouble(arg).ToString("G" + (precision ?? "")));
						break;
						case 's':
						sb.Append(arg == null ? "NULL" : arg.ToString());
						break;
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
					//   %t — produces a printable string representing the value using its canonical format.
					//   %T — produces a string that is a valid GoogleSQL literal (strings are quoted).
					case 't':
						sb.Append(FormatValueCanonical(arg));
						break;
					case 'T':
						sb.Append(FormatValueSqlLiteral(arg));
						break;
					default:
						sb.Append(arg?.ToString());
						break;
				}
			}
			else
			{
				sb.Append(fmt[i++]);
			}
		}
		return sb.ToString();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	//   %t — value formatted as a printable string using its canonical format.
	private static string FormatValueCanonical(object? value)
	{
		if (value == null) return "NULL";
		return value switch
		{
			bool b => b ? "true" : "false",
			long l => l.ToString(),
			int i => i.ToString(),
			double d when double.IsPositiveInfinity(d) => "inf",
			double d when double.IsNegativeInfinity(d) => "-inf",
			double d when double.IsNaN(d) => "nan",
			double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
			float f when float.IsPositiveInfinity(f) => "inf",
			float f when float.IsNegativeInfinity(f) => "-inf",
			float f when float.IsNaN(f) => "nan",
			float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
			decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
			string s => s,
			DateTime dt when dt.Date == dt => dt.ToString("yyyy-MM-dd"),
			DateTime dt => FormatTimestampCanonical(dt),
			byte[] bytes => Convert.ToBase64String(bytes),
			IList<object?> arr => "[" + string.Join(", ", arr.Select(FormatValueCanonical)) + "]",
			_ => value.ToString() ?? ""
		};
	}

	// Formats a DateTime as a Spanner timestamp string, trimming only sub-second fractional zeros.
	private static string FormatTimestampCanonical(DateTime dt)
	{
		var s = dt.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
		if (dt.Ticks % TimeSpan.TicksPerSecond != 0)
		{
			var frac = dt.ToString(".FFFFFF", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
			s += frac;
		}
		return s + "Z";
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
	//   %T — value formatted as a valid GoogleSQL literal (strings are quoted, etc.).
	private static string FormatValueSqlLiteral(object? value)
	{
		if (value == null) return "NULL";
		return value switch
		{
			bool b => b ? "TRUE" : "FALSE",
			long l => l.ToString(),
			int i => i.ToString(),
			double d when double.IsPositiveInfinity(d) => "CAST('inf' AS FLOAT64)",
			double d when double.IsNegativeInfinity(d) => "CAST('-inf' AS FLOAT64)",
			double d when double.IsNaN(d) => "CAST('nan' AS FLOAT64)",
			double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
			float f when float.IsPositiveInfinity(f) => "CAST('inf' AS FLOAT64)",
			float f when float.IsNegativeInfinity(f) => "CAST('-inf' AS FLOAT64)",
			float f when float.IsNaN(f) => "CAST('nan' AS FLOAT64)",
			float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
			decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
			string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
			DateTime dt when dt.Date == dt => "DATE \"" + dt.ToString("yyyy-MM-dd") + "\"",
			DateTime dt => "TIMESTAMP \"" + FormatTimestampCanonical(dt).TrimEnd('Z') + "Z\"",
			byte[] bytes => "b\"" + Convert.ToBase64String(bytes) + "\"",
			IList<object?> arr => "[" + string.Join(", ", arr.Select(FormatValueSqlLiteral)) + "]",
			_ => value.ToString() ?? ""
		};
	}

	private object? EvalRegexpContains(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var pattern = Evaluate(func.Arguments[1], row);
		if (s == null || pattern == null) return null;
		return Regex.IsMatch(Convert.ToString(s)!, Convert.ToString(pattern)!);
	}

	private object? EvalRegexpExtract(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var pattern = Evaluate(func.Arguments[1], row);
		if (s == null || pattern == null) return null;
		var patStr = Convert.ToString(pattern)!;
		var sourceStr = Convert.ToString(s)!;

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract
		//   REGEXP_EXTRACT(value, regexp[, position[, occurrence]])
		//   position: 1-based starting position (default 1)
		//   occurrence: 1-based occurrence index (default 1)
		int position = 1;
		int occurrence = 1;
		if (func.Arguments.Count >= 3)
		{
			var posVal = Evaluate(func.Arguments[2], row);
			if (posVal == null) return null;
			position = Convert.ToInt32(posVal);
			if (position < 1)
				throw new InvalidOperationException(
					"REGEXP_EXTRACT: position must be a positive integer");
		}
		if (func.Arguments.Count >= 4)
		{
			var occVal = Evaluate(func.Arguments[3], row);
			if (occVal == null) return null;
			occurrence = Convert.ToInt32(occVal);
			if (occurrence < 1)
				throw new InvalidOperationException(
					"REGEXP_EXTRACT: occurrence must be a positive integer");
		}

		// Apply position offset: search from (position-1) in the source string
		int startIndex = position - 1;
		if (startIndex >= sourceStr.Length) return null;
		var searchStr = sourceStr.Substring(startIndex);

		var match = Regex.Match(searchStr, patStr);
		// At most one capturing group is allowed
		var capturingGroups = match.Groups.Count - 1; // Groups[0] is the full match
		if (capturingGroups > 1)
			throw new InvalidOperationException(
				"REGEXP_EXTRACT: pattern has more than one capturing group");
		if (!match.Success) return null;

		// Find the Nth occurrence
		for (int i = 1; i < occurrence; i++)
		{
			match = match.NextMatch();
			if (!match.Success) return null;
		}

		return capturingGroups == 1 ? match.Groups[1].Value : match.Value;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	//   LIKE supports % (any chars) and _ (single char).
	//   Backslash escapes: \% → literal %, \_ → literal _, \\ → literal \.
	//   "SELECT NULL LIKE 'a%'; -- Produces an error"
	//   "SELECT 'apple' LIKE NULL; -- Produces an error"
	private object? EvalLike(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = Evaluate(func.Arguments[0], row);
		var pat = Evaluate(func.Arguments[1], row);
		if (val == null || pat == null)
			throw new InvalidOperationException("LIKE operator does not support NULL operands.");
		var valStr = Convert.ToString(val)!;
		var patStr = Convert.ToString(pat)!;

		// Build regex from LIKE pattern, processing escape sequences first
		var sb = new System.Text.StringBuilder("^");
		for (int i = 0; i < patStr.Length; i++)
		{
			var ch = patStr[i];
			if (ch == '\\' && i + 1 < patStr.Length)
			{
				// Backslash escape: next char is literal
				i++;
				sb.Append(Regex.Escape(patStr[i].ToString()));
			}
			else if (ch == '%')
			{
				sb.Append(".*");
			}
			else if (ch == '_')
			{
				sb.Append('.');
			}
			else
			{
				sb.Append(Regex.Escape(ch.ToString()));
			}
		}
		sb.Append('$');
		return Regex.IsMatch(valStr, sb.ToString(), RegexOptions.Singleline);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#like_operator
	//   LIKE ANY/SOME: returns TRUE if value matches at least one pattern.
	//   LIKE ALL: returns TRUE if value matches every pattern.
	//   Returns NULL if value is NULL. NULL patterns are treated as non-matching (ANY) / matching (ALL).
	private object? EvalLikeQuantified(FunctionCallExpr func, Dictionary<string, object?> row, bool any)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;

		// Arguments[1..] are the patterns
		bool hasNull = false;
		for (int i = 1; i < func.Arguments.Count; i++)
		{
			var patVal = Evaluate(func.Arguments[i], row);
			if (patVal == null)
			{
				hasNull = true;
				continue;
			}
			var likeFunc = new FunctionCallExpr("LIKE", new List<SqlExpression> { func.Arguments[0], func.Arguments[i] });
			var result = (bool)EvalLike(likeFunc, row)!;
			if (any && result) return true;
			if (!any && !result) return false;
		}
		// If we had NULL patterns without a definitive answer, propagate NULL
		if (hasNull) return null;
		return !any; // ALL: all matched → true; ANY: none matched → false
	}

	private object? EvalRegexpReplace(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var pattern = Evaluate(func.Arguments[1], row);
		var replacement = Evaluate(func.Arguments[2], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
		//   "You can use backslashed-escaped digits (\1 to \9) within the replacement argument"
		if (s == null || pattern == null || replacement == null) return null;
		// Convert Spanner backreference format (\1, \2) to .NET format ($1, $2)
		var dotNetReplacement = Regex.Replace(Convert.ToString(replacement)!, @"\\(\d)", "$$$1");
		return Regex.Replace(Convert.ToString(s)!, Convert.ToString(pattern)!, dotNetReplacement);
	}



	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#byte_length
	//   BYTE_LENGTH on STRING returns UTF-8 byte count; on BYTES returns the length.
	private object? EvalByteLength(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		if (v is byte[] bytes) return (long)bytes.Length;
		return (long)System.Text.Encoding.UTF8.GetByteCount(Convert.ToString(v)!);
	}

	private object? EvalToHex(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		if (v is byte[] bytes) return Convert.ToHexString(bytes).ToLowerInvariant();
		return Convert.ToInt64(v).ToString("x");
	}

	private object? EvalFromHex(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		return Convert.FromHexString(Convert.ToString(v)!);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#code_points_to_string
	//   CODE_POINTS_TO_STRING(ARRAY<INT64>) converts an array of code points to a STRING.
	private object? EvalChr(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		// If the argument is an array of code points, convert each
		if (v is IList<object?> arr)
		{
			var sb = new System.Text.StringBuilder();
			foreach (var cp in arr)
			{
				if (cp == null) continue;
				sb.Append(char.ConvertFromUtf32(Convert.ToInt32(cp)));
			}
			return sb.ToString();
		}
		// Fallback: single code point
		return char.ConvertFromUtf32(Convert.ToInt32(v));
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
	//   CAST(string AS FLOAT64) must accept 'inf', '-inf', 'nan' (case-insensitive).
	private static double ConvertToFloat64(object value)
	{
		if (value is string s)
		{
			if (s.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("+inf", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("infinity", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("+infinity", StringComparison.OrdinalIgnoreCase))
				return double.PositiveInfinity;
			if (s.Equals("-inf", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
				return double.NegativeInfinity;
			if (s.Equals("nan", StringComparison.OrdinalIgnoreCase))
				return double.NaN;
		}
		return Convert.ToDouble(value);
	}

	private static float ConvertToFloat32(object value)
	{
		if (value is string s)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
			if (s.Equals("inf", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("+inf", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("infinity", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("+infinity", StringComparison.OrdinalIgnoreCase))
				return float.PositiveInfinity;
			if (s.Equals("-inf", StringComparison.OrdinalIgnoreCase) ||
				s.Equals("-infinity", StringComparison.OrdinalIgnoreCase))
				return float.NegativeInfinity;
			if (s.Equals("nan", StringComparison.OrdinalIgnoreCase))
				return float.NaN;
		}
		return Convert.ToSingle(value);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#soundex
	//   Standard US Census Soundex: H and W do NOT separate coded letters
	//   (letters with the same code across H/W are treated as a single code).
	private static string EvalSoundex(string s)
	{
		if (string.IsNullOrEmpty(s)) return "";
		var result = new char[4];
		result[0] = char.ToUpper(s[0]);
		int idx = 1;
		char lastCode = SoundexCode(s[0]);
		for (int i = 1; i < s.Length && idx < 4; i++)
		{
			var c = char.ToUpper(s[i]);
			var code = SoundexCode(s[i]);
			if (code == '0')
			{
				// H and W don't separate codes; vowels (A,E,I,O,U,Y) do
				if (c != 'H' && c != 'W')
					lastCode = '0';
				continue;
			}
			if (code != lastCode)
			{
				result[idx++] = code;
				lastCode = code;
			}
		}
		while (idx < 4) result[idx++] = '0';
		return new string(result);
	}

	private static char SoundexCode(char c) => char.ToUpper(c) switch
	{
		'B' or 'F' or 'P' or 'V' => '1',
		'C' or 'G' or 'J' or 'K' or 'Q' or 'S' or 'X' or 'Z' => '2',
		'D' or 'T' => '3',
		'L' => '4',
		'M' or 'N' => '5',
		'R' => '6',
		_ => '0'
	};



	// ──────────────────────────────────────────
	// Math function helpers
	// ──────────────────────────────────────────

	private object? EvalTrunc(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#trunc
		//   "TRUNC(X [, N]): Rounds X to N decimal places toward zero."
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		var d = Convert.ToDouble(v);
		if (func.Arguments.Count > 1)
		{
			var nVal = Evaluate(func.Arguments[1], row);
			if (nVal == null) return null;
			var n = Convert.ToInt32(nVal);
			var factor = Math.Pow(10, n);
			return Math.Truncate(d * factor) / factor;
		}
		return Math.Truncate(d);
	}

	private object? EvalDiv(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#div
		//   "DIV(X, Y): integer division."
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a == null || b == null) return null;
		var la = Convert.ToInt64(a);
		var lb = Convert.ToInt64(b);
		if (lb == 0) throw new InvalidOperationException("Division by zero in DIV.");
		return la / lb;
	}

	private object? EvalIeeeDivide(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#ieee_divide
		//   "Returns NaN, +Inf, -Inf for special cases instead of errors."
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a == null || b == null) return null;
		var da = Convert.ToDouble(a);
		var db = Convert.ToDouble(b);
		return da / db; // IEEE754 semantics: 0/0=NaN, x/0=±Inf
	}

	private object? EvalSafeDivide(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a == null || b == null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_divide
		//   "Equivalent to the division operator (X / Y), but returns NULL if an error occurs."
		//   "Returns NUMERIC when both arguments are NUMERIC."
		if (a is decimal da && b is decimal db)
		{
			if (db == 0m) return null;
			try { return da / db; }
			catch (OverflowException) { return null; }
		}
		var dblA = Convert.ToDouble(a);
		var dblB = Convert.ToDouble(b);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
		//   "All mathematical functions return NaN if any of the arguments is NaN."
		if (double.IsNaN(dblA) || double.IsNaN(dblB)) return double.NaN;
		// Division by zero is an error → return NULL
		if (dblB == 0) return null;
		var result = dblA / dblB;
		// NaN from Inf/Inf is an error → NULL
		if (double.IsNaN(result)) return null;
		// Overflow from finite/finite producing Inf is an error → NULL
		// But Inf/finite = Inf is valid (input was already Inf)
		if (double.IsInfinity(result) && !double.IsInfinity(dblA)) return null;
		return result;
	}

	private object? EvalSafeNegate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		try
		{
			if (v is long l) return checked(-l);
			if (v is decimal d) return -d;
			return -Convert.ToDouble(v);
		}
		catch (OverflowException) { return null; }
	}

	private object? EvalSafeArith(FunctionCallExpr func, Dictionary<string, object?> row, string op)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a == null || b == null) return null;
		try
		{
			if (a is long la && b is long lb) return op switch
			{
				"ADD" => checked(la + lb),
				"SUB" => checked(la - lb),
				"MUL" => checked(la * lb),
				_ => throw new NotSupportedException()
			};
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#safe_add
			//   "Returns NUMERIC when at least one argument is NUMERIC."
			if (a is decimal || b is decimal)
			{
				var da2 = Convert.ToDecimal(a); var db2 = Convert.ToDecimal(b);
				return op switch
				{
					"ADD" => checked(da2 + db2),
					"SUB" => checked(da2 - db2),
					"MUL" => checked(da2 * db2),
					_ => throw new NotSupportedException()
				};
			}
			var da = Convert.ToDouble(a); var db = Convert.ToDouble(b);
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
			//   "All mathematical functions return NaN if any of the arguments is NaN."
			if (double.IsNaN(da) || double.IsNaN(db)) return double.NaN;
			var result = op switch
			{
				"ADD" => da + db,
				"SUB" => da - db,
				"MUL" => da * db,
				_ => throw new NotSupportedException()
			};
			// NaN from indeterminate form (e.g., Inf + (-Inf), Inf * 0) → NULL
			if (double.IsNaN(result)) return null;
			// Overflow: finite op finite → Inf is an error → NULL
			// But Inf op finite → Inf is valid (input was already Inf)
			if (double.IsInfinity(result) && !double.IsInfinity(da) && !double.IsInfinity(db)) return null;
			return result;
		}
		catch (OverflowException) { return null; }
	}

	private object? EvalPow(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a == null || b == null) return null;
		var da = Convert.ToDouble(a);
		var db = Convert.ToDouble(b);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#pow
		//   "| Finite value < 0 | Non-integer | Error |"
		if (double.IsFinite(da) && da < 0 && double.IsFinite(db) && db != Math.Truncate(db))
			throw new InvalidOperationException("Negative value raised to a non-integer power.");
		//   "| 0 | Finite value < 0 | Error |"
		if (da == 0 && double.IsFinite(db) && db < 0)
			throw new InvalidOperationException("Zero raised to a negative power.");
		return Math.Pow(da, db);
	}

	private object? EvalLog(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		var dv = Convert.ToDouble(v);
		if (dv <= 0)
			throw new InvalidOperationException("LOG of non-positive number");
		if (func.Arguments.Count > 1)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#log
			//   "LOG(X, Y) — computes the logarithm of X to base Y."
			//   i.e., log_Y(X) = Math.Log(X, Y)
			var b = Evaluate(func.Arguments[1], row);
			if (b == null) return null;
			var db = Convert.ToDouble(b);
			if (db <= 0 || db == 1.0)
				throw new InvalidOperationException("LOG base must be positive and not equal to 1");
			return Math.Log(dv, db);
		}
		return Math.Log(dv);
	}

	private object? EvalIsNan(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		return double.IsNaN(Convert.ToDouble(v));
	}

	private object? EvalIsInf(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		return double.IsInfinity(Convert.ToDouble(v));
	}



	// ──────────────────────────────────────────
	// Date/Time function helpers
	// ──────────────────────────────────────────

	private object? EvalTimestampCtor(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		if (v is DateTime dt) return dt;
		if (v is DateTimeOffset dto) return dto.UtcDateTime;
		return DateTime.Parse(Convert.ToString(v)!, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
	}

	private object? EvalDateCtor(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date
		//   DATE(year, month, day) or DATE(timestamp_expression)
		if (func.Arguments.Count >= 3)
		{
			var year = Evaluate(func.Arguments[0], row);
			var month = Evaluate(func.Arguments[1], row);
			var day = Evaluate(func.Arguments[2], row);
			if (year == null || month == null || day == null) return null;
			return new DateTime(Convert.ToInt32(year), Convert.ToInt32(month), Convert.ToInt32(day));
		}
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		if (v is DateTime dt) return dt.Date;
		if (v is DateTimeOffset dto) return dto.UtcDateTime.Date;
		return DateTime.Parse(Convert.ToString(v)!).Date;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
	//   "If no time zone is specified, the default time zone, America/Los_Angeles, is used."
	private static readonly TimeZoneInfo DefaultTimeZone =
		TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

	private object? EvalExtract(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// EXTRACT(part FROM expr) — simplified: func.Arguments[0] is the part name, [1] is the expr
		if (func.Arguments.Count < 2) return null;
		var part = Evaluate(func.Arguments[0], row);
		var ts = Evaluate(func.Arguments[1], row);
		if (ts == null) return null;

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#extract
		//   EXTRACT can also extract parts from an INTERVAL value.
		if (ts is SpannerInterval interval)
		{
			var intervalPart = Convert.ToString(part)?.ToUpperInvariant() ?? "";
			return interval.Extract(intervalPart);
		}

		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
		//   EXTRACT on TIMESTAMP converts to civil time in the default timezone (America/Los_Angeles)
		//   before extracting date/time parts. DATE values are not converted.
		if (dt.Kind == DateTimeKind.Utc)
			dt = TimeZoneInfo.ConvertTimeFromUtc(dt, DefaultTimeZone);

		var partName = Convert.ToString(part)?.ToUpperInvariant() ?? "";
		return partName switch
		{
			"YEAR" => (long)dt.Year,
			"MONTH" => (long)dt.Month,
			"DAY" => (long)dt.Day,
			"HOUR" => (long)dt.Hour,
			"MINUTE" => (long)dt.Minute,
			"SECOND" => (long)dt.Second,
			"MILLISECOND" => (long)dt.Millisecond,
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
			//   MICROSECOND: the microseconds component (0-999999)
			"MICROSECOND" => (long)(dt.Ticks / 10 % 1_000_000),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#extract
			//   NANOSECOND: the nanoseconds component (0-999999999)
			"NANOSECOND" => (long)(dt.Ticks % TimeSpan.TicksPerSecond * 100),
			"DAYOFWEEK" => (long)(dt.DayOfWeek == DayOfWeek.Sunday ? 1 : (int)dt.DayOfWeek + 1),
			"DAYOFYEAR" => (long)dt.DayOfYear,
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#extract
			//   WEEK: range [0,53]. Weeks begin with Sunday; dates before the first Sunday are week 0.
			"WEEK" => ComputeSpannerWeek(dt),
			"ISOWEEK" => (long)System.Globalization.ISOWeek.GetWeekOfYear(dt),
			"ISOYEAR" => (long)System.Globalization.ISOWeek.GetYear(dt),
			"QUARTER" => (long)((dt.Month - 1) / 3 + 1),
			"DATE" => dt.Date,
			_ => throw new InvalidOperationException($"EXTRACT: unsupported part '{partName}'.")
		};
	}

	private object? EvalTimestampAdd(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		var amount = Convert.ToInt64(Evaluate(func.Arguments[1], row));
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		if (ts == null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
		//   TIMESTAMP_ADD only supports: NANOSECOND, MICROSECOND, MILLISECOND, SECOND, MINUTE, HOUR, DAY
		if (part is "MONTH" or "YEAR" or "WEEK" or "QUARTER")
			throw new InvalidOperationException($"TIMESTAMP_ADD does not support the {part} date part");
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		return AddToPart(dt, part!, amount);
	}

	private object? EvalTimestampSub(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		var amount = Convert.ToInt64(Evaluate(func.Arguments[1], row));
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		if (ts == null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_sub
		if (part is "MONTH" or "YEAR" or "WEEK" or "QUARTER")
			throw new InvalidOperationException($"TIMESTAMP_SUB does not support the {part} date part");
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		return AddToPart(dt, part!, -amount);
	}

	private static DateTime AddToPart(DateTime dt, string part, long amount) => part switch
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_add
		//   Supported date parts: NANOSECOND, MICROSECOND, MILLISECOND, SECOND, MINUTE, HOUR, DAY
		"NANOSECOND" => dt.AddTicks(amount / 100),
		"MICROSECOND" => dt.AddTicks(amount * 10),
		"MILLISECOND" => dt.AddMilliseconds(amount),
		"SECOND" => dt.AddSeconds(amount),
		"MINUTE" => dt.AddMinutes(amount),
		"HOUR" => dt.AddHours(amount),
		"DAY" => dt.AddDays(amount),
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
		//   DATE_ADD supports WEEK, MONTH, QUARTER, and YEAR.
		"WEEK" => dt.AddDays(amount * 7),
		"MONTH" => dt.AddMonths((int)amount),
		"QUARTER" => dt.AddMonths((int)amount * 3),
		"YEAR" => dt.AddYears((int)amount),
		_ => throw new InvalidOperationException($"Unsupported INTERVAL part: {part}")
	};

	private object? EvalTimestampDiff(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts1 = Evaluate(func.Arguments[0], row);
		var ts2 = Evaluate(func.Arguments[1], row);
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		if (ts1 == null || ts2 == null) return null;
		var dt1 = ts1 is DateTime d1 ? d1 : DateTime.Parse(Convert.ToString(ts1)!);
		var dt2 = ts2 is DateTime d2 ? d2 : DateTime.Parse(Convert.ToString(ts2)!);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_diff
		//   granularity only supports: NANOSECOND, MICROSECOND, MILLISECOND, SECOND, MINUTE, HOUR, DAY
		var diff = dt1 - dt2;
		return part switch
		{
			"NANOSECOND" => diff.Ticks * 100,
			"MICROSECOND" => diff.Ticks / 10,
			"MILLISECOND" => (long)diff.TotalMilliseconds,
			"SECOND" => (long)diff.TotalSeconds,
			"MINUTE" => (long)diff.TotalMinutes,
			"HOUR" => (long)diff.TotalHours,
			"DAY" => (long)diff.TotalDays,
			_ => throw new InvalidOperationException($"TIMESTAMP_DIFF: unsupported part '{part}'. Only NANOSECOND through DAY are supported.")
		};
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
	//   "If no time zone is specified, the default time zone, America/Los_Angeles, is used."
	//   TIMESTAMP_TRUNC converts to the default timezone, truncates, then converts back to UTC.
	private object? EvalTimestampTrunc(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		var part = Convert.ToString(Evaluate(func.Arguments[1], row))?.ToUpperInvariant();
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);

		// Convert UTC timestamps to default timezone before truncating
		var isUtc = dt.Kind == DateTimeKind.Utc;
		if (isUtc) dt = TimeZoneInfo.ConvertTimeFromUtc(dt, DefaultTimeZone);

		var truncated = part switch
		{
			"MICROSECOND" => new DateTime(dt.Ticks / 10 * 10, dt.Kind),
			"MILLISECOND" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond, dt.Kind),
			"SECOND" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind),
			"MINUTE" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind),
			"HOUR" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind),
			"DAY" => new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Kind),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
			//   "WEEK: Truncates to the preceding Sunday."
			"WEEK" => TruncToWeekday(dt, DayOfWeek.Sunday),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
			//   "ISOWEEK: Truncates to the preceding Monday (ISO 8601 week start)."
			"ISOWEEK" => TruncToWeekday(dt, DayOfWeek.Monday),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
			//   "QUARTER: Truncates to the first day of the quarter."
			"QUARTER" => new DateTime(dt.Year, ((dt.Month - 1) / 3) * 3 + 1, 1, 0, 0, 0, dt.Kind),
			"MONTH" => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, dt.Kind),
			"YEAR" => new DateTime(dt.Year, 1, 1, 0, 0, 0, dt.Kind),
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_trunc
			//   "ISOYEAR: Truncates to the first day of the ISO 8601 year."
			"ISOYEAR" => TruncToIsoYear(dt),
			_ => throw new InvalidOperationException($"TIMESTAMP_TRUNC: unsupported part '{part}'.")
		};

		// Convert back to UTC
		if (isUtc) truncated = TimeZoneInfo.ConvertTimeToUtc(truncated, DefaultTimeZone);

		return truncated;
	}

	private static DateTime TruncToWeekday(DateTime dt, DayOfWeek weekStart)
	{
		int diff = ((int)dt.DayOfWeek - (int)weekStart + 7) % 7;
		return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Kind).AddDays(-diff);
	}

	private static DateTime TruncToIsoYear(DateTime dt)
	{
		// ISO year starts on the Monday of the week containing Jan 4
		var jan4 = new DateTime(dt.Year, 1, 4);
		int diff = ((int)jan4.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
		var isoYearStart = jan4.AddDays(-diff);
		if (dt < isoYearStart)
		{
			// dt is in previous ISO year
			jan4 = new DateTime(dt.Year - 1, 1, 4);
			diff = ((int)jan4.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
			isoYearStart = jan4.AddDays(-diff);
		}
		return new DateTime(isoYearStart.Year, isoYearStart.Month, isoYearStart.Day, 0, 0, 0, dt.Kind);
	}

	private object? EvalDateAdd(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		var amountVal = Evaluate(func.Arguments[1], row);
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_add
		//   DATE_ADD only supports: DAY, WEEK, MONTH, QUARTER, YEAR
		if (part is "NANOSECOND" or "MICROSECOND" or "MILLISECOND" or "SECOND" or "MINUTE" or "HOUR")
			throw new InvalidOperationException($"DATE_ADD does not support the {part} date part");
		if (v == null || amountVal == null) return null;
		var amount = Convert.ToInt32(amountVal);
		var dt = v is DateTime d ? d : DateTime.Parse(Convert.ToString(v)!);
		return AddToPart(dt, part!, amount).Date;
	}

	private object? EvalDateSub(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		var amountVal = Evaluate(func.Arguments[1], row);
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_sub
		//   DATE_SUB only supports: DAY, WEEK, MONTH, QUARTER, YEAR
		if (part is "NANOSECOND" or "MICROSECOND" or "MILLISECOND" or "SECOND" or "MINUTE" or "HOUR")
			throw new InvalidOperationException($"DATE_SUB does not support the {part} date part");
		if (v == null || amountVal == null) return null;
		var amount = Convert.ToInt32(amountVal);
		var dt = v is DateTime d ? d : DateTime.Parse(Convert.ToString(v)!);
		return AddToPart(dt, part!, -amount).Date;
	}

	private object? EvalDateDiff(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v1 = Evaluate(func.Arguments[0], row);
		var v2 = Evaluate(func.Arguments[1], row);
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		if (v1 == null || v2 == null) return null;
		var dt1 = v1 is DateTime d1 ? d1 : DateTime.Parse(Convert.ToString(v1)!);
		var dt2 = v2 is DateTime d2 ? d2 : DateTime.Parse(Convert.ToString(v2)!);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_diff
		//   Counts unit boundaries between two dates.
		return part switch
		{
			"DAY" => (long)(dt1.Date - dt2.Date).TotalDays,
			"WEEK" => DateDiffWeek(dt1, dt2, DayOfWeek.Sunday),
			"ISOWEEK" => DateDiffWeek(dt1, dt2, DayOfWeek.Monday),
			"MONTH" => (long)((dt1.Year - dt2.Year) * 12 + dt1.Month - dt2.Month),
			"QUARTER" => (long)((dt1.Year * 4 + (dt1.Month - 1) / 3) - (dt2.Year * 4 + (dt2.Month - 1) / 3)),
			"YEAR" => (long)(dt1.Year - dt2.Year),
			"ISOYEAR" => (long)(System.Globalization.ISOWeek.GetYear(dt1) - System.Globalization.ISOWeek.GetYear(dt2)),
			_ => throw new InvalidOperationException($"DATE_DIFF: unsupported part '{part}'.")
		};
	}

	// Counts weekday-based week boundaries between two dates.
	private static long DateDiffWeek(DateTime dt1, DateTime dt2, DayOfWeek weekStart)
	{
		// Compute a week number relative to an arbitrary epoch using the given week start day.
		// Each date's week number = floor((daysSinceEpoch + offset) / 7)
		// where offset aligns the week start day to boundary 0.
		var epoch = new DateTime(1970, 1, 1); // Thursday
		var days1 = (long)(dt1.Date - epoch).TotalDays;
		var days2 = (long)(dt2.Date - epoch).TotalDays;
		// offset so that weekStart day maps to 0 mod 7
		int offset = ((int)DayOfWeek.Thursday - (int)weekStart + 7) % 7;
		var week1 = FloorDiv(days1 + offset, 7);
		var week2 = FloorDiv(days2 + offset, 7);
		return week1 - week2;
	}

	private static long FloorDiv(long a, long b) => Math.DivRem(a, b, out long rem) - (rem < 0 ? 1 : 0);

	private object? EvalDateTrunc(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		return EvalTimestampTrunc(func, row);
	}

	private object? EvalFormatTimestamp(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var fmt = Convert.ToString(Evaluate(func.Arguments[0], row));
		var ts = Evaluate(func.Arguments[1], row);
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#format_timestamp
		//   FORMAT_TIMESTAMP(format_string, timestamp[, time_zone])
		if (func.Arguments.Count > 2)
		{
			var tzStr = Convert.ToString(Evaluate(func.Arguments[2], row));
			if (!string.IsNullOrEmpty(tzStr))
			{
				var tz = TimeZoneInfo.FindSystemTimeZoneById(tzStr);
				dt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dt, DateTimeKind.Utc), tz);
			}
		}
		return dt.ToString(ConvertSpannerDateFormat(fmt ?? ""), System.Globalization.CultureInfo.InvariantCulture);
	}

	private object? EvalParseTimestamp(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var fmtVal = Evaluate(func.Arguments[0], row);
		var strVal = Evaluate(func.Arguments[1], row);
		if (fmtVal == null || strVal == null) return null;
		var fmt = Convert.ToString(fmtVal)!;
		var str = Convert.ToString(strVal)!;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#parse_timestamp
		//   PARSE_TIMESTAMP(format_string, timestamp_string[, time_zone])
		//   If no timezone in format/string, the 3rd parameter specifies which timezone to assume.
		if (func.Arguments.Count > 2)
		{
			var tzStr = Convert.ToString(Evaluate(func.Arguments[2], row));
			if (!string.IsNullOrEmpty(tzStr))
			{
				var tz = TimeZoneInfo.FindSystemTimeZoneById(tzStr);
				var local = DateTime.ParseExact(str, ConvertSpannerDateFormat(fmt), System.Globalization.CultureInfo.InvariantCulture);
				return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), tz);
			}
		}
		return DateTime.ParseExact(str, ConvertSpannerDateFormat(fmt), System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
	}

	private object? EvalFormatDate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		return EvalFormatTimestamp(func, row);
	}

	private object? EvalParseDate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var fmtVal = Evaluate(func.Arguments[0], row);
		var strVal = Evaluate(func.Arguments[1], row);
		if (fmtVal == null || strVal == null) return null;
		var fmt = Convert.ToString(fmtVal)!;
		var str = Convert.ToString(strVal)!;
		return DateTime.ParseExact(str, ConvertSpannerDateFormat(fmt), System.Globalization.CultureInfo.InvariantCulture).Date;
	}

	private static string ConvertSpannerDateFormat(string spannerFmt)
	{
		// Convert Spanner strftime-like to .NET format
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/format-elements#format_elements_date_time
		return spannerFmt
			.Replace("%%", "\x00") // Temporarily escape literal %
			.Replace("%E9S", "ss.fffffffff") // nanoseconds (limited to 7 digits in .NET)
			.Replace("%E3S", "ss.fff").Replace("%E6S", "ss.ffffff")
			.Replace("%Y", "yyyy").Replace("%m", "MM").Replace("%d", "dd")
			.Replace("%H", "HH").Replace("%I", "hh").Replace("%M", "mm").Replace("%S", "ss")
			.Replace("%F", "yyyy-MM-dd").Replace("%T", "HH:mm:ss").Replace("%R", "HH:mm")
			.Replace("%p", "tt").Replace("%P", "tt")
			.Replace("%Z", "K").Replace("%z", "zzz")
			.Replace("%B", "MMMM").Replace("%A", "dddd")
			.Replace("%b", "MMM").Replace("%a", "ddd")
			.Replace("%e", "d").Replace("%j", "DDD")
			.Replace("%u", "d") // ISO weekday (1=Monday) — approximate
			.Replace("%V", "ww") // ISO week — approximate with .NET
			.Replace("%n", "\n").Replace("%t", "\t")
			.Replace("\x00", "%"); // Restore literal %
	}

	private object? EvalUnixSeconds(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		return (long)new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
	}

	private object? EvalUnixMillis(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds();
	}

	private object? EvalUnixMicros(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#unix_micros
		//   "Returns the number of microseconds since 1970-01-01 00:00:00 UTC."
		//   Must preserve sub-millisecond precision — use ticks-based calculation.
		var dto = new DateTimeOffset(dt, TimeSpan.Zero);
		const long unixEpochTicks = 621355968000000000L;
		return (dto.UtcTicks - unixEpochTicks) / 10;
	}

	private object? EvalTimestampFromUnix(FunctionCallExpr func, Dictionary<string, object?> row, string unit)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		var n = Convert.ToInt64(v);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#timestamp_micros
		//   Must preserve sub-millisecond precision for MICROS.
		return unit switch
		{
			"SECONDS" => DateTimeOffset.FromUnixTimeSeconds(n).UtcDateTime,
			"MILLIS" => DateTimeOffset.FromUnixTimeMilliseconds(n).UtcDateTime,
			"MICROS" => new DateTime(n * 10 + 621355968000000000L, DateTimeKind.Utc),
			_ => throw new NotSupportedException()
		};
	}

	// ──────────────────────────────────────────
	// INTERVAL function helpers
	// ──────────────────────────────────────────

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/data-types#interval_type
	//   "INTERVAL int64_expression datetime_part"
	private object? EvalIntervalLiteral(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var amount = Convert.ToInt64(Evaluate(func.Arguments[0], row));
		var part = Convert.ToString(Evaluate(func.Arguments[1], row))?.ToUpperInvariant();
		return part switch
		{
			"YEAR" => SpannerInterval.FromYears(amount),
			"QUARTER" => SpannerInterval.FromMonths(amount * 3),
			"MONTH" => SpannerInterval.FromMonths(amount),
			"WEEK" => SpannerInterval.FromDays(amount * 7),
			"DAY" => SpannerInterval.FromDays(amount),
			"HOUR" => SpannerInterval.FromHours(amount),
			"MINUTE" => SpannerInterval.FromMinutes(amount),
			"SECOND" => SpannerInterval.FromSeconds(amount),
			"MILLISECOND" => SpannerInterval.FromMilliseconds(amount),
			"MICROSECOND" => SpannerInterval.FromMicroseconds(amount),
			"NANOSECOND" => SpannerInterval.FromNanoseconds(amount),
			_ => throw new InvalidOperationException($"Unsupported INTERVAL datetime part: {part}")
		};
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#make_interval
	//   MAKE_INTERVAL(year, month, day, hour, minute, second)
	//   All arguments optional, default 0. Supports named arguments.
	private object? EvalMakeInterval(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		long Arg(int index) => func.Arguments.Count > index
			? Convert.ToInt64(Evaluate(func.Arguments[index], row) ?? 0L)
			: 0L;

		return SpannerInterval.Make(
			year: Arg(0), month: Arg(1), day: Arg(2),
			hour: Arg(3), minute: Arg(4), second: Arg(5));
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_days
	private object? EvalJustifyDays(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v is not SpannerInterval interval)
			throw new InvalidOperationException("JUSTIFY_DAYS requires an INTERVAL argument");
		return interval.JustifyDays();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_hours
	private object? EvalJustifyHours(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v is not SpannerInterval interval)
			throw new InvalidOperationException("JUSTIFY_HOURS requires an INTERVAL argument");
		return interval.JustifyHours();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/interval_functions#justify_interval
	private object? EvalJustifyInterval(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v is not SpannerInterval interval)
			throw new InvalidOperationException("JUSTIFY_INTERVAL requires an INTERVAL argument");
		return interval.JustifyInterval();
	}

	// ──────────────────────────────────────────
	// Conversion function helpers
	// ──────────────────────────────────────────

	private object? EvalToJson(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);

		bool isToJsonString = func.Name.Equals("TO_JSON_STRING", StringComparison.OrdinalIgnoreCase);

		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#to_json_string
		//   TO_JSON_STRING takes a SQL value of any type and returns a JSON-formatted STRING.
		//   Returns "null" for SQL NULL.
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#to_json
		//   TO_JSON takes a SQL value and returns a JSON value. Returns SQL NULL for SQL NULL.
		if (v == null) return isToJsonString ? "null" : null;

		if (isToJsonString)
		{
			if (v is JsonElement je) return je.GetRawText();
			if (v is string s) return JsonSerializer.Serialize(s); // produces "\"hello\""
			if (v is long l) return l.ToString();
			if (v is bool b) return b ? "true" : "false";
			if (v is double d) return JsonSerializer.Serialize(d);
			return JsonSerializer.Serialize(v);
		}

		// TO_JSON: return a JsonElement
		if (v is JsonElement alreadyJson) return alreadyJson;
		var json = JsonSerializer.Serialize(v);
		return JsonSerializer.Deserialize<JsonElement>(json);
	}

	private object? EvalParseJson(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		var json = Convert.ToString(v)!;
		return JsonSerializer.Deserialize<JsonElement>(json);
	}

	// ──────────────────────────────────────────
	// Array function helpers
	// ──────────────────────────────────────────

	private object? EvalArrayLength(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		if (v is IList<object?> list) return (long)list.Count;
		if (v is System.Collections.IEnumerable e) return (long)e.Cast<object?>().Count();
		return null;
	}

	private object? EvalArrayConcat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var result = new List<object?>();
		foreach (var arg in func.Arguments)
		{
			var v = Evaluate(arg, row);
			if (v == null) return null;
			if (v is IList<object?> list) result.AddRange(list);
			else if (v is System.Collections.IEnumerable e) result.AddRange(e.Cast<object?>());
		}
		return result;
	}

	private object? EvalArrayToString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		var sepVal = func.Arguments.Count > 1 ? Evaluate(func.Arguments[1], row) : (object)",";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
		//   Returns NULL if the separator is NULL.
		if (sepVal == null) return null;
		var sep = Convert.ToString(sepVal) ?? ",";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
		//   Signature: ARRAY_TO_STRING(ARRAY<STRING>, STRING, [STRING])
		//   or ARRAY_TO_STRING(ARRAY<BYTES>, BYTES, [BYTES])
		//   Non-string/bytes array elements are rejected.
		if (v is IList<object?> list)
		{
			// Validate element types — GCP Spanner only accepts ARRAY<STRING> or ARRAY<BYTES>
			var firstNonNull = list.FirstOrDefault(x => x != null);
			if (firstNonNull != null && firstNonNull is not string && firstNonNull is not byte[])
			{
				var elementType = firstNonNull is long ? "INT64" : firstNonNull is double ? "FLOAT64"
					: firstNonNull is bool ? "BOOL" : firstNonNull.GetType().Name.ToUpperInvariant();
				throw new InvalidOperationException(
					$"No matching signature for function ARRAY_TO_STRING\n" +
					$"  Argument types: ARRAY<{elementType}>, STRING\n" +
					$"  Signature: ARRAY_TO_STRING(ARRAY<STRING>, STRING, [STRING])\n" +
					$"    Argument 1: Unable to coerce type ARRAY<{elementType}> to expected type ARRAY<STRING>");
			}

			if (func.Arguments.Count > 2)
			{
				var nullText = Convert.ToString(Evaluate(func.Arguments[2], row)) ?? "";
				return string.Join(sep, list.Select(x => x != null ? x.ToString() : nullText));
			}
			return string.Join(sep, list.Where(x => x != null).Select(x => x!.ToString()));
		}
		return null;
	}

	private object? EvalArrayFunc(FunctionCallExpr func, Dictionary<string, object?> row,
		Func<List<object?>, List<object?>> transform)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		if (v is IList<object?> list) return transform(list.ToList());
		return null;
	}

	private object? EvalGenerateArray(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var start = Evaluate(func.Arguments[0], row);
		var end = Evaluate(func.Arguments[1], row);
		if (start == null || end == null) return null;
		var stepVal = func.Arguments.Count > 2 ? Evaluate(func.Arguments[2], row) : null;
		if (func.Arguments.Count > 2 && stepVal == null) return null;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_array
		//   "Returns an error if any argument is a NaN."
		if (IsNaN(start) || IsNaN(end) || IsNaN(stepVal))
			throw new InvalidOperationException("GENERATE_ARRAY does not allow NaN arguments.");
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_array
		//   Supports INT64, NUMERIC, and FLOAT64 types.
		if (start is double || end is double || stepVal is double)
		{
			var s = Convert.ToDouble(start);
			var e = Convert.ToDouble(end);
			var step = stepVal != null ? Convert.ToDouble(stepVal) : 1.0;
			if (step == 0.0) throw new InvalidOperationException("GENERATE_ARRAY step cannot be 0.");
			var result = new List<object?>();
			if (step > 0) for (double i = s; i <= e; i += step) result.Add(i);
			else for (double i = s; i >= e; i += step) result.Add(i);
			return result;
		}
		if (start is decimal || end is decimal || stepVal is decimal)
		{
			var s = Convert.ToDecimal(start);
			var e = Convert.ToDecimal(end);
			var step = stepVal != null ? Convert.ToDecimal(stepVal) : 1m;
			if (step == 0m) throw new InvalidOperationException("GENERATE_ARRAY step cannot be 0.");
			var result = new List<object?>();
			if (step > 0) for (decimal i = s; i <= e; i += step) result.Add(i);
			else for (decimal i = s; i >= e; i += step) result.Add(i);
			return result;
		}
		{
			var step = stepVal != null ? Convert.ToInt64(stepVal) : 1L;
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#generate_array
			//   "Returns an error if step is 0."
			if (step == 0) throw new InvalidOperationException("GENERATE_ARRAY step cannot be 0.");
			var s = Convert.ToInt64(start);
			var e = Convert.ToInt64(end);
			var result = new List<object?>();
			if (step > 0) for (long i = s; i <= e; i += step) result.Add(i);
			else for (long i = s; i >= e; i += step) result.Add(i);
			return result;
		}
	}

	private object? EvalArrayIncludes(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var arr = Evaluate(func.Arguments[0], row);
		var val = Evaluate(func.Arguments[1], row);
		if (arr == null) return null;
		if (arr is IList<object?> list) return list.Any(x => Equals(x, val));
		return false;
	}

	// ──────────────────────────────────────────
	// JSON function helpers
	// ──────────────────────────────────────────

	private object? EvalJsonValue(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var json = Evaluate(func.Arguments[0], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value
		//   If no path is provided, defaults to "$" (root).
		var pathObj = func.Arguments.Count > 1 ? Evaluate(func.Arguments[1], row) : (object)"$";
		var path = Convert.ToString(pathObj);
		if (json == null || pathObj == null) return null;
		var elem = NavigateJsonPath(json, path!);
		if (elem is JsonElement je)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value
			//   "Extracts a JSON scalar value" — non-scalar (Object/Array) returns NULL.
			return je.ValueKind switch
			{
				JsonValueKind.String => je.GetString(),
				JsonValueKind.Number => je.GetRawText(),
				JsonValueKind.True => "true",
				JsonValueKind.False => "false",
				JsonValueKind.Null => null,
				_ => null // Objects and Arrays are not scalar → NULL
			};
		}
		return elem?.ToString();
	}

	private object? EvalJsonQuery(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var json = Evaluate(func.Arguments[0], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_query
		//   If no path is provided, defaults to "$" (root). NULL path → NULL.
		var pathObj = func.Arguments.Count > 1 ? Evaluate(func.Arguments[1], row) : (object)"$";
		var path = Convert.ToString(pathObj);
		if (json == null || pathObj == null) return null;
		var elem = NavigateJsonPath(json, path!);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_query
		// Spanner returns compact JSON without extra whitespace.
		if (elem is JsonElement je)
		{
			using var ms = new System.IO.MemoryStream();
			using (var writer = new Utf8JsonWriter(ms))
				je.WriteTo(writer);
			return System.Text.Encoding.UTF8.GetString(ms.ToArray());
		}
		return elem?.ToString();
	}

	private object? EvalJsonQueryArray(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var json = Evaluate(func.Arguments[0], row);
		var path = func.Arguments.Count > 1 ? Convert.ToString(Evaluate(func.Arguments[1], row)) : "$";
		if (json == null) return null;
		var elem = NavigateJsonPath(json, path!);
		if (elem is JsonElement je && je.ValueKind == JsonValueKind.Array)
		{
			return je.EnumerateArray().Select(e => (object?)e.GetRawText()).ToList();
		}
		return null;
	}

	private object? EvalJsonType(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var json = Evaluate(func.Arguments[0], row);
		if (json == null) return null;
		JsonElement je;
		if (json is JsonElement j) je = j;
		else je = JsonSerializer.Deserialize<JsonElement>(Convert.ToString(json)!);
		return je.ValueKind switch
		{
			JsonValueKind.Object => "object",
			JsonValueKind.Array => "array",
			JsonValueKind.String => "string",
			JsonValueKind.Number => "number",
			JsonValueKind.True or JsonValueKind.False => "boolean",
			JsonValueKind.Null => "null",
			_ => "unknown"
		};
	}

	private static object? NavigateJsonPath(object json, string path)
	{
		JsonElement elem;
		if (json is JsonElement j) elem = j;
		else
		{
			var str = Convert.ToString(json)!;
			try { elem = JsonSerializer.Deserialize<JsonElement>(str); }
			catch { return null; }
		}

		// Simple path navigation: $.key1.key2, $[0].key, $.arr[0]
		var parts = path.TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts)
		{
			// Extract bracket index if present, e.g. "[1]" or "arr[0]"
			var bracketPos = part.IndexOf('[');
			var propName = bracketPos >= 0 ? part.Substring(0, bracketPos) : part;
			string? bracketIndex = bracketPos >= 0 ? part.Substring(bracketPos + 1).TrimEnd(']') : null;

			// Navigate property name (if non-empty)
			if (propName.Length > 0)
			{
				if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(propName, out var prop))
					elem = prop;
				else if (int.TryParse(propName, out var idx) && elem.ValueKind == JsonValueKind.Array && idx < elem.GetArrayLength())
					elem = elem[idx];
				else
					return null;
			}

			// Navigate array index (if bracket was present)
			if (bracketIndex != null)
			{
				if (int.TryParse(bracketIndex, out var arrIdx) && elem.ValueKind == JsonValueKind.Array && arrIdx < elem.GetArrayLength())
					elem = elem[arrIdx];
				else
					return null;
			}
		}
		return elem;
	}

	private object? EvalGetNextSequenceValue(FunctionCallExpr func)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#get_next_sequence_value
		var seqName = ExtractSequenceName(func);
		if (_queryExecutor?.Schema.TryGetSequence(seqName, out var seq) == true && seq != null)
			return seq.GetNextValue();
		throw new InvalidOperationException($"Sequence '{seqName}' not found.");
	}

	private object? EvalGetInternalSequenceState(FunctionCallExpr func)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators#get_internal_sequence_state
		var seqName = ExtractSequenceName(func);
		if (_queryExecutor?.Schema.TryGetSequence(seqName, out var seq) == true && seq != null)
			return seq.GetInternalState();
		throw new InvalidOperationException($"Sequence '{seqName}' not found.");
	}

	private static string ExtractSequenceName(FunctionCallExpr func)
	{
		if (func.Arguments.Count < 1)
			throw new InvalidOperationException($"{func.Name} requires a SEQUENCE argument.");
		// The argument is parsed as a column reference (SEQUENCE name)
		if (func.Arguments[0] is ColumnRefExpr col)
			return col.Column;
		throw new InvalidOperationException($"{func.Name}: expected sequence name argument.");
	}

	// ═══════════════════════════════════════════════════════════════
	// Hash Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/hash_functions
	// ═══════════════════════════════════════════════════════════════

	private object? EvalHash(FunctionCallExpr func, Dictionary<string, object?> row,
		Func<byte[], byte[]> hashFunc)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException($"{func.Name} requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		byte[] input = val is byte[] bytes ? bytes : System.Text.Encoding.UTF8.GetBytes(val.ToString()!);
		return hashFunc(input);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/hash_functions#farm_fingerprint
	//   "Computes the fingerprint of the STRING or BYTES input using the Fingerprint64 function."
	private object? EvalFarmFingerprint(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("FARM_FINGERPRINT requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		byte[] input = val is byte[] bytes ? bytes : System.Text.Encoding.UTF8.GetBytes(val.ToString()!);
		// Simple FarmHash fingerprint64 approximation using FNV-1a (not bit-for-bit identical to FarmHash)
		ulong hash = 14695981039346656037;
		foreach (byte b in input)
		{
			hash ^= b;
			hash *= 1099511628211;
		}
		return unchecked((long)hash);
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional Array Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
	// ═══════════════════════════════════════════════════════════════

	private object? EvalArrayFirst(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("ARRAY_FIRST requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
		//   Scalar functions return NULL when any argument is NULL.
		if (val == null) return null;
		if (val is not System.Collections.IList list || list.Count == 0)
			throw new InvalidOperationException("ARRAY_FIRST: empty or non-array argument.");
		return list[0];
	}

	private object? EvalArrayLast(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("ARRAY_LAST requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions
		//   Scalar functions return NULL when any argument is NULL.
		if (val == null) return null;
		if (val is not System.Collections.IList list || list.Count == 0)
			throw new InvalidOperationException("ARRAY_LAST: empty or non-array argument.");
		return list[list.Count - 1];
	}

	private object? EvalArrayMinMax(FunctionCallExpr func, Dictionary<string, object?> row, bool isMin)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException($"{func.Name} requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not System.Collections.IList list) throw new InvalidOperationException($"{func.Name}: non-array argument.");
		object? result = null;
		foreach (var item in list)
		{
			if (item == null) continue;
			if (result == null) { result = item; continue; }
			var cmp = Comparer<object>.Default.Compare(item, result);
			if (isMin ? cmp < 0 : cmp > 0) result = item;
		}
		return result;
	}

	private object? EvalArrayAggregate(FunctionCallExpr func, Dictionary<string, object?> row, string op)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException($"{func.Name} requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not System.Collections.IList list) throw new InvalidOperationException($"{func.Name}: non-array argument.");
		double sum = 0;
		int count = 0;
		foreach (var item in list)
		{
			if (item == null) continue;
			sum += Convert.ToDouble(item);
			count++;
		}
		if (count == 0) return null;
		return op == "AVG" ? sum / count : (object)(long)sum;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_slice
	//   "Returns an array that is a subsequence of the input array."
	private object? EvalArraySlice(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 3)
			throw new InvalidOperationException("ARRAY_SLICE requires 3 arguments: array, start, end.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not System.Collections.IList list)
			throw new InvalidOperationException("ARRAY_SLICE: first argument must be an array.");
		var startVal = Evaluate(func.Arguments[1], row);
		var endVal = Evaluate(func.Arguments[2], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_slice
		//   Scalar functions return NULL when any argument is NULL.
		if (startVal == null || endVal == null) return null;
		var start = Convert.ToInt32(startVal);
		var end = Convert.ToInt32(endVal);
		// Spanner uses 0-based indexing; negative wraps from end
		if (start < 0) start = list.Count + start;
		if (end < 0) end = list.Count + end;
		start = Math.Max(0, start);
		end = Math.Min(list.Count - 1, end);
		var result = new List<object?>();
		for (int i = start; i <= end; i++)
			result.Add(list[i]);
		return result;
	}

	private object? EvalArrayIncludesAnyAll(FunctionCallExpr func, Dictionary<string, object?> row, bool any)
	{
		if (func.Arguments.Count != 2)
			throw new InvalidOperationException($"{func.Name} requires 2 arguments.");
		var haystack = Evaluate(func.Arguments[0], row);
		var needles = Evaluate(func.Arguments[1], row);
		if (haystack == null || needles == null) return null;
		if (haystack is not System.Collections.IList hList || needles is not System.Collections.IList nList)
			throw new InvalidOperationException($"{func.Name}: both arguments must be arrays.");
		var set = new HashSet<object?>();
		foreach (var item in hList) set.Add(item);
		foreach (var needle in nList)
		{
			bool found = set.Contains(needle);
			if (any && found) return true;
			if (!any && !found) return false;
		}
		return !any; // ALL: all found => true; ANY: none found => false
	}

	private object? EvalArrayFilter(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// ARRAY_FILTER(array, lambda) — lambda not fully supported, treat as identity filter (remove nulls)
		if (func.Arguments.Count < 1)
			throw new InvalidOperationException("ARRAY_FILTER requires at least 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not System.Collections.IList list) throw new InvalidOperationException("ARRAY_FILTER: non-array argument.");
		var result = new List<object?>();
		foreach (var item in list)
			if (item != null) result.Add(item);
		return result;
	}

	private object? EvalArrayTransform(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// ARRAY_TRANSFORM(array, lambda) — lambda not fully supported, returns array as-is
		if (func.Arguments.Count < 1)
			throw new InvalidOperationException("ARRAY_TRANSFORM requires at least 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		return val;
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional Date/Time Functions
	// ═══════════════════════════════════════════════════════════════

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#generate_date_array
	//   "Generates an array of dates in a range."
	private object? EvalGenerateDateArray(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 2)
			throw new InvalidOperationException("GENERATE_DATE_ARRAY requires at least 2 arguments.");
		var startVal = Evaluate(func.Arguments[0], row);
		var endVal = Evaluate(func.Arguments[1], row);
		if (startVal == null || endVal == null) return null;
		var startDate = ConvertToDateTime(startVal).Date;
		var endDate = ConvertToDateTime(endVal).Date;
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#generate_date_array
		//   Step is an INTERVAL value supporting DAY, WEEK, MONTH, QUARTER, YEAR.
		string stepPart = "DAY";
		int stepAmount = 1;
		if (func.Arguments.Count >= 3)
		{
			var stepVal = Evaluate(func.Arguments[2], row);
			if (stepVal is SpannerInterval interval)
			{
				if (interval.Months != 0) { stepPart = "MONTH"; stepAmount = interval.Months; }
				else if (interval.Days != 0) { stepPart = "DAY"; stepAmount = interval.Days; }
				else stepAmount = 1;
			}
			else if (stepVal != null) stepAmount = Convert.ToInt32(stepVal);
		}
		if (stepAmount == 0) throw new InvalidOperationException("GENERATE_DATE_ARRAY: step cannot be 0.");
		var result = new List<object?>();
		DateTime StepDate(DateTime d) => stepPart == "MONTH" ? d.AddMonths(stepAmount) : d.AddDays(stepAmount);
		if (stepAmount > 0)
		{
			for (var d = startDate; d <= endDate; d = StepDate(d))
				result.Add(d);
		}
		else
		{
			for (var d = startDate; d >= endDate; d = StepDate(d))
				result.Add(d);
		}
		return result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#generate_timestamp_array
	//   "Generates an array of timestamps in a range."
	private object? EvalGenerateTimestampArray(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 3)
			throw new InvalidOperationException("GENERATE_TIMESTAMP_ARRAY requires at least 3 arguments: start, end, interval.");
		var startVal = Evaluate(func.Arguments[0], row);
		var endVal = Evaluate(func.Arguments[1], row);
		var stepVal = Evaluate(func.Arguments[2], row);
		if (startVal == null || endVal == null) return null;
		var startTs = ConvertToDateTime(startVal);
		var endTs = ConvertToDateTime(endVal);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/timestamp_functions#generate_timestamp_array
		//   Step is an INTERVAL supporting MICROSECOND through DAY.
		long stepNanos = 3_600_000_000_000L; // default 1 hour
		if (stepVal is SpannerInterval interval)
		{
			stepNanos = interval.Nanos + interval.Days * 86_400_000_000_000L;
		}
		else if (stepVal != null)
		{
			stepNanos = (long)(Convert.ToDouble(stepVal) * 3_600_000_000_000.0);
		}
		if (stepNanos == 0) throw new InvalidOperationException("GENERATE_TIMESTAMP_ARRAY: step cannot be 0.");
		var stepTicks = stepNanos / 100; // 1 tick = 100 ns
		var result = new List<object?>();
		if (stepTicks > 0)
		{
			for (var t = startTs; t <= endTs; t = t.AddTicks(stepTicks))
				result.Add(t);
		}
		else
		{
			for (var t = startTs; t >= endTs; t = t.AddTicks(stepTicks))
				result.Add(t);
		}
		return result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#unix_date
	//   "Returns the number of days since 1970-01-01."
	private object? EvalUnixDate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("UNIX_DATE requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var date = ConvertToDateTime(val).Date;
		return (long)(date - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/date_functions#date_from_unix_date
	//   "Interprets an INT64 expression as the number of days since 1970-01-01."
	private object? EvalDateFromUnixDate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("DATE_FROM_UNIX_DATE requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var days = Convert.ToInt64(val);
		return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(days);
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional String Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
	// ═══════════════════════════════════════════════════════════════

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#normalize
	//   "Returns a string with all characters in Unicode Normal Form."
	private object? EvalNormalize(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 1)
			throw new InvalidOperationException("NORMALIZE requires at least 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var form = System.Text.NormalizationForm.FormC;
		if (func.Arguments.Count >= 2)
		{
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#normalize
			//   The second argument is a normalization mode identifier (NFC, NFD, NFKC, NFKD),
			//   not a string literal. The parser treats it as a ColumnRefExpr, but we resolve
			//   it directly by name since it's a keyword-like token.
			string? formStr;
			if (func.Arguments[1] is ColumnRefExpr colRef)
				formStr = colRef.Column.ToUpperInvariant();
			else
				formStr = Evaluate(func.Arguments[1], row)?.ToString()?.ToUpperInvariant();
			form = formStr switch
			{
				"NFC" => System.Text.NormalizationForm.FormC,
				"NFD" => System.Text.NormalizationForm.FormD,
				"NFKC" => System.Text.NormalizationForm.FormKC,
				"NFKD" => System.Text.NormalizationForm.FormKD,
				_ => System.Text.NormalizationForm.FormC
			};
		}
		return val.ToString()!.Normalize(form);
	}

	private object? EvalNormalizeAndCasefold(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var normalized = EvalNormalize(func, row);
		return normalized?.ToString()?.ToLowerInvariant();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#to_code_points
	//   "Converts a STRING to an ARRAY of INT64 Unicode code points."
	private object? EvalToCodePoints(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("TO_CODE_POINTS requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is byte[] bytes)
			return bytes.Select(b => (object)(long)b).ToList();
		var str = val.ToString()!;
		var result = new List<object?>();
		for (int i = 0; i < str.Length; i++)
		{
			if (char.IsHighSurrogate(str[i]) && i + 1 < str.Length && char.IsLowSurrogate(str[i + 1]))
			{
				result.Add((long)char.ConvertToUtf32(str[i], str[i + 1]));
				i++;
			}
			else
			{
				result.Add((long)str[i]);
			}
		}
		return result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#code_points_to_bytes
	//   "Converts an ARRAY of INT64 to BYTES."
	private object? EvalCodePointsToBytes(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("CODE_POINTS_TO_BYTES requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not System.Collections.IList list) throw new InvalidOperationException("CODE_POINTS_TO_BYTES: argument must be an array.");
		var bytes = new byte[list.Count];
		for (int i = 0; i < list.Count; i++)
			bytes[i] = (byte)Convert.ToInt64(list[i]!);
		return bytes;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_extract_all
	//   "Returns an array of all substrings that match the regular expression."
	private object? EvalRegexpExtractAll(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 2)
			throw new InvalidOperationException("REGEXP_EXTRACT_ALL requires exactly 2 arguments.");
		var val = Evaluate(func.Arguments[0], row);
		var pattern = Evaluate(func.Arguments[1], row);
		if (val == null || pattern == null) return null;
		var matches = Regex.Matches(val.ToString()!, pattern.ToString()!);
		var result = new List<object?>();
		foreach (Match m in matches)
		{
			result.Add(m.Groups.Count > 1 ? m.Groups[1].Value : m.Value);
		}
		return result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_instr
	//   "Returns the 1-based position of the first occurrence of the pattern."
	private object? EvalRegexpInstr(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 2)
			throw new InvalidOperationException("REGEXP_INSTR requires at least 2 arguments.");
		var val = Evaluate(func.Arguments[0], row);
		var pattern = Evaluate(func.Arguments[1], row);
		if (val == null || pattern == null) return null;
		int startPos = func.Arguments.Count >= 3 ? Convert.ToInt32(Evaluate(func.Arguments[2], row)) : 1;
		var str = val.ToString()!;
		if (startPos < 1 || startPos > str.Length) return 0L;
		var match = Regex.Match(str.Substring(startPos - 1), pattern.ToString()!);
		return match.Success ? (long)(match.Index + startPos) : 0L;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#octet_length
	//   "Returns the number of bytes in the string."
	private object? EvalOctetLength(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("OCTET_LENGTH requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is byte[] bytes) return (long)bytes.Length;
		return (long)System.Text.Encoding.UTF8.GetByteCount(val.ToString()!);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#safe_convert_bytes_to_string
	//   "Converts BYTES to STRING replacing invalid UTF-8 with U+FFFD."
	private object? EvalSafeConvertBytesToString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("SAFE_CONVERT_BYTES_TO_STRING requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not byte[] bytes) return val.ToString();
		return System.Text.Encoding.UTF8.GetString(bytes);
	}

	// ═══════════════════════════════════════════════════════════════
	// Additional Math / Bit Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions
	// ═══════════════════════════════════════════════════════════════

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions#bit_count
	//   "Returns the number of bits that are set in the input expression."
	private object? EvalBitCount(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException("BIT_COUNT requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is byte[] bytes)
		{
			long count = 0;
			foreach (var b in bytes) count += long.PopCount(b);
			return count;
		}
		var num = Convert.ToInt64(val);
		return (long)ulong.PopCount(unchecked((ulong)num));
	}

	private static DateTime ConvertToDateTime(object value) => value switch
	{
		DateTime dt => dt,
		DateTimeOffset dto => dto.UtcDateTime,
		string s => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal),
		_ => throw new InvalidOperationException($"Cannot convert {value.GetType().Name} to DateTime.")
	};

	// ═══════════════════════════════════════════════════════════════
	// Trigonometric Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions
	// ═══════════════════════════════════════════════════════════════

	private object? EvalTrig(FunctionCallExpr func, Dictionary<string, object?> row, Func<double, double> fn)
	{
		if (func.Arguments.Count != 1)
			throw new InvalidOperationException($"{func.Name} requires exactly 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		return fn(Convert.ToDouble(val));
	}

	private object? EvalTrig2(FunctionCallExpr func, Dictionary<string, object?> row, Func<double, double, double> fn)
	{
		if (func.Arguments.Count != 2)
			throw new InvalidOperationException($"{func.Name} requires exactly 2 arguments.");
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a == null || b == null) return null;
		return fn(Convert.ToDouble(a), Convert.ToDouble(b));
	}

	// ═══════════════════════════════════════════════════════════════
	// Base64 / Base32 Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions
	// ═══════════════════════════════════════════════════════════════

	private object? EvalFromBase64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("FROM_BASE64 requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		return Convert.FromBase64String(val.ToString()!);
	}

	private object? EvalToBase64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("TO_BASE64 requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is byte[] bytes) return Convert.ToBase64String(bytes);
		return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(val.ToString()!));
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#from_base32
	//   Base32 uses A-Z, 2-7
	private object? EvalFromBase32(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("FROM_BASE32 requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		return Base32Decode(val.ToString()!);
	}

	private object? EvalToBase32(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("TO_BASE32 requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is byte[] bytes) return Base32Encode(bytes);
		return Base32Encode(System.Text.Encoding.UTF8.GetBytes(val.ToString()!));
	}

	private static string Base32Encode(byte[] data)
	{
		const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
		var sb = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
		int bits = 0, buffer = 0;
		foreach (var b in data)
		{
			buffer = (buffer << 8) | b;
			bits += 8;
			while (bits >= 5) { bits -= 5; sb.Append(alphabet[(buffer >> bits) & 0x1F]); }
		}
		if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 0x1F]);
		while (sb.Length % 8 != 0) sb.Append('=');
		return sb.ToString();
	}

	private static byte[] Base32Decode(string encoded)
	{
		const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
		encoded = encoded.TrimEnd('=').ToUpperInvariant();
		var output = new List<byte>();
		int bits = 0, buffer = 0;
		foreach (var c in encoded)
		{
			var idx = alphabet.IndexOf(c);
			if (idx < 0) continue;
			buffer = (buffer << 5) | idx;
			bits += 5;
			if (bits >= 8) { bits -= 8; output.Add((byte)(buffer >> bits)); }
		}
		return output.ToArray();
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON Conversion Functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
	// ═══════════════════════════════════════════════════════════════

	private object? EvalJsonToBool(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#bool
		//   "Converts a JSON boolean to a SQL BOOL value. If the JSON value is not a boolean, an error is produced."
		if (func.Arguments.Count != 1) throw new InvalidOperationException("BOOL requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var s = val.ToString()!.Trim().Trim('"');
		if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
		if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
		throw new InvalidOperationException($"BOOL: cannot convert JSON value '{s}' to BOOL");
	}

	private object? EvalJsonToInt64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException($"{func.Name} requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var s = val.ToString()!.Trim().Trim('"');
		return long.Parse(s);
	}

	private object? EvalJsonToFloat64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException($"{func.Name} requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var s = val.ToString()!.Trim().Trim('"');
		return double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
	}

	private object? EvalJsonToString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("STRING requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var s = val.ToString()!;
		// Remove surrounding quotes if present (JSON string)
		if (s.StartsWith('"') && s.EndsWith('"') && s.Length >= 2)
			return s.Substring(1, s.Length - 2);
		return s;
	}

	private object? EvalJsonArray(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var elements = func.Arguments.Select(a => Evaluate(a, row)).ToList();
		var jsonElements = elements.Select(e => e == null ? "null" : JsonSerializer.Serialize(e));
		var json = "[" + string.Join(",", jsonElements) + "]";
		return JsonSerializer.Deserialize<JsonElement>(json);
	}

	private object? EvalJsonObject(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count % 2 != 0)
			throw new InvalidOperationException("JSON_OBJECT requires an even number of arguments (key/value pairs).");
		var sb = new System.Text.StringBuilder("{");
		for (int i = 0; i < func.Arguments.Count; i += 2)
		{
			if (i > 0) sb.Append(',');
			var key = Evaluate(func.Arguments[i], row)?.ToString() ?? "";
			var val = Evaluate(func.Arguments[i + 1], row);
			sb.Append(JsonSerializer.Serialize(key));
			sb.Append(':');
			sb.Append(val == null ? "null" : JsonSerializer.Serialize(val));
		}
		sb.Append('}');
		return JsonSerializer.Deserialize<JsonElement>(sb.ToString());
	}

	private object? EvalJsonSet(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 3 || func.Arguments.Count % 2 != 1)
			throw new InvalidOperationException("JSON_SET requires json, path, value triples.");
		var rawJson = Evaluate(func.Arguments[0], row);
		if (rawJson == null) return null;
		var json = rawJson is JsonElement je0 ? je0.GetRawText() : rawJson.ToString()!;
		using var doc = JsonDocument.Parse(json);
		var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, new JsonSerializerOptions { Converters = { new NestedDictionaryConverter() } }) ?? new();
		for (int i = 1; i < func.Arguments.Count; i += 2)
		{
			var pathStr = Evaluate(func.Arguments[i], row)?.ToString() ?? "$";
			var segments = pathStr.TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries);
			var val = Evaluate(func.Arguments[i + 1], row);
			var serializedVal = (object?)JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(val));

			if (segments.Length == 0) continue;

			var target = dict;
			for (int s = 0; s < segments.Length - 1; s++)
			{
				if (target.TryGetValue(segments[s], out var nested) && nested is Dictionary<string, object?> nestedDict)
					target = nestedDict;
				else
				{
					var newDict = new Dictionary<string, object?>();
					target[segments[s]] = newDict;
					target = newDict;
				}
			}

			target[segments[^1]] = serializedVal;
		}
		return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(dict));
	}

	private sealed class NestedDictionaryConverter : JsonConverter<Dictionary<string, object?>>
	{
		public override Dictionary<string, object?>? Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
		{
			var dict = new Dictionary<string, object?>();
			if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
			while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
			{
				var key = reader.GetString()!;
				reader.Read();
				dict[key] = ReadValue(ref reader, options);
			}
			return dict;
		}

		private object? ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options) => reader.TokenType switch
		{
			JsonTokenType.StartObject => Read(ref reader, typeof(Dictionary<string, object?>), options),
			_ => JsonSerializer.Deserialize<JsonElement>(ref reader)
		};

		public override void Write(Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();
			foreach (var kvp in value)
			{
				writer.WritePropertyName(kvp.Key);
				JsonSerializer.Serialize(writer, kvp.Value, options);
			}
			writer.WriteEndObject();
		}
	}

	private object? EvalJsonStripNulls(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("JSON_STRIP_NULLS requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var jsonStr = val is JsonElement jeStrip ? jeStrip.GetRawText() : val.ToString()!;
		using var doc = JsonDocument.Parse(jsonStr);
		var stripped = StripNulls(doc.RootElement);
		return JsonSerializer.Deserialize<JsonElement>(stripped);
	}

	private static string StripNulls(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Object => "{" + string.Join(",",
			element.EnumerateObject()
				.Where(p => p.Value.ValueKind != JsonValueKind.Null)
				.Select(p => $"\"{p.Name}\":{StripNulls(p.Value)}")) + "}",
		JsonValueKind.Array => "[" + string.Join(",",
			element.EnumerateArray().Select(StripNulls)) + "]",
		_ => element.GetRawText()
	};

	private object? EvalJsonKeys(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 1) throw new InvalidOperationException("JSON_KEYS requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var jsonStr = val is JsonElement jeKeys ? jeKeys.GetRawText() : val.ToString()!;
		using var doc = JsonDocument.Parse(jsonStr);
		if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
		return doc.RootElement.EnumerateObject().Select(p => (object?)p.Name).ToList();
	}

	// LAX conversion functions — return NULL on conversion failure
	private object? EvalLaxBool(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		try { return EvalJsonToBool(func, row); } catch { return null; }
	}

	private object? EvalLaxInt64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		try { return EvalJsonToInt64(func, row); } catch { return null; }
	}

	private object? EvalLaxFloat64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		try { return EvalJsonToFloat64(func, row); } catch { return null; }
	}

	private object? EvalLaxString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		try { return EvalJsonToString(func, row); } catch { return null; }
	}

	// ═══════════════════════════════════════════════════════════════
	// Array utility
	// ═══════════════════════════════════════════════════════════════

	private object? EvalArrayIsDistinct(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("ARRAY_IS_DISTINCT requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not System.Collections.IList list) throw new InvalidOperationException("ARRAY_IS_DISTINCT: non-array argument.");
		var set = new HashSet<object?>();
		foreach (var item in list)
			if (!set.Add(item?.ToString())) return false;
		return true;
	}

	// ═══════════════════════════════════════════════════════════════
	// Vector distance functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions
	// ═══════════════════════════════════════════════════════════════

	private object? EvalCosineDistance(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosine_distance
		//   "A vector can't be a zero vector … If a zero vector is encountered, an error is produced."
		var (a, b) = GetVectorArgs(func, row);
		if (a == null || b == null) return null;
		double dot = 0, magA = 0, magB = 0;
		for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; magA += a[i] * a[i]; magB += b[i] * b[i]; }
		if (magA == 0 || magB == 0)
			throw new InvalidOperationException($"{func.Name}: zero vector is not allowed for cosine distance.");
		var sim = dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
		return 1.0 - sim;
	}

	private object? EvalEuclideanDistance(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#euclidean_distance
		var (a, b) = GetVectorArgs(func, row);
		if (a == null || b == null) return null;
		double sum = 0;
		for (int i = 0; i < a.Length; i++) { var d = a[i] - b[i]; sum += d * d; }
		return Math.Sqrt(sum);
	}

	private object? EvalDotProduct(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#dot_product
		var (a, b) = GetVectorArgs(func, row);
		if (a == null || b == null) return null;
		double dot = 0;
		for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
		return dot;
	}

	private (double[]?, double[]?) GetVectorArgs(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Extract positional (non-named) arguments; named args like options=> are accepted but ignored.
		var positional = EvalPositionalArgs(func, row);
		if (positional.Count < 2) throw new InvalidOperationException($"{func.Name} requires 2 vector arguments.");
		var v1 = positional[0];
		var v2 = positional[1];
		if (v1 == null || v2 == null) return (null, null);
		double[] ToArray(object val)
		{
			if (val is not System.Collections.IList list)
				throw new InvalidOperationException($"{func.Name}: arguments must be arrays.");
			// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/mathematical_functions#cosine_distance
			//   "An error is produced if a magnitude in a vector is NULL."
			var arr = new double[list.Count];
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i] == null)
					throw new InvalidOperationException($"{func.Name}: NULL element in vector is not allowed.");
				arr[i] = Convert.ToDouble(list[i]);
			}
			return arr;
		}
		var a = ToArray(v1);
		var b = ToArray(v2);
		// Ref: "Both vectors in this function must share the same dimensions, and if they don't, an error is produced."
		if (a.Length != b.Length) throw new InvalidOperationException($"{func.Name}: vectors must have the same dimensions (got {a.Length} and {b.Length}).");
		return (a, b);
	}

	// ═══════════════════════════════════════════════════════════════
	// NET functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions
	// ═══════════════════════════════════════════════════════════════

	private object? EvalNetHost(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.HOST requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var url = val.ToString()!;
		if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
		// Try parsing as just a host
		if (Uri.TryCreate("http://" + url, UriKind.Absolute, out uri)) return uri.Host;
		return null;
	}

	private object? EvalNetRegDomain(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.REG_DOMAIN requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var host = val.ToString()!;
		var parts = host.Split('.');
		return parts.Length >= 2 ? string.Join(".", parts.TakeLast(2)) : host;
	}

	private object? EvalNetPublicSuffix(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.PUBLIC_SUFFIX requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var host = val.ToString()!;
		var parts = host.Split('.');
		return parts.Length >= 1 ? parts[^1] : host;
	}

	private object? EvalNetIpFromString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.IP_FROM_STRING requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (!System.Net.IPAddress.TryParse(val.ToString()!, out var ip))
			throw new InvalidOperationException($"Invalid IP address: {val}");
		return ip.GetAddressBytes();
	}

	private object? EvalNetIpToString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.IP_TO_STRING requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is byte[] bytes) return new System.Net.IPAddress(bytes).ToString();
		throw new InvalidOperationException("NET.IP_TO_STRING requires BYTES argument.");
	}

	private object? EvalNetSafeIpFromString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.SAFE_IP_FROM_STRING requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (!System.Net.IPAddress.TryParse(val.ToString()!, out var ip)) return null;
		return ip.GetAddressBytes();
	}

	// ═══════════════════════════════════════════════════════════════
	// NET.IP_NET_MASK
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netip_net_mask
	//   Returns a network mask: a BYTES value with `prefix_length` leading 1-bits.
	//   `output_length` is 4 (IPv4) or 16 (IPv6).
	// ═══════════════════════════════════════════════════════════════

	private object? EvalNetIpNetMask(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 2) throw new InvalidOperationException("NET.IP_NET_MASK requires 2 arguments.");
		var outputLength = Convert.ToInt32(Evaluate(func.Arguments[0], row) ?? throw new InvalidOperationException("NET.IP_NET_MASK: output_length cannot be NULL."));
		var prefixLength = Convert.ToInt32(Evaluate(func.Arguments[1], row) ?? throw new InvalidOperationException("NET.IP_NET_MASK: prefix_length cannot be NULL."));
		if (outputLength != 4 && outputLength != 16)
			throw new InvalidOperationException("NET.IP_NET_MASK: output_length must be 4 or 16.");
		if (prefixLength < 0 || prefixLength > outputLength * 8)
			throw new InvalidOperationException($"NET.IP_NET_MASK: prefix_length must be between 0 and {outputLength * 8}.");
		var bytes = new byte[outputLength];
		for (int i = 0; i < prefixLength; i++)
			bytes[i / 8] |= (byte)(0x80 >> (i % 8));
		return bytes;
	}

	// ═══════════════════════════════════════════════════════════════
	// NET.IP_TRUNC
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netip_trunc
	//   Truncates an IP address (BYTES) to the specified prefix length.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalNetIpTrunc(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 2) throw new InvalidOperationException("NET.IP_TRUNC requires 2 arguments.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not byte[] ipBytes) throw new InvalidOperationException("NET.IP_TRUNC: first argument must be BYTES.");
		var prefixLength = Convert.ToInt32(Evaluate(func.Arguments[1], row) ?? throw new InvalidOperationException("NET.IP_TRUNC: prefix_length cannot be NULL."));
		if (prefixLength < 0 || prefixLength > ipBytes.Length * 8)
			throw new InvalidOperationException($"NET.IP_TRUNC: prefix_length must be between 0 and {ipBytes.Length * 8}.");
		var result = new byte[ipBytes.Length];
		Array.Copy(ipBytes, result, ipBytes.Length);
		// Zero out bits after prefix_length
		for (int i = prefixLength; i < result.Length * 8; i++)
			result[i / 8] &= (byte)~(0x80 >> (i % 8));
		return result;
	}

	// ═══════════════════════════════════════════════════════════════
	// NET.IPV4_FROM_INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netipv4_from_int64
	//   Converts an IPv4 address from an INT64 value to BYTES in network byte order.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalNetIpv4FromInt64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.IPV4_FROM_INT64 requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var intVal = Convert.ToInt64(val);
		if (intVal < 0 || intVal > 0xFFFFFFFFL)
			throw new InvalidOperationException($"NET.IPV4_FROM_INT64: value {intVal} is out of range for IPv4.");
		var bytes = BitConverter.GetBytes((uint)intVal);
		if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
		return bytes;
	}

	// ═══════════════════════════════════════════════════════════════
	// NET.IPV4_TO_INT64
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/net_functions#netipv4_to_int64
	//   Converts an IPv4 address from BYTES in network byte order to INT64.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalNetIpv4ToInt64(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException("NET.IPV4_TO_INT64 requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		if (val is not byte[] bytes || bytes.Length != 4)
			throw new InvalidOperationException("NET.IPV4_TO_INT64 requires a 4-byte BYTES value.");
		var copy = (byte[])bytes.Clone();
		if (BitConverter.IsLittleEndian) Array.Reverse(copy);
		return (long)BitConverter.ToUInt32(copy, 0);
	}

	// ═══════════════════════════════════════════════════════════════
	// SPLIT_SUBSTR
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#split_substr
	//   Returns a substring determined by a delimiter, start_split, and optional count.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalSplitSubstr(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 3 || func.Arguments.Count > 4)
			throw new InvalidOperationException("SPLIT_SUBSTR requires 3 or 4 arguments.");
		var value = Evaluate(func.Arguments[0], row)?.ToString();
		if (value == null) return null;
		var delimiter = Evaluate(func.Arguments[1], row)?.ToString()
			?? throw new InvalidOperationException("SPLIT_SUBSTR: delimiter cannot be NULL.");
		var startSplit = Convert.ToInt32(Evaluate(func.Arguments[2], row)
			?? throw new InvalidOperationException("SPLIT_SUBSTR: start_split cannot be NULL."));
		int? count = func.Arguments.Count > 3
			? Convert.ToInt32(Evaluate(func.Arguments[3], row)
				?? throw new InvalidOperationException("SPLIT_SUBSTR: count cannot be NULL."))
			: null;
		if (count.HasValue && count.Value < 0)
			throw new InvalidOperationException("SPLIT_SUBSTR: count cannot be negative.");
		if (count.HasValue && count.Value == 0) return "";

		// Split the value
		var parts = new List<string>();
		int pos = 0;
		while (pos <= value.Length)
		{
			int next = delimiter.Length > 0 ? value.IndexOf(delimiter, pos, StringComparison.Ordinal) : -1;
			if (next < 0)
			{
				parts.Add(value[pos..]);
				break;
			}
			parts.Add(value[pos..next]);
			pos = next + delimiter.Length;
		}

		int totalSplits = parts.Count;
		// Normalize startSplit: 0 or less than -totalSplits → 1
		if (startSplit == 0 || startSplit < -totalSplits) startSplit = 1;
		// Negative: count from end
		if (startSplit < 0) startSplit = totalSplits + startSplit + 1;
		// If start > total, return empty
		if (startSplit > totalSplits) return "";

		int startIdx = startSplit - 1; // 0-based
		int takeCount = count ?? (totalSplits - startIdx);
		takeCount = Math.Min(takeCount, totalSplits - startIdx);
		var selected = parts.Skip(startIdx).Take(takeCount);
		return string.Join(delimiter, selected);
	}

	// ═══════════════════════════════════════════════════════════════
	// BIT_REVERSE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/bit_functions#bit_reverse
	//   Reverses the bits of the input integer, treating it as `bit_count` wide.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalBitReverse(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 2) throw new InvalidOperationException("BIT_REVERSE requires 2 arguments.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		var intVal = Convert.ToInt64(val);
		var width = Convert.ToBoolean(Evaluate(func.Arguments[1], row)
			?? throw new InvalidOperationException("BIT_REVERSE: bit_count cannot be NULL."));
		// The second arg indicates whether to keep the sign bit (true = keep as INT64)
		ulong unsigned = (ulong)intVal;
		ulong reversed = 0;
		for (int i = 0; i < 64; i++)
		{
			reversed <<= 1;
			reversed |= (unsigned & 1);
			unsigned >>= 1;
		}
		return (long)reversed;
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_VALUE_ARRAY
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_value_array
	//   Extracts a JSON array and converts it to ARRAY<STRING>.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalJsonValueArray(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 1 || func.Arguments.Count > 2)
			throw new InvalidOperationException("JSON_VALUE_ARRAY requires 1 or 2 arguments.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		string jsonStr = val.ToString()!;
		string? path = func.Arguments.Count > 1 ? Evaluate(func.Arguments[1], row)?.ToString() : null;

		var navigated = path != null ? NavigateJsonPath(jsonStr, path) : null;
		JsonElement element;
		if (navigated is JsonElement je)
			element = je;
		else if (path == null)
		{
			using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
			element = doc.RootElement.Clone();
		}
		else
			return null;

		if (element.ValueKind != JsonValueKind.Array) return null;

		var result = new List<object?>();
		foreach (var item in element.EnumerateArray())
		{
			result.Add(item.ValueKind switch
			{
				JsonValueKind.String => item.GetString(),
				JsonValueKind.Number => item.GetRawText(),
				JsonValueKind.True => "true",
				JsonValueKind.False => "false",
				JsonValueKind.Null => null,
				_ => item.GetRawText()
			});
		}
		return result;
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_ARRAY_APPEND
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_append
	//   Appends JSON data to the end of a JSON array.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalJsonArrayAppend(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_append
		//   Signature: JSON_ARRAY_APPEND(json_expr, json_path STRING, value ANY, [[json_path STRING, value ANY], ...])
		if (func.Arguments.Count < 3 || func.Arguments.Count % 2 == 0)
			throw new InvalidOperationException("JSON_ARRAY_APPEND requires an odd number of arguments >= 3 (json, path, value, ...).");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;

		string currentJson = val is JsonElement je0 ? je0.GetRawText() : val.ToString()!;

		// Process path/value pairs, applying each to the result of the previous
		for (int i = 1; i < func.Arguments.Count; i += 2)
		{
			var path = Evaluate(func.Arguments[i], row)?.ToString() ?? "$";
			var appendVal = Evaluate(func.Arguments[i + 1], row);
			currentJson = JsonArrayAppendAtPath(currentJson, path, appendVal);
		}

		return JsonSerializer.Deserialize<JsonElement>(currentJson);
	}

	private string JsonArrayAppendAtPath(string jsonStr, string path, object? appendVal)
	{
		using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);

		if (path == "$")
		{
			if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
				throw new InvalidOperationException("JSON_ARRAY_APPEND: target must be an array.");
			using var stream = new System.IO.MemoryStream();
			using var writer = new System.Text.Json.Utf8JsonWriter(stream);
			writer.WriteStartArray();
			foreach (var item in doc.RootElement.EnumerateArray())
				item.WriteTo(writer);
			WriteJsonValue(writer, appendVal);
			writer.WriteEndArray();
			writer.Flush();
			return System.Text.Encoding.UTF8.GetString(stream.ToArray());
		}

		// Handle $.property path — navigate into object and append to the nested array
		var propMatch = System.Text.RegularExpressions.Regex.Match(path, @"^\$\.(.+)$");
		if (propMatch.Success && doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
		{
			var propName = propMatch.Groups[1].Value;
			using var stream = new System.IO.MemoryStream();
			using var writer = new System.Text.Json.Utf8JsonWriter(stream);
			writer.WriteStartObject();
			foreach (var prop in doc.RootElement.EnumerateObject())
			{
				if (prop.Name == propName && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
				{
					writer.WritePropertyName(prop.Name);
					writer.WriteStartArray();
					foreach (var arrItem in prop.Value.EnumerateArray())
						arrItem.WriteTo(writer);
					WriteJsonValue(writer, appendVal);
					writer.WriteEndArray();
				}
				else
				{
					prop.WriteTo(writer);
				}
			}
			writer.WriteEndObject();
			writer.Flush();
			return System.Text.Encoding.UTF8.GetString(stream.ToArray());
		}

		throw new InvalidOperationException($"JSON_ARRAY_APPEND: unsupported path '{path}'.");
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_ARRAY_INSERT
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_array_insert
	//   Inserts JSON data into a JSON array.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalJsonArrayInsert(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 3) throw new InvalidOperationException("JSON_ARRAY_INSERT requires at least 3 arguments.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;
		string jsonStr = val is JsonElement je1 ? je1.GetRawText() : val.ToString()!;
		var path = Evaluate(func.Arguments[1], row)?.ToString()
			?? throw new InvalidOperationException("JSON_ARRAY_INSERT: path cannot be NULL.");
		var insertVal = Evaluate(func.Arguments[2], row);

		using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
		if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
			throw new InvalidOperationException("JSON_ARRAY_INSERT: target must be an array.");

		// Parse the array index from the path (e.g., "$[1]")
		var match = System.Text.RegularExpressions.Regex.Match(path, @"\[(\d+)\]");
		if (!match.Success) throw new InvalidOperationException("JSON_ARRAY_INSERT: path must contain array index.");
		int insertIdx = int.Parse(match.Groups[1].Value);

		var items = doc.RootElement.EnumerateArray().ToList();
		using var stream = new System.IO.MemoryStream();
		using var writer = new System.Text.Json.Utf8JsonWriter(stream);
		writer.WriteStartArray();
		for (int i = 0; i < items.Count; i++)
		{
			if (i == insertIdx) WriteJsonValue(writer, insertVal);
			items[i].WriteTo(writer);
		}
		if (insertIdx >= items.Count) WriteJsonValue(writer, insertVal);
		writer.WriteEndArray();
		writer.Flush();
		return JsonSerializer.Deserialize<JsonElement>(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_CONTAINS
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_contains
	//   Checks if a JSON document contains another JSON document.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalJsonContains(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count != 2) throw new InvalidOperationException("JSON_CONTAINS requires 2 arguments.");
		var val1 = Evaluate(func.Arguments[0], row);
		var val2 = Evaluate(func.Arguments[1], row);
		if (val1 == null || val2 == null) return null;

		string json1 = val1 is JsonElement je1c ? je1c.GetRawText() : val1.ToString()!;
		string json2 = val2 is JsonElement je2c ? je2c.GetRawText() : val2.ToString()!;
		using var doc1 = System.Text.Json.JsonDocument.Parse(json1);
		using var doc2 = System.Text.Json.JsonDocument.Parse(json2);
		return JsonContains(doc1.RootElement, doc2.RootElement);
	}

	private static bool JsonContains(System.Text.Json.JsonElement container, System.Text.Json.JsonElement target)
	{
		if (target.ValueKind == System.Text.Json.JsonValueKind.Object)
		{
			if (container.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
			foreach (var prop in target.EnumerateObject())
			{
				if (!container.TryGetProperty(prop.Name, out var containerProp)) return false;
				if (!JsonContains(containerProp, prop.Value)) return false;
			}
			return true;
		}
		if (target.ValueKind == System.Text.Json.JsonValueKind.Array)
		{
			if (container.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
			foreach (var item in target.EnumerateArray())
			{
				bool found = false;
				foreach (var cItem in container.EnumerateArray())
				{
					if (JsonContains(cItem, item)) { found = true; break; }
				}
				if (!found) return false;
			}
			return true;
		}
		// For scalar targets, check if the container is an array that contains the scalar
		if (container.ValueKind == System.Text.Json.JsonValueKind.Array)
		{
			foreach (var item in container.EnumerateArray())
			{
				if (item.GetRawText() == target.GetRawText()) return true;
			}
			return false;
		}
		return container.GetRawText() == target.GetRawText();
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON_REMOVE
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#json_remove
	//   Produces JSON with the specified JSON data removed.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalJsonRemove(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		if (func.Arguments.Count < 2) throw new InvalidOperationException("JSON_REMOVE requires at least 2 arguments.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;

		var jsonStr = val is JsonElement jeRem ? jeRem.GetRawText() : val.ToString()!;
		using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
		var paths = new List<string>();
		for (int i = 1; i < func.Arguments.Count; i++)
		{
			var p = Evaluate(func.Arguments[i], row)?.ToString();
			if (p != null) paths.Add(p);
		}

		using var stream = new System.IO.MemoryStream();
		using var writer = new System.Text.Json.Utf8JsonWriter(stream);
		WriteJsonWithoutPaths(writer, doc.RootElement, paths, "$");
		writer.Flush();
		return JsonSerializer.Deserialize<JsonElement>(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
	}

	private static void WriteJsonWithoutPaths(System.Text.Json.Utf8JsonWriter writer,
		System.Text.Json.JsonElement element, List<string> removePaths, string currentPath)
	{
		if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
		{
			writer.WriteStartObject();
			foreach (var prop in element.EnumerateObject())
			{
				var propPath = $"{currentPath}.{prop.Name}";
				if (removePaths.Contains(propPath)) continue;
				writer.WritePropertyName(prop.Name);
				WriteJsonWithoutPaths(writer, prop.Value, removePaths, propPath);
			}
			writer.WriteEndObject();
		}
		else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
		{
			writer.WriteStartArray();
			int idx = 0;
			foreach (var item in element.EnumerateArray())
			{
				var itemPath = $"{currentPath}[{idx}]";
				if (!removePaths.Contains(itemPath))
					WriteJsonWithoutPaths(writer, item, removePaths, itemPath);
				idx++;
			}
			writer.WriteEndArray();
		}
		else
		{
			element.WriteTo(writer);
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// SAFE_TO_JSON
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#safe_to_json
	//   Similar to TO_JSON, but returns NULL instead of error for unsupported fields.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalSafeToJson(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#safe_to_json
		//   SAFE_TO_JSON(NULL) → SQL NULL
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;

		try
		{
			return EvalToJson(func, row);
		}
		catch
		{
			return null;
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// JSON array conversion functions
	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions
	//   Convert JSON arrays to typed SQL arrays.
	// ═══════════════════════════════════════════════════════════════

	private object? EvalJsonToTypedArray(FunctionCallExpr func, Dictionary<string, object?> row, string targetType)
	{
		if (func.Arguments.Count != 1) throw new InvalidOperationException($"{func.Name} requires 1 argument.");
		var val = Evaluate(func.Arguments[0], row);
		if (val == null) return null;

		string jsonStr = val.ToString()!;
		using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
		if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
			throw new InvalidOperationException($"{func.Name}: argument must be a JSON array.");

		var result = new List<object?>();
		foreach (var item in doc.RootElement.EnumerateArray())
		{
			if (item.ValueKind == System.Text.Json.JsonValueKind.Null)
			{
				result.Add(null);
				continue;
			}
			result.Add(targetType switch
			{
				"FLOAT64" => item.GetDouble(),
				"FLOAT32" => (double)item.GetSingle(),
				"INT64" => item.GetInt64(),
				"BOOL" => item.GetBoolean(),
				"STRING" => item.GetString(),
				_ => throw new InvalidOperationException($"Unsupported target type: {targetType}")
			});
		}
		return result;
	}

	private static void WriteJsonValue(System.Text.Json.Utf8JsonWriter writer, object? value)
	{
		switch (value)
		{
			case null: writer.WriteNullValue(); break;
			case JsonElement je: je.WriteTo(writer); break;
			case bool b: writer.WriteBooleanValue(b); break;
			case long l: writer.WriteNumberValue(l); break;
			case int i: writer.WriteNumberValue(i); break;
			case double d: writer.WriteNumberValue(d); break;
			case float f: writer.WriteNumberValue(f); break;
			case string s:
				// Try to parse as JSON first
				try
				{
					using var innerDoc = System.Text.Json.JsonDocument.Parse(s);
					innerDoc.RootElement.WriteTo(writer);
				}
				catch
				{
					writer.WriteStringValue(s);
				}
				break;
			default: writer.WriteStringValue(value.ToString()); break;
		}
	}

	// ═══════════════════════════════════════════════════════════════
	// Full-Text Search Helper: Extract named arguments
	// ═══════════════════════════════════════════════════════════════

	/// <summary>
	/// Extracts positional arguments (non-named) from a function call.
	/// Named arguments (NamedArgExpr) are skipped.
	/// </summary>
	private List<object?> EvalPositionalArgs(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var result = new List<object?>();
		foreach (var arg in func.Arguments)
		{
			if (arg is NamedArgExpr) continue;
			result.Add(Evaluate(arg, row));
		}
		return result;
	}

	/// <summary>
	/// Gets the value of a named argument from a function call, or null if not present.
	/// </summary>
	private object? GetNamedArg(FunctionCallExpr func, Dictionary<string, object?> row, string name)
	{
		foreach (var arg in func.Arguments)
		{
			if (arg is NamedArgExpr named && named.ArgName.Equals(name, StringComparison.OrdinalIgnoreCase))
				return Evaluate(named.Value, row);
		}
		return null;
	}

	/// <summary>
	/// Gets the value of a named argument as a specific type, or the default value if not present.
	/// </summary>
	private T GetNamedArg<T>(FunctionCallExpr func, Dictionary<string, object?> row, string name, T defaultValue)
	{
		var val = GetNamedArg(func, row, name);
		if (val is null) return defaultValue;
		return (T)Convert.ChangeType(val, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
	}

	// ═══════════════════════════════════════════════════════════════
	// Full-Text Search: Tokenization Functions
	// ═══════════════════════════════════════════════════════════════

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#token
	//   TOKEN(value) — exact-match tokenization
	private object? EvalToken(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = EvalPositionalArgs(func, row).FirstOrDefault();
		if (val is null) return null;
		return SpannerTokenList.FromToken(val);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_fulltext
	//   TOKENIZE_FULLTEXT(value [, language_tag => ...] [, content_type => ...] [, token_category => ...])
	private object? EvalTokenizeFulltext(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = EvalPositionalArgs(func, row).FirstOrDefault();
		if (val is null) return null;
		// Named args (language_tag, content_type, token_category, remove_diacritics) accepted but not used
		return SpannerTokenList.FromFullText(val.ToString());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_substring
	//   TOKENIZE_SUBSTRING(value [, ngram_size_min => ...] [, ngram_size_max => ...] ...)
	private object? EvalTokenizeSubstring(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = EvalPositionalArgs(func, row).FirstOrDefault();
		if (val is null) return null;
		var ngramSizeMin = GetNamedArg(func, row, "ngram_size_min", 1);
		var ngramSizeMax = GetNamedArg(func, row, "ngram_size_max", 4);
		return SpannerTokenList.FromSubstring(val.ToString(), ngramSizeMin, ngramSizeMax);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_ngrams
	//   TOKENIZE_NGRAMS(value [, ngram_size_min => ...] [, ngram_size_max => ...])
	private object? EvalTokenizeNgrams(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = EvalPositionalArgs(func, row).FirstOrDefault();
		if (val is null) return null;
		var ngramSizeMin = GetNamedArg(func, row, "ngram_size_min", 1);
		var ngramSizeMax = GetNamedArg(func, row, "ngram_size_max", 4);
		return SpannerTokenList.FromNgrams(val.ToString(), ngramSizeMin, ngramSizeMax);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_number
	//   TOKENIZE_NUMBER(value [, comparison_type => ...] [, algorithm => ...] ...)
	private object? EvalTokenizeNumber(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = EvalPositionalArgs(func, row).FirstOrDefault();
		if (val is null) return null;
		// Named args accepted but range/algorithm not used in approximate implementation
		return SpannerTokenList.FromNumber(val);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_bool
	//   TOKENIZE_BOOL(value)
	private object? EvalTokenizeBool(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = EvalPositionalArgs(func, row).FirstOrDefault();
		if (val is null) return null;
		return SpannerTokenList.FromBool(val);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenize_json
	//   TOKENIZE_JSON(value)
	private object? EvalTokenizeJson(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = EvalPositionalArgs(func, row).FirstOrDefault();
		if (val is null) return null;
		return SpannerTokenList.FromJson(val.ToString());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#tokenlist_concat
	//   TOKENLIST_CONCAT(value1, value2, ...)
	private object? EvalTokenlistConcat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var args = func.Arguments.Select(a => Evaluate(a, row)).ToList();
		// Handle both individual args and array args
		var lists = new List<SpannerTokenList?>();
		foreach (var arg in args)
		{
			if (arg is SpannerTokenList tl) lists.Add(tl);
			else if (arg is IList<object?> arr)
			{
				foreach (var item in arr)
					lists.Add(item as SpannerTokenList);
			}
		}
		if (lists.All(l => l is null)) return null;
		return SpannerTokenList.Concat(lists);
	}

	// ═══════════════════════════════════════════════════════════════
	// Full-Text Search: Retrieval Functions
	// ═══════════════════════════════════════════════════════════════

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_fulltext
	//   SEARCH(tokens, search_query [, dialect => ...] [, language_tag => ...] ...)
	//   "Returns TRUE if a full-text search query matches tokens."
	//   "Returns NULL when tokens or search_query is NULL."
	private object? EvalSearch(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var positional = EvalPositionalArgs(func, row);
		if (positional.Count < 2) throw new InvalidOperationException("SEARCH requires at least 2 arguments.");
		var tokens = positional[0];
		var query = positional[1];
		if (tokens is null || query is null) return null;
		if (tokens is not SpannerTokenList tl)
			throw new InvalidOperationException("SEARCH: first argument must be a TOKENLIST value.");
		var dialect = GetNamedArg(func, row, "dialect", (string?)null);
		return tl.Search(query.ToString(), dialect);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_substring
	//   SEARCH_SUBSTRING(tokens, substring_query [, relative_search_type => ...] ...)
	//   "Returns TRUE if a substring query matches tokens."
	//   "Returns NULL when tokens or substring_query is NULL."
	private object? EvalSearchSubstring(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var positional = EvalPositionalArgs(func, row);
		if (positional.Count < 2) throw new InvalidOperationException("SEARCH_SUBSTRING requires at least 2 arguments.");
		var tokens = positional[0];
		var query = positional[1];
		if (tokens is null || query is null) return null;
		if (tokens is not SpannerTokenList tl)
			throw new InvalidOperationException("SEARCH_SUBSTRING: first argument must be a TOKENLIST value.");
		var relativeSearchType = GetNamedArg(func, row, "relative_search_type", (string?)null);
		return tl.SearchSubstring(query.ToString(), relativeSearchType);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#search_ngrams
	//   SEARCH_NGRAMS(tokens, ngrams_query [, min_ngrams => ...] [, min_ngrams_percent => ...])
	//   "Returns NULL when tokens or ngrams_query is NULL."
	private object? EvalSearchNgrams(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var positional = EvalPositionalArgs(func, row);
		if (positional.Count < 2) throw new InvalidOperationException("SEARCH_NGRAMS requires at least 2 arguments.");
		var tokens = positional[0];
		var query = positional[1];
		if (tokens is null || query is null) return null;
		if (tokens is not SpannerTokenList tl)
			throw new InvalidOperationException("SEARCH_NGRAMS: first argument must be a TOKENLIST value.");
		var minNgrams = GetNamedArg(func, row, "min_ngrams", 2);
		var minNgramsPercentVal = GetNamedArg(func, row, "min_ngrams_percent");
		double? minNgramsPercent = minNgramsPercentVal is not null ? Convert.ToDouble(minNgramsPercentVal) : null;
		return tl.SearchNgrams(query.ToString(), minNgrams, minNgramsPercent);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score
	//   SCORE(tokens, search_query [, dialect => ...] [, options => ...])
	//   "Returns 0 when tokens or search_query is NULL."
	private object? EvalScore(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var positional = EvalPositionalArgs(func, row);
		if (positional.Count < 2) throw new InvalidOperationException("SCORE requires at least 2 arguments.");
		var tokens = positional[0];
		var query = positional[1];
		if (tokens is null || query is null) return 0.0;
		if (tokens is not SpannerTokenList tl)
			throw new InvalidOperationException("SCORE: first argument must be a TOKENLIST value.");
		// Named args (dialect, language_tag, enhance_query, dictionary, options) accepted but not used
		return tl.Score(query.ToString());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#score_ngrams
	//   SCORE_NGRAMS(tokens, ngrams_query [, algorithm => ...])
	//   "Returns 0 when tokens or ngrams_query is NULL."
	private object? EvalScoreNgrams(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var positional = EvalPositionalArgs(func, row);
		if (positional.Count < 2) throw new InvalidOperationException("SCORE_NGRAMS requires at least 2 arguments.");
		var tokens = positional[0];
		var query = positional[1];
		if (tokens is null || query is null) return 0.0;
		if (tokens is not SpannerTokenList tl)
			throw new InvalidOperationException("SCORE_NGRAMS: first argument must be a TOKENLIST value.");
		return tl.ScoreNgrams(query.ToString());
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#snippet
	//   SNIPPET(data_to_search, raw_search_query [, max_snippet_width => ...] [, max_snippets => ...])
	//   "Returns NULL when data_to_search or raw_search_query is NULL."
	private object? EvalSnippet(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var positional = EvalPositionalArgs(func, row);
		if (positional.Count < 2) throw new InvalidOperationException("SNIPPET requires at least 2 arguments.");
		var data = positional[0];
		var query = positional[1];
		if (data is null || query is null) return null;
		var maxWidth = GetNamedArg(func, row, "max_snippet_width", 200);
		var maxSnippets = GetNamedArg(func, row, "max_snippets", 1);
		return SpannerTokenList.Snippet(data.ToString(), query.ToString(), maxWidth, maxSnippets);
	}

	// ═══════════════════════════════════════════════════════════════
	// Full-Text Search: Debugging
	// ═══════════════════════════════════════════════════════════════

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/search_functions#debug_tokenlist
	//   DEBUG_TOKENLIST(tokenlist)
	//   "Displays a human-readable representation of tokens."
	private object? EvalDebugTokenlist(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		if (val is not SpannerTokenList tl)
			throw new InvalidOperationException("DEBUG_TOKENLIST: argument must be a TOKENLIST value.");
		return tl.DebugString();
	}

	// ═══════════════════════════════════════════════════════════════
	// Compression Functions (Zstandard)
	// ═══════════════════════════════════════════════════════════════

	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   ZSTD_COMPRESS(value) — compresses STRING or BYTES to BYTES using Zstandard.
	private object? EvalZstdCompress(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		byte[] input = val switch
		{
			byte[] b => b,
			string s => System.Text.Encoding.UTF8.GetBytes(s),
			_ => throw new InvalidOperationException("ZSTD_COMPRESS: argument must be STRING or BYTES.")
		};
		using var compressor = new ZstdSharp.Compressor();
		return compressor.Wrap(input).ToArray();
	}

	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   ZSTD_DECOMPRESS_TO_BYTES(value) — decompresses BYTES to BYTES using Zstandard.
	private object? EvalZstdDecompressToBytes(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		if (val is not byte[] compressed)
			throw new InvalidOperationException("ZSTD_DECOMPRESS_TO_BYTES: argument must be BYTES.");
		using var decompressor = new ZstdSharp.Decompressor();
		return decompressor.Unwrap(compressed).ToArray();
	}

	// Ref: https://docs.cloud.google.com/spanner/docs/reference/standard-sql/functions-all
	//   ZSTD_DECOMPRESS_TO_STRING(value) — decompresses BYTES to STRING using Zstandard.
	private object? EvalZstdDecompressToString(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		if (val is not byte[] compressed)
			throw new InvalidOperationException("ZSTD_DECOMPRESS_TO_STRING: argument must be BYTES.");
		using var decompressor = new ZstdSharp.Decompressor();
		var decompressed = decompressor.Unwrap(compressed);
		return System.Text.Encoding.UTF8.GetString(decompressed);
	}
}
