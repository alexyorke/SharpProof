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
        if (caseName == "record-copy-initializer-observation")
        {
            var observer = new RecordInitializerObserver();
            var sessions = new RecordingSessionFactory { RequiresObserver = observer };
            var observedDiagnostics = await AnalyzerTestHost.AnalyzeAsync(fixture.Source, "contracts", ["SP0027"],
                new SharpProofAnalyzer(sessions), filePath: caseName + ".cs");
            var rows = observer.Rows.Order(StringComparer.Ordinal);
            GoldenTest.Compare(fixture, "diagnostics " + observedDiagnostics.Length.ToString(CultureInfo.InvariantCulture) + "\n" + string.Join('\n', rows));
            return;
        }
        if (fixture.Source.StartsWith("// golden-scenario: core-advisory-calls\n", StringComparison.Ordinal))
        {
            GoldenTest.Compare(fixture, FormatAdvisoryCalls(fixture.Source));
            return;
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(fixture.Source, "contracts", ["SP0027"], filePath: caseName + ".cs");
        GoldenTest.Compare(fixture, FormatDiagnostics(diagnostics));
    }

    private static string FormatAdvisoryCalls(string source)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]);
        var rows = new List<string>();
        foreach (var syntax in compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Where(method => method.Modifiers.Any(modifier => modifier.ValueText == "public")).OrderBy(method => method.Identifier.ValueText, StringComparer.Ordinal))
        {
            var result = AdvisoryCallConsumer.Analyze(compilation, syntax, true);
            Assert.That(result.Calls, Has.Length.EqualTo(1));
            var site = result.Calls[0];
            var program = result.Program!;
            var occurrences = 0;
            foreach (var input in new[] { -2, -1, 0, 1, 2 })
            {
                var initial = program.GetBlock(program.Entry).Instructions.OfType<IrAssignInstruction>().Select(assign => assign.Value).OfType<IrVariableTerm>()
                    .GroupBy(variable => variable.Variable).ToDictionary(group => group.Key, group => program.Factory.CreateIntegerValue(program.Factory.GetVariableInfo(group.Key).Type, input));
                var replay = new IrProgramReplayOptions(request => program.Factory.CreateIntegerValue(program.Factory.GetVariableInfo(request.Variable).Type, input))
                {
                    AssignmentObserver = (assignment, value, tainted) =>
                    {
                        if (!ReferenceEquals(assignment, site.Marker))
                        {
                            return;
                        }
                        occurrences++;
                        Assert.That(tainted, Is.False);
                        Assert.That(site.Condition.Contains(value.Boolean ? 1 : 0), Is.True);
                    }
                };
                _ = new IrProgramInterpreter(program.Factory).Execute(program, initial, 1000, replay);
            }
            Assert.That(occurrences, Is.GreaterThan(0));
            var gaps = result.Gaps.IsEmpty ? "none" : string.Join(",", result.Gaps);
            rows.Add($"{syntax.Identifier.ValueText} gaps={gaps} marker=[{site.Condition.LowerBound},{site.Condition.UpperBound}] prefixGap={(site.PrefixHasGap ? "true" : "false")} replayOccurrences={occurrences}");
        }
        return string.Join('\n', rows);
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

    private sealed class RecordInitializerObserver : IRequiresCallSiteObserver
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> Rows { get; } = new();

        public void ObserveClause(in RequiresClauseObservation observation)
        {
            if (observation.ContractTarget.Name != "Need")
            {
                return;
            }
            var parameters = string.Join(",", observation.Caller.Parameters.Select(static parameter =>
                parameter.RefKind + ":" + parameter.Type.Name));
            Rows.Enqueue($"owner {observation.Caller.ContainingType.Name}({parameters}) target Need clause " +
                observation.ClauseOrdinal.ToString(CultureInfo.InvariantCulture) + " " + observation.Outcome);
        }

        public void ObserveGap(in RequiresCallObservationGap gap) { }
        public void ObserveOwnerGap(in RequiresOwnerObservationGap gap) { }
    }
}
