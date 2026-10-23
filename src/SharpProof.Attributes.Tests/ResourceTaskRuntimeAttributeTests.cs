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
internal sealed class ResourceTaskRuntimeAttributeTests
{
    private const string DeploymentHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Evidence = "deployment review: runtime resource premises";

    [TestCase(ResourceTaskRuntimeQuantity.RawTraceArrayAttemptsPerFaultEpisode)]
    [TestCase(ResourceTaskRuntimeQuantity.RawTracePayloadBytesPerArrayAttempt)]
    [TestCase(ResourceTaskRuntimeQuantity.ResolverArrayAttemptsPerFaultEpisode)]
    [TestCase(ResourceTaskRuntimeQuantity.ResolverSlotsPerArrayAttempt)]
    [TestCase(ResourceTaskRuntimeQuantity.FaultConstructionBytesPerFaultEpisode)]
    [TestCase(ResourceTaskRuntimeQuantity.ResolverReferentBytesPerFaultEpisode)]
    [TestCase(ResourceTaskRuntimeQuantity.SchedulingBytesPerChildStartAttempt)]
    [TestCase(ResourceTaskRuntimeQuantity.JoinBytesPerJoinAttempt)]
    [TestCase(ResourceTaskRuntimeQuantity.AwaitBytesPerConfiguredAwait)]
    [TestCase(ResourceTaskRuntimeQuantity.InitializationBytesPerInvocation)]
    [TestCase(ResourceTaskRuntimeQuantity.TotalFaultEpisodesPerInvocation)]
    public void EveryRuntimeQuantityRetainsExplicitCallerBounds(ResourceTaskRuntimeQuantity quantity)
    {
        foreach (var upperBound in new[] { 0L, 37L, long.MaxValue })
        {
            var attribute = new ResourceTaskRuntimeUpperBoundAttribute(quantity, upperBound, Evidence);
            Assert.That(attribute.Quantity, Is.EqualTo(quantity));
            Assert.That(attribute.UpperBound, Is.EqualTo(upperBound));
            Assert.That(attribute.Evidence, Is.EqualTo(Evidence));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeploymentRetainsItsProfileAndEvidenceAndCanonicalizesTheDigest(bool uppercase)
    {
        var digest = uppercase ? DeploymentHash.ToUpperInvariant() : DeploymentHash;
        var attribute = new ResourceTaskDeploymentAttribute(
            ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed, digest, Evidence);
        Assert.That(attribute.Profile, Is.EqualTo(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed));
        Assert.That(attribute.DeploymentManifestSha256, Is.EqualTo(DeploymentHash));
        Assert.That(attribute.Evidence, Is.EqualTo(Evidence));
    }

    [TestCase((ResourceTaskDeploymentProfile)(-1))]
    [TestCase(ResourceTaskDeploymentProfile.Unspecified)]
    [TestCase((ResourceTaskDeploymentProfile)2)]
    [TestCase((ResourceTaskDeploymentProfile)int.MaxValue)]
    public void DeploymentRejectsUnspecifiedAndUnsupportedProfiles(ResourceTaskDeploymentProfile profile)
    {
        Action construct = () => { _ = new ResourceTaskDeploymentAttribute(profile, DeploymentHash, Evidence); };
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(construct)!.ParamName, Is.EqualTo("profile"));
    }

    [TestCase((ResourceTaskRuntimeQuantity)(-1))]
    [TestCase(ResourceTaskRuntimeQuantity.Unspecified)]
    [TestCase((ResourceTaskRuntimeQuantity)12)]
    [TestCase((ResourceTaskRuntimeQuantity)int.MaxValue)]
    public void QuantitiesRejectUnspecifiedAndUndefinedValues(ResourceTaskRuntimeQuantity quantity)
    {
        Action construct = () => { _ = new ResourceTaskRuntimeUpperBoundAttribute(quantity, 0, Evidence); };
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(construct)!.ParamName, Is.EqualTo("quantity"));
    }

