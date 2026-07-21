using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SharpProofAnalysisSessionTests {
    [Test]
    public void CanonicalRequestReturnsRequestedFacets() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                static int M(int value) {
                    return value + 1;
                }
            }
            """);

        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: 3),
            SharpProofAnalysisFacet.Effects | SharpProofAnalysisFacet.ProofFacts |
            SharpProofAnalysisFacet.Complexity));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(SharpProofQueryStatus.Succeeded));
            Assert.That(result.MethodEffects, Is.Not.Null);
            Assert.That(result.ProofFacts, Is.Not.Empty);
            Assert.That(result.Complexity, Is.Not.Null);
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public void RuntimeHazardsUseTheCanonicalSessionAndRemainUnknownWhenUnproven() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                static int M(int value) => 10 / value;
            }
            """);

        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: 2),
            SharpProofAnalysisFacet.RuntimeHazards));

        Assert.That(result.Status, Is.EqualTo(SharpProofQueryStatus.Unknown));
        Assert.That(result.Hazards, Has.Some.Property(nameof(SharpProofHazard.Status)).EqualTo("Unknown"));
        Assert.That(result.UnknownReasons.Any(reason => reason.Code == "SP-SMT-REQUIRED"), Is.False);
    }

    [Test]
    public void ConditionProofExposesCompactCounterexampleWithoutPublicSolverModel() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                static int M(int value) {
                    return value;
                }
            }
            """);

        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: 3, Column: 13),
            SharpProofAnalysisFacet.ProofFacts,
            "value > 0"));

        var proof = result.ProofFacts.Single();
        Assert.Multiple(() => {
            Assert.That(proof.Status, Is.EqualTo("Unknown"));
            Assert.That(proof.SymbolicCondition, Does.Contain("value"));
            Assert.That(proof.Counterexample, Does.Contain("value="));
        });
    }

    [Test]
    public void RequestsAreSafeToReuseConcurrently() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                static int M(int value) => value + 1;
            }
            """);
        var request = new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: 2),
            SharpProofAnalysisFacet.Effects);

        var results = Enumerable.Range(0, 16)
            .AsParallel()
            .Select(_ => session.Analyze(request))
            .ToArray();

        Assert.That(results.Select(static result => result.MethodEffects).Distinct().Count(), Is.EqualTo(1));
    }
}
