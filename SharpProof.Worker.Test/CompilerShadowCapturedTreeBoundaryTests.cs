using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerShadowCapturedTreeBoundaryTests
{
    [Test]
    public void IncompleteCapturedTreeArrayRejectsWholeBatchAfterRealCompleteControl()
    {
        var compilation = TestCompilation.Create("ShadowCapturedTreeBoundary",
            ("root.cs", "public static partial class Subject { public static int Root(int value) => Helper(value); }"),
            ("helper.cs", "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; " +
                "public static partial class Subject { public static int Helper(int value) { " +
                "Contract.Requires(value > 0); return value + 1; } }"));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ShadowCapturedTreeBoundary", isCollectible: true);
        try
        {
            image.Position = 0;
            var root = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!;
            Assert.That(root.Invoke(null, [1]), Is.EqualTo(2));
            Assert.That(root.Invoke(null, [4]), Is.EqualTo(5));
        }
        finally { runtime.Unload(); }

        var captured = CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None);
        Assert.That(captured, Has.Length.EqualTo(2));
        var authority = CompilerSpecificationPackProvider.ResolveConfiguration([]);
        var complete = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            captured, null, authority, CancellationToken.None);
        Assert.That(complete.Callers, Has.Length.EqualTo(1));
        Assert.That(complete.Callers.Single().Body.CallPreconditions, Has.Length.EqualTo(1));

        // Retain the original compiler capture record; only the argument array is incomplete.
        var incomplete = captured[..1];
        Assert.That(incomplete[0], Is.SameAs(captured[0]));
        var rejected = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            incomplete, null, authority, CancellationToken.None);
        Assert.That(rejected.Callers, Is.Empty);
        Assert.That(rejected.Gaps, Has.Length.EqualTo(1));
        Assert.That(rejected.Gaps.Single().Reason, Is.EqualTo("SourceSnapshotMismatch"));
        Assert.That(rejected.Gaps.Single().TreeOrdinal, Is.EqualTo(0));
        Assert.Throws<OperationCanceledException>((Action)(() =>
            CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
                incomplete, null, authority, new CancellationToken(true))));
    }
}
