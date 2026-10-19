using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed partial class ResourceBoundsTests
{
    private static ResourceBoundsAnalysis.Result Analyze(string body, string parameters = "int n, int m, int[] values")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);
        var compilation = AnalyzerTestHost.CreateCompilation(
            $"public static class C {{ public static void M({parameters}) {{ {body} }} }}", []);
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new ArgumentNullException(nameof(d)) : d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        return ResourceBoundsAnalysis.Analyze((IMethodSymbol)model.GetDeclaredSymbol(method)!,
            model.GetOperation(method.Body!), TestContext.CurrentContext.CancellationToken);
    }

    [TestCase("", "0", "0", "0")]
    [TestCase("int sum = 0; sum += n;", "0", "0", "0")]
    [TestCase("var a = new int[4];", "0", "1", "4")]
    [TestCase("var a = new int[n];", "0", "1", "max(0, n)")]
    [TestCase("var a = new byte[values.Length];", "0", "1", "values.Length")]
    [TestCase("var a = n>0 ? new int[2] : new int[4];", "0", "1", "4")]
    [TestCase("for (int i=0; i<4; i++) { var a = new int[3]; }", "4", "4", "12")]
    [TestCase("for (int i=0; i<n; i++) { var a = new int[3]; }", "max(0, n)", "max(0, n)", "(max(0, n) * 3)")]
    [TestCase("for (int i=0; i<values.Length; ++i) { values[i] = i; }", "values.Length", "0", "0")]
    [TestCase("for (int i=0; i<2; i++) { for (int j=0; j<3; j++) { var a = new int[4]; } }", "8", "6", "24")]
    [TestCase("for (int i=0; i<2; i++) {} for (int j=0; j<3; j++) {}", "5", "0", "0")]
    [TestCase("if (n>0) { var a = new int[3]; } else { var b = new int[5]; }", "0", "1", "5")]
    [TestCase("for (int i=0; i<0; i++) { var a = new int[4]; }", "0", "0", "0")]
    [TestCase("for (int i=0; i< -1; i++) {}", "0", "0", "0")]
    [TestCase("for (int i=0; i<2147483647; i++) {}", "2147483647", "0", "0")]
    [TestCase("for (int i=0; i<3; i++) { if (n>0) return; var a = new int[2]; }", "3", "3", "6")]
    public void SupportedBodiesHaveProvenUpperBounds(string body, string loops, string arrays, string elements)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(loops);
        ArgumentNullException.ThrowIfNull(arrays);
        ArgumentNullException.ThrowIfNull(elements);
        var result = Analyze(body);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Bounds!.LoopIterations.Text, Is.EqualTo(loops));
            Assert.That(result.Bounds.Arrays.Text, Is.EqualTo(arrays));
            Assert.That(result.Bounds.Elements.Text, Is.EqualTo(elements));
            Assert.That(result.UnknownReason, Is.Null);
        }
    }

    [TestCase("while (n>0) {}", "UnsupportedLoop")]
    [TestCase("for (int i=0; i<=n; i++) {}", "UnsupportedLoop")]
    [TestCase("for (int i=0; i<n; i+=2) {}", "LoopCounterMayOverflow")]
    [TestCase("for (int i=0; i<n; i++) { i=0; }", "UnstableInputOrCounter")]
    [TestCase("for (int i=0; i<n; i++) { n++; }", "UnstableInputOrCounter")]
    [TestCase("n=0; for (int i=0; i<n; i++) {}", "UnstableInputOrCounter")]
    [TestCase("for (int i=0; i<values.Length; i++) { values = new int[3]; }", "UnstableInputOrCounter")]
    [TestCase("var a = new int[n+1];", "UnsupportedAllocationSize")]
    [TestCase("var a = new object();", "UnsupportedLocal")]
    [TestCase("var a = new int[2,3];", "UnsupportedLocal")]
    [TestCase("var a = new int[] { 1,2 };", "UnsupportedOperation:ArrayCreation")]
    [TestCase("object a = n;", "UnsupportedLocal")]
    [TestCase("System.Console.WriteLine(n);", "OpaqueCall")]
    [TestCase("M(n,m,values);", "RecursiveCostUnknown")]
    [TestCase("for (int i=0; i<n; i++) { break; }", "UnsupportedOperation:Branch")]
    [TestCase("for (int i=0; i<n; i++) { continue; }", "UnsupportedOperation:Branch")]
    [TestCase("try {} finally {}", "UnsupportedOperation:Try")]
    [TestCase("ref int x = ref n;", "UnsupportedLocal")]
    [TestCase("System.Func<int> f = () => n;", "UnsupportedLocal")]
    public void UnsupportedBodiesReturnExplicitUnknownWithoutPartialBounds(string body, string reason)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(reason);
        var result = Analyze("var allocatedBeforeGap = new int[3]; " + body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Proven, Is.False);
            Assert.That(result.Bounds, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo(reason));
        }
    }

    [Test]
    public void SymbolicIndependentNestingMultipliesAllocationCosts()
    {
        var result = Analyze("for (int i=0; i<n; i++) { for (int j=0; j<m; j++) { var a = new int[n]; } }");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("(max(0, n) * max(0, m))"));
        Assert.That(result.Bounds.Elements.Text, Is.EqualTo("(max(0, n) * (max(0, m) * max(0, n)))"));
    }

    [Test]
    public void MathematicalCostArithmeticDoesNotOverflowClrIntegers()
    {
        var result = Analyze("for (int i=0; i<2147483647; i++) { for (int j=0; j<2147483647; j++) { var a = new byte[2147483647]; } }");
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Elements.Constant, Is.GreaterThan(new System.Numerics.BigInteger(long.MaxValue)));
    }

    [Test]
    public void CanonicalCounterExecutionAgreesWithIndependentSmallInputCounts()
    {
        // Independently execute the algorithm and observe allocation events and
        // elements for negative, empty and positive iteration domains.
        for (int n = -2; n <= 8; n++)
        {
            for (int m = -2; m <= 8; m++)
            {
                int loops = 0, arrays = 0, elements = 0;
                for (int i = 0; i < n; i++)
                {
                    loops++;
                    for (int j = 0; j < m; j++)
                    {
                        loops++;
                        var allocated = new int[3];
                        arrays++;
                        elements += allocated.Length;
                    }
                }
                var result = Analyze($"for (int i=0; i<{Math.Max(0, n)}; i++) {{ for (int j=0; j<{Math.Max(0, m)}; j++) {{ var a = new int[3]; }} }}");
                Assert.That(result.Proven, Is.True);
                Assert.That(result.Bounds!.LoopIterations.Constant, Is.EqualTo(new System.Numerics.BigInteger(loops)));
                Assert.That(result.Bounds.Arrays.Constant, Is.EqualTo(new System.Numerics.BigInteger(arrays)));
                Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new System.Numerics.BigInteger(elements)));
            }
        }
    }

    [Test]
    public void UnsupportedCallablesAreUnknown()
    {
        Assert.That(Analyze("", "ref int n").UnknownReason, Is.EqualTo("UnsupportedCallable"));
    }

    [Test]
    public void CancellationPropagates()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("public class C { public void M() {} }", []);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        Action canceled = () => ResourceBoundsAnalysis.Analyze(
            (IMethodSymbol)model.GetDeclaredSymbol(method)!, model.GetOperation(method.Body!), new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(canceled);
    }

    [Test]
    public void BudgetExhaustionIsUnknown()
    {
        var result = Analyze(string.Concat(Enumerable.Repeat("{ int x = 0; x++; }", 900)));
        Assert.That(result.UnknownReason, Is.EqualTo("AnalysisBudgetExceeded"));
        Assert.That(result.Bounds, Is.Null);
    }

    [Test]
    public async Task AnalyzerReportsProvenAndUnknownWithMachineReadableProperties()
    {
        const string source = "public class C { public void P(int n) { for (int i=0; i<n; i++) { var a = new int[2]; } } public void U() { while(true) {} } }";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, null, ["SP0060"]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0060", 2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics[0].Properties["ResourceStatus"], Is.EqualTo("Proven"));
            Assert.That(diagnostics[0].Properties["LoopIterationsUpperBound"], Is.EqualTo("max(0, n)"));
            Assert.That(diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("cumulative elements"));
            Assert.That(diagnostics[1].Properties["ResourceStatus"], Is.EqualTo("Unknown"));
            Assert.That(diagnostics[1].Properties.ContainsKey("AllocatedElementsUpperBound"), Is.False);
            Assert.That(diagnostics[1].Properties["UnknownReason"], Is.EqualTo("UnsupportedLoop"));
        }
    }

    [Test]
    public async Task DefaultAndOffProfilesDoNotReportResources()
    {
        const string source = "public class C { public void M() { while(true) {} } }";
        Assert.That(await AnalyzerTestHost.AnalyzeAsync(source, null, []), Is.Empty);
        Assert.That(await AnalyzerTestHost.AnalyzeAsync(source, null, ["SP0060"], profile: "off"), Is.Empty);
    }

    [Test]
    public async Task EditorConfigActivationWorksWithoutExistingContracts()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("public class C { public int M() => 1; }", ["SP0060"])
            .WithReferences(TestMetadataReferences.WithoutSharpProof);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation,
            new Dictionary<string, string> { ["dotnet_diagnostic.SP0060.severity"] = "suggestion" });
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Properties["ResourceStatus"], Is.EqualTo("Proven"));
    }

    [Test]
    public async Task EditorConfigCanDisableResourceAnalysis()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("public class C { public void M() { while(true) {} } }", ["SP0060"]);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation,
            new Dictionary<string, string> { ["dotnet_diagnostic.SP0060.severity"] = "none" });
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task GeneratedCodeIsSkippedAndMalformedCodeIsUnknown()
    {
        Assert.That(await AnalyzerTestHost.AnalyzeAsync("// <auto-generated/>\npublic class C { public void M() {} }",
            null, ["SP0060"]), Is.Empty);
        var malformed = await AnalyzerTestHost.AnalyzeAsync("public class C { public void M() { Missing(); } }",
            null, ["SP0060"], allowCompilationErrors: true);
        Assert.That(malformed.Single().Properties["UnknownReason"], Is.EqualTo("UnsupportedOperation:Invalid"));
    }

    [Test]
    public async Task GeneratedCodeAttributesAreRespected()
    {
        const string source = "public class C { [System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")] public void M() {} }";
        Assert.That(await AnalyzerTestHost.AnalyzeAsync(source, null, ["SP0060"]), Is.Empty);
    }
}
