using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractAttributeNameGuardCoverageTests
{
    [TestCase("")]
    [TestCase("Example.UnknownAttribute")]
    public void UnknownAttributeNamesFailWithTheOriginalArgument(string metadataName)
    {
        using var subject = TypedProgramSubject.Create("int Target() => 17;");
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var resolver = ContractApiIdentityResolver.ForCompilation(subject.Compilation);
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = resolver.ResolveAttribute(metadataName)));
        Assert.That(error!.ParamName, Is.EqualTo("metadataName"));
        Assert.That(error.ActualValue, Is.EqualTo(metadataName));
    }
}
