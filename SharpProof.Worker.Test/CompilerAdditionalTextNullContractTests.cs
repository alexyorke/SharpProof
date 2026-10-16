using NUnit.Framework;
using SharpProof.CompilerSupport;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerAdditionalTextNullContractTests
{
    [Test]
    public void ShippedCollectorRejectsNullAdditionalTextArgument()
    {
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            _ = CompilerAdditionalTextStability.GetStableAdditionalText(null!, CancellationToken.None);
        }));

        Assert.That(exception!.ParamName, Is.EqualTo("file"));
    }
}
