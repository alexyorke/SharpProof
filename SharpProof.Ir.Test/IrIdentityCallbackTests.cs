using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrIdentityCallbackTests
{
    [Test]
    public void ReentrantComparerInsertionIsIncludedBeforePublishingANewIdentity()
    {
        var factory = new IrFactory();
        var comparer = new ReentrantComparer(factory);
        var first = factory.InternExternalIdentity(1, comparer);
        var second = factory.InternExternalIdentity(2, comparer);
        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(comparer.InsertedIdentity, Is.EqualTo(factory.InternExternalIdentity(3, comparer)));
        Assert.That(factory.InternExternalIdentity(2, comparer), Is.EqualTo(second));
        Assert.That(comparer.Comparisons, Does.Contain((3, 2)));
    }

    private sealed class ReentrantComparer(IrFactory factory) : IEqualityComparer<int>
    {
        private bool _inserted;
        internal IrIdentityId InsertedIdentity { get; private set; }
        internal List<(int, int)> Comparisons { get; } = [];

        public bool Equals(int left, int right)
        {
            Comparisons.Add((left, right));
            if (!_inserted && right == 2)
            {
                _inserted = true;
                InsertedIdentity = factory.InternExternalIdentity(3, this);
            }
            return left == right;
        }

        public int GetHashCode(int value) => 0;
    }
}
