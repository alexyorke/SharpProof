using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using System.Collections.Immutable;
using SharpProof.Dataflow;
using SharpProof.Ir;
using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class GoldenAnalyzerTests
{
    public static IEnumerable<string> Cases()
    {
        return GoldenTest.Cases("analyzer");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task DiagnosticsMatchGolden(string caseName)
    {
        var fixture = GoldenTest.Load("analyzer", caseName);
        if (fixture.Source.StartsWith("// golden-scenario: core-advisory\n", StringComparison.Ordinal))
        {
            GoldenTest.Compare(fixture, FormatCoreAdvisory(fixture.Source));
            return;
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(fixture.Source, "contracts", ["SP0027"], filePath: caseName + ".cs");
        GoldenTest.Compare(fixture, FormatDiagnostics(diagnostics));
    }

    private static string FormatCoreAdvisory(string source)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var rows = new List<string>();
        foreach (var syntax in tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
                     .OrderBy(static method => method.Identifier.ValueText, StringComparer.Ordinal))
        {
            var context = new SharpProof.Frontend.TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total),
                (Microsoft.CodeAnalysis.IMethodSymbol)Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, syntax)!);
            var graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!;
            var lowering = new SharpProof.Frontend.RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context);
            Assert.That(lowering.IsExact, Is.True);
            Assert.That(lowering.Program.Blocks.Count(block => block.Terminator is IrExceptionalExitInstruction), Is.EqualTo(1));
            var variables = context.Parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Current, parameter.PreState })
                .Concat(lowering.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>().Select(assign => assign.Target))
                .Append(context.Result!.Value).Distinct().ToImmutableArray();
            var result = new CoreIrAdvisoryInterpreter(lowering.Program, variables).Run(
                ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(context.Parameters[0].Entry, IntervalValue.Range(-2, 2)));
            var returns = result.Accepted ? lowering.Program.Blocks.Where(block => block.Terminator is IrReturnInstruction && result.RequireOutput(block.Id.Value).Reachable) : [];
            var bounds = returns.Aggregate(IntervalValue.Bottom,
                (combined, block) => IntervalDomain.Instance.Join(combined, result.RequireOutput(block.Id.Value).Values[context.Result.Value]));
            var gaps = result.Gaps.IsEmpty ? "none" : string.Join(",", result.Gaps);
            rows.Add($"{syntax.Identifier.ValueText} accepted={(result.Accepted ? "true" : "false")} gaps={gaps} return=[{bounds.LowerBound},{bounds.UpperBound}]");
        }
        return string.Join('\n', rows);
    }
    internal static string FormatDiagnostics(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        var rows = diagnostics.Select(diagnostic =>
        {
            var location = diagnostic.Location.GetMappedLineSpan();
            return $"{diagnostic.Id} {diagnostic.Severity} {location.Path}:" +
                $"{location.StartLinePosition.Line + 1}:{location.StartLinePosition.Character + 1}-" +
                $"{location.EndLinePosition.Line + 1}:{location.EndLinePosition.Character + 1} " +
                diagnostic.GetMessage(CultureInfo.InvariantCulture);
        }).Order(StringComparer.Ordinal);
        return string.Join('\n', rows);
    }
}
