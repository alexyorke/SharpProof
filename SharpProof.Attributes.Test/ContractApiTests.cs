using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Attributes.Test;

[TestFixture]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates test fixtures through reflection.")]
internal sealed class ContractApiTests
{
    [TestCase(nameof(Contract.Requires))]
    [TestCase(nameof(Contract.Ensures))]
    [TestCase(nameof(Contract.Assume))]
    public void ContractStatementsAreNormallyElidedFromIl(string methodName)
    {
        var method = typeof(Contract).GetMethod(methodName, [typeof(bool)]);

        Assert.That(method, Is.Not.Null);
        var conditional = method!.GetCustomAttributes(
                typeof(ConditionalAttribute),
                inherit: false)
            .Cast<ConditionalAttribute>()
            .Single();
        Assert.That(
            conditional.ConditionString,
            Is.EqualTo(Contract.ConditionalSymbol));
    }

    [Test]
    public void ContractValuePlaceholdersRejectRuntimeUse()
    {
        var resultException = Assert.Throws<InvalidOperationException>(
            (Action)(static () => _ = Contract.Result<int>()));
        var oldException = Assert.Throws<InvalidOperationException>(
            (Action)(static () => _ = Contract.Old(1)));

        Assert.That(
            resultException!.Message,
            Does.Contain("Contract.Ensures"));
        Assert.That(
            oldException!.Message,
            Does.Contain("Contract.Ensures"));
    }

    [Test]
    public void TrustAndSuppressionReasonsAreRequired()
    {
        Assert.Throws<ArgumentException>(
            (Action)(static () => _ = new SharpProofTrustedAttribute(" ")));
        Assert.Throws<ArgumentException>(
            (Action)(static () => _ = new SharpProofSuppressAttribute("")));

        Assert.That(
            new SharpProofTrustedAttribute("reviewed boundary").Reason,
            Is.EqualTo("reviewed boundary"));
        Assert.That(
            new SharpProofSuppressAttribute("tracked precision gap").Reason,
            Is.EqualTo("tracked precision gap"));
    }

    [Test]
    public void ClosedRangeRejectsAnInvertedBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(static () => _ = new InRangeAttribute(2, 1)));
        var range = new InRangeAttribute(-1, 3);
        Assert.That(range.Minimum, Is.EqualTo(-1));
        Assert.That(range.Maximum, Is.EqualTo(3));
    }

    [Test]
    public void ClosedContractsTargetOnlyParametersAndReturns()
    {
        foreach (var type in new[] {
                     typeof(NotNullAttribute),
                     typeof(PositiveAttribute),
                     typeof(InRangeAttribute)
                 })
        {
            Assert.That(
                type.GetCustomAttribute<AttributeUsageAttribute>()!.ValidOn,
                Is.EqualTo(
                    AttributeTargets.Parameter |
                    AttributeTargets.ReturnValue));
        }

        Assert.That(
            typeof(Contract).Assembly.GetType(
                "SharpProof.Attributes.PureAttribute"),
            Is.Null);
    }

    [Test]
    public void PublicEffectVocabularyContainsNoAnalysisState()
    {
        var names = Enum.GetNames<SharpProofEffect>();

        Assert.That(names, Does.Not.Contain("Unknown"));
        Assert.That(names, Does.Not.Contain("DirectCall"));
        Assert.That(names, Does.Not.Contain("DispatchUncertainty"));
        Assert.That(names, Does.Not.Contain("UnsupportedOperation"));
        Assert.That(names, Does.Not.Contain("BudgetExhaustion"));
        Assert.That(names, Does.Not.Contain("WritesFreshOwnedState"));
        Assert.That(names, Has.Length.LessThanOrEqualTo(17));
    }

    [Test]
    public void EffectContractsUseOnlyClosedTypedArguments()
    {
        Assert.That(
            typeof(EffectContractAttribute).GetConstructors()
                .SelectMany(static constructor => constructor.GetParameters()),
            Has.None.Property(nameof(System.Reflection.ParameterInfo.ParameterType))
                .EqualTo(typeof(string)));
        Assert.That(
            typeof(EffectContractAttribute).GetProperties()
                .Where(static property => property.DeclaringType ==
                    typeof(EffectContractAttribute))
                .Select(static property => property.PropertyType),
            Has.None.EqualTo(typeof(string)));
        Assert.That(
            typeof(EffectContractAttribute).GetCustomAttributes(
                    typeof(AttributeUsageAttribute),
                    inherit: false)
                .Cast<AttributeUsageAttribute>()
                .Single()
                .ValidOn,
            Is.EqualTo(
                AttributeTargets.Method |
                AttributeTargets.Constructor |
                AttributeTargets.Property));
    }

    [Test]
    public void EffectContractsDefaultToPartialConservativeEvidence()
    {
        var contract = new EffectContractAttribute(SharpProofEffect.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                contract.Capabilities,
                Is.EqualTo(SharpProofCapability.None));
            Assert.That(contract.ThrownExceptions, Is.Empty);
            Assert.That(contract.IsDeterministic, Is.False);
            Assert.That(contract.Complete, Is.False);
            Assert.That(contract.PreconditionFree, Is.False);
        }
    }

}
