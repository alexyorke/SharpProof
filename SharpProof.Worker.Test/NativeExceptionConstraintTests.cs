using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeExceptionConstraintTests
{
    [TestCase("System.OverflowException", false)]
    [TestCase("System.DivideByZeroException", false)]
    [TestCase("System.ArithmeticException", true)]
    [TestCase("System.SystemException", true)]
    [TestCase("System.Exception", true)]
    [TestCase("System.ArgumentException", false)]
    public async Task BoundAllowedTypesControlJoinedFaultsAndAgreeWithRuntime(string allowedType, bool proven)
    {
        var source = "using SharpProof.Attributes; public static class C { [AllowedExceptions(typeof(" + allowedType +
            "))] public static int Target(int x) { if (x == 0) return 10 / x; return checked(x + 1); } }";
        var preparation = RoundTrip(source);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        if (result.Outcome is RefutedOutcome)
        {
            var input = (int)result.EntryModel.Values.Single().IntegerNumericValue;
            var actual = RuntimeException(source, input);
            var allowed = typeof(Exception).Assembly.GetType(allowedType)!;
            Assert.That(allowed.IsAssignableFrom(actual.GetType()), Is.False);
        }
    }

    [Test]
    public async Task MultipleExceptionClaimsUseTheirOwnConstraintInOneSession()
    {
        var preparation = RoundTrip("""
            using SharpProof.Attributes;
            public static class C {
                [DoesNotThrow, AllowedExceptions(typeof(System.ArithmeticException))]
                public static int Target(int x) { return checked(10 / x); }
            }
            """);
        var checks = await NativeExceptionEffectVerifier.VerifyClaimsAsync(preparation, new WorkerBudgets());
        Assert.That(checks.Keys, Is.EquivalentTo(preparation.EffectClaims.Select(claim => claim.ClaimId)));
        foreach (var claim in preparation.EffectClaims)
        {
            Assert.That(checks[claim.ClaimId].Outcome, claim.ContractKind == WorkerEffectContractKind.DoesNotThrow
                ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>());
        }
    }

    [Test]
    public async Task SourceLookalikeCannotAllowCoreLibraryOverflow()
    {
        const string source = """
            using SharpProof.Attributes;
            namespace System { public class OverflowException : Exception {} }
            public static class C {
                [AllowedExceptions(typeof(System.OverflowException))]
                public static int Target(int x) { return checked(x + 1); }
            }
            """;
        var preparation = RoundTrip(source);
        Assert.That(preparation.Total!.ExceptionConstraints.Single().AllowedKinds, Is.Empty);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(RuntimeException(source, (int)result.EntryModel.Values.Single().IntegerNumericValue), Is.TypeOf<OverflowException>());
    }

    [TestCase("unknown-kind")]
    [TestCase("duplicate-kind")]
    [TestCase("unordered-kinds")]
    [TestCase("duplicate-claim")]
    [TestCase("foreign-claim")]
    [TestCase("entry-constraint")]
    [TestCase("null-kinds")]
    public void DecoderRejectsMalformedOrForeignConstraints(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class C {
                [AllowedExceptions(typeof(System.Exception))]
                public static int Target(int x) { return x; }
            }
            """);
        var total = artifact.Callables.Single().Total!;
        var row = total.ExceptionConstraints.Single();
        if (mutation == "unknown-kind")
        {
            // Inject an invalid wire value; producer enum validation rejects it too.
            var node = JsonNode.Parse(CompilerManifestArtifactJson.SerializeProducerValidated(artifact))!;
            node["callables"]![0]!["total"]!["exceptionConstraints"]![0]!["allowedKinds"] = new JsonArray(JsonValue.Create(99));
            Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(node.ToJsonString(), out _)));
            return;
        }
        switch (mutation)
        {
            case "duplicate-kind":
                row.AllowedKinds = [IrExceptionKind.Overflow, IrExceptionKind.Overflow];
                break;
            case "unordered-kinds":
                row.AllowedKinds = [IrExceptionKind.Overflow, IrExceptionKind.DivideByZero];
                break;
            case "duplicate-claim":
                total.ExceptionConstraints = [row, row];
                break;
            case "foreign-claim":
                row.ClaimId = "foreign";
                break;
            case "entry-constraint":
                artifact.Callables.Single().TotalEntry!.ExceptionConstraints = [row];
                break;
            case "null-kinds":
                row.AllowedKinds = null!;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOptionalConstraintAbstainsInsteadOfAssumingDoesNotThrow(bool contradictory)
    {
        var preparation = RoundTrip("using SharpProof.Attributes; public static class C { [DoesNotThrow] public static int Target(int x) { " +
            (contradictory ? "Contract.Requires(x > 0 && x < 0); " : "") + "return x; } }");
        preparation = preparation with { Total = preparation.Total! with { ExceptionConstraints = [] } };
        Assert.That((await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets())).Reason,
            Is.EqualTo(WorkerClaimReason.UnsupportedContract));
    }

    [Test]
    public void DoesNotThrowCannotCarryAnAllowance()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; public static class C { [DoesNotThrow] public static int Target(int x) { return x; } }");
        artifact.Callables.Single().Total!.ExceptionConstraints.Single().AllowedKinds = [IrExceptionKind.Overflow];
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    private static CompilerCallablePreparation RoundTrip(string source)
    {
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        return preparations.Single();
    }

    private static Exception RuntimeException(string source, int input)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("NativeConstraintOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("NativeConstraintOracle", isCollectible: true);
        try
        {
            var target = context.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
            return Assert.Throws<TargetInvocationException>(new Action(() => target.Invoke(null, [input])))!.InnerException!;
        }
        finally { context.Unload(); }
    }
}
