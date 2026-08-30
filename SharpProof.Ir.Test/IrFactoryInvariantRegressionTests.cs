using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrFactoryInvariantRegressionTests
{
    [Test]
    public void CastRejectsInvalidNonIdentitySourceAndTargetTypes()
    {
        var factory = new IrFactory();

        var scalarCast = Assert.Throws<ArgumentException>(
            (Action)(() => factory.Cast(
                factory.BooleanType,
                factory.Integer(1))));
        var boxingCast = Assert.Throws<ArgumentException>(
            (Action)(() => factory.Cast(
                factory.ObjectType,
                factory.Integer(1))));
        var nullToScalarCast = Assert.Throws<ArgumentException>(
            (Action)(() => factory.Cast(
                factory.IntegerType,
                factory.Null(factory.StringType))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scalarCast!.ParamName, Is.EqualTo("operand"));
            Assert.That(boxingCast!.ParamName, Is.EqualTo("operand"));
            Assert.That(nullToScalarCast!.ParamName, Is.EqualTo("targetType"));
        }
    }

    [Test]
    public void ExistingReferenceTypeLookupDoesNotInternDiscardedDisplayName()
    {
        var factory = new IrFactory();
        var identity = factory.CreateIdentity();
        var type = factory.GetOrCreateReferenceType(identity, "Widget");

        var existing = factory.GetOrCreateReferenceType(identity, "Gadget");
        var marker = factory.InternString("after-lookup");
        var discarded = factory.InternString("Gadget");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(existing, Is.EqualTo(type));
            Assert.That(marker.Value, Is.LessThan(discarded.Value));
        }
    }

    [Test]
    public void ExistingIdentitySequenceTypeLookupDoesNotInternDiscardedDisplayName()
    {
        var factory = new IrFactory();
        var identity = factory.CreateIdentity();
        var type = factory.GetOrCreateSequenceType(
            identity,
            factory.IntegerType,
            "Widgets");

        var existing = factory.GetOrCreateSequenceType(
            identity,
            factory.IntegerType,
            "Gadgets");
        var marker = factory.InternString("after-lookup");
        var discarded = factory.InternString("Gadgets");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(existing, Is.EqualTo(type));
            Assert.That(marker.Value, Is.LessThan(discarded.Value));
        }
    }

    [Test]
    public void StringSemanticIdentityCannotBypassRejectionThroughObjectWidening()
    {
        var factory = new IrFactory();
        object identity = "semantic-key";

        var error = Assert.Throws<ArgumentException>(
            (Action)(() => factory.InternExternalIdentity(
                identity,
                EqualityComparer<object>.Default)));

        Assert.That(error!.ParamName, Is.EqualTo("identity"));
    }

    [Test]
    public void SequenceEnumerationRunsOutsideFactoryLock()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);

        IEnumerable<IrValue> Elements()
        {
            AssertFactoryCanMakeProgressFromAnotherThread(factory);
            yield return factory.CreateIntegerValue(1);
        }

        var value = factory.CreateSequenceValue(sequenceType, Elements());

        Assert.That(value.Elements, Has.Length.EqualTo(1));
    }

    [Test]
    public void ExternalIdentityHashingRunsOutsideFactoryLock()
    {
        var factory = new IrFactory();
        var comparer = new ProgressCheckingHashComparer(factory);

        var identity = factory.InternExternalIdentity(new object(), comparer);

        Assert.That(identity.IsDefault, Is.False);
    }

    [Test]
    public void ExternalIdentityEqualityRunsOutsideFactoryLock()
    {
        var factory = new IrFactory();
        var comparer = new ProgressCheckingEqualityComparer(factory);
        var first = factory.InternExternalIdentity(new object(), comparer);

        var second = factory.InternExternalIdentity(new object(), comparer);

        Assert.That(second, Is.EqualTo(first));
    }

    private static void AssertFactoryCanMakeProgressFromAnotherThread(
        IrFactory factory)
    {
        var worker = new Thread(() => factory.CreateIdentity())
        {
            IsBackground = true
        };
        worker.Start();

        Assert.That(
            worker.Join(TimeSpan.FromSeconds(2)),
            Is.True,
            "Caller code ran while the factory-wide lock was held.");
    }

    private sealed class ProgressCheckingHashComparer(IrFactory factory) :
        IEqualityComparer<object>
    {
        public new bool Equals(object? left, object? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            AssertFactoryCanMakeProgressFromAnotherThread(factory);
            return 0;
        }
    }

    private sealed class ProgressCheckingEqualityComparer(IrFactory factory) :
        IEqualityComparer<object>
    {
        public new bool Equals(object? left, object? right)
        {
            AssertFactoryCanMakeProgressFromAnotherThread(factory);
            return true;
        }

        public int GetHashCode(object value)
        {
            return 0;
        }
    }
}
