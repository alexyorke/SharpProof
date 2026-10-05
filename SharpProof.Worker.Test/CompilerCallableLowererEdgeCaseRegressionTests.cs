using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerCallableLowererEdgeCaseRegressionTests
{
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
        var compilation = TestCompilation.Create(
            "CompilerCallableLowererEdgeCaseRegressionTests",
            (Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "CompilerCallableLowererEdgeCaseSubject.cs"),
                source));

        var discovery = new ClaimManifestBuilder(compilation).Build();
        return (compilation, discovery);
    }
}
