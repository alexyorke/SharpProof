using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Schema;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
internal sealed class SymbolicSourceQueryLineTests
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
        var service = new SymbolicQueryExecutor();

        var invariants = service.Query(new SymbolicQueryContext(
            input,
            SharpProofTarget.AllLines(),
            options));
        Assert.That(invariants.ProgramPoints.Any(static point =>
            point.MethodName?.Contains("Identity", StringComparison.Ordinal) == true), Is.True);

        var hazards = service.QueryRuntimeHazards(new SymbolicQueryContext(
            input,
            SharpProofTarget.LineNumber(FindLine(source, "throw new InvalidOperationException")),
            options));
        Assert.That(hazards.Hazards, Has.Some.Property("Kind").EqualTo(SymbolicRuntimeHazardKind.DirectThrow));

        var capabilities = service.QueryCapabilities(new SymbolicQueryContext(
            input,
            SharpProofTarget.LineNumber(FindLine(source, "Console.WriteLine")),
            options));
        Assert.That(capabilities.CapabilityText, Does.Contain("Console"));

        var complexity = service.QueryComplexity(new SymbolicQueryContext(
            input,
            SharpProofTarget.LineNumber(FindLine(source, "for (var index")),
            options));
        Assert.That(complexity.MethodDisplayName, Does.Contain("Complexity"));
    }

    [Test]
    public void SymbolicQueryService_RoutesFileTextSyntaxTreeAndNodeQueries()
    {
        const string source = @"
internal class TestClass
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
        var service = new SymbolicQueryExecutor();
        var options = new SymbolicQueryOptions(AnalyzerTestHost.GetTrustedPlatformReferences());

        var textLine = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "TextInput.cs"),
            SharpProofTarget.LineNumber(FindLine(source, "if (value > 0)")),
            options));
        Assert.That(textLine.ScopeKind, Is.EqualTo("line"));
        Assert.That(textLine.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.Line));
        Assert.That(textLine.Scope.Line, Is.EqualTo(FindLine(source, "if (value > 0)")));
        Assert.That(textLine.ProgramPoints.Select(static point => point.NodeKind), Does.Contain("IfStatement"));

        var textPosition = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "PositionInput.cs"),
            SharpProofTarget.AtPosition(FindPosition(source, "return value;")),
            options));
        Assert.That(textPosition.ScopeKind, Is.EqualTo("point"));
        Assert.That(textPosition.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.Point));
        Assert.That(textPosition.Scope.Position, Is.EqualTo(FindPosition(source, "return value;")));
        Assert.That(textPosition.ProgramPoints.Single().NodeKind, Is.EqualTo("ReturnStatement"));

        var syntaxSpan = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
            SharpProofTarget.Span(
                FindPosition(source, "if (value > 0)"),
                FindPosition(source, "return 0;")),
            SymbolicQueryOptions.Default));
        Assert.That(syntaxSpan.ScopeKind, Is.EqualTo("span"));
        Assert.That(syntaxSpan.Scope.Kind, Is.EqualTo(SymbolicQueryScopeKind.Span));
        Assert.That(syntaxSpan.Scope.SpanStart, Is.EqualTo(FindPosition(source, "if (value > 0)")));
        Assert.That(syntaxSpan.ProgramPointCount, Is.GreaterThan(0));

        var syntaxAllLines = service.Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromSyntaxTree(syntaxTree, compilation),
            SharpProofTarget.AllLines()));
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
            SharpProofTarget.Node()));
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
                (SharpProofTarget.Point(FindLine(source, "return value;")), SymbolicQueryScopeKind.Point),
                (SharpProofTarget.AtPosition(FindPosition(source, "return value;")), SymbolicQueryScopeKind.Point),
                (SharpProofTarget.LineNumber(FindLine(source, "if (value > 0)")), SymbolicQueryScopeKind.Line),
                (SharpProofTarget.Span(
                    FindPosition(source, "if (value > 0)"),
                    FindPosition(source, "return 0;")), SymbolicQueryScopeKind.Span),
                (SharpProofTarget.LineSpan(
                    FindLine(source, "if (value > 0)"), 1,
                    FindLine(source, "return 0;"), 1), SymbolicQueryScopeKind.Span),
                (SharpProofTarget.AllLines(), SymbolicQueryScopeKind.File)
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
internal class TestClass
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
internal class TestClass
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
internal class TestClass
{
    public int TestMethod()
    {
        var divisor = 5;
        return 10 / divisor;
    }
}", "QuerySyntaxTreeLine_PriorAssignmentFactsFlowThroughSymbolicState", "PriorAssignmentStateFacts.cs", "return 10 / divisor;",
            StateFlowQueryMode.Line, "ReturnStatement", ["provenance=ir.path.prior-statement.assigned-value", "text=divisor == 5", "facts=divisor == 5"], [], null, null),
        new(@"
internal class TestClass
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

internal class TestClass
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
internal class TestClass
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

internal class TestClass
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

internal class TestClass
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
internal class TestClass
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
internal class TestClass
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
internal class TestClass
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
            point = new SymbolicQueryExecutor().Query(new SymbolicQueryContext(SymbolicSourceInput.FromNode(node, compilation.GetSemanticModel(tree)), SharpProofTarget.Node())).ProgramPoints.Single();
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
internal class TestClass
{
    public int TestMethod(int value)
    {
        return value / 0;
    }
}";
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var service = new SymbolicQueryExecutor();
        var options = new SymbolicQueryOptions(AnalyzerTestHost.GetTrustedPlatformReferences(), smtAnalysis);
        var hazardOptions = new SymbolicRuntimeHazardQueryOptions(
            kinds: new[] { SymbolicRuntimeHazardKind.DivideByZero });
        var targets = new[]
        {
            SharpProofTarget.Point(FindLine(source, "return value / 0;")),
            SharpProofTarget.LineNumber(FindLine(source, "return value / 0;")),
            SharpProofTarget.Span(
                FindPosition(source, "return value / 0;"),
                FindPosition(source, "return value / 0;") + "return value / 0;".Length),
            SharpProofTarget.AllLines()
        };

        void AssertHazard(SymbolicSourceInput input, SharpProofTarget target)
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
        var ex = Assert.Throws<ArgumentException>(() => new SymbolicQueryExecutor().QueryRuntimeHazards(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromText("class C { int M(int value) => value; }", "HazardInput.cs"),
                SharpProofTarget.AllLines(),
                SymbolicQueryOptions.Default)));

        Assert.That(ex!.Message, Does.Contain("Runtime hazard queries require SMT analysis."));
    }

    [Test]
    public void SymbolicQueryService_Prove_RequiresPointTarget()
    {
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var ex = Assert.Throws<ArgumentException>(() => new SymbolicQueryExecutor().Prove(
            new SymbolicQueryContext(
                SymbolicSourceInput.FromText("class C { int M(int value) => value; }", "ProofInput.cs"),
                SharpProofTarget.LineNumber(1),
                new SymbolicQueryOptions(
                    AnalyzerTestHost.GetTrustedPlatformReferences(),
                    smtAnalysis)),
            "value > 0"));

        Assert.That(ex!.Message, Does.Contain("Condition proof requests require a point target."));
    }

    [Test]
    public void SymbolicQueryApi_HidesLegacyOverloadServicesFromPublicSurface()
    {
        var assembly = typeof(SymbolicQueryExecutor).Assembly;
        Assert.That(typeof(SymbolicQueryExecutor).IsPublic, Is.False);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicQueryService"), Is.Null);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicSourceQueryService"), Is.Null);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicConditionProofDispatcher"), Is.Null);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicSourceQueryDispatcher"), Is.Null);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicRuntimeHazardQueryDispatcher"), Is.Null);
        Assert.That(assembly.GetType("SharpProof.Symbolic.ValidatedSymbolicQueryRequest"), Is.Null);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicRuntimeHazardQueryService")!.IsPublic, Is.False);
        Assert.That(assembly.GetType("SharpProof.Symbolic.SymbolicFileQuery"), Is.Null);
        Assert.That(typeof(SymbolicProgramPointResult).IsPublic, Is.False);
        Assert.That(typeof(SymbolicProgramPointResult).IsVisible, Is.False);
        Assert.That(typeof(SymbolicConditionProofResult).IsVisible, Is.False);
    }

    [Test]
    public void QuerySyntaxTreeLine_ReturnsEveryProgramPointOnLine()
    {
        const string source = @"
internal class TestClass
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
        var aggregateProof = GetProofSummaries(result).Single(proof => proof.Condition == "value > 0");
        Assert.That(aggregateProof.Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.Unknown));
        Assert.That(aggregateProof.Summary, Does.Contain("unresolved"));
        Assert.That(aggregateProof.DisplayKind, Is.Not.Empty);
        Assert.That(aggregateProof.Condition, Is.EqualTo("value > 0"));
        Assert.That(aggregateProof.Target, Is.EqualTo("value"));
        Assert.That(returnPoint.MergedInvariantText, Is.EqualTo("value > 0"));
        var summary = SymbolicInvariantFactSummary.Merge(result.ProgramPoints.Select(point => point.Facts));
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
        Assert.That(result.Metrics.ProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));
        Assert.That(
            result.Metrics.TotalPathConditionCount,
            Is.EqualTo(result.ProgramPoints.Sum(point => point.PathConditionCount)));
        Assert.That(
            result.Metrics.MaxPathConditionCount,
            Is.EqualTo(result.ProgramPoints.Max(point => point.PathConditionCount)));
        Assert.That(
            result.Metrics.ProofTotalCount,
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
        var returnMetrics = SymbolicQueryMetrics.FromProgramPoints(new[] { returnPoint });
        Assert.That(returnMetrics.ProofTotalCount, Is.EqualTo(returnPoint.ConditionProofs.Count));
        Assert.That(returnMetrics.ProofProvenTrueCount, Is.EqualTo(1));
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
internal class TestClass
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
internal class TestClass
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
internal class TestClass
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

        var invariantResult = CanonicalJson(SymbolicQueryResult.From(result));
        Assert.That(invariantResult.GetProperty("scopeKind").GetString(), Is.EqualTo("point"));
        Assert.That(invariantResult.GetProperty("filePath").GetString(), Is.EqualTo(result.FilePath));
        Assert.That(invariantResult.GetProperty("line").GetInt32(), Is.EqualTo(result.Line));
        Assert.That(invariantResult.GetProperty("column").GetInt32(), Is.EqualTo(result.Column));
        Assert.That(invariantResult.GetProperty("position").GetInt32(), Is.EqualTo(result.Position));
        var serializedPoint = invariantResult.GetProperty("programPoints")[0];
        Assert.That(serializedPoint.GetProperty("requestedLine").GetInt32(), Is.EqualTo(line));
        Assert.That(serializedPoint.GetProperty("requestedColumn").GetInt32(), Is.EqualTo(column));
        Assert.That(serializedPoint.GetProperty("requestedPosition").GetInt32(), Is.EqualTo(requestedPosition));
        Assert.That(serializedPoint.GetProperty("requestedPositionDistance").GetInt32(), Is.EqualTo(0));
        Assert.That(serializedPoint.GetProperty("containsRequestedPosition").GetBoolean(), Is.True);
        Assert.That(serializedPoint.GetProperty("nodeKind").GetString(), Is.EqualTo("AddExpression"));
        Assert.That(serializedPoint.GetProperty("programPointKind").GetString(), Is.EqualTo(SymbolicProgramPointKinds.Expression));
        Assert.That(serializedPoint.GetProperty("reachability").GetString(),
            Is.EqualTo(result.Reachability.ToString()));
        Assert.That(serializedPoint.GetProperty("reachabilityReason").GetString(), Is.EqualTo(result.ReachabilityReason));
        Assert.That(invariantResult.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public void QuerySyntaxTreeLinePoint_ExposesNearestFallbackWhenColumnMissesProgramPoint()
    {
        const string source = @"
internal class TestClass
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
internal class TestClass
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
internal class TestClass
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
        var result = new SymbolicQueryExecutor().Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromNode(assignment, semanticModel),
            SharpProofTarget.Node(),
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
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAtPosition(
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
internal class TestClass
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

        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAtPosition(
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
internal class TestClass
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
internal class TestClass
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

        var query = InvariantView(result);
        Assert.That(query.Text, Is.EqualTo(result.MergedInvariantText));
        Assert.That(query.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(query.MustFacts, Is.Empty);
        Assert.That(query.MaybeFacts, Has.Member("value > 0"));
        Assert.That(query.MaybeFacts.Any(IsNonPositiveValueFact), Is.True);
        Assert.That(query.MaybeFacts.Count, Is.InRange(2, 3));
        Assert.That(query.UnknownFacts, Is.EquivalentTo(new[] { "unknown(value)" }));
        Assert.That(query.HasMaybeFacts, Is.True);
        Assert.That(query.HasUnknowns, Is.True);
        Assert.That(query.HasUnresolvedAnalysis, Is.True);
        Assert.That(query.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Unresolved));
        Assert.That(query.Summary, Does.Contain("unresolved"));
        var targetSummary = query.TargetSummaries.Single();
        Assert.That(query.TargetSummaryCount, Is.EqualTo(1));
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
            query.TargetPathSummaries.Single(static summary => summary.Target == "value");
        Assert.That(query.TargetPathSummaryCount, Is.EqualTo(query.TargetPathSummaries.Count));
        Assert.That(targetPathSummary.PathConditionCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(targetPathSummary.SmtConditionCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(targetPathSummary.ProgramPointCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(targetPathSummary.ProofTotalCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(targetPathSummary.ProofUnknownCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(targetPathSummary.ReasonCode, Is.EqualTo("SP-SYM-TARGET-PROOF-UNKNOWN"));
        Assert.That(targetPathSummary.Conditions, Does.Contain("value > 0"));
        Assert.That(
            query.Diagnostics.Select(static diagnostic => diagnostic.Code),
            Is.EquivalentTo(new[] { "SP-SYM-MAYBE-FACTS", "SP-SYM-CONSERVATIVE-UNKNOWN", "SP-SYM-PROOF-UNKNOWN" }));
        Assert.That(query.Diagnostics.Sum(static diagnostic => diagnostic.Count), Is.GreaterThanOrEqualTo(3));
        Assert.That(query.CandidateProgramPointCount,
            Is.EqualTo(result.MergedPathFacts.CandidateProgramPointCount));
        Assert.That(query.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(321));
        Assert.That(query.SmtDiagnostics.MethodBudgetMs, Is.EqualTo(2345));
        Assert.That(query.SmtDiagnostics.MaxPathConditions, Is.EqualTo(17));
        Assert.That(query.SmtDiagnostics.MaxExpressionNodes, Is.EqualTo(99));
        var aggregateProof = GetProofSummaries(result).Single(static proof => proof.Condition == "value > 0");
        var aggregateInputs = result.ProgramPoints.SelectMany(static point => point.ConditionProofs)
            .Where(static proof => proof.Condition == "value > 0").ToArray();
        Assert.That(aggregateInputs, Has.Length.EqualTo(aggregateProof.TotalCount));
        Assert.That(aggregateInputs.Select(static proof => proof.TruthValue),
            Does.Contain(SymbolicTruthValue.ProvenTrue));

        var positiveReturn = result.ProgramPoints
            .Where(static point => point.NodeKind == "ReturnStatement")
            .Single(point => point.MergedInvariantText == "value > 0");
        var positiveQuery = InvariantView(positiveReturn);
        Assert.That(positiveQuery.MustFacts, Is.EquivalentTo(new[] { "value > 0" }));
        Assert.That(positiveQuery.MaybeFacts, Is.Empty);
        Assert.That(positiveQuery.UnknownFacts, Is.Empty);
        Assert.That(positiveQuery.HasUnresolvedAnalysis, Is.False);
        Assert.That(positiveQuery.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Exact));
        Assert.That(positiveQuery.Diagnostics, Is.Empty);
        Assert.That(positiveQuery.Metrics.ProofProvenTrueCount, Is.EqualTo(1));
        var positiveTargetSummary = positiveQuery.TargetSummaries.Single();
        Assert.That(positiveTargetSummary.Target, Is.EqualTo("value"));
        Assert.That(positiveTargetSummary.MustFacts, Is.EquivalentTo(new[] { "value > 0" }));
        Assert.That(positiveTargetSummary.MaybeFacts, Is.Empty);
        Assert.That(positiveTargetSummary.UnknownFacts, Is.Empty);
        Assert.That(positiveTargetSummary.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Exact));
        Assert.That(positiveTargetSummary.StatusReason, Is.EqualTo("target_exact"));
        Assert.That(positiveTargetSummary.ReasonCode, Is.EqualTo("SP-SYM-TARGET-EXACT"));
        Assert.That(positiveTargetSummary.Summary, Does.Contain("agree"));
        var positivePathSummary = positiveQuery.TargetPathSummaries.Single();
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
internal class TestClass
{
    public int TestMethod(int value)
    {
        if (value == 0) { return 0; } if (value == 1) { return 1; } if (value == 2) { return 2; } if (value == 3) { return 3; } if (value == 4) { return 4; } if (value == 5) { return 5; } if (value == 6) { return 6; } if (value == 7) { return 7; } if (value == 8) { return 8; }
        return 9;
    }
}";
        using var session = new SymbolicSourceQueryTestSession(source, "BoundedInvariantDiagnostics.cs");
        var result = session.AnalyzeLine("if (value == 0)");

        var maybeDiagnostic = InvariantView(result).Diagnostics
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
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeSpan(
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
        var query = InvariantView(result);
        Assert.That(query.MaybeFacts, Does.Contain("copy > 0"));
        Assert.That(
            query.MaybeFacts.Any(static fact => fact is "!(copy > 0)" or "copy <= 0"),
            Is.True);
        Assert.That(query.UnknownFacts, Does.Contain("unknown(copy)"));
        Assert.That(query.CandidateProgramPointCount, Is.EqualTo(result.ProgramPoints.Count));

        var guardedReturn = result.ProgramPoints
            .Where(static point => point.NodeKind == "ReturnStatement")
            .Single(point => point.Invariant.Conditions.Any(static condition => condition.Text == "copy > 0"));
        Assert.That(InvariantView(guardedReturn).MustFacts, Does.Contain("copy > 0"));
        Assert.That(guardedReturn.ConditionProofs.Single().TruthValue, Is.EqualTo(SymbolicTruthValue.ProvenTrue));
    }

    [Test]
    public void QuerySyntaxTreeLineSpan_ConvertsLineColumnsToSourceSpan()
    {
        const string source = @"
internal class TestClass
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
internal class TestClass
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
        Assert.That(result.Metrics.UnreachableCount, Is.GreaterThanOrEqualTo(1));

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
internal class TestClass
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
        Assert.That(filtered.Metrics.ProgramPointCount, Is.EqualTo(filtered.ProgramPoints.Count));
        Assert.That(filtered.Metrics.TotalPathConditionCount,
            Is.EqualTo(filtered.ProgramPoints.Single().PathConditionCount));
        Assert.That(filtered.Metrics.ProofTotalCount, Is.Zero);
    }

    [Test]
    public void QuerySourceLine_ReturnsEmptyProgramPointsForBlankLine()
    {
        const string source = @"
internal class TestClass
{

    public int TestMethod(int value) => value;
}";

        var result = new SymbolicQueryExecutor().QuerySourceLine(
            source,
            "BlankLineQuery.cs",
            FindBlankLine(source),
            AnalyzerTestHost.GetTrustedPlatformReferences());

        Assert.That(result.ProgramPoints, Is.Empty);
        var summary = SymbolicInvariantFactSummary.Merge(result.ProgramPoints.Select(point => point.Facts));
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
        Assert.That(result.Metrics.ProgramPointCount, Is.Zero);
        Assert.That(result.Metrics.TotalPathConditionCount, Is.Zero);
        Assert.That(result.Metrics.MaxPathConditionCount, Is.Zero);
        Assert.That(result.Metrics.ProofTotalCount, Is.Zero);
    }

    [Test]
    public void QuerySyntaxTreeAllLines_ReturnsFileLevelAggregateSummary()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAllLines(
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
        Assert.That(result.Metrics.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(
            result.Metrics.TotalPathConditionCount,
            Is.EqualTo(result.Lines.SelectMany(line => line.ProgramPoints).Sum(point => point.PathConditionCount)));
        Assert.That(
            result.Metrics.MaxPathConditionCount,
            Is.EqualTo(result.Lines.SelectMany(line => line.ProgramPoints).Max(point => point.PathConditionCount)));
        Assert.That(result.Metrics.ReachableCount, Is.EqualTo(result.ProgramPointCount));
        var proofSummary = GetProofSummaries(result).Single(summary => summary.Condition == "value > 0");
        Assert.That(proofSummary.ProvenTrueCount, Is.GreaterThan(0));
        Assert.That(
            proofSummary.ProvenTrueCount + proofSummary.ProvenFalseCount + proofSummary.UnreachableCount +
            proofSummary.UnknownCount,
            Is.EqualTo(result.ProgramPointCount));
        Assert.That(result.Metrics.ProofTotalCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(result.Metrics.ProofProvenTrueCount, Is.EqualTo(proofSummary.ProvenTrueCount));
    }

    [Test]
    public void SymbolicFileQueryResult_Filter_RecomputesAggregateSummary()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAllLines(
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
        Assert.That(filtered.Metrics.ReachableCount, Is.EqualTo(filtered.ProgramPointCount));
        Assert.That(filtered.ObservedFacts,
            Is.EquivalentTo(filtered.Lines.SelectMany(line => line.ProgramPoints).SelectMany(point => point.Facts)
                .Distinct()));
        Assert.That(filtered.ObservedInvariant.ConditionCount, Is.EqualTo(filtered.ObservedFactCount));
        Assert.That(filtered.ObservedInvariant.Conditions.Select(condition => condition.Text),
            Is.EquivalentTo(filtered.ObservedFacts));
        Assert.That(filtered.MergedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.ConservativeFactMerge));
        Assert.That(filtered.MergedInvariantText, Is.EqualTo(filtered.MergedPathFacts.MergedInvariantText));
        Assert.That(filtered.MergedPathFacts.ConservativeUnknowns, Does.Contain("unknown(value)"));
        Assert.That(filtered.Metrics.ProgramPointCount, Is.EqualTo(filtered.ProgramPointCount));
        Assert.That(
            filtered.Metrics.TotalPathConditionCount,
            Is.EqualTo(filtered.Lines.SelectMany(line => line.ProgramPoints).Sum(point => point.PathConditionCount)));
        Assert.That(filtered.Metrics.ReachableCount, Is.EqualTo(filtered.ProgramPointCount));
        Assert.That(GetProofSummaries(filtered).Single(summary => summary.Condition == "value > 0").ProvenTrueCount,
            Is.GreaterThan(0));
    }

    [Test]
    public void SymbolicSourceQueryFilter_CanFilterByMethodAndConditionMetadata()
    {
        const string source = @"
internal class TestClass
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

        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAllLines(syntaxTree, compilation);
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

        var serializedPoints = CanonicalJson(filtered).GetProperty("programPoints").EnumerateArray().ToArray();
        Assert.That(serializedPoints, Is.Not.Empty);
        Assert.That(serializedPoints.All(static point => point.GetProperty("methodName").GetString() == "First"), Is.True);
    }

    [Test]
    public void SymbolicSourceQueryFilter_CanFilterByLinePointKindMethodSubstringAndProofMetadata()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAllLines(
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
        Assert.That(GetProofSummaries(filtered).Single().TotalCount, Is.EqualTo(1));

        var serializedPoint = CanonicalJson(filtered).GetProperty("programPoints").EnumerateArray().Single();
        Assert.That(serializedPoint.GetProperty("programPointKind").GetString(),
            Is.EqualTo(SymbolicProgramPointKinds.Expression));
        Assert.That(serializedPoint.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(),
            Is.EqualTo(1));
    }

    [Test]
    public void SharpProofEvidenceSchema_DefinesExactV2Compatibility()
    {
        Assert.That(SharpProofEvidenceSchema.CurrentVersion, Is.EqualTo(2));
        Assert.That(SharpProofEvidenceSchema.CompatibilityPolicy, Is.EqualTo("exact-v2"));
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(0), Is.False);
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(SharpProofEvidenceSchema.CurrentVersion), Is.True);
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(-1), Is.False);
        Assert.That(SharpProofEvidenceSchema.IsReadCompatible(SharpProofEvidenceSchema.CurrentVersion + 1), Is.False);
    }

    [Test]
    public void SymbolicProgramPointResult_ToCompactResult_AppliesPointBoundsAndJsonShape()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAtPosition(
            syntaxTree,
            compilation,
            position,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });
        var query = SymbolicQueryResult.From(result);
        var invariant = InvariantView(query);
        var root = CanonicalJson(query);
        var serializedPoint = root.GetProperty("programPoints").EnumerateArray().Single();

        Assert.That(query.ScopeKind, Is.EqualTo("point"));
        Assert.That(query.FilePath, Is.EqualTo(result.FilePath));
        Assert.That(query.Line, Is.EqualTo(result.Line));
        Assert.That(query.Column, Is.EqualTo(result.Column));
        Assert.That(query.Position, Is.EqualTo(position));
        Assert.That(query.ProgramPoints, Has.Count.EqualTo(1));
        Assert.That(query.MergedPathFacts.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(query.Metrics.ProofTotalCount, Is.EqualTo(result.ConditionProofs.Count));
        Assert.That(query.Metrics.TotalPathConditionCount, Is.EqualTo(result.PathConditionCount));
        Assert.That(query.Metrics.MaxPathConditionCount, Is.EqualTo(result.PathConditionCount));
        Assert.That(query.Metrics.ReachableCount + query.Metrics.UnreachableCount +
            query.Metrics.ReachabilityUnknownCount, Is.EqualTo(1));
        Assert.That(query.Metrics.ReachableCount + query.Metrics.UnreachableCount, Is.EqualTo(1));
        Assert.That(invariant.StatusReason, Is.EqualTo("all_candidate_program_points_exact"));
        Assert.That(invariant.HasUnresolvedAnalysis, Is.False);
        Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("point"));
        Assert.That(root.TryGetProperty("ScopeKind", out _), Is.False);
        Assert.That(serializedPoint.GetProperty("nodeKind").GetString(), Is.EqualTo("ReturnStatement"));
        Assert.That(serializedPoint.GetProperty("programPointKind").GetString(),
            Is.EqualTo(SymbolicProgramPointKinds.Statement));
        Assert.That(serializedPoint.GetProperty("nodeSpanStart").GetInt32(), Is.EqualTo(result.NodeSpanStart));
        Assert.That(serializedPoint.GetProperty("nodeSpanEnd").GetInt32(), Is.EqualTo(result.NodeSpanEnd));
        Assert.That(serializedPoint.GetProperty("reachability").GetString(), Is.EqualTo(result.Reachability.ToString()));
        var symbolicFacts = serializedPoint.GetProperty("symbolicFacts");
        Assert.That(symbolicFacts.GetArrayLength(), Is.EqualTo(result.SymbolicFacts.Count));
        Assert.That(symbolicFacts[0].GetProperty("kind").GetString(), Is.EqualTo("SymbolicRelationAtom"));
        Assert.That(symbolicFacts[0].GetProperty("provenance").GetString(), Does.StartWith("ir."));
    }

    [Test]
    public void SymbolicLineQueryResult_ToCompactResult_SeparatesObservedRawFactsFromConservativeMerge()
    {
        const string source = @"
internal class TestClass
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
        var root = CanonicalJson(result);
        var serializedPoint = root.GetProperty("programPoints").EnumerateArray().First();
        var sourcePoint = result.ProgramPoints.First();

        Assert.That(result.ScopeKind, Is.EqualTo("line"));
        Assert.That(result.ProgramPoints, Is.Not.Empty);
        Assert.That(result.ObservedInvariant.MergeKind, Is.EqualTo(SymbolicInvariantMergeKind.DistinctFactUnion));
        Assert.That(result.ObservedInvariant.MergedInvariantText, Does.Contain("value > 0"));
        Assert.That(result.MergedPathFacts.ConservativeUnknowns, Does.Contain("unknown(value)"));
        var diagnostic = result.MergedPathFacts.ConservativeUnknownDiagnostics.Single();
        Assert.That(diagnostic.Target, Is.EqualTo("value"));
        Assert.That(diagnostic.UnknownText, Is.EqualTo("unknown(value)"));
        Assert.That(diagnostic.Reason, Is.EqualTo("not_common_to_all_candidate_program_points"));
        Assert.That(diagnostic.MaybeFacts, Is.Not.Empty);
        Assert.That(result.SmtDiagnostics.IsConfigured, Is.True);
        Assert.That(result.SmtDiagnostics.Mode, Is.EqualTo(SmtAnalysisMode.Bounded));
        Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("line"));
        Assert.That(serializedPoint.GetProperty("filePath").GetString(), Is.EqualTo(sourcePoint.FilePath));
        Assert.That(serializedPoint.GetProperty("line").GetInt32(), Is.EqualTo(sourcePoint.Line));
        Assert.That(serializedPoint.GetProperty("methodName").GetString(), Is.EqualTo(sourcePoint.MethodName));
        Assert.That(serializedPoint.GetProperty("programPointKind").GetString(), Is.EqualTo(sourcePoint.ProgramPointKind));
        Assert.That(serializedPoint.GetProperty("reachability").GetString(), Is.EqualTo(sourcePoint.Reachability.ToString()));
    }

    [Test]
    public void SymbolicFileQueryResult_ToCompactResult_AppliesOutputBounds()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAllLines(
            syntaxTree,
            compilation,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });
        var root = CanonicalJson(result);

        Assert.That(result.ScopeKind, Is.EqualTo("file"));
        Assert.That(result.FilePath, Does.EndWith("CompactFileQuery.cs"));
        Assert.That(result.LineCount, Is.EqualTo(source.Split('\n').Length));
        Assert.That(result.ProgramPoints, Is.Not.Empty);
        Assert.That(result.MergedPathFacts.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(result.Metrics.ProofTotalCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.ObservedInvariant.ConditionCount, Is.EqualTo(result.ObservedFactCount));
        Assert.That(result.SmtDiagnostics.IsConfigured, Is.True);
        Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("file"));
        Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo(result.FilePath));
        Assert.That(root.GetProperty("programPoints").GetArrayLength(), Is.EqualTo(result.ProgramPointCount));
        Assert.That(root.GetProperty("mergedPathFacts").GetProperty("maybeFacts").GetArrayLength(),
            Is.EqualTo(result.MergedPathFacts.MaybeFacts.Count));
    }

    [Test]
    public void SymbolicSpanQueryResult_ToCompactResult_ExposesInvariantQueryAndBudgetMetadata()
    {
        const string source = @"
internal class TestClass
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

        var result = new SymbolicQueryExecutor().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0" });
        var query = InvariantView(result);
        var root = CanonicalJson(result);

        Assert.That(result.ScopeKind, Is.EqualTo("span"));
        Assert.That(result.SpanStart, Is.EqualTo(spanStart));
        Assert.That(result.SpanEnd, Is.EqualTo(spanEnd));
        Assert.That(result.StartLine, Is.EqualTo(FindLine(source, "if (copy > 0)")));
        Assert.That(result.EndLine, Is.EqualTo(FindLine(source, "return 0;")));
        Assert.That(query.MaybeFacts, Is.Not.Empty);
        Assert.That(query.UnknownFacts, Does.Contain("unknown(copy)"));
        Assert.That(query.HasUnresolvedAnalysis, Is.True);
        var pathSummary = query.TargetPathSummaries.Single(static summary => summary.Target == "copy");
        Assert.That(pathSummary.PathConditionCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(pathSummary.ReasonCode, Is.Not.Empty);
        Assert.That(result.SmtDiagnostics.QueryTimeoutMs, Is.EqualTo(222));
        Assert.That(result.SmtDiagnostics.MethodBudgetMs, Is.EqualTo(2222));
        Assert.That(result.SmtDiagnostics.MaxPathConditions, Is.EqualTo(22));
        Assert.That(result.SmtDiagnostics.MaxExpressionNodes, Is.EqualTo(222));
        Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("span"));
        Assert.That(root.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
        Assert.That(root.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
        Assert.That(root.GetProperty("programPoints").GetArrayLength(), Is.EqualTo(result.ProgramPointCount));
    }

    [Test]
    public void SymbolicSpanQueryResult_ToInvariantQueryResult_EmitsBoundedQueryAnswer()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0", "copy <= 0" });
        var query = InvariantView(result);
        Assert.That(result.SymbolicFacts, Is.Not.Empty);
        Assert.That(result.InvariantInfo.MergedText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(result.InvariantInfo.MergeKind, Is.EqualTo(result.MergedInvariant.MergeKind));
        Assert.That(result.InvariantInfo.ConditionCount, Is.EqualTo(result.MergedInvariant.ConditionCount));
        Assert.That(result.InvariantInfo.Facts, Is.EquivalentTo(result.SymbolicFacts));
        Assert.That(result.InvariantInfo.Proofs.Select(static proof => proof.Backend),
            Does.Contain(SymbolicProofBackend.Smt));
        Assert.That(result.ScopeKind, Is.EqualTo("span"));
        Assert.That(result.FilePath, Does.EndWith("InvariantQueryProjection.cs"));
        Assert.That(result.SpanStart, Is.EqualTo(spanStart));
        Assert.That(result.SpanEnd, Is.EqualTo(spanEnd));
        Assert.That(result.Metrics.MaxPathConditionCount,
            Is.LessThanOrEqualTo(result.SmtDiagnostics.MaxPathConditions));
        Assert.That(query.HasUnresolvedAnalysis, Is.True);
        Assert.That(query.TargetPathSummaries.Select(static summary => summary.Target), Does.Contain("copy"));
        var target = query.TargetSummaries.Single(static summary => summary.Target == "copy");
        Assert.That(target.Status, Is.EqualTo(SymbolicInvariantQueryStatus.Conservative));
        Assert.That(target.StatusReason, Is.EqualTo("target_has_conservative_unknowns"));
        Assert.That(target.ReasonCode, Is.EqualTo("SP-SYM-TARGET-CONSERVATIVE-UNKNOWN"));
        Assert.That(target.UnknownFacts, Does.Contain("unknown(copy)"));
        var path = query.TargetPathSummaries.Single(static summary => summary.Target == "copy");
        Assert.That(path.PathConditionCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(path.SmtConditionCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(path.ProofTotalCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(path.ReasonCode, Is.Not.Empty);
        Assert.That(GetProofSummaries(result), Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(GetProofSummaries(result)
            .All(static proof => proof.Status != SymbolicConditionProofSummaryStatus.None), Is.True);
        Assert.That(result.SmtDiagnostics.IsConfigured, Is.True);
    }

    [Test]
    public void SymbolicSpanQueryResult_ToInvariantQueryResult_FiltersTargetSummaries()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0", "other < 10" });
        var query = InvariantView(result);
        Assert.That(
            query.TargetPathSummaries.Select(static summary => summary.Target),
            Does.Contain("copy"));
        Assert.That(
            query.TargetPathSummaries.Select(static summary => summary.Target),
            Does.Contain("other"));

        var filters = new[] { " copy ", "copy" };
        var targetPaths = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetPathSummaries, filters, static summary => summary.Target);
        var targetSummaries = SymbolicInvariantTargetFilter.ApplyToTargets(
            query.TargetSummaries, filters, static summary => summary.Target);
        var proofs = SymbolicInvariantTargetFilter.ApplyToTargets(
            GetProofSummaries(result), filters, static proof => proof.Target);
        Assert.That(query.TargetPathSummaries.Count, Is.GreaterThan(targetPaths.Count));
        Assert.That(targetPaths.Select(static summary => summary.Target), Is.EquivalentTo(new[] { "copy" }));
        Assert.That(targetSummaries.Select(static summary => summary.Target), Is.All.EqualTo("copy"));
        Assert.That(result.MergedInvariantText, Does.Contain("unknown(other)"));
        Assert.That(targetSummaries.SelectMany(static summary => summary.UnknownFacts), Does.Contain("unknown(copy)"));
        Assert.That(proofs.Select(static proof => proof.Target), Is.EquivalentTo(new[] { "copy" }));
    }

    [Test]
    public void SymbolicSpanQueryResult_ToCompactResult_FiltersPerPointTargetDetails()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeSpan(
            syntaxTree,
            compilation,
            spanStart,
            spanEnd,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "copy > 0", "other < 10" });
        Assert.That(
            GetProofSummaries(result).Select(static proof => proof.Target),
            Does.Contain("copy"));
        Assert.That(
            GetProofSummaries(result).Select(static proof => proof.Target),
            Does.Contain("other"));

        var filters = new[] { " copy " };
        var pointProofs = result.ProgramPoints
            .SelectMany(point => SymbolicInvariantTargetFilter.ApplyToProofResults(point.ConditionProofs, filters))
            .ToArray();
        Assert.That(pointProofs, Is.Not.Empty);
        Assert.That(pointProofs.Select(static proof => proof.Target), Is.All.EqualTo("copy"));
        Assert.That(pointProofs.Select(static proof => proof.Target), Does.Not.Contain("other"));

        var pointConditions = result.ProgramPoints
            .SelectMany(point => SymbolicInvariantTargetFilter.ApplyToConditions(point.Invariant.Conditions, filters))
            .ToArray();
        Assert.That(pointConditions.Select(static condition => condition.Target), Does.Contain("copy"));
        Assert.That(pointConditions.Select(static condition => condition.Target), Does.Not.Contain("other"));
        Assert.That(pointConditions.Select(static condition => condition.Text),
            Has.None.Matches<string>(fact => fact.Contains("other", StringComparison.Ordinal)));
    }

    [Test]
    public void SymbolicConditionProofAggregate_DescribesReachableProofOutcomes()
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

        var summaries = GetProofSummaries(SymbolicConditionProofProjection.FromProgramPoints(points))
            .ToDictionary(static summary => summary.Condition);

        Assert.That(summaries["always"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.AlwaysTrue));
        Assert.That(summaries["always"].ReachableCount, Is.EqualTo(1));
        Assert.That(summaries["always"].ResolvedCount, Is.EqualTo(2));
        Assert.That(summaries["always"].HoldsOnAllReachablePoints, Is.True);
        Assert.That(summaries["always"].Summary, Does.Contain("proven true"));

        Assert.That(summaries["never"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.AlwaysFalse));
        Assert.That(summaries["never"].ProvenFalseCount, Is.EqualTo(summaries["never"].ReachableCount));

        Assert.That(summaries["mixed"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.Mixed));
        Assert.That(summaries["mixed"].ProvenTrueCount, Is.GreaterThan(0));
        Assert.That(summaries["mixed"].ProvenFalseCount, Is.GreaterThan(0));

        Assert.That(summaries["unknown"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.Unknown));
        Assert.That(summaries["unknown"].ResolvedCount, Is.Zero);

        Assert.That(summaries["unreachable"].Status, Is.EqualTo(SymbolicConditionProofSummaryStatus.UnreachableOnly));
        Assert.That(summaries["unreachable"].ReachableCount, Is.Zero);
    }

    [Test]
    public void SymbolicFileQueryResult_ToCompactResult_SummaryOnlyOmitsNestedResults()
    {
        const string source = @"
internal class TestClass
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
        var result = new SymbolicQueryExecutor().QuerySyntaxTreeAllLines(
            syntaxTree,
            compilation,
            smtAnalysis: smtAnalysis,
            impliedConditions: new[] { "value > 0" });
        var root = CanonicalJson(result);

        Assert.That(result.ScopeKind, Is.EqualTo("file"));
        Assert.That(result.FilePath, Does.EndWith("CompactSummaryOnlyQuery.cs"));
        Assert.That(result.ProgramPoints, Is.Not.Empty);
        Assert.That(result.Metrics.ProgramPointCount, Is.EqualTo(result.ProgramPointCount));
        Assert.That(result.MergedPathFacts.MergedInvariantText, Is.EqualTo(result.MergedInvariantText));
        Assert.That(result.Metrics.ReachableCount + result.Metrics.UnreachableCount +
            result.Metrics.ReachabilityUnknownCount, Is.LessThanOrEqualTo(result.ProgramPointCount));
        Assert.That(result.SmtDiagnostics.IsConfigured, Is.True);
        Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("file"));
        Assert.That(root.TryGetProperty("lines", out _), Is.False);
        Assert.That(root.GetProperty("programPoints").GetArrayLength(), Is.EqualTo(result.ProgramPointCount));
        Assert.That(root.GetProperty("programPointSummary").GetProperty("programPointCount").GetInt32(),
            Is.EqualTo(result.ProgramPointCount));
    }

    [Test]
    public async Task SymbolicCli_CompactJson_EmitsPerPointMetadataWhenDetailsAreBounded()
    {
        var source = @"
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("line"));
            Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(root.GetProperty("line").GetInt32(), Is.EqualTo(FindLine(source, "return value;")));
            Assert.That(root.GetProperty("mergedPathFacts").GetProperty("mergedInvariantText").GetString(),
                Is.EqualTo("value > 0"));
            Assert.That(root.GetProperty("programPointSummary").GetProperty("proofOutcomes")
                .GetProperty("totalCount").GetInt32(), Is.EqualTo(1));

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
            Assert.That(point.GetProperty("conditionProofs").GetArrayLength(), Is.EqualTo(1));
            Assert.That(point.GetProperty("symbolicFacts").GetArrayLength(), Is.GreaterThan(0));
            Assert.That(point.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(point.GetProperty("proofOutcomes").GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
            Assert.That(point.GetProperty("invariant").GetProperty("mergedInvariantText").GetString(),
                Is.EqualTo("value > 0"));
            Assert.That(point.GetProperty("invariant").GetProperty("conservativeUnknownCount").GetInt32(), Is.Zero);
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
            "--json");

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
        var requestJson = SymbolicCliTestHost.CreateJsonRequest(
            "--source-text", source,
            "--source-file-name", "virtual/RequestSample.cs",
            "--source-map-uri", "editor://workspace/RequestSample.cs",
            "--source-map-original-line", "11",
            "--source-map-original-column", "3",
            "--line", FindLine(source, "if (value > 0)").ToString(),
            "--line-invariants",
            "--reference", typeof(object).Assembly.Location,
            "--language-version", "preview",
            "--define", "REQUEST",
            "--nullable", "enable",
            "--documentation-mode", "parse",
            "--platform", "AnyCpu",
            "--optimization", "Debug",
            "--assembly-name", "Request.Assembly",
            "--implies", "value > 0",
            "--smt-mode", "bounded",
            "--smt-timeout-ms", "337",
            "--smt-method-budget-ms", "2337",
            "--smt-max-path-conditions", "37",
            "--smt-max-expression-nodes", "337",
            "--smt-transient-retries", "1",
            "--smt-keep-context-on-transient-failure",
            "--smt-dispose-context-on-exit",
            "--analysis-limit", "merged-if-else-facts=13",
            "--check-reachability",
            "--line-expressions",
            "--post-line-invariants",
            "--json");

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("line"));
        Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo("virtual/RequestSample.cs"));
        Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
        Assert.That(root.GetProperty("programPointSummary").GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(),
            Is.GreaterThan(0));
        Assert.That(root.GetProperty("smtDiagnostics").GetProperty("queryTimeoutMs").GetInt32(),
            Is.EqualTo(337));
        Assert.That(root.GetProperty("smtDiagnostics").GetProperty("methodBudgetMs").GetInt32(),
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
        var requestJson = SymbolicCliTestHost.CreateJsonRequest(
            "explain",
            "--source-text", source,
            "--source-file-name", "virtual/RequestStdinSample.cs",
            "--source-map-uri", "editor://workspace/OriginalRequest.cs",
            "--source-map-original-line", "21",
            "--source-map-original-column", "5",
            "--line", FindLine(source, "Identity").ToString(),
            "--column", FindColumn(source, "Identity").ToString(),
            "--smt-mode", "disabled");

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
                                     "schemaVersion": 2,
                                     "arguments": ["--source-text", "class C { }", "--line", "1"],
                                     "argumentsTypo": []
                                   }
                                   """;

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.EqualTo(SymbolicErrorExitCodes.InvalidData));
        Assert.That(result.StandardError, Is.Empty);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("error").GetProperty("code").GetString(),
            Is.EqualTo(SymbolicErrorCodes.ParseFailed));
        Assert.That(document.RootElement.GetProperty("error").GetProperty("message").GetString(),
            Does.Contain("argumentsTypo"));
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
        var requestJson = SymbolicCliTestHost.CreateJsonRequest(
            "--source-text", source,
            "--source-file-name", "virtual/RequestGateSample.cs",
            "--line", FindLine(source, "if (value > 0)").ToString(),
            "--line-invariants",
            "--json",
            "--max-conservative-unknowns", "0",
            "--fail-on-threshold", "program-points=10");

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
        var modeOption = mode switch
        {
            "runtimeHazards" => "--runtime-hazards",
            "capabilities" => "--capabilities",
            _ => "--complexity"
        };
        var requestArguments = new List<string>
        {
            modeOption,
            "--source-text", source,
            "--source-file-name", "virtual/RequestModes.cs",
            "--line", FindLine(source, targetMarker).ToString(),
            "--smt-mode", "disabled",
            "--json"
        };
        var requestJson = SymbolicCliTestHost.CreateJsonRequest(requestArguments.ToArray());

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(expectedKind switch
        {
            "runtimeHazards" => document.RootElement.TryGetProperty("hazards", out _),
            "capabilities" => document.RootElement.TryGetProperty("capabilities", out _),
            _ => document.RootElement.TryGetProperty("complexity", out _)
        }, Is.True);
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
        var requestJson = SymbolicCliTestHost.CreateJsonRequest(
            "--runtime-hazards",
            "--source-text", source,
            "--source-file-name", "virtual/RequestHazards.cs",
            "--line", FindLine(source, "throw new InvalidOperationException").ToString(),
            "--hazard-status", "Proven",
            "--hazard-exception-type", "System.InvalidOperationException",
            "--hazard-category", "direct_throw",
            "--json");

        var result = await SymbolicCliTestHost.RunAsync("--request-json", requestJson);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.That(document.RootElement.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
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
                    "--request-json", "{\"schemaVersion\":1,\"arguments\":[\"--source-text\",\"class C { }\",\"--line\",\"1\"]}"
                },
                Error = "schemaVersion must be 2"
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
            "--json");
        Assert.That(unproven.ExitCode, Is.EqualTo(1));
        Assert.That(unproven.StandardError, Does.Contain("CI gate failed [unproven-implies]"));
        using var document = JsonDocument.Parse(unproven.StandardOutput);
        Assert.That(document.RootElement.GetProperty("scopeKind").GetString(), Is.EqualTo("line"));
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
            "--json"
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
            .Concat(new[] { "--fail-on-threshold", "conservative-unknowns=0" })
            .ToArray());
        Assert.That(thresholdFailure.ExitCode, Is.EqualTo(1));
        Assert.That(thresholdFailure.StandardError,
            Does.Contain("CI gate failed [threshold.conservative-unknowns]"));

        var truncationFailure = await SymbolicCliTestHost.RunAsync(commonArguments
            .Concat(new[] { "--fail-on-analysis-truncation" })
            .ToArray());
        Assert.That(truncationFailure.ExitCode, Is.Zero, truncationFailure.StandardError);
        using var document = JsonDocument.Parse(truncationFailure.StandardOutput);
        Assert.That(document.RootElement.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
        Assert.That(document.RootElement.GetProperty("programPoints").GetArrayLength(), Is.GreaterThan(0));
    }

    [Test]
    public async Task SymbolicCli_PostLineInvariants_ExposeCurrentAssignmentCompletionFact()
    {
        var source = @"
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var point = root.GetProperty("programPoints")[0];

            Assert.That(
                point.GetProperty("conditionProofs")[0].GetProperty("truthValue").GetString(),
                Is.EqualTo(SymbolicTruthValue.ProvenTrue.ToString()));
            Assert.That(
                point.GetProperty("invariant")
                    .GetProperty("conditions")
                    .EnumerateArray()
                    .Select(static condition => condition.GetProperty("target").GetString()),
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("file"));
            Assert.That(root.GetProperty("lineCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("mergedPathFacts").GetProperty("mergedInvariantText").GetString(), Is.Not.Empty);
            var analysisSummary = root.GetProperty("programPointSummary");
            Assert.That(
                analysisSummary.GetProperty("programPointCount").GetInt32(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
            Assert.That(analysisSummary.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(),
                Is.GreaterThan(0));
            Assert.That(root.GetProperty("smtDiagnostics").GetProperty("isConfigured").GetBoolean(), Is.True);
            Assert.That(root.TryGetProperty("lines", out _), Is.False);
            Assert.That(root.GetProperty("programPoints").GetArrayLength(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("span"));
            Assert.That(root.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(root.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.GreaterThanOrEqualTo(2));
            var mergedFacts = root.GetProperty("mergedPathFacts");
            Assert.That(mergedFacts.GetProperty("maybeFacts").EnumerateArray()
                .Select(static fact => fact.GetString()), Does.Contain("copy > 0"));
            Assert.That(mergedFacts.GetProperty("conservativeUnknowns").EnumerateArray()
                .Select(static fact => fact.GetString()), Does.Contain("unknown(copy)"));
            var smtDiagnostics = root.GetProperty("smtDiagnostics");
            Assert.That(smtDiagnostics.GetProperty("queryTimeoutMs").GetInt32(), Is.EqualTo(333));
            Assert.That(smtDiagnostics.GetProperty("methodBudgetMs").GetInt32(), Is.EqualTo(2333));
            Assert.That(smtDiagnostics.GetProperty("maxPathConditions").GetInt32(), Is.EqualTo(33));
            Assert.That(smtDiagnostics.GetProperty("maxExpressionNodes").GetInt32(), Is.EqualTo(333));
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("span"));
            Assert.That(root.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(root.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));
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
internal class TestClass
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
                "--invariant-target",
                "copy",
                "--invariant-target",
                "missing");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("target filter: copy, missing"));
            Assert.That(result.StandardOutput, Does.Contain("target filter matched: True"));
            Assert.That(result.StandardOutput, Does.Contain("matched target filters: copy"));
            Assert.That(result.StandardOutput, Does.Contain("unmatched target filters: missing"));
            Assert.That(result.StandardOutput, Does.Contain("copy"));
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
internal class TestClass
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("span"));
            Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(root.GetProperty("programPoints").GetArrayLength(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
            Assert.That(root.TryGetProperty("lines", out _), Is.False);
            Assert.That(root.GetProperty("spanStart").GetInt32(), Is.EqualTo(spanStart));
            Assert.That(root.GetProperty("spanEnd").GetInt32(), Is.EqualTo(spanEnd));

            var programPointSummary = root.GetProperty("programPointSummary");
            Assert.That(
                programPointSummary.GetProperty("programPointCount").GetInt32(),
                Is.EqualTo(root.GetProperty("programPointCount").GetInt32()));
            Assert.That(programPointSummary.GetProperty("totalPathConditionCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(programPointSummary.GetProperty("maxPathConditionCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(programPointSummary.GetProperty("proofOutcomes").GetProperty("totalCount").GetInt32(),
                Is.GreaterThan(0));
            Assert.That(root.GetProperty("smtDiagnostics").GetProperty("isEnabled").GetBoolean(), Is.True);

            var invariantQuery = root.GetProperty("mergedPathFacts");
            Assert.That(invariantQuery.GetProperty("maybeFacts").GetArrayLength(), Is.GreaterThanOrEqualTo(2));
            Assert.That(invariantQuery.GetProperty("conservativeUnknowns").EnumerateArray()
                .Select(static fact => fact.GetString()), Does.Contain("unknown(copy)"));
            Assert.That(root.GetProperty("conditionProofs").GetArrayLength(), Is.GreaterThanOrEqualTo(1));
            var proof = root.GetProperty("conditionProofs")[0];
            Assert.That(proof.GetProperty("condition").GetString(), Is.Not.Empty);
            Assert.That(proof.GetProperty("status").GetString(),
                Is.Not.EqualTo(SymbolicConditionProofSummaryStatus.None.ToString()));
            Assert.That(proof.GetProperty("summary").GetString(), Is.Not.Empty);
            Assert.That(proof.GetProperty("reasons").GetArrayLength(), Is.GreaterThan(0));

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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("line"));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("mergedPathFacts").GetProperty("mergedInvariantText").GetString(),
                Is.EqualTo("value > 0"));
            Assert.That(root.GetProperty("programPointSummary").GetProperty("proofOutcomes")
                .GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));

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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("file"));
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("programPointSummary").GetProperty("proofOutcomes")
                .GetProperty("provenTrueCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("conditionProofs")[0].GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(
                root.GetProperty("conditionProofs")[0].GetProperty("reasons")[0].GetProperty("truthValue").GetString(),
                Is.EqualTo(SymbolicTruthValue.ProvenTrue.ToString()));
            Assert.That(
                root.GetProperty("conditionProofs")[0].GetProperty("reasons")[0].GetProperty("reason").GetString(),
                Is.Not.Empty);

            var point = root.GetProperty("programPoints")[0];
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("scopeKind").GetString(), Is.EqualTo("file"));
            var points = root.GetProperty("programPoints").EnumerateArray().ToArray();
            Assert.That(points, Is.Not.Empty);
            Assert.That(root.GetProperty("programPointCount").GetInt32(), Is.EqualTo(points.Length));
            foreach (var point in points)
            {
                Assert.That(point.GetProperty("methodName").GetString(), Is.EqualTo("First"));
                Assert.That(
                    point.GetProperty("invariant").GetProperty("conditions").EnumerateArray()
                        .Select(static condition => condition.GetProperty("target").GetString()),
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
internal class TestClass
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
            var reachability = root.GetProperty("programPoints")[0].GetProperty("reachability");
            Assert.That(reachability.ValueKind, Is.EqualTo(JsonValueKind.String));
            Assert.That(
                Enum.TryParse<SymbolicReachability>(reachability.GetString(), out _),
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("filePath").GetString(), Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(root.GetProperty("lineCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(2));
            var hazards = root.GetProperty("hazards").EnumerateArray().ToArray();
            Assert.That(hazards, Has.Length.EqualTo(2));
            Assert.That(hazards.Select(static hazard => hazard.GetProperty("exceptionType").GetString()),
                Is.EquivalentTo(new[] { "System.InvalidOperationException", "System.ArgumentException" }));
            Assert.That(hazards.All(static hazard =>
                hazard.GetProperty("status").GetString() == SymbolicRuntimeHazardStatus.Proven.ToString()), Is.True);

            var hazard = hazards[0];
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
internal class TestClass
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
                "--json");

            Assert.That(compactResult.ExitCode, Is.EqualTo(0), compactResult.StandardError);
            using var compactDocument = JsonDocument.Parse(compactResult.StandardOutput);
            var compactRoot = compactDocument.RootElement;
            Assert.That(compactRoot.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
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
            Assert.That(fullJsonRoot.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(fullJsonRoot.GetProperty("hazards")[0].GetProperty("exceptionType").GetString(),
                Is.EqualTo("System.ArgumentException"));
            Assert.That(fullJsonRoot.GetProperty("hazards")[0].GetProperty("category").GetString(),
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
internal class TestClass
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
                "--json");

            Assert.That(compactResult.ExitCode, Is.EqualTo(0), compactResult.StandardError);
            using var compactDocument = JsonDocument.Parse(compactResult.StandardOutput);
            var compactRoot = compactDocument.RootElement;
            Assert.That(compactRoot.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
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
            Assert.That(fullJsonRoot.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(
                fullJsonRoot.GetProperty("hazards")[0].GetProperty("status").GetString(),
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("hazards")[0].GetProperty("status").GetString(),
                Is.EqualTo(SymbolicRuntimeHazardStatus.Proven.ToString()));
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("hazards").GetArrayLength(), Is.Zero);
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
internal class TestClass
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
                "--json");

            Assert.That(result.ExitCode, Is.EqualTo(1), result.StandardError);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            Assert.That(root.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("hazards").GetArrayLength(), Is.EqualTo(1));
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
                "--json");

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
            var retiredCompactJson = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--compact-json");
            Assert.That(retiredCompactJson.ExitCode, Is.EqualTo(64));
            Assert.That(retiredCompactJson.StandardError + retiredCompactJson.StandardOutput,
                Does.Contain("Unknown option '--compact-json'."));

            var maxLinesWithoutCompact = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--max-lines",
                "1");
            Assert.That(maxLinesWithoutCompact.ExitCode, Is.EqualTo(64));
            Assert.That(maxLinesWithoutCompact.StandardError + maxLinesWithoutCompact.StandardOutput,
                Does.Contain("Unknown option '--max-lines'."));

            var negativeThreshold = await SymbolicCliTestHost.RunAsync(
                "--file",
                sourcePath,
                "--position",
                "0",
                "--fail-on-threshold",
                "program-points=-1");
            Assert.That(negativeThreshold.ExitCode, Is.EqualTo(64));
            Assert.That(negativeThreshold.StandardError + negativeThreshold.StandardOutput,
                Does.Contain("non-negative-integer"));

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
                "--json",
                "--max-hazards",
                "1");
            Assert.That(maxHazardsWithoutRuntimeHazards.ExitCode, Is.EqualTo(64));
            Assert.That(maxHazardsWithoutRuntimeHazards.StandardError + maxHazardsWithoutRuntimeHazards.StandardOutput,
                Does.Contain("--max-hazards"));
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
                    "--source-text", source, "--line", "1", "--fail-on-threshold", "not-a-metric=0"
                },
                Error = "unknown metric"
            },
            new
            {
                Arguments = new[]
                {
                    "--source-text", source, "--line", "1", "--capabilities", "--json",
                    "--fail-on-threshold", "hazards=0"
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

    private static SymbolicInvariantQueryView InvariantView(SymbolicQueryResult result) =>
        SymbolicInvariantQueryView.From(result);

    private static SymbolicInvariantQueryView InvariantView(SymbolicProgramPointResult point) =>
        SymbolicInvariantQueryView.From(point);

    private static JsonElement CanonicalJson(object value) =>
        JsonSerializer.SerializeToElement(value, value.GetType(), CanonicalJsonOptions);

    private static IReadOnlyList<ProofSummary> GetProofSummaries(SymbolicQueryResult result) =>
        GetProofSummaries(result.ConditionProofs);

    private static IReadOnlyList<ProofSummary> GetProofSummaries(
        IReadOnlyList<SymbolicConditionProofSummary> proofs) => proofs
        .Select(static proof => new ProofSummary(
            proof.Condition,
            proof.Target,
            proof.DisplayKind,
            proof.TotalCount,
            proof.UnknownCount,
            proof.ProvenTrueCount,
            proof.ProvenFalseCount,
            proof.UnreachableCount,
            proof.ReachableCount,
            proof.ResolvedCount,
            proof.Status,
            proof.Summary,
            proof.HoldsOnAllReachablePoints))
        .ToArray();

    private sealed record ProofSummary(
        string Condition,
        string Target,
        string DisplayKind,
        int TotalCount,
        int UnknownCount,
        int ProvenTrueCount,
        int ProvenFalseCount,
        int UnreachableCount,
        int ReachableCount,
        int ResolvedCount,
        SymbolicConditionProofSummaryStatus Status,
        string Summary,
        bool HoldsOnAllReachablePoints);

    private static JsonSerializerOptions CanonicalJsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

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