    [TestCase(-1L)]
    [TestCase(long.MinValue)]
    public void QuantitiesRejectNegativeCallerBounds(long upperBound)
    {
        Action construct = () =>
        {
            _ = new ResourceTaskRuntimeUpperBoundAttribute(ResourceTaskRuntimeQuantity.TotalFaultEpisodesPerInvocation, upperBound, Evidence);
        };
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(construct)!.ParamName, Is.EqualTo("upperBound"));
    }

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("whitespace")]
    [TestCase("short")]
    [TestCase("long")]
    [TestCase("nonhex")]
    [TestCase("control")]
    public void DeploymentRejectsMissingAndMalformedDigests(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        var digest = corruption switch
        {
            "null" => null,
            "empty" => string.Empty,
            "whitespace" => new string(' ', 64),
            "short" => DeploymentHash.Substring(0, 63),
            "long" => DeploymentHash + "0",
            "nonhex" => "g" + new string('a', 63),
            "control" => "\n" + new string('a', 63),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        Action construct = () =>
        {
            _ = new ResourceTaskDeploymentAttribute(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed, digest!, Evidence);
        };
        Assert.That(Assert.Catch<ArgumentException>(construct)!.ParamName, Is.EqualTo("deploymentManifestSha256"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("review\nreference")]
    [TestCase("review\treference")]
    [TestCase("review\0reference")]
    public void BothPremiseKindsRequirePrintableEvidence(string? evidence)
    {
        Action deployment = () =>
        {
            _ = new ResourceTaskDeploymentAttribute(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed, DeploymentHash, evidence!);
        };
        Action quantity = () =>
        {
            _ = new ResourceTaskRuntimeUpperBoundAttribute(ResourceTaskRuntimeQuantity.RawTraceArrayAttemptsPerFaultEpisode, 0, evidence!);
        };
        Assert.That(Assert.Throws<ArgumentException>(deployment)!.ParamName, Is.EqualTo("evidence"));
        Assert.That(Assert.Throws<ArgumentException>(quantity)!.ParamName, Is.EqualTo("evidence"));
    }

    [Test]
    public void EvidenceAcceptsThePrintableTextBoundaryAndRejectsLongerText()
    {
        var evidence = new string('e', 1024);
        var deployment = new ResourceTaskDeploymentAttribute(
            ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed, DeploymentHash, evidence);
        var quantity = new ResourceTaskRuntimeUpperBoundAttribute(ResourceTaskRuntimeQuantity.TotalFaultEpisodesPerInvocation, 0, evidence);
        Assert.That(deployment.Evidence, Is.EqualTo(evidence));
        Assert.That(quantity.Evidence, Is.EqualTo(evidence));
        Action longDeployment = () =>
        {
            _ = new ResourceTaskDeploymentAttribute(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed, DeploymentHash, evidence + "e");
        };
        Action longQuantity = () =>
        {
            _ = new ResourceTaskRuntimeUpperBoundAttribute(ResourceTaskRuntimeQuantity.TotalFaultEpisodesPerInvocation, 0, evidence + "e");
        };
        Assert.That(Assert.Throws<ArgumentException>(longDeployment)!.ParamName, Is.EqualTo("evidence"));
        Assert.That(Assert.Throws<ArgumentException>(longQuantity)!.ParamName, Is.EqualTo("evidence"));
    }

    [TestCase(typeof(ResourceTaskDeploymentAttribute), false)]
    [TestCase(typeof(ResourceTaskRuntimeUpperBoundAttribute), true)]
    public void PremisesAreImmutableMethodDeclarationsWithNoConstructorDefaults(Type attributeType, bool allowMultiple)
    {
        ArgumentNullException.ThrowIfNull(attributeType);
        var constructor = attributeType.GetConstructors().Single();
        Assert.That(constructor.GetParameters(), Has.Length.EqualTo(3));
        Assert.That(constructor.GetParameters().Any(static parameter => parameter is null
            ? throw new ArgumentNullException(nameof(parameter)) : parameter.IsOptional || parameter.HasDefaultValue), Is.False);
        Assert.That(attributeType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(static property => property is null ? throw new ArgumentNullException(nameof(property)) : property.CanWrite), Is.False);
        var usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();
        Assert.That(usage, Is.Not.Null);
        Assert.That(usage!.ValidOn, Is.EqualTo(AttributeTargets.Method));
        Assert.That(usage.Inherited, Is.False);
        Assert.That(usage.AllowMultiple, Is.EqualTo(allowMultiple));
    }
}
