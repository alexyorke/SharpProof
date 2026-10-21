using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrFactoryComparerTests
{
    [Test]
    public void CollidingParameterHashesPreserveDistinctMemberSignatures()
    {
        var factory = new IrFactory();
        var last = factory.ObjectType;
        while (last.Value < 397)
        {
            last = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Parameter" + last.Value);
        }
        Assert.That(last.Value, Is.EqualTo(397));
        var identity = factory.CreateIdentity();
        var first = factory.GetOrCreateMember(identity, factory.ObjectType, "Member", factory.IntegerType, true,
            factory.BooleanType, last);
        var second = factory.GetOrCreateMember(identity, factory.ObjectType, "Member", factory.IntegerType, true,
            factory.IntegerType, factory.BooleanType);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(factory.GetMemberInfo(first).ParameterTypes, Is.EqualTo(ImmutableArray.Create(factory.BooleanType, last)));
        Assert.That(factory.GetMemberInfo(second).ParameterTypes, Is.EqualTo(ImmutableArray.Create(factory.IntegerType, factory.BooleanType)));
        Assert.That(factory.GetOrCreateMember(identity, factory.ObjectType, "Member", factory.IntegerType, true,
            factory.BooleanType, last), Is.EqualTo(first));
    }

    [Test]
    public void ExternalIdentityBucketsPreserveComparerReferenceIdentity()
    {
        var factory = new IrFactory();
        var firstComparer = new ConstantHashComparer();
        var secondComparer = new ConstantHashComparer();
        var first = factory.InternExternalIdentity(42, firstComparer);
        var second = factory.InternExternalIdentity(42, secondComparer);
        var different = factory.InternExternalIdentity(43, firstComparer);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(first, Is.Not.EqualTo(different));
        Assert.That(factory.InternExternalIdentity(42, firstComparer), Is.EqualTo(first));
        Assert.That(factory.InternExternalIdentity(42, secondComparer), Is.EqualTo(second));
    }

    private sealed class ConstantHashComparer : IEqualityComparer<int>
    {
        public bool Equals(int left, int right)
        {
            return left == right;
        }

        public int GetHashCode(int value)
        {
            return 0;
        }
    }
}
