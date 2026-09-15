using System.Text;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class InvocationEmissionPolicyConcurrencyTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ConditionalOverrideUsesInheritedEmissionCondition(bool enabled)
    {
        var compilation = EffectTestHost.CreateCompilation(
            (enabled ? "#define TRACE_CALL\n" : "") +
            """
            public class Base {
                [System.Diagnostics.Conditional("TRACE_CALL")]
                public virtual void Trace(int value) { }
            }
            public class Middle : Base {
                public override void Trace(int value) { }
            }
            public sealed class Leaf : Middle {
                public override void Trace(int value) { }
            }
            public static class Sample {
                public static int State;
                public static void Caller() { new Leaf().Trace(++State); }
            }
            """);
        Assert.That(compilation.GetDiagnostics().Where(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        RuntimeAssemblyTestHost.WithRuntimeAssembly(
            "ConditionalOverride", EffectTestHost.EmitImage(compilation).Image, assembly =>
            {
                var sample = assembly.GetType("Sample")!;
                sample.GetMethod("Caller")!.Invoke(null, null);
                Assert.That(sample.GetField("State")!.GetValue(null),
                    Is.EqualTo(enabled ? 1 : 0));
            });
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)compilation.GetSemanticModel(tree)
            .GetOperation(syntax)!;
        Assert.That(new InvocationEmissionPolicy(compilation).IsElided(invocation),
            Is.EqualTo(!enabled));
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.SampleMethod(compilation, "Caller"));
        Assert.That(result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.EqualTo(enabled));
    }

    private const int TreeCount = 8;
    private const int MethodsPerTree = 60;
    private const int Rounds = 25;

    // One policy lives on each compilation's effect session and is shared by
    // every concurrent analyzer callback; its caches must survive racing
    // first inserts.
    [Test]
    public void SharedPolicyIsSafeForConcurrentCallers()
    {
        var compilation = EffectTestHost.CreateCompilation(
            Enumerable.Range(0, TreeCount).Select(CreateTree),
            "InvocationEmissionConcurrency");
        var invocations = compilation.SyntaxTrees
            .SelectMany(tree =>
            {
                var model = compilation.GetSemanticModel(tree);
                return tree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Select(syntax =>
                        (IInvocationOperation)model.GetOperation(syntax)!);
            })
            .ToArray();
        Assert.That(
            invocations,
            Has.Length.EqualTo(TreeCount * MethodsPerTree));
        var expected = invocations
            .Select(static invocation =>
                !invocation.TargetMethod.Name.StartsWith(
                    "Kept",
                    StringComparison.Ordinal))
            .ToArray();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount)
        };

        for (var round = 0; round < Rounds; round++)
        {
            var policy = new InvocationEmissionPolicy(compilation);
            var actual = new bool[invocations.Length];
            Parallel.For(
                0,
                invocations.Length,
                options,
                index => actual[index] = policy.IsElided(invocations[index]));

            Assert.That(actual, Is.EqualTo(expected), "round " + round);
        }
    }

    private static SyntaxTree CreateTree(int tree)
    {
        var declarations = new StringBuilder();
        var calls = new StringBuilder();
        for (var method = 0; method < MethodsPerTree; method++)
        {
            var suffix = FormattableString.Invariant($"{tree}_{method}");
            switch (method % 3)
            {
                case 0:
                    declarations.AppendLine(
                        "public static void Kept" + suffix + "() { }");
                    calls.AppendLine("Kept" + suffix + "();");
                    break;
                case 1:
                    declarations.AppendLine(
                        "[System.Diagnostics.Conditional(\"SHARPPROOF_UNDEFINED\")] " +
                        "public static void Conditional" + suffix + "() { }");
                    calls.AppendLine("Conditional" + suffix + "();");
                    break;
                default:
                    declarations.AppendLine(
                        "static partial void Partial" + suffix + "();");
                    calls.AppendLine("Partial" + suffix + "();");
                    break;
            }
        }

        var source =
            "public static partial class Sample {\n" +
            declarations +
            FormattableString.Invariant($"public static void Caller{tree}() {{\n") +
            calls +
            "}\n}\n";
        return CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
            path: FormattableString.Invariant($"Concurrency{tree}.cs"));
    }
}
