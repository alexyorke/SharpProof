using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Dataflow;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275CoreRequiredInputTests
{
    [Test]
    public void CreateGraphRejectsNullRequiredInputsAndAcceptsRealSource()
    {
        var fixture = CreateSourceFixture();

        AssertArgumentNull(
            "declaration",
            () => TotalBodyLowering.CreateGraph(
                null!, fixture.Method, fixture.Model, CancellationToken.None));
        AssertArgumentNull(
            "method",
            () => TotalBodyLowering.CreateGraph(
                fixture.Declaration, null!, fixture.Model, CancellationToken.None));
        AssertArgumentNull(
            "model",
            () => TotalBodyLowering.CreateGraph(
                fixture.Declaration, fixture.Method, null!, CancellationToken.None));

        var graph = TotalBodyLowering.CreateGraph(
            fixture.Declaration, fixture.Method, fixture.Model, CancellationToken.None);
        Assert.That(graph, Is.Not.Null);
    }

    [Test]
    public void LowerRejectsNullRequiredInputsAndLowersARealBody()
    {
        var fixture = CreateSourceFixture();
        var context = new TotalLoweringContext(
            new IrFactory(IrExecutionSemantics.Total), fixture.Method);

        AssertArgumentNull(
            "compilation",
            () => TotalBodyLowering.Lower(
                null!, fixture.Graph, context, CancellationToken.None));
        AssertArgumentNull(
            "graph",
            () => TotalBodyLowering.Lower(
                fixture.Compilation, null!, context, CancellationToken.None));
        AssertArgumentNull(
            "context",
            () => TotalBodyLowering.Lower(
                fixture.Compilation, fixture.Graph, null!, CancellationToken.None));

        var lowering = TotalBodyLowering.Lower(
            fixture.Compilation, fixture.Graph, context, CancellationToken.None);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.Program.Blocks, Is.Not.Empty);
    }

    [Test]
    public void ClosedContractValidationRejectsNullRequiredInputsAndAcceptsRealSession()
    {
        var fixture = CreateSourceFixture();
        var session = new AnalyzerSession(
            fixture.Compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);
        var diagnostics = new List<Diagnostic>();

        AssertArgumentNull(
            "method",
            () => ClosedContractDiagnostics.Validate(null!, session, diagnostics.Add));
        AssertArgumentNull(
            "session",
            () => ClosedContractDiagnostics.Validate(fixture.Method, null!, diagnostics.Add));
        AssertArgumentNull(
            "reportDiagnostic",
            () => ClosedContractDiagnostics.Validate(fixture.Method, session, null!));

        ClosedContractDiagnostics.Validate(fixture.Method, session, diagnostics.Add);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void InvalidTreeConfigurationRejectsNullOptionsAndReadsRoslynDefaults()
    {
        AssertArgumentNull(
            "options",
            () => AnalyzerConfiguration.GetInvalidTreeConfigurationValues(null!));

        var fixture = CreateSourceFixture();
        var analyzerOptions = new AnalyzerOptions([]);
        var treeOptions = analyzerOptions.AnalyzerConfigOptionsProvider
            .GetOptions(fixture.Declaration.SyntaxTree);

        Assert.That(
            AnalyzerConfiguration.GetInvalidTreeConfigurationValues(treeOptions),
            Is.Empty);
    }

    [Test]
    public void AdvisoryRunRejectsNullInitialStateAndAcceptsNormallyLoweredProgram()
    {
        var fixture = CreateSourceFixture();
        var context = new TotalLoweringContext(
            new IrFactory(IrExecutionSemantics.Total), fixture.Method);
        var lowering = TotalBodyLowering.Lower(
            fixture.Compilation, fixture.Graph, context, CancellationToken.None);
        var variables = context.Parameters
            .SelectMany(static parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (new[]
            {
                parameter.Entry,
                parameter.Current,
                parameter.PreState
            }))
            .Concat(lowering.Program.Blocks
                .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                .OfType<IrAssignInstruction>()
                .Select(static assignment => assignment is null ? throw new System.ArgumentNullException(nameof(assignment)) : (assignment.Target)))
            .Append(context.Result!.Value)
            .Distinct()
            .ToImmutableArray();
        var interpreter = new CoreIrAdvisoryInterpreter(
            lowering.Program, variables);

        AssertArgumentNull("initial", () => interpreter.Run(null!));

        var result = interpreter.Run(
            ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.True);
    }

    private static SourceFixture CreateSourceFixture()
    {
        var compilation = TestCompilation.Create(
            "Pr275CoreRequiredInput",
            ("Subject.cs", """
                public static class Subject
                {
                    public static int Target(int value)
                    {
                        return value;
                    }
                }
                """));
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var model = compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var graph = ControlFlowGraph.Create(declaration, model)!;
        return new SourceFixture(compilation, declaration, method, model, graph);
    }

    private static void AssertArgumentNull(string parameterName, Action action)
    {
        System.ArgumentNullException.ThrowIfNull(action);
        System.ArgumentNullException.ThrowIfNull(parameterName);
        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }

    private sealed record SourceFixture(
        CSharpCompilation Compilation,
        MethodDeclarationSyntax Declaration,
        IMethodSymbol Method,
        SemanticModel Model,
        ControlFlowGraph Graph)
    {
        public CSharpCompilation Compilation { get; init; } = Compilation ?? throw new System.ArgumentNullException(nameof(Compilation));
        public MethodDeclarationSyntax Declaration { get; init; } = Declaration ?? throw new System.ArgumentNullException(nameof(Declaration));
        public IMethodSymbol Method { get; init; } = Method ?? throw new System.ArgumentNullException(nameof(Method));
        public SemanticModel Model { get; init; } = Model ?? throw new System.ArgumentNullException(nameof(Model));
        public ControlFlowGraph Graph { get; init; } = Graph ?? throw new System.ArgumentNullException(nameof(Graph));
    }
}
