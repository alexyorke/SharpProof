using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static bool CheckConstrainedComparableIntrinsic(CSharpCompilation references, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        foreach (var (type, values) in IntegerComparisonEdges)
        {
            for (var left = 0; left < values.Length; left++)
            {
                for (var right = 0; right < values.Length; right++)
                {
                    var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Check() => Compare<" + type + ">(" + values[left] + ", " + values[right] + "); " +
                        "static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y); }");
                    var compilation = references.AddSyntaxTrees(tree);
                    if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
                    { return false; }
                    var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Check");
                    var model = compilation.GetSemanticModel(tree);
                    var factory = new IrFactory(IrExecutionSemantics.Total);
                    var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
                    var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
                        static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
                    if (!lowered.IsExact)
                    { return false; }
                    var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 4096, cancellationToken: TestContext.CurrentContext.CancellationToken);
                    var expected = CompareRuntime(type, left, right, comparer: false);
                    if (replay.Status != IrProgramExecutionStatus.Returned || replay.ConsumedApproximation ||
                        replay.ReturnValue!.IntegerNumericValue != expected + (wrongClaim ? 1 : 0))
                    { return false; }
                }
            }
        }
        return true;
    }

    [Test]
    public void ConstrainedComparableValueReceiverObservesArgumentMutationInTheClr()
    {
        var direct = 17;
        Assert.That(direct.CompareTo(direct = 23), Is.Zero);
        Assert.That(Mutate(17, 23), Is.Zero);
        Assert.That(Mutate(sbyte.MinValue, sbyte.MaxValue), Is.Zero);
        Assert.That(Mutate(ulong.MinValue, ulong.MaxValue), Is.Zero);

        foreach (var choose in new[] { true, false })
        {
            Assert.That(MutateConditional(17, 23, choose), Is.Zero);
            var data = new[] { 17 };
            Assert.That(data[0].CompareTo(choose ? (data[0] = 23) : (data[0] = 0)), Is.Zero);
        }

        static int MutateConditional<T>(T value, T replacement, bool choose) where T : IComparable<T>
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(replacement);
            return value.CompareTo(choose ? (value = replacement) : (value = default(T)!));
        }

        static int Mutate<T>(T value, T replacement) where T : IComparable<T>
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(replacement);
            return value.CompareTo(value = replacement);
        }
    }
}
