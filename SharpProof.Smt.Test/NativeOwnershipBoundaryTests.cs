using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeOwnershipBoundaryTests
{
    [Test]
    public void RetiredExpressionOwnerRejectsAndReleasesNewNativeExpressions()
    {
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        owner.Own(context.MkInt(7));
        owner.Dispose();
        var expression = context.MkInt(11);

        Assert.Throws<ObjectDisposedException>((Action)(() => { _ = owner.Own(expression); }));
        Assert.That(owner.OwnedCount, Is.Zero);
        var nativeObject = typeof(Z3Object).GetProperty("NativeObject",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        Assert.That(nativeObject, Is.Not.Null);
        Assert.That(nativeObject!.GetValue(expression), Is.EqualTo(IntPtr.Zero));
    }

    [Test]
    public void LegacySemanticsCannotEnterTheBitvectorBackend()
    {
        var error = Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var backend = new CallableSolverSession(
                new IrFactory(IrExecutionSemantics.Legacy), new IrSmtBackendOptions());
        }));

        Assert.That(error!.ParamName, Is.EqualTo("factory"));
    }
}
