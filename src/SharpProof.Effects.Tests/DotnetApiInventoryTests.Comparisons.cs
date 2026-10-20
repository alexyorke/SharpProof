using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static readonly ImmutableArray<(string Type, ImmutableArray<string> Values)> IntegerComparisonEdges =
    [
        ("sbyte", ["sbyte.MinValue", "(sbyte)-1", "(sbyte)0", "(sbyte)1", "sbyte.MaxValue"]),
        ("byte", ["byte.MinValue", "(byte)1", "byte.MaxValue"]),
        ("short", ["short.MinValue", "(short)-1", "(short)0", "(short)1", "short.MaxValue"]),
        ("ushort", ["ushort.MinValue", "(ushort)1", "ushort.MaxValue"]),
        ("char", ["char.MinValue", "'\ud800'", "char.MaxValue"]),
        ("int", ["int.MinValue", "-1", "0", "1", "int.MaxValue"]),
        ("long", ["long.MinValue", "-1L", "0L", "1L", "long.MaxValue"]),
        ("uint", ["0U", "1U", "uint.MaxValue"]),
        ("ulong", ["0UL", "1UL", "ulong.MaxValue"])
    ];

    private static bool CheckIntegerComparisonIntrinsic(CSharpCompilation references, string api, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(api);
        foreach (var (type, values) in IntegerComparisonEdges)
        {
            var comparer = api.Contains("Comparer", StringComparison.Ordinal);
            var metadataType = type switch
            {
                "sbyte" => "SByte.",
                "byte" => "Byte.",
                "short" => "Int16.",
                "ushort" => "UInt16.",
                "char" => "Char.",
                "int" => "Int32.",
                "long" => "Int64.",
                "uint" => "UInt32.",
                _ => "UInt64."
            };
            if (!comparer && !api.Contains("System." + metadataType, StringComparison.Ordinal))
            { continue; }
            for (var left = 0; left < values.Length; left++)
            {
                for (var right = 0; right < values.Length; right++)
                {
                    var expression = comparer ? "System.Collections.Generic.Comparer<" + type + ">.Default.Compare(y: " + values[right] + ", x: " + values[left] + ")" :
                        "(" + values[left] + ").CompareTo(" + values[right] + ")";
                    var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Check() => " + expression + "; }");
                    var compilation = references.AddSyntaxTrees(tree);
                    if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
                    { return false; }
                    var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
                    var model = compilation.GetSemanticModel(tree);
                    var target = model.GetDeclaredSymbol(syntax)!;
                    var operation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
                    var factory = new IrFactory(IrExecutionSemantics.Total);
                    var lowered = new RoslynTotalExpressionLowerer(new TotalLoweringContext(factory, target)).LowerClause(operation);
                    if (!lowered.Classification.IsExact)
                    { return false; }
                    var interpreter = new IrInterpreter(factory);
                    var result = interpreter.Evaluate(lowered.Value).Value;
                    var safe = interpreter.Evaluate(lowered.SafeCondition).Value;
                    var expected = CompareRuntime(type, left, right, comparer);
                    if (safe?.Boolean != true || result?.IntegerNumericValue != expected + (wrongClaim ? 1 : 0))
                    { return false; }
                }
            }
        }
        return true;
    }

    private static int CompareRuntime(string type, int left, int right, bool comparer)
    {
        ArgumentNullException.ThrowIfNull(type);
        var count = IntegerComparisonEdges.Single(edge => edge.Type == type).Values.Length;
        ArgumentOutOfRangeException.ThrowIfNegative(left);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(left, count);
        ArgumentOutOfRangeException.ThrowIfNegative(right);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(right, count);
        return type switch
        {
            "sbyte" => CompareRuntimeValues<sbyte>([sbyte.MinValue, -1, 0, 1, sbyte.MaxValue], left, right, comparer),
            "byte" => CompareRuntimeValues<byte>([byte.MinValue, 1, byte.MaxValue], left, right, comparer),
            "short" => CompareRuntimeValues<short>([short.MinValue, -1, 0, 1, short.MaxValue], left, right, comparer),
            "ushort" => CompareRuntimeValues<ushort>([ushort.MinValue, 1, ushort.MaxValue], left, right, comparer),
            "char" => CompareRuntimeValues<char>([char.MinValue, '\ud800', char.MaxValue], left, right, comparer),
            "int" => CompareRuntimeValues<int>([int.MinValue, -1, 0, 1, int.MaxValue], left, right, comparer),
            "long" => CompareRuntimeValues<long>([long.MinValue, -1, 0, 1, long.MaxValue], left, right, comparer),
            "uint" => CompareRuntimeValues<uint>([0, 1, uint.MaxValue], left, right, comparer),
            "ulong" => CompareRuntimeValues<ulong>([0, 1, ulong.MaxValue], left, right, comparer),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static int CompareRuntimeValues<T>(ImmutableArray<T> values, int left, int right, bool comparer) where T : IComparable<T>
    {
        if (values.IsDefault)
        { throw new ArgumentNullException(nameof(values)); }
        var first = values[left];
        var second = values[right];
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return comparer ? Comparer<T>.Default.Compare(first, second) : first.CompareTo(second);
    }

    private static bool CheckCountInvariant(string api, bool wrongClaim)
    {
        foreach (var size in ImmutableArray.Create(
            0,
            1,
            65
        )

        )
        {
            var values = Enumerable.Range(0, size).ToImmutableArray();
            var observed = api switch
            {
                "P:System.Collections.Generic.List`1.Count" => new List<int>(values).Count,
                "P:System.Collections.Generic.Dictionary`2.Count" => values.ToDictionary(value => value).Count,
                "P:System.Collections.Generic.Queue`1.Count" => new Queue<int>(values).Count,
                "P:System.Collections.Generic.Stack`1.Count" => new Stack<int>(values).Count,
                _ => throw new InvalidOperationException("Unregistered count invariant.")
            };
            if ((observed >= 0) == wrongClaim)
            {
                return false;
            }
        }

        return true;
    }
}
