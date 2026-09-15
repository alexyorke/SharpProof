using System.Text;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class InvocationEmissionPolicyConcurrencyTests
{
    [TestCase(false, "expression")]
    [TestCase(true, "expression")]
    [TestCase(false, "block")]
    [TestCase(true, "block")]
    [TestCase(false, "anonymous")]
    [TestCase(true, "anonymous")]
    public void OmittedBranchingLambdaCreationPreservesNestedBodyAnalysis(bool enabled, string form)
    {
        var first = form switch
        {
            "expression" => "() => State++",
            "block" => "() => { State++; }",
            "anonymous" => "delegate { State++; }",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        var second = first.Replace("++", "--", StringComparison.Ordinal);
        var compilation = EffectTestHost.CreateCompilation(
            (enabled ? "#define TRACE_CALL\n" : "") +
            $$"""
            public static class Sample {
                public static int State;
                [System.Diagnostics.Conditional("TRACE_CALL")]
                public static void Accept(System.Action action) { action(); }
                public static void Caller() {
                    Accept(State == 0 ? (System.Action)({{first}}) : {{second}});
                }
            }
            """);
        RuntimeAssemblyTestHost.WithRuntimeAssembly(
            "ConditionalLambda", EffectTestHost.EmitImage(compilation).Image, assembly =>
            {
                var sample = assembly.GetType("Sample")!;
                sample.GetMethod("Caller")!.Invoke(null, null);
                Assert.That(sample.GetField("State")!.GetValue(null),
                    Is.EqualTo(enabled ? 1 : 0));
            });
        var session = new EffectAnalysisSession(compilation);
        var result = session.Analyze(EffectTestHost.SampleMethod(compilation, "Caller"));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var caller = EffectTestHost.SampleMethod(compilation, "Caller");
        var body = (IMethodBodyOperation)model.GetOperation(
            caller.DeclaringSyntaxReferences.Single().GetSyntax())!;
        var graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(body);
        var policy = new InvocationEmissionPolicy(compilation);
        foreach (var operation in graph.Blocks.SelectMany(block => block.Operations)
                     .SelectMany(operation => operation.DescendantsAndSelf())
                     .OfType<IDelegateCreationOperation>())
        {
            Assert.That(policy.IsElided(operation), Is.EqualTo(!enabled));
        }
        if (enabled)
        {
            Assert.That(result.Summary.Allocation, Is.Not.EqualTo(EffectAllocationKind.None));
        }
        else
        {
            Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        }
        foreach (var syntax in tree.GetRoot().DescendantNodes()
                     .OfType<AnonymousFunctionExpressionSyntax>())
        {
            var lambda = (IAnonymousFunctionOperation)model.GetOperation(syntax)!;
            Assert.That(policy.IsElided(lambda.Body), Is.False);
            Assert.That(session.Analyze(lambda.Symbol).Summary.Writes.Contains(
                EffectRegionId.Static()), Is.True);
        }
    }

    [TestCase("Caller", false)]
    [TestCase("Caller", true)]
    [TestCase("After", false)]
    [TestCase("After", true)]
    [TestCase("Catch", false)]
    [TestCase("Catch", true)]
    [TestCase("Local", false)]
    [TestCase("Local", true)]
    [TestCase("Nested", false)]
    [TestCase("Nested", true)]
    [TestCase("PlainArgument", false)]
    [TestCase("PlainArgument", true)]
    public void ConditionalAccessReceiverEffectsMatchEmittedCode(string methodName, bool enabled)
    {
        var compilation = EffectTestHost.CreateCompilation(
            (enabled ? "#define TRACE_CALL\n" : "") +
            """
            public sealed class Sample {
                public static int State;
                [System.Diagnostics.Conditional("TRACE_CALL")]
                public void Trace() { }
                public static Sample Receiver() { State++; return null; }
                public static void Caller() { Receiver()?.Trace(); }
                public static Sample ThrowReceiver() { throw new System.InvalidOperationException(); }
                public static void After() { ThrowReceiver()?.Trace(); State++; }
                public static void Catch() {
                    try { ThrowReceiver()?.Trace(); }
                    catch (System.InvalidOperationException) { State++; }
                }
                public static Sample GetSample(int value) { return null; }
                public static void Local() {
                    int value = 1;
                    GetSample(value = 0)?.Trace();
                    if (value == 1) State++;
                }
                public Sample Next() { return this; }
                public static void Nested() { Receiver()?.Next()?.Trace(); }
                [System.Diagnostics.Conditional("TRACE_CALL")]
                public static void ConditionalValue(int value) { }
                public static void PlainArgument() {
                    int value = 1;
                    ConditionalValue((value = 0) == 0 ? 1 : 2);
                    if (value == 1) State++;
                }
            }
            """);
        var runtimeWrites = false;
        RuntimeAssemblyTestHost.WithRuntimeAssembly(
            "ConditionalAccess", EffectTestHost.EmitImage(compilation).Image, assembly =>
            {
                var sample = assembly.GetType("Sample")!;
                try
                {
                    sample.GetMethod(methodName)!.Invoke(null, null);
                }
                catch (System.Reflection.TargetInvocationException exception)
                    when (exception.InnerException is InvalidOperationException)
                {
                }
                runtimeWrites = (int)sample.GetField("State")!.GetValue(null)! != 0;
            });
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.SampleMethod(compilation, methodName));
        Assert.That(result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.EqualTo(runtimeWrites));
    }

    [TestCase("After", false, true)]
    [TestCase("After", true, false)]
    [TestCase("Catch", false, false)]
    [TestCase("Catch", true, true)]
    [TestCase("AfterWrapper", false, true)]
    [TestCase("AfterWrapper", true, false)]
    [TestCase("CatchWrapper", false, false)]
    [TestCase("CatchWrapper", true, true)]
    [TestCase("AfterArgument", false, true)]
    [TestCase("AfterArgument", true, false)]
    [TestCase("CatchArgument", false, false)]
    [TestCase("CatchArgument", true, true)]
    public void ConditionalThrowControlsFollowingAndHandlerEffects(
        string methodName, bool enabled, bool writes)
    {
        var compilation = EffectTestHost.CreateCompilation(
            (enabled ? "#define TRACE_CALL\n" : "") +
            """
            public static class Sample {
                public static int State;
                [System.Diagnostics.Conditional("TRACE_CALL")]
                public static void Throw() { throw new System.InvalidOperationException(); }
                public static void After() { Throw(); State++; }
                public static void Catch() {
                    try { Throw(); }
                    catch (System.InvalidOperationException) { State++; }
                }
                public static void Wrapper() { Throw(); }
                public static void AfterWrapper() { Wrapper(); State++; }
                public static void CatchWrapper() {
                    try { Wrapper(); }
                    catch (System.InvalidOperationException) { State++; }
                }
                [System.Diagnostics.Conditional("TRACE_CALL")]
                public static void ConditionalValue(int value) { }
                public static int ThrowValue() { throw new System.InvalidOperationException(); }
                public static void AfterArgument() {
                    int value = 1;
                    ConditionalValue(value = 0);
                    if (value == 1) State++;
                }
                public static void CatchArgument() {
                    try { ConditionalValue(ThrowValue()); }
                    catch (System.InvalidOperationException) { State++; }
                }
            }
            """);
        RuntimeAssemblyTestHost.WithRuntimeAssembly(
            "ConditionalThrow", EffectTestHost.EmitImage(compilation).Image, assembly =>
            {
                var sample = assembly.GetType("Sample")!;
                try
                {
                    sample.GetMethod(methodName)!.Invoke(null, null);
                }
                catch (System.Reflection.TargetInvocationException exception)
                    when (exception.InnerException is InvalidOperationException)
                {
                }
                Assert.That(sample.GetField("State")!.GetValue(null),
                    Is.EqualTo(writes ? 1 : 0));
            });
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.SampleMethod(compilation, methodName));
        Assert.That(result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.EqualTo(writes));
    }

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
