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

	public ExpressionEvaluator(IDictionary<string, object?>? parameters = null,
		QueryExecutor? queryExecutor = null,
		Dictionary<string, SelectStatement>? cteMap = null)
	{
		_parameters = parameters;
		_queryExecutor = queryExecutor;
		_cteMap = cteMap;
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
			ScalarSubqueryExpr sub => EvalScalarSubquery(sub),
			ExistsExpr exists => EvalExists(exists),
			InSubqueryExpr inSub => EvalInSubquery(inSub, row),
			ArraySubqueryExpr arraySub => EvalArraySubquery(arraySub),
			ArrayLiteralExpr arrayLit => arrayLit.Elements.Select(e => Evaluate(e, row)).ToList(),
			ArrayAccessExpr access => EvalArrayAccess(access, row),
			WindowExpr win => EvalWindowExpr(win, row),
			StructExpr structExpr => structExpr.Fields.ToDictionary(f => f.Name ?? "", f => Evaluate(f.Value, row)),
			StarExpr => throw new InvalidOperationException("Star expression cannot be evaluated as a value."),
			CountStarExpr => throw new InvalidOperationException("COUNT(*) should be evaluated in aggregate context."),
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
		}

		// Try unqualified name
		if (row.TryGetValue(col.Column, out var value))
			return value;

		// Case-insensitive fallback
		var key = row.Keys.FirstOrDefault(k => string.Equals(k, col.Column, StringComparison.OrdinalIgnoreCase));
		if (key != null)
			return row[key];

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
		// Short-circuit AND/OR
		if (bin.Op == BinaryOp.And)
		{
			var left = Evaluate(bin.Left, row);
			if (left is not true) return false;
			var right = Evaluate(bin.Right, row);
			return right is true;
		}
		if (bin.Op == BinaryOp.Or)
		{
			var left = Evaluate(bin.Left, row);
			if (left is true) return true;
			var right = Evaluate(bin.Right, row);
			return right is true;
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
			"TRIM" => EvalStringFunc1(func, row, s => s.Trim()),
			"LTRIM" => EvalStringFunc1(func, row, s => s.TrimStart()),
			"RTRIM" => EvalStringFunc1(func, row, s => s.TrimEnd()),
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
			"SQRT" => EvalMathFunc1Double(func, row, Math.Sqrt),
			"POW" or "POWER" => EvalPow(func, row),
			"EXP" => EvalMathFunc1Double(func, row, Math.Exp),
			"LN" => EvalMathFunc1Double(func, row, Math.Log),
			"LOG" => EvalLog(func, row),
			"LOG10" => EvalMathFunc1Double(func, row, Math.Log10),
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
		//   "Position is 1-based."
		var startIndex = Convert.ToInt32(pos) - 1;
		if (startIndex < 0) startIndex = 0;
		if (startIndex >= str.Length) return "";

		if (func.Arguments.Count > 2)
		{
			var len = Evaluate(func.Arguments[2], row);
			if (len is null) return null;
			var length = Convert.ToInt32(len);
			if (length < 0) throw new InvalidOperationException("SUBSTR length must be non-negative.");
			return str.Substring(startIndex, Math.Min(length, str.Length - startIndex));
		}

		return str[startIndex..];
	}

	private object? EvalReplace(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		var from = Evaluate(func.Arguments[1], row);
		var to = Evaluate(func.Arguments[2], row);
		if (s is null || from is null || to is null) return null;
		return ((string)s).Replace((string)from, (string)to);
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
		return val switch
		{
			long l => (long)fn(l),
			double d => fn(d),
			float f => (float)fn(f),
			_ => throw new InvalidOperationException($"Cannot apply math function to {val.GetType().Name}")
		};
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
		var val = Evaluate(func.Arguments[0], row);
		if (val is null) return null;

		int digits = 0;
		if (func.Arguments.Count > 1)
		{
			var d = Evaluate(func.Arguments[1], row);
			if (d != null) digits = Convert.ToInt32(d);
		}

		return val switch
		{
			double dv => Math.Round(dv, digits, MidpointRounding.AwayFromZero),
			float fv => (float)Math.Round(fv, digits, MidpointRounding.AwayFromZero),
			decimal dec => Math.Round(dec, digits, MidpointRounding.AwayFromZero),
			long l => l,
			_ => throw new InvalidOperationException($"Cannot round {val.GetType().Name}")
		};
	}

	private object? EvalGreatestLeast(FunctionCallExpr func, Dictionary<string, object?> row, bool greatest)
	{
		var values = func.Arguments.Select(a => Evaluate(a, row)).Where(v => v != null).ToList();
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
				// "When casting from floating point to integer, [CAST] truncates."
				TypeCode.Int64 => value is double d ? (long)d : value is float f ? (long)f : Convert.ToInt64(value),
				TypeCode.Float64 => Convert.ToDouble(value),
				TypeCode.Float32 => Convert.ToSingle(value),
				TypeCode.Bool => Convert.ToBoolean(value),
				TypeCode.String => value.ToString(),
				TypeCode.Numeric => Convert.ToDecimal(value),
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

	private List<Dictionary<string, object?>> RunSubquery(SelectStatement subquery)
	{
		if (_queryExecutor == null)
			throw new InvalidOperationException("Subqueries require a QueryExecutor context.");
		return _queryExecutor.ExecuteSubquery(subquery, _parameters, _cteMap);
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#scalar_subquery_concepts
	//   "A scalar subquery produces at most one row. If the subquery returns zero rows, the result is NULL."
	private object? EvalScalarSubquery(ScalarSubqueryExpr sub)
	{
		var rows = RunSubquery(sub.Subquery);
		if (rows.Count == 0) return null;
		if (rows.Count > 1)
			throw new InvalidOperationException("Scalar subquery returned more than one row.");
		return rows[0].Values.FirstOrDefault();
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#exists_subquery_concepts
	private object? EvalExists(ExistsExpr exists)
	{
		var rows = RunSubquery(exists.Subquery);
		var result = rows.Count > 0;
		return exists.IsNegated ? !result : result;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#in_subquery_concepts
	private object? EvalInSubquery(InSubqueryExpr inSub, Dictionary<string, object?> row)
	{
		var value = Evaluate(inSub.Value, row);
		var subRows = RunSubquery(inSub.Subquery);
		var found = subRows.Any(r =>
		{
			var subVal = r.Values.FirstOrDefault();
			return value != null && subVal != null && CompareValues(value, subVal) == 0;
		});
		return inSub.IsNegated ? !found : found;
	}

	// Ref: https://cloud.google.com/spanner/docs/reference/standard-sql/subqueries#array_subquery_concepts
	private object? EvalArraySubquery(ArraySubqueryExpr arraySub)
	{
		var rows = RunSubquery(arraySub.Subquery);
		return rows.Select(r => r.Values.FirstOrDefault()).ToList();
	}

	// ── Comparison helper ──

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
		return str.Split(delimiter).Cast<object?>().ToList();
	}

	private object? EvalPad(FunctionCallExpr func, Dictionary<string, object?> row, bool padLeft)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		var len = Convert.ToInt32(Evaluate(func.Arguments[1], row));
		var pad = func.Arguments.Count > 2 ? Convert.ToString(Evaluate(func.Arguments[2], row)) ?? " " : " ";
		if (pad.Length == 0) return str;
		while (str.Length < len)
			str = padLeft ? pad[..(Math.Min(pad.Length, len - str.Length))] + str : str + pad[..(Math.Min(pad.Length, len - str.Length))];
		return str;
	}

	private object? EvalRepeat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		var count = Convert.ToInt32(Evaluate(func.Arguments[1], row));
		return string.Concat(Enumerable.Repeat(str, Math.Max(0, count)));
	}

	private object? EvalFormat(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// Simplified FORMAT: just concatenate the format string with args via string.Format
		var fmt = Evaluate(func.Arguments[0], row);
		if (fmt == null) return null;
		var fmtStr = Convert.ToString(fmt) ?? "";
		// Spanner FORMAT uses %s, %d, etc. — simplified: replace %s/%d/%f with {0},{1},...
		int idx = 0;
		var converted = Regex.Replace(fmtStr, @"%(s|d|f|i|t|T)", _ => $"{{{idx++}}}");
		var args = func.Arguments.Skip(1).Select(a => Evaluate(a, row)).ToArray();
		try { return string.Format(converted, args); }
		catch { return fmtStr; }
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
		if (s == null) return null;
		return Regex.Replace(Convert.ToString(s)!, Convert.ToString(pattern)!, Convert.ToString(replacement) ?? "");
	}

	private object? EvalLeft(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		var len = Convert.ToInt32(Evaluate(func.Arguments[1], row));
		return str[..Math.Min(len, str.Length)];
	}

	private object? EvalRight(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var s = Evaluate(func.Arguments[0], row);
		if (s == null) return null;
		var str = Convert.ToString(s) ?? "";
		var len = Convert.ToInt32(Evaluate(func.Arguments[1], row));
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
			var n = Convert.ToInt32(Evaluate(func.Arguments[1], row));
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
		if (func.Arguments.Count > 1)
		{
			var b = Evaluate(func.Arguments[1], row);
			if (b == null) return null;
			return Math.Log(Convert.ToDouble(v), Convert.ToDouble(b));
		}
		return Math.Log(Convert.ToDouble(v));
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
		var v = Evaluate(func.Arguments[0], row);
		if (v == null) return null;
		if (v is DateTime dt) return dt.Date;
		if (v is DateTimeOffset dto) return dto.UtcDateTime.Date;
		return DateTime.Parse(Convert.ToString(v)!).Date;
	}

	private object? EvalExtract(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		// EXTRACT(part FROM expr) — simplified: func.Arguments[0] is the part name, [1] is the expr
		// In our AST, EXTRACT might be parsed differently. For now, handle common patterns.
		if (func.Arguments.Count < 2) return null;
		var part = Evaluate(func.Arguments[0], row);
		var ts = Evaluate(func.Arguments[1], row);
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
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
			"WEEK" => (long)System.Globalization.ISOWeek.GetWeekOfYear(dt),
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
		var diff = dt1 - dt2;
		return part switch
		{
			"MICROSECOND" => diff.Ticks / 10,
			"MILLISECOND" => (long)diff.TotalMilliseconds,
			"SECOND" => (long)diff.TotalSeconds,
			"MINUTE" => (long)diff.TotalMinutes,
			"HOUR" => (long)diff.TotalHours,
			"DAY" => (long)diff.TotalDays,
			_ => throw new InvalidOperationException($"TIMESTAMP_DIFF: unsupported part '{part}'.")
		};
	}

	private object? EvalTimestampTrunc(FunctionCallExpr func, Dictionary<string, object?> row)
	{
		var ts = Evaluate(func.Arguments[0], row);
		var part = Convert.ToString(Evaluate(func.Arguments[1], row))?.ToUpperInvariant();
		if (ts == null) return null;
		var dt = ts is DateTime d ? d : DateTime.Parse(Convert.ToString(ts)!);
		return part switch
		{
			"MICROSECOND" => new DateTime(dt.Ticks / 10 * 10, DateTimeKind.Utc),
			"MILLISECOND" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond, DateTimeKind.Utc),
			"SECOND" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, DateTimeKind.Utc),
			"MINUTE" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, DateTimeKind.Utc),
			"HOUR" => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Utc),
			"DAY" => dt.Date,
			"MONTH" => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc),
			"YEAR" => new DateTime(dt.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			_ => throw new InvalidOperationException($"TIMESTAMP_TRUNC: unsupported part '{part}'.")
		};
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
		if (v is IList<object?> list) return string.Join(sep, list.Select(x => x?.ToString() ?? ""));
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
		if (elem is JsonElement je) return je.GetRawText();
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
