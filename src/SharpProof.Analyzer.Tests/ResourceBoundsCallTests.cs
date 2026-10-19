using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

public sealed partial class ResourceBoundsTests
{
    private static ResourceBoundsAnalysis.Result AnalyzeSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new ArgumentNullException(nameof(d)) :
            d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Identifier.ValueText == "Root");
        var model = compilation.GetSemanticModel(tree);
        return ResourceBoundsAnalysis.Analyze((IMethodSymbol)model.GetDeclaredSymbol(method)!,
            method.Body is { } body ? model.GetOperation(body) : model.GetOperation(method.ExpressionBody!.Expression),
            TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void SourceCallsSubstituteFormalParameterSizesBySymbol()
    {
        var result = AnalyzeSource("""
            public static class C {
                public static void Root(int n) { Leaf(n); }
                private static void Leaf(int count) {
                    for (int i=0; i<count; i++) { var a = new int[2]; }
                }
            }
            """);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo("max(0, n)"));
        Assert.That(result.Bounds.Arrays.Text, Is.EqualTo("max(0, n)"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("(max(0, n) * 2)"));
    }

    [Test]
    public void AllocationArgumentsAreChargedOnceAndArrayLengthIsSubstituted()
    {
        var result = AnalyzeSource("""
            public static class C {
                public static void Root() { Leaf(new int[3]); }
                private static void Leaf(int[] data) {
                    for (int i=0; i<data.Length; i++) { var a = new int[2]; }
                }
            }
            """);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo("3"));
        Assert.That(result.Bounds.Arrays.Text, Is.EqualTo("4"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("9"));
    }

    [Test]
    public void SourceCallsComposeInsideDependentLoops()
    {
        var result = AnalyzeSource("""
            public static class C {
                public static void Root(int n) { for(int i=0; i<n; i++) Leaf(i); }
                private static void Leaf(int m) { for(int j=0; j<m; j++) {} }
            }
            """);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo("(max(0, n) * (1 + max(0, n)))"));
    }

    [Test]
    public void NamedArgumentsAndMultiLevelSummariesCompose()
    {
        var result = AnalyzeSource("""
            public static class C {
                public static void Root() { Middle(b:3, a:2); Middle(a:1, b:4); }
                private static void Middle(int a, int b) { Leaf(b,a); }
                private static void Leaf(int size, int count) {
                    for(int i=0; i<count; i++) { var a = new byte[size]; }
                }
            }
            """);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("3"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("10"));
    }

    [TestCase("public static void Root() { Root(); }", "RecursiveCostUnknown")]
    [TestCase("public static void Root() { Leaf(); } static void Leaf() { Root(); }", "RecursiveCostUnknown")]
    [TestCase("public static void Root() { System.Console.WriteLine(1); }", "OpaqueCall")]
    [TestCase("public void Root() { Leaf(); } void Leaf() {}", "UnsupportedCall")]
    [TestCase("static int[] Hidden = new int[4]; public static void Root() { Leaf(); } static void Leaf() {}", "CallTypeInitializationUnknown")]
    [TestCase("public static void Root(int n) { Leaf(n+1); } static void Leaf(int x) { for(int i=0; i<x; i++) {} }", "UnsupportedLoopBound")]
    [TestCase("public static void Root(int n) { Leaf(ref n); } static void Leaf(ref int x) {}", "UnsupportedCall")]
    public void UnsupportedCallBoundariesStayExplicit(string methods, string reason)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(reason);
        var result = AnalyzeSource("public class C { " + methods + " }");
        Assert.That(result.Proven, Is.False);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.Bounds, Is.Null);
    }

    [Test]
    public void RepeatedCallExpansionSharesTheRootBudget()
    {
        var methods = string.Concat(Enumerable.Range(0, 13).Select(index =>
            $"static void M{index}() {{ M{index + 1}(); M{index + 1}(); }}"));
        var result = AnalyzeSource("public class C { public static void Root() { M0(); } " + methods + " static void M13() {} }");
        Assert.That(result.UnknownReason, Is.EqualTo("AnalysisBudgetExceeded"));
        Assert.That(result.Bounds, Is.Null);
    }
    [TestCase(false, "0")]
    [TestCase(true, "1")]
    public void ConditionalCallElisionIncludesArgumentAllocations(bool defined, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var source = (defined ? "#define RESOURCE_RUNTIME\n" : "") + """
            public static class C {
                [System.Diagnostics.Conditional("RESOURCE_RUNTIME")]
                static void Sink(int[] values) {}
                public static void Root() { Sink(new int[4]); }
            }
            """;
        var result = AnalyzeSource(source);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo(expected));
    }

    [Test]
    public void CompilerElidedContractArgumentsDoNotAddRuntimeCosts()
    {
        var result = AnalyzeSource("""
            public static class C {
                public static void Root() { SharpProof.Attributes.Contract.Requires(new int[4].Length == 4); }
            }
            """);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("0"));
    }
}
