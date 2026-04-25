using System.Text.Json;
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
	private readonly Dictionary<string, SelectStatement>? _cteMap;
	private readonly Dictionary<string, object?>? _outerRow;

	public ExpressionEvaluator(IDictionary<string, object?>? parameters = null,
		QueryExecutor? queryExecutor = null,
		Dictionary<string, SelectStatement>? cteMap = null,
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
			StructExpr structExpr => structExpr.Fields.ToDictionary(f => f.Name ?? "", f => Evaluate(f.Value, row)),
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
		var operand = Evaluate(unary.Operand, row);
		return unary.Op switch
		{
			UnaryOp.Not => operand is null ? null : !(bool)operand,
			UnaryOp.Negate => operand switch
			{
				null => null,
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

		var found = inExpr.List.Any(item =>
		{
			var itemValue = Evaluate(item, row);
			return itemValue != null && CompareValues(value, itemValue) == 0;
		});

		return inExpr.IsNegated ? !found : found;
	}

	private object? EvalBetween(BetweenExpr between, Dictionary<string, object?> row)
	{
		var value = Evaluate(between.Value, row);
		var low = Evaluate(between.Low, row);
		var high = Evaluate(between.High, row);

		if (value is null || low is null || high is null) return null;

		var inRange = CompareValues(value, low) >= 0 && CompareValues(value, high) <= 0;
		return between.IsNegated ? !inRange : inRange;
	}

	private object? EvalFunction(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/functions-and-operators
		var funcName = func.Name.ToUpperInvariant();

		// Aggregate functions may have been pre-computed and stored in the row
		// (e.g., during GROUP BY processing or HAVING evaluation).
		if (funcName is "SUM" or "AVG" or "COUNT" or "MIN" or "MAX"
			or "ARRAY_AGG" or "STRING_AGG" or "COUNTIF" or "ANY_VALUE"
			or "LOGICAL_AND" or "LOGICAL_OR"
			or "BIT_AND" or "BIT_OR" or "BIT_XOR")
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
			"COALESCE" => func.Arguments.Select(a => Evaluate(a, row)).FirstOrDefault(v => v != null),
			"IFNULL" => Evaluate(func.Arguments[0], row) ?? Evaluate(func.Arguments[1], row),
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
			"LEFT" => EvalLeft(func, row),
			"RIGHT" => EvalRight(func, row),
			"BYTE_LENGTH" => EvalStringFunc1(func, row, s => (long)System.Text.Encoding.UTF8.GetByteCount(s)),
			"TO_HEX" => EvalToHex(func, row),
			"FROM_HEX" => EvalFromHex(func, row),
			"ASCII" => EvalStringFunc1(func, row, s => s.Length > 0 ? (long)s[0] : 0L),
			"CHR" or "CODE_POINTS_TO_STRING" => EvalChr(func, row),
			"CONTAINS_SUBSTR" => EvalStringFunc2Bool(func, row, (s, sub) =>
				s.Contains(sub, StringComparison.OrdinalIgnoreCase)),
			"SOUNDEX" => EvalStringFunc1(func, row, EvalSoundex),
			"UNICODE" => EvalStringFunc1(func, row, s => s.Length > 0 ? (long)char.ConvertToUtf32(s, 0) : 0L),
			"INITCAP" => EvalStringFunc1(func, row, s =>
				System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant())),
			"TRANSLATE" => EvalTranslate(func, row),
			"INSTR" => EvalInstr(func, row),

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
			"SIGN" => EvalMathFunc1(func, row, l => Math.Sign(l), d => Math.Sign(d)),
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
			"EXP" => EvalMathFunc1Double(func, row, Math.Exp),
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
			"RAND" => new Random().NextDouble(),
			"RANGE_BUCKET" => EvalRangeBucket(func, row),

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

			_ => throw new NotSupportedException($"Function '{func.Name}' is not supported.")
		};
	}

	private object? EvalNullIf(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a != null && b != null && CompareValues(a, b) == 0) return null;
		return a;
	}

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
		return fn((string)a, (string)b);
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
		//   "Position is 1-based. If pos is negative, the function counts from the end."
		var position = Convert.ToInt32(pos);
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
		Func<long, long> longFn, Func<double, double> doubleFn)
	{
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;
		return val switch
		{
			long l => longFn(l),
			double d => doubleFn(d),
			float f => (float)doubleFn(f),
			decimal dec => Math.Abs(dec),
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

	private object? EvalMod(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a is null || b is null) return null;
		return a switch
		{
			long la when b is long lb => la % lb,
			double da when b is double db => da % db,
			_ => Convert.ToDouble(a) % Convert.ToDouble(b)
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
		var values = func.Arguments.Select(a => Evaluate(a, row)).ToList();
		if (values.Any(v => v is null)) return null;
		if (values.Count == 0) return null;

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
				// "When casting from floating point to integer, [CAST] rounds to the nearest integer."
				TypeCode.Int64 => value is double d ? (long)Math.Round(d, MidpointRounding.AwayFromZero) : value is float f ? (long)Math.Round(f, MidpointRounding.AwayFromZero) : Convert.ToInt64(value),
				TypeCode.Float64 => Convert.ToDouble(value),
				TypeCode.Float32 => Convert.ToSingle(value),
				TypeCode.Bool => Convert.ToBoolean(value),
				TypeCode.String => value switch
				{
					// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/conversion_functions#cast
					//   CAST(bool AS STRING) returns lowercase "true" / "false"
					bool bv => bv ? "true" : "false",
					DateTime dt => dt.Date == dt ? dt.ToString("yyyy-MM-dd") : dt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
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
				TypeCode.Array => value is IList<object?> ? value : new List<object?> { value },
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
				if (operand != null && whenVal != null && CompareValues(operand, whenVal) == 0)
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
		// The QueryExecutor stores window results keyed by InferColumnName(win)
		// which for a FunctionCallExpr is the function name
		var key = win.Function switch
		{
			FunctionCallExpr f => f.Name,
			CountStarExpr => "COUNT(*)",
			_ => ""
		};
		if (row.TryGetValue(key, out var val))
			return val;

		// If the window result was stored under a different name, check the row keys
		throw new InvalidOperationException($"Window function result '{key}' not found. Window functions must be pre-computed by QueryExecutor.");
	}

	// ── Subquery evaluation ──

	private List<Dictionary<string, object?>> RunSubquery(SelectStatement subquery,
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
		var subRows = RunSubquery(inSub.Subquery, row);
		var found = subRows.Any(r =>
		{
			var subVal = r.Values.FirstOrDefault();
			return value != null && subVal != null && CompareValues(value, subVal) == 0;
		});
		return inSub.IsNegated ? !found : found;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/operators#in_operators
	//   "value [NOT] IN UNNEST(array_expression)"
	private object? EvalInUnnest(InUnnestExpr inUnnest, Dictionary<string, object?> row)
	{
		var value = Evaluate(inUnnest.Value, row);
		var array = Evaluate(inUnnest.ArrayExpr, row);
		if (array == null || value == null) return inUnnest.IsNegated ? true : false;

		bool found = false;
		if (array is System.Collections.IEnumerable enumerable)
		{
			foreach (var item in enumerable)
			{
				if (item != null && CompareValues(value, item) == 0)
				{
					found = true;
					break;
				}
			}
		}
		return inUnnest.IsNegated ? !found : found;
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

		// Fallback: convert to string
		return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
	}

	private static bool IsNumeric(object? v) =>
		v is long or double or float or decimal or int or short or byte;

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

	private static object? ConcatValues(object? a, object? b)
	{
		if (a is null || b is null) return null;
		return a.ToString() + b.ToString();
	}

	private static object? ArithmeticOp(object? a, object? b, char op)
	{
		if (a is null || b is null) return null;

		if (a is long la && b is long lb)
		{
			return op switch
			{
				'+' => la + lb,
				'-' => la - lb,
				'*' => la * lb,
				'/' => lb == 0 ? throw new InvalidOperationException("Division by zero.") : la / lb,
				'%' => lb == 0 ? throw new InvalidOperationException("Division by zero.") : la % lb,
				_ => throw new NotSupportedException()
			};
		}

		var da = Convert.ToDouble(a);
		var db = Convert.ToDouble(b);
		return op switch
		{
			'+' => da + db,
			'-' => da - db,
			'*' => da * db,
			'/' => db == 0 ? throw new InvalidOperationException("Division by zero.") : da / db,
			'%' => db == 0 ? throw new InvalidOperationException("Division by zero.") : da % db,
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
		var delimiter = func.Arguments.Count > 1 ? Convert.ToString(Evaluate(func.Arguments[1], row)) ?? "," : ",";
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
		// Truncate if length is less than the string length
		if (len <= 0) return "";
		if (len <= str.Length) return str[..len];
		var pad = func.Arguments.Count > 2 ? Convert.ToString(Evaluate(func.Arguments[2], row)) ?? " " : " ";
		if (pad.Length == 0) return str;
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
		var countVal = Evaluate(func.Arguments[1], row);
		if (countVal == null) return null;
		var count = Convert.ToInt32(countVal);
		return string.Concat(Enumerable.Repeat(str, Math.Max(0, count)));
	}

	private object? EvalFormat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#format_string
		var fmt = Evaluate(func.Arguments[0], row);
		if (fmt == null) return null;
		var fmtStr = Convert.ToString(fmt) ?? "";
		// Check if any argument is NULL — return NULL
		var args = func.Arguments.Skip(1).Select(a => Evaluate(a, row)).ToArray();
		if (args.Any(a => a == null)) return null;
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
						sb.Append(arg?.ToString());
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
		var match = Regex.Match(Convert.ToString(s)!, Convert.ToString(pattern)!);
		if (!match.Success) return null;
		return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
	}

	private object? EvalRegexpReplace(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var pattern = Evaluate(func.Arguments[1], row);
		var replacement = Evaluate(func.Arguments[2], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/string_functions#regexp_replace
		if (s == null || pattern == null || replacement == null) return null;
		return Regex.Replace(Convert.ToString(s)!, Convert.ToString(pattern)!, Convert.ToString(replacement)!);
	}

	private object? EvalLeft(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		var lenVal = Evaluate(func.Arguments[1], row);
		if (lenVal == null) return null;
		var len = Convert.ToInt32(lenVal);
		return str[..Math.Min(len, str.Length)];
	}

	private object? EvalRight(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		var lenVal = Evaluate(func.Arguments[1], row);
		if (lenVal == null) return null;
		var len = Convert.ToInt32(lenVal);
		return str.Length <= len ? str : str[(str.Length - len)..];
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

	private object? EvalChr(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		return char.ConvertFromUtf32(Convert.ToInt32(v));
	}

	private static string EvalSoundex(string s)
	{
		if (string.IsNullOrEmpty(s)) return "";
		var result = new char[4];
		result[0] = char.ToUpper(s[0]);
		int idx = 1;
		for (int i = 1; i < s.Length && idx < 4; i++)
		{
			var code = SoundexCode(s[i]);
			if (code != '0' && code != SoundexCode(s[i - 1]))
				result[idx++] = code;
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

	private object? EvalTranslate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		var from = Convert.ToString(Evaluate(func.Arguments[1], row)) ?? "";
		var to = Convert.ToString(Evaluate(func.Arguments[2], row)) ?? "";
		var chars = str.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			int idx = from.IndexOf(chars[i]);
			if (idx >= 0) chars[i] = idx < to.Length ? to[idx] : '\0';
		}
		return new string(chars.Where(c => c != '\0').ToArray());
	}

	private object? EvalInstr(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var sub = Evaluate(func.Arguments[1], row);
		if (s == null || sub == null) return null;
		var str = Convert.ToString(s) ?? "";
		var substr = Convert.ToString(sub) ?? "";
		var pos = func.Arguments.Count > 2 ? Convert.ToInt32(Evaluate(func.Arguments[2], row)) : 1;
		var occ = func.Arguments.Count > 3 ? Convert.ToInt32(Evaluate(func.Arguments[3], row)) : 1;
		int startIdx = pos > 0 ? pos - 1 : 0;
		for (int i = 0; i < occ; i++)
		{
			int found = str.IndexOf(substr, startIdx, StringComparison.Ordinal);
			if (found < 0) return 0L;
			if (i == occ - 1) return (long)(found + 1);
			startIdx = found + 1;
		}
		return 0L;
	}

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
		var db = Convert.ToDouble(b);
		if (db == 0) return null;
		return Convert.ToDouble(a) / db;
	}

	private object? EvalSafeNegate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		try
		{
			if (v is long l) return checked(-l);
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
			var da = Convert.ToDouble(a); var db = Convert.ToDouble(b);
			return op switch
			{
				"ADD" => da + db,
				"SUB" => da - db,
				"MUL" => da * db,
				_ => throw new NotSupportedException()
			};
		}
		catch (OverflowException) { return null; }
	}

	private object? EvalPow(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var a = Evaluate(func.Arguments[0], row);
		var b = Evaluate(func.Arguments[1], row);
		if (a == null || b == null) return null;
		return Math.Pow(Convert.ToDouble(a), Convert.ToDouble(b));
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

	private object? EvalRangeBucket(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var point = Evaluate(func.Arguments[0], row);
		var arr = Evaluate(func.Arguments[1], row) as IList<object?>;
		if (point == null || arr == null) return null;
		long bucket = 0;
		foreach (var boundary in arr)
		{
			if (boundary != null && CompareValues(point, boundary) >= 0) bucket++;
			else break;
		}
		return bucket;
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
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		return AddToPart(dt, part!, amount);
	}

	private object? EvalTimestampSub(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		var amount = Convert.ToInt64(Evaluate(func.Arguments[1], row));
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		return AddToPart(dt, part!, -amount);
	}

	private static DateTime AddToPart(DateTime dt, string part, long amount) => part switch
	{
		"MICROSECOND" => dt.AddTicks(amount * 10),
		"MILLISECOND" => dt.AddMilliseconds(amount),
		"SECOND" => dt.AddSeconds(amount),
		"MINUTE" => dt.AddMinutes(amount),
		"HOUR" => dt.AddHours(amount),
		"DAY" => dt.AddDays(amount),
		"MONTH" => dt.AddMonths((int)amount),
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
		var diff = dt1 - dt2;
		return part switch
		{
			"MICROSECOND" => diff.Ticks / 10,
			"MILLISECOND" => (long)diff.TotalMilliseconds,
			"SECOND" => (long)diff.TotalSeconds,
			"MINUTE" => (long)diff.TotalMinutes,
			"HOUR" => (long)diff.TotalHours,
			"DAY" => (long)diff.TotalDays,
			"MONTH" => (dt1.Year - dt2.Year) * 12 + dt1.Month - dt2.Month,
			"YEAR" => (long)(dt1.Year - dt2.Year),
			_ => throw new InvalidOperationException($"TIMESTAMP_DIFF: unsupported part '{part}'.")
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
			"MONTH" => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, dt.Kind),
			"YEAR" => new DateTime(dt.Year, 1, 1, 0, 0, 0, dt.Kind),
			_ => throw new InvalidOperationException($"TIMESTAMP_TRUNC: unsupported part '{part}'.")
		};

		// Convert back to UTC
		if (isUtc) truncated = TimeZoneInfo.ConvertTimeToUtc(truncated, DefaultTimeZone);

		return truncated;
	}

	private object? EvalDateAdd(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		var amount = Convert.ToInt32(Evaluate(func.Arguments[1], row));
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		if (v == null) return null;
		var dt = v is DateTime d ? d : DateTime.Parse(Convert.ToString(v)!);
		return AddToPart(dt, part!, amount).Date;
	}

	private object? EvalDateSub(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		var amount = Convert.ToInt32(Evaluate(func.Arguments[1], row));
		var part = Convert.ToString(Evaluate(func.Arguments[2], row))?.ToUpperInvariant();
		if (v == null) return null;
		var dt = v is DateTime d ? d : DateTime.Parse(Convert.ToString(v)!);
		return AddToPart(dt, part!, -amount).Date;
	}

	private object? EvalDateDiff(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		return EvalTimestampDiff(func, row);
	}

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
		// Simplified: use .NET format (Spanner uses strftime-like)
		return dt.ToString(ConvertSpannerDateFormat(fmt ?? ""), System.Globalization.CultureInfo.InvariantCulture);
	}

	private object? EvalParseTimestamp(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var fmt = Convert.ToString(Evaluate(func.Arguments[0], row));
		var str = Convert.ToString(Evaluate(func.Arguments[1], row));
		if (str == null) return null;
		return DateTime.ParseExact(str, ConvertSpannerDateFormat(fmt ?? ""), System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
	}

	private object? EvalFormatDate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		return EvalFormatTimestamp(func, row);
	}

	private object? EvalParseDate(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var fmt = Convert.ToString(Evaluate(func.Arguments[0], row));
		var str = Convert.ToString(Evaluate(func.Arguments[1], row));
		if (str == null) return null;
		return DateTime.ParseExact(str, ConvertSpannerDateFormat(fmt ?? ""), System.Globalization.CultureInfo.InvariantCulture).Date;
	}

	private static string ConvertSpannerDateFormat(string spannerFmt)
	{
		// Convert Spanner strftime-like to .NET format
		return spannerFmt
			.Replace("%Y", "yyyy").Replace("%m", "MM").Replace("%d", "dd")
			.Replace("%H", "HH").Replace("%M", "mm").Replace("%S", "ss")
			.Replace("%E3S", "ss.fff").Replace("%E6S", "ss.ffffff")
			.Replace("%Z", "K").Replace("%z", "zzz");
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
		return new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000;
	}

	private object? EvalTimestampFromUnix(FunctionCallExpr func, Dictionary<string, object?> row, string unit)
	{
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		var n = Convert.ToInt64(v);
		return unit switch
		{
			"SECONDS" => DateTimeOffset.FromUnixTimeSeconds(n).UtcDateTime,
			"MILLIS" => DateTimeOffset.FromUnixTimeMilliseconds(n).UtcDateTime,
			"MICROS" => DateTimeOffset.FromUnixTimeMilliseconds(n / 1000).UtcDateTime,
			_ => throw new NotSupportedException()
		};
	}

	// ──────────────────────────────────────────
	// Conversion function helpers
	// ──────────────────────────────────────────

	private object? EvalToJson(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var v = Evaluate(func.Arguments[0], row);
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/json_functions#to_json_string
		//   TO_JSON_STRING returns the JSON-formatted STRING representation, including "null" for SQL NULL.
		if (v == null) return "null";
		return JsonSerializer.Serialize(v);
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
		var sep = func.Arguments.Count > 1 ? Convert.ToString(Evaluate(func.Arguments[1], row)) ?? "," : ",";
		// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/array_functions#array_to_string
		//   With 2 args: NULL elements are omitted. With 3 args: NULL elements are replaced.
		if (v is IList<object?> list)
		{
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
		var step = func.Arguments.Count > 2 ? Convert.ToInt64(Evaluate(func.Arguments[2], row)) : 1L;
		var s = Convert.ToInt64(start);
		var e = Convert.ToInt64(end);
		var result = new List<object?>();
		if (step > 0) for (long i = s; i <= e; i += step) result.Add(i);
		else if (step < 0) for (long i = s; i >= e; i += step) result.Add(i);
		return result;
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
		var path = Convert.ToString(Evaluate(func.Arguments[1], row));
		if (json == null || path == null) return null;
		var elem = NavigateJsonPath(json, path);
		if (elem is JsonElement je)
		{
			return je.ValueKind switch
			{
				JsonValueKind.String => je.GetString(),
				JsonValueKind.Number => je.GetRawText(),
				JsonValueKind.True => "true",
				JsonValueKind.False => "false",
				JsonValueKind.Null => null,
				_ => je.GetRawText()
			};
		}
		return elem?.ToString();
	}

	private object? EvalJsonQuery(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var json = Evaluate(func.Arguments[0], row);
		var path = Convert.ToString(Evaluate(func.Arguments[1], row));
		if (json == null || path == null) return null;
		var elem = NavigateJsonPath(json, path);
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

		// Simple path navigation: $.key1.key2 or $[0].key
		var parts = path.TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts)
		{
			if (elem.ValueKind == JsonValueKind.Object && elem.TryGetProperty(part, out var prop))
				elem = prop;
			else if (int.TryParse(part, out var idx) && elem.ValueKind == JsonValueKind.Array && idx < elem.GetArrayLength())
				elem = elem[idx];
			else
				return null;
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
}
