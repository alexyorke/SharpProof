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
    public void ConditionProofInsideLocalFunctionResolvesCapturedParameters() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                static int M(string left, string right) {
                    int Local() => (left + right).Length;
                    return Local();
                }
            }
            """);
        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: 3, Column: 9),
            SharpProofAnalysisFacet.ProofFacts,
            "left == left"));
        var falseResult = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: 3, Column: 9),
            SharpProofAnalysisFacet.ProofFacts,
            "left != left"));
        Assert.Multiple(() => {
            Assert.That(result.ProofFacts.Single().Status, Is.EqualTo("ProvenTrue"));
            Assert.That(falseResult.ProofFacts.Single().Status, Is.EqualTo("Unknown"));
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
    [Test]
    public void InvalidSourceReturnsStructuredParseAndCompilationErrors() {
        using var syntaxSession = SharpProofAnalysisSession.FromText("class C { static int M( { }");
        var syntaxResult = syntaxSession.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.AllLines), SharpProofAnalysisFacet.Effects));
        using var semanticSession = SharpProofAnalysisSession.FromText("class C { static int M() => missing; }");
        var semanticResult = semanticSession.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.AllLines), SharpProofAnalysisFacet.Effects));
        Assert.Multiple(() => {
            Assert.That(syntaxResult.Status, Is.EqualTo(SharpProofQueryStatus.Failed));
            Assert.That(syntaxResult.Error?.Code, Is.EqualTo("SPQ1200"));
            Assert.That(semanticResult.Status, Is.EqualTo(SharpProofQueryStatus.Failed));
            Assert.That(semanticResult.Error?.Code, Is.EqualTo("SPQ1201"));
        });
    }
    [Test]
    public void QueryCompilationIncludesSharpProofAttributes() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                [SharpProof.Attributes.EnforcePure]
                static int M(int value) => value + 1;
            }
            """);
        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: 3), SharpProofAnalysisFacet.Effects));
        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(SharpProofQueryStatus.Succeeded));
            Assert.That(result.Error, Is.Null);
        });
    }
    [Test]
    public void MalformedRequestsNeverBecomeInternalFailures() {
        using var session = SharpProofAnalysisSession.FromText("class C { static int M() => 1; }");
        var requests = new[] {
            new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Point), SharpProofAnalysisFacet.Effects),
            new SharpProofAnalysisRequest(new SharpProofTarget((SharpProofTargetKind)128), SharpProofAnalysisFacet.Effects),
            new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Span), SharpProofAnalysisFacet.Effects),
            new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Span, SpanStart: 2, SpanEnd: 2),
                SharpProofAnalysisFacet.Effects),
            new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Line, Line: 1),
                (SharpProofAnalysisFacet)128),
            new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Position, Position: 1000),
                SharpProofAnalysisFacet.RuntimeHazards),
            new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Point, Line: 1, Column: 1),
                SharpProofAnalysisFacet.Effects, "true"),
            new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Line, Line: 1),
                SharpProofAnalysisFacet.ProofFacts, "true")
        };
        var results = requests.Select(request => session.Analyze(request)).ToArray();
        Assert.Multiple(() => {
            Assert.That(results, Has.All.Property(nameof(SharpProofAnalysisResult.Status)).EqualTo(SharpProofQueryStatus.Failed));
            Assert.That(results.Select(static result => result.Error?.Code), Has.None.EqualTo("SPQ9000"));
            Assert.That(results.Select(static result => result.Error?.Code),
                Has.All.Matches<string?>(code => code is "SPQ1000" or "SPQ1001"));
        });
    }
    [Test]
    public void MalformedConditionReturnsStructuredParseFailure() {
        using var session = SharpProofAnalysisSession.FromText("class C { static int M(int x) => x; }");
        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: 1, Column: 35),
            SharpProofAnalysisFacet.ProofFacts,
            "x +"));
        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(SharpProofQueryStatus.Failed));
            Assert.That(result.Error?.Code, Is.EqualTo("SPQ1200"));
        });
    }
    [Test]
    public void PointAndPositionHazardsHonorTheExactColumn() {
        const string source = """
            class C {
                static int M(int a, int b) => 10 / a + 20 / b;
            }
            """;
        var firstPosition = source.IndexOf("a +", StringComparison.Ordinal);
        var secondPosition = source.LastIndexOf("b;", StringComparison.Ordinal);
        using var session = SharpProofAnalysisSession.FromText(source);
        var byPosition = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Position, Position: firstPosition),
            SharpProofAnalysisFacet.RuntimeHazards));
        var line = source.Take(secondPosition).Count(static character => character == '\n') + 1;
        var lineStart = source.LastIndexOf('\n', secondPosition) + 1;
        var byPoint = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: line, Column: secondPosition - lineStart + 1),
            SharpProofAnalysisFacet.RuntimeHazards));
        Assert.Multiple(() => {
            Assert.That(byPosition.Hazards.Count(static hazard => hazard.Kind == "DivideByZero"), Is.EqualTo(1));
            Assert.That(byPosition.Hazards.Single(static hazard => hazard.Kind == "DivideByZero").Operation,
                Does.Contain("10 / a"));
            Assert.That(byPoint.Hazards.Count(static hazard => hazard.Kind == "DivideByZero"), Is.EqualTo(1));
            Assert.That(byPoint.Hazards.Single(static hazard => hazard.Kind == "DivideByZero").Operation,
                Does.Contain("20 / b"));
        });
    }
    [Test]
    public void ThrowCandidateIdentityPreservesNullAndNonNullRoutes() {
        const string source = "class C { static void M(System.Exception ex) { throw ex; } }";
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "ex;", 0);
        Assert.Multiple(() => {
            Assert.That(result.Hazards, Has.Some.Property(nameof(SharpProofHazard.ExceptionType))
                .EqualTo("System.NullReferenceException"));
            Assert.That(result.Hazards, Has.Some.Property(nameof(SharpProofHazard.ExceptionType))
                .EqualTo("System.Exception"));
        });
    }
    [Test]
    public void ReducedExtensionReceiverIsNotAnInstanceDereference() {
        const string source = """
            static class E { public static int Len(this string? value) => value?.Length ?? 0; }
            class C { static int M(string? value) => value.Len(); }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "value.Len", 0);
        Assert.That(result.Hazards.Where(static hazard => hazard.ExceptionType == "System.NullReferenceException"), Is.Empty);
    }
    [Test]
    public void NullableConditionalAccessPreservesInnerHasValue() {
        const string source = """
            sealed class B { public int? Get() => null; }
            class C { static int M(B? value) { var x = value?.Get(); return x.Value; } }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "x.Value", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.InvalidOperationException" && hazard.Status == "Proven"));
    }
    [Test]
    public void DefinitionTimeSnapshotPreservesDefiniteInvalidCast() {
        const string source = """
            class A { }
            class B { }
            class C { static B M() { object y = new A(); object x = y; y = new B(); return (B)x; } }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "(B)x", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.InvalidCastException" && hazard.Status == "Proven"));
    }
    [Test]
    public void DefinitionTimeSnapshotPreservesDefiniteArrayTypeMismatch() {
        const string source = """
            class C {
                static void M() {
                    object[] later = new string[1];
                    var snapshot = later;
                    later = new object[1];
                    snapshot[0] = new object();
                }
            }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "snapshot[0]", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.ArrayTypeMismatchException" && hazard.Status == "Proven"));
    }
    [Test]
    public void OpenVirtualPredicateDoesNotMakeReachableHazardUnreachable() {
        const string source = """
            class B { public virtual bool IsZero() => false; }
            sealed class D : B { public override bool IsZero() => true; }
            class C {
                static int M(B value) {
                    var zero = 0;
                    if (value.IsZero()) return 10 / zero;
                    return 1;
                }
            }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "10 / zero", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.DivideByZeroException" && hazard.Status != "Unreachable"));
    }
    [Test]
    public void UserDefinedEqualityDoesNotEstablishANullGuard() {
        const string source = """
            class P {
                public int Value;
                public static bool operator ==(P? left, P? right) => false;
                public static bool operator !=(P? left, P? right) => true;
                public override bool Equals(object? value) => false;
                public override int GetHashCode() => 0;
            }
            class C { static int M(P? value) { if (value == null) throw new System.Exception(); return value.Value; } }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "value.Value", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.NullReferenceException" && hazard.Status != "Unreachable"));
    }
    [Test]
    public void NonZeroBasedMultidimensionalArrayBoundsAreNotAssumedToStartAtZero() {
        const string source = "class C { static int M(int[,] values, int i, int j) => values[i, j]; }";
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "values[i", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.Kind == "IndexOutOfRange" && hazard.Status != "Unreachable"));
    }
    [Test]
    public void PerDimensionLowerAndUpperBoundsCanProveMultidimensionalAccessSafe() {
        const string source = """
            class C {
                static int M(int[,] values, int i, int j) {
                    if (i < values.GetLowerBound(0) || i > values.GetUpperBound(0) ||
                        j < values.GetLowerBound(1) || j > values.GetUpperBound(1)) return 0;
                    return values[i, j];
                }
            }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "values[i", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.Kind == "IndexOutOfRange" && hazard.Status == "Unreachable"));
    }
    [Test]
    public void SymbolicDecimalArithmeticProducesAConservativeOverflowCandidate() {
        const string source = "class C { static decimal M(decimal value) => value * decimal.MaxValue; }";
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "value *", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.OverflowException" && hazard.Status == "Unknown"));
    }
    [Test]
    public void ConstantDecimalArithmeticIsClassifiedExactly() {
        const string source = "class C { static decimal M() => 1m + 2m; }";
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "1m +", 0);
        Assert.That(result.Hazards, Has.None.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.OverflowException"));
    }
    [Test]
    public void DecimalDivisionByZeroIsProven() {
        const string source = "class C { static decimal M(decimal value) => value / 0m; }";
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = AnalyzeHazardsAt(session, source, "value /", 0);
        Assert.That(result.Hazards, Has.Some.Matches<SharpProofHazard>(hazard =>
            hazard.ExceptionType == "System.DivideByZeroException" && hazard.Status == "Proven"));
    }
    private static SharpProofAnalysisResult AnalyzeHazardsAt(
        SharpProofAnalysisSession session,
        string source,
        string marker,
        int occurrence) {
        var position = -1;
        for (var index = 0; index <= occurrence; index++)
            position = source.IndexOf(marker, position + 1, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Marker was not found.");
        return session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Position, Position: position),
            SharpProofAnalysisFacet.RuntimeHazards));
    }
}
