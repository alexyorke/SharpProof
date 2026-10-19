using IrTypeId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrTypeTag>;
using System.Collections.Immutable;
using System.Numerics;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceExpressionOwnershipTests
{
    private static CompilerTotalCallablePreparation Callable(IrFactory factory, params IrTypeId[] types)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(types);
        var parameters = ImmutableArray.CreateBuilder<CompilerTotalParameter>();
        foreach (var type in types)
        {
            if (type.IsDefault)
            { throw new ArgumentException("Type must be initialized.", nameof(types)); }
            parameters.Add(new(factory.CreateVariable("entry", type), factory.CreateVariable("current", type), factory.CreateVariable("old", type)));
        }
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Return(block, factory.CreateOperation("return"));
        return new("owned", builder.Build(), parameters.ToImmutable(), null, []);
    }

    private static ResourceExpression Input(int ordinal, ResourceInputKind kind, BigInteger maximum)
    {
        return ResourceExpression.InputSize(new ResourceInput(ordinal, kind, "label", maximum));
    }

    private static ResourceExpression Decode(ResourceExpression expression, CompilerTotalCallablePreparation callable, string owner = "owned")
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(callable);
        ArgumentNullException.ThrowIfNull(owner);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        return ResourceExpressionCodec.DecodeForCallable(ResourceExpressionCodec.Encode(expression, cancellation), owner, callable, cancellation);
    }

    [Test]
    public void InputsBindToOwnedScalarArrayAndStringTypes()
    {
        var factory = new IrFactory();
        var int32 = factory.GetOrCreateIntegerType(32, true);
        var callable = Callable(factory, int32, factory.GetOrCreateIntegerType(64, true), factory.GetOrCreateSequenceType(int32), factory.StringType);
        var expression = ResourceExpression.Add(Input(0, ResourceInputKind.NonNegativeScalar, int.MaxValue),
            ResourceExpression.Add(Input(1, ResourceInputKind.NonNegativeScalar, long.MaxValue),
            ResourceExpression.Add(Input(2, ResourceInputKind.ArrayLength, int.MaxValue), Input(3, ResourceInputKind.StringLength, int.MaxValue))));
        Assert.That(Decode(expression, callable), Is.EqualTo(expression));
        Assert.That(Decode(ResourceExpression.One, callable), Is.EqualTo(ResourceExpression.One));
    }

    [Test]
    public void WrongOwnerOrdinalKindAndDomainAreRejected()
    {
        var factory = new IrFactory();
        var callable = Callable(factory, factory.GetOrCreateIntegerType(32, true));
        Action wrongOwner = () => Decode(ResourceExpression.One, callable, "other");
        Action wrongOrdinal = () => Decode(Input(1, ResourceInputKind.NonNegativeScalar, int.MaxValue), callable);
        Action wrongKind = () => Decode(Input(0, ResourceInputKind.ArrayLength, int.MaxValue), callable);
        Action narrowedDomain = () => Decode(Input(0, ResourceInputKind.NonNegativeScalar, 10), callable);
        Action widenedDomain = () => Decode(Input(0, ResourceInputKind.NonNegativeScalar, long.MaxValue), callable);
        Assert.Throws<InvalidDataException>(wrongOwner);
        Assert.Throws<InvalidDataException>(wrongOrdinal);
        Assert.Throws<InvalidDataException>(wrongKind);
        Assert.Throws<InvalidDataException>(narrowedDomain);
        Assert.Throws<InvalidDataException>(widenedDomain);
    }

    [Test]
    public void UnsupportedIntegerDomainsDoNotBorrowInt32Bounds()
    {
        var factory = new IrFactory();
        foreach (var type in new[] { factory.IntegerType, factory.BooleanType, factory.GetOrCreateIntegerType(32, false), factory.GetOrCreateIntegerType(16, true) })
        {
            var callable = Callable(factory, type);
            Action decode = () => Decode(Input(0, ResourceInputKind.NonNegativeScalar, int.MaxValue), callable);
            Assert.Throws<InvalidDataException>(decode);
        }
    }

    [Test]
    public void ReceiverAndAbstractBodyCannotSupplyResourceInputs()
    {
        var factory = new IrFactory();
        var callable = Callable(factory, factory.GetOrCreateIntegerType(32, true));
        Action receiver = () => Decode(Input(0, ResourceInputKind.NonNegativeScalar, int.MaxValue), callable with { HasReceiver = true });
        Action abstractBody = () => Decode(ResourceExpression.One, callable with { IsBodyAbstraction = true });
        Action missingReceiver = () => Decode(ResourceExpression.One, Callable(factory) with { HasReceiver = true });
        Assert.Throws<InvalidDataException>(receiver);
        Assert.Throws<InvalidDataException>(abstractBody);
        Assert.Throws<InvalidDataException>(missingReceiver);
    }

    [Test]
    public void ForeignDuplicateAndInconsistentParameterMapsAreRejected()
    {
        var factory = new IrFactory();
        var callable = Callable(factory, factory.GetOrCreateIntegerType(32, true));
        var parameter = callable.Parameters[0];
        var foreign = new IrFactory();
        Action wrongFactory = () => Decode(ResourceExpression.One, callable with
        { Parameters = [parameter with { Entry = foreign.CreateVariable("entry", foreign.IntegerType) }] });
        Action duplicate = () => Decode(ResourceExpression.One, callable with { Parameters = [parameter, parameter] });
        Action inconsistent = () => Decode(ResourceExpression.One, callable with
        { Parameters = [parameter with { Current = factory.CreateVariable("wrong", factory.StringType) }] });
        Action missing = () => Decode(ResourceExpression.One, callable with { Parameters = default });
        Assert.Throws<InvalidDataException>(wrongFactory);
        Assert.Throws<InvalidDataException>(duplicate);
        Assert.Throws<InvalidDataException>(inconsistent);
        Assert.Throws<InvalidDataException>(missing);
    }

    [Test]
    public void OwnershipValidationHonorsCancellation()
    {
        var factory = new IrFactory();
        var callable = Callable(factory);
        Action validate = () => ResourceExpressionOwnership.Validate(ResourceExpression.One, "owned", callable, new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(validate);
    }
}
