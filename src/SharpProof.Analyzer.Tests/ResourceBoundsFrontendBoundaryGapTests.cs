using System.Numerics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceBoundsFrontendBoundaryGapTests
{
    [Test]
    public void BodylessPartialDeclarationDoesNotInventZeroCost()
    {
        var result = Analyze("public static partial class C { static partial void Root(); }");
        AssertUnknown(result, "MissingBody");
    }

    [Test]
    public void UnknownFirstBranchDiscardsBothThePrefixAndTheCleanAlternative()
    {
        var result = Analyze("""
            public static class C {
                public static void Root(bool selected) {
                    var before = new int[2];
                    if(selected) { while(true) {} }
                    else { var alternative = new int[3]; }
                }
            }
            """);
        AssertUnknown(result, "UnsupportedLoop");
    }

    [TestCase(3000, true)]
    [TestCase(9000, false)]
    public void IndividuallyRepresentableLoopAndAllocationCostsShareTheOutputBudget(int nameLength, bool supported)
    {
        var count = "count" + new string('n', nameLength);
        var width = "width" + new string('m', nameLength);
        var result = Analyze("public static class C { public static void Root(int " + count + ", int " + width + ") { " +
            "for(int i=0;i<" + count + ";i++) {} var data = new int[" + width + "]; } }");
        if (!supported)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(result.Bounds!.LoopIterations.Expression.Evaluate(static _ => 2, cancellation), Is.EqualTo(new BigInteger(2)));
        Assert.That(result.Bounds.Arrays.Constant, Is.EqualTo(BigInteger.One));
        Assert.That(result.Bounds.Elements.Expression.Evaluate(static _ => 3, cancellation), Is.EqualTo(new BigInteger(3)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SequentialAllocationProgramsObeyTheConstructionBudget(bool exhausted)
    {
        var count = exhausted ? ResourceExpression.MaximumDepth + 1 : ResourceExpression.MaximumDepth / 2;
        var allocations = string.Concat(Enumerable.Repeat("_ = new int[2];", count));
        var result = Analyze("public static class C { public static void Root() { " + allocations + " } }");
        if (exhausted)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(new BigInteger(count)));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(count) * 2));
    }

    [TestCase(8, true)]
    [TestCase(9, false)]
    public void SeparatelyAdmittedBooleanBranchesShareTheCompositionBudget(int depth, bool supported)
    {
        var whenTrue = BalancedAllocations(depth, 2);
        var whenFalse = BalancedAllocations(depth, 3);
        foreach (var (body, elements) in new[] { (whenTrue, 2), (whenFalse, 3) })
        {
            var branch = Analyze("public static class C { public static void Root(bool branch) { " + body + " } }");
            Assert.That(branch.Proven, Is.True, branch.UnknownReason);
            Assert.That(branch.Bounds!.Arrays.Constant, Is.EqualTo(BigInteger.One));
            Assert.That(branch.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(elements)));
        }
        var result = Analyze("using SharpProof.Attributes; public static class C { " +
            "public static void Root([ResourceBooleanInput(1,1)] bool selected, bool branch) { " +
            "if(selected) { " + whenTrue + " } else { " + whenFalse + " } } }");
        if (!supported)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(BigInteger.One));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(3)));
        Assert.That(result.Bounds.Program.Kind, Is.EqualTo(ResourceProgramKind.BooleanDistribution));
    }

    [TestCase(7, true)]
    [TestCase(8, false)]
    public void ExpandedScalarHelpersCannotBypassTheRecurrenceBudget(int levels, bool supported)
    {
        var source = new StringBuilder("public static class C { public static void Root() { M0(3,new int[3]); } ");
        for (var index = 0; index < levels; index++)
        {
            source.Append("static void M").Append(index).Append("(int n,int[] data) { int first=n,second=n; first+=second; M")
                .Append(index + 1).Append("(n,data); M").Append(index + 1).Append("(n,data); } ");
        }
        source.Append("static void M").Append(levels)
            .Append("(int n,int[] data) { if(n<=0)return; var a=new int[data.Length]; M")
            .Append(levels).Append("(n-1,data); } }");
        var result = Analyze(source.ToString());
        if (!supported)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        var arrays = BigInteger.One + (BigInteger.One << levels) * 3;
        Assert.That(result.Bounds!.LoopIterations.Constant, Is.EqualTo(BigInteger.Zero));
        Assert.That(result.Bounds.Arrays.Constant, Is.EqualTo(arrays));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(arrays * 3));
    }

    private static string BalancedAllocations(int depth, int elements)
    {
        if (depth == 0)
        { return "_ = new int[" + elements.ToString(System.Globalization.CultureInfo.InvariantCulture) + "];"; }
        var child = BalancedAllocations(depth - 1, elements);
        return "if(branch) { " + child + " } else { " + child + " }";
    }

    private static ResourceBoundsAnalysis.Result Analyze(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(compilation.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot(cancellation).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(static method =>
            method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Root");
        var model = compilation.GetSemanticModel(tree);
        var body = declaration.Body is { } block ? model.GetOperation(block, cancellation) :
            declaration.ExpressionBody is { } arrow ? model.GetOperation(arrow.Expression, cancellation) : null;
        return ResourceBoundsAnalysis.Analyze((IMethodSymbol)model.GetDeclaredSymbol(declaration, cancellation)!, body, cancellation);
    }

    private static void AssertUnknown(ResourceBoundsAnalysis.Result result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.Proven, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.Assumptions, Is.Empty);
    }
}
