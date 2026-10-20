using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceForkJoinProjectionTests
{
    [TestCase("for(int i=0;i<count;i++){}")]
    [TestCase("for(int i=0;i<count;i++){var value=new int[2];}")]
    [TestCase("Leaf(count);", "static void Leaf(int n) {for(int i=0;i<n;i++) {}}")]
    public void EveryChildHasSeparateExactTypedAndCostOwnership(string body, string extra = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(extra);
        var (compilation, method) = Create(body, extra);
        var result = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.Projection, Is.Not.Null);
        var projection = result.Projection!;
        Assert.That(projection.RootCallableId, Is.EqualTo("root"));
        Assert.That(projection.FrameworkAssemblyIdentity, Does.Contain("System.Private.CoreLib"));
        Assert.That(projection.Children.Length, Is.EqualTo(2));
        Assert.That(projection.Children[0].Owner.CallableId, Is.EqualTo("root:fork-child:0"));
        Assert.That(projection.Children[1].Owner.CallableId, Is.EqualTo("root:fork-child:1"));
        Assert.That(projection.Children[0].Owner.Program.Factory, Is.Not.SameAs(projection.Children[1].Owner.Program.Factory));
        foreach (var child in projection.Children)
        {
            Assert.That(child.Owner.IsBodyAbstraction, Is.False);
            Assert.That(child.Owner.Result, Is.Null);
            Assert.That(child.Owner.Clauses, Is.Empty);
            Assert.That(child.Owner.ValidEffectClaimIds, Is.Empty);
            Assert.That(child.Owner.EffectsCompleteAtEntry, Is.True);
            Assert.That(child.Cost, Is.Not.Null);
            Assert.That(child.Owner.Program.Blocks.SelectMany(static block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions),
                Has.Some.Matches<IrInstruction>(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) :
                    instruction is IrBranchInstruction && child.Owner.Program.Factory.GetOperationInfo(instruction.Operation).Description is { } description &&
                    child.Owner.Program.Factory.GetString(description) == IrSourceLoopEntry.GuardMarker));
        }
    }

    [Test]
    public void NamedArgumentsPreserveFormalOrdinalsAndRootIdentity()
    {
        var source = "using System.Threading.Tasks; public static class C { " +
            "public static Task Root(int n,int m) => Task.WhenAll(Task.Run(()=>Child(right:m,left:n)),Task.Run(()=>Child(-3,n))); " +
            "static void Child(int left,int right) {for(int i=0;i<left;i++) {}} }";
        var (compilation, method) = Compile(source);
        var projection = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken).Projection!;
        Assert.That(projection, Is.Not.Null);
        Assert.That(projection.Children[0].Arguments, Is.EqualTo(new[] {
            new CompilerForkJoinArgument(1, 1, null), new CompilerForkJoinArgument(0, 0, null) }));
        Assert.That(projection.Children[1].Arguments, Is.EqualTo(new[] {
            new CompilerForkJoinArgument(0, null, -3), new CompilerForkJoinArgument(1, 0, null) }));
    }

    [TestCase("while(true) {}")]
    [TestCase("System.Console.WriteLine(count);")]
    [TestCase("Shared=count;", "static int Shared;")]
    [TestCase("for(int i=0;i<Shared;i++) {}", "static int Shared;")]
    [TestCase("Contract.Requires(count>=0); for(int i=0;i<count;i++) {}", "", "using SharpProof.Attributes;")]
    [TestCase("for(int i=0;i<count;i++) {}", "[System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() {}")]
    [TestCase("Leaf(count);", "static void Leaf(int n) {Contract.Requires(n>=0); for(int i=0;i<n;i++) {}}", "using SharpProof.Attributes;")]
    public void UnsupportedChildLeavesNoPartialProjection(string body, string extra = "", string imports = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(extra);
        ArgumentNullException.ThrowIfNull(imports);
        var (compilation, method) = Create(body, extra, imports);
        var result = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Projection, Is.Null);
        Assert.That(result.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void OneUnsupportedLaterChildCannotPublishTheFirstChild()
    {
        var (compilation, method) = Compile("using System.Threading.Tasks; public static class C { public static Task Root(int n) => Task.WhenAll(Task.Run(()=>Good(n)),Task.Run(()=>Bad(n))); static void Good(int n) {for(int i=0;i<n;i++){}} static void Bad(int n) {while(true){}} }");
        var result = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Projection, Is.Null);
        Assert.That(result.UnknownReason, Does.StartWith("ForkJoinChildCostUnknown:"));
    }

    [Test]
    public void AggregateChildWorkHasAFiniteProjectionBudget()
    {
        var body = "int value=count;" + string.Concat(Enumerable.Repeat("value++;", 300));
        var calls = string.Join(",", Enumerable.Repeat("Task.Run(()=>Child(n))", 16));
        var (compilation, method) = Compile("using System.Threading.Tasks; public static class C {public static Task Root(int n)=>Task.WhenAll(" + calls + "); static void Child(int count) {" + body + "}} ");
        var result = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Projection, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ForkJoinProjectionBudgetExceeded"));
    }

    [Test]
    public void CancellationAndForeignOwnershipAreNotConvertedIntoChildren()
    {
        var (compilation, method) = Create("for(int i=0;i<count;i++){}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action lower = () => { _ = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", cancellation.Token); };
        Assert.Throws<OperationCanceledException>(lower);
        var result = CompilerForkJoinResourceLowering.Lower(method, compilation.RemoveAllSyntaxTrees(), "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Projection, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ForkJoinShapeUnsupported"));
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public void CarrierCaptureUsesEmittedFields(OptimizationLevel optimization, bool configured)
    {
        var (initial, _) = Compile("using System.Threading.Tasks; public static class C { public static " +
            (configured ? "async Task" : "Task") + " Root(int n) => " + (configured ? "await " : "") +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3)))" +
            (configured ? ".ConfigureAwait(false)" : "") + "; static void Child(int n) {} }");
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(optimization));
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var capture = new CompilerTaskCarrierCollector(compilation).Capture(method, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Shapes, Is.Not.Null);
        Assert.That(capture.Shapes!.RootCallableId, Is.EqualTo("root"));
        Assert.That(capture.Shapes.ModuleSha256, Has.Length.EqualTo(64));
        Assert.That(capture.Shapes.CompilerIdentity, Does.Contain("Microsoft.CodeAnalysis.CSharp"));
        Assert.That(capture.Shapes.MatchedCallbacks, Is.EqualTo(2));
        Assert.That(capture.Shapes.CallbackFrames.SelectMany(static frame => frame is null ? throw new ArgumentNullException(nameof(frame)) : frame.Fields),
            Does.Contain(ResourceStorageKind.Word32));
        Assert.That(capture.Shapes.StateMachine != null, Is.EqualTo(configured));
        if (configured)
        {
            Assert.That(capture.Shapes.StateMachine!.IsValueType, Is.EqualTo(optimization == OptimizationLevel.Release));
            Assert.That(capture.Shapes.StateMachine.Fields, Does.Contain(ResourceStorageKind.Reference));
        }
    }

    [Test]
    public void CarrierCapturePreservesCancellationAndRejectsEmissionErrors()
    {
        var (compilation, method) = Create("");
        var collector = new CompilerTaskCarrierCollector(compilation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action capture = () => { _ = collector.Capture(method, "root", cancellation.Token); };
        Assert.Throws<OperationCanceledException>(capture);
        Assert.That(collector.Capture(method, "root", TestContext.CurrentContext.CancellationToken).Shapes, Is.Not.Null);
        var broken = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Broken { Missing value; }", (CSharpParseOptions)compilation.SyntaxTrees.First().Options));
        var brokenMethod = broken.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var failure = new CompilerTaskCarrierCollector(broken).Capture(brokenMethod, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(failure.Shapes, Is.Null);
        Assert.That(failure.UnknownReason, Is.EqualTo("TaskCarrierEmissionFailed"));
    }

    [Test]
    public void CarrierCaptureReturnsUnknownBeforeOversizedEmission()
    {
        var (initial, _) = Create("");
        var compilation = initial.AddSyntaxTrees(CSharpSyntaxTree.ParseText("/*" + new string('x', 1024 * 1024) + "*/", (CSharpParseOptions)initial.SyntaxTrees.First().Options));
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var capture = new CompilerTaskCarrierCollector(compilation).Capture(method, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierEmissionBudgetExceeded"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CarrierEmissionCachePreservesNestedNamespaceOwnership(bool configured)
    {
        var (compilation, method) = Compile("using System.Threading.Tasks; namespace A.B { public class Outer { public static class Inner { " +
            "public static " + (configured ? "async " : "") + "Task Root(int n) => " + (configured ? "await " : "") +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3)))" + (configured ? ".ConfigureAwait(false)" : "") +
            "; static void Child(int n) {} } } }");
        var collector = new CompilerTaskCarrierCollector(compilation);
        var first = collector.Capture(method, "first", TestContext.CurrentContext.CancellationToken);
        var second = collector.Capture(method, "second", TestContext.CurrentContext.CancellationToken);
        Assert.That(first.UnknownReason, Is.Null);
        Assert.That(second.UnknownReason, Is.Null);
        Assert.That(first.Shapes!.RootCallableId, Is.EqualTo("first"));
        Assert.That(second.Shapes!.RootCallableId, Is.EqualTo("second"));
        Assert.That(second.Shapes.ModuleSha256, Is.EqualTo(first.Shapes.ModuleSha256));
        Assert.That(second.Shapes.CallbackFrames.Select(static frame => frame is null ? throw new ArgumentNullException(nameof(frame)) : frame.MetadataName),
            Has.Some.StartsWith("A.B.Outer+Inner+"));
    }

    [Test]
    public void CarrierCaptureRejectsUnsupportedRootIdentityBeforeEmission()
    {
        var (compilation, method) = Create("");
        var capture = new CompilerTaskCarrierCollector(compilation).Capture(method, " ", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierRootUnsupported"));
    }

    [Test]
    public void CarrierCaptureRejectsAmbiguousEmittedParameterModes()
    {
        var (compilation, method) = Create("", "public static Task Root(ref int n) => Task.CompletedTask;");
        var capture = new CompilerTaskCarrierCollector(compilation).Capture(method, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierRootMetadataAmbiguous"));
    }

    [TestCase("types", "TaskCarrierMetadataBudgetExceeded")]
    [TestCase("callbacks", "TaskCarrierCallbackMetadataUnsupported")]
    [TestCase("fields", "TaskCarrierMetadataBudgetExceeded")]
    public void CarrierMetadataBudgetsRejectTheWholeCapture(string budget, string reason)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(reason);
        var extra = new System.Text.StringBuilder();
        if (budget == "types")
        {
            for (var index = 0; index < 4100; index++)
            { extra.Append("private class Nested").Append(index).Append(" {} "); }
        }
        else
        {
            var methods = budget == "callbacks" ? 33 : 8;
            for (var index = 0; index < methods; index++)
            {
                var arity = budget == "callbacks" ? index + 2 : index + 300;
                var parameters = string.Join(",", Enumerable.Range(0, arity).Select(static ordinal => "int p" + ordinal));
                var arguments = string.Join(",", Enumerable.Range(0, arity).Select(static ordinal => "p" + ordinal));
                extra.Append("public static Task Root(").Append(parameters).Append(") => Task.WhenAll(Task.Run(()=>Sink(")
                    .Append(arguments).Append(")),Task.Run(()=>Sink(").Append(arguments).Append("))); static void Sink(")
                    .Append(parameters).Append(") {} ");
            }
        }
        var (compilation, method) = Create("", extra.ToString());
        var capture = new CompilerTaskCarrierCollector(compilation).Capture(method, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo(reason));
    }
    private static (CSharpCompilation Compilation, IMethodSymbol Method) Create(string body, string extra = "", string imports = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(extra);
        ArgumentNullException.ThrowIfNull(imports);
        return Compile(imports + " using System.Threading.Tasks; public static class C {public static Task Root(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {" + body + "} " + extra + "}");
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method) Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(static method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Root" &&
                method.ParameterList.Parameters.All(static parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) : parameter.Modifiers.Count == 0))
            .OrderBy(static method => method is null ? throw new ArgumentNullException(nameof(method)) : method.ParameterList.Parameters.Count).First();
        return (compilation, (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!);
    }
}
