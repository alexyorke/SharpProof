[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath = 'artifacts\mutation\summary.json',

    [string[]]$MutationName = @(),

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit,

    [switch]$KeepWorkspace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationEvidence.psm1') -Force

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
if (-not $output.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the repository: $output"
}

$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
    throw 'Unable to resolve the mutation source commit.'
}
if ($ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw "ExpectedCommit must be a 40-character commit SHA: '$ExpectedCommit'."
}
if ($sourceCommit -ne $ExpectedCommit) {
    throw "Mutation source commit '$sourceCommit' does not match '$ExpectedCommit'."
}

& git -C $repositoryRoot diff --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked working tree.'
}
& git -C $repositoryRoot diff --cached --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked index.'
}

$mutations = @(
    [pscustomobject]@{
        Name = 'scalar-int32-upper-bound'
        File = 'SharpProof.Frontend\CSharpScalarSemantics.generated.cs'
        Original = 'new(SpecialType.System_Int32, true, 32, -2147483648L, 2147483647L),'
        Mutated = 'new(SpecialType.System_Int32, true, 32, -2147483648L, 2147483646L),'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~SupportedIntegerCatalogIsExactAndExhaustive'
    },
    [pscustomobject]@{
        Name = 'scalar-binary-reverse-relation'
        File = 'SharpProof.Frontend\CSharpScalarSemantics.generated.cs'
        Original = 'new(BinaryOperatorKind.LessThan, IrBinaryOperator.LessThan, reverseKind: BinaryOperatorKind.GreaterThan, negatedKind: BinaryOperatorKind.GreaterThanOrEqual),'
        Mutated = 'new(BinaryOperatorKind.LessThan, IrBinaryOperator.LessThan, reverseKind: BinaryOperatorKind.GreaterThanOrEqual, negatedKind: BinaryOperatorKind.GreaterThanOrEqual),'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~BinaryReverseAndNegationRelationsAreExactAndInvolutive'
    },
    [pscustomobject]@{
        Name = 'scalar-binary-inverse-mapping'
        File = 'SharpProof.Frontend\CSharpScalarSemantics.generated.cs'
        Original = '            if (candidate.IrOperator == @operator)'
        Mutated = '            if (candidate.IrOperator != @operator)'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~BinaryMappingsAndArithmeticCategoriesAreExhaustive'
    },
    [pscustomobject]@{
        Name = 'scalar-ir-binary-key'
        File = 'SharpProof.Ir\IrOperatorCatalog.generated.cs'
        Original = '            IrBinaryOperator.LessThan => (9, IrTypeKind.Integer, IrTypeKind.Boolean, "<"),'
        Mutated = '            IrBinaryOperator.LessThan => (8, IrTypeKind.Integer, IrTypeKind.Boolean, "<"),'
        Project = 'SharpProof.Ir.Test\SharpProof.Ir.Test.csproj'
        Filter = 'FullyQualifiedName~BinaryMetadataIsExactAndExhaustive'
    },
    [pscustomobject]@{
        Name = 'scalar-ir-binary-enum-value'
        File = 'SharpProof.Ir\IrOperatorCatalog.generated.cs'
        Original = '    LessThan = 9,'
        Mutated = '    LessThan = 14,'
        Project = 'SharpProof.Ir.Test\SharpProof.Ir.Test.csproj'
        Filter = 'FullyQualifiedName~BinaryMetadataIsExactAndExhaustive'
    },
    [pscustomobject]@{
        Name = 'portable-codec-unknown-wire-fails-closed'
        File = 'SharpProof.CompilerArtifact\PortableIrGraphCodec.cs'
        Original = (@'
        return value >= 0 && value < values.Length
            ? values[value]
            : throw Bad("Portable IR contains an unknown enum value.");
'@).Trim()
        Mutated = (@'
        return value >= 0 && value < values.Length
            ? values[value]
            : value == 999
                ? values[0]
                : throw Bad("Portable IR contains an unknown enum value.");
'@).Trim()
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DecoderRejectsUnknownWireEnumCodes'
    },
    [pscustomobject]@{
        Name = 'portable-schema-slot-role-fails-closed'
        File = 'SharpProof.CompilerArtifact\CompilerArtifactModel.schema.json'
        Original = '{ "kind": "Boolean", "slots": ["booleanValue",'
        Mutated = '{ "kind": "Boolean", "slots": ["unsupported",'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SchemaPinsEnvelopeWireCatalogsAndEffectEvidenceDomain'
    },
    [pscustomobject]@{
        Name = 'portable-codec-havoc-order-fails-closed'
        File = 'SharpProof.CompilerArtifact\PortableIrGraphCodec.cs'
        Original = '                    index > previous,'
        Mutated = '                    index >= previous,'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DecoderRejectsMalformedGraphs'
    },
    [pscustomobject]@{
        Name = 'portable-codec-whitespace-name-fails-closed'
        File = 'SharpProof.CompilerArtifact\PortableIrGraphCodec.cs'
        Original = '                value == null || !string.IsNullOrWhiteSpace(value),'
        Mutated = '                value == null || !string.IsNullOrEmpty(value),'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DecoderRejectsMalformedGraphs'
    },
    [pscustomobject]@{
        Name = 'portable-codec-unused-slot-fails-closed'
        File = 'SharpProof.CompilerArtifact\PortableIrGraphCodec.cs'
        Original = "                row.A,`n                row.B,`n                row.C,`n                row.D,"
        Mutated = "                row.A,`n                -1,`n                row.C,`n                row.D,"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DecoderRejectsNonCanonicalSlotsAfterSerialization'
    },
    [pscustomobject]@{
        Name = 'portable-codec-metadata-optional-type-index'
        File = 'SharpProof.CompilerArtifact\PortableIrModel.generated.cs'
        Original = '                value.ElementType.HasValue ? TypeIndex(value.ElementType.Value) : -1);'
        Mutated = '                -1);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~MetadataRowsProjectEveryDeclaredValue'
    },
    [pscustomobject]@{
        Name = 'portable-codec-metadata-member-identity'
        File = 'SharpProof.CompilerArtifact\PortableIrModel.generated.cs'
        Original = '                _identities.Add(value.Identity),'
        Mutated = '                -1,'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~MetadataRowsProjectEveryDeclaredValue'
    },
    [pscustomobject]@{
        Name = 'portable-codec-metadata-parameter-types'
        File = 'SharpProof.CompilerArtifact\PortableIrModel.generated.cs'
        Original = '                [.. value.ParameterTypes.Select(TypeIndex)]);'
        Mutated = '                []);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~MetadataRowsProjectEveryDeclaredValue'
    },
    [pscustomobject]@{
        Name = 'portable-codec-metadata-operation-description'
        File = 'SharpProof.CompilerArtifact\PortableIrModel.generated.cs'
        Original = '                value.Description.HasValue ? _factory.GetString(value.Description.Value) : null);'
        Mutated = '                null);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~MetadataRowsProjectEveryDeclaredValue'
    },
    [pscustomobject]@{
        Name = 'collector-option-output-kind'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerWireMappings.generated.cs'
        Original = '            OutputKind.ConsoleApplication => CompilerOutputKind.ConsoleApplication,'
        Mutated = '            OutputKind.ConsoleApplication => CompilerOutputKind.WindowsApplication,'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~EveryCurrentRoslynCompilerOptionHasAClosedWireMapping'
    },
    [pscustomobject]@{
        Name = 'collector-identity-comparer'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerWireMappings.generated.cs'
        Original = '            return CompilerAssemblyIdentityComparer.Default;'
        Mutated = '            return CompilerAssemblyIdentityComparer.Desktop;'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~EveryCurrentRoslynCompilerOptionHasAClosedWireMapping'
    },
    [pscustomobject]@{
        Name = 'collector-effect-flag-projection'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerWireMappings.generated.cs'
        Original = '            result |= WorkerEffectSet.ReadsReceiverState;'
        Mutated = '            result |= WorkerEffectSet.ReadsArgumentState;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~EffectWireMappingsAreNamedAndExhaustive'
    },
    [pscustomobject]@{
        Name = 'lowering-unchecked-arithmetic'
        File = 'SharpProof.Frontend\RoslynOperationLowerer.cs'
        Original = "CSharpScalarSemantics.RequiresCheckedArithmetic(`n                        operation.OperatorKind) && !operation.IsChecked)"
        Mutated = "CSharpScalarSemantics.RequiresCheckedArithmetic(`n                        operation.OperatorKind) && operation.IsChecked)"
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~OverflowAndConversionShapesAreExactOnlyWhenRepresentable'
    },
    [pscustomobject]@{
        Name = 'lowering-global-constant-bypass'
        File = 'SharpProof.Frontend\RoslynOperationLowerer.cs'
        Original = "            if (_owner._allowCompilerConstants &&`n                operation.ConstantValue.HasValue)"
        Mutated = "            if (_owner._allowCompilerConstants ||`n                operation.ConstantValue.HasValue)"
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~ConstantFoldingCannotBypassTheClosedOperationCatalog'
    },
    [pscustomobject]@{
        Name = 'requires-concrete-compiler-constant-replay'
        File = 'SharpProof.Analyzer\RequiresCallSiteAnalyzer.cs'
        Original = "            var lowerer = RoslynOperationLowerer.CreateForConcreteReplay(`n                _factory,`n                session.IsKnownPure);"
        Mutated = '            var lowerer = new RoslynOperationLowerer(_factory, session.IsKnownPure);'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~UncheckedOverflowFailsClosedButConcreteViolationsReport'
    },
    [pscustomobject]@{
        Name = 'smt-strict-less-than'
        File = 'SharpProof.Smt\IrSmtBackend.cs'
        Original = '_context.MkLt(Integer(left), Integer(right)),'
        Mutated = '_context.MkLe(Integer(left), Integer(right)),'
        Project = 'SharpProof.Smt.Test\SharpProof.Smt.Test.csproj'
        Filter = 'FullyQualifiedName~StrictComparisonDoesNotAcceptEqualityBoundary'
    },
    [pscustomobject]@{
        Name = 'spec-approved-assembly-name'
        File = 'SharpProof.Effects\ApiSpecResolution.cs'
        Original = 'string.Equals(approved.Name, identity.Name, StringComparison.Ordinal) &&'
        Mutated = 'string.Equals(approved.Name, identity.Name, StringComparison.Ordinal) ||'
        Project = 'SharpProof.Specs.Test\SharpProof.Specs.Test.csproj'
        Filter = 'FullyQualifiedName~ResolverRejectsATypeFromAnUnapprovedAssemblyIdentity'
    },
    [pscustomobject]@{
        Name = 'untrusted-return-annotation'
        File = 'SharpProof.Effects\ManagedAbstractFlow.cs'
        Original = "_trustedBoundaries.AuthorizesDeclaredContracts(method))"
        Mutated = '(_trustedBoundaries.AuthorizesDeclaredContracts(method) || method.ContainingAssembly != null))'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~UnverifiedReturnAnnotationsCannotDischargeRuntimeExceptions'
    },
    [pscustomobject]@{
        Name = 'effect-discovery-operation-stage'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = '            OperationSupportStage.EffectDiscovery,'
        Mutated = '            OperationSupportStage.ContractExpressionLowering,'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~CatchVariableFlowUsesTheEffectDiscoveryCatalog'
    },
    [pscustomobject]@{
        Name = 'effect-fresh-array-content-provenance'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = "            IFieldReferenceOperation or IArrayElementReferenceOperation =>`n                EffectRegionSet.Unknown,"
        Mutated = "            IFieldReferenceOperation => EffectRegionSet.Unknown,`n            IArrayElementReferenceOperation element => ClassifyRegion(element.ArrayReference, aliasSource),"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~FreshArrayContentsDoNotBecomeFreshOwnedAliases'
    },
    [pscustomobject]@{
        Name = 'effect-metadata-precondition-certificate'
        File = 'SharpProof.Effects\ExternalEffectResolver.cs'
        Original = "        if (method.DeclaringSyntaxReferences.Length == 0 &&`n            !preconditionFree)"
        Mutated = "        if (method.DeclaringSyntaxReferences.Length == 0 &&`n            preconditionFree)"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~SourceOnlyMetadataPreconditionsCannotDisappearIntoTrustedSummaries'
    },
    [pscustomobject]@{
        Name = 'external-contract-closed-precondition-boundary'
        File = 'SharpProof.Effects\EffectAnalysisSession.cs'
        Original = "                EffectSummaryOperations.Join(`n                    _external.Resolve(normalized),`n                    ResolveEntryPreconditions(normalized)));"
        Mutated = '                _external.Resolve(normalized));'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DirectExternalAnalysisAppliesClosedEntryPreconditions'
    },
    [pscustomobject]@{
        Name = 'frontend-default-subset-decision'
        File = 'SharpProof.Frontend\FrontendSubset.cs'
        Original = '    public bool IsExact => Decision == FrontendSubsetDecision.Exact;'
        Mutated = '    public bool IsExact => Decision is FrontendSubsetDecision.Unspecified or FrontendSubsetDecision.Exact;'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~DefaultAndUnknownSubsetDecisionsCannotBecomeExact'
    },
    [pscustomobject]@{
        Name = 'frontend-delegate-reference-equality'
        File = 'SharpProof.Frontend\CSharpScalarSemantics.generated.cs'
        Original = '        type is null or ({ IsReferenceType: true, TypeKind: not TypeKind.Delegate } and not INamedTypeSymbol { IsAbstract: true }) ||'
        Mutated = '        type is null or { IsReferenceType: true } ||'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~UnsupportedValueDomainsCannotMasqueradeAsReferenceEquality'
    },
    [pscustomobject]@{
        Name = 'analyzer-configuration-provider-failure'
        File = 'SharpProof.Analyzer\Configuration\AnalyzerConfiguration.cs'
        Original = '                [ProviderFailure(exception)]);'
        Mutated = '                []);'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ConfigurationProviderFailureReportsAndSuppressesAnalysis'
    },
    [pscustomobject]@{
        Name = 'compiler-collector-configuration-gate'
        File = 'SharpProof.CompilerCollector\FinalCompilationCollector.cs'
        Original = '            if (!SharpProofAnalyzer.GetConfigurationDiagnostics('
        Mutated = '            if (SharpProofAnalyzer.GetConfigurationDiagnostics('
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~TreeLocalConfigurationGateDoesNotEmitAnArtifact'
    },
    [pscustomobject]@{
        Name = 'effect-region-contract-catalog'
        File = 'SharpProof.Effects\EffectContractMappings.generated.cs'
        Original = '        (EffectRegionKind.Receiver, EffectContractKind.ReadsReceiverState, EffectContractKind.WritesReceiverState, EffectRegionId.Receiver, false),'
        Mutated = '        (EffectRegionKind.Receiver, EffectContractKind.ReadsArgumentState, EffectContractKind.WritesReceiverState, EffectRegionId.Receiver, false),'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~RegionCatalogIsClosedAndDrivesBothDirections'
    },
    [pscustomobject]@{
        Name = 'effect-direct-event-wire-catalog'
        File = 'SharpProof.Effects\EffectContractMappings.generated.cs'
        Original = '        (EffectDirectEventKind.ManagedObjectAllocation, "managed-allocation"),'
        Mutated = '        (EffectDirectEventKind.ManagedObjectAllocation, "managed-object-allocation"),'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DirectEventWireCatalogIsClosedAndBijective'
    },
    [pscustomobject]@{
        Name = 'effect-lock-constructor-completion'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = "                RecordAllocation(creation) &&`n                HasNonThrowingConstructorSpec(creation),"
        Mutated = '                RecordAllocation(creation),'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DirectLockWitnessesRequireReceiverEvaluationToComplete'
    },
    [pscustomobject]@{
        Name = 'effect-lock-array-admission'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = '            IArrayCreationOperation array => RecordArrayAllocation(array),'
        Mutated = '            IArrayCreationOperation => true,'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DirectLockWitnessesRequireReceiverEvaluationToComplete'
    },
    [pscustomobject]@{
        Name = 'effect-lock-harmless-receiver-unwrapping'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = '        var receiver = DefiniteOperationFacts.UnwrapHarmlessValue(value);'
        Mutated = '        var receiver = value;'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DirectLockWitnessesRequireReceiverEvaluationToComplete'
    },
    [pscustomobject]@{
        Name = 'effect-array-length-symbol-identity'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = '        CompilerIdentityBridge.IsIntrinsicSequenceLength(property);'
        Mutated = '        property.Property.Name is "Length" or "LongLength";'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~EffectArrayCardinalityRequiresCompilerBoundSymbolIdentity'
    },
    [pscustomobject]@{
        Name = 'effect-allocation-base-type-initialization'
        File = 'SharpProof.Effects\EffectMethodNodeBuilder.cs'
        Original = '            current = current.BaseType;'
        Mutated = '            current = null;'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DirectWitnessesAreNarrowDeterministicAndOrdered'
    },
    [pscustomobject]@{
        Name = 'effect-allocation-base-depth-budget'
        File = 'SharpProof.Effects\EffectMethodNodeBuilder.cs'
        Original = '            if (depth >= maximumBaseTypeDepth ||'
        Mutated = '            if (depth < 0 && depth >= maximumBaseTypeDepth ||'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~ExcessiveBaseTypeDepthFailsClosedWithoutRecursion'
    },
    [pscustomobject]@{
        Name = 'effect-allocation-metadata-type-initialization'
        File = 'SharpProof.Effects\EffectMethodNodeBuilder.cs'
        Original = "        if (type.DeclaringSyntaxReferences.Length == 0)`n        {`n            return true;`n        }"
        Mutated = "        if (type.DeclaringSyntaxReferences.Length == 0)`n        {`n            return false;`n        }"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~MetadataBaseInitializationBlocksDirectAllocationWitness'
    },
    [pscustomobject]@{
        Name = 'effect-direct-witness-prebody-completion'
        File = 'SharpProof.Effects\EffectMethodNodeBuilder.cs'
        Original = "            allowDirectWitnesses:`n                graph != null &&`n                HasDefiniteBodyEntry(method, _session.ApiSpecs));"
        Mutated = "            allowDirectWitnesses:`n                graph != null &&`n                !HasDefiniteBodyEntry(method, _session.ApiSpecs));"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~PreBodyExecutionBlocksDirectBodyWitnesses'
    },
    [pscustomobject]@{
        Name = 'effect-system-object-approved-identity'
        File = 'SharpProof.Effects\EffectMethodNodeBuilder.cs'
        Original = "        if (type.SpecialType == SpecialType.System_Object &&`n            HasApprovedSystemObjectConstructor(type, apiSpecs))"
        Mutated = "        if (type.SpecialType == SpecialType.System_Object &&`n            !HasApprovedSystemObjectConstructor(type, apiSpecs))"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~SystemObjectAllocationRequiresApprovedFrameworkIdentity'
    },
    [pscustomobject]@{
        Name = 'effect-direct-witness-conversion-completion'
        File = 'SharpProof.Effects\ManagedAbstractFlow.cs'
        Original = "        !conversion.Conversion.IsUserDefined &&`n        (conversion.Conversion.IsIdentity ||`n         conversion.Conversion.IsImplicit) &&"
        Mutated = "        !conversion.Conversion.IsUserDefined &&`n        true &&"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DirectAllocationWitnessesRequireArgumentCompletion'
    },
    [pscustomobject]@{
        Name = 'effect-collector-subset-admission'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs'
        Original = "            target.MethodKind is`n                MethodKind.Ordinary or`n                MethodKind.Constructor &&`n            selectedSubset.IsSupported;"
        Mutated = "            target.MethodKind is`n                MethodKind.Ordinary or`n                MethodKind.Constructor &&`n            true;"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~UnsupportedEffectCallablesCannotCarryConcreteEvidence'
    },
    [pscustomobject]@{
        Name = 'effect-collector-contract-subset-admission'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs'
        Original = "            analyzerContractsSelected ||`n            analyzerEffectsSelected"
        Mutated = "            false ||`n            analyzerEffectsSelected"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~UnsupportedContractCallablesUseTheSharedSubsetGate'
    },
    [pscustomobject]@{
        Name = 'effect-collector-full-support-evidence'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs'
        Original = '                target, callableId, postconditions.Length, supported)'
        Mutated = '                target, callableId, postconditions.Length, selectedSubset.IsSupported)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~UnsupportedEffectCallableShapesCannotCarryReplayEvidence'
    },
    [pscustomobject]@{
        Name = 'runtime-interpolation-fails-closed'
        File = 'SharpProof.Frontend\OperationSupportCatalog.generated.cs'
        Original = "        OperationKind.ConditionalAccessInstance,`n        OperationKind.ObjectOrCollectionInitializer,"
        Mutated = "        OperationKind.ConditionalAccessInstance,`n        OperationKind.InterpolatedString,`n        OperationKind.ObjectOrCollectionInitializer,"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~StringConstructionDistinguishesKnownAndUnknownAllocation'
    },
    [pscustomobject]@{
        Name = 'effect-incomplete-reason-projection'
        File = 'SharpProof.Analyzer\EffectEvaluationProjections.generated.cs'
        Original = '            (_, true) => EffectEvaluationReason.UnsupportedBody,'
        Mutated = '            (_, true) => EffectEvaluationReason.ResourceLimit,'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~IncompleteReasonCoversEveryDefinedFlagCombination'
    },
    [pscustomobject]@{
        Name = 'advisory-contract-candidate-detection'
        File = 'SharpProof.Frontend\ContractApiMetadata.generated.cs'
        Original = '            "Ensures",'
        Mutated = '            "Requires",'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ContractCandidateActivationRunsClausePlacementValidation'
    },
    [pscustomobject]@{
        Name = 'contract-clause-role-projection'
        File = 'SharpProof.Frontend\ContractApiMetadata.generated.cs'
        Original = 'ContractApiCatalog.RequiresMethodName => ContractApiClauseRole.Requires,'
        Mutated = 'ContractApiCatalog.RequiresMethodName => ContractApiClauseRole.Ensures,'
        Project = 'SharpProof.Contracts.Test\SharpProof.Contracts.Test.csproj'
        Filter = 'FullyQualifiedName~InventoryClassifiesEveryPlacementInStableSourceOrder'
    },
    [pscustomobject]@{
        Name = 'contract-api-duplicate-json-rejection'
        File = 'scripts\Generate-ContractApiCatalog.ps1'
        Original = 'if (-not $names.Add($property.Name)) {'
        Mutated = 'if ($false) {'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~GeneratorRejectsMalformedCatalogs'
    },
    [pscustomobject]@{
        Name = 'contract-api-exported-attribute-parity'
        File = 'SharpProof.Frontend\ContractApiMetadata.generated.cs'
        Original = "            ContractFor,`n            EnforcePure,"
        Mutated = "            EnforcePure,`n            EnforcePure,"
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~CatalogExactlyMatchesTheExportedContractApi'
    },
    [pscustomobject]@{
        Name = 'advisory-full-activation-selection'
        File = 'SharpProof.Analyzer\SharpProofAnalyzer.cs'
        Original = 'return AdvisoryActivation.Full;'
        Mutated = "return new(`n                        RequiresSymbolAnalysis: false,`n                        RequiresOperationAnalysis: true,`n                        RequiresFullOperationAnalysis: false);"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~SelectedGeneratedMethodIsAnalyzedAndReported'
    },
    [pscustomobject]@{
        Name = 'advisory-lazy-state-creation'
        File = 'SharpProof.Analyzer\AnalyzerSession.cs'
        Original = '_callPreconditions = new('
        Mutated = "_ = _apiSpecs.Value;`n        _callPreconditions = new("
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~AdvisoryPotentialWorkCreatesOnlyALightweightSession'
    },
    [pscustomobject]@{
        Name = 'external-precondition-screening'
        File = 'SharpProof.Frontend\ContractApiMetadataRuntime.cs'
        Original = "                attribute.Category ==`n                    ContractApiAttributeCategory.Closed &&"
        Mutated = "                attribute.Category !=`n                    ContractApiAttributeCategory.Closed &&"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~UnannotatedCallerStillChecksExternalClosedPreconditions'
    },
    [pscustomobject]@{
        Name = 'effect-metadata-callsite-certificate'
        File = 'SharpProof.Analyzer\AnalyzerSession.cs'
        Original = "            ResolveEffectContract(method) is`n            { Kind: > EffectContractResolutionKind.Missing and < EffectContractResolutionKind.Valid })"
        Mutated = "            ResolveEffectContract(method) is`n            { Kind: > EffectContractResolutionKind.Missing and <= EffectContractResolutionKind.Valid })"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ExternalMetadataPreconditionEnvelopeCannotBeAssumed'
    },
    [pscustomobject]@{
        Name = 'analyzer-bodyless-entry-precondition'
        File = 'SharpProof.Analyzer\EffectCallPreconditionPolicy.cs'
        Original = "        if (method is`n            {`n                IsAbstract: false,`n                IsExtern: false,`n                DeclaringSyntaxReferences: { IsEmpty: false }`n            } &&`n            binding.Contracts.Clauses.Any(`n                static clause =>`n                    clause.Kind ==`n                    BoundContractKind.Requires))"
        Mutated = "        if (method.IsAbstract || method.IsExtern)"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~BodylessContractWithClosedPreconditionRemainsUnknown'
    },
    [pscustomobject]@{
        Name = 'compilation-reference-model-owner'
        File = 'SharpProof.Frontend\CompilationModelProvider.cs'
        Original = '        return owner.GetSemanticModel(tree, ignoreAccessibility: false);'
        Mutated = '        return compilation.GetSemanticModel(tree, ignoreAccessibility: false);'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~CompilationReferenceNestedParameterContractActivatesCallAnalysis'
    },
    [pscustomobject]@{
        Name = 'generated-selected-analysis-accountability'
        File = 'SharpProof.Analyzer\SharpProofAnalyzer.cs'
        Original = "context.ConfigureGeneratedCodeAnalysis(`n            GeneratedCodeAnalysisFlags.Analyze |`n            GeneratedCodeAnalysisFlags.ReportDiagnostics);"
        Mutated = 'context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~SelectedGeneratedMethodIsAnalyzedAndReported'
    },
    [pscustomobject]@{
        Name = 'trusted-boundary-nonblank-reason'
        File = 'SharpProof.Effects\TrustedBoundaryPolicy.cs'
        Original = '!string.IsNullOrWhiteSpace(reason));'
        Mutated = 'reason != "\0");'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~UnverifiedReturnAnnotationsCannotDischargeRuntimeExceptions'
    },
    [pscustomobject]@{
        Name = 'counterexample-replay-polarity'
        File = 'SharpProof.Worker\CallableCounterexampleReplayer.cs'
        Original = 'evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false }'
        Mutated = 'evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: true }'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~TrivialNormalCompletionRequiresItsPostconditionToBeFalse'
    },
    [pscustomobject]@{
        Name = 'modeled-call-flow-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = 'predicate = application.Predicate;'
        Mutated = 'predicate = factory.Boolean(true);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallArgumentDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'modeled-call-receiver-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = 'guard = receiverGuard;'
        Mutated = 'guard = factory.Boolean(true);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallReceiverDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'modeled-call-argument-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = 'guard = argumentGuard;'
        Mutated = 'guard = factory.Boolean(true);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallArgumentDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'effect-unsupported-candidate-downgrade'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\ClaimManifestBuilder.cs'
        Original = '        evidence.Outcome = WorkerClaimOutcome.Unknown;'
        Mutated = '        evidence.Outcome = WorkerClaimOutcome.Refuted;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~UnsupportedDefiniteEffectViolationFailsClosedWithoutReplay'
    },
    [pscustomobject]@{
        Name = 'effect-replay-object-event-kind'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerEffectReplayLowerer.cs'
        Original = "                eventKind =`n                    CompilerEffectReplayEventKind.ManagedObjectAllocation;"
        Mutated = "                eventKind =`n                    CompilerEffectReplayEventKind.ManagedArrayAllocation;"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~AllocationViolationsCarrySealedUnconditionalReplayEvidence'
    },
    [pscustomobject]@{
        Name = 'effect-replay-worker-constraint-hash'
        File = 'SharpProof.Worker\EffectCounterexampleReplayer.cs'
        Original = "            `"SharpProof.CompilerEffectReplayConstraint`",`n            1,`n            kind,"
        Mutated = "            `"SharpProof.CompilerEffectReplayConstraint`",`n            2,`n            kind,"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~WorkerOwnsCanonicalReplayHashing'
    },
    [pscustomobject]@{
        Name = 'effect-replay-tree-identity'
        File = 'SharpProof.Worker\EffectCounterexampleReplayer.cs'
        Original = '            effectEvent.SyntaxTreeSha256 != tree.Sha256 ||'
        Mutated = '            false && effectEvent.SyntaxTreeSha256 != tree.Sha256 ||'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~StructurallyMalformedReplayEvidenceIsRejected'
    },
    [pscustomobject]@{
        Name = 'effect-replay-allocation-constraint'
        File = 'SharpProof.Worker\EffectCounterexampleReplayer.cs'
        Original = '                (observed & WorkerEffectSet.Allocates) != 0,'
        Mutated = '                (observed & WorkerEffectSet.Allocates) == 0,'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~AllocationReplayRespectsTheSelectedContract'
    },
    [pscustomobject]@{
        Name = 'effect-replay-exact-witness'
        File = 'SharpProof.Worker\EffectCounterexampleReplayer.cs'
        Original = '        return (actual.Kind, actual.Detail, actual.Effects,'
        Mutated = '        return (actual.Kind, claimed.Detail, actual.Effects,'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SemanticWitnessMismatchRemainsTypedUnknown'
    },
    [pscustomobject]@{
        Name = 'effect-vacuity-requires-entry-contradiction'
        File = 'SharpProof.Worker\EffectClaimResultAssembler.cs'
        Original = 'if (entryFeasibility.IsContradictory)'
        Mutated = 'if (!entryFeasibility.IsUnknown)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~EffectOnlyClaimRemainsAccountableWhileMixedRequiresFailsClosed'
    },
    [pscustomobject]@{
        Name = 'effect-invalid-contract-before-vacuity'
        File = 'SharpProof.Worker\EffectClaimResultAssembler.cs'
        Original = "        if (evidence.Outcome == WorkerClaimOutcome.Unknown &&`n            evidence.Reason == WorkerClaimReason.UnsupportedContract)"
        Mutated = "        if (false &&`n            evidence.Reason == WorkerClaimReason.UnsupportedContract)"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~InvalidEffectClaimsCannotBecomeVacuouslyProven'
    },
    [pscustomobject]@{
        Name = 'effect-vacuity-used-assumption-core'
        File = 'SharpProof.Worker\CallableClaimResultAssembler.cs'
        Original = 'usedAssumptionIds.Contains(evidence.Id)'
        Mutated = 'usedAssumptionIds.Contains(evidence.Id) || evidence.Kind == WorkerAssumptionKind.Precondition'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~LiteralEffectVacuityMarksOnlyItsContradictoryPreconditionUsed'
    },
    [pscustomobject]@{
        Name = 'live-effect-bottom-entry-fails-closed'
        File = 'SharpProof.Analyzer\EffectContractDiagnostics.cs'
        Original = '        var declaredComplete = entrySummaryReachable && projection.IsComplete &&'
        Mutated = '        var declaredComplete = projection.IsComplete &&'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~BottomEntryCannotDirectlyProveAnEffectContract'
    },
    [pscustomobject]@{
        Name = 'cache-manifest-binding'
        File = 'SharpProof.Worker\VerificationCache.cs'
        Original = '                !string.Equals(payloadManifestHash, manifest.Hash, StringComparison.Ordinal) ||'
        Mutated = 'false ||'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RehashedCacheSealedForDifferentManifestMissesAndRecomputes'
    },
    [pscustomobject]@{
        Name = 'cache-write-size-admission'
        File = 'SharpProof.Worker\VerificationCache.cs'
        Original = "            if (Encoding.UTF8.GetByteCount(json) >`n                Math.Min(_maximumBytes, WorkerProtocolJson.MaximumJsonBytes))"
        Mutated = '            if (Encoding.UTF8.GetByteCount(json) > _maximumBytes)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~CacheWriteLimitsAreRejectedBeforePublication'
    },
    [pscustomobject]@{
        Name = 'cache-read-lock-coordination'
        File = 'SharpProof.Worker\VerificationCache.cs'
        Original = "            using var cacheLock = AcquireLock(_directory);`n            ValidatePath(path);`n            var json = await WorkerProtocolJson.ReadUtf8FileAsync(path, cancellationToken)"
        Mutated = '            var json = await WorkerProtocolJson.ReadUtf8FileAsync(path, cancellationToken)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~CacheDirectoryLockMakesReadMissAndWriteUnavailable'
    },
    [pscustomobject]@{
        Name = 'cache-write-lock-coordination'
        File = 'SharpProof.Worker\VerificationCache.cs'
        Original = "            using var cacheLock = AcquireLock(_directory);`n            var payload = JsonSerializer.Serialize(new CachePayload("
        Mutated = '            var payload = JsonSerializer.Serialize(new CachePayload('
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~CacheDirectoryLockMakesReadMissAndWriteUnavailable'
    },
    [pscustomobject]@{
        Name = 'protocol-manifest-result-equality'
        File = 'SharpProof.Worker.Protocol\ProtocolJson.cs'
        Original = "actual.OrderBy(static value => value, s_ordinal)`n            .SequenceEqual(expected.OrderBy(static value => value, s_ordinal),`n                s_ordinal)"
        Mutated = 'actual.Concat(expected).All(static _ => true)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~StrictResponseValidationRequiresExactManifestAndResultSets'
    },
    [pscustomobject]@{
        Name = 'worker-runtime-component-byte-limit'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = 'internal const int MaximumComponentBytes = 32 * 1024 * 1024;'
        Mutated = 'internal const int MaximumComponentBytes = 64 * 1024 * 1024;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RuntimeClosureLimitsFailClosedAtEveryBoundary'
    },
    [pscustomobject]@{
        Name = 'worker-rejects-unsupported-runtime-rid-leaf'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '(?!runtimes/)'
        Mutated = '(?=runtimes/)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~IdentityCoversTheCompleteTrustedRuntimeClosure'
    },
    [pscustomobject]@{
        Name = 'worker-runtime-target-selection'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '        foreach (var name in names)'
        Mutated = '        foreach (var name in Array.Empty<string>())'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~IdentityCoversTheCompleteTrustedRuntimeClosure'
    },
    [pscustomobject]@{
        Name = 'launcher-argument-query-budget-projection'
        File = 'SharpProof.Worker.Launcher\LauncherArguments.generated.cs'
        Original = 'QueryRlimit = Number("query-rlimit", WorkerBudgets.DefaultQueryRlimit),'
        Mutated = 'QueryRlimit = Number("method-rlimit", WorkerBudgets.DefaultQueryRlimit),'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~CustomArgumentsProjectEveryRequestValueExactly'
    },
    [pscustomobject]@{
        Name = 'launcher-kill-on-close'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = 'NativeMethods.JobObjectLimitFlags.KillOnJobClose |'
        Mutated = 'NativeMethods.JobObjectLimitFlags.ActiveProcess |'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~WorkerContainmentIsMandatoryOnTheSupportedHost'
    },
    [pscustomobject]@{
        Name = 'launcher-create-suspended'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = 'NativeMethods.CreateSuspended | NativeMethods.CreateNoWindow,'
        Mutated = 'NativeMethods.CreateNoWindow,'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~WorkerCannotReachModuleInitializerBeforeResume'
    },
    [pscustomobject]@{
        Name = 'launcher-timeout-owns-result'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = '        if (exitCode == 124)'
        Mutated = '        if (exitCode != 124)'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~HardTimeoutReplacesWorkerOwnedMalformedOutput'
    },
    [pscustomobject]@{
        Name = 'launcher-manifest-byte-limit'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = 'MaximumBytes = WorkerProtocolJson.MaximumJsonBytes;'
        Mutated = 'MaximumBytes = 32 * 1024 * 1024;'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~CompilerManifestByteLimitIsEnforcedBeforeAllocation'
    },
    [pscustomobject]@{
        Name = 'protocol-json-depth-limit'
        File = 'SharpProof.Worker.Protocol\ProtocolJson.cs'
        Original = 'MaximumJsonDepth = 32;'
        Mutated = 'MaximumJsonDepth = 64;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DeserializationRejectsDocumentsBeyondTheDeclaredDepth'
    },
    [pscustomobject]@{
        Name = 'contract-api-consumer-requires-identity'
        File = 'SharpProof.Effects\ManagedAbstractFlow.cs'
        Original = '            Name: ContractApiCatalog.RequiresMethodName,'
        Mutated = '            Name: ContractApiCatalog.EnsuresMethodName,'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~ContractRequiresRefinesSubsequentFacts'
    },
    [pscustomobject]@{
        Name = 'effect-managed-flow-binary-refinement-guard'
        File = 'SharpProof.Effects\ManagedAbstractFlow.cs'
        Original = "            IBinaryOperation { OperatorMethod: null, IsLifted: false } binary =>`n                AssumeComparison(state, binary.LeftOperand, binary.RightOperand,"
        Mutated = "            IBinaryOperation binary =>`n                AssumeComparison(state, binary.LeftOperand, binary.RightOperand,"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~UserDefinedEqualityDoesNotRefineNullness'
    },
    [pscustomobject]@{
        Name = 'effect-managed-flow-binary-evaluation-guard'
        File = 'SharpProof.Effects\ManagedAbstractFlow.cs'
        Original = "            IBinaryOperation { OperatorMethod: null, IsLifted: false } binary =>`n                Binary(binary.OperatorKind,"
        Mutated = "            IBinaryOperation binary =>`n                Binary(binary.OperatorKind,"
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~UserDefinedEqualityEvaluatesAsUnknown'
    },
    [pscustomobject]@{
        Name = 'contract-api-closed-category-parity'
        File = 'SharpProof.Frontend\ContractApiMetadata.generated.cs'
        Original = "                NotNull,`n                `"NotNullAttribute`",`n                ContractApiAttributeCategory.Closed,"
        Mutated = "                NotNull,`n                `"NotNullAttribute`",`n                ContractApiAttributeCategory.Effect,"
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~CatalogAttributeMetadataMatchesDeclarations'
    },
    [pscustomobject]@{
        Name = 'contract-api-closed-selection-parity'
        File = 'SharpProof.Frontend\ContractApiMetadata.generated.cs'
        Original = "                NotNull,`n                `"NotNullAttribute`",`n                ContractApiAttributeCategory.Closed,`n                ContractApiSelectionFeature.Contracts),"
        Mutated = "                NotNull,`n                `"NotNullAttribute`",`n                ContractApiAttributeCategory.Closed,`n                ContractApiSelectionFeature.Effects),"
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~CatalogAttributeMetadataMatchesDeclarations'
    },
    [pscustomobject]@{
        Name = 'worker-rejects-implicit-worker-path'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))'
        Mutated = '        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~MissingWorkerWithoutDllSuffixIsRejectedBeforeHashing'
    },
    [pscustomobject]@{
        Name = 'launcher-disables-inherited-handles'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = '                    inheritHandles: false,'
        Mutated = '                    inheritHandles: true,'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~WorkerProcessCreationDisablesHandleInheritance'
    },
    [pscustomobject]@{
        Name = 'closure-retains-staged-component-handles'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '                stagedHandles[stagedCount++] = OpenRead(stagedPath);'
        Mutated = '                stagedHandles[stagedCount++] = OpenRead(component.Value);'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~WorkerClosureRetainsStagedComponentsUntilSnapshotDisposal'
    },
    [pscustomobject]@{
        Name = 'closure-canonicalizes-component-key'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '                    hash.Add(component.Key.ToUpperInvariant()).Add(stagedRead);'
        Mutated = '                    hash.Add(component.Key).Add(stagedRead);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~IdentityIgnoresWindowsPathSpelling'
    },
    [pscustomobject]@{
        Name = 'worker-rejects-request-result-alias'
        File = 'SharpProof.Worker\Program.cs'
        Original = '        return !string.Equals(request, result, StringComparison.OrdinalIgnoreCase);'
        Mutated = '        return true;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DirectInvocationRejectsRequestResultAliasBeforeStartBarrier'
    },
    [pscustomobject]@{
        Name = 'closure-retains-each-component-path-once'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '                components.Values.ToArray(),'
        Mutated = '                components.Values.Take(1).ToArray(),'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~IdentityCoversTheCompleteTrustedRuntimeClosure'
    },
    [pscustomobject]@{
        Name = 'closure-component-paths-immutable'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '        ImmutableArray.CreateRange(componentPaths);'
        Mutated = '        componentPaths;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RuntimeClosureComponentPathsAreImmutable'
    },
    [pscustomobject]@{
        Name = 'closure-executes-staged-worker'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '    internal string ExecutionWorkerPath { get; } = executionWorkerPath;'
        Mutated = '    internal string ExecutionWorkerPath { get; } = string.IsNullOrEmpty(executionWorkerPath) ? workerPath : workerPath;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RuntimeClosureComponentPathsAreImmutable'
    },
    [pscustomobject]@{
        Name = 'launcher-checks-discovered-runtime-paths'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = "            runtimeSnapshot?.ComponentPaths.Any(path =>`n                !runtimeRoots.Contains(path, StringComparer.OrdinalIgnoreCase) &&`n                !LauncherArguments.LauncherRuntimePaths.Contains(`n                    path, StringComparer.OrdinalIgnoreCase) &&`n                !paths.Add(path)) is true"
        Mutated = '            runtimeSnapshot?.ComponentPaths.Any(path => path.Length == 0) == true'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~RequestProjectionRejectsDiscoveredRuntimeAssetCollisionBeforeManifestRead'
    },
    [pscustomobject]@{
        Name = 'launcher-validates-static-alias-before-snapshot'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = '            arguments.ValidateDistinctPaths(runtimeSnapshot);'
        Mutated = '            arguments.ValidatePreflight();'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~WorkerRuntimeCompanionAliasIsRejectedBeforeInvalidationDeletesIt'
    }
    [pscustomobject]@{
        Name = 'launcher-checks-launcher-runtime-paths'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = '            ..LauncherArguments.LauncherRuntimePaths,'
        Mutated = '            ..LauncherArguments.LauncherRuntimePaths[1..],'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~RequestProjectionRejectsLauncherRuntimeCollisionBeforeManifestRead'
    },
    [pscustomobject]@{
        Name = 'launcher-checks-protocol-companion-path'
        File = 'SharpProof.Worker.Launcher\LauncherArguments.generated.cs'
        Original = 'System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "SharpProof.Worker.Protocol.dll")'
        Mutated = 'System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "SharpProof.Worker.dll")'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~RequestProjectionRejectsLauncherProtocolRuntimeCollisionBeforeManifestRead'
    },
    [pscustomobject]@{
        Name = 'targets-protect-protocol-companion-path'
        File = 'SharpProof.Verifier.Win-x64\buildTransitive\SharpProof.Verifier.Win-x64.targets'
        Original = '                    "SharpProof.Worker.Protocol.dll")'
        Mutated = '                    "SharpProof.Worker.Missing.dll")'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~LauncherProtocolAssetRemainsProtectedByTargets'
    },
    [pscustomobject]@{
        Name = 'launcher-normalizes-malformed-worker-deps'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = "                InvalidDataException or JsonException or KeyNotFoundException or`n                InvalidOperationException)"
        Mutated = "                InvalidDataException or KeyNotFoundException or`n                InvalidOperationException)"
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~MainFailsClosedWhenWorkerDependencyManifestIsMalformed'
    },
    [pscustomobject]@{
        Name = 'launcher-rejects-cache-inside-worker-tree'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = "            candidates`n                .Skip(runtimeRoots.Length +`n                    LauncherArguments.LauncherRuntimePaths.Length)`n                .OfType<string>()`n                .Any(path => WorkerCachePath.IsSameOrDescendant(`n                    Path.GetFullPath(path),`n                    Path.GetDirectoryName(workerPath)!))"
        Mutated = '            false'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~DirectLauncherRejectsCacheInsideWorkerRuntimeDirectory'
    },
    [pscustomobject]@{
        Name = 'analyzer-source-companion-fallback'
        File = 'SharpProof.Analyzer\AnalyzerSession.cs'
        Original = '                        includeSourceCompanions: false'
        Mutated = '                        includeSourceCompanions: true'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~CompanionPreconditionDoesNotContaminateOtherMember'
    },
    [pscustomobject]@{
        Name = 'closure-staging-content-consistency'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = "        if (!CompilerManifestArtifactFile.ReadAllBytes(`n                    sourcePath,`n                    MaximumComponentBytes).SequenceEqual("
        Mutated = "        if (CompilerManifestArtifactFile.ReadAllBytes(`n                    sourcePath,`n                    MaximumComponentBytes).SequenceEqual("
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~StagedComponentConsistencyIsFailClosed'
    },
    [pscustomobject]@{
        Name = 'closure-staging-length-consistency'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '        if (length > maximum || totalBytes > MaximumClosureBytes - length)'
        Mutated = '        if (length > maximum && totalBytes > MaximumClosureBytes - length)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RuntimeClosureLimitsFailClosedAtEveryBoundary'
    },
    [pscustomobject]@{
        Name = 'build-task-cancel-before-launch'
        File = 'SharpProof.BuildTasks\RunVerifier.cs'
        Original = '                if (_canceled)'
        Mutated = '                if (_canceled && string.IsNullOrEmpty(Executable))'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~CanceledVerifierTaskDoesNotLaunchAProcess'
    },
    [pscustomobject]@{
        Name = 'build-task-cancel-active-process'
        File = 'SharpProof.BuildTasks\RunVerifier.cs'
        Original = '            if (!process.HasExited)'
        Mutated = '            if (process.HasExited)'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~ActiveVerifierTaskCancellationStopsTheProcess'
    },
    [pscustomobject]@{
        Name = 'compiler-linked-module-closure'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerCompilationCapture.cs'
        Original = "            backingModules = [`n                backingModules[0],`n                .. backingModules.Skip(1).OrderBy(`n                    static module => ReadModuleName(module.GetMetadataReader()),`n                    StringComparer.Ordinal)`n            ];"
        Mutated = '            backingModules = [backingModules[0]];'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~LinkedNetmoduleProvenanceCapturesCompleteClosure'
    },
    [pscustomobject]@{
        Name = 'compiler-reference-raw-metadata-binding'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerCompilationCapture.cs'
        Original = '        return left.MetadataLength == right.MetadataLength &&'
        Mutated = '        return left.MetadataLength == right.MetadataLength ||'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ReferencePathMustMatchRawMetadataWhenMvidIsUnchanged'
    },
    [pscustomobject]@{
        Name = 'publication-locks-every-member'
        File = 'SharpProof.Worker.Protocol\WindowsPathIdentity.cs'
        Original = '            .OrderBy(static name => name, StringComparer.Ordinal)'
        Mutated = '            .OrderBy(static name => name, StringComparer.Ordinal).Take(1)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~OverlapOnAnyPublicationMemberBlocks'
    },
    [pscustomobject]@{
        Name = 'publication-rejects-unc'
        File = 'SharpProof.Worker.Protocol\WindowsPathIdentity.cs'
        Original = '        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))'
        Mutated = '        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) && string.IsNullOrEmpty(fullPath))'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RemotePublicationPathIsRejected'
    },
    [pscustomobject]@{
        Name = 'analyzer-rejects-retired-mode'
        File = 'SharpProof.Analyzer\Configuration\AnalyzerConfiguration.cs'
        Original = '        return (options.TryGetValue("sharpproof_mode", out value!) ||'
        Mutated = '        return (options.TryGetValue("sharpproof_removed", out value!) ||'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~RetiredModeOptionFailsClosed'
    },
    [pscustomobject]@{
        Name = 'package-rejects-retired-mode'
        File = 'SharpProof.Package\buildTransitive\SharpProof.targets'
        Original = '    <Error Condition="''$(SharpProofMode)'' != ''''"'
        Mutated = '    <Error Condition="''false'' == ''true''"'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~ProjectBodyConfigurationRejectsRetiredMode'
    }
)

$acceptanceContract = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng\acceptance\contract.json') -Raw |
    ConvertFrom-Json
$mutationPolicy = $acceptanceContract.mutationEvidence
$catalogCount = @($mutations).Count
$catalogLines = @(
    $mutations |
        ForEach-Object {
            $file = $_.File.Replace('\', '/')
            "$($_.Name)`t$file`t$($_.Filter)"
        })
$catalogText = [string]::Join("`n", $catalogLines) + "`n"
$catalogHasher = [Security.Cryptography.SHA256]::Create()
try {
    $catalogBytes = [Text.UTF8Encoding]::new($false).GetBytes($catalogText)
    $catalogSha256 = [Convert]::ToHexString(
        $catalogHasher.ComputeHash($catalogBytes)).ToLowerInvariant()
}
finally {
    $catalogHasher.Dispose()
}
if ($catalogCount -ne [int]$mutationPolicy.expectedCatalogCount -or
    $catalogSha256 -ne [string]$mutationPolicy.expectedCatalogSha256) {
    throw (
        'Trusted mutation registrations do not match the acceptance ' +
        'catalog policy.')
}

if ($MutationName.Count -gt 0) {
    $selection = 'selected'
    $knownNames = @($mutations.Name)
    $requestedNames = @($MutationName | Select-Object -Unique)
    $unknownNames = @($requestedNames | Where-Object { $_ -notin $knownNames })
    if ($unknownNames.Count -gt 0) {
        throw "Unknown mutation name(s): $($unknownNames -join ', ')."
    }
    $mutations = @($mutations | Where-Object { $_.Name -in $requestedNames })
}
else {
    $selection = 'full'
}

$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) 'SharpProof-mutation'
$workspace = Join-Path $mutationRoot (
    'workspace-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $workspace 'source'
$archive = Join-Path $workspace 'source.zip'
$runId = $sourceCommit.Substring(0, 12) + '-' +
    [Guid]::NewGuid().ToString('N')
$logs = Join-Path (Join-Path (Split-Path -Parent $output) 'mutation-logs') $runId
New-Item -ItemType Directory -Path $sourceRoot, $logs -Force | Out-Null
Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue

function Invoke-IsolatedDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$LogName
    )

    $log = Join-Path $logs $LogName
    Push-Location $sourceRoot
    try {
        & (Join-Path $sourceRoot 'scripts\Invoke-SharpProofDotnet.ps1') `
            -MemoryLimitMb 8192 `
            -TimeoutSeconds 600 `
            @Arguments *> $log
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Assert-UniqueMutationTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$Needle,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $first = $Content.IndexOf($Needle, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        throw "Mutation '$Name' target text was not found."
    }
    $second = $Content.IndexOf(
        $Needle,
        $first + $Needle.Length,
        [StringComparison]::Ordinal)
    if ($second -ge 0) {
        throw "Mutation '$Name' target text is not unique."
    }
}

try {
    & git -C $repositoryRoot archive `
        --format=zip `
        --output=$archive `
        $sourceCommit
    if ($LASTEXITCODE -ne 0) {
        throw "git archive failed with exit code $LASTEXITCODE."
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $sourceRoot

    $restoreExit = Invoke-IsolatedDotnet `
        -Arguments @('restore', 'SharpProof.sln') `
        -LogName 'restore.log'
    if ($restoreExit -ne 0) {
        throw "Mutation workspace restore failed; see $logs\restore.log."
    }

    foreach ($mutation in $mutations) {
        $baselineTrxName = $mutation.Name + '-baseline.trx'
        $baselineTrx = Join-Path $logs $baselineTrxName
        Remove-Item -LiteralPath $baselineTrx -Force -ErrorAction SilentlyContinue
        $baselineExit = Invoke-IsolatedDotnet `
            -Arguments @(
                'test',
                $mutation.Project,
                '-c',
                $Configuration,
                '--no-restore',
                '--filter',
                $mutation.Filter,
                '--logger',
                'console;verbosity=minimal',
                '--logger',
                "trx;LogFileName=$baselineTrxName",
                '--results-directory',
                $logs) `
            -LogName ($mutation.Name + '-baseline.log')
        if ($baselineExit -ne 0) {
            throw (
                "Baseline for mutation '$($mutation.Name)' failed; see " +
                "$logs\$($mutation.Name)-baseline.log.")
        }
        $expectedMethodName = $mutation.Filter.Substring(
            'FullyQualifiedName~'.Length)
        $baselineEvidence = Read-SharpProofMutationTestEvidence `
            -TrxPath $baselineTrx `
            -EvidenceName ($mutation.Name + ' baseline') `
            -Mode Baseline `
            -ProcessExitCode $baselineExit `
            -ExpectedMethodName $expectedMethodName
        $mutation | Add-Member `
            -NotePropertyName BaselineLedger `
            -NotePropertyValue @($baselineEvidence.testLedger)
    }

    $results = @()
    foreach ($mutation in $mutations) {
        $path = Join-Path $sourceRoot $mutation.File
        $originalContent = [IO.File]::ReadAllText($path)
        Assert-UniqueMutationTarget `
            -Content $originalContent `
            -Needle $mutation.Original `
            -Name $mutation.Name
        $mutatedContent = $originalContent.Replace(
            $mutation.Original,
            $mutation.Mutated,
            [StringComparison]::Ordinal)
        try {
            [IO.File]::WriteAllText(
                $path,
                $mutatedContent,
                [Text.UTF8Encoding]::new($false))
            $buildExit = Invoke-IsolatedDotnet `
                -Arguments @(
                    'build',
                    $mutation.Project,
                    '-c',
                    $Configuration,
                    '--no-restore') `
                -LogName ($mutation.Name + '-build.log')
            if ($buildExit -ne 0) {
                throw (
                    "Mutation '$($mutation.Name)' did not compile; see " +
                    "$logs\$($mutation.Name)-build.log.")
            }
            $testTrxName = $mutation.Name + '-test.trx'
            $testTrx = Join-Path $logs $testTrxName
            Remove-Item -LiteralPath $testTrx -Force -ErrorAction SilentlyContinue
            $testExit = Invoke-IsolatedDotnet `
                -Arguments @(
                    'test',
                    $mutation.Project,
                    '-c',
                    $Configuration,
                    '--no-build',
                    '--filter',
                    $mutation.Filter,
                    '--logger',
                    'console;verbosity=minimal',
                    '--logger',
                    "trx;LogFileName=$testTrxName",
                    '--results-directory',
                    $logs) `
                -LogName ($mutation.Name + '-test.log')
            if ($testExit -eq 0) {
                throw (
                    "Mutation '$($mutation.Name)' survived its focused test; " +
                    "see $logs\$($mutation.Name)-test.log.")
            }
            if ($testExit -eq 124) {
                throw (
                    "Mutation '$($mutation.Name)' timed out instead of being " +
                    "killed by an assertion.")
            }
            $expectedMethodName = $mutation.Filter.Substring(
                'FullyQualifiedName~'.Length)
            $testEvidence = Read-SharpProofMutationTestEvidence `
                -TrxPath $testTrx `
                -EvidenceName $mutation.Name `
                -Mode Mutation `
                -ProcessExitCode $testExit `
                -ExpectedMethodName $expectedMethodName `
                -ExpectedLedger $mutation.BaselineLedger
            $results += [pscustomobject]@{
                name = $mutation.Name
                file = $mutation.File.Replace('\', '/')
                test = $mutation.Filter
                killed = $true
                exitCode = $testExit
                executedCount = $testEvidence.executedCount
                failedCount = $testEvidence.failedCount
                assertionFailureCount = $testEvidence.assertionFailureCount
                selectedTests = $testEvidence.testLedger
                log = "mutation-logs/$runId/$($mutation.Name)-test.log"
                trx = "mutation-logs/$runId/$testTrxName"
            }
        }
        finally {
            [IO.File]::WriteAllText(
                $path,
                $originalContent,
                [Text.UTF8Encoding]::new($false))
        }
    }

    $outputDirectory = Split-Path -Parent $output
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $currentCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    & git -C $repositoryRoot diff --quiet --
    $trackedTreeChanged = $LASTEXITCODE -ne 0
    & git -C $repositoryRoot diff --cached --quiet --
    $trackedIndexChanged = $LASTEXITCODE -ne 0
    if ($currentCommit -ne $sourceCommit -or
        $trackedTreeChanged -or $trackedIndexChanged) {
        throw 'Repository identity changed while mutation evidence was produced.'
    }
    $temporaryOutput = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [pscustomobject]@{
        schemaVersion = 2
        commit = $sourceCommit
        configuration = $Configuration
        selection = $selection
        catalogCount = $catalogCount
        catalogSha256 = $catalogSha256
        mutationCount = $results.Count
        killedCount = @($results | Where-Object killed).Count
        mutations = $results
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $temporaryOutput -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporaryOutput -Destination $output -Force
    Write-Host "Killed $($results.Count) trusted-boundary mutations."
    Write-Host "Evidence: $output"
}
finally {
    if (-not $KeepWorkspace -and
        (Test-Path -LiteralPath $workspace)) {
        $resolvedWorkspace = [IO.Path]::GetFullPath($workspace)
        $resolvedMutationRoot = [IO.Path]::GetFullPath($mutationRoot)
        if (-not $resolvedWorkspace.StartsWith(
                $resolvedMutationRoot +
                [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove mutation workspace: $resolvedWorkspace"
        }
        Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
    }
}
