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
            .Single(static method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Root");
        return (compilation, (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!);
    }
}
