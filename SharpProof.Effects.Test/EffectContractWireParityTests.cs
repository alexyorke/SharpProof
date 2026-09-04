namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectContractWireParityTests
{
    private static readonly (
        EffectRegionKind Region,
        EffectContractKind Read,
        EffectContractKind Write,
        EffectRegionId? RegionId,
        bool ExpandsParameters)[] s_regionMappings =
    [
        (
            EffectRegionKind.Receiver,
            EffectContractKind.ReadsReceiverState,
            EffectContractKind.WritesReceiverState,
            (EffectRegionId?)EffectRegionId.Receiver,
            false),
        (
            EffectRegionKind.Parameter,
            EffectContractKind.ReadsArgumentState,
            EffectContractKind.WritesArgumentState,
            null,
            true),
        (
            EffectRegionKind.Captured,
            EffectContractKind.ReadsCapturedState,
            EffectContractKind.WritesCapturedState,
            (EffectRegionId?)EffectRegionId.Captured(0),
            false),
        (
            EffectRegionKind.Static,
            EffectContractKind.ReadsStaticState,
            EffectContractKind.WritesStaticState,
            (EffectRegionId?)EffectRegionId.Static(),
            false),
        (
            EffectRegionKind.Fresh,
            EffectContractKind.None,
            EffectContractKind.None,
            null,
            false),
        (
            EffectRegionKind.Ambient,
            EffectContractKind.ReadsAmbientState,
            EffectContractKind.WritesAmbientState,
            (EffectRegionId?)EffectRegionId.Ambient,
            false),
        (
            EffectRegionKind.Unknown,
            EffectContractKind.None,
            EffectContractKind.None,
            null,
            false)
    ];

    [Test]
    public void GeneratedEffectCatalogContainsNoAnalysisAlgorithms()
    {
        var source = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "SharpProof.Effects",
            "EffectContractMappings.generated.cs"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Not.Contain("if ("));
            Assert.That(source, Does.Not.Contain("switch"));
            Assert.That(source, Does.Not.Contain("foreach"));
        }
    }

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

    [TestCaseSource(nameof(RegionProjectionCases))]
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

    private static IEnumerable<TestCaseData> RegionProjectionCases()
    {
        return s_regionMappings.Select(static mapping => new TestCaseData(
            mapping.Region,
            mapping.Read,
            mapping.Write));
    }

    [Test]
    public void RegionCatalogIsClosedAndDrivesBothDirections()
    {
        Assert.That(
            EffectContractMappings.RegionContracts,
            Is.EqualTo(s_regionMappings));
        Assert.That(
            s_regionMappings.Select(static mapping => mapping.Region),
            Is.EqualTo(Enum.GetValues<EffectRegionKind>()));

        const int parameterCount = 3;
        foreach (var mapping in s_regionMappings)
        {
            var expectedRegions =
                mapping.ExpandsParameters
                    ? EffectContractMappings.ParameterRegions(parameterCount)
                    : mapping.RegionId is { } region
                        ? EffectRegionSet.Create(region)
                        : EffectRegionSet.Empty;
            var reads = EffectContractMappings.ToAnalysisRegions(
                mapping.Read,
                isWrite: false,
                parameterCount);
            var writes = EffectContractMappings.ToAnalysisRegions(
                mapping.Write,
                isWrite: true,
                parameterCount);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    reads,
                    Is.EqualTo(
                        mapping.Read == EffectContractKind.None
                            ? EffectRegionSet.Empty
                            : expectedRegions),
                    mapping.Region + " read");
                Assert.That(
                    writes,
                    Is.EqualTo(
                        mapping.Write == EffectContractKind.None
                            ? EffectRegionSet.Empty
                            : expectedRegions),
                    mapping.Region + " write");
            }
        }
    }

    [Test]
    public void RegionCatalogRejectsValuesOutsideItsClosedDomain()
    {
        Assert.That(
            (Action)(() => _ =
                EffectContractMappings.ToContractRegion(
                    (EffectRegionKind)int.MaxValue,
                    isWrite: false)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void DirectEventWireCatalogIsClosedAndBijective()
    {
        var expected = new[]
        {
            (
                EffectDirectEventKind.ManagedObjectAllocation,
                "managed-allocation"),
            (
                EffectDirectEventKind.ManagedArrayAllocation,
                "managed-array-allocation"),
            (
                EffectDirectEventKind.ExplicitThrow,
                "explicit-throw"),
            (
                EffectDirectEventKind.ReceiverFieldRead,
                "direct-field-read"),
            (
                EffectDirectEventKind.ReceiverFieldWrite,
                "direct-field-write"),
            (
                EffectDirectEventKind.MonitorCall,
                "synchronization-call"),
            (
                EffectDirectEventKind.EmptyLock,
                "synchronization-lock"),
            (
                EffectDirectEventKind.VolatileFieldAccess,
                "volatile-field-access")
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectDirectEventKinds.WireNames,
                Is.EqualTo(expected));
            Assert.That(
                expected.Select(static mapping => mapping.Item1),
                Is.EqualTo(Enum.GetValues<EffectDirectEventKind>()));
            Assert.That(
                expected.Select(static mapping => mapping.Item2),
                Is.Unique);
        }

        foreach (var mapping in expected)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    EffectDirectEventKinds.ToWireName(mapping.Item1),
                    Is.EqualTo(mapping.Item2));
                Assert.That(
                    EffectDirectEventKinds.FromWireName(mapping.Item2),
                    Is.EqualTo(mapping.Item1));
            }
        }
    }

    [Test]
    public void DirectEventWireCatalogRejectsValuesOutsideItsClosedDomain()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (Action)(() => _ =
                    EffectDirectEventKinds.ToWireName(
                        (EffectDirectEventKind)int.MaxValue)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                (Action)(() => _ =
                    EffectDirectEventKinds.FromWireName(
                        "managed-allocation-vNext")),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                (Action)(() => _ =
                    EffectDirectEventKinds.FromWireName(null!)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
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
