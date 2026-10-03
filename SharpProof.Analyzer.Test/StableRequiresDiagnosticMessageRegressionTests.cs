using System.Globalization;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class StableRequiresDiagnosticMessageRegressionTests
{
    private static readonly int[] RequiresOrdinals = [0, 1];
    private static readonly int[] InvocationRoles = [0];
    private static readonly int[] IndexerRoles = [1, 2];
    [TestCase("0", "Refuted", "Proven", 1)]
    [TestCase("1", "Proven", "Proven", 0)]
    [TestCase("Unknown()", "Unknown", "Unknown", 0)]
    public async Task ClauseObservationsPreserveIdentityAndPublishedDiagnostics(string argument,
        string firstOutcome, string secondOutcome, int violations)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                private static int Need(int value) {
                    Contract.Requires(value > 0); Contract.Requires(value < 10); return value;
                }
                private static int Unknown() => -1;
                public static int Caller() => Need({{argument}});
            }
            """;
        var baseline = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", ["SP0027"]);
        var observer = new ClauseObserver();
        var sessions = new RecordingSessionFactory { RequiresObserver = observer };
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", ["SP0027"],
            new SharpProofAnalyzer(sessions));
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.ToString()),
            Is.EqualTo(baseline.Select(static diagnostic => diagnostic.ToString())));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == "SP0027"), Is.EqualTo(violations));
        var rows = observer.Clauses.Where(static row => row.Caller.Name == "Caller" &&
            row.ContractTarget.Name == "Need").OrderBy(static row => row.ClauseOrdinal).ToArray();
        Assert.That(rows, Has.Length.EqualTo(2));
        Assert.That(rows.Select(static row => row.ClauseOrdinal), Is.EqualTo(RequiresOrdinals));
        Assert.That(rows.Select(static row => row.Outcome.ToString()), Is.EqualTo(new[] { firstOutcome, secondOutcome }));
        Assert.That(rows.Select(static row => row.Clause.SourceOperation).Distinct().Count(), Is.EqualTo(2));
        Assert.That(rows.All(static row => row.Candidate.Syntax.ToString().StartsWith("Need(", StringComparison.Ordinal)), Is.True);
        Assert.That(rows.All(row => SymbolEqualityComparer.Default.Equals(row.Candidate.TargetMethod, row.ContractTarget)), Is.True);
        Assert.That(observer.Gaps.Any(static gap => gap.Caller.Name == "Caller" && gap.Candidate.TargetMethod.Name == "Need"), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClauseObservationsApplyIncompleteFlowDowngradePerClause(bool includeRefutedClause)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public class Base {
                public Base(int value) {
                    Contract.Requires(value > 0);
                    {{(includeRefutedClause ? "Contract.Requires(value < 0);" : "")}}
                }
            }
            public class Derived() : Base(1) { }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027", "SP0047"]);
        var constructor = compilation.GetTypeByMetadataName("Derived")!.InstanceConstructors.Single();
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(static syntax => syntax.Identifier.ValueText == "Derived");
        var observer = new ClauseObserver();
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None, requiresObserver: observer);
        var diagnostics = new List<Diagnostic>();
        var outcome = RequiresCallSiteAnalyzer.AnalyzePrimaryConstructorInitializer(constructor, declaration,
            compilation.GetSemanticModel(tree), session, diagnostics.Add, CancellationToken.None);
        Assert.That(outcome.ToString(), Is.EqualTo(includeRefutedClause ? "Refuted" : "Unknown"));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == "SP0027"), Is.EqualTo(includeRefutedClause ? 1 : 0));
        var rows = observer.Clauses.Where(static row => row.ContractTarget.ContainingType.Name == "Base")
            .OrderBy(static row => row.ClauseOrdinal).ToArray();
        Assert.That(rows, Has.Length.EqualTo(includeRefutedClause ? 2 : 1));
        Assert.That(rows[0].Outcome.ToString(), Is.EqualTo("Unknown"));
        if (includeRefutedClause)
        { Assert.That(rows[1].Outcome.ToString(), Is.EqualTo("Refuted")); }
    }

    [Test]
    public void PotentialRequiresWithoutBoundClausesProducesExplicitGap()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Need(int value) => value;
                public static int Caller() => Need(0);
            }
            [ContractFor(typeof(Subject))]
            public static class SubjectContracts {
                public static int Need(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """, ["SP0027"]);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Need").OfType<IMethodSymbol>().Single();
        var screen = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        Assert.That(screen.HasPotentialCallPreconditions(target), Is.True);
        var binding = screen.BindRequires(target);
        Assert.That(binding.IsSuccess, Is.True);
        Assert.That(binding.Contracts!.Clauses.Any(static clause => clause.Kind == SharpProof.Contracts.BoundContractKind.Requires), Is.False);
        var observer = new ClauseObserver();
        var diagnostics = new List<Diagnostic>();
        var outcome = AnalyzeObservedCaller(compilation, observer, diagnostics.Add, CancellationToken.None);
        Assert.That(outcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        Assert.That(observer.Clauses, Is.Empty);
        Assert.That(observer.OwnerGaps, Is.Empty);
        var gap = observer.Gaps.Single();
        Assert.That(gap.Caller.Name, Is.EqualTo("Caller"));
        Assert.That(gap.Candidate.TargetMethod.Name, Is.EqualTo("Need"));
        Assert.That(gap.Reason, Is.EqualTo("UnboundPotentialRequires"));
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void ExcludedExternalSourceRequiresProducesExplicitGap()
    {
        var external = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public static class ExternalGuard {
                public static int Need(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """, []).WithAssemblyName("ExternalSourceContractObservation");
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Subject { public static int Caller() => ExternalGuard.Need(0); }",
            ["SP0027"], [external.ToMetadataReference()]);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var observer = new ClauseObserver();
        var diagnostics = new List<Diagnostic>();
        var outcome = AnalyzeObservedCaller(compilation, observer, diagnostics.Add, CancellationToken.None);
        Assert.That(outcome, Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
        Assert.That(observer.Clauses, Is.Empty);
        Assert.That(observer.OwnerGaps, Is.Empty);
        var gap = observer.Gaps.Single();
        Assert.That(gap.Candidate.TargetMethod.Name, Is.EqualTo("Need"));
        Assert.That(gap.Reason, Is.EqualTo("ExternalSourceRequiresExcluded"));
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void CancellationFromDiagnosticStopsClauseObservationPublication()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static int Need(int value) {
                    Contract.Requires(value > 0);
                    Contract.Requires(value >= 0);
                    return value;
                }
                public static int Caller() => Need(0);
            }
            """, ["SP0027"]);
        var observer = new ClauseObserver();
        var diagnostics = new List<Diagnostic>();
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>((Action)(() => AnalyzeObservedCaller(
            compilation, observer, diagnostic =>
            {
                diagnostics.Add(diagnostic);
                cancellation.Cancel();
            }, cancellation.Token)));
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SP0027"));
        Assert.That(observer.Clauses, Is.Empty);
        Assert.That(observer.Gaps, Is.Empty);
        Assert.That(observer.OwnerGaps, Is.Empty);
    }

    private static AnalyzerSemanticOutcome AnalyzeObservedCaller(
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation,
        ClauseObserver observer, Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
    {
        var caller = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Caller").OfType<IMethodSymbol>().Single();
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static syntax => syntax.Identifier.ValueText == "Caller");
        var model = compilation.GetSemanticModel(tree);
        var graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(declaration, model, cancellationToken);
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, cancellationToken, requiresObserver: observer);
        return RequiresCallSiteAnalyzer.AnalyzeCallable(caller, declaration, model, session, reportDiagnostic,
            graph, model.GetOperation(declaration, cancellationToken), cancellationToken);
    }

    private sealed class ClauseObserver : IRequiresCallSiteObserver
    {
        internal readonly ConcurrentQueue<RequiresClauseObservation> Clauses = new();
        internal readonly ConcurrentQueue<RequiresCallObservationGap> Gaps = new();
        internal readonly ConcurrentQueue<RequiresOwnerObservationGap> OwnerGaps = new();

        public void ObserveClause(in RequiresClauseObservation observation)
        { Clauses.Enqueue(observation); }

        public void ObserveGap(in RequiresCallObservationGap gap)
        { Gaps.Enqueue(gap); }

        public void ObserveOwnerGap(in RequiresOwnerObservationGap gap)
        { OwnerGaps.Enqueue(gap); }
    }

    [Test]
    public void FailedCallDiscoveryProducesOwnerGapRatherThanSuccessfulEmptyObservations()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public interface ITarget { static abstract int Caller(int value); }", ["SP0027"]);
        var caller = compilation.GetTypeByMetadataName("ITarget")!.GetMembers("Caller").OfType<IMethodSymbol>().Single();
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var observer = new ClauseObserver();
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None, requiresObserver: observer);
        var diagnostics = new List<Diagnostic>();
        var outcome = RequiresCallSiteAnalyzer.AnalyzeCallable(caller, declaration, compilation.GetSemanticModel(tree),
            session, diagnostics.Add, null, null, CancellationToken.None);
        Assert.That(outcome.ToString(), Is.EqualTo("Unknown"));
        Assert.That(observer.Clauses, Is.Empty);
        Assert.That(observer.Gaps, Is.Empty);
        var gap = observer.OwnerGaps.Single();
        Assert.That(gap.Caller, Is.SameAs(caller));
        Assert.That(gap.Declaration, Is.SameAs(declaration));
        Assert.That(gap.Reason, Is.EqualTo("CallDiscoveryIncomplete"));
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task RequiresDiagnosticUsesStableSourceClauseText()
    {
        var plainDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(includeUnrelatedCallerContract: false, argument: 0),
            "contracts",
            ["SP0027"]);
        var shiftedDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(includeUnrelatedCallerContract: true, argument: 0),
            "contracts",
            ["SP0027"]);
        var passingDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(includeUnrelatedCallerContract: true, argument: 1),
            "contracts",
            ["SP0027"]);

        var plain = plainDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var shifted = shiftedDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var plainMessage = plain.GetMessage(CultureInfo.InvariantCulture);
        var shiftedMessage = shifted.GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plainMessage, Is.EqualTo(shiftedMessage));
            Assert.That(plainMessage, Does.Contain("value"));
            Assert.That(plainMessage, Does.Contain("value > 0"));
            Assert.That(plainMessage, Does.Not.Contain("#t"));
            Assert.That(
                passingDiagnostics,
                Has.None.Matches<Diagnostic>(static diagnostic =>
                    diagnostic.Id == "SP0027"));
        }
    }

    [Test]
    public async Task ClosedAttributeDiagnosticUsesStableAttributeAndParameterText()
    {
        var plainDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateAttributeSource(includeUnrelatedCallerContract: false, argument: 0),
            "contracts",
            ["SP0027"]);
        var shiftedDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateAttributeSource(includeUnrelatedCallerContract: true, argument: 0),
            "contracts",
            ["SP0027"]);
        var passingDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateAttributeSource(includeUnrelatedCallerContract: true, argument: 1),
            "contracts",
            ["SP0027"]);

        var plain = plainDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var shifted = shiftedDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var plainMessage = plain.GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plainMessage,
                Is.EqualTo(shifted.GetMessage(CultureInfo.InvariantCulture)));
            Assert.That(plainMessage, Does.Contain("[Positive] value"));
            Assert.That(plainMessage, Does.Not.Contain("#t"));
            Assert.That(
                passingDiagnostics,
                Has.None.Matches<Diagnostic>(static diagnostic =>
                    diagnostic.Id == "SP0027"));
        }
    }

    [Test]
    public async Task CompanionRequiresDiagnosticUsesCompanionSourceText()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public interface IService {
                int Find(int value);
            }

            [ContractFor(typeof(IService))]
            public static class ServiceContracts {
                public static int Find(IService receiver, int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }

            public sealed class Service : IService {
                public int Find(int value) => value;
            }

            public static class Caller {
                public static int Call(IService service) => service.Find(0);
            }
            """,
            "contracts",
            ["SP0027"]);

        var diagnostic = diagnostics.Single(static item => item.Id == "SP0027");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("value > 0"));
            Assert.That(message, Does.Not.Contain("#t"));
        }
    }

    [Test]
    public async Task OversizedSourceClauseUsesBoundedDiagnosticText()
    {
        var condition = string.Join(
            " && ",
            Enumerable.Repeat("value > 0", 100));
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            $$"""
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Positive(int value) {
                    Contract.Requires({{condition}});
                    return value;
                }

                public static int Caller() => Positive(0);
            }
            """,
            "contracts",
            ["SP0027"]);

        var message = diagnostics.Single(static item => item.Id == "SP0027")
            .GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("condition exceeds the display limit"));
            Assert.That(message.Length, Is.LessThan(128));
        }
    }

    private static string CreateSource(
        bool includeUnrelatedCallerContract,
        int argument)
    {
        var unrelatedContract = includeUnrelatedCallerContract
            ? "Contract.Requires(unrelated > 0);"
            : string.Empty;
        return $$"""
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Caller(int unrelated) {
                    {{unrelatedContract}}
                    return Positive({{argument}});
                }
            }
            """;
    }

    private static string CreateAttributeSource(
        bool includeUnrelatedCallerContract,
        int argument)
    {
        var unrelatedContract = includeUnrelatedCallerContract
            ? "Contract.Requires(unrelated > 0);"
            : string.Empty;
        return $$"""
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Positive([Positive] int value) => value;

                public static int Caller(int unrelated) {
                    {{unrelatedContract}}
                    return Positive({{argument}});
                }
            }
            """;
    }
    [TestCase("invocation")]
    [TestCase("property")]
    [TestCase("list")]
    [TestCase("using-two")]
    [TestCase("using-null")]
    public async Task ManagedCallRolesPreserveDiagnosticsAndExposeCoverage(string scenario)
    {
        var body = scenario switch
        {
            "invocation" => "_ = Need(1);",
            "property" => "P += 1;",
            "list" => "_ = new Items() is [1, 2];",
            "using-two" => "using (D first = new D(), second = new D()) { }",
            "using-null" => "D missing = null; using (D first = new D(), second = missing) { }",
            _ => throw new InvalidOperationException()
        };
        var source = $$"""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                static int Need(int value) { Contract.Requires(value > 0); return value; }
                static int P {
                    get { Contract.Requires(true); return 1; }
                    set { Contract.Requires(true); }
                }
                public static void Caller() { {{body}} }
            }
            public class D : IDisposable {
                public void Dispose() { Contract.Requires(true); }
            }
            public class Items {
                public int Length { get { Contract.Requires(true); return 2; } }
                public int this[int index] { get { Contract.Requires(index >= 0); return index + 1; } }
            }
            """;
        var baseline = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", ["SP0027"]);
        var observer = new ClauseObserver();
        var sessions = new RecordingSessionFactory { RequiresObserver = observer };
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", ["SP0027"], new SharpProofAnalyzer(sessions));
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.ToString()),
            Is.EqualTo(baseline.Select(static diagnostic => diagnostic.ToString())));
        var rows = observer.Clauses.Where(static row => row.Caller.Name == "Caller").ToArray();
        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.All(static row => row.Candidate.CallRoleIndex >= 0 && row.Candidate.OriginKind != null), Is.True);
        var roles = rows.Select(static row => row.Candidate.CallRoleIndex).Distinct().Order().ToArray();
        if (scenario == "invocation")
        {
            Assert.That(roles, Is.EqualTo(InvocationRoles));
            Assert.That(rows.All(static row => !row.Candidate.MergedRoleCoverage), Is.True);
        }
        if (scenario == "property")
        {
            Assert.That(roles, Is.EqualTo(RequiresOrdinals));
        }
        if (scenario == "list")
        {
            var indices = rows.Where(static row => row.ContractTarget.Name == "get_Item")
                .Select(static row => row.Candidate.CallRoleIndex).Distinct().Order().ToArray();
            Assert.That(indices, Is.EqualTo(IndexerRoles));
        }
        if (scenario == "using-two")
        {
            Assert.That(rows.Count(static row => row.ContractTarget.Name == "Dispose"), Is.EqualTo(1));
            Assert.That(rows.Single(static row => row.ContractTarget.Name == "Dispose").Candidate.MergedRoleCoverage, Is.True);
            Assert.That(observer.Gaps.Any(static gap => gap.Caller.Name == "Caller" && gap.Reason == "MergedCallRoleCoverage"), Is.True);
        }
        if (scenario == "using-null")
        {
            var dispose = rows.Where(static row => row.ContractTarget.Name == "Dispose").ToArray();
            Assert.That(dispose, Has.Length.EqualTo(1));
            Assert.That(dispose[0].Candidate.CallRoleIndex == 1 || dispose[0].Candidate.MergedRoleCoverage, Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PrimaryBaseCallRolesAreExplicit(bool explicitBase)
    {
        var baseParameters = explicitBase ? "int value" : "";
        var baseSyntax = explicitBase ? "Base(1)" : "Base";
        var source = $$"""
            using SharpProof.Attributes;
            public class Base { public Base({{baseParameters}}) { Contract.Requires(true); } }
            public class Derived() : {{baseSyntax}} { }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027", "SP0047"]);
        var constructor = compilation.GetTypeByMetadataName("Derived")!.InstanceConstructors.Single();
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(static syntax => syntax.Identifier.ValueText == "Derived");
        var observer = new ClauseObserver();
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None, requiresObserver: observer);
        var diagnostics = new List<Diagnostic>();
        _ = RequiresCallSiteAnalyzer.AnalyzePrimaryConstructorInitializer(constructor, declaration,
            compilation.GetSemanticModel(tree), session, diagnostics.Add, CancellationToken.None);
        var row = observer.Clauses.Single(static row => row.ContractTarget.ContainingType.Name == "Base");
        Assert.That(row.Candidate.CallRoleIndex, Is.EqualTo(0));
        Assert.That(row.Candidate.OriginKind, Is.EqualTo(explicitBase ? PotentialRequiresCallOrigin.ExplicitPrimaryBaseConstructor :
            PotentialRequiresCallOrigin.ImplicitBaseConstructor));
    }


    [Test]
    public void PrimaryNestedListRolesReportLegacyMerging()
    {
        var source = """
            using SharpProof.Attributes;
            public class Items {
                public int Length { get { Contract.Requires(true); return 2; } }
                public int this[int index] { get { Contract.Requires(index >= 0); return index; } }
            }
            public class Base { public Base(int value) { Contract.Requires(true); } }
            public class Derived(Items items) : Base(items is [1, 2] ? 1 : 0) { }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027", "SP0047"]);
        var constructor = compilation.GetTypeByMetadataName("Derived")!.InstanceConstructors.Single();
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(static syntax => syntax.Identifier.ValueText == "Derived");
        var observer = new ClauseObserver();
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None, requiresObserver: observer);
        var diagnostics = new List<Diagnostic>();
        _ = RequiresCallSiteAnalyzer.AnalyzePrimaryConstructorInitializer(constructor, declaration,
            compilation.GetSemanticModel(tree), session, diagnostics.Add, CancellationToken.None);
        var row = observer.Clauses.Single(static row => row.ContractTarget.Name == "get_Item");
        Assert.That(row.Candidate.MergedRoleCoverage, Is.True);
        Assert.That(observer.Gaps.Any(static gap => gap.Candidate.TargetMethod.Name == "get_Item" &&
            gap.Reason == "MergedCallRoleCoverage"), Is.True);
    }


    [Test]
    public void UnavailableCallRoleProducesCoverageGap()
    {
        var source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Need(int value) { Contract.Requires(value > 0); return value; }
                public static int Caller() => Need(1);
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static syntax => syntax.Identifier.ValueText == "Caller");
        var caller = model.GetDeclaredSymbol(declaration)!;
        var invocation = (Microsoft.CodeAnalysis.Operations.IInvocationOperation)model.GetOperation(declaration.ExpressionBody!.Expression)!;
        var candidate = RequiresCallSiteDiscovery.CreateUnflowedCandidates(invocation, model).Single();
        Assert.That(candidate.OriginKind, Is.Null);
        Assert.That(candidate.CallRoleIndex, Is.EqualTo(-1));
        var observer = new ClauseObserver();
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None, requiresObserver: observer);
        var analysisType = typeof(RequiresCallSiteAnalyzer).GetNestedType("Analysis", System.Reflection.BindingFlags.NonPublic)!;
        var arguments = new object?[] { caller, declaration, model, session, (Action<Diagnostic>)(_ => { }), null, null, CancellationToken.None, null };
        var analysis = Activator.CreateInstance(analysisType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public, binder: null, args: arguments, culture: CultureInfo.InvariantCulture)!;
        var analyze = analysisType.GetMethod("AnalyzeCallSite", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        _ = analyze.Invoke(analysis, [candidate, true]);
        Assert.That(observer.Gaps.Single().Reason, Is.EqualTo("UnavailableCallRoleCoverage"));
        Assert.That(observer.Clauses.Single().Candidate.CallRoleIndex, Is.EqualTo(-1));
    }



    private readonly record struct RoleSite(int Tree, int Start, int Length);
    private readonly record struct RoleKey(string Owner, string DeclaredTarget, string ResolvedTarget,
        PotentialRequiresCallOrigin Origin, int Role, RoleSite Call, int ClauseOrdinal, RoleSite Clause);
    private sealed record RoleCensus(IMethodSymbol? Owner, SyntaxNode Declaration,
        RoleKey[] Expected, string[] Gaps);
    private sealed record RoleMatch(int Expected, int Matched, string[] Gaps)
    {
        internal bool Complete => Gaps.Length == 0 && Expected == Matched;
    }

    private static string ScopedMethodId(IMethodSymbol method)
    {
        return DocumentationCommentId.CreateDeclarationId(method.OriginalDefinition) ??
            method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static RoleSite RolePhysicalSite(Compilation compilation, SyntaxTree tree, Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        var ordinal = Array.IndexOf(compilation.SyntaxTrees.ToArray(), tree);
        if (ordinal < 0)
        {
            throw new InvalidOperationException("Foreign physical source tree.");
        }
        return new(ordinal, span.Start, span.Length);
    }

    private static RoleCensus CensusSourceRoles(Compilation compilation, SyntaxNode declaration)
    {
        if (compilation.GetDiagnostics().Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new(null, declaration, [], ["CompilationFailed"]);
        }
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var owner = model.GetDeclaredSymbol(declaration) as IMethodSymbol;
        if (owner == null)
        {
            return new(null, declaration, [], ["UnsupportedOwnerDeclaration"]);
        }
        var screen = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        var discovery = new RequiresCallSiteDiscovery(owner, declaration, model, CancellationToken.None);
        var calls = discovery.GetPotentialCalls(screen.HasPotentialCallPreconditions, out var complete);
        if (!calls.HasValue)
        {
            return new(owner, declaration, [], ["SourceDiscoveryUnavailable"]);
        }
        var expected = new List<RoleKey>();
        var gaps = new List<string>();
        if (!complete)
        {
            gaps.Add("SourceDiscoveryIncomplete");
        }
        foreach (var call in calls.Value)
        {
            if (!SymbolEqualityComparer.Default.Equals(call.Owner, owner))
            {
                gaps.Add("ForeignCallOwner");
                continue;
            }
            var binding = screen.BindRequires(call.Target);
            if (!binding.IsSuccess || binding.Contracts == null)
            {
                gaps.Add("IndependentClauseBindingFailed");
                continue;
            }
            var clauses = binding.Contracts.Clauses.Where(static clause => clause.Kind == SharpProof.Contracts.BoundContractKind.Requires).ToArray();
            for (var ordinal = 0; ordinal < clauses.Length; ordinal++)
            {
                var site = clauses[ordinal].SourceSyntax;
                if (site == null)
                {
                    gaps.Add("IndependentClauseSourceUnavailable");
                    continue;
                }
                expected.Add(new(ScopedMethodId(owner), ScopedMethodId(call.DeclaredTarget), ScopedMethodId(call.Target),
                    call.OriginKind, call.CallRoleIndex, RolePhysicalSite(compilation, call.Syntax.SyntaxTree, call.Syntax.Span), ordinal,
                    RolePhysicalSite(compilation, site.SyntaxTree, site.Span)));
            }
        }
        if (expected.Distinct().Count() != expected.Count)
        {
            gaps.Add("DuplicateIndependentRole");
        }
        return new(owner, declaration, expected.ToArray(), gaps.ToArray());
    }

    private static RoleMatch MatchObservedRoles(Compilation compilation, RoleCensus census,
        IEnumerable<RequiresClauseObservation> clauses, ClauseObserver observer)
    {
        var gaps = census.Gaps.ToList();
        var expected = census.Expected.ToHashSet();
        var matched = new HashSet<RoleKey>();
        var seen = new HashSet<RoleKey>();
        var screen = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        gaps.AddRange(observer.OwnerGaps.Where(gap => SymbolEqualityComparer.Default.Equals(gap.Caller, census.Owner))
            .Select(static gap => "ManagedOwnerGap:" + gap.Reason));
        gaps.AddRange(observer.Gaps.Where(gap => SymbolEqualityComparer.Default.Equals(gap.Caller, census.Owner) &&
                screen.HasPotentialCallPreconditions(gap.Candidate.ResolvedTargetMethod ?? gap.Candidate.TargetMethod))
            .Select(static gap => "ManagedCallGap:" + gap.Reason));
        foreach (var row in clauses.Where(row => SymbolEqualityComparer.Default.Equals(row.Caller, census.Owner)))
        {
            if (row.Candidate.MergedRoleCoverage || row.Candidate.OriginKind == null || row.Candidate.CallRoleIndex < 0)
            {
                gaps.Add("UnqualifiedManagedRole");
                continue;
            }
            var clause = row.Clause.SourceSyntax;
            if (clause == null)
            {
                gaps.Add("ManagedClauseSourceUnavailable");
                continue;
            }
            var key = new RoleKey(ScopedMethodId(row.Caller), ScopedMethodId(row.Candidate.TargetMethod),
                ScopedMethodId(row.ContractTarget), row.Candidate.OriginKind.Value, row.Candidate.CallRoleIndex,
                RolePhysicalSite(compilation, row.Candidate.Syntax.SyntaxTree, row.Candidate.Syntax.Span), row.ClauseOrdinal,
                RolePhysicalSite(compilation, clause.SyntaxTree, clause.Span));
            if (!seen.Add(key))
            {
                gaps.Add("DuplicateManagedRole");
            }
            else if (!expected.Contains(key))
            {
                gaps.Add("UnexpectedManagedRole");
            }
            else
            {
                matched.Add(key);
            }
        }
        foreach (var missing in expected.Except(matched))
        {
            gaps.Add("MissingManagedRole:" + missing.Role.ToString(CultureInfo.InvariantCulture));
        }
        return new(expected.Count, matched.Count, gaps.ToArray());
    }

    [TestCase("invocation", 1, true)]
    [TestCase("property", 2, true)]
    [TestCase("list", 3, true)]
    [TestCase("using-two", 2, false)]
    [TestCase("using-null", 2, false)]
    [TestCase("implicit-base", 1, true)]
    [TestCase("explicit-base", 1, true)]
    [TestCase("omitted", 1, false)]
    [TestCase("replaced", 1, false)]
    [TestCase("compilation-failure", 0, false)]
    [TestCase("discovery-failure", 0, false)]
    public void IndependentRoleCensusRejectsMissingCoverage(string scenario, int expectedCount, bool complete)
    {
        var body = scenario switch
        {
            "property" => "P += 1;",
            "list" => "_ = new Items() is [1, 2];",
            "using-two" => "using (D first = new D(), second = new D()) { }",
            "using-null" => "D missing = null; using (D first = new D(), second = missing) { }",
            "compilation-failure" => "Missing();",
            _ => "_ = Need(1);"
        };
        var source = $$"""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                static int Need(int value) { Contract.Requires(value > 0); return value; }
                static int P {
                    get { Contract.Requires(true); return 1; }
                    set { Contract.Requires(true); }
                }
                public static void Caller() { {{body}} }
            }
            public class D : IDisposable { public void Dispose() { Contract.Requires(true); } }
            public class Items {
                public int Length { get { Contract.Requires(true); return 2; } }
                public int this[int index] { get { Contract.Requires(index >= 0); return index + 1; } }
            }
            """;
        if (scenario == "implicit-base")
        {
            source = "using SharpProof.Attributes; public class Base { public Base() { Contract.Requires(true); } } public class Derived : Base { public Derived() { } }";
        }
        if (scenario == "explicit-base")
        {
            source = "using SharpProof.Attributes; public class Base { public Base(int value) { Contract.Requires(value > 0); } } public class Derived : Base { public Derived() : base(1) { } }";
        }
        if (scenario == "discovery-failure")
        {
            source = "public abstract class Subject { public abstract void Caller(); }";
        }
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027", "SP0047"]);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<BaseMethodDeclarationSyntax>().Single(static node =>
            node is MethodDeclarationSyntax method && method.Identifier.ValueText == "Caller" ||
            node is ConstructorDeclarationSyntax constructor && constructor.Identifier.ValueText == "Derived");
        var census = CensusSourceRoles(compilation, declaration);
        var observer = new ClauseObserver();
        if (census.Owner != null)
        {
            var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None, requiresObserver: observer);
            _ = RequiresCallSiteAnalyzer.AnalyzeCallable(census.Owner, declaration, compilation.GetSemanticModel(tree), session,
                static _ => { }, graph: null, operationRoot: null, CancellationToken.None);
        }
        var observations = observer.Clauses.ToArray();
        if (scenario == "omitted")
        {
            observations = [];
        }
        if (scenario == "replaced")
        {
            observations = observations.Select(static row => row with { Candidate = row.Candidate with { CallRoleIndex = 999 } }).ToArray();
        }
        var match = MatchObservedRoles(compilation, census, observations, observer);
        Assert.That(match.Expected, Is.EqualTo(expectedCount));
        Assert.That(match.Complete, Is.EqualTo(complete), string.Join(";", match.Gaps));
        if (scenario == "using-two")
        {
            Assert.That(match.Gaps.Any(static gap => gap == "ManagedCallGap:MergedCallRoleCoverage"), Is.True);
        }
        if (scenario == "omitted")
        {
            Assert.That(match.Gaps.Any(static gap => gap.StartsWith("MissingManagedRole:", StringComparison.Ordinal)), Is.True);
        }
        if (scenario == "replaced")
        {
            Assert.That(match.Gaps, Does.Contain("UnexpectedManagedRole"));
        }
        if (scenario == "compilation-failure")
        {
            Assert.That(match.Gaps, Does.Contain("CompilationFailed"));
        }
        if (scenario == "discovery-failure")
        {
            Assert.That(match.Gaps, Does.Contain("SourceDiscoveryUnavailable"));
            Assert.That(match.Gaps, Does.Contain("ManagedOwnerGap:CallDiscoveryIncomplete"));
        }
    }




    private sealed record OwnerAdapterBatch(RoleCensus[] Owners, string[] Gaps, string[] Exclusions);

    private static void AppendSourceClauseKeys(Compilation compilation, AnalyzerSession screen, IMethodSymbol owner,
        IMethodSymbol declaredTarget, IMethodSymbol resolvedTarget, SyntaxNode callSyntax, PotentialRequiresCallOrigin origin,
        int role, List<RoleKey> keys, List<string> gaps)
    {
        if (!screen.HasPotentialCallPreconditions(resolvedTarget))
        {
            return;
        }
        var binding = screen.BindRequires(resolvedTarget);
        if (!binding.IsSuccess || binding.Contracts == null)
        {
            gaps.Add("IndependentClauseBindingFailed");
            return;
        }
        var clauses = binding.Contracts.Clauses.Where(static clause => clause.Kind == SharpProof.Contracts.BoundContractKind.Requires).ToArray();
        for (var ordinal = 0; ordinal < clauses.Length; ordinal++)
        {
            var source = clauses[ordinal].SourceSyntax;
            if (source == null)
            {
                gaps.Add("IndependentClauseSourceUnavailable");
                continue;
            }
            keys.Add(new(ScopedMethodId(owner), ScopedMethodId(declaredTarget), ScopedMethodId(resolvedTarget),
                origin, role, RolePhysicalSite(compilation, callSyntax.SyntaxTree, callSyntax.Span), ordinal,
                RolePhysicalSite(compilation, source.SyntaxTree, source.Span)));
        }
    }

    private static void CensusOwnedForest(Compilation compilation, SemanticModel model, AnalyzerSession screen,
        IMethodSymbol owner, IOperation root, List<RoleKey> keys, List<string> gaps)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        var remaining = 65_536;
        while (pending.Count != 0)
        {
            if (--remaining < 0)
            {
                gaps.Add("OwnedForestBudget");
                return;
            }
            var operation = pending.Pop();
            if (operation is Microsoft.CodeAnalysis.Operations.IAnonymousFunctionOperation or Microsoft.CodeAnalysis.Operations.ILocalFunctionOperation)
            {
                gaps.Add("DeferredNestedOwnerExcluded");
                continue;
            }
            if (operation is Microsoft.CodeAnalysis.Operations.IInvalidOperation or Microsoft.CodeAnalysis.Operations.IDynamicInvocationOperation or
                Microsoft.CodeAnalysis.Operations.IFunctionPointerInvocationOperation or Microsoft.CodeAnalysis.Operations.IAwaitOperation)
            {
                gaps.Add("UnsupportedOwnedOperation");
            }
            foreach (var call in RequiresCallSiteDiscovery.CreateUnflowedCandidates(operation, model, collectCallRoles: true))
            {
                var target = RequiresCallSiteDispatch.ResolveExactTarget(call.TargetMethod, call.Instance, CancellationToken.None);
                AppendSourceClauseKeys(compilation, screen, owner, call.TargetMethod, target, call.Syntax,
                    PotentialRequiresCallOrigin.Operation, call.CallRoleIndex, keys, gaps);
            }
            foreach (var child in operation.ChildOperations.Reverse())
            {
                pending.Push(child);
            }
        }
    }

    private static RoleCensus CensusOwnedRegion(Compilation compilation, SemanticModel model,
        IMethodSymbol owner, SyntaxNode declaration)
    {
        var screen = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        var keys = new List<RoleKey>();
        var gaps = new List<string>();
        if (declaration is TypeDeclarationSyntax primary)
        {
            var initializer = primary.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().SingleOrDefault();
            IMethodSymbol? target;
            if (initializer == null)
            {
                target = RequiresCallSiteAnalyzer.TryGetImplicitBaseConstructor(owner);
            }
            else
            {
                var operation = model.GetOperation(initializer);
                if (operation is not Microsoft.CodeAnalysis.Operations.IInvocationOperation invocation)
                {
                    gaps.Add("PrimaryBaseOperationUnavailable");
                    return new(owner, declaration, [], gaps.ToArray());
                }
                target = invocation.TargetMethod;
                foreach (var argument in invocation.Arguments)
                {
                    CensusOwnedForest(compilation, model, screen, owner, argument.Value, keys, gaps);
                }
            }
            if (target == null)
            {
                gaps.Add("PrimaryBaseTargetUnavailable");
            }
            else
            {
                AppendSourceClauseKeys(compilation, screen, owner, target, target, initializer ?? (SyntaxNode)primary,
                    initializer == null ? PotentialRequiresCallOrigin.ImplicitBaseConstructor : PotentialRequiresCallOrigin.ExplicitPrimaryBaseConstructor,
                    0, keys, gaps);
            }
        }
        else
        {
            SyntaxNode operationSyntax = declaration switch
            {
                EqualsValueClauseSyntax initializer => initializer.Value,
                PropertyDeclarationSyntax { ExpressionBody: { } body } => body.Expression,
                IndexerDeclarationSyntax { ExpressionBody: { } body } => body.Expression,
                _ => declaration
            };
            var operation = model.GetOperation(operationSyntax);
            if (operation == null)
            {
                gaps.Add("OwnedSourceOperationUnavailable");
            }
            else
            {
                CensusOwnedForest(compilation, model, screen, owner, operation, keys, gaps);
            }
        }
        if (keys.Distinct().Count() != keys.Count)
        {
            gaps.Add("DuplicateIndependentRole");
        }
        return new(owner, declaration, keys.ToArray(), gaps.ToArray());
    }

    private static OwnerAdapterBatch EnumerateOwnerRegions(Compilation compilation, SyntaxNode declaration)
    {
        if (compilation.GetDiagnostics().Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new([], ["CompilationFailed"], []);
        }
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var owners = new List<RoleCensus>();
        var gaps = new List<string>();
        var exclusions = new List<string>();
        if (declaration is TypeDeclarationSyntax primary)
        {
            if (PrimaryConstructorCallableInventory.TryGet(primary, model, CancellationToken.None, out var constructor))
            {
                owners.Add(CensusOwnedRegion(compilation, model, constructor, primary));
            }
            else
            {
                gaps.Add("PrimaryOwnerUnavailable");
            }
        }
        else if (declaration is BasePropertyDeclarationSyntax property and not EventDeclarationSyntax)
        {
            if (model.GetDeclaredSymbol(property) is not IPropertySymbol symbol)
            {
                gaps.Add("AccessorOwnerUnavailable");
            }
            else if (property.AccessorList != null)
            {
                foreach (var accessor in property.AccessorList.Accessors)
                {
                    if (model.GetDeclaredSymbol(accessor) is IMethodSymbol method)
                    {
                        owners.Add(CensusOwnedRegion(compilation, model, method, accessor));
                    }
                    else
                    {
                        gaps.Add("AccessorOwnerUnavailable");
                    }
                }
            }
            else if (symbol.GetMethod != null)
            {
                owners.Add(CensusOwnedRegion(compilation, model, symbol.GetMethod, property));
            }
        }
        else if (declaration is EventDeclarationSyntax eventDeclaration)
        {
            foreach (var accessor in eventDeclaration.AccessorList!.Accessors)
            {
                if (model.GetDeclaredSymbol(accessor) is IMethodSymbol method)
                {
                    owners.Add(CensusOwnedRegion(compilation, model, method, accessor));
                }
                else
                {
                    gaps.Add("EventAccessorOwnerUnavailable");
                }
            }
        }
        else if (declaration is EqualsValueClauseSyntax initializer)
        {
            var member = initializer.Parent == null ? null : model.GetDeclaredSymbol(initializer.Parent);
            if (member is not IFieldSymbol and not IPropertySymbol || member.ContainingType.TypeKind != TypeKind.Class)
            {
                gaps.Add("UnsupportedInitializerOwner");
            }
            else
            {
                foreach (var constructor in member.IsStatic ? member.ContainingType.StaticConstructors : member.ContainingType.InstanceConstructors)
                {
                    if (constructor.ContainingType.TypeKind == TypeKind.Class && constructor.ContainingType.IsRecord && constructor.Parameters.Length == 1 &&
                        constructor.Parameters[0].RefKind == RefKind.None &&
                        SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, constructor.ContainingType))
                    {
                        exclusions.Add("RecordCopyInitializerOwner");
                        continue;
                    }
                    if (constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ConstructorDeclarationSyntax
                        { Initializer.ThisOrBaseKeyword.RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword }))
                    {
                        exclusions.Add("ThisDelegatingInitializerOwner");
                        continue;
                    }
                    owners.Add(CensusOwnedRegion(compilation, model, constructor, initializer));
                }
            }
        }
        else
        {
            gaps.Add("UnsupportedExecutableOwner");
        }
        return new(owners.ToArray(), gaps.ToArray(), exclusions.ToArray());
    }

    private static void ObserveOwnerRegion(Compilation compilation, RoleCensus owner, ClauseObserver observer)
    {
        var model = compilation.GetSemanticModel(owner.Declaration.SyntaxTree);
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None, requiresObserver: observer);
        if (owner.Declaration is TypeDeclarationSyntax primary)
        {
            _ = RequiresCallSiteAnalyzer.AnalyzePrimaryConstructorInitializer(owner.Owner!, primary, model, session, static _ => { }, CancellationToken.None);
        }
        else if (owner.Declaration is EqualsValueClauseSyntax initializer)
        {
            var operation = model.GetOperation(initializer.Value);
            if (operation != null)
            {
                _ = RequiresCallSiteAnalyzer.AnalyzeInitializerCall(owner.Owner!, initializer, operation, model, session, static _ => { }, CancellationToken.None);
            }
        }
        else
        {
            _ = RequiresCallSiteAnalyzer.AnalyzeCallable(owner.Owner!, owner.Declaration, model, session, static _ => { },
                graph: null, operationRoot: null, CancellationToken.None);
        }
    }

    [TestCase("primary-explicit", 1, true)]
    [TestCase("primary-implicit", 1, true)]
    [TestCase("primary-nested", 2, true)]
    [TestCase("primary-list-roles", 4, false)]
    [TestCase("primary-empty", 0, true)]
    [TestCase("primary-struct", 0, false)]
    [TestCase("accessor-pair", 2, true)]
    [TestCase("indexer-pair", 2, true)]
    [TestCase("abstract-accessor", 0, false)]
    [TestCase("property-initializer", 1, true)]
    [TestCase("static-initializer", 1, true)]
    [TestCase("primary-field-initializer", 1, true)]
    [TestCase("multiple-constructors", 2, true)]
    [TestCase("field-initializer", 1, true)]
    [TestCase("expression-property", 1, true)]
    public void ExtendedOwnerCensusQualifiesOwnedSourceRegions(string scenario, int expectedCount, bool complete)
    {
        var region = scenario switch
        {
            "primary-explicit" => "public class Derived() : Base(1) { }",
            "primary-implicit" => "public class Derived() : Base { }",
            "primary-nested" => "public class Derived() : Base(Guard.Need(1)) { }",
            "primary-list-roles" => "public class Derived(Items items) : Base(items is [1, 2] ? 1 : 0) { }",
            "primary-empty" => "public class Derived() { }",
            "primary-struct" => "public struct Derived() { }",
            "accessor-pair" => "public class Subject { public int P { get { return Guard.Need(1); } set { _ = Guard.Need(value); } } }",
            "indexer-pair" => "public class Subject { public int this[int index] { get { return Guard.Need(index); } set { _ = Guard.Need(value); } } }",
            "abstract-accessor" => "public abstract class Subject { public abstract int P { get; } }",
            "property-initializer" => "public class Subject { public int P { get; } = Guard.Need(1); }",
            "static-initializer" => "public class Subject { private static int value = Guard.Need(1); }",
            "primary-field-initializer" => "public class Subject() { private int value = Guard.Need(1); }",
            "multiple-constructors" => "public class Subject { private int value = Guard.Need(1); public Subject() { } public Subject(int marker) { } public Subject(bool marker) : this() { } }",
            "field-initializer" => "public class Subject { private int value = Guard.Need(1); }",
            _ => "public class Subject { public static int P => Guard.Need(1); }"
        };
        var source = $$"""
            using SharpProof.Attributes;
            public static class Guard { public static int Need(int value) { Contract.Requires(value > 0); return value; } }
            public class Base { public Base() { Contract.Requires(true); } public Base(int value) { Contract.Requires(value > 0); } }
            public class Items { public int Length { get { Contract.Requires(true); return 2; } } public int this[int index] { get { Contract.Requires(index >= 0); return index + 1; } } }
            {{region}}
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027", "SP0047"]);
        var root = compilation.SyntaxTrees.Single().GetRoot();
        SyntaxNode declaration = scenario switch
        {
            "primary-explicit" or "primary-implicit" or "primary-nested" or "primary-list-roles" or "primary-empty" or "primary-struct" => root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(static type => type.Identifier.ValueText == "Derived"),
            "field-initializer" or "property-initializer" or "static-initializer" or "primary-field-initializer" or "multiple-constructors" => root.DescendantNodes().OfType<EqualsValueClauseSyntax>().Single(),
            "indexer-pair" => root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(static type => type.Identifier.ValueText == "Subject").DescendantNodes().OfType<IndexerDeclarationSyntax>().Single(),
            _ => root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(static type => type.Identifier.ValueText == "Subject").DescendantNodes().OfType<PropertyDeclarationSyntax>().Single()
        };
        var batch = EnumerateOwnerRegions(compilation, declaration);
        Assert.That(batch.Gaps, Is.Empty);
        Assert.That(batch.Owners, Is.Not.Empty);
        Assert.That(batch.Owners.Select(static owner => ScopedMethodId(owner.Owner!)).Distinct().ToArray(), Has.Length.EqualTo(batch.Owners.Length));
        Assert.That(batch.Exclusions.Length, Is.EqualTo(scenario == "multiple-constructors" ? 1 : 0));
        var observer = new ClauseObserver();
        foreach (var owner in batch.Owners)
        {
            ObserveOwnerRegion(compilation, owner, observer);
        }
        var matches = batch.Owners.Select(owner => MatchObservedRoles(compilation, owner, observer.Clauses, observer)).ToArray();
        Assert.That(matches.Sum(static match => match.Expected), Is.EqualTo(expectedCount));
        Assert.That(matches.All(static match => match.Complete), Is.EqualTo(complete), string.Join(";", matches.SelectMany(static match => match.Gaps)));
        if (scenario == "primary-struct")
        {
            Assert.That(matches.SelectMany(static match => match.Gaps), Does.Contain("PrimaryBaseTargetUnavailable"));
        }
        else if (scenario == "abstract-accessor")
        {
            Assert.That(matches.SelectMany(static match => match.Gaps), Does.Contain("OwnedSourceOperationUnavailable"));
        }
        else if (scenario == "primary-list-roles")
        {
            Assert.That(matches.SelectMany(static match => match.Gaps), Does.Contain("UnqualifiedManagedRole"));
        }
    }


    [Test]
    public void ExtendedOwnerCensusRejectsCompilationFailureBeforeBinding()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("public class Derived() : Missing(Unknown()) { }", ["SP0027"]);
        var declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var batch = EnumerateOwnerRegions(compilation, declaration);
        Assert.That(batch.Owners, Is.Empty);
        Assert.That(batch.Gaps, Has.Length.EqualTo(1));
        Assert.That(batch.Gaps[0], Is.EqualTo("CompilationFailed"));
    }

    [Test]
    public void ExtendedOwnerCensusSeparatesPrimaryOwnerContexts()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("""
            using SharpProof.Attributes;
            public class Base { public Base(int value) { Contract.Requires(value > 0); } }
            public class First() : Base(1) { }
            public class Second() : Base(1) { }
            """, ["SP0027"]);
        var declarations = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(static declaration => declaration.ParameterList != null).ToArray();
        var owners = declarations.SelectMany(declaration => EnumerateOwnerRegions(compilation, declaration).Owners).ToArray();
        Assert.That(owners, Has.Length.EqualTo(2));
        Assert.That(owners.Select(static owner => ScopedMethodId(owner.Owner!)).Distinct().ToArray(), Has.Length.EqualTo(2));
        var observer = new ClauseObserver();
        foreach (var owner in owners)
        {
            ObserveOwnerRegion(compilation, owner, observer);
        }
        foreach (var owner in owners)
        {
            var match = MatchObservedRoles(compilation, owner, observer.Clauses, observer);
            Assert.That(match.Expected, Is.EqualTo(1));
            Assert.That(match.Complete, Is.True, string.Join(";", match.Gaps));
        }
    }


    [TestCase(true, false, false, 1, "")]
    [TestCase(true, true, false, 1, "")]
    [TestCase(false, true, false, 2, "")]
    [TestCase(true, true, true, 2, "")]
    [TestCase(true, true, false, 2, "ref")]
    [TestCase(true, true, false, 2, "in")]
    public async Task RecordCopyConstructorsDoNotObserveMemberInitializerCalls(bool isRecord, bool explicitCopy, bool isStruct, int expectedCalls, string refModifier)
    {
        ArgumentNullException.ThrowIfNull(refModifier);
        var explicitDeclaration = explicitCopy ? $"public Subject() {{ }} {(isStruct ? "public" : "protected")} Subject({refModifier} Subject source) {{ later = source.later; }}" : "";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Guard {
                public static int Calls;
                public static int Need(int value) { Contract.Requires(value > 0); Calls++; return value; }
            }
            public {{(isStruct ? "record struct" : isRecord ? "record" : "class")}} Subject {
                private int later = Guard.Need(1);
                {{explicitDeclaration}}
                public static Subject Copy(Subject source) => {{(isRecord && !isStruct && string.IsNullOrEmpty(refModifier) ? "source with { }" : $"new Subject({refModifier} source)")}};
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]).WithAssemblyName("RecordInitializer" + Guid.NewGuid().ToString("N"));
        var assembly = System.Reflection.Assembly.Load(AnalyzerTestHost.EmitImage(compilation));
        var type = assembly.GetType("Subject")!;
        var original = Activator.CreateInstance(type)!;
        _ = type.GetMethod("Copy")!.Invoke(null, [original]);
        Assert.That(assembly.GetType("Guard")!.GetField("Calls")!.GetValue(null), Is.EqualTo(expectedCalls));
        var observer = new ClauseObserver();
        var sessions = new RecordingSessionFactory { RequiresObserver = observer };
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", ["SP0027"], new SharpProofAnalyzer(sessions));
        Assert.That(diagnostics, Is.Empty);
        var rows = observer.Clauses.Where(static row => row.Caller.ContainingType.Name == "Subject" && row.ContractTarget.Name == "Need").ToArray();
        Assert.That(rows, Has.Length.EqualTo(expectedCalls));
        var root = await compilation.SyntaxTrees.Single().GetRootAsync();
        var initializer = root.DescendantNodes().OfType<EqualsValueClauseSyntax>().Single();
        var batch = EnumerateOwnerRegions(compilation, initializer);
        if (isStruct)
        {
            Assert.That(batch.Gaps, Does.Contain("UnsupportedInitializerOwner"));
        }
        else
        {
            Assert.That(batch.Owners, Has.Length.EqualTo(expectedCalls));
            Assert.That(batch.Exclusions.Length, Is.EqualTo(isRecord ? 1 : 0));
            Assert.That(batch.Gaps, Is.Empty);
        }
    }

    [Test]
    public void EventAccessorCensusSeparatesAddAndRemoveOwners()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("""
            using SharpProof.Attributes;
            public static class Guard { public static int Need(int value) { Contract.Requires(value > 0); return value; } }
            public class Subject { public event System.Action Changed { add { _ = Guard.Need(1); } remove { _ = Guard.Need(1); } } }
            """, ["SP0027"]);
        var declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<EventDeclarationSyntax>().Single();
        var batch = EnumerateOwnerRegions(compilation, declaration);
        Assert.That(batch.Gaps, Is.Empty);
        Assert.That(batch.Owners, Has.Length.EqualTo(2));
        Assert.That(batch.Owners.Select(static owner => ScopedMethodId(owner.Owner!)).Distinct().ToArray(), Has.Length.EqualTo(2));
        var observer = new ClauseObserver();
        foreach (var owner in batch.Owners)
        {
            ObserveOwnerRegion(compilation, owner, observer);
        }
        foreach (var owner in batch.Owners)
        {
            var match = MatchObservedRoles(compilation, owner, observer.Clauses, observer);
            Assert.That(match.Expected, Is.EqualTo(1));
            Assert.That(match.Complete, Is.True, string.Join(";", match.Gaps));
        }
    }

    [TestCase("ref")]
    [TestCase("in")]
    public async Task SameTypeByRefRecordConstructorRetainsImplicitBaseCall(string modifier)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public record Base {
                public static int Calls;
                public Base() { Contract.Requires(true); Calls++; }
            }
            public record Subject : Base {
                public Subject() { }
                protected Subject({{modifier}} Subject source) { }
                public static Subject Copy(Subject source) => new Subject({{modifier}} source);
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]).WithAssemblyName("RecordBase" + Guid.NewGuid().ToString("N"));
        var assembly = System.Reflection.Assembly.Load(AnalyzerTestHost.EmitImage(compilation));
        var type = assembly.GetType("Subject")!;
        var original = Activator.CreateInstance(type)!;
        _ = type.GetMethod("Copy")!.Invoke(null, [original]);
        Assert.That(assembly.GetType("Base")!.GetField("Calls")!.GetValue(null), Is.EqualTo(2));
        var observer = new ClauseObserver();
        var sessions = new RecordingSessionFactory { RequiresObserver = observer };
        _ = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", ["SP0027"], new SharpProofAnalyzer(sessions));
        var rows = observer.Clauses.Where(static row => row.Caller.ContainingType.Name == "Subject" &&
            row.ContractTarget.ContainingType.Name == "Base" && row.ContractTarget.Parameters.Length == 0).ToArray();
        Assert.That(rows, Has.Length.EqualTo(2));
        Assert.That(rows.Count(static row => row.Caller.Parameters.Length == 0), Is.EqualTo(1));
        Assert.That(rows.Count(static row => row.Caller.Parameters.Length == 1 && row.Caller.Parameters[0].RefKind != RefKind.None), Is.EqualTo(1));
    }
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task PriorThrowInitializerRetainsCompiledEvaluationOrder(bool partial, bool throwFirst)
    {
        const string guard = """
            using SharpProof.Attributes;
            public static class Guard {
                public static int Calls;
                public static int Need(int value) { Contract.Requires(value > 0); Calls++; return value; }
            }
            """;
        var first = "private int first = (new int[0])[0];";
        var later = "private int later = Guard.Need(0);";
        var compilation = AnalyzerTestHost.CreateCompilation(guard, ["SP0027"]).WithAssemblyName("InitializerOrder" + Guid.NewGuid().ToString("N"));
        var options = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)compilation.SyntaxTrees.Single().Options;
        if (partial)
        {
            var throwTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public partial class Subject { " + first + " }", options, "z-throw.cs");
            var callTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public partial class Subject { " + later + " }", options, "a-call.cs");
            compilation = compilation.AddSyntaxTrees(throwFirst ? [throwTree, callTree] : [callTree, throwTree]);
        }
        else
        {
            compilation = compilation.AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                "public class Subject { " + (throwFirst ? first + later : later + first) + " }", options, "initializers.cs"));
        }
        var assembly = System.Reflection.Assembly.Load(AnalyzerTestHost.EmitImage(compilation));
        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() => { _ = Activator.CreateInstance(assembly.GetType("Subject")!); }));
        Assert.That(thrown!.InnerException, Is.TypeOf<IndexOutOfRangeException>());
        Assert.That(assembly.GetType("Guard")!.GetField("Calls")!.GetValue(null), Is.EqualTo(throwFirst ? 0 : 1));
        var observer = new ClauseObserver();
        var sessions = new RecordingSessionFactory { RequiresObserver = observer };
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", new SharpProofAnalyzer(sessions));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == "SP0027"), Is.EqualTo(throwFirst ? 0 : 1));
        var rows = observer.Clauses.Where(static row => row.Caller.ContainingType.Name == "Subject" && row.ContractTarget.Name == "Need").ToArray();
        Assert.That(rows, Has.Length.EqualTo(throwFirst ? 0 : 1));
        Assert.That(observer.OwnerGaps.Where(static gap => gap.Caller.ContainingType.Name == "Subject"), Is.Empty);
    }

    [TestCase("null", 0)]
    [TestCase("null-side-effect-index", 1)]
    [TestCase("null-side-effect-value", 1)]
    [TestCase("bounds-side-effect-value", 1)]
    [TestCase("negative-index", 0)]
    [TestCase("in-bounds", 1)]
    [TestCase("unknown-array", 1)]
    [TestCase("unknown-index", 1)]
    [TestCase("parenthesized-call", 2)]
    public async Task ArrayFaultInitializersPreserveEvaluatedCallsAndUnknownCases(string scenario, int expectedCalls)
    {
        var first = scenario switch
        {
            "null" => "((int[])null)[0]",
            "null-side-effect-index" => "((int[])null)[Guard.Need(0)]",
            "null-side-effect-value" => "(((int[])null)[0] = Guard.Need(0))",
            "bounds-side-effect-value" => "((new int[0])[0] = Guard.Need(0))",
            "negative-index" => "(new int[1])[-1]",
            "in-bounds" => "(new int[1])[0]",
            "unknown-array" => "Guard.UnknownArray()[0]",
            "parenthesized-call" => "(Guard.Need(0))",
            _ => "(new int[1])[Guard.UnknownIndex()]"
        };
        var source = $$"""
            using SharpProof.Attributes;
            public static class Guard {
                public static int Calls;
                public static int Need(int value) { Contract.Requires(value > 0); Calls++; return value; }
                public static int[] UnknownArray() => new int[1];
                public static int UnknownIndex() => 0;
            }
            public class Subject { private int first = {{first}}; private int later = Guard.Need(0); }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]).WithAssemblyName("ArrayInitializer" + Guid.NewGuid().ToString("N"));
        var assembly = System.Reflection.Assembly.Load(AnalyzerTestHost.EmitImage(compilation));
        if (scenario is "null" or "null-side-effect-index" or "null-side-effect-value" or "bounds-side-effect-value" or "negative-index")
        {
            var thrown = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() => { _ = Activator.CreateInstance(assembly.GetType("Subject")!); }));
            Assert.That(thrown!.InnerException is NullReferenceException or IndexOutOfRangeException, Is.True);
        }
        else
        {
            _ = Activator.CreateInstance(assembly.GetType("Subject")!);
        }
        Assert.That(assembly.GetType("Guard")!.GetField("Calls")!.GetValue(null), Is.EqualTo(expectedCalls));
        var observer = new ClauseObserver();
        var sessions = new RecordingSessionFactory { RequiresObserver = observer };
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", new SharpProofAnalyzer(sessions));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == "SP0027"), Is.EqualTo(expectedCalls));
        var rows = observer.Clauses.Where(static row => row.Caller.ContainingType.Name == "Subject" && row.ContractTarget.Name == "Need").ToArray();
        Assert.That(rows, Has.Length.EqualTo(expectedCalls));
        if (scenario is "null-side-effect-value" or "bounds-side-effect-value")
        {
            var root = await compilation.SyntaxTrees.Single().GetRootAsync();
            var firstInitializer = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(static variable => variable.Identifier.ValueText == "first").Initializer!;
            ExpressionSyntax expression = firstInitializer.Value;
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }
            var operation = compilation.GetSemanticModel(firstInitializer.SyntaxTree).GetOperation(expression)!;
            var facts = new SharpProof.Effects.DefiniteOperationFacts(compilation, CancellationToken.None);
            Assert.That(facts.MayCompleteNormally(operation), Is.False, operation.Kind.ToString());
            Assert.That(rows[0].Candidate.Syntax.Span.Start, Is.LessThan(firstInitializer.Span.End));
        }
        if (scenario == "null-side-effect-index")
        {
            Assert.That(rows[0].Candidate.Syntax.Ancestors().OfType<ElementAccessExpressionSyntax>().Any(), Is.True);
        }
    }
    [TestCase("suppress", false, 0)]
    [TestCase("suppress", true, 0)]
    [TestCase("trusted", false, 2)]
    [TestCase("trusted", true, 2)]
    [TestCase("invalid-suppress", false, 2)]
    [TestCase("invalid-suppress", true, 2)]
    [TestCase("generated", false, 0)]
    [TestCase("generated", true, 0)]
    [TestCase("none", false, 2)]
    [TestCase("none", true, 2)]
    public async Task PropertyInitializerSuppressionPreservesConstructorPolicy(string control, bool partial, int expectedPropertyRows)
    {
        var attribute = control switch
        {
            "suppress" => "[SharpProofSuppress(\"reviewed property initializer\")]",
            "trusted" => "[SharpProofTrusted(\"reviewed property contract boundary\")]",
            "invalid-suppress" => "[SharpProofSuppress(\"\")]",
            "generated" => "[GeneratedCode(\"test\", \"1\")]",
            _ => ""
        };
        var fields = $$"""
            private int field = Guard.Need(0);
            {{attribute}}
            public int Property { get; } = Guard.Need(0);
            private int fault = (new int[0])[0];
            private int afterFault = Guard.Need(0);
            """;
        const string constructors = """
            public Subject() { }
            [SharpProofTrusted("reviewed ordinary constructor")]
            public Subject(int marker) { }
            [SharpProofSuppress("reviewed constructor")]
            public Subject(bool marker) { }
            public Subject(double marker) : this() { }
            """;
        const string generatedConstructor = """
            [GeneratedCode("test", "1")]
            public Subject(string marker) { }
            """;
        var source = $$"""
            using SharpProof.Attributes;
            using System.CodeDom.Compiler;
            public static class Guard {
                public static int Calls;
                public static int Need(int value) { Contract.Requires(value > 0); Calls++; return value; }
            }
            public partial class Subject { {{fields}} {{(partial ? "" : constructors + generatedConstructor)}} }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"], filePath: "z-fields.cs")
            .WithAssemblyName("PropertySuppression" + Guid.NewGuid().ToString("N"));
        if (partial)
        {
            var options = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)compilation.SyntaxTrees.Single().Options;
            compilation = compilation.AddSyntaxTrees(
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("using SharpProof.Attributes; public partial class Subject { " + constructors + " }", options, "a-constructors.cs"),
                Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("using System.CodeDom.Compiler; public partial class Subject { " + generatedConstructor + " }", options, "generated-constructors.g.cs"));
        }
        var assembly = System.Reflection.Assembly.Load(AnalyzerTestHost.EmitImage(compilation));
        var type = assembly.GetType("Subject")!;
        var firstThrown = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() => { _ = Activator.CreateInstance(type); }));
        Assert.That(firstThrown!.InnerException, Is.TypeOf<IndexOutOfRangeException>());
        foreach (var argument in new object[] { 1, true, "marker", 1.0 })
        {
            var thrown = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() => { _ = Activator.CreateInstance(type, argument); }));
            Assert.That(thrown!.InnerException, Is.TypeOf<IndexOutOfRangeException>());
        }
        Assert.That(assembly.GetType("Guard")!.GetField("Calls")!.GetValue(null), Is.EqualTo(10));
        var observer = new ClauseObserver();
        var sessions = new RecordingSessionFactory { RequiresObserver = observer };
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", new SharpProofAnalyzer(sessions));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == "SP0027"), Is.EqualTo(expectedPropertyRows == 0 ? 1 : 2));
        var rows = observer.Clauses.Where(static row => row.Caller.ContainingType.Name == "Subject" && row.ContractTarget.Name == "Need").ToArray();
        Assert.That(rows, Has.Length.EqualTo(2 + expectedPropertyRows));
        Assert.That(rows.Count(static row => row.Candidate.Syntax.Ancestors().OfType<PropertyDeclarationSyntax>().Any()), Is.EqualTo(expectedPropertyRows));
        Assert.That(rows.Any(static row => row.Caller.Parameters.Any(static parameter => parameter.Type.SpecialType is SpecialType.System_Boolean or SpecialType.System_String or SpecialType.System_Double)), Is.False);
        Assert.That(rows.Count(static row => row.Caller.Parameters.Length == 1 && row.Caller.Parameters[0].Type.SpecialType == SpecialType.System_Int32), Is.EqualTo(1 + expectedPropertyRows / 2));
    }
}
