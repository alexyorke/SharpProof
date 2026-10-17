using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerCfgConstructionBoundaryTests
{
    [TestCase(2100, "AE050150ED4DDF971D30C0A807739946C6FAD01E7EB0083EE0F7F03276BD17E1", false)]
    [TestCase(64, "B81010213D6F7DD51702DE13A91382E6BD31EB45141C5A2E4B1E639901EED83E", true)]
    public void FrozenIndependentBranchProgramsReportTheirActualCfgAndLowering(
        int expectedIfCount, string expectedHash, bool positiveControl)
    {
        System.ArgumentNullException.ThrowIfNull(expectedHash);
        var name = "branch-" + expectedIfCount;
        var path = name + ".cs";
        var sourceBuilder = new StringBuilder("#nullable enable\npublic static class LunaCfgProbe\n{\n    public static int Target(int x)\n    {\n");
        for (var index = 0; index < expectedIfCount; index++)
        {
            sourceBuilder.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"        if (x == {index}) return {index};\n");
        }
        sourceBuilder.Append("        return 17;\n    }\n}\n");
        var source = sourceBuilder.ToString();
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Assert.That(sourceHash, Is.EqualTo(expectedHash), name + " frozen source hash changed");

        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            path);
        var compilation = CSharpCompilation.Create(
            "LunaCfgProbe_" + name.Replace("-", "_", StringComparison.Ordinal),
            [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                generalDiagnosticOption: ReportDiagnostic.Error,
                nullableContextOptions: NullableContextOptions.Enable));
        var diagnostics = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
            .ToArray();
        Assert.That(diagnostics, Is.Empty, string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var loadContext = new AssemblyLoadContext("LunaCfgProbe_" + name, isCollectible: true);
        try
        {
            stream.Position = 0;
            var assembly = loadContext.LoadFromStream(stream);
            var target = assembly.GetType("LunaCfgProbe")!.GetMethod("Target")!;
            var native = (int)target.Invoke(null, new object[] { -1 })!;

            var methodSyntax = tree.GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
            var model = compilation.GetSemanticModel(tree);
            var graph = ControlFlowGraph.Create(methodSyntax, model)!;
            var actualIfCount = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Count();
            Assert.That(actualIfCount, Is.EqualTo(expectedIfCount));

            var factory = new IrFactory(IrExecutionSemantics.Total);
            var method = (IMethodSymbol)model.GetDeclaredSymbol(methodSyntax)!;
            var context = new TotalLoweringContext(factory, method);
            var lowering = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);

            WriteEvidence(
                $"case={name}; source_sha256={sourceHash}; strict_diagnostics=0; emit={emitted.Success}; " +
                $"native_fallthrough={native}; cfg_blocks={graph.Blocks.Length}; if_count={actualIfCount}; " +
                $"lowering_exact={lowering.IsExact}; abstention={lowering.Classification.Abstention}; " +
                $"construction_limit_exceeded={lowering.ConstructionLimitExceeded}; ir_blocks={lowering.Program.Blocks.Length}");

            Assert.That(native, Is.EqualTo(17));
            if (positiveControl)
            {
                Assert.That(graph.Blocks.Length, Is.LessThan(RoslynTotalProgramLowerer.MaximumRegionSteps));
                Assert.That(lowering.IsExact, Is.True);
                Assert.That(lowering.ConstructionLimitExceeded, Is.False);
                var inputs = context.Parameters.ToDictionary(
                    binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry),
                    binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (factory.CreateIntegerValue(factory.GetVariableInfo(binding.Entry).Type, -1L)));
                var replay = new IrProgramInterpreter(factory).Execute(lowering.Program, inputs);
                WriteEvidence(
                    $"case={name}; ir_status={replay.Status}; ir_return={replay.ReturnValue?.IntegerNumericValue}; " +
                    $"consumed_approximation={replay.ConsumedApproximation}");
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
                Assert.That(replay.ConsumedApproximation, Is.False);
            }
            else
            {
                Assert.That(graph.Blocks.Length, Is.GreaterThan(RoslynTotalProgramLowerer.MaximumRegionSteps));
                Assert.That(lowering.IsExact, Is.False);
                Assert.That(lowering.ConstructionLimitExceeded, Is.True);
                Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
                Assert.That(lowering.Program.Blocks.Length, Is.EqualTo(1));
            }
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static void WriteEvidence(string line)
    {
        System.ArgumentNullException.ThrowIfNull(line);
        TestContext.Progress.WriteLine(line);

    }
}
