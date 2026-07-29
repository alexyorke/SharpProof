namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectContractWireParityTests
{
    [TestCase(
        typeof(SharpProof.Attributes.SharpProofEffect),
        typeof(EffectContractKind))]
    [TestCase(
        typeof(SharpProof.Attributes.SharpProofCapability),
        typeof(EffectContractCapabilityKind))]
    public void NeutralFlagsMatchThePublicAttributeWireVocabulary(
        Type attributeFlags,
        Type neutralFlags)
    {
        ArgumentNullException.ThrowIfNull(attributeFlags);
        ArgumentNullException.ThrowIfNull(neutralFlags);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                neutralFlags.GetEnumUnderlyingType(),
                Is.EqualTo(attributeFlags.GetEnumUnderlyingType()));
            Assert.That(
                neutralFlags.IsDefined(typeof(FlagsAttribute), inherit: false),
                Is.EqualTo(attributeFlags.IsDefined(
                    typeof(FlagsAttribute),
                    inherit: false)));
            Assert.That(
                GetNamedValues(neutralFlags),
                Is.EqualTo(GetNamedValues(attributeFlags)));
        }
    }

    [Test]
    public void DecoderMasksCoverExactlyTheNeutralVocabulary()
    {
        Assert.That(
            EffectContractMetadata.AllEffects,
            Is.EqualTo(Enum.GetValues<EffectContractKind>().Aggregate(
                EffectContractKind.None,
                static (all, value) => all | value)));
        Assert.That(
            EffectContractMetadata.AllCapabilities,
            Is.EqualTo(Enum.GetValues<EffectContractCapabilityKind>().Aggregate(
                EffectContractCapabilityKind.None,
                static (all, value) => all | value)));
    }

    [Test]
    public void MetadataConstantsMatchThePublicAttributeShape()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectContractMetadata.AttributeMetadataName,
                Is.EqualTo(typeof(EffectContractAttribute).FullName));
            Assert.That(
                EffectContractMetadata.TrustedAttributeMetadataName,
                Is.EqualTo(typeof(SharpProofTrustedAttribute).FullName));
            Assert.That(
                EffectContractMetadata.CapabilitiesPropertyName,
                Is.EqualTo(nameof(EffectContractAttribute.Capabilities)));
            Assert.That(
                EffectContractMetadata.CompletePropertyName,
                Is.EqualTo(nameof(EffectContractAttribute.Complete)));
            Assert.That(
                EffectContractMetadata.IsDeterministicPropertyName,
                Is.EqualTo(nameof(EffectContractAttribute.IsDeterministic)));
            Assert.That(
                EffectContractMetadata.ThrownExceptionsPropertyName,
                Is.EqualTo(nameof(EffectContractAttribute.ThrownExceptions)));
        }
    }

    [Test]
    public void CapabilityConversionsExhaustivelyRoundTripNamedFlags()
    {
        foreach (var contract in Enum.GetValues<EffectContractCapabilityKind>())
        {
            var analysis =
                EffectContractMappings.ToAnalysisCapabilities(contract);
            Assert.That(
                EffectContractMappings.ToContractCapabilities(analysis),
                Is.EqualTo(contract),
                contract.ToString());
        }
    }

    [Test]
    public void CapabilityConversionsRejectValuesOutsideTheirNamedDomain()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (Action)(() => _ =
                    EffectContractMappings.ToAnalysisCapabilities(
                        (EffectContractCapabilityKind)(1 << 13))),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                (Action)(() => _ =
                    EffectContractMappings.ToContractCapabilities(
                        EffectCapabilityKind.Unknown)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    [TestCase(EffectRegionKind.Receiver,
        EffectContractKind.ReadsReceiverState,
        EffectContractKind.WritesReceiverState)]
    [TestCase(EffectRegionKind.Parameter,
        EffectContractKind.ReadsArgumentState,
        EffectContractKind.WritesArgumentState)]
    [TestCase(EffectRegionKind.Captured,
        EffectContractKind.ReadsCapturedState,
        EffectContractKind.WritesCapturedState)]
    [TestCase(EffectRegionKind.Static,
        EffectContractKind.ReadsStaticState,
        EffectContractKind.WritesStaticState)]
    [TestCase(EffectRegionKind.Ambient,
        EffectContractKind.ReadsAmbientState,
        EffectContractKind.WritesAmbientState)]
    [TestCase(EffectRegionKind.Fresh,
        EffectContractKind.None,
        EffectContractKind.None)]
    [TestCase(EffectRegionKind.Unknown,
        EffectContractKind.None,
        EffectContractKind.None)]
    public void RegionProjectionUsesAnExplicitNamedMapping(
        EffectRegionKind region,
        EffectContractKind expectedRead,
        EffectContractKind expectedWrite)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectContractMappings.ToContractRegion(
                    region,
                    isWrite: false),
                Is.EqualTo(expectedRead));
            Assert.That(
                EffectContractMappings.ToContractRegion(
                    region,
                    isWrite: true),
                Is.EqualTo(expectedWrite));
        }
    }

    [Test]
    public void EvidenceUsesOnlyValidatedNamedEnumValues()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectContractMappings.EvidenceName(EffectContractMetadata.AllEffects),
                Does.Not.Match(@"^-?\d"));
            Assert.That(
                EffectContractMappings.EvidenceName(EffectContractMetadata.AllCapabilities),
                Does.Not.Match(@"^-?\d"));
            Assert.That(
                (Action)(() => _ = EffectContractMappings.EvidenceName(
                    (EffectAllocationKind)(1 << 2))),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                (Action)(() => _ = EffectContractMappings.EvidenceName(DayOfWeek.Monday)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    private static Dictionary<string, long> GetNamedValues(Type enumType)
    {
        return Enum.GetNames(enumType).ToDictionary(
            static name => name,
            name => Convert.ToInt64(
                Enum.Parse(enumType, name),
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparer.Ordinal);
    }
}
