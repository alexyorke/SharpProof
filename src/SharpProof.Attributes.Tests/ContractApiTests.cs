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
        System.ArgumentNullException.ThrowIfNull(methodName);
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

    [TestCase(nameof(Contract.Requires))]
    [TestCase(nameof(Contract.Ensures))]
    [TestCase(nameof(Contract.Assume))]
    public void ContractStatementsRemainRuntimeInertWhenInvokedThroughReflection(
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        var method = typeof(Contract).GetMethod(methodName, [typeof(bool)]);

        Assert.That(method, Is.Not.Null);
        Assert.DoesNotThrow((Action)(() =>
        {
            _ = method!.Invoke(null, [false]);
        }));
    }

    [Test]
    public void ClosedContractAttributesAreDiscoverableOnParametersAndReturns()
    {
        var method = typeof(ContractApiTests).GetMethod(
            nameof(ClosedContractSample),
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Assert.That(
            method!.GetParameters().Single().GetCustomAttributes(),
            Has.Exactly(1).TypeOf<NotNullAttribute>() &
            Has.Exactly(1).TypeOf<PositiveAttribute>() &
            Has.Exactly(1).TypeOf<InRangeAttribute>());
        Assert.That(
            method.ReturnParameter.GetCustomAttributes(),
            Has.Exactly(1).TypeOf<NotNullAttribute>());
    }

    [Test]
    public void EffectRequirementAttributesAreDiscoverableOnMethods()
    {
        var method = typeof(ContractApiTests).GetMethod(
            nameof(EffectRequirementSample),
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Assert.That(
            method!.GetCustomAttributes(),
            Has.Exactly(1).TypeOf<DoesNotThrowAttribute>() &
            Has.Exactly(1).TypeOf<EnforcePureAttribute>() &
            Has.Exactly(1).TypeOf<ZeroAllocationsAttribute>());
    }

    [return: NotNull]
    private static string ClosedContractSample(
        [NotNull, Positive, InRange(0, 10)] int value)
    {
        if (value is <= 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [DoesNotThrow, EnforcePure, ZeroAllocations]
    private static int EffectRequirementSample()
    {
        return 0;
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
    public void CompanionAndAllowanceAttributesPreserveTheirTypedArguments()
    {
        Type[] exceptions = [typeof(IOException), typeof(InvalidOperationException)];
        var capabilities = SharpProofCapability.Console | SharpProofCapability.FileRead;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new AllowedExceptionsAttribute(exceptions).ExceptionTypes, Is.SameAs(exceptions));
            Assert.That(new AllowedExceptionsAttribute().ExceptionTypes, Is.Empty);
            Assert.That(new AllowedCapabilitiesAttribute(capabilities).Capabilities, Is.EqualTo(capabilities));
            Assert.That(new ContractForAttribute(typeof(IDisposable)).TargetType, Is.EqualTo(typeof(IDisposable)));
            Assert.That(Assert.Throws<ArgumentNullException>((Action)(() =>
                _ = new AllowedExceptionsAttribute(null!)))!.ParamName, Is.EqualTo("exceptionTypes"));
            Assert.That(Assert.Throws<ArgumentNullException>((Action)(() =>
                _ = new ContractForAttribute(null!)))!.ParamName, Is.EqualTo("targetType"));
        }
    }

    [Test]
    public void EffectFlagsRejectUnknownBits()
    {
        Assert.That(new AllowedCapabilitiesAttribute(SharpProofCapability.None).Capabilities,
            Is.EqualTo(SharpProofCapability.None));
        Assert.That(new EffectContractAttribute(SharpProofEffect.None).Effects,
            Is.EqualTo(SharpProofEffect.None));
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(static () => _ = new AllowedCapabilitiesAttribute((SharpProofCapability)(1 << 30))))!.ParamName,
            Is.EqualTo("capabilities"));
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(static () => _ = new EffectContractAttribute((SharpProofEffect)(1L << 30))))!.ParamName,
            Is.EqualTo("effects"));
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
                .SelectMany(static constructor => constructor is null ? throw new System.ArgumentNullException(nameof(constructor)) : (constructor.GetParameters())),
            Has.None.Property(nameof(System.Reflection.ParameterInfo.ParameterType))
                .EqualTo(typeof(string)));
        Assert.That(
            typeof(EffectContractAttribute).GetProperties()
                .Where(static property => property is null ? throw new System.ArgumentNullException(nameof(property)) : (property.DeclaringType ==
                    typeof(EffectContractAttribute)))
                .Select(static property => property is null ? throw new System.ArgumentNullException(nameof(property)) : (property.PropertyType)),
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

    [TestCase(ResourceMetric.LoopBodyEntries)]
    [TestCase(ResourceMetric.ArrayAllocations)]
    [TestCase(ResourceMetric.AllocatedElements)]
    [TestCase(ResourceMetric.AllocatedBytes)]
    public void ResourceContractsPreserveDeclaredConstantAndLinearTerms(ResourceMetric metric)
    {
        var constant = new ResourceUpperBoundAttribute(metric, 3);
        Assert.That(constant.Metric, Is.EqualTo(metric));
        Assert.That(constant.Constant, Is.EqualTo(3));
        Assert.That(constant.SizeParameter, Is.Null);
        Assert.That(constant.Coefficient, Is.Zero);
        var linear = new ResourceUpperBoundAttribute(metric, 2, "values.Length", 4);
        Assert.That(linear.Metric, Is.EqualTo(metric));
        Assert.That(linear.Constant, Is.EqualTo(2));
        Assert.That(linear.SizeParameter, Is.EqualTo("values.Length"));
        Assert.That(linear.Coefficient, Is.EqualTo(4));
    }

    [TestCase(0, 0, 0)]
    [TestCase(99, 0, 0)]
    [TestCase(1, -1, 0)]
    [TestCase(1, 0, -1)]
    public void ResourceContractsRejectUndefinedMetricsAndNegativeTerms(int metric, long constant, long coefficient)
    {
        Action construct = () => _ = new ResourceUpperBoundAttribute((ResourceMetric)metric, constant, coefficient: coefficient);
        Assert.Throws<ArgumentOutOfRangeException>(construct);
    }

    [TestCase(null, 1)]
    [TestCase("", 1)]
    [TestCase(" ", 1)]
    [TestCase("n", 0)]
    public void ResourceContractsRequireASizeExactlyWhenTheCoefficientIsPositive(string? size, long coefficient)
    {
        Action construct = () => _ = new ResourceUpperBoundAttribute(ResourceMetric.ArrayAllocations, 0, size, coefficient);
        Assert.Throws<ArgumentException>(construct);
    }
}
