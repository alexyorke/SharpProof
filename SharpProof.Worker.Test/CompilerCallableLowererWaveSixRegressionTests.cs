using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerCallableLowererWaveSixRegressionTests
{
    [Test]
    public void SignedInt64SourceIntervalsAreProjected()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static long Identity(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
            }
            """,
            "Identity");

        Assert.That(
            preparation.IsSuccess,
            Is.True,
            preparation.FailureReason.ToString());
        var expected = new CompilerIntegerInterval(
            long.MinValue,
            long.MaxValue);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                preparation.Variables.Single(static variable =>
                    variable.Role == CompilerVariableRole.Parameter)
                    .SourceIntegerInterval,
                Is.EqualTo(expected));
            Assert.That(
                preparation.Variables.Single(static variable =>
                    variable.Role == CompilerVariableRole.Result)
                    .SourceIntegerInterval,
                Is.EqualTo(expected));
        }
    }

    [Test]
    public void SignedInt64SourceIntervalsRoundTripThroughCompilerArtifact()
    {
        var source =
            """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            internal static class Subject {
                internal static long Identity(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
            }
            """;
        var (compilation, discovery) = CreateCompilation(source);
        var artifact = CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);

        var roundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));
        var preparation = CompilerManifestArtifactJson.DecodeCallables(
            roundTrip).Single();
        var expected = new CompilerIntegerInterval(
            long.MinValue,
            long.MaxValue);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                preparation.Variables.Single(static variable =>
                    variable.Role == CompilerVariableRole.Parameter)
                    .SourceIntegerInterval,
                Is.EqualTo(expected));
            Assert.That(
                preparation.Variables.Single(static variable =>
                    variable.Role == CompilerVariableRole.Result)
                    .SourceIntegerInterval,
                Is.EqualTo(expected));
        }
    }

    [Test]
    public void ExpressionBodiedRequiresOnlyVoidMethodIsAdmitted()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static void Verify(int value) =>
                    Contract.Requires(value > 0);
            }
            """,
            "Verify");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                preparation.IsSuccess,
                Is.True,
                preparation.FailureReason.ToString());
            Assert.That(
                preparation.FailureReason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                preparation.Clauses.Select(static clause => clause.Kind),
                Is.EqualTo(new[] { CompilerContractKind.Requires }));
            Assert.That(preparation.Body, Is.Null);
        }
    }

    private static CompilerCallablePreparation Prepare(
        string source,
        string methodName)
    {
        var (compilation, discovery) = CreateCompilation(source);
        var target = discovery.Targets.Values.Single(candidate =>
            candidate.Method.MetadataName == methodName);
        return new CompilerCallableLowerer(compilation, new IrFactory())
            .Prepare(target);
    }

    private static (
        CSharpCompilation Compilation,
        ClaimManifestBuildResult Discovery) CreateCompilation(
        string source)
    {
        var compilation = WorkerTestCompilation.Create(
            "CompilerCallableLowererWaveSixRegressionTests",
            (Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "CompilerCallableLowererWaveSixSubject.cs"),
                source));

        var discovery = new ClaimManifestBuilder(compilation).Build();
        return (compilation, discovery);
    }
}
