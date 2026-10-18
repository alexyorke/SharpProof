using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static readonly (string Type, string[] Values)[] IntegerComparisonEdges = [("int", ["int.MinValue", "-1", "0", "1", "int.MaxValue"]), ("long", ["long.MinValue", "-1L", "0L", "1L", "long.MaxValue"]), ("uint", ["0U", "1U", "uint.MaxValue"]), ("ulong", ["0UL", "1UL", "ulong.MaxValue"])];
    private static bool CheckIntegerComparisonIntrinsic(CSharpCompilation references, string api, bool wrongClaim)
    {
        foreach (var (type, values) in IntegerComparisonEdges)
        {
            var comparer = api.Contains("Comparer", StringComparison.Ordinal);
            var metadataType = type switch
            {
                "int" => "Int32.",
                "long" => "Int64.",
                "uint" => "UInt32.",
                _ => "UInt64."
            };
            if (!comparer && !api.Contains(metadataType, StringComparison.Ordinal))
            {
                continue;
            }

            for (var left = 0; left < values.Length; left++)
            {
                for (var right = 0; right < values.Length; right++)
                {
                    var expression = comparer ? "System.Collections.Generic.Comparer<" + type + ">.Default.Compare(y: " + values[right] + ", x: " + values[left] + ")" : "(" + values[left] + ").CompareTo(" + values[right] + ")";
                    var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Check() => " + expression + "; }");
                    var compilation = references.AddSyntaxTrees(tree);
                    if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)))
                    {
                        return false;
                    }

                    var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
                    var model = compilation.GetSemanticModel(tree);
                    var target = model.GetDeclaredSymbol(syntax)!;
                    var operation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
                    var factory = new IrFactory(IrExecutionSemantics.Total);
                    var lowered = new RoslynTotalExpressionLowerer(new TotalLoweringContext(factory, target)).LowerClause(operation);
                    if (!lowered.Classification.IsExact)
                    {
                        return false;
                    }

                    var interpreter = new IrInterpreter(factory);
                    var result = interpreter.Evaluate(lowered.Value).Value;
                    var safe = interpreter.Evaluate(lowered.SafeCondition).Value;
                    var expected = CompareRuntime(type, left, right);
                    if (safe?.Boolean != true || result?.IntegerNumericValue != expected + (wrongClaim ? 1 : 0))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static int CompareRuntime(string type, int left, int right)
    {
        global::System.ArgumentNullException.ThrowIfNull(type);
        System.ArgumentOutOfRangeException.ThrowIfNegative(left);
        System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(left, type is "int" or "long" ? 5 : 3);
        System.ArgumentOutOfRangeException.ThrowIfNegative(right);
        System.ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(right, type is "int" or "long" ? 5 : 3);
        return type switch
        {
            "int" => new[]
            {
                int.MinValue,
                -1,
                0,
                1,
                int.MaxValue
            }[left].CompareTo(new[] { int.MinValue, -1, 0, 1, int.MaxValue }[right]),
            "long" => Comparer<long>.Default.Compare(new[] { long.MinValue, -1, 0, 1, long.MaxValue }[left], new[] { long.MinValue, -1, 0, 1, long.MaxValue }[right]),
            "uint" => Comparer<uint>.Default.Compare(new[] { 0U, 1U, uint.MaxValue }[left], new[] { 0U, 1U, uint.MaxValue }[right]),
            _ => Comparer<ulong>.Default.Compare(new[] { 0UL, 1UL, ulong.MaxValue }[left], new[] { 0UL, 1UL, ulong.MaxValue }[right])
        };
    }

    private static bool CheckCountInvariant(string api, bool wrongClaim)
    {
        foreach (var size in new[]
        {
            0,
            1,
            65
        }

        )
        {
            var values = Enumerable.Range(0, size).ToArray();
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
