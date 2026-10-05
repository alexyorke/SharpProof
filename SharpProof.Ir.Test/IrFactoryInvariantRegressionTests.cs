using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrFactoryInvariantRegressionTests
{
    [Test]
    public void CastAllowsReferenceToScalarUnboxingTerms()
    {
        var factory = new IrFactory();
        var customReferenceType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Box");
        var objectValue = factory.CreateVariable(
            "objectValue",
            factory.ObjectType);
        var customValue = factory.CreateVariable(
            "customValue",
            customReferenceType);

        var integer = factory.Cast(
            factory.IntegerType,
            factory.Variable(objectValue));
        var boolean = factory.Cast(
            factory.BooleanType,
            factory.Variable(customValue));
        var nullInteger = factory.Cast(
            factory.IntegerType,
            factory.Null(factory.ObjectType));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(integer, Is.TypeOf<IrCastTerm>());
            Assert.That(integer.Type, Is.EqualTo(factory.IntegerType));
            Assert.That(boolean, Is.TypeOf<IrCastTerm>());
            Assert.That(boolean.Type, Is.EqualTo(factory.BooleanType));
            Assert.That(nullInteger, Is.TypeOf<IrCastTerm>());
            Assert.That(nullInteger.Type, Is.EqualTo(factory.IntegerType));
        }
    }

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
    public void OpaqueArgumentsCannotChangeAfterValidation()
    {
        const int argumentCount = 4096;
        const int maximumAttempts = 256;
        var factory = new IrFactory();
        var parameterTypes = Enumerable.Repeat(
                factory.IntegerType,
                argumentCount)
            .ToArray();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "M",
            factory.IntegerType,
            isStatic: true,
            parameterTypes);
        var valid = factory.Integer(1);
        var invalid = factory.Boolean(true);
        var arguments = Enumerable.Repeat<IrTerm>(
                valid,
                argumentCount)
            .ToArray();
        using var stop = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var mutator = new Thread(() =>
        {
            started.Set();
            while (!stop.IsCancellationRequested)
            {
                Volatile.Write(ref arguments[0], invalid);
                Thread.SpinWait(64);
                Volatile.Write(ref arguments[0], valid);
                Thread.SpinWait(64);
            }
        })
        {
            IsBackground = true
        };

        IrOpaqueTerm? poisoned = null;
        var successfulConstructions = 0;
        var joined = false;
        try
        {
            mutator.Start();
            started.Wait();
            for (var attempt = 0;
                 attempt < maximumAttempts && poisoned == null;
                 attempt++)
            {
                try
                {
                    var opaque = factory.PureOpaque(
                        member,
                        receiver: null,
                        arguments);
                    successfulConstructions++;
                    if (opaque.Arguments[0].Type != factory.IntegerType)
                    {
                        poisoned = opaque;
                    }
                }
                catch (ArgumentException)
                {
                    Thread.Yield();
                }
            }
        }
        finally
        {
            stop.Cancel();
            joined = mutator.Join(TimeSpan.FromSeconds(5));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(joined, Is.True, "The argument mutator did not stop.");
            Assert.That(
                successfulConstructions,
                Is.GreaterThan(0),
                "The constructor never observed a valid argument snapshot.");
            Assert.That(
                poisoned,
                Is.Null,
                "A signature-mismatched argument escaped validation.");
        }
    }

    [Test]
    public void ExternalIdentityHashingRunsOutsideFactoryLock()
    {
        var factory = new IrFactory();
        var comparer = new ProgressCheckingComparer(
            factory,
            checkEquality: false);

        var identity = factory.InternExternalIdentity(new object(), comparer);

        Assert.That(identity.IsDefault, Is.False);
    }

    [Test]
    public void ExternalIdentityEqualityRunsOutsideFactoryLock()
    {
        var factory = new IrFactory();
        var comparer = new ProgressCheckingComparer(
            factory,
            checkEquality: true);
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

    private sealed class ProgressCheckingComparer(
        IrFactory factory,
        bool checkEquality) :
        IEqualityComparer<object>
    {
        public new bool Equals(object? left, object? right)
        {
            if (checkEquality)
            {
                AssertFactoryCanMakeProgressFromAnotherThread(factory);
                return true;
            }
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            if (!checkEquality)
            {
                AssertFactoryCanMakeProgressFromAnotherThread(factory);
            }
            return 0;
        }
    }
}
