using System.Reflection;
using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
[NonParallelizable]
public sealed class NativeLoggingInitializationTests
{
    [Test]
    public void InvalidNativeLogPathReleasesTheContextAfterSolverCreationFails()
    {
        const string parameter = "solver.smtlib2_log";
        var previous = Global.GetParameter(parameter) ?? string.Empty;
        var missingDirectory = Path.Combine(Path.GetTempPath(), "sharpproof-smt-missing-" + Guid.NewGuid().ToString("N"));
        Assert.That(Directory.Exists(missingDirectory), Is.False);
        try
        {
            Global.SetParameter(parameter, Path.Combine(missingDirectory, "solver.smt2"));
            using var context = new Context();
            var nativeHandle = typeof(Context).GetField("m_ctx", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(nativeHandle, Is.Not.Null);
            Assert.That(nativeHandle!.GetValue(context), Is.Not.EqualTo(IntPtr.Zero));
            var factory = new IrFactory(IrExecutionSemantics.Total);

            var error = Assert.Throws<Z3Exception>((Action)(() =>
            {
                using var session = new CallableSolverSession(factory, new IrSmtBackendOptions(), () => context);
            }));

            Assert.That(error!.Message, Does.Contain("open"));
            // Read the actual managed ownership marker; never invoke native code
            // through a context after its disposal, or fabricate a handle.
            Assert.That(nativeHandle.GetValue(context), Is.EqualTo(IntPtr.Zero));
        }
        finally
        {
            Global.SetParameter(parameter, previous);
        }
    }
}
