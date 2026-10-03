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
}
