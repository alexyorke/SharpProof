namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class AutoPropertyFactsCoverageTests
{
    [Test]
    public void IsAccessorRejectsNullMethod()
    {
        Assert.Throws<ArgumentNullException>(
            (Action)(() =>
                _ = AutoPropertyFacts.IsAccessor(null!, default)));
    }

    [Test]
    public void IsAutoPropertyRejectsNullProperty()
    {
        Assert.Throws<ArgumentNullException>(
            (Action)(() =>
                _ = AutoPropertyFacts.IsAutoProperty(null!, default)));
    }

    [Test]
    public void TryGetBackingFieldRejectsNullProperty()
    {
        Assert.Throws<ArgumentNullException>(
            (Action)(() =>
                _ = AutoPropertyFacts.TryGetBackingField(null!, out _)));
    }
}
