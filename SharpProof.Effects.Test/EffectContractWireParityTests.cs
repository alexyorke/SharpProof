namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectContractWireParityTests {
    [TestCase(
        typeof(SharpProof.Attributes.SharpProofEffect),
        typeof(EffectContractKind))]
    [TestCase(
        typeof(SharpProof.Attributes.SharpProofCapability),
        typeof(EffectContractCapabilityKind))]
    public void NeutralFlagsMatchThePublicAttributeWireVocabulary(
        Type attributeFlags,
        Type neutralFlags) {
        ArgumentNullException.ThrowIfNull(attributeFlags);
        ArgumentNullException.ThrowIfNull(neutralFlags);
        using (Assert.EnterMultipleScope()) {
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
    public void DecoderMasksCoverExactlyTheNeutralVocabulary() {
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
    public void MetadataConstantsMatchThePublicAttributeShape() {
        using (Assert.EnterMultipleScope()) {
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

    private static Dictionary<string, long> GetNamedValues(Type enumType) =>
        Enum.GetNames(enumType).ToDictionary(
            static name => name,
            name => Convert.ToInt64(
                Enum.Parse(enumType, name),
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparer.Ordinal);
}
