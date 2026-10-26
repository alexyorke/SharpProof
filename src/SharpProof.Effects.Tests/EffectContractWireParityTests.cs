using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects.Test;

[TestFixture]
internal sealed class EffectContractWireParityTests
{
    private static readonly ImmutableArray<(
        EffectRegionKind Region,
        EffectContractKind Read,
        EffectContractKind Write,
        EffectRegionId? RegionId,
        bool ExpandsParameters)> s_regionMappings =
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
    public void GeneratedEffectCatalogContainsOnlyDeclarationsAndData()
    {
        var source = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            "src/SharpProof.Compiler.Semantics/Effects",
            "EffectContractMappings.generated.cs"));
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));
        var parseErrors = syntaxTree.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        var executableSyntax = FindExecutableCatalogSyntax(syntaxTree.GetRoot());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parseErrors, Is.Empty, string.Join(
                Environment.NewLine,
                parseErrors.Select(static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.ToString())));
            Assert.That(
                executableSyntax,
                Is.Empty,
                string.Join(
                    ", ",
                    executableSyntax.Select(node =>
                        node is null ? throw new ArgumentNullException(nameof(node)) : $"{node.Kind()}@{node.GetLocation().GetLineSpan().StartLinePosition}")));

            var executableProbe = CSharpSyntaxTree.ParseText("""
                internal static class ExecutableProbe
                {
                    internal static bool IsParameter(int value) => value == 1;

                    internal static bool HasBranch(int value)
                    {
                        if(value == 1) return true;
                        return false;
                    }
                }
                """, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
            var probeSyntax = FindExecutableCatalogSyntax(executableProbe);
            Assert.That(
                probeSyntax.OfType<MethodDeclarationSyntax>()
                    .Any(method => method.ExpressionBody is not null),
                Is.True);
            Assert.That(
                probeSyntax.OfType<IfStatementSyntax>().ToImmutableArray(),
                Has.Length.EqualTo(1));
        }
    }

    private static ImmutableArray<SyntaxNode> FindExecutableCatalogSyntax(SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.DescendantNodesAndSelf()
            .Where(node =>
                node is null ? throw new ArgumentNullException(nameof(node)) : node is BaseMethodDeclarationSyntax
                    or LocalFunctionStatementSyntax
                    or AnonymousFunctionExpressionSyntax
                    or StatementSyntax
                    or ConditionalExpressionSyntax
                    or SwitchExpressionSyntax
                    or ThrowExpressionSyntax
                    or QueryExpressionSyntax
                || node is PropertyDeclarationSyntax property && property.ExpressionBody is not null
                || node is AccessorDeclarationSyntax accessor &&
                    (accessor.Body is not null || accessor.ExpressionBody is not null)
                || node is InvocationExpressionSyntax invocation && !IsCatalogDataFactory(invocation))
            .ToImmutableArray();
    }

    private static bool IsCatalogDataFactory(InvocationExpressionSyntax invocation)
    {
        var field = invocation.Ancestors().OfType<FieldDeclarationSyntax>().FirstOrDefault();
        return field is not null
            && field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))
            && field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ReadOnlyKeyword))
            && invocation.Ancestors().OfType<EqualsValueClauseSyntax>().Any()
            && invocation.Expression is MemberAccessExpressionSyntax member
            && member.Expression is IdentifierNameSyntax owner
            && owner.Identifier.ValueText == "EffectRegionId"
            && member.Name.Identifier.ValueText is "Captured" or "Static";
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
        Assert.That(
            EffectContractMetadata.AllIncompleteReasons,
            Is.EqualTo(Enum.GetValues<EffectAnalysisIncompleteReason>().Aggregate(
                EffectAnalysisIncompleteReason.None,
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
                    Is.EqualTo(expectedRegions),
                    mapping.Region + " read");
                Assert.That(
                    writes,
                    Is.EqualTo(expectedRegions),
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
        var expected = ImmutableArray.Create(
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
        );

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

    private static ImmutableDictionary<string, long> GetNamedValues(Type enumType)
    {
        System.ArgumentNullException.ThrowIfNull(enumType);
        var values = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
        foreach (var name in Enum.GetNames(enumType))
        {
            ArgumentNullException.ThrowIfNull(name);
            var value = Convert.ToInt64(Enum.Parse(enumType, name), System.Globalization.CultureInfo.InvariantCulture);
            if (values.ContainsKey(name))
            { throw new ArgumentException("An item with the same key has already been added. Key: " + name); }
            values.Add(name, value);
        }
        return values.ToImmutable();
    }

}
