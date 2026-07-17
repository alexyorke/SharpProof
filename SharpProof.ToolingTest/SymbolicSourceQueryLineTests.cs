using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class SymbolicSourceQueryLineTests
{
    [Test]
    public void SymbolicSourceCompilationProfile_AppliesEveryCompilerSetting()
    {
        var profile = new SymbolicSourceCompilationProfile(
            LanguageVersion.CSharp12,
            new[] { "PROFILE", "PROFILE" },
            NullableContextOptions.Enable,
            true,
            DocumentationMode.Diagnose,
            Platform.X64,
            OptimizationLevel.Release,
            "Profile.Assembly");
        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            "#if PROFILE\npublic unsafe class C { public int* Pointer; }\n#endif\n",
            "Profile.cs",
            "Default.cs",
            "Default.Assembly",
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            CancellationToken.None,
            profile);

        var parseOptions = (CSharpParseOptions)syntaxTree.Options;
        var compilationOptions = (CSharpCompilationOptions)compilation.Options;
        Assert.That(parseOptions.LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        Assert.That(parseOptions.PreprocessorSymbolNames, Is.EqualTo(new[] { "PROFILE" }));
        Assert.That(parseOptions.DocumentationMode, Is.EqualTo(DocumentationMode.Diagnose));
        Assert.That(compilationOptions.NullableContextOptions, Is.EqualTo(NullableContextOptions.Enable));
        Assert.That(compilationOptions.AllowUnsafe, Is.True);
        Assert.That(compilationOptions.Platform, Is.EqualTo(Platform.X64));
        Assert.That(compilationOptions.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        Assert.That(compilation.AssemblyName, Is.EqualTo("Profile.Assembly"));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty);
    }

    [Test]
    public void SymbolicSourceInput_WithSourceMapPreservesSnippetOriginMetadata()
    {
        var original = SymbolicSourceInput.FromText("class C { }", "virtual/Generated.cs");
        var sourceMap = new SymbolicSourceMap("editor://workspace/Original.cs", 41, 7);

        var mapped = original.WithSourceMap(sourceMap);

        Assert.That(original.SourceMap, Is.Null);
        Assert.That(mapped.Kind, Is.EqualTo(SymbolicSourceInputKind.Text));
        Assert.That(mapped.FilePath, Is.EqualTo("virtual/Generated.cs"));
        Assert.That(mapped.SourceText, Is.EqualTo(original.SourceText));
        Assert.That(mapped.SourceMap, Is.SameAs(sourceMap));
        Assert.That(mapped.SourceMap!.SourceUri, Is.EqualTo("editor://workspace/Original.cs"));
        Assert.That(mapped.SourceMap.OriginalStartLine, Is.EqualTo(41));
        Assert.That(mapped.SourceMap.OriginalStartColumn, Is.EqualTo(7));
    }

    [Test]
    public void SymbolicQueryService_CompilationProfileFlowsThroughEveryStandaloneMode()
    {
        const string source = """
                              #if PROFILE
                              using System;

                              public static class Profiled
                              {
                                  public static int Identity(int value) => value;

                                  public static void Hazard()
                                  {
                                      throw new InvalidOperationException();
                                  }

                                  public static void Capability()
                                  {
                                      Console.WriteLine("profiled");
                                  }

                                  public static int Complexity(int count)
                                  {
                                      var total = 0;
                                      for (var index = 0; index < count; index++) total += index;
                                      return total;
                                  }
                              }
                              #endif
                              """;
        var profile = new SymbolicSourceCompilationProfile(preprocessorSymbols: new[] { "PROFILE" });
        var input = SymbolicSourceInput.FromTextWithProfile(source, profile, "Profiled.cs");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var options = new SymbolicQueryOptions(
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            smtAnalysis);
        var service = new SymbolicQueryService();

        var invariants = service.Query(new SymbolicQueryContext(
            input,
            SymbolicQueryTarget.AllLines(),
            options));
        Assert.That(invariants.ProgramPoints.Any(static point =>
            point.MethodName?.Contains("Identity", StringComparison.Ordinal) == true), Is.True);

        var hazards = service.QueryRuntimeHazards(new SymbolicQueryContext(
            input,
            SymbolicQueryTarget.Line(FindLine(source, "throw new InvalidOperationException")),
            options));
        Assert.That(hazards.Hazards, Has.Some.Property("Kind").EqualTo(SymbolicRuntimeHazardKind.DirectThrow));

        var capabilities = service.QueryCapabilities(new SymbolicQueryContext(
            input,
            SymbolicQueryTarget.Line(FindLine(source, "Console.WriteLine")),
            options));
        Assert.That(capabilities.CapabilityText, Does.Contain("Console"));

        var complexity = service.QueryComplexity(new SymbolicQueryContext(
            input,
            SymbolicQueryTarget.Line(FindLine(source, "for (var index")),
            options));
        Assert.That(complexity.MethodDisplayName, Does.Contain("Complexity"));
    }

    [Test]
    public void SymbolicQueryService_RoutesFileTextSyntaxTreeAndNodeQueries()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "NewSymbolicApi.cs");
        var compilation = CSharpCompilation.Create(
            "NewSymbolicApi",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var service = new SymbolicQueryService();
        var options = new SymbolicQueryOptions(AnalyzerTestHost.GetTrustedPlatformReferences());

        var textLine = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "TextInput.cs"),
            SymbolicQueryTarget.Line(FindLine(source, "if (value > 0)")),
            options));
        Assert.That(textLine.ScopeKind, Is.EqualTo("line"));
        Assert.That(textLine.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.Line));
        Assert.That(textLine.Scope.Line, Is.EqualTo(FindLine(source, "if (value > 0)")));
        Assert.That(textLine.ProgramPoints.Select(static point => point.NodeKind), Does.Contain("IfStatement"));

        var textPosition = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "PositionInput.cs"),
            SymbolicQueryTarget.Position(FindPosition(source, "return value;")),
            options));
        Assert.That(textPosition.ScopeKind, Is.EqualTo("point"));
        Assert.That(textPosition.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.Point));
        Assert.That(textPosition.Scope.Position, Is.EqualTo(FindPosition(source, "return value;")));
        Assert.That(textPosition.ProgramPoints.Single().NodeKind, Is.EqualTo("ReturnStatement"));

        var syntaxSpan = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
            SymbolicQueryTarget.Span(
                FindPosition(source, "if (value > 0)"),
                FindPosition(source, "return 0;")),
            SymbolicQueryOptions.Default));
        Assert.That(syntaxSpan.ScopeKind, Is.EqualTo("span"));
        Assert.That(syntaxSpan.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.Span));
        Assert.That(syntaxSpan.Scope.SpanStart, Is.EqualTo(FindPosition(source, "if (value > 0)")));
        Assert.That(syntaxSpan.ProgramPointCount, Is.GreaterThan(0));

        var syntaxAllLines = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
            SymbolicQueryTarget.AllLines()));
        Assert.That(syntaxAllLines.ScopeKind, Is.EqualTo("file"));
        Assert.That(syntaxAllLines.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.File));
        Assert.That(syntaxAllLines.LineCount, Is.GreaterThan(0));

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var returnNode = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single(statement => statement.ToString().Contains("return value;", StringComparison.Ordinal));
        var nodeResult = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromNode(returnNode, semanticModel),
            SymbolicQueryTarget.Node()));
        Assert.That(nodeResult.ScopeKind, Is.EqualTo("point"));
        Assert.That(nodeResult.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.Point));
        Assert.That(nodeResult.ProgramPoints.Single().NodeKind, Is.EqualTo("ReturnStatement"));
        Assert.That(nodeResult.ProgramPoints.Single().SymbolicFacts, Is.Not.Empty);
        Assert.That(
            nodeResult.ProgramPoints.Single().SymbolicFacts.Select(static fact => fact.Kind),
            Has.Some.EqualTo("SymbolicRelationAtom"));

        var sourcePath = Path.Combine(Path.GetTempPath(),
            "SharpProof.SymbolicQueryApi." + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            File.WriteAllText(sourcePath, source);
            var fileTargets = new[]
            {
                (SymbolicQueryTarget.Point(FindLine(source, "return value;")), SymbolicQueryScopeKind.Point),
                (SymbolicQueryTarget.Position(FindPosition(source, "return value;")), SymbolicQueryScopeKind.Point),
                (SymbolicQueryTarget.Line(FindLine(source, "if (value > 0)")), SymbolicQueryScopeKind.Line),
                (SymbolicQueryTarget.Span(
                    FindPosition(source, "if (value > 0)"),
                    FindPosition(source, "return 0;")), SymbolicQueryScopeKind.Span),
                (SymbolicQueryTarget.LineSpan(
                    FindLine(source, "if (value > 0)"), 1,
                    FindLine(source, "return 0;"), 1), SymbolicQueryScopeKind.Span),
                (SymbolicQueryTarget.AllLines(), SymbolicQueryScopeKind.File)
            };

            foreach (var (target, expectedScope) in fileTargets)
            {
                var fileResult = service.Query(new SymbolicQueryContext(
                    SymbolicSourceInput.FromFile(sourcePath), target));
                Assert.That(fileResult.Scope.Kind, Is.EqualTo(expectedScope), target.Kind.ToString());
                Assert.That(fileResult.FilePath, Is.EqualTo(Path.GetFullPath(sourcePath)), target.Kind.ToString());
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    public enum StateFlowQueryMode { Line, Node }

    public sealed record StateFlowScenario(string Source, string Name, string FileName, string Marker, StateFlowQueryMode QueryMode, string Target, string[] RequiredFactFragments, string[] ForbiddenFactFragments, SymbolicTruthValue? ExpectedProof, SymbolicReachability? ExpectedReachability);

    private static readonly StateFlowScenario[] StateFlowScenarios =
    {
        new(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 1:
                return value;
            default:
                return 0;
        }
    }
}", "QuerySyntaxTreeLine_SwitchSectionFactsFlowThroughSymbolicState", "SwitchStateFacts.cs", "return value;",
            StateFlowQueryMode.Line, "ReturnStatement", ["kind=SymbolicRelationAtom", "facts=value == 1", "merged~value == 1"], [], null, null),
        new(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        foreach (var value in values)
        {
            return value;
        }

        return 0;
    }
}", "QuerySyntaxTreeLine_ForeachEntryFactsFlowThroughSymbolicState", "ForeachStateFacts.cs", "return value;",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.foreach-entry.not-null", "provenance=ir.path.foreach-entry.length-positive", "facts=values != null", "merged~values.Length > 0"], [], null, null),
        new(@"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 5;
        return 10 / divisor;
    }
}", "QuerySyntaxTreeLine_PriorAssignmentFactsFlowThroughSymbolicState", "PriorAssignmentStateFacts.cs", "return 10 / divisor;",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.prior-statement.assigned-value", "text=divisor == 5", "facts=divisor == 5"], [], null, null),
        new(@"
public class TestClass
{
    public int TestMethod()
    {
        for (var index = 0; index < 10; index++)
        {
            return index;
        }

        return -1;
    }
}", "SymbolicQueryService_ForInitialEntryFactsFlowThroughSymbolicState", "ForInitialEntryStateFacts.cs", "for (var index = 0; index < 10; index++)",
            StateFlowQueryMode.Node, "ForStatement", ["provenance^ir.path.for-initializer", "text=index == 0", "facts=index == 0"], [], null, null),
        new(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException ex) when (value > 0)
        {
            return value;
        }

        return 0;
    }
}", "QuerySyntaxTreeLine_CatchEntryFactsFlowThroughSymbolicState", "CatchStateFacts.cs", "return value;",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.catch-entry.exception-not-null", "provenance^ir.relation", "facts=ex != null", "merged~value > 0"], [], null, null),
        new(@"
public class TestClass
{
    public int TestMethod(object gate)
    {
        lock (gate)
        {
            return gate.GetHashCode();
        }
    }
}", "QuerySyntaxTreeLine_LockEntryFactsFlowThroughSymbolicState", "LockStateFacts.cs", "return gate.GetHashCode();",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.lock-entry.not-null", "facts=gate != null"], [], null, null),
        new(@"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (value ?? throw new InvalidOperationException())
        {
            return 1;
        }
    }
}", "QuerySyntaxTreeLine_UsingExpressionFactsFlowThroughSymbolicState", "UsingExpressionStateFacts.cs", "return 1;",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.using-entry.throw-guarded-not-null", "facts=value != null"], [], null, null),
        new(@"
using System;

public class TestClass
{
    public int TestMethod(IDisposable value)
    {
        using (IDisposable resource = value ?? throw new InvalidOperationException())
        {
            return resource.GetHashCode();
        }
    }
}", "QuerySyntaxTreeLine_UsingDeclarationFactsFlowThroughSymbolicState", "UsingDeclarationStateFacts.cs", "return resource.GetHashCode();",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.using-entry.throw-guarded-not-null", "provenance=ir.path.using-entry.declaration-alias", "facts=value != null", "facts=resource == value"], [], null, null),
        new(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            return values[index];
        }

        return 0;
    }
}", "QuerySyntaxTreeLine_ForLoopInvariantFactsFlowThroughSymbolicState", "ForLoopStateFacts.cs", "return values[index];",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.for-loop-invariant.lower-bound", "facts=index >= 0", "merged~index >= 0"], [], null, null),
        new(@"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        var index = 0;
        while (index < values.Length)
        {
            return values[index];
        }

        return 0;
    }
}", "QuerySyntaxTreeLine_WhileLoopInvariantFactsFlowThroughSymbolicState", "WhileLoopStateFacts.cs", "return values[index];",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.while-loop-invariant.lower-bound", "facts=index >= 0"], [], null, null),
        new(@"
public class TestClass
{
    public int TestMethod()
    {
        var index = 0;
        do
        {
            return index;
        } while (index < 10);
    }
}", "QuerySyntaxTreeLine_DoLoopInvariantFactsFlowThroughSymbolicState", "DoLoopStateFacts.cs", "return index;",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.do-loop-invariant.lower-bound", "facts=index >= 0"], [], null, null),
    };

    private static IEnumerable<TestCaseData> StateFlowScenarioData()
    {
        if (StateFlowScenarios.Length != 11 || StateFlowScenarios.Select(static x => x.Name).Distinct(StringComparer.Ordinal).Count() != 11) throw new InvalidOperationException("State-flow scenario invariants failed.");
        return StateFlowScenarios.Select(static scenario => new TestCaseData(scenario).SetName(scenario.Name));
    }

    [TestCaseSource(nameof(StateFlowScenarioData))]
    public void StateFlowScenariosPreserveAssertions(StateFlowScenario scenario)
    {
        SymbolicProgramPointResult point;
        if (scenario.QueryMode == StateFlowQueryMode.Line)
        { using var session = new SymbolicSourceQueryTestSession(scenario.Source, scenario.FileName); point = session.AnalyzeLine(scenario.Marker).ProgramPoints.Single(candidate => candidate.NodeKind == scenario.Target); }
        else
        {
            var tree = CSharpSyntaxTree.ParseText(scenario.Source, new CSharpParseOptions(LanguageVersion.Preview), scenario.FileName);
            var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(scenario.FileName), [tree], AnalyzerTestHost.GetTrustedPlatformReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var node = tree.GetRoot().DescendantNodes().Single(candidate => candidate.Kind().ToString() == scenario.Target);
            point = new SymbolicQueryService().Query(new SymbolicQueryContext(SymbolicSourceInput.FromNode(node, compilation.GetSemanticModel(tree)), SymbolicQueryTarget.Node())).ProgramPoints.Single();
        }
        bool Matches(string expectation) => expectation switch
        {
            var value when value.StartsWith("kind=", StringComparison.Ordinal) => point.SymbolicFacts.Any(fact => fact.Kind == value[5..]), var value when value.StartsWith("provenance=", StringComparison.Ordinal) => point.SymbolicFacts.Any(fact => fact.Provenance == value[11..]),
            var value when value.StartsWith("provenance^", StringComparison.Ordinal) => point.SymbolicFacts.Any(fact => fact.Provenance.StartsWith(value[11..], StringComparison.Ordinal)), var value when value.StartsWith("text=", StringComparison.Ordinal) => point.SymbolicFacts.Any(fact => fact.Text == value[5..]),
            var value when value.StartsWith("facts=", StringComparison.Ordinal) => point.Facts.Contains(value[6..]), var value when value.StartsWith("merged~", StringComparison.Ordinal) => point.MergedInvariantText.Contains(value[7..], StringComparison.Ordinal),
            _ => throw new InvalidOperationException("Unknown state-flow fact expectation: " + expectation)
        };
        foreach (var required in scenario.RequiredFactFragments) Assert.That(Matches(required), Is.True, required);
        foreach (var forbidden in scenario.ForbiddenFactFragments) Assert.That(Matches(forbidden), Is.False, forbidden);
        if (scenario.ExpectedProof is { } proof) Assert.That(point.ConditionProofs.Select(static x => x.TruthValue), Does.Contain(proof));
        if (scenario.ExpectedReachability is { } reachability) Assert.That(point.Reachability, Is.EqualTo(reachability));
    }

    [Test]
    public void SymbolicQueryService_QueryRuntimeHazards_UsesRequestApi()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value / 0;
    }
}";
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var service = new SymbolicQueryService();
        var options = new SymbolicQueryOptions(AnalyzerTestHost.GetTrustedPlatformReferences(), smtAnalysis);
        var hazardOptions = new SymbolicRuntimeHazardQueryOptions(
            kinds: new[] { SymbolicRuntimeHazardKind.DivideByZero });
        var targets = new[]
        {
            SymbolicQueryTarget.Point(FindLine(source, "return value / 0;")),
            SymbolicQueryTarget.Line(FindLine(source, "return value / 0;")),
            SymbolicQueryTarget.Span(
                FindPosition(source, "return value / 0;"),
                FindPosition(source, "return value / 0;") + "return value / 0;".Length),
            SymbolicQueryTarget.AllLines()
        };

        void AssertHazard(SymbolicSourceInput input, SymbolicQueryTarget target)
        {
            var result = service.QueryRuntimeHazards(
                new SymbolicQueryContext(input, target, options), hazardOptions);
            Assert.That(result.Hazards.Single().Kind, Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero),
                target.Kind.ToString());
        }

        foreach (var target in targets)
            AssertHazard(SymbolicSourceInput.FromText(source, "HazardInput.cs"), target);

        var sourcePath = Path.Combine(Path.GetTempPath(), "SharpProof.HazardQuery." + Guid.NewGuid() + ".cs");
        try
        {
            File.WriteAllText(sourcePath, source);
            foreach (var target in targets)
                AssertHazard(SymbolicSourceInput.FromFile(sourcePath), target);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public void SymbolicQueryService_QueryRuntimeHazards_RequiresSmtAnalysis()
    {
        var ex = Assert.Throws<ArgumentException>(() => new SymbolicQueryService().QueryRuntimeHazards(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromText("class C { int M(int value) => value; }", "HazardInput.cs"),
                SymbolicQueryTarget.AllLines(),
                SymbolicQueryOptions.Default)));

        Assert.That(ex!.Message, Does.Contain("Runtime hazard queries require SMT analysis."));
    }

    [Test]
    public void SymbolicQueryService_Prove_RequiresPointTarget()
    {
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var ex = Assert.Throws<ArgumentException>(() => new SymbolicQueryService().Prove(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromText("class C { int M(int value) => value; }", "ProofInput.cs"),
                SymbolicQueryTarget.Line(1),
                new SymbolicQueryOptions(
                    AnalyzerTestHost.GetTrustedPlatformReferences(),
                    smtAnalysis)),
            "value > 0"));

        Assert.That(ex!.Message, Does.Contain("Condition proof requests require a point target."));
    }

    [Test]
    public void SymbolicQueryApi_HidesLegacyOverloadServicesFromPublicSurface()
    {
        var assembly = typeof(SymbolicQueryService).Assembly;
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicSourceQueryService")!.IsPublic, Is.False);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicRuntimeHazardQueryService")!.IsPublic, Is.False);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicFileQuery"), Is.Null);
        Assert.That(typeof(SymbolicProgramPointResult).IsPublic, Is.True);
        Assert.That(typeof(SymbolicProgramPointResult).GetConstructors(), Is.Empty);
        Assert.That(typeof(SymbolicConditionProofResult).GetConstructors(), Is.Empty);
    }

    [Test]
    public void QuerySyntaxTreeLine_ReturnsEveryProgramPointOnLine()
    {
        const string source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "LineQuery.cs");
        var result = session.AnalyzeLine(
            "if (value > 0)",
            impliedConditions: new[] { "value > 0" });

        Assert.That(result.ProgramPoints.Select(point => point.NodeKind), Does.Contain("IfStatement"));
        var returnPoint = result.ProgramPoints.Single(point => point.NodeKind == "ReturnStatement");
        var returnProof = returnPoint.ConditionProofs.Single();
        Assert.That(returnProof.TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(returnProof.Proof.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(returnProof.Proof.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(returnProof.Proof.UnknownReason, Is.EqualTo(SymbolicUnknownReason.None));
        Assert.That(returnProof.Proof.Reason, Is.EqualTo(returnProof.Reason));
        Assert.That(returnProof.Proof.DisplayKind, Is.Not.Empty);
        Assert.That(returnProof.Proof.ConditionText, Is.EqualTo("value > 0"));
        Assert.That(returnProof.Proof.Target, Is.EqualTo(returnProof.Target));
        Assert.That(returnProof.Target, Is.EqualTo("value"));
        Assert.That(returnProof.ValueKind, Is.EqualTo("Bool"));
        Assert.That(returnProof.FilePath, Does.EndWith("LineQuery.cs"));
        Assert.That(returnProof.Line, Is.EqualTo(returnPoint.Line));
        Assert.That(returnProof.Column, Is.EqualTo(returnPoint.Column));
        Assert.That(returnProof.NodeSpanStart, Is.EqualTo(returnPoint.NodeSpanStart));
        Assert.That(returnProof.NodeSpanEnd, Is.EqualTo(returnPoint.NodeSpanEnd));
        var aggregateProof = result.ConditionProofs.Single(proof => proof.Condition == "value > 0");
        Assert.That(aggregateProof.Proof.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(aggregateProof.Proof.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(aggregateProof.Proof.UnknownReason, Is.EqualTo(SymbolicUnknownReason.Unknown));
        Assert.That(aggregateProof.Proof.Reason, Is.EqualTo(aggregateProof.Summary));
        Assert.That(aggregateProof.Proof.DisplayKind, Is.Not.Empty);
        Assert.That(aggregateProof.Proof.ConditionText, Is.EqualTo("value > 0"));
        Assert.That(aggregateProof.Proof.Target, Is.EqualTo(aggregateProof.Target));
        Assert.That(aggregateProof.Target, Is.EqualTo("value"));
        Assert.That(aggregateProof.ValueKind, Is.EqualTo("Bool"));
        Assert.That(returnPoint.MergedInvariantText, Is.EqualTo("value > 0"));
        var summary = SymbolicInvariantService.MergeInvariantFacts(result.ProgramPoints.Select(point => point.Facts));
        Assert.That(summary.Facts, Is.EquivalentTo(result.ProgramPoints.SelectMany(point => point.Facts).Distinct()));
        Assert.That(summary.MergedInvariantText, Does.Contain("value"));
        Assert.That(result.Facts, Is.EquivalentTo(summary.Facts));
        Assert.That(result.ObservedInvariant.MergedInvariantText, Is.EqualTo(summary.MergedInvariantText));
        Assert.That(result.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
        Assert.That(result.MergedInvariantText, Is.EqualTo("unknown(value)"));
        Assert.That(result.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(result.MergedInvariant.ConditionCount, Is.EqualTo(result.MergedPathFacts.MergedFacts.Count));
        Assert.That(result.MergedPathFacts.AlwaysFacts, Is.Empty);
        Assert.That(result.MergedPathFacts.MaybeFacts, Does.Contain("value > 0"));
        Assert.That(result.MergedPathFacts.ConservativeUnknowns, Is.EquivalentTo(new[] { "unknown(value)" }));
        Assert.That(result.ProgramPointSummary.ProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));
        Assert.That(
            result.ProgramPointSummary.TotalPathConditionCount,
            Is.EqualTo(result.ProgramPoints.Sum(point => point.PathConditionCount)));
        Assert.That(
            result.ProgramPointSummary.MaxPathConditionCount,
            Is.EqualTo(result.ProgramPoints.Max(point => point.PathConditionCount)));
        Assert.That(
            result.ProgramPointSummary.ProofOutcomes.TotalCount,
            Is.EqualTo(result.ProgramPoints.Sum(point => point.ConditionProofs.Count)));
        Assert.That(returnPoint.Invariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.Conjunction));
        Assert.That(returnPoint.Invariant.MergedInvariantText, Is.EqualTo(returnPoint.MergedInvariantText));
        Assert.That(returnPoint.Invariant.Conditions.Select(condition => condition.Text),
            Is.EquivalentTo(new[] { "value > 0" }));
        Assert.That(returnPoint.PathConditionCount, Is.EqualTo(returnPoint.Invariant.Conditions.Count));
        Assert.That(returnPoint.SymbolicFacts, Is.Not.Empty);
        Assert.That(returnPoint.SymbolicFacts.Single().Kind, Is.EqualTo("SymbolicRelationAtom"));
        Assert.That(returnPoint.SymbolicFacts.Single().Text, Is.EqualTo("value > 0"));
        Assert.That(returnPoint.SymbolicFacts.Single().Provenance, Does.StartWith("ir."));
        Assert.That(returnPoint.InvariantInfo.MergedText, Is.EqualTo(returnPoint.MergedInvariantText));
        Assert.That(returnPoint.InvariantInfo.Facts, Is.EquivalentTo(returnPoint.SymbolicFacts));
        Assert.That(returnPoint.InvariantInfo.Proofs.Single().Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.SymbolicFacts, Is.Not.Empty);
        Assert.That(result.InvariantInfo.MergedText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(result.InvariantInfo.MergeKind, Is.EqualTo(result.MergedInvariant.MergeKind));
        Assert.That(result.InvariantInfo.ConditionCount, Is.EqualTo(result.MergedInvariant.ConditionCount));
        Assert.That(result.InvariantInfo.Facts, Is.EquivalentTo(result.SymbolicFacts));
        Assert.That(result.InvariantInfo.Proofs.Select(static proof => proof.Backend),
            Does.Contain(SymbolicProofBackend.Smt));
        Assert.That(returnPoint.ProofOutcomes.TotalCount, Is.EqualTo(returnPoint.ConditionProofs.Count));
        Assert.That(returnPoint.ProofOutcomes.ProvenTrueCount, Is.EqualTo(1));
        Assert.That(returnPoint.Invariant.Conditions.All(condition => condition.IsSolverBacked), Is.True);
        Assert.That(returnPoint.Invariant.Conditions.Single().Target, Is.EqualTo("value"));
        Assert.That(
            returnPoint.Invariant.Conditions.All(condition => !string.IsNullOrWhiteSpace(condition.DisplayKind)),
            Is.True);
    }

    [Test]
    public void QuerySyntaxTreeLine_WithExpressionProgramPoints_IncludesExpressionNodesOnLine()
    {
        const string source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "LineExpressionQuery.cs");
        var defaultResult = session.AnalyzeLine(
            "return value + 1;",
            impliedConditions: new[] { "value > 0" });

        Assert.That(defaultResult.ProgramPoints.Select(point => point.NodeKind), Does.Not.Contain("AddExpression"));

        var expressionResult = session.AnalyzeLine(
            "return value + 1;",
            impliedConditions: new[] { "value > 0" },
            includeExpressionProgramPoints: true);

        Assert.That(expressionResult.ProgramPoints.Select(point => point.NodeKind), Does.Contain("ReturnStatement"));
        Assert.That(
            expressionResult.ProgramPoints.Single(point => point.NodeKind == "ReturnStatement").ProgramPointKind,
            Is.EqualTo(SymbolicProgramPointKinds.Statement));
        var addPoint = expressionResult.ProgramPoints.Single(point => point.NodeKind == "AddExpression");
        Assert.That(addPoint.ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Expression));
        Assert.That(addPoint.MergedInvariantText, Is.EqualTo("value > 0"));
        Assert.That(addPoint.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(addPoint.NodeStartLine, Is.EqualTo(FindLine(source, "return value + 1;")));
    }

    [Test]
    public void QuerySyntaxTreeLinePoint_WithExpressionProgramPoints_SelectsNearestExpressionNode()
    {
        const string source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "LinePointExpressionQuery.cs");
        var marker = session.FindMarker("value + 1");
        var result = session.AnalyzeLinePoint(
            marker.Line,
            marker.Column,
            impliedConditions: new[] { "value > 0" },
            includeExpressionProgramPoints: true);

        Assert.That(result.NodeKind, Is.EqualTo("AddExpression"));
        Assert.That(result.ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Expression));
        Assert.That(result.Column, Is.EqualTo(FindColumn(source, "value + 1")));
        Assert.That(result.MergedInvariantText, Is.EqualTo("value > 0"));
        Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void QuerySyntaxTreeLinePoint_ExposesRequestedLocationForExactExpressionHit()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "LinePointRequestedLocation.cs");
        var marker = session.FindMarker("value + 1");
        var line = marker.Line;
        var column = marker.Column;
        var requestedPosition = marker.Position;

        var result = session.AnalyzeLinePoint(
            line,
            column,
            includeExpressionProgramPoints: true);

        Assert.That(result.NodeKind, Is.EqualTo("AddExpression"));
        Assert.That(result.RequestedLine, Is.EqualTo(line));
        Assert.That(result.RequestedColumn, Is.EqualTo(column));
        Assert.That(result.RequestedPosition, Is.EqualTo(requestedPosition));
        Assert.That(result.RequestedPositionDistance, Is.EqualTo(0));
        Assert.That(result.ContainsRequestedPosition, Is.True);

        var compact = result.ToCompactResult();
        Assert.That(compact.GetProperty("requestedLine").GetInt32(), Is.EqualTo(line));
        Assert.That(compact.GetProperty("requestedColumn").GetInt32(), Is.EqualTo(column));
        Assert.That(compact.GetProperty("requestedPosition").GetInt32(), Is.EqualTo(requestedPosition));
        Assert.That(compact.GetProperty("requestedPositionDistance").GetInt32(), Is.EqualTo(0));
        Assert.That(compact.GetProperty("containsRequestedPosition").GetBoolean(), Is.True);
        var compactPoint = compact.GetProperty("programPoints")[0];
        Assert.That(compactPoint.GetProperty("requestedLine").GetInt32(), Is.EqualTo(line));
        Assert.That(compactPoint.GetProperty("requestedColumn").GetInt32(), Is.EqualTo(column));
        Assert.That(compactPoint.GetProperty("requestedPosition").GetInt32(), Is.EqualTo(requestedPosition));
        Assert.That(compactPoint.GetProperty("requestedPositionDistance").GetInt32(), Is.EqualTo(0));
        Assert.That(compactPoint.GetProperty("containsRequestedPosition").GetBoolean(), Is.True);

        var invariantResult = result.ToInvariantQueryResult();
        var focus = invariantResult.GetProperty("focus");
        Assert.That(focus.GetProperty("scopeKind").GetString(), Is.EqualTo("point"));
        Assert.That(focus.GetProperty("filePath").GetString(), Is.EqualTo(result.FilePath));
        Assert.That(focus.GetProperty("hasSourceLocation").GetBoolean(), Is.True);
        Assert.That(focus.GetProperty("line").GetInt32(), Is.EqualTo(result.Line));
        Assert.That(focus.GetProperty("column").GetInt32(), Is.EqualTo(result.Column));
        Assert.That(focus.GetProperty("position").GetInt32(), Is.EqualTo(result.Position));
        Assert.That(focus.GetProperty("requestedLine").GetInt32(), Is.EqualTo(line));
        Assert.That(focus.GetProperty("requestedColumn").GetInt32(), Is.EqualTo(column));
        Assert.That(focus.GetProperty("requestedPosition").GetInt32(), Is.EqualTo(requestedPosition));
        Assert.That(focus.GetProperty("requestedPositionDistance").GetInt32(), Is.EqualTo(0));
        Assert.That(focus.GetProperty("containsRequestedPosition").GetBoolean(), Is.True);
        Assert.That(focus.GetProperty("nodeKind").GetString(), Is.EqualTo("AddExpression"));
        Assert.That(focus.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Expression));
        Assert.That(focus.GetProperty("reachabilityStatus").GetString(), Is.EqualTo(result.Reachability.ToString()));
        Assert.That(focus.GetProperty("reachabilityReason").GetString(), Is.EqualTo(result.ReachabilityReason));
        Assert.That(focus.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void QuerySyntaxTreeLinePoint_ExposesNearestFallbackWhenColumnMissesProgramPoint()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "LinePointNearestFallback.cs");
        var line = session.FindLine("return value;");
        var column = 1;
        var requestedPosition = session.FindLineStartPosition("return value;");

        var result = session.AnalyzeLinePoint(
            line,
            column);

        Assert.That(result.NodeKind, Is.EqualTo("ReturnStatement"));
        Assert.That(result.RequestedLine, Is.EqualTo(line));
        Assert.That(result.RequestedColumn, Is.EqualTo(column));
        Assert.That(result.RequestedPosition, Is.EqualTo(requestedPosition));
        Assert.That(result.RequestedPositionDistance, Is.EqualTo(result.NodeSpanStart - requestedPosition));
        Assert.That(result.RequestedPositionDistance, Is.GreaterThan(0));
        Assert.That(result.ContainsRequestedPosition, Is.False);
    }

    [Test]
    public void QuerySyntaxTreeLine_PostLineInvariants_ProvesCurrentAssignmentCompletionFact()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        value = 7;
        return value;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "PostLineInvariantQuery.cs");
        var defaultResult = session.AnalyzeLine(
            "value = 7;",
            impliedConditions: new[] { "value == 7" });
        var defaultPoint = defaultResult.ProgramPoints.Single(point => point.NodeKind == "ExpressionStatement");

        Assert.That(defaultPoint.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));

        var postLineResult = session.AnalyzeLine(
            "value = 7;",
            impliedConditions: new[] { "value == 7" },
            includeCurrentStatementCompletionFacts: true);
        var postLinePoint = postLineResult.ProgramPoints.Single(point => point.NodeKind == "ExpressionStatement");

        Assert.That(postLinePoint.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(
            postLinePoint.Invariant.Conditions,
            Has.Some.Matches<SymbolicInvariantCondition>(condition =>
                condition.Target == "value" && condition.IsSolverBacked));
    }

    [Test]
    public void SymbolicQueryService_NodeProofsReuseCurrentStatementCompletionState()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        value = 7;
        return value;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "NodeCompletionProof.cs");
        var compilation = CSharpCompilation.Create(
            "NodeCompletionProof",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var assignment = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ExpressionStatementSyntax>()
            .Single();

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicQueryService().Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromNode(assignment, semanticModel),
            SymbolicQueryTarget.Node(),
            new SymbolicQueryOptions(
                smtAnalysis: smtAnalysis,
                impliedConditions: new[] { "value == 7" },
                includeCurrentStatementCompletionFacts: true)));

        var point = result.ProgramPoints.Single();
        Assert.That(point.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(point.Facts, Does.Contain("value == 7"));
    }

    [Test]
    public void QuerySyntaxTreeAtPosition_ReturnsFormattedInvariantAtAbsolutePosition()
    {
        const string source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "PositionQuery.cs");
        var compilation = CSharpCompilation.Create(
            "PositionQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var position = FindPosition(source, "return value;");

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAtPosition(
            syntaxTree,
            compilation,
            position,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });

        Assert.That(result.Position, Is.EqualTo(position));
        Assert.That(result.Line, Is.EqualTo(FindLine(source, "return value;")));
        Assert.That(result.Column, Is.EqualTo(FindColumn(source, "return value;")));
        Assert.That(result.NodeKind, Is.EqualTo("ReturnStatement"));
        Assert.That(result.Reachability, Is.EqualTo(SymbolicReachability.Reachable));
        Assert.That(result.Facts, Does.Contain("value > 0"));
        Assert.That(result.Invariant.Conditions.Select(condition => condition.Text),
            Is.EquivalentTo(new[] { "value > 0" }));
        Assert.That(result.MergedInvariantText, Is.EqualTo("value > 0"));
        Assert.That(result.Invariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.Conjunction));
        Assert.That(result.Invariant.Conditions.Single().Target, Is.EqualTo("value"));
        Assert.That(result.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void QuerySyntaxTreeAtPosition_InstanceReferenceMethodIncludesNonNullThisEntryFact()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod() => 1;
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "InstanceThisEntryFact.cs");
        var compilation = CSharpCompilation.Create(
            "InstanceThisEntryFact",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAtPosition(
            syntaxTree,
            compilation,
            FindPosition(source, "1;"));

        Assert.That(result.Invariant.Conditions,
            Has.Some.Matches<SymbolicInvariantCondition>(condition =>
                condition.Text == "this != null" &&
                condition.Target == "this" &&
                condition.IsSolverBacked));
    }

    [Test]
    public void QuerySyntaxTreeLine_ConservativeMergeReportsUnknownForBranchFacts()
    {
        const string source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0) { return 1; } else { return 2; }
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "BranchLineQuery.cs");
        var result = session.AnalyzeLine("if (value > 0)");

        Assert.That(result.ProgramPoints.Count(point => point.NodeKind == "ReturnStatement"), Is.EqualTo(2));
        var conditionTexts = result.ProgramPoints.SelectMany(point => point.Invariant.Conditions)
            .Select(condition => condition.Text);
        Assert.That(conditionTexts, Does.Contain("value > 0"));
        Assert.That(conditionTexts.Any(IsNonPositiveValueFact), Is.True);
        Assert.That(result.ObservedInvariant.MergedInvariantText, Does.Contain("value > 0"));
        Assert.That(ContainsNonPositiveValueFact(result.ObservedInvariant.MergedInvariantText), Is.True);
        Assert.That(result.MergedPathFacts.AlwaysFacts, Is.Empty);
        Assert.That(result.MergedPathFacts.MaybeFacts, Has.Member("value > 0"));
        Assert.That(result.MergedPathFacts.MaybeFacts.Any(IsNonPositiveValueFact), Is.True);
        Assert.That(result.MergedPathFacts.MaybeFacts.Count, Is.InRange(2, 3));
        Assert.That(result.MergedPathFacts.ConservativeUnknowns, Is.EquivalentTo(new[] { "unknown(value)" }));
        var diagnostic = result.MergedPathFacts.ConservativeUnknownDiagnostics.Single();
        Assert.That(diagnostic.UnknownText, Is.EqualTo("unknown(value)"));
        Assert.That(diagnostic.Target, Is.EqualTo("value"));
        Assert.That(diagnostic.Reason, Is.EqualTo("not_common_to_all_candidate_program_points"));
        Assert.That(diagnostic.MaybeFacts, Has.Member("value > 0"));
        Assert.That(diagnostic.MaybeFacts.Any(IsNonPositiveValueFact), Is.True);
        Assert.That(diagnostic.MaybeFacts.Count, Is.InRange(2, 3));
        Assert.That(diagnostic.CandidateProgramPointCount,
            Is.EqualTo(result.MergedPathFacts.CandidateProgramPointCount));
        Assert.That(result.MergedInvariantText, Is.EqualTo("unknown(value)"));
        Assert.That(result.MergedInvariant.Conditions.Single().IsConservativeUnknown, Is.True);
        Assert.That(result.MergedInvariant.Conditions.Single().Target, Is.EqualTo("value"));
    }

    [Test]
    public void QuerySyntaxTreeLine_InvariantQuerySummarizesMustMaybeUnknownFactsAndBudget()
    {
        const string source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0) { return value; } else { return -value; }
    }
}";
        var smtOptions = SmtAnalysisOptions.ForMode(SmtAnalysisMode.Bounded).WithOverrides(
                TimeSpan.FromMilliseconds(321),
                TimeSpan.FromMilliseconds(2345),
                17,
                99);
        using var session = new SymbolicSourceQueryTestSession(
            source,
            "InvariantQueryLine.cs",
            smtOptions: smtOptions);
        var result = session.AnalyzeLine(
            "if (value > 0)",
            impliedConditions: new[] { "value > 0" });

        Assert.That(result.InvariantQuery.Text, Is.EqualTo(result.MergedInvariantText));
        Assert.That(result.InvariantQuery.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(result.InvariantQuery.MustFacts, Is.Empty);
        Assert.That(result.InvariantQuery.MaybeFacts, Has.Member("value > 0"));
        Assert.That(result.InvariantQuery.MaybeFacts.Any(IsNonPositiveValueFact), Is.True);
        Assert.That(result.InvariantQuery.MaybeFacts.Count, Is.InRange(2, 3));
        Assert.That(result.InvariantQuery.UnknownFacts, Is.EquivalentTo(new[] { "unknown(value)" }));
        Assert.That(result.InvariantQuery.HasMaybeFacts, Is.True);
        Assert.That(result.InvariantQuery.HasUnknowns, Is.True);
        Assert.That(result.InvariantQuery.HasUnresolvedAnalysis, Is.True);
        Assert.That(result.InvariantQuery.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Unresolved));
        Assert.That(result.InvariantQuery.Summary, Does.Contain("unresolved"));
        var targetSummary = result.InvariantQuery.TargetSummaries.Single();
        Assert.That(result.InvariantQuery.TargetSummaryCount, Is.EqualTo(1));
        Assert.That(targetSummary.Target, Is.EqualTo("value"));
        Assert.That(targetSummary.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Conservative));
        Assert.That(targetSummary.StatusReason, Is.EqualTo("target_has_conservative_unknowns"));
        Assert.That(targetSummary.ReasonCode, Is.EqualTo("SP-SYM-TARGET-CONSERVATIVE-UNKNOWN"));
        Assert.That(targetSummary.Summary, Does.Contain("conservative unknown"));
        Assert.That(targetSummary.MustFacts, Is.Empty);
        Assert.That(targetSummary.MaybeFacts, Has.Member("value > 0"));
        Assert.That(targetSummary.MaybeFacts.Any(IsNonPositiveValueFact), Is.True);
        Assert.That(targetSummary.MaybeFacts.Count, Is.InRange(2, 3));
        Assert.That(targetSummary.UnknownFacts, Is.EquivalentTo(new[] { "unknown(value)" }));
        var targetPathSummary =
            result.InvariantQuery.TargetPathSummaries.Single(static summary => summary.Target == "value");
        Assert.That(result.InvariantQuery.TargetPathSummaryCount,
            Is.EqualTo(result.InvariantQuery.TargetPathSummaries.Count));
        Assert.That(targetPathSummary.PathConditionCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(targetPathSummary.SmtConditionCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(targetPathSummary.ProgramPointCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(targetPathSummary.ProofTotalCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(targetPathSummary.ProofUnknownCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(targetPathSummary.ReasonCode, Is.EqualTo("SP-SYM-TARGET-PROOF-UNKNOWN"));
        Assert.That(targetPathSummary.Conditions, Does.Contain("value > 0"));
        Assert.That(
            result.InvariantQuery.Diagnostics.Select(static diagnostic => diagnostic.Code),
            Is.EquivalentTo(new[] { "SP-SYM-MAYBE-FACTS", "SP-SYM-CONSERVATIVE-UNKNOWN", "SP-SYM-PROOF-UNKNOWN" }));
        Assert.That(
            result.InvariantQuery.Diagnostics.Single(static diagnostic => diagnostic.Code == "SP-SYM-MAYBE-FACTS")
                .Evidence,
            Has.Member("value > 0"));
        Assert.That(
            result.InvariantQuery.Diagnostics.Single(static diagnostic => diagnostic.Code == "SP-SYM-MAYBE-FACTS")
                .Evidence.Any(IsNonPositiveValueFact),
            Is.True);
        Assert.That(
            result.InvariantQuery.Diagnostics.Single(static diagnostic => diagnostic.Code == "SP-SYM-MAYBE-FACTS")
                .Evidence.Count,
            Is.InRange(2, 3));
        Assert.That(result.InvariantQuery.CandidateProgramPointCount,
            Is.EqualTo(result.MergedPathFacts.CandidateProgramPointCount));
        Assert.That(result.InvariantQuery.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(321));
        Assert.That(result.InvariantQuery.SmtDiagnostics.MethodBudgetMs, Is.EqualTo(2345));
        Assert.That(result.InvariantQuery.SmtDiagnostics.MaxPathConditions, Is.EqualTo(17));
        Assert.That(result.InvariantQuery.SmtDiagnostics.MaxExpressionNodes, Is.EqualTo(99));
        var aggregateProof = result.ConditionProofs.Single(static proof => proof.Condition == "value > 0");
        Assert.That(aggregateProof.Reasons, Is.Not.Empty);
        Assert.That(
            aggregateProof.Reasons.Sum(static reason => reason.Count),
            Is.EqualTo(aggregateProof.TotalCount));
        Assert.That(
            aggregateProof.Reasons.Select(static reason => reason.TruthValue),
            Does.Contain(SymbolicTruthValue.ProvenTrue));

        var positiveReturn = result.ProgramPoints
            .Where(static point => point.NodeKind == "ReturnStatement")
            .Single(point => point.MergedInvariantText == "value > 0");
        Assert.That(positiveReturn.InvariantQuery.MustFacts, Is.EquivalentTo(new[] { "value > 0" }));
        Assert.That(positiveReturn.InvariantQuery.MaybeFacts, Is.Empty);
        Assert.That(positiveReturn.InvariantQuery.UnknownFacts, Is.Empty);
        Assert.That(positiveReturn.InvariantQuery.HasUnresolvedAnalysis, Is.False);
        Assert.That(positiveReturn.InvariantQuery.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Exact));
        Assert.That(positiveReturn.InvariantQuery.Diagnostics, Is.Empty);
        Assert.That(positiveReturn.InvariantQuery.ProofOutcomes.ProvenTrueCount, Is.EqualTo(1));
        var positiveTargetSummary = positiveReturn.InvariantQuery.TargetSummaries.Single();
        Assert.That(positiveTargetSummary.Target, Is.EqualTo("value"));
        Assert.That(positiveTargetSummary.MustFacts, Is.EquivalentTo(new[] { "value > 0" }));
        Assert.That(positiveTargetSummary.MaybeFacts, Is.Empty);
        Assert.That(positiveTargetSummary.UnknownFacts, Is.Empty);
        Assert.That(positiveTargetSummary.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Exact));
        Assert.That(positiveTargetSummary.StatusReason, Is.EqualTo("target_exact"));
        Assert.That(positiveTargetSummary.ReasonCode, Is.EqualTo("SP-SYM-TARGET-EXACT"));
        Assert.That(positiveTargetSummary.Summary, Does.Contain("agree"));
        var positivePathSummary = positiveReturn.InvariantQuery.TargetPathSummaries.Single();
        Assert.That(positivePathSummary.Target, Is.EqualTo("value"));
        Assert.That(positivePathSummary.PathConditionCount, Is.EqualTo(1));
        Assert.That(positivePathSummary.SmtConditionCount, Is.EqualTo(1));
        Assert.That(positivePathSummary.ProofTotalCount, Is.EqualTo(1));
        Assert.That(positivePathSummary.ProofProvenTrueCount, Is.EqualTo(1));
        Assert.That(positivePathSummary.StatusReason, Is.EqualTo("target_has_path_conditions"));
        Assert.That(positivePathSummary.ReasonCode, Is.EqualTo("SP-SYM-TARGET-PATH-CONDITIONS"));
    }

    [Test]
    public void QuerySyntaxTreeLine_InvariantDiagnosticsBoundLargeEvidenceLists()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value == 0) { return 0; } if (value == 1) { return 1; } if (value == 2) { return 2; } if (value == 3) { return 3; } if (value == 4) { return 4; } if (value == 5) { return 5; } if (value == 6) { return 6; } if (value == 7) { return 7; } if (value == 8) { return 8; }
        return 9;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "BoundedInvariantDiagnostics.cs");
        var result = session.AnalyzeLine("if (value == 0)");

        var maybeDiagnostic = result.InvariantQuery.Diagnostics
            .Single(static diagnostic => diagnostic.Code == "SP-SYM-MAYBE-FACTS");
        Assert.That(maybeDiagnostic.EvidenceTotalCount,
            Is.GreaterThan(SymbolicInvariantQueryDiagnostic.DefaultMaxEvidence));
        Assert.That(maybeDiagnostic.Evidence.Count, Is.EqualTo(SymbolicInvariantQueryDiagnostic.DefaultMaxEvidence));
        Assert.That(maybeDiagnostic.EvidenceTruncated, Is.True);
    }

    [Test]
    public void QuerySyntaxTreeSpan_ReturnsMergedInvariantQueryForSourceSpan()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "InvariantSpanQuery.cs");
        var compilation = CSharpCompilation.Create(
            "InvariantSpanQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var spanStart = FindPosition(source, "if (copy > 0)");
        var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0" });

        Assert.That(result.SpanStart, Is.EqualTo(spanStart));
        Assert.That(result.SpanEnd, Is.EqualTo(spanEnd));
        Assert.That(result.StartLine, Is.EqualTo(FindLine(source, "if (copy > 0)")));
        Assert.That(result.EndLine, Is.EqualTo(FindLine(source, "return 0;")));
        Assert.That(result.ProgramPoints.Select(static point => point.NodeKind), Does.Contain("IfStatement"));
        Assert.That(result.ProgramPoints.Count(static point => point.NodeKind == "ReturnStatement"), Is.EqualTo(2));
        Assert.That(result.InvariantQuery.MaybeFacts, Does.Contain("copy > 0"));
        Assert.That(
            result.InvariantQuery.MaybeFacts.Any(static fact => fact is "!(copy > 0)" or "copy <= 0"),
            Is.True);
        Assert.That(result.InvariantQuery.UnknownFacts, Does.Contain("unknown(copy)"));
        Assert.That(result.InvariantQuery.CandidateProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));

        var guardedReturn = result.ProgramPoints
            .Where(static point => point.NodeKind == "ReturnStatement")
            .Single(point => point.Invariant.Conditions.Any(static condition => condition.Text == "copy > 0"));
        Assert.That(guardedReturn.InvariantQuery.MustFacts, Does.Contain("copy > 0"));
        Assert.That(guardedReturn.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void QuerySyntaxTreeLineSpan_ConvertsLineColumnsToSourceSpan()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "LineColumnSpanQuery.cs");
        var spanStart = FindPosition(source, "if (copy > 0)");
        var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
        var start = session.FindMarker("if (copy > 0)");
        var end = session.FindMarker("return 0;");

        var result = session.AnalyzeLineSpan(
            start.Line,
            start.Column,
            end.Line,
            end.Column + "return 0;".Length,
            impliedConditions: new[] { "copy > 0" });

        Assert.That(result.SpanStart, Is.EqualTo(spanStart));
        Assert.That(result.SpanEnd, Is.EqualTo(spanEnd));
        Assert.That(result.ProgramPoints.Select(static point => point.NodeKind), Does.Contain("IfStatement"));
        Assert.That(result.ProgramPoints.Count(static point => point.NodeKind == "ReturnStatement"), Is.EqualTo(2));
        var guardedReturn = result.ProgramPoints
            .Where(static point => point.NodeKind == "ReturnStatement")
            .Single(point => point.Invariant.Conditions.Any(static condition => condition.Text == "copy > 0"));
        Assert.That(guardedReturn.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void QuerySyntaxTreeLine_ClassifiesImpossibleReturnAsUnreachable()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0 && value <= 0) { return value; }
        return 0;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "ImpossibleLineQuery.cs");
        var result = session.AnalyzeLine(
            "value > 0 && value <= 0",
            impliedConditions: new[] { "value > 0" });

        var impossibleReturn = result.ProgramPoints.Single(point => point.NodeKind == "ReturnStatement");
        Assert.That(impossibleReturn.Reachability, Is.EqualTo(SymbolicReachability.Unreachable));
        Assert.That(impossibleReturn.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.Unreachable));
        Assert.That(result.ProgramPointSummary.Reachability.UnreachableCount, Is.GreaterThanOrEqualTo(1));

        var unreachableOnly = result.Filter(new SymbolicSourceQueryFilter(
            reachability: new[] { SymbolicReachability.Unreachable }));
        Assert.That(unreachableOnly.ProgramPoints, Is.Not.Empty);
        Assert.That(unreachableOnly.ProgramPoints.All(point => point.Reachability == SymbolicReachability.Unreachable),
            Is.True);
        Assert.That(unreachableOnly.MergedPathFacts.IsUnreachable, Is.True);
        Assert.That(unreachableOnly.MergedInvariantText, Is.EqualTo("false"));
        Assert.That(unreachableOnly.MergedInvariant.Conditions.Single().Text, Is.EqualTo("false"));
    }

    [Test]
    public void SymbolicLineQueryResult_Filter_RecomputesLineSummary()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "LineFilterQuery.cs");
        var result = session.AnalyzeLine("if (value > 0)");
        var filtered = result.Filter(new SymbolicSourceQueryFilter(new[] { "ReturnStatement" }));

        Assert.That(filtered.ProgramPoints, Has.Count.EqualTo(1));
        Assert.That(filtered.ProgramPoints.Single().NodeKind, Is.EqualTo("ReturnStatement"));
        Assert.That(filtered.Facts, Is.EquivalentTo(filtered.ProgramPoints.Single().Facts));
        Assert.That(filtered.MergedInvariantText, Is.EqualTo(filtered.ProgramPoints.Single().MergedInvariantText));
        Assert.That(filtered.ObservedInvariant.Conditions.Select(condition => condition.Text),
            Is.EquivalentTo(filtered.Facts));
        Assert.That(filtered.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
        Assert.That(filtered.MergedInvariant.Conditions.Select(condition => condition.Text),
            Is.EquivalentTo(filtered.MergedPathFacts.MergedFacts));
        Assert.That(filtered.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(filtered.MergedPathFacts.ConservativeUnknowns, Is.Empty);
        Assert.That(filtered.ProgramPointSummary.ProgramPointCount, Is.EqualTo(filtered.ProgramPoints.Count));
        Assert.That(filtered.ProgramPointSummary.TotalPathConditionCount,
            Is.EqualTo(filtered.ProgramPoints.Single().PathConditionCount));
        Assert.That(filtered.ProgramPointSummary.ProofOutcomes.TotalCount, Is.Zero);
    }

    [Test]
    public void QuerySourceLine_ReturnsEmptyProgramPointsForBlankLine()
    {
        const string source = @"
public class TestClass
{

    public int TestMethod(int value) => value;
}";

        var result = new SymbolicSourceQueryService().QuerySourceLine(
            source,
            "BlankLineQuery.cs",
            FindBlankLine(source),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(result.ProgramPoints, Is.Empty);
        var summary = SymbolicInvariantService.MergeInvariantFacts(result.ProgramPoints.Select(point => point.Facts));
        Assert.That(summary.Facts, Is.Empty);
        Assert.That(summary.MergedInvariantText, Is.EqualTo("true"));
        Assert.That(result.Facts, Is.Empty);
        Assert.That(result.ObservedFactCount, Is.Zero);
        Assert.That(result.ObservedInvariant.IsTrivial, Is.True);
        Assert.That(result.MergedInvariantText, Is.EqualTo("true"));
        Assert.That(result.MergedInvariant.IsTrivial, Is.True);
        Assert.That(result.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(result.MergedInvariant.ConditionCount, Is.Zero);
        Assert.That(result.MergedPathFacts.ConservativeUnknowns, Is.Empty);
        Assert.That(result.ProgramPointSummary.ProgramPointCount, Is.Zero);
        Assert.That(result.ProgramPointSummary.TotalPathConditionCount, Is.Zero);
        Assert.That(result.ProgramPointSummary.MaxPathConditionCount, Is.Zero);
        Assert.That(result.ProgramPointSummary.ProofOutcomes.TotalCount, Is.Zero);
    }

    [Test]
    public void QuerySyntaxTreeAllLines_ReturnsFileLevelAggregateSummary()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "AllLinesQuery.cs");
        var compilation = CSharpCompilation.Create(
            "AllLinesQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
            syntaxTree,
            compilation,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });

        Assert.That(result.FilePath, Is.EqualTo("AllLinesQuery.cs"));
        Assert.That(result.LineCount, Is.EqualTo(syntaxTree.GetText().Lines.Count));
        Assert.That(result.LinesWithProgramPoints, Is.EqualTo(result.Lines.Count));
        Assert.That(result.ProgramPointCount, Is.EqualTo(result.Lines.Sum(line => line.ProgramPoints.Count)));
        Assert.That(result.ProgramPointCount, Is.GreaterThan(0));
        Assert.That(result.ObservedFacts,
            Is.EquivalentTo(result.Lines.SelectMany(line => line.ProgramPoints).SelectMany(point => point.Facts)
                .Distinct()));
        Assert.That(result.ObservedFactCount, Is.EqualTo(result.ObservedFacts.Count));
        Assert.That(result.ObservedFacts.Any(fact => fact.Contains("value", StringComparison.Ordinal)), Is.True);
        Assert.That(result.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
        Assert.That(result.ObservedInvariant.ConditionCount, Is.EqualTo(result.ObservedFactCount));
        Assert.That(result.ObservedInvariant.Conditions.Select(condition => condition.Text),
            Is.EquivalentTo(result.ObservedFacts));
        Assert.That(result.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(result.MergedInvariantText, Is.EqualTo(result.MergedPathFacts.MergedInvariantText));
        Assert.That(result.MergedPathFacts.MaybeFacts.Any(fact => fact.Contains("value", StringComparison.Ordinal)),
            Is.True);
        Assert.That(result.MergedPathFacts.ConservativeUnknowns, Does.Contain("unknown(value)"));
        Assert.That(result.ProgramPointSummary.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(
            result.ProgramPointSummary.TotalPathConditionCount,
            Is.EqualTo(result.Lines.SelectMany(line => line.ProgramPoints).Sum(point => point.PathConditionCount)));
        Assert.That(
            result.ProgramPointSummary.MaxPathConditionCount,
            Is.EqualTo(result.Lines.SelectMany(line => line.ProgramPoints).Max(point => point.PathConditionCount)));
        Assert.That(result.Reachability.ReachableCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(result.ProgramPointSummary.Reachability.ReachableCount,
            Is.EqualTo(result.Reachability.ReachableCount));
        var proofSummary = result.ConditionProofs.Single(summary => summary.Condition == "value > 0");
        Assert.That(proofSummary.ProvenTrueCount, Is.GreaterThan(0));
        Assert.That(
            proofSummary.ProvenTrueCount + proofSummary.ProvenFalseCount + proofSummary.UnreachableCount +
            proofSummary.UnknownCount,
            Is.EqualTo(result.ProgramPointCount));
        Assert.That(result.ProgramPointSummary.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(result.ProgramPointSummary.ProofOutcomes.ProvenTrueCount, Is.EqualTo(proofSummary.ProvenTrueCount));
    }

    [Test]
    public void SymbolicFileQueryResult_Filter_RecomputesAggregateSummary()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "AllLinesFilterQuery.cs");
        var compilation = CSharpCompilation.Create(
            "AllLinesFilterQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
            syntaxTree,
            compilation,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });
        var filtered = result.Filter(new SymbolicSourceQueryFilter(
            new[] { "ReturnStatement" },
            true,
            new[] { SymbolicReachability.Reachable }));

        Assert.That(filtered.Lines, Is.Not.Empty);
        Assert.That(filtered.ProgramPointCount, Is.EqualTo(filtered.Lines.Sum(line => line.ProgramPoints.Count)));
        Assert.That(
            filtered.Lines.SelectMany(line => line.ProgramPoints).All(point => point.NodeKind == "ReturnStatement"),
            Is.True);
        Assert.That(filtered.Lines.SelectMany(line => line.ProgramPoints).All(point => point.Facts.Count != 0),
            Is.True);
        Assert.That(filtered.Reachability.ReachableCount, Is.EqualTo(filtered.ProgramPointCount));
        Assert.That(filtered.ObservedFacts,
            Is.EquivalentTo(filtered.Lines.SelectMany(line => line.ProgramPoints).SelectMany(point => point.Facts)
                .Distinct()));
        Assert.That(filtered.ObservedInvariant.ConditionCount, Is.EqualTo(filtered.ObservedFactCount));
        Assert.That(filtered.ObservedInvariant.Conditions.Select(condition => condition.Text),
            Is.EquivalentTo(filtered.ObservedFacts));
        Assert.That(filtered.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(filtered.MergedInvariantText, Is.EqualTo(filtered.MergedPathFacts.MergedInvariantText));
        Assert.That(filtered.MergedPathFacts.ConservativeUnknowns, Does.Contain("unknown(value)"));
        Assert.That(filtered.ProgramPointSummary.ProgramPointCount, Is.EqualTo(filtered.ProgramPointCount));
        Assert.That(
            filtered.ProgramPointSummary.TotalPathConditionCount,
            Is.EqualTo(filtered.Lines.SelectMany(line => line.ProgramPoints).Sum(point => point.PathConditionCount)));
        Assert.That(filtered.ProgramPointSummary.Reachability.ReachableCount, Is.EqualTo(filtered.ProgramPointCount));
        Assert.That(filtered.ConditionProofs.Single(summary => summary.Condition == "value > 0").ProvenTrueCount,
            Is.GreaterThan(0));
    }

    [Test]
    public void SymbolicSourceQueryFilter_CanFilterByMethodAndConditionMetadata()
    {
        const string source = @"
public class TestClass
{
    public int First(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }

    public int Second(int other)
    {
        if (other > 0) { return other; }
        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "MetadataFilterQuery.cs");
        var compilation = CSharpCompilation.Create(
            "MetadataFilterQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(syntaxTree, compilation);
        var filtered = result.Filter(new SymbolicSourceQueryFilter(
            methodNames: new[] { "First" },
            requirePathConditions: true,
            conditionTargets: new[] { "value" },
            conditionTexts: new[] { "value > 0" },
            conditionTextContains: new[] { "value" }));
        var points = filtered.Lines.SelectMany(static line => line.ProgramPoints).ToArray();

        Assert.That(points, Is.Not.Empty);
        Assert.That(points.All(static point => point.MethodName == "First"), Is.True);
        Assert.That(
            points.All(static point => point.Invariant.Conditions.Any(condition => condition.Target == "value")),
            Is.True);
        Assert.That(
            points.All(static point => point.Invariant.Conditions.Any(condition => condition.Text == "value > 0")),
            Is.True);
        Assert.That(points.All(static point => point.PathConditionCount > 0), Is.True);
        Assert.That(points.Select(static point => point.MethodName), Does.Not.Contain("Second"));

        var compact = SymbolicCompactQueryProjection.Create(
            filtered,
            new SymbolicCompactQueryOptions(maxProgramPoints: 10));
        var compactPoints = compact.Json.GetProperty("lines").EnumerateArray()
            .SelectMany(static line => line.GetProperty("programPoints").EnumerateArray())
            .ToArray();
        Assert.That(compactPoints, Is.Not.Empty);
        Assert.That(compactPoints.All(static point => point.GetProperty("methodName").GetString() == "First"), Is.True);
        Assert.That(compactPoints.All(static point => point.GetProperty("conservativeInvariant")
            .GetProperty("targets").EnumerateArray().Any(target => target.GetString() == "value")), Is.True);
    }

    [Test]
    public void SymbolicSourceQueryFilter_CanFilterByLinePointKindMethodSubstringAndProofMetadata()
    {
        const string source = @"
public class TestClass
{
    public int FirstValue(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }

    public int SecondValue(int value)
    {
        if (value > 0)
        {
            return value + 2;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "RicherFilterQuery.cs");
        var compilation = CSharpCompilation.Create(
            "RicherFilterQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var firstReturnLine = FindLine(source, "return value + 1;");

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
            syntaxTree,
            compilation,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" },
            includeExpressionProgramPoints: true);
        var filtered = result.Filter(new SymbolicSourceQueryFilter(
            methodNameContains: new[] { "First" },
            lines: new[] { firstReturnLine },
            lineStart: firstReturnLine,
            lineEnd: firstReturnLine,
            programPointKinds: new[] { SymbolicProgramPointKinds.Expression },
            requireProofs: true,
            proofOutcomes: new[] { SymbolicTruthValue.ProvenTrue },
            proofConditions: new[] { "value > 0" },
            proofConditionContains: new[] { "value" }));
        var points = filtered.Lines.SelectMany(static line => line.ProgramPoints).ToArray();

        Assert.That(points, Has.Length.EqualTo(1));
        Assert.That(points[0].NodeKind, Is.EqualTo("AddExpression"));
        Assert.That(points[0].ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Expression));
        Assert.That(points[0].MethodName, Is.EqualTo("FirstValue"));
        Assert.That(points[0].Line, Is.EqualTo(firstReturnLine));
        Assert.That(points[0].ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
        Assert.That(filtered.ConditionProofs.Single().TotalCount, Is.EqualTo(1));

        var compactPoint = SymbolicCompactQueryProjection.Create(
                filtered,
                new SymbolicCompactQueryOptions(maxProgramPoints: 10))
            .Json.GetProperty("lines").EnumerateArray()
            .SelectMany(static line => line.GetProperty("programPoints").EnumerateArray())
            .Single();
        Assert.That(compactPoint.GetProperty("programPointKind").GetString(),
            Is.EqualTo(SymbolicProgramPointKinds.Expression));
        Assert.That(compactPoint.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(),
            Is.EqualTo(1));
    }

    [Test]
    public void SharpProofEvidenceSchema_DefinesExactV2Compatibility()
    {
        Assert.That(SharpProofEvidenceSchema.CurrentVersion, Is.EqualTo(2));
        Assert.That(SharpProofEvidenceSchema.CompatibilityPolicy, Is.EqualTo("exact-v2"));
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(
            SharpProofEvidenceSchema.LegacyUnversionedVersion), Is.False);
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(SharpProofEvidenceSchema.CurrentVersion), Is.True);
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(-1), Is.False);
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(SharpProofEvidenceSchema.CurrentVersion + 1), Is.False);
    }

    [Test]
    public void SymbolicProgramPointResult_ToCompactResult_AppliesPointBoundsAndJsonShape()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "CompactPointQuery.cs");
        var compilation = CSharpCompilation.Create(
            "CompactPointQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var position = FindPosition(source, "return value;");

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAtPosition(
            syntaxTree,
            compilation,
            position,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });
        var compact = SymbolicCompactQueryProjection.Create(SymbolicQueryResult.From(result), new SymbolicCompactQueryOptions(
            maxProgramPoints: 0,
            maxFacts: 0,
            maxConditions: 0,
            maxProofs: 0));
        var compactWithFacts = SymbolicCompactQueryProjection.Create(SymbolicQueryResult.From(result), new SymbolicCompactQueryOptions(
            maxProgramPoints: 1,
            maxFacts: 1,
            maxConditions: 1,
            maxProofs: 1));
        var descriptorJson = compact.QueryDescriptor.Json;
        var analysisSummaryJson = compact.AnalysisSummary.Json;
        var invariantQueryJson = compact.InvariantQuery.Json;

        Assert.That(compact.Scope.Kind, Is.EqualTo("point"));
        Assert.That(compact.SchemaVersion, Is.EqualTo(1));
        Assert.That(compact.EvidenceSchemaVersion, Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
        Assert.That(compact.EvidenceSchemaCompatibility,
            Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));
        Assert.That(descriptorJson.GetProperty("kind").GetString(), Is.EqualTo("point"));
        Assert.That(descriptorJson.GetProperty("filePath").GetString(), Is.EqualTo(result.FilePath));
        Assert.That(descriptorJson.GetProperty("line").GetInt32(), Is.EqualTo(result.Line));
        Assert.That(descriptorJson.GetProperty("column").GetInt32(), Is.EqualTo(result.Column));
        Assert.That(descriptorJson.GetProperty("position").GetInt32(), Is.EqualTo(position));
        Assert.That(descriptorJson.GetProperty("nodeKind").GetString(), Is.EqualTo("ReturnStatement"));
        Assert.That(descriptorJson.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Statement));
        Assert.That(compact.Scope.Line, Is.EqualTo(result.Line));
        Assert.That(compact.Scope.Column, Is.EqualTo(result.Column));
        Assert.That(compact.Scope.Position, Is.EqualTo(position));
        Assert.That(compact.Scope.NodeKind, Is.EqualTo("ReturnStatement"));
        Assert.That(compact.Scope.ProgramPointKind, Is.EqualTo(SymbolicProgramPointKinds.Statement));
        Assert.That(compact.Scope.NodeSpanStart, Is.EqualTo(result.NodeSpanStart));
        Assert.That(compact.Scope.NodeSpanEnd, Is.EqualTo(result.NodeSpanEnd));
        Assert.That(compact.Scope.NodeSpanLength, Is.EqualTo(result.NodeSpanLength));
        Assert.That(compact.Scope.NodeStartLine, Is.EqualTo(result.NodeStartLine));
        Assert.That(compact.Scope.NodeStartColumn, Is.EqualTo(result.NodeStartColumn));
        Assert.That(compact.Scope.NodeEndLine, Is.EqualTo(result.NodeEndLine));
        Assert.That(compact.Scope.NodeEndColumn, Is.EqualTo(result.NodeEndColumn));
        Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(compact.Scope.PointReachability, Is.EqualTo(result.Reachability.ToString()));
        Assert.That(compact.Scope.ReachabilityReason, Is.EqualTo(result.ReachabilityReason));
        Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProofOutcomes.TotalCount));
        Assert.That(compact.ProgramPointCount, Is.EqualTo(1));
        Assert.That(analysisSummaryJson.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
        Assert.That(analysisSummaryJson.GetProperty("invariantConditionCount").GetInt32(), Is.EqualTo(result.Invariant.ConditionCount));
        Assert.That(compact.AnalysisSummary.TotalPathConditionCount, Is.EqualTo(result.PathConditionCount));
        Assert.That(compact.AnalysisSummary.MaxPathConditionCount, Is.EqualTo(result.PathConditionCount));
        Assert.That(analysisSummaryJson.GetProperty("reachabilityCheckedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(analysisSummaryJson.GetProperty("reachabilityKnownCount").GetInt32(), Is.EqualTo(1));
        Assert.That(analysisSummaryJson.GetProperty("proofResolvedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(analysisSummaryJson.GetProperty("smtEnabled").GetBoolean(), Is.True);
        Assert.That(compact.AnalysisSummary.HasUnresolvedAnalysis, Is.False);
        Assert.That(invariantQueryJson.GetProperty("statusReason").GetString(),
            Is.EqualTo("all_candidate_program_points_exact"));
        Assert.That(analysisSummaryJson.GetProperty("invariantStatusReason").GetString(),
            Is.EqualTo(invariantQueryJson.GetProperty("statusReason").GetString()));
        Assert.That(compact.ProgramPoints, Is.Empty);
        Assert.That(compact.Truncation.ProgramPoints, Is.True);
        Assert.That(compact.Truncation.Facts, Is.EqualTo(result.Facts.Count > 0));
        Assert.That(compact.Truncation.Conditions, Is.EqualTo(result.PathConditionCount > 0));
        Assert.That(compact.Truncation.Proofs, Is.EqualTo(result.ConditionProofs.Count > 0));
        var observedJson = compact.ObservedInvariant.Json;
        var conservativeJson = compact.ConservativeInvariant.Json;
        Assert.That(observedJson.GetProperty("rawFactCount").GetInt32(), Is.EqualTo(result.Facts.Count));
        Assert.That(observedJson.GetProperty("rawFacts").GetArrayLength(), Is.Zero);
        Assert.That(compact.ConservativeInvariant.ConditionCount, Is.EqualTo(result.Invariant.ConditionCount));
        Assert.That(conservativeJson.GetProperty("conservativeUnknownCount").GetInt32(),
            Is.EqualTo(result.Invariant.ConservativeUnknownCount));
        Assert.That(conservativeJson.GetProperty("hasConservativeUnknowns").GetBoolean(), Is.False);
        Assert.That(conservativeJson.GetProperty("conditions").GetArrayLength(), Is.Zero);
        var symbolicFactsJson = compactWithFacts.ProgramPoints.Single().Json.GetProperty("symbolicFacts");
        Assert.That(symbolicFactsJson.GetArrayLength(), Is.EqualTo(1));
        Assert.That(symbolicFactsJson[0].GetProperty("kind").GetString(), Is.EqualTo("SymbolicRelationAtom"));
        Assert.That(symbolicFactsJson[0].GetProperty("provenance").GetString(), Does.StartWith("ir."));

        var root = compact.Json;
        Assert.That(root.TryGetProperty("kind", out var kind), Is.True);
        Assert.That(kind.GetString(), Is.EqualTo("point"));
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        SymbolicCliTestAssertions.AssertEvidenceSchema(root);
        Assert.That(root.TryGetProperty("Kind", out _), Is.False);
        Assert.That(root.TryGetProperty("lineCount", out _), Is.False);
        var queryDescriptor = root.GetProperty("queryDescriptor");
        Assert.That(queryDescriptor.GetProperty("kind").GetString(), Is.EqualTo("point"));
        Assert.That(queryDescriptor.GetProperty("line").GetInt32(), Is.EqualTo(result.Line));
        Assert.That(queryDescriptor.GetProperty("column").GetInt32(), Is.EqualTo(result.Column));
        Assert.That(queryDescriptor.GetProperty("position").GetInt32(), Is.EqualTo(position));
        Assert.That(queryDescriptor.GetProperty("nodeKind").GetString(), Is.EqualTo("ReturnStatement"));
        Assert.That(queryDescriptor.GetProperty("programPointKind").GetString(),
            Is.EqualTo(SymbolicProgramPointKinds.Statement));
        Assert.That(queryDescriptor.TryGetProperty("spanStart", out _), Is.False);
        Assert.That(root.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Statement));
        Assert.That(root.GetProperty("nodeSpanStart").GetInt32(), Is.EqualTo(result.NodeSpanStart));
        Assert.That(root.GetProperty("nodeSpanEnd").GetInt32(), Is.EqualTo(result.NodeSpanEnd));
        Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.EqualTo(result.MergedInvariantText));
        Assert.That(root.GetProperty("pointReachability").GetString(), Is.EqualTo(result.Reachability.ToString()));
        Assert.That(root.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(),
            Is.EqualTo(result.ProofOutcomes.TotalCount));
        var analysisSummary = root.GetProperty("analysisSummary");
        Assert.That(analysisSummary.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
        Assert.That(analysisSummary.GetProperty("invariantConditionCount").GetInt32(),
            Is.EqualTo(result.Invariant.ConditionCount));
        Assert.That(analysisSummary.GetProperty("invariantStatusReason").GetString(),
            Is.EqualTo("all_candidate_program_points_exact"));
        Assert.That(analysisSummary.GetProperty("reachabilityKnownCount").GetInt32(), Is.EqualTo(1));
        Assert.That(analysisSummary.GetProperty("proofResolvedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(analysisSummary.GetProperty("smtEnabled").GetBoolean(), Is.True);
        Assert.That(analysisSummary.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("invariantQuery").GetProperty("statusReason").GetString(),
            Is.EqualTo("all_candidate_program_points_exact"));
        Assert.That(root.GetProperty("programPoints").GetArrayLength(), Is.Zero);
        Assert.That(root.GetProperty("truncation").GetProperty("isTruncated").GetBoolean(), Is.True);
    }

    [Test]
    public void SymbolicLineQueryResult_ToCompactResult_SeparatesObservedRawFactsFromConservativeMerge()
    {
        const string source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0) { return value; } else { return 0; }
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "CompactLineQuery.cs");
        var result = session.AnalyzeLine(
            "if (value > 0)",
            impliedConditions: new[] { "value > 0" });
        var compact = SymbolicCompactQueryProjection.Create(result, new SymbolicCompactQueryOptions(
            maxProgramPoints: 1,
            maxFacts: 1,
            maxConditions: 1,
            maxProofs: 1));
        var descriptorJson = compact.QueryDescriptor.Json;

        Assert.That(compact.Scope.Kind, Is.EqualTo("line"));
        Assert.That(descriptorJson.GetProperty("kind").GetString(), Is.EqualTo("line"));
        Assert.That(descriptorJson.GetProperty("line").GetInt32(), Is.EqualTo(result.Line));
        Assert.That(descriptorJson.TryGetProperty("column", out _), Is.False);
        Assert.That(descriptorJson.TryGetProperty("position", out _), Is.False);
        Assert.That(descriptorJson.TryGetProperty("spanStart", out _), Is.False);
        Assert.That(compact.ProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));
        Assert.That(compact.ProgramPoints, Has.Count.EqualTo(1));
        Assert.That(compact.Truncation.ProgramPoints, Is.EqualTo(result.ProgramPoints.Count > 1));
        Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
        var observedJson = compact.ObservedInvariant.Json;
        var conservativeJson = compact.ConservativeInvariant.Json;
        Assert.That(observedJson.GetProperty("mergeKind").GetString(),
            Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion.ToString()));
        Assert.That(observedJson.GetProperty("rawFactCount").GetInt32(), Is.EqualTo(result.Facts.Count));
        Assert.That(observedJson.GetProperty("rawFacts").EnumerateArray().Select(static value => value.GetString()),
            Is.EqualTo(result.Facts.Take(1)));
        Assert.That(compact.ObservedInvariant.Text, Does.Contain("value > 0"));
        Assert.That(conservativeJson.GetProperty("mergeKind").GetString(),
            Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge.ToString()));
        Assert.That(compact.ConservativeInvariant.Text, Is.EqualTo(result.MergedInvariantText));
        Assert.That(conservativeJson.GetProperty("conservativeUnknownCount").GetInt32(),
            Is.EqualTo(result.MergedInvariant.ConservativeUnknownCount));
        Assert.That(conservativeJson.GetProperty("hasConservativeUnknowns").GetBoolean(), Is.True);
        Assert.That(compact.ConservativeInvariant.Targets, Does.Contain("value"));
        var mergedPathJson = conservativeJson.GetProperty("mergedPathFacts");
        Assert.That(mergedPathJson.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(mergedPathJson.GetProperty("conservativeUnknowns").EnumerateArray()
                .Select(static value => value.GetString()),
            Does.Contain("unknown(value)"));
        var diagnostic = mergedPathJson.GetProperty("conservativeUnknownDiagnostics").EnumerateArray().Single();
        Assert.That(diagnostic.GetProperty("unknownText").GetString(), Is.EqualTo("unknown(value)"));
        Assert.That(diagnostic.GetProperty("target").GetString(), Is.EqualTo("value"));
        Assert.That(diagnostic.GetProperty("reason").GetString(),
            Is.EqualTo("not_common_to_all_candidate_program_points"));
        Assert.That(diagnostic.GetProperty("maybeFacts").GetArrayLength(), Is.GreaterThan(0));
        Assert.That(compact.Reachability.ReachableCount,
            Is.EqualTo(result.ProgramPointSummary.Reachability.ReachableCount));
        Assert.That(compact.SmtDiagnostics.IsConfigured, Is.True);
        Assert.That(compact.SmtDiagnostics.Mode, Is.EqualTo(SmtAnalysisMode.Bounded.ToString()));

        var compactPoint = compact.ProgramPoints.Single();
        var compactPointJson = compactPoint.Json;
        var sourcePoint = result.ProgramPoints.First();
        Assert.That(compactPointJson.GetProperty("filePath").GetString(), Is.EqualTo(sourcePoint.FilePath));
        Assert.That(compactPointJson.GetProperty("line").GetInt32(), Is.EqualTo(sourcePoint.Line));
        Assert.That(compactPointJson.GetProperty("column").GetInt32(), Is.EqualTo(sourcePoint.Column));
        Assert.That(compactPointJson.GetProperty("position").GetInt32(), Is.EqualTo(sourcePoint.Position));
        Assert.That(compactPointJson.GetProperty("nodeSpanStart").GetInt32(), Is.EqualTo(sourcePoint.NodeSpanStart));
        Assert.That(compactPointJson.GetProperty("nodeSpanEnd").GetInt32(), Is.EqualTo(sourcePoint.NodeSpanEnd));
        Assert.That(compactPointJson.GetProperty("nodeSpanLength").GetInt32(), Is.EqualTo(sourcePoint.NodeSpanLength));
        Assert.That(compactPointJson.GetProperty("nodeStartLine").GetInt32(), Is.EqualTo(sourcePoint.NodeStartLine));
        Assert.That(compactPointJson.GetProperty("nodeStartColumn").GetInt32(), Is.EqualTo(sourcePoint.NodeStartColumn));
        Assert.That(compactPointJson.GetProperty("nodeEndLine").GetInt32(), Is.EqualTo(sourcePoint.NodeEndLine));
        Assert.That(compactPointJson.GetProperty("nodeEndColumn").GetInt32(), Is.EqualTo(sourcePoint.NodeEndColumn));
        Assert.That(compactPointJson.GetProperty("methodName").GetString(), Is.EqualTo(sourcePoint.MethodName));
        Assert.That(compactPointJson.GetProperty("programPointKind").GetString(), Is.EqualTo(sourcePoint.ProgramPointKind));
        Assert.That(compactPointJson.GetProperty("mergedInvariantText").GetString(), Is.EqualTo(sourcePoint.MergedInvariantText));
        Assert.That(compactPointJson.GetProperty("reachability").GetString(), Is.EqualTo(sourcePoint.Reachability.ToString()));
        Assert.That(compactPointJson.GetProperty("reachabilityReason").GetString(), Is.EqualTo(sourcePoint.ReachabilityReason));
        Assert.That(compactPointJson.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.EqualTo(sourcePoint.ProofOutcomes.TotalCount));
    }

    [Test]
    public void SymbolicFileQueryResult_ToCompactResult_AppliesOutputBounds()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        if (value < 0) { return -value; }
        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "CompactFileQuery.cs");
        var compilation = CSharpCompilation.Create(
            "CompactFileQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
            syntaxTree,
            compilation,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });
        var compact = SymbolicCompactQueryProjection.Create(result, new SymbolicCompactQueryOptions(
            1,
            1,
            0,
            0,
            0));
        var descriptorJson = compact.QueryDescriptor.Json;

        Assert.That(compact.Scope.Kind, Is.EqualTo("file"));
        Assert.That(descriptorJson.GetProperty("kind").GetString(), Is.EqualTo("file"));
        Assert.That(descriptorJson.GetProperty("filePath").GetString(), Is.EqualTo(result.FilePath));
        Assert.That(descriptorJson.TryGetProperty("line", out _), Is.False);
        Assert.That(descriptorJson.TryGetProperty("position", out _), Is.False);
        Assert.That(descriptorJson.TryGetProperty("spanStart", out _), Is.False);
        Assert.That(compact.LineCount, Is.EqualTo(result.LineCount));
        Assert.That(compact.Lines, Has.Count.EqualTo(1));
        Assert.That(compact.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(compact.Truncation.Lines, Is.EqualTo(result.Lines.Count > 1));
        Assert.That(compact.Truncation.ProgramPoints, Is.EqualTo(result.ProgramPointCount > 1));
        Assert.That(compact.Truncation.Facts, Is.EqualTo(result.ObservedFactCount > 0));
        Assert.That(compact.Truncation.Conditions, Is.EqualTo(result.MergedInvariant.ConditionCount > 0));
        Assert.That(compact.Truncation.Proofs, Is.EqualTo(result.ConditionProofs.Count > 0));
        Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
        Assert.That(compact.ObservedInvariant.Json.GetProperty("rawFactCount").GetInt32(),
            Is.EqualTo(result.ObservedFactCount));
        Assert.That(compact.ObservedInvariant.Json.GetProperty("rawFacts").GetArrayLength(), Is.Zero);
        Assert.That(compact.ConservativeInvariant.Text, Is.EqualTo(result.MergedInvariantText));
        var mergedPathJson = compact.ConservativeInvariant.Json.GetProperty("mergedPathFacts");
        Assert.That(mergedPathJson.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(mergedPathJson.GetProperty("maybeFactCount").GetInt32(),
            Is.EqualTo(result.MergedPathFacts.MaybeFacts.Count));
        Assert.That(mergedPathJson.GetProperty("maybeFacts").GetArrayLength(), Is.Zero);
        Assert.That(compact.SmtDiagnostics.IsConfigured, Is.True);
    }

    [Test]
    public void SymbolicSpanQueryResult_ToCompactResult_ExposesInvariantQueryAndBudgetMetadata()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "CompactSpanQuery.cs");
        var compilation = CSharpCompilation.Create(
            "CompactSpanQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var spanStart = FindPosition(source, "if (copy > 0)");
        var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
        using var smtAnalysis = new SmtAnalysisService(
            SmtAnalysisOptions.ForMode(SmtAnalysisMode.Bounded).WithOverrides(
                TimeSpan.FromMilliseconds(222),
                TimeSpan.FromMilliseconds(2222),
                22,
                222));

        var result = new SymbolicSourceQueryService().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0" });
        var compact = SymbolicCompactQueryProjection.Create(result, new SymbolicCompactQueryOptions(
            maxProgramPoints: 2,
            maxFacts: 1,
            maxConditions: 2,
            maxProofs: 1));
        var descriptorJson = compact.QueryDescriptor.Json;
        var analysisSummaryJson = compact.AnalysisSummary.Json;
        var invariantQueryJson = compact.InvariantQuery.Json;

        Assert.That(compact.Scope.Kind, Is.EqualTo("span"));
        Assert.That(descriptorJson.GetProperty("kind").GetString(), Is.EqualTo("span"));
        Assert.That(descriptorJson.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
        Assert.That(descriptorJson.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
        Assert.That(descriptorJson.GetProperty("startLine").GetInt32(), Is.EqualTo(FindLine(source, "if (copy > 0)")));
        Assert.That(descriptorJson.GetProperty("endLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));
        Assert.That(compact.Scope.NodeSpanStart, Is.EqualTo(spanStart));
        Assert.That(compact.Scope.NodeSpanEnd, Is.EqualTo(spanEnd));
        Assert.That(compact.Scope.NodeStartLine, Is.EqualTo(FindLine(source, "if (copy > 0)")));
        Assert.That(compact.Scope.NodeEndLine, Is.EqualTo(FindLine(source, "return 0;")));
        Assert.That(invariantQueryJson.GetProperty("text").GetString(), Is.EqualTo(result.InvariantQuery.Text));
        Assert.That(invariantQueryJson.GetProperty("maybeFactCount").GetInt32(),
            Is.EqualTo(result.InvariantQuery.MaybeFactCount));
        Assert.That(invariantQueryJson.GetProperty("maybeFacts").EnumerateArray()
            .Select(static value => value.GetString()), Is.EquivalentTo(result.InvariantQuery.MaybeFacts.Take(2)));
        Assert.That(invariantQueryJson.GetProperty("unknownFacts").EnumerateArray()
            .Select(static value => value.GetString()), Does.Contain("unknown(copy)"));
        Assert.That(invariantQueryJson.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.True);
        Assert.That(invariantQueryJson.GetProperty("statusReason").GetString(),
            Is.EqualTo(result.InvariantQuery.StatusReason));
        Assert.That(invariantQueryJson.GetProperty("targetPathSummaryCount").GetInt32(),
            Is.EqualTo(result.InvariantQuery.TargetPathSummaryCount));
        var compactPathSummary = invariantQueryJson.GetProperty("targetPathSummaries").EnumerateArray()
            .Single(static summary => summary.GetProperty("target").GetString() == "copy");
        Assert.That(compactPathSummary.GetProperty("pathConditionCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
        Assert.That(compactPathSummary.GetProperty("conditions").GetArrayLength(), Is.LessThanOrEqualTo(2));
        Assert.That(compactPathSummary.GetProperty("reasonCode").GetString(), Is.Not.Empty);
        Assert.That(analysisSummaryJson.GetProperty("mustFactCount").GetInt32(), Is.EqualTo(result.InvariantQuery.MustFactCount));
        Assert.That(analysisSummaryJson.GetProperty("maybeFactCount").GetInt32(), Is.EqualTo(result.InvariantQuery.MaybeFactCount));
        Assert.That(analysisSummaryJson.GetProperty("unknownFactCount").GetInt32(), Is.EqualTo(result.InvariantQuery.UnknownFactCount));
        Assert.That(analysisSummaryJson.GetProperty("invariantStatusReason").GetString(),
            Is.EqualTo(invariantQueryJson.GetProperty("statusReason").GetString()));
        Assert.That(analysisSummaryJson.GetProperty("smtQueryTimeoutMs").GetInt32(), Is.EqualTo(222));
        Assert.That(analysisSummaryJson.GetProperty("smtMethodBudgetMs").GetInt32(), Is.EqualTo(2222));
        Assert.That(analysisSummaryJson.GetProperty("smtMaxPathConditions").GetInt32(), Is.EqualTo(22));
        Assert.That(analysisSummaryJson.GetProperty("smtMaxExpressionNodes").GetInt32(), Is.EqualTo(222));
        Assert.That(compact.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(222));
        Assert.That(compact.ProgramPoints, Has.Count.EqualTo(2));
    }

    [Test]
    public void SymbolicSpanQueryResult_ToInvariantQueryResult_EmitsBoundedQueryAnswer()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "InvariantQueryProjection.cs");
        var compilation = CSharpCompilation.Create(
            "InvariantQueryProjection",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var spanStart = FindPosition(source, "if (copy > 0)");
        var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0", "copy <= 0" });
        var invariantResult = SymbolicInvariantQueryProjection.Create(result, new SymbolicCompactQueryOptions(
            maxConditions: 1,
            maxProofs: 1));
        var descriptorJson = invariantResult.QueryDescriptor.Json;
        var querySummaryJson = invariantResult.QuerySummary.Json;
        var focusJson = invariantResult.Focus.Json;
        var analysisSummaryJson = invariantResult.AnalysisSummary.Json;
        var invariantQueryJson = invariantResult.InvariantQuery.Json;

        Assert.That(invariantResult.Kind, Is.EqualTo("invariantQuery"));
        Assert.That(invariantResult.SchemaVersion, Is.EqualTo(1));
        Assert.That(invariantResult.EvidenceSchemaVersion, Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
        Assert.That(invariantResult.EvidenceSchemaCompatibility,
            Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));
        Assert.That(invariantResult.ScopeKind, Is.EqualTo("span"));
        Assert.That(invariantResult.FilePath, Does.EndWith("InvariantQueryProjection.cs"));
        Assert.That(descriptorJson.GetProperty("kind").GetString(), Is.EqualTo("span"));
        Assert.That(descriptorJson.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
        Assert.That(descriptorJson.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
        Assert.That(descriptorJson.GetProperty("startLine").GetInt32(), Is.EqualTo(FindLine(source, "if (copy > 0)")));
        Assert.That(descriptorJson.GetProperty("endLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));
        Assert.That(querySummaryJson.GetProperty("outputMaxConditions").GetInt32(), Is.EqualTo(1));
        Assert.That(querySummaryJson.GetProperty("outputMaxProofs").GetInt32(), Is.EqualTo(1));
        Assert.That(querySummaryJson.GetProperty("programPointCount").GetInt32(), Is.EqualTo(result.ProgramPointCount));
        Assert.That(querySummaryJson.GetProperty("totalPathConditionCount").GetInt32(),
            Is.EqualTo(result.ProgramPointSummary.TotalPathConditionCount));
        Assert.That(querySummaryJson.GetProperty("maxPathConditionCount").GetInt32(),
            Is.EqualTo(result.ProgramPointSummary.MaxPathConditionCount));
        Assert.That(querySummaryJson.GetProperty("proofTotalCount").GetInt32(),
            Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
        Assert.That(querySummaryJson.GetProperty("proofUnknownCount").GetInt32(),
            Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.UnknownCount));
        Assert.That(querySummaryJson.GetProperty("targetCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(querySummaryJson.GetProperty("targets").EnumerateArray().Select(static value => value.GetString()), Does.Contain("copy"));
        Assert.That(querySummaryJson.GetProperty("reasons").GetArrayLength(), Is.LessThanOrEqualTo(1));
        Assert.That(querySummaryJson.GetProperty("reasonCount").GetInt32(),
            Is.GreaterThanOrEqualTo(querySummaryJson.GetProperty("reasons").GetArrayLength()));
        Assert.That(querySummaryJson.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.True);
        Assert.That(invariantResult.QuerySummary.HasTruncatedOutput, Is.True);
        Assert.That(querySummaryJson.GetProperty("conditionsTruncated").GetBoolean(), Is.True);
        Assert.That(querySummaryJson.GetProperty("proofsTruncated").GetBoolean(), Is.True);
        Assert.That(querySummaryJson.GetProperty("smtEnabled").GetBoolean(), Is.True);
        Assert.That(querySummaryJson.GetProperty("pathConditionBudgetExceeded").GetBoolean(), Is.False);
        Assert.That(focusJson.GetProperty("scopeKind").GetString(), Is.EqualTo("span"));
        Assert.That(focusJson.GetProperty("hasSourceLocation").GetBoolean(), Is.True);
        Assert.That(focusJson.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
        Assert.That(focusJson.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
        Assert.That(focusJson.GetProperty("startLine").GetInt32(), Is.EqualTo(FindLine(source, "if (copy > 0)")));
        Assert.That(focusJson.GetProperty("endLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));
        Assert.That(focusJson.GetProperty("programPointCount").GetInt32(), Is.EqualTo(result.ProgramPointCount));
        Assert.That(focusJson.GetProperty("reachabilityStatus").GetString(), Is.Not.Empty);
        Assert.That(focusJson.GetProperty("reachabilityReason").GetString(), Is.Not.Empty);
        Assert.That(invariantResult.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(invariantResult.LinesWithProgramPoints, Is.EqualTo(result.LinesWithProgramPoints));
        Assert.That(invariantResult.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(result.SymbolicFacts, Is.Not.Empty);
        Assert.That(result.InvariantInfo.MergedText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(result.InvariantInfo.MergeKind, Is.EqualTo(result.MergedInvariant.MergeKind));
        Assert.That(result.InvariantInfo.ConditionCount, Is.EqualTo(result.MergedInvariant.ConditionCount));
        Assert.That(result.InvariantInfo.Facts, Is.EquivalentTo(result.SymbolicFacts));
        Assert.That(result.InvariantInfo.Proofs.Select(static proof => proof.Backend),
            Does.Contain(SymbolicProofBackend.Smt));
        Assert.That(invariantQueryJson.GetProperty("text").GetString(), Is.EqualTo(result.InvariantQuery.Text));
        Assert.That(invariantQueryJson.GetProperty("maybeFactCount").GetInt32(),
            Is.EqualTo(result.InvariantQuery.MaybeFactCount));
        Assert.That(invariantQueryJson.GetProperty("maybeFacts").GetArrayLength(), Is.LessThanOrEqualTo(1));
        Assert.That(invariantQueryJson.GetProperty("maybeFactsTruncated").GetBoolean(),
            Is.EqualTo(result.InvariantQuery.MaybeFactCount > 1));
        Assert.That(invariantQueryJson.GetProperty("targetSummaryCount").GetInt32(),
            Is.EqualTo(result.InvariantQuery.TargetSummaryCount));
        Assert.That(invariantQueryJson.GetProperty("targetSummaries").GetArrayLength(), Is.LessThanOrEqualTo(1));
        var compactTargetJson = invariantQueryJson.GetProperty("targetSummaries").EnumerateArray().Single();
        Assert.That(compactTargetJson.GetProperty("target").GetString(), Is.EqualTo("copy"));
        Assert.That(compactTargetJson.GetProperty("status").GetString(),
            Is.EqualTo(SymbolicInvariantQueryStatus.Conservative.ToString()));
        Assert.That(compactTargetJson.GetProperty("statusReason").GetString(),
            Is.EqualTo("target_has_conservative_unknowns"));
        Assert.That(compactTargetJson.GetProperty("reasonCode").GetString(),
            Is.EqualTo("SP-SYM-TARGET-CONSERVATIVE-UNKNOWN"));
        Assert.That(compactTargetJson.GetProperty("summary").GetString(), Does.Contain("conservative unknown"));
        Assert.That(compactTargetJson.GetProperty("maybeFactCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
        Assert.That(compactTargetJson.GetProperty("maybeFacts").GetArrayLength(), Is.LessThanOrEqualTo(1));
        Assert.That(compactTargetJson.GetProperty("maybeFactsTruncated").GetBoolean(), Is.True);
        Assert.That(compactTargetJson.GetProperty("unknownFacts").EnumerateArray()
            .Select(static value => value.GetString()), Does.Contain("unknown(copy)"));
        Assert.That(invariantQueryJson.GetProperty("targetPathSummaryCount").GetInt32(),
            Is.EqualTo(result.InvariantQuery.TargetPathSummaryCount));
        Assert.That(invariantQueryJson.GetProperty("targetPathSummaries").GetArrayLength(), Is.LessThanOrEqualTo(1));
        var compactPathJson = invariantQueryJson.GetProperty("targetPathSummaries").EnumerateArray().Single();
        Assert.That(compactPathJson.GetProperty("target").GetString(), Is.EqualTo("copy"));
        Assert.That(compactPathJson.GetProperty("pathConditionCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
        Assert.That(compactPathJson.GetProperty("smtConditionCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
        Assert.That(compactPathJson.GetProperty("proofTotalCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
        Assert.That(compactPathJson.GetProperty("conditions").GetArrayLength(), Is.LessThanOrEqualTo(1));
        Assert.That(compactPathJson.GetProperty("conditionsTruncated").GetBoolean(), Is.True);
        Assert.That(compactPathJson.GetProperty("reasonCode").GetString(), Is.Not.Empty);
        Assert.That(analysisSummaryJson.GetProperty("programPointCount").GetInt32(), Is.EqualTo(result.ProgramPointCount));
        Assert.That(analysisSummaryJson.GetProperty("invariantStatus").GetString(),
            Is.EqualTo(invariantQueryJson.GetProperty("status").GetString()));
        Assert.That(result.ConditionProofs, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(invariantResult.ConditionProofCount, Is.EqualTo(1));
        Assert.That(invariantResult.ConditionProofs, Has.Count.EqualTo(1));
        Assert.That(invariantResult.ConditionProofs[0].Condition, Is.Not.Empty);
        Assert.That(invariantResult.ConditionProofs[0].Status,
            Is.Not.EqualTo(SymbolicConditionProofSummaryStatus.None));
        Assert.That(invariantResult.ConditionProofs[0].Reasons, Is.Not.Empty);
        Assert.That(invariantResult.ConditionProofsTruncated, Is.True);
        Assert.That(invariantResult.ProofOutcomes.TotalCount,
            Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
        Assert.That(invariantResult.SmtDiagnostics.IsConfigured, Is.True);
    }

    [Test]
    public void SymbolicSpanQueryResult_ToInvariantQueryResult_FiltersTargetSummaries()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        var other = value;
        if (copy > 0 && other < 10)
        {
            return copy + other;
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "InvariantQueryTargetFilter.cs");
        var compilation = CSharpCompilation.Create(
            "InvariantQueryTargetFilter",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var spanStart = FindPosition(source, "if (copy > 0 && other < 10)");
        var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0", "other < 10" });
        Assert.That(
            result.InvariantQuery.TargetPathSummaries.Select(static summary => summary.Target),
            Does.Contain("copy"));
        Assert.That(
            result.InvariantQuery.TargetPathSummaries.Select(static summary => summary.Target),
            Does.Contain("other"));

        var invariantResult = SymbolicInvariantQueryProjection.Create(result, new SymbolicCompactQueryOptions(
            maxConditions: 10,
            maxProofs: 10,
            invariantTargets: new[] { " copy ", "copy" }));
        var querySummaryJson = invariantResult.QuerySummary.Json;
        var analysisSummaryJson = invariantResult.AnalysisSummary.Json;
        var invariantQueryJson = invariantResult.InvariantQuery.Json;

        Assert.That(invariantQueryJson.GetProperty("hasTargetFilter").GetBoolean(), Is.True);
        Assert.That(invariantQueryJson.GetProperty("targetFilterCount").GetInt32(), Is.EqualTo(1));
        Assert.That(invariantQueryJson.GetProperty("targetFilters").EnumerateArray()
            .Select(static value => value.GetString()), Is.EquivalentTo(new[] { "copy" }));
        Assert.That(invariantQueryJson.GetProperty("targetFilterMatched").GetBoolean(), Is.True);
        Assert.That(
            invariantQueryJson.GetProperty("unfilteredTargetPathSummaryCount").GetInt32(),
            Is.GreaterThan(invariantQueryJson.GetProperty("targetPathSummaryCount").GetInt32()));
        Assert.That(
            invariantQueryJson.GetProperty("targetPathSummaries").EnumerateArray()
                .Select(static summary => summary.GetProperty("target").GetString()),
            Is.EquivalentTo(new[] { "copy" }));
        Assert.That(
            invariantQueryJson.GetProperty("targetSummaries").EnumerateArray()
                .Select(static summary => summary.GetProperty("target").GetString()),
            Is.All.EqualTo("copy"));
        var summaryTargets = querySummaryJson.GetProperty("targets").EnumerateArray()
            .Select(static value => value.GetString()).ToArray();
        Assert.That(summaryTargets, Is.EquivalentTo(new[] { "copy" }));
        Assert.That(summaryTargets, Does.Not.Contain("other"));
        Assert.That(invariantResult.MergedInvariantText, Does.Contain("copy"));
        Assert.That(invariantResult.MergedInvariantText, Does.Not.Contain("unknown(other)"));
        Assert.That(invariantQueryJson.GetProperty("text").GetString(), Does.Contain("copy"));
        Assert.That(invariantQueryJson.GetProperty("text").GetString(), Does.Not.Contain("unknown(other)"));
        Assert.That(
            invariantQueryJson.GetProperty("maybeFacts").EnumerateArray().Select(static value => value.GetString()),
            Has.All.Matches<string>(fact => fact.Contains("copy", StringComparison.Ordinal)));
        Assert.That(
            invariantQueryJson.GetProperty("maybeFacts").EnumerateArray().Select(static value => value.GetString()),
            Has.None.Matches<string>(fact => fact.Contains("other", StringComparison.Ordinal)));
        Assert.That(
            invariantQueryJson.GetProperty("unknownFacts").EnumerateArray().Select(static value => value.GetString()),
            Has.All.Matches<string>(fact => fact.Contains("copy", StringComparison.Ordinal)));
        Assert.That(
            invariantQueryJson.GetProperty("unknownFacts").EnumerateArray().Select(static value => value.GetString()),
            Has.None.Matches<string>(fact => fact.Contains("other", StringComparison.Ordinal)));
        Assert.That(
            invariantQueryJson.GetProperty("unknownDiagnostics").EnumerateArray()
                .Select(static diagnostic => diagnostic.GetProperty("target").GetString()),
            Is.All.EqualTo("copy"));
        Assert.That(analysisSummaryJson.GetProperty("maybeFactCount").GetInt32(),
            Is.EqualTo(invariantQueryJson.GetProperty("maybeFactCount").GetInt32()));
        Assert.That(analysisSummaryJson.GetProperty("unknownFactCount").GetInt32(),
            Is.EqualTo(invariantQueryJson.GetProperty("unknownFactCount").GetInt32()));
        Assert.That(invariantResult.ConditionProofCount, Is.EqualTo(1));
        Assert.That(invariantResult.ConditionProofs.Select(static proof => proof.Target),
            Is.EquivalentTo(new[] { "copy" }));
        Assert.That(invariantResult.ConditionProofs.Select(static proof => proof.Target), Does.Not.Contain("other"));
    }

    [Test]
    public void SymbolicSpanQueryResult_ToCompactResult_FiltersPerPointTargetDetails()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        var other = value;
        if (copy > 0)
        {
            if (other < 10)
            {
                return copy + other;
            }
        }

        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "CompactTargetFilter.cs");
        var compilation = CSharpCompilation.Create(
            "CompactTargetFilter",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var spanStart = FindPosition(source, "if (copy > 0)");
        var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0", "other < 10" });
        Assert.That(
            result.ConditionProofs.Select(static proof => proof.Target),
            Does.Contain("copy"));
        Assert.That(
            result.ConditionProofs.Select(static proof => proof.Target),
            Does.Contain("other"));

        var compact = SymbolicCompactQueryProjection.Create(result, new SymbolicCompactQueryOptions(
            maxProgramPoints: 20,
            maxFacts: 20,
            maxConditions: 20,
            maxProofs: 20,
            invariantTargets: new[] { " copy " }));
        var invariantQueryJson = compact.InvariantQuery.Json;

        Assert.That(invariantQueryJson.GetProperty("hasTargetFilter").GetBoolean(), Is.True);
        Assert.That(invariantQueryJson.GetProperty("targetFilters").EnumerateArray()
            .Select(static value => value.GetString()), Is.EquivalentTo(new[] { "copy" }));
        Assert.That(compact.ConditionProofs.Select(static proof => proof.Target), Is.EquivalentTo(new[] { "copy" }));

        var pointProofs = compact.ProgramPoints
            .SelectMany(static point => point.Json.GetProperty("conditionProofs").EnumerateArray())
            .ToArray();
        Assert.That(pointProofs, Is.Not.Empty);
        Assert.That(pointProofs.Select(static proof => proof.GetProperty("target").GetString()), Is.All.EqualTo("copy"));
        Assert.That(pointProofs.Select(static proof => proof.GetProperty("target").GetString()), Does.Not.Contain("other"));

        var pointConditions = compact.ProgramPoints
            .SelectMany(static point => point.Json.GetProperty("invariantConditions").EnumerateArray())
            .ToArray();
        Assert.That(pointConditions.Select(static condition => condition.GetProperty("target").GetString()), Does.Contain("copy"));
        Assert.That(pointConditions.Select(static condition => condition.GetProperty("target").GetString()), Does.Not.Contain("other"));
        Assert.That(
            compact.ProgramPoints.SelectMany(static point => point.Json.GetProperty("facts").EnumerateArray())
                .Select(static fact => fact.GetString()),
            Has.None.Matches<string>(fact => fact.Contains("other", StringComparison.Ordinal)));
    }

    [Test]
    public void SymbolicConditionProofSummary_DescribesReachableProofOutcomes()
    {
        var points = new[]
        {
            CreateSyntheticProofPoint("always", SymbolicTruthValue.ProvenTrue),
            CreateSyntheticProofPoint("always", SymbolicTruthValue.Unreachable),
            CreateSyntheticProofPoint("never", SymbolicTruthValue.ProvenFalse),
            CreateSyntheticProofPoint("mixed", SymbolicTruthValue.ProvenTrue),
            CreateSyntheticProofPoint("mixed", SymbolicTruthValue.ProvenFalse),
            CreateSyntheticProofPoint("unknown", SymbolicTruthValue.Unknown),
            CreateSyntheticProofPoint("unreachable", SymbolicTruthValue.Unreachable)
        };

        var summaries = SymbolicConditionProofSummary
            .FromProgramPoints(points)
            .ToDictionary(static summary => summary.Condition);

        Assert.That(summaries["always"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.AlwaysTrue));
        Assert.That(summaries["always"].ReachableCount, Is.EqualTo(1));
        Assert.That(summaries["always"].ResolvedCount, Is.EqualTo(2));
        Assert.That(summaries["always"].HoldsOnAllReachablePoints, Is.True);
        Assert.That(summaries["always"].Summary, Does.Contain("proven true"));

        Assert.That(summaries["never"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.AlwaysFalse));
        Assert.That(summaries["never"].RefutedOnAllReachablePoints, Is.True);

        Assert.That(summaries["mixed"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.Mixed));
        Assert.That(summaries["mixed"].HasMixedReachableOutcomes, Is.True);

        Assert.That(summaries["unknown"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.Unknown));
        Assert.That(summaries["unknown"].ResolvedCount, Is.Zero);

        Assert.That(summaries["unreachable"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.UnreachableOnly));
        Assert.That(summaries["unreachable"].ReachableCount, Is.Zero);
    }

    [Test]
    public void SymbolicFileQueryResult_ToCompactResult_SummaryOnlyOmitsNestedResults()
    {
        const string source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0) { return value; }
        return 0;
    }
}";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "CompactSummaryOnlyQuery.cs");
        var compilation = CSharpCompilation.Create(
            "CompactSummaryOnlyQuery",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var result = new SymbolicSourceQueryService().QuerySyntaxTreeAllLines(
            syntaxTree,
            compilation,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });
        var compact = SymbolicCompactQueryProjection.Create(result, SymbolicCompactQueryOptions.SummaryOnly);
        var descriptorJson = compact.QueryDescriptor.Json;
        var analysisSummaryJson = compact.AnalysisSummary.Json;

        Assert.That(SymbolicCompactQueryOptions.SummaryOnly.MaxLines, Is.Zero);
        Assert.That(SymbolicCompactQueryOptions.SummaryOnly.MaxProgramPoints, Is.Zero);
        Assert.That(compact.Scope.Kind, Is.EqualTo("file"));
        Assert.That(descriptorJson.GetProperty("kind").GetString(), Is.EqualTo("file"));
        Assert.That(descriptorJson.GetProperty("filePath").GetString(), Is.EqualTo(result.FilePath));
        Assert.That(compact.LineCount, Is.EqualTo(result.LineCount));
        Assert.That(compact.LinesWithProgramPoints, Is.EqualTo(result.LinesWithProgramPoints));
        Assert.That(compact.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(compact.Lines, Is.Empty);
        Assert.That(compact.ProgramPoints, Is.Empty);
        Assert.That(compact.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(compact.ConservativeInvariant.ConditionCount, Is.EqualTo(result.MergedInvariant.ConditionCount));
        Assert.That(compact.ProofOutcomes.TotalCount, Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
        Assert.That(analysisSummaryJson.GetProperty("programPointCount").GetInt32(),
            Is.EqualTo(result.ProgramPointSummary.ProgramPointCount));
        Assert.That(analysisSummaryJson.GetProperty("invariantConditionCount").GetInt32(), Is.EqualTo(result.MergedInvariant.ConditionCount));
        Assert.That(compact.AnalysisSummary.ConservativeUnknownCount,
            Is.EqualTo(result.MergedInvariant.ConservativeUnknownCount));
        Assert.That(compact.AnalysisSummary.TotalPathConditionCount,
            Is.EqualTo(result.ProgramPointSummary.TotalPathConditionCount));
        Assert.That(compact.AnalysisSummary.MaxPathConditionCount,
            Is.EqualTo(result.ProgramPointSummary.MaxPathConditionCount));
        Assert.That(
            analysisSummaryJson.GetProperty("reachabilityCheckedCount").GetInt32(),
            Is.EqualTo(
                result.Reachability.ReachableCount +
                result.Reachability.UnreachableCount +
                result.Reachability.UnknownCount));
        Assert.That(
            analysisSummaryJson.GetProperty("reachabilityKnownCount").GetInt32(),
            Is.EqualTo(result.Reachability.ReachableCount + result.Reachability.UnreachableCount));
        Assert.That(compact.AnalysisSummary.ProofTotalCount,
            Is.EqualTo(result.ProgramPointSummary.ProofOutcomes.TotalCount));
        Assert.That(
            analysisSummaryJson.GetProperty("proofResolvedCount").GetInt32(),
            Is.EqualTo(
                result.ProgramPointSummary.ProofOutcomes.ProvenTrueCount +
                result.ProgramPointSummary.ProofOutcomes.ProvenFalseCount +
                result.ProgramPointSummary.ProofOutcomes.UnreachableCount));
        Assert.That(analysisSummaryJson.GetProperty("smtConfigured").GetBoolean(), Is.True);
        Assert.That(compact.Truncation.Lines, Is.EqualTo(result.Lines.Count > 0));
        Assert.That(compact.Truncation.ProgramPoints, Is.EqualTo(result.ProgramPointCount > 0));
    }

    [Test]
    public async Task SymbolicCli_CompactJson_EmitsPerPointMetadataWhenDetailsAreBounded()
    {
        var source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliCompactMetadata-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "return value;").ToString(),
                "--line-invariants",
                "--check-reachability",
                "--implies",
                "value > 0",
                "--compact-json",
                "--max-points",
                "1",
                "--max-facts",
                "0",
                "--max-conditions",
                "0",
                "--max-proofs",
                "0");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("line"));
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            SymbolicCliTestAssertions.AssertEvidenceSchema(root);
            Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
            Assert.That(root.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            var queryDescriptor = root.GetProperty("queryDescriptor");
            Assert.That(queryDescriptor.GetProperty("kind").GetString(), Is.EqualTo("line"));
            Assert.That(queryDescriptor.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(queryDescriptor.GetProperty("line").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
            Assert.That(queryDescriptor.TryGetProperty("position", out _), Is.False);

            var point = root.GetProperty("programPoints")[0];
            Assert.That(point.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(point.GetProperty("line").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
            Assert.That(point.GetProperty("column").GetInt32(), Is.EqualTo(FindColumn(source, "return value;")));
            Assert.That(point.GetProperty("position").GetInt32(), Is.EqualTo(FindPosition(source, "return value;")));
            Assert.That(point.GetProperty("nodeSpanStart").GetInt32(),
                Is.EqualTo(FindPosition(source, "return value;")));
            Assert.That(point.GetProperty("nodeSpanEnd").GetInt32(),
                Is.GreaterThan(point.GetProperty("nodeSpanStart").GetInt32()));
            Assert.That(point.GetProperty("nodeSpanLength").GetInt32(), Is.GreaterThan(0));
            Assert.That(point.GetProperty("nodeStartLine").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
            Assert.That(point.GetProperty("nodeEndLine").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
            Assert.That(point.GetProperty("programPointKind").GetString(),
                Is.EqualTo(SymbolicProgramPointKinds.Statement));
            Assert.That(point.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
            Assert.That(point.GetProperty("reachability").GetString(),
                Is.EqualTo(SymbolicReachability.Reachable.ToString()));
            Assert.That(point.GetProperty("reachabilityReason").GetString(), Is.Not.Empty);
            Assert.That(point.GetProperty("conditionProofs").GetArrayLength(), Is.Zero);
            Assert.That(point.GetProperty("symbolicFacts").GetArrayLength(), Is.Zero);
            Assert.That(point.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(point.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
            Assert.That(point.GetProperty("conservativeInvariant").GetProperty("text").GetString(),
                Is.EqualTo("value > 0"));
            Assert.That(point.GetProperty("conservativeInvariant").GetProperty("conservativeUnknownCount").GetInt32(),
                Is.Zero);
            Assert.That(point.GetProperty("conservativeInvariant").GetProperty("conditions").GetArrayLength(), Is.Zero);
            Assert.That(point.GetProperty("truncation").GetProperty("conditions").GetBoolean(), Is.True);
            Assert.That(point.GetProperty("truncation").GetProperty("proofs").GetBoolean(), Is.True);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_ProjectExplain_LoadsBuildContextAndAnalyzerInputs()
    {
        var sourcePath = Path.Combine("SharpProof.Demo", "Program.cs");
        var projectPath = Path.Combine("SharpProof.Demo", "SharpProof.Demo.csproj");

        var (exitCode, standardOutput, standardError) = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "explain",
            "--file",
            sourcePath,
            "--project",
            projectPath,
            "--configuration",
            "Debug",
            "--framework",
            "net8.0",
            "--line",
            "39");

        Assert.That(exitCode, Is.Zero, standardError);
        Assert.That(standardOutput, Does.Contain("Project: SharpProof.Demo"));
        Assert.That(standardOutput, Does.Contain("Additional files: 2"));
        Assert.That(standardOutput, Does.Contain("Baseline loaded: True"));
        Assert.That(standardOutput, Does.Contain("Effect summaries: 1"));
        Assert.That(standardOutput, Does.Contain("Build diagnostics"));
        Assert.That(standardOutput, Does.Contain("SP0004 Warning"));
        Assert.That(standardOutput, Does.Contain("Query timeout ms: 321"));
    }

    [Test]
    public async Task SymbolicCli_SolutionExplain_SelectsNamedProject()
    {
        var sourcePath = Path.Combine("SharpProof.Demo", "Program.cs");

        var (exitCode, standardOutput, standardError) = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "explain",
            "--file",
            sourcePath,
            "--solution",
            "SharpProof.sln",
            "--project-name",
            "SharpProof.Demo",
            "--line",
            "39",
            "--smt-mode",
            "disabled");

        Assert.That(exitCode, Is.Zero, standardError);
        Assert.That(standardOutput, Does.Contain("Project: SharpProof.Demo"));
        Assert.That(standardOutput, Does.Contain("Solution file:"));
        Assert.That(standardOutput, Does.Contain("Baseline loaded: True"));
        Assert.That(standardOutput, Does.Contain("Build diagnostics"));
    }

    [Test]
    public async Task SymbolicCli_ProjectModeRejectsStandaloneCompilationOverrides()
    {
        var sourcePath = Path.Combine("SharpProof.Demo", "Program.cs");
        var projectPath = Path.Combine("SharpProof.Demo", "SharpProof.Demo.csproj");

        var (exitCode, _, standardError) = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "--file",
            sourcePath,
            "--project",
            projectPath,
            "--line",
            "39",
            "--language-version",
            "preview");

        Assert.That(exitCode, Is.EqualTo(64));
        Assert.That(
            standardError,
            Does.Contain("Standalone compilation options cannot be combined with --project or --solution"));
    }

    [Test]
    public async Task SymbolicCli_SourceText_UsesVirtualFileNameWithoutTemporaryFile()
    {
        const string source = """
                              public static class InlineSample
                              {
                                  public static int Identity(int value) => value;
                              }
                              """;
        var result = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            source,
            "--source-file-name",
            "virtual/InlineSample.cs",
            "--line",
            FindLine(source, "Identity").ToString(),
            "--compact-json");

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("filePath").GetString(),
            Is.EqualTo("virtual/InlineSample.cs"));
    }

    [Test]
    public async Task SymbolicCli_StdinExplain_PreservesSourceMapMetadata()
    {
        const string source = """
                              public static class StdinSample
                              {
                                  public static int Identity(int value) => value;
                              }
                              """;
        var result = await SymbolicCliTestHost.RunWithInputAsync(
            source,
            "explain",
            "--stdin",
            "--source-file-name",
            "virtual/StdinSample.cs",
            "--source-map-uri",
            "editor://workspace/Original.cs",
            "--source-map-original-line",
            "41",
            "--source-map-original-column",
            "7",
            "--line",
            FindLine(source, "Identity").ToString(),
            "--smt-mode",
            "disabled");

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardOutput, Does.Contain("File: virtual/StdinSample.cs"));
        Assert.That(result.StandardOutput, Does.Contain("Source input: Text"));
        Assert.That(result.StandardOutput,
            Does.Contain("Source map URI: editor://workspace/Original.cs"));
        Assert.That(result.StandardOutput, Does.Contain("Source map origin: line 41, column 7"));
    }

    [TestCase("off")]
    [TestCase("false")]
    [TestCase("true")]
    [TestCase("default")]
    [TestCase("aggressive")]
    public async Task SymbolicCli_SmtModeAliases_AreRejected(string alias)
    {
        var result = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            "public sealed class C { }",
            "--line",
            "1",
            "--smt-mode",
            alias);

        Assert.That(result.ExitCode, Is.EqualTo(64));
        Assert.That(result.StandardError, Does.Contain("must be disabled, bounded, or deep"));
    }

    [Test]
    public async Task SymbolicCli_RejectsMultipleStandaloneSourceSelectors()
    {
        var result = await SymbolicCliTestHost.RunAsync(
            "--stdin",
            "--source-text",
            "class C { }",
            "--line",
            "1");

        Assert.That(result.ExitCode, Is.EqualTo(64));
        Assert.That(result.StandardError,
            Does.Contain("--file, --stdin, and --source-text are mutually exclusive"));
    }

    [Test]
    public async Task SymbolicCli_InlineJsonRequest_CarriesStandaloneAnalysisConfiguration()
    {
        const string source = """
                              #if REQUEST
                              public static class RequestSample
                              {
                                  public static int Select(int value)
                                  {
                                      if (value > 0)
                                      {
                                          return value;
                                      }

                                      return 0;
                                  }
                              }
                              #endif
                              """;
        var requestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            mode = "query",
            source = new
            {
                text = source,
                filePath = "virtual/RequestSample.cs",
                sourceMap = new
                {
                    sourceUri = "editor://workspace/RequestSample.cs",
                    originalStartLine = 11,
                    originalStartColumn = 3
                }
            },
            target = new
            {
                kind = "line",
                line = FindLine(source, "if (value > 0)")
            },
            references = new[] { typeof(object).Assembly.Location },
            parseOptions = new
            {
                languageVersion = "preview",
                preprocessorSymbols = new[] { "REQUEST" },
                nullable = "enable",
                allowUnsafe = false,
                documentationMode = "parse",
                platform = "AnyCpu",
                optimization = "Debug",
                assemblyName = "Request.Assembly"
            },
            impliedConditions = new[] { "value > 0" },
            smt = new
            {
                mode = "bounded",
                timeoutMs = 337,
                methodBudgetMs = 2337,
                maxPathConditions = 37,
                maxExpressionNodes = 337,
                transientRetries = 1,
                recycleContextOnTransientFailure = false,
                disposeContextOnExit = true
            },
            analysisLimits = new Dictionary<string, int>
            {
                ["merged-if-else-facts"] = 13
            },
            query = new
            {
                checkReachability = true,
                includeExpressionProgramPoints = true,
                includeCurrentStatementCompletionFacts = true,
                invariantTargets = new[] { "value" }
            },
            output = new
            {
                format = "compactJson",
                maxLines = 1,
                maxPoints = 3,
                maxFacts = 3,
                maxConditions = 3,
                maxProofs = 3
            }
        });

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("line"));
        Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo("virtual/RequestSample.cs"));
        Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
        Assert.That(root.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(),
            Is.GreaterThan(0));
        Assert.That(root.GetProperty("analysisSummary").GetProperty("smtQueryTimeoutMs").GetInt32(),
            Is.EqualTo(337));
        Assert.That(root.GetProperty("analysisSummary").GetProperty("smtMethodBudgetMs").GetInt32(),
            Is.EqualTo(2337));
    }

    [Test]
    public async Task SymbolicCli_JsonRequestFromStdin_RunsWithoutTemporaryFile()
    {
        const string source = """
                              public static class RequestStdinSample
                              {
                                  public static int Identity(int value) => value;
                              }
                              """;
        var requestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            mode = "explain",
            source = new
            {
                text = source,
                filePath = "virtual/RequestStdinSample.cs",
                sourceMap = new
                {
                    sourceUri = "editor://workspace/OriginalRequest.cs",
                    originalStartLine = 21,
                    originalStartColumn = 5
                }
            },
            target = new
            {
                kind = "point",
                line = FindLine(source, "Identity"),
                column = FindColumn(source, "Identity")
            },
            smt = new { mode = "disabled" },
            output = new { format = "text" }
        });

        var result = await SymbolicCliTestHost.RunWithInputAsync(
            requestJson,
            "--request-json-stdin");

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardOutput, Does.Contain("File: virtual/RequestStdinSample.cs"));
        Assert.That(result.StandardOutput,
            Does.Contain("Source map URI: editor://workspace/OriginalRequest.cs"));
        Assert.That(result.StandardOutput, Does.Contain("Source map origin: line 21, column 5"));
    }

    [Test]
    public async Task SymbolicCli_JsonRequestRejectsUnknownProperties()
    {
        const string requestJson = """
                                   {
                                     "schemaVersion": 1,
                                     "source": { "text": "class C { }" },
                                     "target": { "kind": "point", "line": 1 },
                                     "outputTypo": { "format": "json" }
                                   }
                                   """;

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.EqualTo(SymbolicErrorExitCodes.InvalidData));
        Assert.That(result.StandardError, Is.Empty);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("error").GetProperty("code").GetString(),
            Is.EqualTo(SymbolicErrorCodes.ParseFailed));
        Assert.That(document.RootElement.GetProperty("error").GetProperty("message").GetString(),
            Does.Contain("outputTypo"));
    }

    [Test]
    public async Task SymbolicCli_JsonRequest_CarriesCiExitGates()
    {
        const string source = """
                              public static class RequestGateSample
                              {
                                  public static int Select(int value)
                                  {
                                      if (value > 0) { return 1; } else { return 2; }
                                  }
                              }
                              """;
        var requestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            source = new { text = source, filePath = "virtual/RequestGateSample.cs" },
            target = new { kind = "line", line = FindLine(source, "if (value > 0)") },
            output = new { format = "compactJson" },
            gates = new
            {
                maxConservativeUnknowns = 0,
                compactThresholds = new Dictionary<string, int>
                {
                    ["program-points"] = 10
                }
            }
        });

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.StandardError, Does.Contain("CI gate failed [conservative-unknowns]"));
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
    }

    [TestCase("runtimeHazards", "throw new InvalidOperationException", "runtimeHazards")]
    [TestCase("capabilities", "Console.WriteLine", "capabilities")]
    [TestCase("complexity", "for (var index", "complexity")]
    public async Task SymbolicCli_JsonRequest_RoutesFocusedQueryModes(
        string mode,
        string targetMarker,
        string expectedKind)
    {
        const string source = """
                              using System;

                              public static class RequestModes
                              {
                                  public static void Throw() => throw new InvalidOperationException();

                                  public static void Write() => Console.WriteLine("request");

                                  public static int Sum(int count)
                                  {
                                      var total = 0;
                                      for (var index = 0; index < count; index++) total += index;
                                      return total;
                                  }
                              }
                              """;
        var requestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            mode,
            source = new { text = source, filePath = "virtual/RequestModes.cs" },
            target = new { kind = "line", line = FindLine(source, targetMarker) },
            smt = new { mode = "disabled" },
            output = new
            {
                format = "compactJson",
                maxHazards = mode == "runtimeHazards" ? 5 : (int?)null
            }
        });

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("kind").GetString(), Is.EqualTo(expectedKind));
        Assert.That(document.RootElement.GetProperty("filePath").GetString(),
            Is.EqualTo("virtual/RequestModes.cs"));
    }

    [Test]
    public async Task SymbolicCli_JsonRequest_AcceptsRuntimeHazardFilters()
    {
        const string source = """
                              using System;
                              public static class RequestHazards
                              {
                                  public static void Throw() => throw new InvalidOperationException();
                              }
                              """;
        var requestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            mode = "runtimeHazards",
            source = new { text = source, filePath = "virtual/RequestHazards.cs" },
            target = new { kind = "line", line = FindLine(source, "throw new InvalidOperationException") },
            query = new
            {
                hazardStatuses = new[] { "Proven" },
                hazardExceptionTypes = new[] { "System.InvalidOperationException" },
                hazardCategories = new[] { "direct_throw" }
            },
            output = new { format = "json" }
        });

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("HazardCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task SymbolicCli_LightweightInputsRejectIncompleteMetadataAndProjectMixing()
    {
        var cases = new[]
        {
            new
            {
                Arguments = new[]
                {
                    "--source-text", "class C { }", "--source-map-original-line", "2", "--line", "1"
                },
                Error = "require --source-map-uri"
            },
            new
            {
                Arguments = new[]
                {
                    "--file", Path.Combine("SharpProof.Demo", "Program.cs"),
                    "--source-file-name", "virtual/Demo.cs", "--line", "1"
                },
                Error = "--source-file-name requires --stdin or --source-text"
            },
            new
            {
                Arguments = new[]
                {
                    "--source-text", "class C { }",
                    "--project", Path.Combine("SharpProof.Demo", "SharpProof.Demo.csproj"),
                    "--line", "1"
                },
                Error = "--project and --solution require --file"
            },
            new
            {
                Arguments = new[]
                {
                    "--request-json", "{\"schemaVersion\":2,\"source\":{\"text\":\"class C { }\"},\"target\":{\"kind\":\"point\",\"line\":1}}"
                },
                Error = "schemaVersion must be 1"
            }
        };

        foreach (var item in cases)
        {
            var result = await SymbolicCliTestHost.RunAsync(item.Arguments);
            Assert.That(result.ExitCode, Is.EqualTo(64), string.Join(" ", item.Arguments));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain(item.Error));
        }
    }

    [Test]
    public async Task SymbolicCli_ImplicationExitGate_RequiresEveryProofToSucceed()
    {
        const string source = """
                              public static class ProofGateSample
                              {
                                  public static int Select(int value)
                                  {
                                      if (value > 0) return value;
                                      return 0;
                                  }
                              }
                              """;
        var line = FindLine(source, "return value;").ToString();

        var proven = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            source,
            "--line",
            line,
            "--line-invariants",
            "--node-kind",
            "ReturnStatement",
            "--implies",
            "value > 0",
            "--fail-on-unproven-implies");
        Assert.That(proven.ExitCode, Is.Zero, proven.StandardError);

        var unproven = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            source,
            "--line",
            line,
            "--line-invariants",
            "--node-kind",
            "ReturnStatement",
            "--implies",
            "value < 0",
            "--fail-on-unproven-implies",
            "--compact-json");
        Assert.That(unproven.ExitCode, Is.EqualTo(1));
        Assert.That(unproven.StandardError, Does.Contain("CI gate failed [unproven-implies]"));
        using var document = JsonDocument.Parse(unproven.StandardOutput);
        Assert.That(document.RootElement.GetProperty("kind").GetString(), Is.EqualTo("line"));
    }

    [Test]
    public async Task SymbolicCli_InvariantCountAndCompactGates_UseUntruncatedTotals()
    {
        const string source = """
                              public static class InvariantGateSample
                              {
                                  public static int Select(int value)
                                  {
                                      if (value > 0) { return 1; } else { return 2; }
                                  }
                              }
                              """;
        var commonArguments = new[]
        {
            "--source-text",
            source,
            "--line",
            FindLine(source, "if (value > 0)").ToString(),
            "--line-invariants",
            "--compact-json"
        };

        var unknownFailure = await SymbolicCliTestHost.RunAsync(commonArguments
            .Concat(new[] { "--max-conservative-unknowns", "0" })
            .ToArray());
        Assert.That(unknownFailure.ExitCode, Is.EqualTo(1));
        Assert.That(unknownFailure.StandardError, Does.Contain("CI gate failed [conservative-unknowns]"));

        var unknownPass = await SymbolicCliTestHost.RunAsync(commonArguments
            .Concat(new[] { "--max-conservative-unknowns", "1" })
            .ToArray());
        Assert.That(unknownPass.ExitCode, Is.Zero, unknownPass.StandardError);

        var thresholdFailure = await SymbolicCliTestHost.RunAsync(commonArguments
            .Concat(new[] { "--fail-on-compact-threshold", "conservative-unknowns=0" })
            .ToArray());
        Assert.That(thresholdFailure.ExitCode, Is.EqualTo(1));
        Assert.That(thresholdFailure.StandardError,
            Does.Contain("CI gate failed [compact-threshold.conservative-unknowns]"));

        var truncationFailure = await SymbolicCliTestHost.RunAsync(commonArguments
            .Concat(new[] { "--max-points", "0", "--fail-on-compact-truncation" })
            .ToArray());
        Assert.That(truncationFailure.ExitCode, Is.EqualTo(1));
        Assert.That(truncationFailure.StandardError, Does.Contain("CI gate failed [compact-truncation]"));
        using var document = JsonDocument.Parse(truncationFailure.StandardOutput);
        Assert.That(document.RootElement.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
        Assert.That(document.RootElement.GetProperty("programPoints").GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task SymbolicCli_PostLineInvariants_ExposeCurrentAssignmentCompletionFact()
    {
        var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        value = 7;
        return value;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliPostLineInvariant-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "value = 7;").ToString(),
                "--line-invariants",
                "--post-line-invariants",
                "--check-reachability",
                "--implies",
                "value == 7",
                "--compact-json",
                "--max-points",
                "1",
                "--max-facts",
                "10",
                "--max-conditions",
                "10",
                "--max-proofs",
                "1");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var point = root.GetProperty("programPoints")[0];

            Assert.That(
                point.GetProperty("conditionProofs")[0].GetProperty("truthValue").GetString(),
                Is.EqualTo(SymbolicTruthValue.ProvenTrue.ToString()));
            Assert.That(
                point.GetProperty("conservativeInvariant")
                    .GetProperty("targets")
                    .EnumerateArray()
                    .Select(static target => target.GetString()),
                Does.Contain("value"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_SummaryOnly_EmitsAggregateCompactJsonWithoutNestedResults()
    {
        var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliSummaryOnly-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--all-lines",
                "--check-reachability",
                "--implies",
                "value > 0",
                "--summary-only");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("file"));
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            SymbolicCliTestAssertions.AssertEvidenceSchema(root);
            Assert.That(root.GetProperty("queryDescriptor").GetProperty("kind").GetString(), Is.EqualTo("file"));
            Assert.That(root.GetProperty("queryDescriptor").TryGetProperty("line", out _), Is.False);
            Assert.That(root.GetProperty("lineCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("linesWithProgramPoints").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.GreaterThan(0));
            var analysisSummary = root.GetProperty("analysisSummary");
            Assert.That(
                analysisSummary.GetProperty("programPointCount").GetInt32(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
            Assert.That(analysisSummary.GetProperty("reachabilityCheckedCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(analysisSummary.GetProperty("reachabilityKnownCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(analysisSummary.GetProperty("proofTotalCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(analysisSummary.GetProperty("proofResolvedCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(analysisSummary.GetProperty("smtConfigured").GetBoolean(), Is.True);
            Assert.That(analysisSummary.GetProperty("smtEnabled").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("lines").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("programPoints").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("truncation").GetProperty("lines").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("truncation").GetProperty("programPoints").GetBoolean(), Is.True);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_SpanCompactJson_EmitsInvariantQueryAndBudgetMetadata()
    {
        var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliSpanInvariantQuery-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var spanStart = FindPosition(source, "if (copy > 0)");
            var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--span-start",
                spanStart.ToString(),
                "--span-end",
                spanEnd.ToString(),
                "--check-reachability",
                "--implies",
                "copy > 0",
                "--smt-timeout-ms",
                "333",
                "--smt-method-budget-ms",
                "2333",
                "--smt-max-path-conditions",
                "33",
                "--smt-max-expression-nodes",
                "333",
                "--smt-transient-retries",
                "3",
                "--smt-keep-context-on-transient-failure",
                "--smt-dispose-context-on-exit",
                "--compact-json",
                "--max-points",
                "2",
                "--max-conditions",
                "3");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("span"));
            Assert.That(root.GetProperty("querySpanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(root.GetProperty("querySpanEnd").GetInt32(), Is.EqualTo(spanEnd));
            Assert.That(root.GetProperty("queryStartLine").GetInt32(), Is.EqualTo(FindLine(source, "if (copy > 0)")));
            Assert.That(root.GetProperty("queryEndLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));
            var queryDescriptor = root.GetProperty("queryDescriptor");
            Assert.That(queryDescriptor.GetProperty("kind").GetString(), Is.EqualTo("span"));
            Assert.That(queryDescriptor.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(queryDescriptor.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
            Assert.That(queryDescriptor.GetProperty("startLine").GetInt32(),
                Is.EqualTo(FindLine(source, "if (copy > 0)")));
            Assert.That(queryDescriptor.GetProperty("endLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThanOrEqualTo(2));

            var invariantQuery = root.GetProperty("invariantQuery");
            Assert.That(invariantQuery.GetProperty("maybeFactCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(
                invariantQuery.GetProperty("maybeFacts").EnumerateArray().Select(static fact => fact.GetString()),
                Does.Contain("copy > 0"));
            Assert.That(
                invariantQuery.GetProperty("unknownFacts").EnumerateArray().Select(static fact => fact.GetString()),
                Does.Contain("unknown(copy)"));
            Assert.That(invariantQuery.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.True);
            Assert.That(invariantQuery.GetProperty("status").GetString(),
                Is.EqualTo(SymbolicInvariantQueryStatus.Unresolved.ToString()));
            Assert.That(invariantQuery.GetProperty("summary").GetString(), Does.Contain("unresolved"));
            Assert.That(invariantQuery.GetProperty("diagnosticCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(
                invariantQuery.GetProperty("diagnostics").EnumerateArray()
                    .Select(static diagnostic => diagnostic.GetProperty("code").GetString()),
                Does.Contain("SP-SYM-CONSERVATIVE-UNKNOWN"));
            Assert.That(invariantQuery.GetProperty("targetPathSummaryCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
            var targetPathSummary = invariantQuery.GetProperty("targetPathSummaries")
                .EnumerateArray()
                .Single(static summary => summary.GetProperty("target").GetString() == "copy");
            Assert.That(targetPathSummary.GetProperty("pathConditionCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(targetPathSummary.GetProperty("smtConditionCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(targetPathSummary.GetProperty("proofTotalCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
            Assert.That(targetPathSummary.GetProperty("reasonCode").GetString(), Is.Not.Empty);

            var analysisSummary = root.GetProperty("analysisSummary");
            Assert.That(analysisSummary.GetProperty("maybeFactCount").GetInt32(),
                Is.EqualTo(invariantQuery.GetProperty("maybeFactCount").GetInt32()));
            Assert.That(analysisSummary.GetProperty("unknownFactCount").GetInt32(),
                Is.EqualTo(invariantQuery.GetProperty("unknownFactCount").GetInt32()));
            Assert.That(analysisSummary.GetProperty("invariantStatus").GetString(),
                Is.EqualTo(SymbolicInvariantQueryStatus.Unresolved.ToString()));
            Assert.That(analysisSummary.GetProperty("invariantDiagnosticCount").GetInt32(),
                Is.EqualTo(invariantQuery.GetProperty("diagnosticCount").GetInt32()));
            Assert.That(analysisSummary.GetProperty("smtQueryTimeoutMs").GetInt32(), Is.EqualTo(333));
            Assert.That(analysisSummary.GetProperty("smtMethodBudgetMs").GetInt32(), Is.EqualTo(2333));
            Assert.That(analysisSummary.GetProperty("smtMaxPathConditions").GetInt32(), Is.EqualTo(33));
            Assert.That(analysisSummary.GetProperty("smtMaxExpressionNodes").GetInt32(), Is.EqualTo(333));
            var smtDiagnostics = root.GetProperty("smtDiagnostics");
            Assert.That(smtDiagnostics.GetProperty("health").GetProperty("state").GetString(), Is.EqualTo("Ready"));
            Assert.That(
                smtDiagnostics.GetProperty("health").GetProperty("isPermanentlyUnavailable").GetBoolean(),
                Is.False);
            var lifecycle = smtDiagnostics.GetProperty("lifecycle");
            Assert.That(lifecycle.GetProperty("maxTransientRetries").GetInt32(), Is.EqualTo(3));
            Assert.That(lifecycle.GetProperty("recycleContextOnTransientFailure").GetBoolean(), Is.False);
            Assert.That(lifecycle.GetProperty("disposeCurrentThreadContextOnServiceDispose").GetBoolean(), Is.True);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_LineColumnSpan_QueriesSpanWithoutAbsoluteOffsets()
    {
        var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliLineColumnSpanQuery-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var spanStart = FindPosition(source, "if (copy > 0)");
            var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--span-start-line",
                FindLine(source, "if (copy > 0)").ToString(),
                "--span-start-column",
                FindColumn(source, "if (copy > 0)").ToString(),
                "--span-end-line",
                FindLine(source, "return 0;").ToString(),
                "--span-end-column",
                (FindColumn(source, "return 0;") + "return 0;".Length).ToString(),
                "--implies",
                "copy > 0",
                "--compact-json",
                "--max-points",
                "3");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("span"));
            Assert.That(root.GetProperty("querySpanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(root.GetProperty("querySpanEnd").GetInt32(), Is.EqualTo(spanEnd));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(
                root.GetProperty("programPoints")
                    .EnumerateArray()
                    .SelectMany(static point => point.GetProperty("conditionProofs").EnumerateArray())
                    .Any(static proof => proof.GetProperty("truthValue").GetString() ==
                                         SymbolicTruthValue.ProvenTrue.ToString()),
                Is.True);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_InvariantTargetFilter_NarrowsInvariantJsonTargetSections()
    {
        var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        var other = value;
        if (copy > 0 && other < 10)
        {
            return copy + other;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliInvariantTargetFilter-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var spanStart = FindPosition(source, "if (copy > 0 && other < 10)");
            var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--span-start",
                spanStart.ToString(),
                "--span-end",
                spanEnd.ToString(),
                "--check-reachability",
                "--implies",
                "copy > 0",
                "--implies",
                "other < 10",
                "--invariant-json",
                "--invariant-target",
                "copy",
                "--invariant-target",
                "missing");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("invariantQuery"));
            Assert.That(root.GetProperty("mergedInvariantText").GetString(), Does.Contain("copy"));
            Assert.That(root.GetProperty("mergedInvariantText").GetString(), Does.Not.Contain("unknown(other)"));

            var querySummary = root.GetProperty("querySummary");
            Assert.That(
                querySummary.GetProperty("targets").EnumerateArray().Select(static target => target.GetString()),
                Is.EquivalentTo(new[] { "copy" }));

            var invariantQuery = root.GetProperty("invariantQuery");
            Assert.That(invariantQuery.GetProperty("hasTargetFilter").GetBoolean(), Is.True);
            Assert.That(invariantQuery.GetProperty("targetFilterCount").GetInt32(), Is.EqualTo(2));
            Assert.That(
                invariantQuery.GetProperty("targetFilters").EnumerateArray()
                    .Select(static target => target.GetString()),
                Is.EquivalentTo(new[] { "copy", "missing" }));
            Assert.That(invariantQuery.GetProperty("targetFilterMatched").GetBoolean(), Is.True);
            Assert.That(invariantQuery.GetProperty("matchedTargetFilterCount").GetInt32(), Is.EqualTo(1));
            Assert.That(
                invariantQuery.GetProperty("matchedTargetFilters").EnumerateArray()
                    .Select(static target => target.GetString()),
                Is.EquivalentTo(new[] { "copy" }));
            Assert.That(invariantQuery.GetProperty("matchedTargetFiltersTruncated").GetBoolean(), Is.False);
            Assert.That(invariantQuery.GetProperty("unmatchedTargetFilterCount").GetInt32(), Is.EqualTo(1));
            Assert.That(
                invariantQuery.GetProperty("unmatchedTargetFilters").EnumerateArray()
                    .Select(static target => target.GetString()),
                Is.EquivalentTo(new[] { "missing" }));
            Assert.That(invariantQuery.GetProperty("unmatchedTargetFiltersTruncated").GetBoolean(), Is.False);
            Assert.That(
                invariantQuery.GetProperty("unfilteredTargetPathSummaryCount").GetInt32(),
                Is.GreaterThan(invariantQuery.GetProperty("targetPathSummaryCount").GetInt32()));
            Assert.That(invariantQuery.GetProperty("text").GetString(), Does.Contain("copy"));
            Assert.That(invariantQuery.GetProperty("text").GetString(), Does.Not.Contain("unknown(other)"));
            var maybeFacts = invariantQuery.GetProperty("maybeFacts")
                .EnumerateArray()
                .Select(static fact => fact.GetString() ?? string.Empty)
                .ToArray();
            Assert.That(maybeFacts.All(static fact => fact.Contains("copy", StringComparison.Ordinal)), Is.True);
            Assert.That(maybeFacts.Any(static fact => fact.Contains("other", StringComparison.Ordinal)), Is.False);
            var unknownFacts = invariantQuery.GetProperty("unknownFacts")
                .EnumerateArray()
                .Select(static fact => fact.GetString() ?? string.Empty)
                .ToArray();
            Assert.That(unknownFacts.All(static fact => fact.Contains("copy", StringComparison.Ordinal)), Is.True);
            Assert.That(unknownFacts.Any(static fact => fact.Contains("other", StringComparison.Ordinal)), Is.False);
            Assert.That(
                invariantQuery.GetProperty("unknownDiagnostics").EnumerateArray()
                    .Select(static diagnostic => diagnostic.GetProperty("target").GetString()),
                Is.All.EqualTo("copy"));

            var targetSummaries = invariantQuery.GetProperty("targetSummaries")
                .EnumerateArray()
                .Select(static summary => summary.GetProperty("target").GetString())
                .ToArray();
            Assert.That(targetSummaries, Is.All.EqualTo("copy"));
            Assert.That(targetSummaries, Does.Not.Contain("other"));

            var targetPathSummaries = invariantQuery.GetProperty("targetPathSummaries")
                .EnumerateArray()
                .Select(static summary => summary.GetProperty("target").GetString())
                .ToArray();
            Assert.That(targetPathSummaries, Is.EquivalentTo(new[] { "copy" }));
            Assert.That(root.GetProperty("conditionProofCount").GetInt32(), Is.EqualTo(1));
            Assert.That(
                root.GetProperty("conditionProofs").EnumerateArray()
                    .Select(static proof => proof.GetProperty("target").GetString()),
                Is.EquivalentTo(new[] { "copy" }));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_InvariantTargetFilter_TextReportsMatchedAndUnmatchedTargets()
    {
        var source = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliInvariantTargetFilterText-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var spanStart = FindPosition(source, "if (copy > 0)");
            var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--span-start",
                spanStart.ToString(),
                "--span-end",
                spanEnd.ToString(),
                "--check-reachability",
                "--implies",
                "copy > 0",
                "--invariant-target",
                "copy",
                "--invariant-target",
                "missing");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("Span invariant query target filter: copy, missing"));
            Assert.That(result.StandardOutput, Does.Contain("Span invariant query target filter matched: True"));
            Assert.That(result.StandardOutput, Does.Contain("Span invariant query matched target filters: copy"));
            Assert.That(result.StandardOutput, Does.Contain("Span invariant query unmatched target filters: missing"));
            Assert.That(result.StandardOutput, Does.Contain("Span invariant query text:"));
            Assert.That(result.StandardOutput, Does.Contain("unknown(copy)"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_InvariantJson_LineColumnSpanEmitsOnlyInvariantAnswer()
    {
        var source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        var copy = value;
        if (copy > 0)
        {
            return copy;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliInvariantJsonLineSpan-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var spanStart = FindPosition(source, "if (copy > 0)");
            var spanEnd = FindPosition(source, "return 0;") + "return 0;".Length;
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--span-start-line",
                FindLine(source, "if (copy > 0)").ToString(),
                "--span-start-column",
                FindColumn(source, "if (copy > 0)").ToString(),
                "--span-end-line",
                FindLine(source, "return 0;").ToString(),
                "--span-end-column",
                (FindColumn(source, "return 0;") + "return 0;".Length).ToString(),
                "--check-reachability",
                "--implies",
                "copy > 0",
                "--implies",
                "copy <= 0",
                "--condition-target",
                "copy",
                "--invariant-json",
                "--max-conditions",
                "1",
                "--max-proofs",
                "1");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("invariantQuery"));
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            SymbolicCliTestAssertions.AssertEvidenceSchema(root);
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("span"));
            Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(root.TryGetProperty("programPoints", out _), Is.False);
            Assert.That(root.TryGetProperty("lines", out _), Is.False);

            var queryDescriptor = root.GetProperty("queryDescriptor");
            Assert.That(queryDescriptor.GetProperty("kind").GetString(), Is.EqualTo("span"));
            Assert.That(queryDescriptor.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(queryDescriptor.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
            Assert.That(queryDescriptor.GetProperty("startLine").GetInt32(),
                Is.EqualTo(FindLine(source, "if (copy > 0)")));
            Assert.That(queryDescriptor.GetProperty("endLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));

            var querySummary = root.GetProperty("querySummary");
            Assert.That(querySummary.GetProperty("outputMaxConditions").GetInt32(), Is.EqualTo(1));
            Assert.That(querySummary.GetProperty("outputMaxProofs").GetInt32(), Is.EqualTo(1));
            Assert.That(
                querySummary.GetProperty("programPointCount").GetInt32(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
            Assert.That(querySummary.GetProperty("totalPathConditionCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(querySummary.GetProperty("maxPathConditionCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(querySummary.GetProperty("proofTotalCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(querySummary.GetProperty("targetCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
            Assert.That(querySummary.GetProperty("targets").GetArrayLength(), Is.LessThanOrEqualTo(1));
            Assert.That(querySummary.GetProperty("targets")[0].GetString(), Is.EqualTo("copy"));
            Assert.That(querySummary.GetProperty("reasonCount").GetInt32(), Is.GreaterThanOrEqualTo(0));
            Assert.That(querySummary.GetProperty("reasons").GetArrayLength(), Is.LessThanOrEqualTo(1));
            Assert.That(querySummary.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.True);
            Assert.That(querySummary.GetProperty("hasTruncatedOutput").GetBoolean(), Is.True);
            Assert.That(querySummary.GetProperty("conditionsTruncated").GetBoolean(), Is.True);
            Assert.That(querySummary.GetProperty("proofsTruncated").GetBoolean(), Is.True);
            Assert.That(querySummary.GetProperty("smtEnabled").GetBoolean(), Is.True);
            Assert.That(querySummary.GetProperty("pathConditionBudgetExceeded").GetBoolean(), Is.False);

            var focus = root.GetProperty("focus");
            Assert.That(focus.GetProperty("scopeKind").GetString(), Is.EqualTo("span"));
            Assert.That(focus.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(focus.GetProperty("hasSourceLocation").GetBoolean(), Is.True);
            Assert.That(focus.TryGetProperty("line", out _), Is.False);
            Assert.That(focus.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(focus.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
            Assert.That(focus.GetProperty("startLine").GetInt32(), Is.EqualTo(FindLine(source, "if (copy > 0)")));
            Assert.That(focus.GetProperty("endLine").GetInt32(), Is.EqualTo(FindLine(source, "return 0;")));
            Assert.That(focus.GetProperty("programPointCount").GetInt32(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
            Assert.That(focus.GetProperty("reachabilityStatus").GetString(), Is.Not.Empty);
            Assert.That(focus.GetProperty("reachabilityReason").GetString(), Is.Not.Empty);

            var invariantQuery = root.GetProperty("invariantQuery");
            Assert.That(invariantQuery.GetProperty("maybeFactCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(invariantQuery.GetProperty("maybeFacts").GetArrayLength(), Is.EqualTo(1));
            Assert.That(invariantQuery.GetProperty("maybeFactsTruncated").GetBoolean(), Is.True);
            Assert.That(invariantQuery.GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.True);
            Assert.That(invariantQuery.GetProperty("status").GetString(),
                Is.EqualTo(SymbolicInvariantQueryStatus.Conservative.ToString()));
            Assert.That(invariantQuery.GetProperty("targetSummaryCount").GetInt32(), Is.GreaterThanOrEqualTo(1));
            Assert.That(invariantQuery.GetProperty("targetSummaries").GetArrayLength(), Is.EqualTo(1));
            Assert.That(invariantQuery.GetProperty("targetSummariesTruncated").GetBoolean(), Is.False);
            var targetSummary = invariantQuery.GetProperty("targetSummaries")[0];
            Assert.That(targetSummary.GetProperty("target").GetString(), Is.EqualTo("copy"));
            Assert.That(targetSummary.GetProperty("status").GetString(),
                Is.EqualTo(SymbolicInvariantQueryStatus.Conservative.ToString()));
            Assert.That(targetSummary.GetProperty("statusReason").GetString(),
                Is.EqualTo("target_has_conservative_unknowns"));
            Assert.That(targetSummary.GetProperty("reasonCode").GetString(),
                Is.EqualTo("SP-SYM-TARGET-CONSERVATIVE-UNKNOWN"));
            Assert.That(targetSummary.GetProperty("summary").GetString(), Does.Contain("conservative unknown"));
            Assert.That(targetSummary.GetProperty("maybeFactCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(targetSummary.GetProperty("maybeFacts").GetArrayLength(), Is.EqualTo(1));
            Assert.That(targetSummary.GetProperty("maybeFactsTruncated").GetBoolean(), Is.True);
            Assert.That(
                targetSummary.GetProperty("unknownFacts").EnumerateArray().Select(static fact => fact.GetString()),
                Does.Contain("unknown(copy)"));
            var targetPathSummaryCount = invariantQuery.GetProperty("targetPathSummaryCount").GetInt32();
            var targetPathSummaries = invariantQuery.GetProperty("targetPathSummaries");
            Assert.That(targetPathSummaryCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(targetPathSummaries.GetArrayLength(), Is.EqualTo(1));
            Assert.That(
                invariantQuery.GetProperty("targetPathSummariesTruncated").GetBoolean(),
                Is.EqualTo(targetPathSummaryCount > targetPathSummaries.GetArrayLength()));
            var targetPathSummary = targetPathSummaries[0];
            Assert.That(targetPathSummary.GetProperty("target").GetString(), Is.EqualTo("copy"));
            Assert.That(targetPathSummary.GetProperty("pathConditionCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            Assert.That(targetPathSummary.GetProperty("conditions").GetArrayLength(), Is.EqualTo(1));
            Assert.That(targetPathSummary.GetProperty("conditionsTruncated").GetBoolean(), Is.True);
            Assert.That(targetPathSummary.GetProperty("statusReason").GetString(), Is.Not.Empty);
            Assert.That(targetPathSummary.GetProperty("reasonCode").GetString(), Is.Not.Empty);

            Assert.That(root.GetProperty("conditionProofCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("conditionProofs").GetArrayLength(), Is.EqualTo(1));
            Assert.That(root.GetProperty("conditionProofsTruncated").GetBoolean(), Is.True);
            var proof = root.GetProperty("conditionProofs")[0];
            Assert.That(proof.GetProperty("condition").GetString(), Is.Not.Empty);
            Assert.That(proof.GetProperty("status").GetString(),
                Is.Not.EqualTo(SymbolicConditionProofSummaryStatus.None.ToString()));
            Assert.That(proof.GetProperty("summary").GetString(), Is.Not.Empty);
            Assert.That(proof.GetProperty("reasons").GetArrayLength(), Is.GreaterThan(0));

            var analysisSummary = root.GetProperty("analysisSummary");
            Assert.That(
                analysisSummary.GetProperty("programPointCount").GetInt32(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
            Assert.That(
                analysisSummary.GetProperty("invariantStatus").GetString(),
                Is.EqualTo(invariantQuery.GetProperty("status").GetString()));
            Assert.That(analysisSummary.GetProperty("proofTotalCount").GetInt32(),
                Is.GreaterThan(root.GetProperty("conditionProofCount").GetInt32()));
            Assert.That(root.GetProperty("smtDiagnostics").GetProperty("isConfigured").GetBoolean(), Is.True);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_LineExpressions_AllowsFilteringToExpressionProgramPoint()
    {
        var source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliLineExpressions-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "return value + 1;").ToString(),
                "--line-invariants",
                "--line-expressions",
                "--program-point-kind",
                "Expression",
                "--node-kind",
                "AddExpression",
                "--check-reachability",
                "--implies",
                "value > 0",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("line"));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
            Assert.That(root.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));

            var point = root.GetProperty("programPoints")[0];
            Assert.That(point.GetProperty("nodeKind").GetString(), Is.EqualTo("AddExpression"));
            Assert.That(point.GetProperty("programPointKind").GetString(),
                Is.EqualTo(SymbolicProgramPointKinds.Expression));
            Assert.That(point.GetProperty("mergedInvariantText").GetString(), Is.EqualTo("value > 0"));
            Assert.That(point.GetProperty("conditionProofs")[0].GetProperty("truthValue").GetString(),
                Is.EqualTo(SymbolicTruthValue.ProvenTrue.ToString()));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RicherFilters_NarrowExpressionProofResults()
    {
        var source = @"
public class TestClass
{
    public int FirstValue(int value)
    {
        if (value > 0)
        {
            return value + 1;
        }

        return 0;
    }

    public int SecondValue(int value)
    {
        if (value > 0)
        {
            return value + 2;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliRicherFilters-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var firstReturnLine = FindLine(source, "return value + 1;");
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--all-lines",
                "--line-expressions",
                "--method-contains",
                "First",
                "--filter-line",
                firstReturnLine.ToString(),
                "--line-start",
                firstReturnLine.ToString(),
                "--line-end",
                firstReturnLine.ToString(),
                "--program-point-kind",
                "Expression",
                "--with-proofs",
                "--proof-outcome",
                "ProvenTrue",
                "--proof-condition",
                "value > 0",
                "--proof-condition-contains",
                "value",
                "--implies",
                "value > 0",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("file"));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("conditionProofs")[0].GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(
                root.GetProperty("conditionProofs")[0].GetProperty("reasons")[0].GetProperty("truthValue").GetString(),
                Is.EqualTo(SymbolicTruthValue.ProvenTrue.ToString()));
            Assert.That(
                root.GetProperty("conditionProofs")[0].GetProperty("reasons")[0].GetProperty("reason").GetString(),
                Is.Not.Empty);

            var point = root
                .GetProperty("lines")[0]
                .GetProperty("programPoints")[0];
            Assert.That(point.GetProperty("line").GetInt32(), Is.EqualTo(firstReturnLine));
            Assert.That(point.GetProperty("nodeKind").GetString(), Is.EqualTo("AddExpression"));
            Assert.That(point.GetProperty("programPointKind").GetString(),
                Is.EqualTo(SymbolicProgramPointKinds.Expression));
            Assert.That(point.GetProperty("methodName").GetString(), Is.EqualTo("FirstValue"));
            Assert.That(point.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_FilterMetadataSwitches_NarrowAllLinesCompactJson()
    {
        var source = @"
public class TestClass
{
    public int First(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }

    public int Second(int other)
    {
        if (other > 0)
        {
            return other;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliMetadataFilters-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--all-lines",
                "--method",
                "First",
                "--with-conditions",
                "--condition-target",
                "value",
                "--condition",
                "value > 0",
                "--condition-contains",
                "value",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("file"));
            var points = root
                .GetProperty("lines")
                .EnumerateArray()
                .SelectMany(static line => line.GetProperty("programPoints").EnumerateArray())
                .ToArray();
            Assert.That(points, Is.Not.Empty);
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(points.Length));
            foreach (var point in points)
            {
                Assert.That(point.GetProperty("methodName").GetString(), Is.EqualTo("First"));
                Assert.That(
                    point.GetProperty("conservativeInvariant").GetProperty("targets").EnumerateArray()
                        .Select(static target => target.GetString()),
                    Does.Contain("value"));
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_TextOutput_EmitsInvariantStatusReasonAndProofSummary()
    {
        var source = @"
public class TestClass
{
    public static int TestMethod(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 0;
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliTextStatusReason-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                FindLine(source, "return value;").ToString(),
                "--line-invariants",
                "--check-reachability",
                "--implies",
                "value > 0");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput,
                Does.Contain("Line invariant query status reason: all_candidate_program_points_exact"));
            Assert.That(result.StandardOutput,
                Does.Contain(
                    "Line invariant query target: value status=Exact reason=target_exact code=SP-SYM-TARGET-EXACT"));
            Assert.That(result.StandardOutput,
                Does.Contain(
                    "Line invariant query target summary: All selected reachable program points agree on the facts for this target."));
            Assert.That(result.StandardOutput,
                Does.Contain("Line invariant query target path: value conditions=1 smt=1"));
            Assert.That(result.StandardOutput,
                Does.Contain(
                    "Line invariant query target path summary: This target has source-location path conditions available for invariant queries."));
            Assert.That(result.StandardOutput, Does.Contain("Line invariant query target path conditions: value > 0"));
            Assert.That(result.StandardOutput,
                Does.Contain("Implies 'value > 0' target=value kind=SmtBinary summary: Status=AlwaysTrue"));
            Assert.That(result.StandardOutput,
                Does.Contain(
                    "Proof summary: The condition is proven true at every reachable candidate program point."));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_Json_EmitsEnumNames()
    {
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliFullJson-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, "public class C { public int M(int value) => value; }\n");
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("pointReachability").ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(
                Enum.TryParse<SymbolicReachability>(root.GetProperty("pointReachability").GetString(), out _),
                Is.True);
            Assert.That(root.GetProperty("observedInvariant").GetProperty("mergeKind").ValueKind,
                Is.EqualTo(JsonValueKind.String));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazards_CompactJsonEmitsBoundedMetadata()
    {
        var source = @"
public class TestClass
{
    public void First()
    {
        throw new System.InvalidOperationException(""one"");
    }

    public void Second()
    {
        throw new System.ArgumentException(""two"");
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliRuntimeHazardCompact-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--hazard-kind",
                "DirectThrow",
                "--compact-json",
                "--max-hazards",
                "1");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("runtimeHazards"));
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            SymbolicCliTestAssertions.AssertEvidenceSchema(root);
            Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("file"));
            Assert.That(root.GetProperty("lineCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("statusCounts").GetProperty("Proven").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("kindCounts").GetProperty("DirectThrow").GetInt32(), Is.EqualTo(2));
            Assert.That(
                root.GetProperty("exceptionTypeCounts").GetProperty("System.InvalidOperationException").GetInt32(),
                Is.EqualTo(1));
            Assert.That(root.GetProperty("exceptionTypeCounts").GetProperty("System.ArgumentException").GetInt32(),
                Is.EqualTo(1));
            Assert.That(root.GetProperty("categoryCounts").GetProperty("direct_throw").GetInt32(), Is.EqualTo(2));
            var analysisSummary = root.GetProperty("analysisSummary");
            Assert.That(analysisSummary.GetProperty("hazardCount").GetInt32(), Is.EqualTo(2));
            Assert.That(analysisSummary.GetProperty("provenCount").GetInt32(), Is.EqualTo(2));
            Assert.That(analysisSummary.GetProperty("unknownCount").GetInt32(), Is.Zero);
            Assert.That(analysisSummary.GetProperty("status").GetString(), Is.EqualTo("ProvenOnly"));
            Assert.That(analysisSummary.GetProperty("hasUnprovenHazards").GetBoolean(), Is.False);
            Assert.That(analysisSummary.GetProperty("summary").GetString(), Does.Contain("2 proven"));
            Assert.That(root.GetProperty("hazards").GetArrayLength(), Is.EqualTo(1));
            Assert.That(root.GetProperty("truncation").GetProperty("hazards").GetBoolean(), Is.True);

            var hazard = root.GetProperty("hazards")[0];
            Assert.That(hazard.GetProperty("kind").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardKind.DirectThrow.ToString()));
            Assert.That(hazard.GetProperty("status").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
            Assert.That(hazard.GetProperty("statusReason").GetString(), Is.Not.Empty);
            Assert.That(hazard.GetProperty("exceptionType").GetString(), Does.Contain("Exception"));
            Assert.That(hazard.GetProperty("line").GetInt32(),
                Is.EqualTo(FindLine(source, "throw new System.InvalidOperationException")));
            Assert.That(hazard.GetProperty("nodeKind").GetString(), Is.EqualTo("ThrowStatement"));
            Assert.That(hazard.GetProperty("operationText").GetString(), Does.Contain("throw"));
            Assert.That(hazard.GetProperty("reachability").GetString(),
                Is.EqualTo(SymbolicReachability.Reachable.ToString()));
            Assert.That(hazard.GetProperty("pathConditionCount").GetInt32(), Is.GreaterThanOrEqualTo(0));
            Assert.That(hazard.GetProperty("pathConditions").GetArrayLength(),
                Is.LessThanOrEqualTo(hazard.GetProperty("pathConditionCount").GetInt32()));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazards_ExceptionTypeAndCategoryFiltersNarrowTextCompactAndFullJson()
    {
        var source = @"
public class TestClass
{
    public void First()
    {
        throw new System.InvalidOperationException(""one"");
    }

    public void Second()
    {
        throw new System.ArgumentException(""two"");
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliRuntimeHazardTypeCategory-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var compactResult = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--hazard-exception-type",
                "system.argumentexception",
                "--hazard-category",
                "DIRECT_THROW",
                "--compact-json");

            Assert.That(compactResult.ExitCode, Is.EqualTo(0), compactResult.StandardError);
            using var compactDocument = JsonDocument.Parse(compactResult.StandardOutput);
            var compactRoot = compactDocument.RootElement;
            Assert.That(compactRoot.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(
                compactRoot.GetProperty("exceptionTypeCounts").GetProperty("System.ArgumentException").GetInt32(),
                Is.EqualTo(1));
            Assert.That(
                compactRoot.GetProperty("exceptionTypeCounts")
                    .TryGetProperty("System.InvalidOperationException", out _), Is.False);
            Assert.That(compactRoot.GetProperty("categoryCounts").GetProperty("direct_throw").GetInt32(),
                Is.EqualTo(1));
            Assert.That(compactRoot.GetProperty("analysisSummary").GetProperty("status").GetString(),
                Is.EqualTo("ProvenOnly"));
            Assert.That(compactRoot.GetProperty("analysisSummary").GetProperty("summary").GetString(),
                Does.Contain("1 proven"));
            var compactHazard = compactRoot.GetProperty("hazards")[0];
            Assert.That(compactHazard.GetProperty("exceptionType").GetString(), Is.EqualTo("System.ArgumentException"));
            Assert.That(compactHazard.GetProperty("category").GetString(), Is.EqualTo("direct_throw"));

            var fullJsonResult = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--hazard-exception-type",
                "System.ArgumentException",
                "--hazard-category",
                "direct_throw",
                "--json");

            Assert.That(fullJsonResult.ExitCode, Is.EqualTo(0), fullJsonResult.StandardError);
            using var fullJsonDocument = JsonDocument.Parse(fullJsonResult.StandardOutput);
            var fullJsonRoot = fullJsonDocument.RootElement;
            Assert.That(fullJsonRoot.GetProperty("HazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(fullJsonRoot.TryGetProperty("hazardCount", out _), Is.False);
            Assert.That(fullJsonRoot.GetProperty("Hazards")[0].GetProperty("ExceptionType").GetString(),
                Is.EqualTo("System.ArgumentException"));
            Assert.That(fullJsonRoot.GetProperty("Hazards")[0].GetProperty("Category").GetString(),
                Is.EqualTo("direct_throw"));

            var textResult = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--hazard-exception-type",
                "System.InvalidOperationException",
                "--hazard-category",
                "direct_throw");

            Assert.That(textResult.ExitCode, Is.EqualTo(0), textResult.StandardError);
            Assert.That(textResult.StandardOutput, Does.Contain("Runtime hazards: 1"));
            Assert.That(textResult.StandardOutput, Does.Contain("Hazard status summary: Proven=1"));
            Assert.That(textResult.StandardOutput,
                Does.Contain("Hazard exception summary: System.InvalidOperationException=1"));
            Assert.That(textResult.StandardOutput, Does.Contain("Hazard category summary: direct_throw=1"));
            Assert.That(textResult.StandardOutput, Does.Contain("Exception: System.InvalidOperationException"));
            Assert.That(textResult.StandardOutput, Does.Not.Contain("System.ArgumentException"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazards_HazardStatusFilterNarrowsOutput()
    {
        var source = @"
public class TestClass
{
    public int Unknown(int divisor)
    {
        return 10 / divisor;
    }

    public void Proven()
    {
        throw new System.InvalidOperationException(""proven"");
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliRuntimeHazardStatus-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var compactResult = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--include-unproven-hazards",
                "--hazard-status",
                "Unknown",
                "--compact-json");

            Assert.That(compactResult.ExitCode, Is.EqualTo(0), compactResult.StandardError);
            using var compactDocument = JsonDocument.Parse(compactResult.StandardOutput);
            var compactRoot = compactDocument.RootElement;
            Assert.That(compactRoot.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(compactRoot.GetProperty("statusCounts").GetProperty("Unknown").GetInt32(), Is.EqualTo(1));
            Assert.That(compactRoot.GetProperty("statusCounts").TryGetProperty("Proven", out _), Is.False);
            Assert.That(compactRoot.GetProperty("kindCounts").GetProperty("DivideByZero").GetInt32(), Is.EqualTo(1));
            var compactHazard = compactRoot.GetProperty("hazards")[0];
            Assert.That(compactHazard.GetProperty("status").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown.ToString()));
            Assert.That(compactHazard.GetProperty("kind").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardKind.DivideByZero.ToString()));
            Assert.That(compactHazard.GetProperty("operationText").GetString(), Does.Contain("/ divisor"));

            var fullJsonResult = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--include-unproven-hazards",
                "--hazard-status",
                "Unknown",
                "--json");

            Assert.That(fullJsonResult.ExitCode, Is.EqualTo(0), fullJsonResult.StandardError);
            using var fullJsonDocument = JsonDocument.Parse(fullJsonResult.StandardOutput);
            var fullJsonRoot = fullJsonDocument.RootElement;
            Assert.That(fullJsonRoot.GetProperty("HazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(fullJsonRoot.TryGetProperty("hazardCount", out _), Is.False);
            Assert.That(
                fullJsonRoot.GetProperty("Hazards")[0].GetProperty("Status").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown.ToString()));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazards_FailOnHazardReturnsOneAfterEmittingCompactJson()
    {
        var source = @"
public class TestClass
{
    public void TestMethod()
    {
        throw new System.InvalidOperationException(""boom"");
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliRuntimeHazardFail-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--fail-on-hazard",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("runtimeHazards"));
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("analysisSummary").GetProperty("provenCount").GetInt32(), Is.EqualTo(1));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazards_FailOnHazardReturnsZeroWhenFiltersRemoveAllHazards()
    {
        var source = @"
public class TestClass
{
    public void TestMethod()
    {
        throw new System.InvalidOperationException(""boom"");
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliRuntimeHazardFailFiltered-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--fail-on-hazard",
                "--hazard-exception-type",
                "System.ArgumentException",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("runtimeHazards"));
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("analysisSummary").GetProperty("hazardCount").GetInt32(), Is.Zero);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazards_FailOnHazardUsesFullFilteredCountWhenSummaryOnly()
    {
        var source = @"
public class TestClass
{
    public void TestMethod()
    {
        throw new System.InvalidOperationException(""boom"");
    }
}
";
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliRuntimeHazardFailSummary-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--fail-on-hazard",
                "--summary-only");

            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("hazards").GetArrayLength(), Is.Zero);
            Assert.That(root.GetProperty("truncation").GetProperty("hazards").GetBoolean(), Is.True);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RuntimeHazards_RejectsInvalidHazardStatusCombinations()
    {
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliInvalidHazardStatus-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, "public class C { public int M(int value) => value; }\n");
        try
        {
            var statusWithoutRuntimeHazards = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--hazard-status",
                "Unknown");
            Assert.That(statusWithoutRuntimeHazards.ExitCode, Is.EqualTo(64));
            Assert.That(statusWithoutRuntimeHazards.StandardError, Does.Contain("--hazard-status"));
            Assert.That(statusWithoutRuntimeHazards.StandardError, Does.Contain("--runtime-hazards"));

            var exceptionTypeWithoutRuntimeHazards = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--hazard-exception-type",
                "System.Exception");
            Assert.That(exceptionTypeWithoutRuntimeHazards.ExitCode, Is.EqualTo(64));
            Assert.That(exceptionTypeWithoutRuntimeHazards.StandardError, Does.Contain("--hazard-exception-type"));
            Assert.That(exceptionTypeWithoutRuntimeHazards.StandardError, Does.Contain("--runtime-hazards"));

            var categoryWithoutRuntimeHazards = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--hazard-category",
                "direct_throw");
            Assert.That(categoryWithoutRuntimeHazards.ExitCode, Is.EqualTo(64));
            Assert.That(categoryWithoutRuntimeHazards.StandardError, Does.Contain("--hazard-category"));
            Assert.That(categoryWithoutRuntimeHazards.StandardError, Does.Contain("--runtime-hazards"));

            var failOnHazardWithoutRuntimeHazards = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--fail-on-hazard");
            Assert.That(failOnHazardWithoutRuntimeHazards.ExitCode, Is.EqualTo(64));
            Assert.That(failOnHazardWithoutRuntimeHazards.StandardError, Does.Contain("--fail-on-hazard"));
            Assert.That(failOnHazardWithoutRuntimeHazards.StandardError, Does.Contain("--runtime-hazards"));

            var nonProvenStatusWithoutCandidates = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--runtime-hazards",
                "--all-lines",
                "--hazard-status",
                "Unknown");
            Assert.That(nonProvenStatusWithoutCandidates.ExitCode, Is.EqualTo(64));
            Assert.That(
                nonProvenStatusWithoutCandidates.StandardError,
                Does.Contain("--hazard-status values other than Proven require --include-unproven-hazards."));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_AnalysisLimit_EmitsTruncationEvidence()
    {
        const string source = """
                              public sealed class Sample
                              {
                                  public int Visit()
                                  {
                                      foreach (var value in new[] { 1, 2 })
                                      {
                                          return value;
                                      }

                                      return 0;
                                  }
                              }
                              """;
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliAnalysisLimit-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, source);
        try
        {
            var result = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--all-lines",
                "--analysis-limit",
                "finite-foreach-element-facts=1",
                "--compact-json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var truncation = document.RootElement.GetProperty("analysisTruncation");
            Assert.That(truncation.GetProperty("isTruncated").GetBoolean(), Is.True);
            Assert.That(
                truncation.GetProperty("events").EnumerateArray()
                    .Select(static item => item.GetProperty("code").GetString()),
                Does.Contain("analysis_limit.foreach_element_facts"));

            var textResult = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--all-lines",
                "--analysis-limit",
                "finite-foreach-element-facts=1");
            Assert.That(textResult.ExitCode, Is.EqualTo(0), textResult.StandardError);
            Assert.That(textResult.StandardOutput, Does.Contain("Analysis limits hit:"));
            Assert.That(textResult.StandardOutput, Does.Contain("analysis_limit.foreach_element_facts"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RejectsInvalidCompactOptionCombinations()
    {
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliInvalidOptions-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, "public class C { public int M(int value) => value; }\n");
        try
        {
            var jsonAndCompact = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--json",
                "--compact-json");
            Assert.That(jsonAndCompact.ExitCode, Is.EqualTo(64));
            Assert.That(jsonAndCompact.StandardError + jsonAndCompact.StandardOutput,
                Does.Contain("--json cannot be combined with --compact-json."));

            var maxLinesWithoutCompact = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--max-lines",
                "1");
            Assert.That(maxLinesWithoutCompact.ExitCode, Is.EqualTo(64));
            Assert.That(maxLinesWithoutCompact.StandardError + maxLinesWithoutCompact.StandardOutput,
                Does.Contain("require --compact-json"));

            var negativeMaxPoints = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--compact-json",
                "--max-points",
                "-1");
            Assert.That(negativeMaxPoints.ExitCode, Is.EqualTo(64));
            Assert.That(negativeMaxPoints.StandardError + negativeMaxPoints.StandardOutput,
                Does.Contain("non-negative integer"));

            var lineExpressionsWithoutLineMode = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--line-expressions");
            Assert.That(lineExpressionsWithoutLineMode.ExitCode, Is.EqualTo(64));
            Assert.That(lineExpressionsWithoutLineMode.StandardError + lineExpressionsWithoutLineMode.StandardOutput,
                Does.Contain(
                    "--line-expressions requires --line-invariants, --span-start/--span-end, or --all-lines."));

            var postLineWithoutLineMode = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--post-line-invariants");
            Assert.That(postLineWithoutLineMode.ExitCode, Is.EqualTo(64));
            Assert.That(postLineWithoutLineMode.StandardError + postLineWithoutLineMode.StandardOutput,
                Does.Contain(
                    "--post-line-invariants requires --line-invariants, --span-start/--span-end, or --all-lines."));

            var maxHazardsWithoutRuntimeHazards = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--compact-json",
                "--max-hazards",
                "1");
            Assert.That(maxHazardsWithoutRuntimeHazards.ExitCode, Is.EqualTo(64));
            Assert.That(maxHazardsWithoutRuntimeHazards.StandardError + maxHazardsWithoutRuntimeHazards.StandardOutput,
                Does.Contain("--max-hazards requires --runtime-hazards."));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_RejectsNonPositiveLineAndColumn()
    {
        var sourcePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SymbolicCliInvalidLocation-" + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourcePath, "public class C { public int M(int value) => value; }\n");
        try
        {
            var zeroLine = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                "0");
            Assert.That(zeroLine.ExitCode, Is.EqualTo(64));
            Assert.That(zeroLine.StandardError, Does.Contain("requires a positive integer value"));

            var negativeColumn = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--line",
                "1",
                "--column",
                "-1");
            Assert.That(negativeColumn.ExitCode, Is.EqualTo(64));
            Assert.That(negativeColumn.StandardError, Does.Contain("positive"));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Test]
    public async Task SymbolicCli_ExitGatesRejectMissingModesAndPolicies()
    {
        const string source = "class C { }";
        var cases = new[]
        {
            new
            {
                Arguments = new[]
                {
                    "--source-text", source, "--line", "1", "--fail-on-unproven-implies"
                },
                Error = "requires at least one --implies"
            },
            new
            {
                Arguments = new[]
                {
                    "--source-text", source, "--line", "1", "--fail-on-capability-unknown"
                },
                Error = "require --capabilities"
            },
            new
            {
                Arguments = new[]
                {
                    "--source-text", source, "--line", "1", "--fail-on-complexity-exceeded", "Linear"
                },
                Error = "require --complexity"
            },
            new
            {
                Arguments = new[]
                {
                    "--source-text", source, "--line", "1", "--fail-on-compact-truncation"
                },
                Error = "require --compact-json or --invariant-json"
            },
            new
            {
                Arguments = new[]
                {
                    "--source-text", source, "--line", "1", "--capabilities", "--compact-json",
                    "--fail-on-compact-threshold", "hazards=0"
                },
                Error = "not supported for this query mode"
            },
            new
            {
                Arguments = new[]
                {
                    "--source-text", source, "--line", "1", "--capabilities",
                    "--allowed-capability", "Console"
                },
                Error = "requires --fail-on-capability-violation"
            }
        };

        foreach (var item in cases)
        {
            var result = await SymbolicCliTestHost.RunAsync(item.Arguments);
            Assert.That(result.ExitCode, Is.EqualTo(64), string.Join(" ", item.Arguments));
            Assert.That(result.StandardError + result.StandardOutput, Does.Contain(item.Error));
        }
    }

    private static int FindLine(string source, string text)
    {
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Contains(text, StringComparison.Ordinal))
                return index + 1;

        throw new InvalidOperationException("Text not found: " + text);
    }

    private static bool IsNonPositiveValueFact(string fact)
    {
        return fact is "!(value > 0)" or "value <= 0";
    }

    private static bool ContainsNonPositiveValueFact(string text)
    {
        return text.Contains("!(value > 0)", StringComparison.Ordinal) ||
               text.Contains("value <= 0", StringComparison.Ordinal);
    }

    private static int FindColumn(string source, string text)
    {
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var column = lines[index].IndexOf(text, StringComparison.Ordinal);
            if (column >= 0) return column + 1;
        }

        throw new InvalidOperationException("Text not found: " + text);
    }

    private static int FindPosition(string source, string text)
    {
        var position = source.IndexOf(text, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Text not found: " + text);

        return position;
    }

    private static SymbolicProgramPointResult CreateSyntheticProofPoint(
        string condition,
        SymbolicTruthValue truthValue)
    {
        return new SymbolicProgramPointResult(
            "Synthetic.cs",
            1,
            1,
            0,
            0,
            "ReturnStatement",
            Array.Empty<string>(),
            conditionProofs: new[]
            {
                new SymbolicConditionProofResult(condition, truthValue, "synthetic")
            });
    }

    private static int FindBlankLine(string source)
    {
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (string.IsNullOrWhiteSpace(lines[index]))
                return index + 1;

        throw new InvalidOperationException("Blank line not found.");
    }
}
