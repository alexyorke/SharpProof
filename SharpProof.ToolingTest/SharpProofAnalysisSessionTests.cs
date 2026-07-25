using NUnit.Framework;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class SharpProofAnalysisSessionTests {
    public sealed record HazardCase(string Name, string Source, string Marker,
        string? Kind = null, string? ExceptionType = null, string? Status = null, string? ForbiddenStatus = null,
        bool Expected = true);

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
    [TestCase(SharpProofTargetKind.AllLines)]
    [TestCase(SharpProofTargetKind.Span)]
    public void DefaultFacetsWorkForMultiPointTargets(SharpProofTargetKind kind) {
        const string source = """
            class C {
                static int M(int value) {
                    return value + 1;
                }
            }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var target = kind == SharpProofTargetKind.Span
            ? new SharpProofTarget(kind, SpanStart: 0, SpanEnd: source.Length)
            : new SharpProofTarget(kind);
        var result = session.Analyze(new SharpProofAnalysisRequest(target));
        Assert.Multiple(() => {
            Assert.That(result.Status, Is.Not.EqualTo(SharpProofQueryStatus.Failed));
            Assert.That(result.MethodEffects, Is.Not.Null);
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
        var result = AnalyzeProof("""
            class C {
                static int M(int value) {
                    return value;
                }
            }
            """, 3, 13, "value > 0");
        var proof = result.ProofFacts.Single();
        Assert.Multiple(() => {
            Assert.That(proof.Status, Is.EqualTo("Unknown"));
            Assert.That(proof.SymbolicCondition, Does.Contain("value"));
            Assert.That(proof.Counterexample, Does.Contain("value="));
        });
    }
    [Test]
    public void ReorderedNamedStringArgumentsPreservePathFacts() {
        var result = AnalyzeProof("""
            class C {
                static int M(string text) {
                    if (!text.StartsWith(comparisonType: System.StringComparison.Ordinal, value: "pre")) return 0;
                    return 1;
                }
            }
            """, 4, 9, "text.StartsWith(\"pre\", System.StringComparison.Ordinal)");
        Assert.That(result.ProofFacts.Single().Status, Is.EqualTo("ProvenTrue"));
    }
    [TestCase("StartsWith")]
    [TestCase("EndsWith")]
    public void CultureSensitiveStringPredicatesDoNotImplyOrdinalFacts(string method) {
        var result = AnalyzeProof($$"""
            class C {
                static int M(string text) {
                    if (!text.{{method}}("\u00AD")) return 0;
                    return 1;
                }
            }
            """, 4, 9, $"text.{method}(\"\\u00AD\", System.StringComparison.Ordinal)");
        Assert.That(result.ProofFacts.Single().Status, Is.EqualTo("Unknown"));
    }
    [Test]
    public void OrdinalIgnoreCaseDoesNotUseRegexCaseFolding() {
        const string source = """
            class C {
                static int M(string text) {
                    if (!text.Equals("k", System.StringComparison.OrdinalIgnoreCase)) return 0;
                    return 1;
                }
            }
            """;
        var result = AnalyzeProof(source, 4, 9,
            "text.Equals(\"\\u212A\", System.StringComparison.OrdinalIgnoreCase)");
        var equivalent = AnalyzeProof(source, 4, 9,
            "text.Equals(\"K\", System.StringComparison.OrdinalIgnoreCase)");
        Assert.Multiple(() => {
            Assert.That(result.ProofFacts.Single().Status, Is.EqualTo("ProvenFalse"));
            Assert.That(equivalent.ProofFacts.Single().Status, Is.EqualTo("ProvenTrue"));
        });
    }
    [TestCase("IndexOf")]
    [TestCase("LastIndexOf")]
    public void OrdinalIgnoreCaseSearchDoesNotUseRegexCaseFolding(string method) {
        var result = AnalyzeProof($$"""
            class C {
                static int M(string text) {
                    if (text.{{method}}("k", System.StringComparison.OrdinalIgnoreCase) < 0) return 0;
                    return 1;
                }
            }
            """, 4, 9, $"text.{method}(\"\\u212A\", System.StringComparison.OrdinalIgnoreCase) >= 0");
        Assert.That(result.ProofFacts.Single().Status, Is.EqualTo("Unknown"));
    }
    [Test]
    public void ConditionProofInsideLocalFunctionResolvesCapturedParameters() {
        const string source = """
            class C {
                static int M(string left, string right) {
                    int Local() => (left + right).Length;
                    return Local();
                }
            }
            """;
        var result = AnalyzeProof(source, 3, 9, "left == left");
        var falseResult = AnalyzeProof(source, 3, 9, "left != left");
        Assert.Multiple(() => {
            Assert.That(result.ProofFacts.Single().Status, Is.EqualTo("ProvenTrue"));
            Assert.That(falseResult.ProofFacts.Single().Status, Is.EqualTo("ProvenFalse"));
        });
    }
    [Test]
    public void ConditionProofAtLocalFunctionDeclarationProducesCounterexample() {
        const string source = """
            class C {
                static int M(int x) {
                    int Local() => x + 1;
                    return Local();
                }
            }
            """;
        var result = AnalyzeProof(source, 3, 9, "x > 0");
        var proof = result.ProofFacts.Single();
        Assert.Multiple(() => {
            Assert.That(proof.Status, Is.EqualTo("Unknown"));
            Assert.That(proof.Counterexample, Does.Contain("x="), proof.Reason);
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
    public void PositionTargetAcceptsEndOfFile() {
        const string source = "class C { static int M() => 1; }";
        using var session = SharpProofAnalysisSession.FromText(source);
        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Position, Position: source.Length),
            SharpProofAnalysisFacet.RuntimeHazards));
        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(SharpProofQueryStatus.Succeeded));
            Assert.That(result.Error, Is.Null);
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
    public static IEnumerable<TestCaseData> HazardCases() {
        yield return Hazard("ReducedExtensionReceiverIsNotAnInstanceDereference",
            "static class E { public static int Len(this string? value) => value?.Length ?? 0; }\n" +
            "class C { static int M(string? value) => value.Len(); }",
            "value.Len",
            exceptionType: "System.NullReferenceException",
            expected: false);
        yield return Hazard("NullableConditionalAccessPreservesInnerHasValue",
            "sealed class B { public int? Get() => null; }\n" +
            "class C { static int M(B? value) { var x = value?.Get(); return x.Value; } }",
            "x.Value",
            exceptionType: "System.InvalidOperationException",
            status: "Proven");
        yield return Hazard("DefinitionTimeSnapshotPreservesDefiniteInvalidCast",
            "class A { }\nclass B { }\n" +
            "class C { static B M() { object y = new A(); object x = y; y = new B(); return (B)x; } }",
            "(B)x",
            exceptionType: "System.InvalidCastException",
            status: "Proven");
        yield return Hazard("DefinitionTimeSnapshotPreservesDefiniteArrayTypeMismatch",
            "class C { static void M() { object[] later = new string[1]; var snapshot = later; " +
            "later = new object[1]; snapshot[0] = new object(); } }",
            "snapshot[0]",
            exceptionType: "System.ArrayTypeMismatchException",
            status: "Proven");
        yield return Hazard("OpenVirtualPredicateDoesNotMakeReachableHazardUnreachable",
            "class B { public virtual bool IsZero() => false; }\n" +
            "sealed class D : B { public override bool IsZero() => true; }\n" +
            "class C { static int M(B value) { var zero = 0; " +
            "if (value.IsZero()) return 10 / zero; return 1; } }",
            "10 / zero",
            exceptionType: "System.DivideByZeroException",
            forbiddenStatus: "Unreachable");
        yield return Hazard("UserDefinedEqualityDoesNotEstablishANullGuard",
            "class P { public int Value; public static bool operator ==(P? left, P? right) => false; " +
            "public static bool operator !=(P? left, P? right) => true; " +
            "public override bool Equals(object? value) => false; public override int GetHashCode() => 0; }\n" +
            "class C { static int M(P? value) { if (value == null) throw new System.Exception(); return value.Value; } }",
            "value.Value",
            exceptionType: "System.NullReferenceException",
            forbiddenStatus: "Unreachable");
        yield return Hazard("NonZeroBasedMultidimensionalArrayBoundsAreNotAssumedToStartAtZero",
            "class C { static int M(int[,] values, int i, int j) => values[i, j]; }", "values[i",
            kind: "IndexOutOfRange", forbiddenStatus: "Unreachable");
        yield return Hazard("PerDimensionLowerAndUpperBoundsCanProveMultidimensionalAccessSafe",
            "class C { static int M(int[,] values, int i, int j) { " +
            "if (i < values.GetLowerBound(0) || i > values.GetUpperBound(0) || " +
            "j < values.GetLowerBound(1) || j > values.GetUpperBound(1)) return 0; return values[i, j]; } }",
            "values[i",
            kind: "IndexOutOfRange",
            status: "Unreachable");
        yield return Hazard("ReorderedNamedMemoryExtensionArgumentsPreserveViewLength",
            "class C { static char M(string text) { if (text.Length < 3) return '\\0'; " +
            "var span = System.MemoryExtensions.AsSpan(start: 2, text: text); return span[text.Length - 3]; } }",
            "span[text.Length",
            kind: "IndexOutOfRange",
            status: "Unreachable");
        yield return Hazard("SymbolicDecimalArithmeticProducesAConservativeOverflowCandidate",
            "class C { static decimal M(decimal value) => value * decimal.MaxValue; }", "value *",
            exceptionType: "System.OverflowException", status: "Unknown");
        yield return Hazard("ConstantDecimalArithmeticIsClassifiedExactly",
            "class C { static decimal M() => 1m + 2m; }", "1m +",
            exceptionType: "System.OverflowException", expected: false);
        yield return Hazard("DecimalDivisionByZeroIsProven",
            "class C { static decimal M(decimal value) => value / 0m; }", "value /",
            exceptionType: "System.DivideByZeroException", status: "Proven");
    }
    [TestCaseSource(nameof(HazardCases))]
    public void HazardMatrix(HazardCase test) {
        using var session = SharpProofAnalysisSession.FromText(test.Source);
        var result = AnalyzeHazardsAt(session, test.Source, test.Marker, 0);
        Assert.That(result.Hazards.Any(hazard =>
            (test.Kind is null || hazard.Kind == test.Kind) &&
            (test.ExceptionType is null || hazard.ExceptionType == test.ExceptionType) &&
            (test.Status is null || hazard.Status == test.Status) &&
            (test.ForbiddenStatus is null || hazard.Status != test.ForbiddenStatus)),
            Is.EqualTo(test.Expected));
    }
    private static TestCaseData Hazard(string name, string source, string marker,
        string? kind = null, string? exceptionType = null, string? status = null, string? forbiddenStatus = null,
        bool expected = true) =>
        new(new HazardCase(name, source, marker, kind, exceptionType, status, forbiddenStatus, expected)) {
            TestName = name
        };
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
    private static SharpProofAnalysisResult AnalyzeProof(
        string source,
        int line,
        int column,
        string condition) {
        using var session = SharpProofAnalysisSession.FromText(source);
        return session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: line, Column: column),
            SharpProofAnalysisFacet.ProofFacts,
            condition));
    }
}
