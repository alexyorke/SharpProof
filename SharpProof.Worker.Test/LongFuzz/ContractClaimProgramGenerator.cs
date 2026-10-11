using System.Globalization;
using System.Text;

namespace SharpProof.Worker.Test.LongFuzz;

// Generates one compilable-or-not C# class C whose Target method carries a
// single claim ([DoesNotThrow], [EnforcePure], [ZeroAllocations] or an
// Ensures clause), plus Pre/Post mirrors of the contract for the CLR oracle.
internal sealed class ContractClaimProgramGenerator(Random random)
{
    internal const string DoesNotThrow = "DoesNotThrow";
    internal const string EnforcePure = "EnforcePure";
    internal const string ZeroAllocations = "ZeroAllocations";
    internal const string Ensures = "Ensures";

    private static readonly string[] s_modes = [DoesNotThrow, EnforcePure, ZeroAllocations, Ensures];
    private static readonly string[] s_comparisons = ["==", "!=", "<", ">=", ">"];
    private static readonly int[] s_simpleStatements = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21];
    private static readonly int[] s_simpleInts = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 21, 22, 23, 24, 25, 28, 29, 30];
    private static readonly string[] s_compoundOperators = [" += ", " -= ", " *= ", " /= ", " %= ", " &= ", " |= ", " ^= ", " <<= "];
    private static readonly string[] s_exceptions = ["InvalidOperationException()", "ArgumentException(\"m\")", "System.IO.IOException()"];
    private static readonly string[] s_extremes = ["int.MaxValue", "int.MinValue", "-1", "0"];
    private int _locals;
    private bool _simple;

    internal (string Source, string Mode, string Claim) Program()
    {
        _simple = Chance(60);
        var mode = s_modes[Next(s_modes.Length)];
        var requires = Chance(60) ? Bool([], Next(3) + 1, true) : "true";
        var body = new StringBuilder();
        Statements(body, [], 2, Next(5) + 1);
        body.Append("return ").Append(Int([], 2)).Append(';');
        var ensuresExpression = "true";
        if (mode == Ensures)
        {
            ensuresExpression = Next(6) switch
            {
                0 => "{R} == {R}",
                1 => "{R} >= int.MinValue",
                2 => "{R} " + s_comparisons[Next(5)] + " " + EnsuresInt(2),
                _ => EnsuresBool(3)
            };
        }
        var attribute = mode == Ensures ? "" : "[" + mode + "]";
        var ensures = mode == Ensures
            ? "Contract.Ensures(" + ensuresExpression.Replace("{R}", "Contract.Result<int>()", StringComparison.Ordinal)
                .Replace("{X0}", "Contract.Old(x)", StringComparison.Ordinal)
                .Replace("{Y0}", "Contract.Old(y)", StringComparison.Ordinal)
                .Replace("{S0}", "Contract.Old(State)", StringComparison.Ordinal)
                .Replace("{S}", "State", StringComparison.Ordinal) + ");"
            : "";
        var postExpression = ensuresExpression.Replace("{R}", "r", StringComparison.Ordinal)
            .Replace("{X0}", "x0", StringComparison.Ordinal).Replace("{Y0}", "y0", StringComparison.Ordinal)
            .Replace("{S0}", "s0", StringComparison.Ordinal).Replace("{S}", "st", StringComparison.Ordinal);
        var claim = (mode == Ensures ? ensures : attribute) + (requires == "true" ? "" : " requires " + requires);
        return (Wrap(attribute, requires, ensures, body.ToString(), postExpression), mode, claim);
    }

    private static string Wrap(string attribute, string requires, string ensures, string body, string postExpression)
    {
        return """
            using System;
            using SharpProof.Attributes;
            public static class C
            {
                public static int State;
                public static int Other;
                private static int Need(int v) { Contract.Requires(v > 0); return v - 1; }
                private static int Twice(int v) { Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(v) * 2)); return unchecked(v * 2); }
                private static int Bump(int v) { State++; return v; }
                private static int Clamp(int v) => v < 0 ? 0 : v > 9 ? 9 : v;
                private static int Thrower(int v) { if (v == 3) throw new InvalidOperationException(); return v; }
                private static int Sum(int[] items) { var total = 0; foreach (var item in items) total = unchecked(total + item); return total; }
                private static bool Positive(int v) { Contract.Ensures(Contract.Result<bool>() == (Contract.Old(v) > 0)); return v > 0; }
                ATTR
                public static int Target(int x, int y, int[]? a, string? s)
                {
                    Contract.Requires(REQ);
                    ENS
                    BODY
                }
                public static bool Pre(int x, int y, int[]? a, string? s) => REQ;
                public static bool Post(int r, int x0, int y0, int s0, int st) => POST;
            }
            """.Replace("ATTR", attribute, StringComparison.Ordinal).Replace("REQ", requires, StringComparison.Ordinal)
            .Replace("ENS", ensures, StringComparison.Ordinal).Replace("BODY", body, StringComparison.Ordinal)
            .Replace("POST", postExpression, StringComparison.Ordinal);
    }

    private int Next(int max)
    {
        return random.Next(max);
    }

    private bool Chance(int percent)
    {
        return random.Next(100) < percent;
    }

    private static string Literal(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private string EnsuresBool(int depth)
    {
        if (depth == 0 || Chance(25))
        {
            return Next(8) switch
            {
                0 => "{R} " + s_comparisons[Next(5)] + " " + EnsuresInt(1),
                1 => "Positive({R})",
                2 => "({R} is > 0 and < 10)",
                3 => "Clamp({R}) == {R}",
                4 => "{X0} > 0",
                5 => "({R} is 0 or 1 or -1)",
                6 => "{S} == {S0}",
                _ => "true"
            };
        }
        return Next(5) switch
        {
            0 => "(" + EnsuresBool(depth - 1) + " && " + EnsuresBool(depth - 1) + ")",
            1 => "(" + EnsuresBool(depth - 1) + " || " + EnsuresBool(depth - 1) + ")",
            2 => "!(" + EnsuresBool(depth - 1) + ")",
            3 => "(" + EnsuresBool(depth - 1) + " ? " + EnsuresBool(depth - 1) + " : " + EnsuresBool(depth - 1) + ")",
            _ => "(" + EnsuresBool(depth - 1) + " == " + EnsuresBool(depth - 1) + ")"
        };
    }

    private string EnsuresInt(int depth)
    {
        if (depth == 0 || Chance(35))
        {
            return Next(6) switch
            {
                0 => "{X0}",
                1 => "{Y0}",
                2 => "{S0}",
                3 => "{S}",
                _ => Literal(Next(11))
            };
        }
        return Next(4) switch
        {
            0 => "unchecked(" + EnsuresInt(depth - 1) + " + " + EnsuresInt(depth - 1) + ")",
            1 => "unchecked(" + EnsuresInt(depth - 1) + " - " + EnsuresInt(depth - 1) + ")",
            2 => "Math.Max(" + EnsuresInt(depth - 1) + ", " + EnsuresInt(depth - 1) + ")",
            _ => "(" + EnsuresInt(depth - 1) + " > 0 ? " + EnsuresInt(depth - 1) + " : " + EnsuresInt(depth - 1) + ")"
        };
    }

    private void Statements(StringBuilder sb, List<string> scope, int depth, int count)
    {
        var local = new List<string>(scope);
        for (var i = 0; i < count; i++)
        {
            Statement(sb, local, depth);
        }
    }

    private void Block(StringBuilder sb, List<string> scope, int depth)
    {
        sb.Append("{ ");
        Statements(sb, scope, depth, Next(3) + 1);
        sb.Append("} ");
    }

    private void Statement(StringBuilder sb, List<string> scope, int depth)
    {
        var choice = Next(depth > 0 ? 26 : 13);
        if (_simple)
        {
            do
            {
                choice = s_simpleStatements[Next(s_simpleStatements.Length)];
            }
            while (depth <= 0 && choice >= 13);
        }
        switch (choice)
        {
            case 0:
            case 1:
                {
                    var name = "v" + _locals++;
                    sb.Append("int ").Append(name).Append(" = ").Append(Int(scope, 2)).Append("; ");
                    scope.Add(name);
                    break;
                }
            case 2:
                sb.Append(Target(scope)).Append(" = ").Append(Int(scope, 2)).Append("; ");
                break;
            case 3:
                sb.Append(Target(scope))
                    .Append(s_compoundOperators[Next(s_compoundOperators.Length)])
                    .Append(Int(scope, 1)).Append("; ");
                break;
            case 4:
                sb.Append(Target(scope)).Append(Chance(50) ? "++; " : "--; ");
                break;
            case 5:
                sb.Append(Chance(70) ? "State = " + Int(scope, 1) + "; " : "Other += 1; ");
                break;
            case 6:
                sb.Append("if (").Append(Bool(scope, 1)).Append(") throw new ")
                    .Append(s_exceptions[Next(s_exceptions.Length)])
                    .Append("; ");
                break;
            case 7:
                sb.Append("if (").Append(Bool(scope, 1)).Append(") return ").Append(Int(scope, 1)).Append("; ");
                break;
            case 8:
                sb.Append("_ = ").Append(Int(scope, 2)).Append("; ");
                break;
            case 9:
                sb.Append("if (a != null && a.Length > 0) a[0] = ").Append(Int(scope, 1)).Append("; ");
                break;
            case 10:
                {
                    var name = "v" + _locals++;
                    sb.Append("var ").Append(name).Append(" = ").Append(Bool(scope, 1)).Append(" ? ").Append(Int(scope, 1))
                        .Append(" : ").Append(Int(scope, 1)).Append("; ");
                    scope.Add(name);
                    break;
                }
            case 11:
                sb.Append("Contract.Equals(0, 0); ");
                break;
            case 12:
                sb.Append("x = checked(").Append(Int(scope, 1)).Append(" + 1); ");
                break;
            case 13:
            case 14:
                sb.Append("if (").Append(Bool(scope, 2)).Append(") ");
                Block(sb, scope, depth - 1);
                if (Chance(60))
                {
                    sb.Append("else ");
                    Block(sb, scope, depth - 1);
                }
                break;
            case 15:
                {
                    var name = "i" + _locals++;
                    sb.Append("for (int ").Append(name).Append(" = 0; ").Append(name).Append(" < ").Append(Next(4)).Append("; ")
                        .Append(name).Append("++) ");
                    Block(sb, [.. scope, name], depth - 1);
                    break;
                }
            case 16:
                sb.Append("switch (").Append(Int(scope, 1)).Append(") { case 0: ");
                Statements(sb, scope, depth - 1, 1);
                sb.Append("break; case 1: case 2: ");
                Statements(sb, scope, depth - 1, 1);
                sb.Append("break; default: ");
                Statements(sb, scope, depth - 1, 1);
                sb.Append("break; } ");
                break;
            case 17:
                sb.Append("try ");
                Block(sb, scope, depth - 1);
                switch (Next(4))
                {
                    case 0:
                        sb.Append("catch (DivideByZeroException) ");
                        Block(sb, scope, depth - 1);
                        break;
                    case 1:
                        sb.Append("catch (Exception) ");
                        Block(sb, scope, depth - 1);
                        break;
                    case 2:
                        sb.Append("finally ");
                        Block(sb, scope, depth - 1);
                        break;
                    default:
                        sb.Append("catch (InvalidOperationException e) when (e.Message.Length > 0) ");
                        Block(sb, scope, depth - 1);
                        sb.Append("finally ");
                        Block(sb, scope, depth - 1);
                        break;
                }
                break;
            case 18:
                sb.Append(Chance(50) ? "checked " : "unchecked ");
                Block(sb, scope, depth - 1);
                break;
            case 19:
                {
                    var name = "e" + _locals++;
                    sb.Append("if (a != null) foreach (var ").Append(name).Append(" in a) ");
                    Block(sb, [.. scope, name], depth - 1);
                    break;
                }
            case 20:
                sb.Append("do ");
                Block(sb, scope, depth - 1);
                sb.Append("while (false); ");
                break;
            case 21:
                {
                    var name = "w" + _locals++;
                    sb.Append("int ").Append(name).Append(" = ").Append(Next(4)).Append("; while (").Append(name).Append(" > 0) { ")
                        .Append(name).Append("--; ");
                    Statements(sb, scope, depth - 1, 1);
                    sb.Append("} ");
                    break;
                }
            case 22:
                sb.Append("if (s is { Length: > 0 } text" + _locals++ + ") ");
                Block(sb, scope, depth - 1);
                break;
            case 23:
                {
                    var name = "t" + _locals++;
                    sb.Append("var (").Append(name).Append(", _) = (").Append(Int(scope, 1)).Append(", ").Append(Int(scope, 1))
                        .Append("); ");
                    scope.Add(name);
                    break;
                }
            case 24:
                {
                    var name = "r" + _locals++;
                    sb.Append("ref int ").Append(name).Append(" = ref ").Append(Chance(50) ? "x" : "State").Append("; ").Append(name)
                        .Append(" = ").Append(Int(scope, 1)).Append("; ");
                    break;
                }
            default:
                {
                    var name = "n" + _locals++;
                    sb.Append("int? ").Append(name).Append(" = ").Append(Chance(50) ? "null" : Int(scope, 1)).Append("; ");
                    sb.Append("x = ").Append(name).Append(" ?? ").Append(Int(scope, 1)).Append("; ");
                    break;
                }
        }
    }

    private string Target(List<string> scope)
    {
        var assignable = scope.Where(name => name.StartsWith('v') || name.StartsWith('t')).ToList();
        assignable.Add("x");
        assignable.Add("y");
        assignable.Add("State");
        return assignable[Next(assignable.Count)];
    }

    private string Int(List<string> scope, int depth)
    {
        if (depth == 0 || Chance(30))
        {
            return Next(10) switch
            {
                0 => "x",
                1 => "y",
                2 => scope.Count > 0 ? scope[Next(scope.Count)] : "x",
                3 => scope.Count > 0 ? scope[Next(scope.Count)] : "y",
                4 => "State",
                5 => s_extremes[Next(s_extremes.Length)],
                6 => _simple ? "(a == null ? 0 : a.Length)" : "(a?.Length ?? 0)",
                7 => _simple ? "(s == null ? -1 : s.Length)" : "(s?.Length ?? -1)",
                _ => Literal(Next(10))
            };
        }
        var a = Int(scope, depth - 1);
        var b = Int(scope, depth - 1);
        return (_simple ? s_simpleInts[Next(s_simpleInts.Length)] : Next(34)) switch
        {
            0 => "(" + a + " + " + b + ")",
            1 => "(" + a + " - " + b + ")",
            2 => "(" + a + " * " + b + ")",
            3 => "(" + a + " / " + b + ")",
            4 => "(" + a + " % " + b + ")",
            5 => "(-" + a + ")",
            6 => "checked(" + a + " + " + b + ")",
            7 => "unchecked(" + a + " * " + b + ")",
            8 => "(" + Bool(scope, depth - 1) + " ? " + a + " : " + b + ")",
            9 => "Math.Max(" + a + ", " + b + ")",
            10 => "Math.Abs(" + a + ")",
            11 => "(int)(long)" + a,
            12 => "(int)(byte)" + a,
            13 => "(" + a + " & " + b + ")",
            14 => "(" + a + " | " + b + ")",
            15 => "(" + a + " ^ " + b + ")",
            16 => "(" + a + " << " + Next(33) + ")",
            17 => "(" + a + " >> " + Next(33) + ")",
            18 => "(" + a + " switch { 0 => " + b + ", 1 => 7, _ => " + Int(scope, depth - 1) + " })",
            19 => "(" + a + ", " + b + ").Item" + (Next(2) + 1),
            20 => "Need(" + a + ")",
            21 => "Twice(" + a + ")",
            22 => "Bump(" + a + ")",
            23 => "Clamp(" + a + ")",
            24 => "Thrower(" + a + ")",
            25 => "(a != null ? Sum(a) : 0)",
            26 => "$\"{" + a + "}\".Length",
            27 => "new int[Clamp(" + a + ")].Length",
            28 => "(a != null && a.Length > 2 ? a[Clamp(" + a + ") % 3] : " + b + ")",
            29 => "(s != null && s.Length > 0 ? s[0] + " + a + " : " + b + ")",
            30 => "(Positive(" + a + ") ? 1 : 0)",
            31 => "(" + a + " is > 0 and < 5 ? " + b + " : 0)",
            32 => "~" + a,
            _ => "(int)(uint)(" + a + " >>> " + Next(33) + ")"
        };
    }

    private string Bool(List<string> scope, int depth, bool requiresOnly = false)
    {
        if (depth == 0 || Chance(25))
        {
            return Next(10) switch
            {
                0 => "a != null",
                1 => "s == null",
                2 => "x > 0",
                3 => _simple ? "(a != null && a.Length > 1)" : "a is { Length: > 1 }",
                4 => "true",
                6 => "a is null",
                7 => "s is not null",
                8 => "Positive(x)",
                9 => "(x is > 0 and < 5)",
                _ => "y != 0"
            };
        }
        if (requiresOnly)
        {
            // Keep Requires free of effects.
            var p = Next(4) switch
            {
                0 => "x",
                1 => "y",
                2 => "(a == null ? 0 : a.Length)",
                _ => "(s == null ? 0 : s.Length)"
            };
            var q = Next(3) switch
            {
                0 => "x",
                1 => "y",
                _ => Literal(Next(10))
            };
            return Next(11) switch
            {
                0 => p + " < " + q,
                1 => p + " != " + q,
                2 => "(" + Bool(scope, depth - 1, true) + " && " + Bool(scope, depth - 1, true) + ")",
                3 => "(" + Bool(scope, depth - 1, true) + " || " + Bool(scope, depth - 1, true) + ")",
                5 => "Positive(" + p + ")",
                6 => "Clamp(" + p + ") > " + q,
                7 => "(a == null || a.Length > " + q + ")",
                8 => "(s != null && s.Length == " + q + ")",
                9 => "(" + p + " is > 0 and < 5)",
                10 => "(" + Bool(scope, depth - 1, true) + " ? " + Bool(scope, depth - 1, true) + " : " +
                    Bool(scope, depth - 1, true) + ")",
                _ => "!(" + Bool(scope, depth - 1, true) + ")"
            };
        }
        return Next(9) switch
        {
            0 => Int(scope, depth - 1) + " < " + Int(scope, depth - 1),
            1 => Int(scope, depth - 1) + " == " + Int(scope, depth - 1),
            2 => "(" + Bool(scope, depth - 1) + " && " + Bool(scope, depth - 1) + ")",
            3 => "(" + Bool(scope, depth - 1) + " || " + Bool(scope, depth - 1) + ")",
            4 => "!(" + Bool(scope, depth - 1) + ")",
            5 => _simple ? "(" + Int(scope, depth - 1) + " >= 0)" : "(" + Int(scope, depth - 1) + " is 0 or 1)",
            7 => "(" + Bool(scope, depth - 1) + " ? " + Bool(scope, depth - 1) + " : " + Bool(scope, depth - 1) + ")",
            8 => "(" + Int(scope, depth - 1) + " is var p" + _locals++ + " && p" + (_locals - 1) + " > 0)",
            _ => "(" + Bool(scope, depth - 1) + " ^ " + Bool(scope, depth - 1) + ")"
        };
    }
}
