namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class ArgumentNullGuardBoundaryTests
{
    [Test]
    public void BackendGuardsPreserveParameterNames()
    {
        var optionsError = Assert.Throws<ArgumentNullException>(
            (Action)(() =>
            {
                _ = new IrSmtBackend(null!);
            }));
        using var backend = new IrSmtBackend();
        var queryError = Assert.Throws<ArgumentNullException>(
            (Action)(() =>
            {
                _ = backend.CheckAsync(null!, CancellationToken.None);
            }));

        Assert.That(optionsError!.ParamName, Is.EqualTo("options"));
        Assert.That(queryError!.ParamName, Is.EqualTo("query"));
    }
}
