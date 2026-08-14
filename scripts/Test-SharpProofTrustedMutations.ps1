[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath = 'artifacts\mutation\summary.json',

    [string[]]$MutationName = @(),

    [ValidateRange(0, 15)]
    [int]$MutationShardIndex = 0,

    [ValidateRange(1, 16)]
    [int]$MutationShardCount = 1,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit,

    [string]$BaselineEvidencePath = '',

    [switch]$BaselineOnly,

    [switch]$Resume,

    [switch]$KeepWorkspace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationEvidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationScheduling.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationBaselines.psm1') -Force
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $OutputPath -ParameterName 'OutputPath'
$baselineFile = if ([string]::IsNullOrWhiteSpace($BaselineEvidencePath)) {
    $null
}
else {
    Resolve-SharpProofContainedPath `
        -Root $repositoryRoot -Path $BaselineEvidencePath `
        -ParameterName 'BaselineEvidencePath'
}
if ($BaselineOnly -and $null -eq $baselineFile) {
    throw 'BaselineOnly requires BaselineEvidencePath.'
}
if ($BaselineOnly -and
        ($MutationShardCount -ne 1 -or $MutationShardIndex -ne 0)) {
    throw 'BaselineOnly cannot be combined with catalog sharding.'
}
if ($BaselineOnly -and $Resume) {
    throw 'BaselineOnly cannot be combined with Resume.'
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
        File = 'SharpProof.Analyzer.Core\RequiresCallSiteAnalyzer.cs'
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
        Name = 'effect-exception-handler-reachability'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = "            return _handlerReachability.IsReachable(`n                @catch,`n                @catch.Filter?.Span.Contains(operation.Syntax.Span) == true);"
        Mutated = '            return true;'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~ExceptionHandlersContributeEffectsOnlyWhenReachable'
    },
    [pscustomobject]@{
        Name = 'effect-fresh-initializer-creation-capture-ownership'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = '        _creationCaptures.Record(capture);'
        Mutated = '        _ = _creationCaptures;'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~FreshObjectInitializerOwnershipMatrixIsExact'
    },
    [pscustomobject]@{
        Name = 'effect-definitely-null-throw-projection'
        File = 'SharpProof.Effects\EffectExceptionFlow.cs'
        Original = '        if (abstractFlow?.ProvesNull(thrown, thrown.Exception) == true)'
        Mutated = '        if (abstractFlow?.ProvesNull(thrown, thrown.Exception) == false)'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~DefinitelyNullThrownExpressionsReplaceTheirDeclaredExceptionType'
    },
    [pscustomobject]@{
        Name = 'effect-exact-array-store-compatibility'
        File = 'SharpProof.Effects\OperationEffectScanner.cs'
        Original = "        return _session.Compilation.ClassifyCommonConversion(`n            assignedValue.Type,`n            runtimeType.ElementType).IsImplicit;"
        Mutated = '        return false;'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~ArrayStoreCompatibilityUsesExactFreshRuntimeElementType'
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
        File = 'SharpProof.Analyzer.Core\Configuration\AnalyzerConfiguration.cs'
        Original = '                [ProviderFailure(exception)]);'
        Mutated = '                []);'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ConfigurationProviderFailureReportsAndSuppressesAnalysis'
    },
    [pscustomobject]@{
        Name = 'compiler-collector-configuration-gate'
        File = 'SharpProof.CompilerCollector\FinalCompilationCollector.cs'
        Original = '            if (!SharpProofAnalyzerEngine.GetConfigurationDiagnostics('
        Mutated = '            if (SharpProofAnalyzerEngine.GetConfigurationDiagnostics('
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
        Name = 'ordinary-interpolation-catalog-parent'
        File = 'SharpProof.Frontend\OperationSupportCatalog.generated.cs'
        Original = "        OperationKind.InterpolatedString,`n        OperationKind.InterpolatedStringText,"
        Mutated = '        OperationKind.InterpolatedStringText,'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~StageSpecificOperationClassifiersMatchTheClosedCatalog'
    },
    [pscustomobject]@{
        Name = 'effect-incomplete-reason-projection'
        File = 'SharpProof.Analyzer.Core\EffectEvaluationProjections.generated.cs'
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
        File = 'SharpProof.Analyzer.Core\SharpProofAnalyzerEngine.cs'
        Original = 'return AdvisoryActivation.Full;'
        Mutated = "return new(`n                        RequiresSymbolAnalysis: false,`n                        RequiresOperationAnalysis: true,`n                        RequiresFullOperationAnalysis: false);"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~SelectedGeneratedMethodIsAnalyzedAndReported'
    },
    [pscustomobject]@{
        Name = 'rejected-control-declaration-scope'
        File = 'SharpProof.Analyzer.Core\SharpProofControlAttributePolicy.cs'
        Original = '            if (!session.Attributes.IsRejectedControlAttribute(attribute) ||'
        Mutated = '            if (true ||'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~SourceShadowedControlAttributesReportOnEveryDeclaredScope'
    },
    [pscustomobject]@{
        Name = 'advisory-lazy-state-creation'
        File = 'SharpProof.Analyzer.Core\AnalyzerSession.cs'
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
        File = 'SharpProof.Analyzer.Core\AnalyzerSession.cs'
        Original = "            ResolveEffectContract(method) is`n            { Kind: > EffectContractResolutionKind.Missing and < EffectContractResolutionKind.Valid })"
        Mutated = "            ResolveEffectContract(method) is`n            { Kind: > EffectContractResolutionKind.Missing and <= EffectContractResolutionKind.Valid })"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ExternalMetadataPreconditionEnvelopeCannotBeAssumed'
    },
    [pscustomobject]@{
        Name = 'analyzer-bodyless-entry-precondition'
        File = 'SharpProof.Analyzer.Core\EffectCallPreconditionPolicy.cs'
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
        Name = 'generated-header-exact-token'
        File = 'SharpProof.Analyzer.Core\AnalyzerGeneratedCodePolicy.cs'
        Original = "        return GeneratedHeaderTokens.Any(token => string.Equals(`n            body,`n            token,`n            StringComparison.OrdinalIgnoreCase));"
        Mutated = '        return body.IndexOf("<auto-generated", StringComparison.OrdinalIgnoreCase) >= 0 || body.IndexOf("<autogenerated", StringComparison.OrdinalIgnoreCase) >= 0;'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~MarkerMentionsDoNotSuppressHandwrittenCallSites'
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
        Original = 'predicate = application.Value.Predicate;'
        Mutated = 'predicate = factory.Boolean(true);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallArgumentDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'modeled-call-receiver-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = "guard = receiverGuard;`n                substitutions.Add(template.Receiver.Value, receiver);"
        Mutated = "guard = factory.Boolean(true);`n                substitutions.Add(template.Receiver.Value, receiver);"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallReceiverDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'modeled-call-argument-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = "guard = argumentGuard;`n                substitutions.Add(template.Parameters[index], argument);"
        Mutated = "guard = factory.Boolean(true);`n                substitutions.Add(template.Parameters[index], argument);"
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
        File = 'SharpProof.Analyzer.Core\EffectContractDiagnostics.cs'
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
        Name = 'launcher-stdin-start-release'
        File = 'SharpProof.Host\LinuxWorkerProcess.cs'
        Original = '    public const string StartMessage = "SharpProof.Start/1";'
        Mutated = '    public const string StartMessage = "SharpProof.Start/0";'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~LinuxWorkerReceivesTheExactStartupRelease'
    },
    [pscustomobject]@{
        Name = 'worker-parent-death-boundary'
        File = 'SharpProof.Host\LinuxWorkerProcess.cs'
        Original = '    private const int ParentDeathSignal = 1;'
        Mutated = '    private const int ParentDeathSignal = 0;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~ParentDeathKillsAWorkerBlockedBeforeStartupRelease'
    },
    [pscustomobject]@{
        Name = 'container-z3-payload-hash'
        File = 'SharpProof.Host\ContainerContract.cs'
        Original = "        if (!string.Equals(`n                hash,`n                contract.Z3LibrarySha256,`n                StringComparison.OrdinalIgnoreCase))"
        Mutated = "        if (false && !string.Equals(`n                hash,`n                contract.Z3LibrarySha256,`n                StringComparison.OrdinalIgnoreCase))"
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~ContainerZ3PayloadRejectsAHashMismatch'
    },
    [pscustomobject]@{
        Name = 'container-z3-refuses-ambient-load'
        File = 'SharpProof.Host\ContainerNativeLibrary.cs'
        Original = "            var handle = NativeLibrary.Load(`n                ContainerContract.ResolveZ3LibraryRequired());"
        Mutated = '            var handle = NativeLibrary.Load(Z3ImportName);'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~NativeZ3ResolverLoadsOnlyTheContainerVerifiedPath'
    },
    [pscustomobject]@{
        Name = 'worker-test-z3-required-bootstrap'
        File = 'SharpProof.Worker.Test\ContainerNativeLibrarySetup.cs'
        Original = '            typeof(Microsoft.Z3.Context).Assembly);'
        Mutated = '            typeof(ContainerNativeLibrarySetup).Assembly);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~InjectedBuiltInBackendStillChargesTheMethodRlimit'
    },
    [pscustomobject]@{
        Name = 'release-tag-exact-ref-authority'
        File = 'scripts\Invoke-SharpProofReleaseContainer.ps1'
        Original = '        if ($ref -cne $expectedRef) {'
        Mutated = '        if ($false) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~ReleaseTagAuthorityRejectsEveryNonExactIdentity'
    },
    [pscustomobject]@{
        Name = 'release-configuration-empty-expected-set'
        File = 'scripts\Test-SharpProofReleaseConfiguration.ps1'
        Original = '[Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Expected,'
        Mutated = '[Parameter(Mandatory = $true)][object[]]$Expected,'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~EffectiveReleaseRefSetsMustEqualTheContract'
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
        Name = 'protocol-json-nested-shape-authority'
        File = 'SharpProof.Worker.Protocol\ProtocolJsonSupport.cs'
        Original = '        EnsureObjectShape(document.RootElement, shape);'
        Mutated = '        _ = shape;'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~OmittedNestedManifestSchemaVersionIsRejectedDuringDeserialization'
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
        Name = 'linux-worker-startup-release-uses-private-stdin'
        File = 'SharpProof.Host\LinuxWorkerProcess.cs'
        Original = '            RedirectStandardInput = true,'
        Mutated = '            RedirectStandardInput = false,'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~WorkerProcessBoundaryUsesADirectLinuxChildAndStdinRelease'
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
        Name = 'closure-preserves-linux-component-key-case'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '                    hash.Add(component.Key).Add(stagedRead);'
        Mutated = '                    hash.Add(component.Key.ToUpperInvariant()).Add(stagedRead);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~IdentityDistinguishesLinuxComponentNameCase'
    },
    [pscustomobject]@{
        Name = 'worker-rejects-request-result-alias'
        File = 'SharpProof.Worker\Program.cs'
        Original = '        return !string.Equals(request, result, StringComparison.Ordinal);'
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
        Original = "            runtimeSnapshot?.ComponentPaths.Any(path =>`n                !runtimeRoots.Contains(path, StringComparer.Ordinal) &&`n                !LauncherArguments.LauncherRuntimePaths.Contains(`n                    path, StringComparer.Ordinal) &&`n                !paths.Add(path)) is true"
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
        File = 'SharpProof.BuildTasks\InvalidatePublishedResult.cs'
        Original = '                WorkerProtocolPath)'
        Mutated = '                InvocationManifestPath)'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~LauncherProtocolAssetRemainsProtectedByTargets'
    },
    [pscustomobject]@{
        Name = 'launcher-normalizes-malformed-worker-deps'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = "                InvalidDataException or JsonException or KeyNotFoundException or`n                InvalidOperationException or System.ComponentModel.Win32Exception)"
        Mutated = "                InvalidDataException or KeyNotFoundException or`n                InvalidOperationException or System.ComponentModel.Win32Exception)"
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~MainFailsClosedWhenWorkerDependencyManifestIsMalformed'
    },
    [pscustomobject]@{
        Name = 'launcher-rejects-cache-inside-worker-tree'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = "            candidates`n                .Skip(runtimeRoots.Length +`n                    LauncherArguments.LauncherRuntimePaths.Length)`n                .OfType<string>()`n                .Any(path => LinuxPathIdentity.IsSameOrDescendant(`n                    path,`n                    Path.GetDirectoryName(workerPath)!))"
        Mutated = '            false'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~DirectLauncherRejectsCacheInsideWorkerRuntimeDirectory'
    },
    [pscustomobject]@{
        Name = 'analyzer-source-companion-fallback'
        File = 'SharpProof.Analyzer.Core\AnalyzerSession.cs'
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
        Name = 'compiler-reference-module-byte-limit'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerCompilationCapture.cs'
        Original = '            if (sizeBytes <= 0 || sizeBytes > _limits.MaximumModuleBytes)'
        Mutated = '            if (sizeBytes <= 0)'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ReferenceCaptureEnforcesModuleClosureAndCountLimits'
    },
    [pscustomobject]@{
        Name = 'compiler-reference-closure-byte-limit'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerCompilationCapture.cs'
        Original = '                _closureBytes > _limits.MaximumClosureBytes - sizeBytes)'
        Mutated = '                _closureBytes > _limits.MaximumClosureBytes)'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ReferenceCaptureEnforcesModuleClosureAndCountLimits'
    },
    [pscustomobject]@{
        Name = 'compiler-reference-module-count-limit'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerCompilationCapture.cs'
        Original = '            if (_moduleCount >= _limits.MaximumModuleCount ||'
        Mutated = '            if (_moduleCount > _limits.MaximumModuleCount ||'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ReferenceCaptureEnforcesModuleClosureAndCountLimits'
    },
    [pscustomobject]@{
        Name = 'compiler-reference-evidence-size-validation'
        File = 'SharpProof.CompilerArtifact\CompilationFingerprint.cs'
        Original = '            value.SizeBytes is > 0 and'
        Mutated = '            value.SizeBytes is >= 0 and'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RecomputedOuterHashCannotHideMalformedNestedEvidence'
    },
    [pscustomobject]@{
        Name = 'generated-contract-for-final-compilation-validation'
        File = 'SharpProof.Analyzer.Core\SharpProofAnalyzerEngine.cs'
        Original = '        if (configuration.ContractsEnabled)'
        Mutated = '        if (false && configuration.ContractsEnabled)'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~GeneratedCompanionIsValidatedFromFinalCompilation'
    },
    [pscustomobject]@{
        Name = 'requires-accessor-call-site-discovery'
        File = 'SharpProof.Analyzer.Core\RequiresCallSiteDiscovery.cs'
        Original = "            IPropertyReferenceOperation property =>`n                GetPropertyCalls(property),`n            IEventReferenceOperation eventReference =>`n                GetEventCalls(eventReference),"
        Mutated = "            IPropertyReferenceOperation property => [],`n            IEventReferenceOperation eventReference => [],"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~PropertyAndEventAccessorsCheckRequiresExactlyOnce'
    },
    [pscustomobject]@{
        Name = 'package-contract-for-generator-entrypoint'
        File = 'SharpProof.Package\SharpProof.nuspec'
        Original = '    <file src="..\SharpProof.ContractForGenerator\bin\$configuration$\netstandard2.0\SharpProof.ContractForGenerator.dll" target="tools\analyzers\dotnet\cs" />'
        Mutated = '    <file src="..\SharpProof.ContractForGenerator\bin\$configuration$\netstandard2.0\SharpProof.ContractForGenerator.dll" target="tools\shared\netstandard2.0" />'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~PackageGraphAndLayoutsAreExact'
    },
    [pscustomobject]@{
        Name = 'tcb-path-backslash-canonicality'
        File = 'scripts\Get-SharpProofTcbPaths.ps1'
        Original = "        if (`$path.Contains('\') -or"
        Mutated = '        if ($false -or'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~TrustedComputingBaseRejectsNoncanonicalPaths'
    },
    [pscustomobject]@{
        Name = 'release-authority-contained-path-case-sensitivity'
        File = 'scripts\Resolve-SharpProofContainedPath.ps1'
        Original = "    if (-not `$canonicalPath.StartsWith(`n            `$prefix,`n            [StringComparison]::Ordinal)) {"
        Mutated = "    if (-not `$canonicalPath.StartsWith(`n            `$prefix,`n            [StringComparison]::OrdinalIgnoreCase)) {"
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~LinuxEvidencePathsUseOrdinalCanonicalContainment'
    },
    [pscustomobject]@{
        Name = 'publication-locks-every-member'
        File = 'SharpProof.Host\LinuxPathIdentity.cs'
        Original = "        var locks = canonicalPaths`n            .Select(PublicationLockNameForCanonicalPath)`n            .OrderBy(static path => path, StringComparer.Ordinal)`n            .Select(static path => new PublicationLock(path))"
        Mutated = "        var locks = canonicalPaths`n            .Select(PublicationLockNameForCanonicalPath)`n            .OrderBy(static path => path, StringComparer.Ordinal)`n            .Take(1)`n            .Select(static path => new PublicationLock(path))"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~OverlapOnAnyPublicationMemberBlocks'
    },
    [pscustomobject]@{
        Name = 'publication-set-identity-injective-framing'
        File = 'SharpProof.Host\LinuxPathIdentity.cs'
        Original = '            AppendPublicationSetFrame(hash, bytes.Length);'
        Mutated = '            hash.AppendData(Encoding.UTF8.GetBytes("\n"));'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~PublicationSetIdentityUsesCanonicalInjectiveUtf8Framing'
    },
    [pscustomobject]@{
        Name = 'publication-metadata-fixed-size-hash-name'
        File = 'SharpProof.Host\LinuxPathIdentity.cs'
        Original = "            PublicationMetadataDirectory,`n            identity + extension);"
        Mutated = "            PublicationMetadataDirectory,`n            Path.GetFileName(canonicalPath) + extension);"
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~PublicationMetadataSupportsNameMaxBoundaryForEveryMember'
    },
    [pscustomobject]@{
        Name = 'contractfor-location-active-compilation'
        File = 'SharpProof.Analyzer.Core\ContractForValidation\ContractForCompanionValidator.cs'
        Original = "                location.SourceTree is { } tree &&`n                compilation.ContainsSyntaxTree(tree))"
        Mutated = "                location.SourceTree is { } tree &&`n                !compilation.ContainsSyntaxTree(tree))"
        Project = 'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj'
        Filter = 'FullyQualifiedName~CompilationReferenceTargetDiagnosticUsesCurrentCompanionLocation'
    },
    [pscustomobject]@{
        Name = 'multitarget-sarif-framework-scope'
        File = 'SharpProof.Verifier\buildTransitive\SharpProof.Verifier.targets'
        Original = "      <_SharpProofEffectiveSarifFile Condition=`"'`$(SharpProofVerifySarifFile)' != '' AND '`$(TargetFrameworks)' != ''`">"
        Mutated = "      <_SharpProofEffectiveSarifFile Condition=`"'`$(SharpProofVerifySarifFile)' != '' AND '`$(TargetFrameworks)' == ''`">"
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~MultiTargetConfiguredSarifIsFrameworkScoped'
    },
    [pscustomobject]@{
        Name = 'packaged-paths-normalize-after-project-body'
        File = 'SharpProof.Package\buildTransitive\SharpProof.targets'
        Original = "    <_SharpProofAnalyzerDirectory>`$([System.IO.Path]::GetFullPath('`$(SharpProofAnalyzerDirectory)'))</_SharpProofAnalyzerDirectory>"
        Mutated = "    <_SharpProofAnalyzerDirectory>`$([System.IO.Path]::GetFullPath('`$(MSBuildThisFileDirectory)../tools/analyzers/dotnet/cs'))</_SharpProofAnalyzerDirectory>"
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~ProjectBodyAnalyzerAndCollectorOverridesNormalizeLate'
    },
    [pscustomobject]@{
        Name = 'runtime-closure-validates-before-invalidation'
        File = 'SharpProof.Verifier\buildTransitive\SharpProof.Verifier.targets'
        Original = '          DependsOnTargets="_SharpProofValidateRuntimeClosure"'
        Mutated = '          DependsOnTargets=""'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~ProjectBodyRuntimeClosureOverridesAreRejectedBeforePublication'
    },
    [pscustomobject]@{
        Name = 'requires-skips-compiler-elided-invocations'
        File = 'SharpProof.Analyzer.Core\RequiresCallSiteDiscovery.cs'
        Original = "        if (operation is IInvocationOperation invocation &&`n            _invocationEmission.IsElided(invocation))"
        Mutated = "        if (operation is IInvocationOperation invocation &&`n            !_invocationEmission.IsElided(invocation))"
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~SourceConditionalInvocationAndArgumentsFollowEmission'
    },
    [pscustomobject]@{
        Name = 'requires-parenthesized-call-ownership'
        File = 'SharpProof.Analyzer.Core\RequiresCallSiteDiscovery.cs'
        Original = '            expression = parenthesized.Expression;'
        Mutated = '            expression = null;'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ParenthesizedDirectCallsReplayPreconditionsInEveryOwnedShape'
    },
    [pscustomobject]@{
        Name = 'nightly-fuzz-command-connected'
        File = '.github\workflows\nightly.yml'
        Original = '          docker compose run --rm tooling fuzz-nightly'
        Mutated = '          docker compose run --rm tooling acceptance'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~NightlyFuzzCampaignIsContainerConnectedAndEvidenceBound'
    },
    [pscustomobject]@{
        Name = 'fuzz-result-requires-exact-property-count'
        File = 'scripts\Assert-SharpProofFuzzRunnerResult.ps1'
        Original = '$actual.Count -ne $Expected.Count -or'
        Mutated = '$false -or'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~FuzzRunnerEvidenceUsesStrictSchemaFourDecoder'
    },
    [pscustomobject]@{
        Name = 'fuzz-result-requires-number-tokens'
        File = 'scripts\Assert-SharpProofFuzzRunnerResult.ps1'
        Original = '$property.ValueKind -ne [Text.Json.JsonValueKind]::Number -or'
        Mutated = '$property.ValueKind -eq [Text.Json.JsonValueKind]::Number -or'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~FuzzRunnerEvidenceUsesStrictSchemaFourDecoder'
    },
    [pscustomobject]@{
        Name = 'fuzz-result-requires-positive-coverage'
        File = 'scripts\Assert-SharpProofFuzzRunnerResult.ps1'
        Original = '(Get-ExactJsonInt32 $coverage $name) -le 0'
        Mutated = '(Get-ExactJsonInt32 $coverage $name) -lt 0'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~FuzzRunnerEvidenceUsesStrictSchemaFourDecoder'
    },
    [pscustomobject]@{
        Name = 'contract-generic-owners-ignore-nongeneric-wrappers'
        File = 'SharpProof.Contracts\ContractForSymbolMatcher.cs'
        Original = ': GetGenericTypeLayers(containingType);'
        Mutated = ': GetTypeLayers(containingType);'
        Project = 'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj'
        Filter = 'FullyQualifiedName~GenericOwnersAlignIndependentlyOfNonGenericWrappers'
    },
    [pscustomobject]@{
        Name = 'contract-ref-readonly-normalizes-compiler-in-modifier'
        File = 'SharpProof.Contracts\ContractForSymbolMatcher.cs'
        Original = 'return refKind is RefKind.In or RefKind.RefReadOnlyParameter;'
        Mutated = 'return refKind is RefKind.In;'
        Project = 'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj'
        Filter = 'FullyQualifiedName~RefReadonlyParameterMatchesExactStaticCompanion'
    },
    [pscustomobject]@{
        Name = 'contract-ref-expression-uses-declaration-operation-root'
        File = 'SharpProof.Contracts\ContractClauseInventoryBuilder.cs'
        Original = 'return expression is RefExpressionSyntax ? declaration : expression;'
        Mutated = 'return expression;'
        Project = 'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj'
        Filter = 'FullyQualifiedName~RefReturningExpressionBodiedCompanionHasAnOperationBody'
    },
    [pscustomobject]@{
        Name = 'contract-function-pointer-conventions-are-unordered'
        File = 'SharpProof.Contracts\ContractForSymbolMatcher.cs'
        Original = '                    match = index;'
        Mutated = '                    match = left.IndexOf(leftType);'
        Project = 'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj'
        Filter = 'FullyQualifiedName~UnmanagedFunctionPointerConventionOrderIsInterchangeable'
    },
    [pscustomobject]@{
        Name = 'contract-float-default-bits-are-exact'
        File = 'SharpProof.Contracts\ContractForSymbolMatcher.cs'
        Original = '                SingleBits(leftValue) == SingleBits(rightValue),'
        Mutated = '                SingleBits(leftValue) == SingleBits(rightValue) || leftValue.Equals(rightValue),'
        Project = 'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj'
        Filter = 'FullyQualifiedName~FloatingDefaultBitsMustMatchExactly'
    },
    [pscustomobject]@{
        Name = 'contract-double-default-bits-are-exact'
        File = 'SharpProof.Contracts\ContractForSymbolMatcher.cs'
        Original = "                BitConverter.DoubleToInt64Bits(leftValue) ==`n                BitConverter.DoubleToInt64Bits(rightValue),"
        Mutated = "                BitConverter.DoubleToInt64Bits(leftValue) ==`n                BitConverter.DoubleToInt64Bits(rightValue) || leftValue.Equals(rightValue),"
        Project = 'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj'
        Filter = 'FullyQualifiedName~FloatingDefaultBitsMustMatchExactly'
    },
    [pscustomobject]@{
        Name = 'requires-discovers-implicit-base-constructor'
        File = 'SharpProof.Analyzer.Core\RequiresCallSiteDiscovery.cs'
        Original = '            caller.ContainingType.TypeKind != TypeKind.Class ||'
        Mutated = '            caller.ContainingType.TypeKind == TypeKind.Class ||'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ImplicitBaseInitializerReplaysParameterlessPrecondition'
    },
    [pscustomobject]@{
        Name = 'verifier-native-payload-is-build-tool-only'
        File = 'SharpProof.Verifier\SharpProof.Verifier.nuspec'
        Original = 'target="tools/native/linux-x64"'
        Mutated = 'target="runtimes/linux-x64/native"'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~VerifierNativeToolDoesNotBecomeApplicationRuntimeAsset'
    },
    [pscustomobject]@{
        Name = 'standalone-gate-requires-passing-result'
        File = 'scripts\Assert-SharpProofStandaloneGateResult.ps1'
        Original = '$document.Result.Passed -isnot [bool] -or'
        Mutated = '$document.Result.Passed -is [bool] -or'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~StandaloneGateDecoderRejectsUnauthenticatedEvidence'
    },
    [pscustomobject]@{
        Name = 'publication-rejects-symbolic-links'
        File = 'SharpProof.Host\LinuxPathIdentity.cs'
        Original = '                if (type == FileTypeSymbolicLink)'
        Mutated = '                if (type == FileTypeSymbolicLink && current.Length == 0)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SymbolicLinksAndNonDirectoryAncestorsAreRejected'
    },
    [pscustomobject]@{
        Name = 'analyzer-rejects-retired-mode'
        File = 'SharpProof.Analyzer.Core\Configuration\AnalyzerConfiguration.cs'
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
    },
    [pscustomobject]@{
        Name = 'relational-source-call-admission'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerRelationalSummaryProvider.cs'
        Original = '        return method.MethodKind == MethodKind.Ordinary &&'
        Mutated = '        return method.MethodKind != MethodKind.Ordinary &&'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DirectAcyclicSourceCallCarriesAReusableRelationalSummary'
    },
    [pscustomobject]@{
        Name = 'relational-implementation-il-admission'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerImplementationIlSummaryLowerer.cs'
        Original = '        if (!IsCandidate(compilation, method))'
        Mutated = '        if (IsCandidate(compilation, method))'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~ExactImplementationIlSummaryProvesAnExternalCallChain'
    },
    [pscustomobject]@{
        Name = 'relational-reference-assembly-rejection'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerImplementationIlSummaryLowerer.cs'
        Original = '        if (IsReferenceAssembly(method.ContainingAssembly))'
        Mutated = '        if (false && IsReferenceAssembly(method.ContainingAssembly))'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~ReferenceAssemblyIsNotImplementationProofAuthority'
    },
    [pscustomobject]@{
        Name = 'relational-spec-pack-explicit-opt-in'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerSpecificationPackProvider.cs'
        Original = '        var selected = (enabledPacks ?? [])'
        Mutated = '        var selected = (enabledPacks ?? []).Concat(catalog.Packs.Keys)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~AuditedSpecificationPackRequiresExplicitOptIn'
    },
    [pscustomobject]@{
        Name = 'relational-transitive-provenance'
        File = 'SharpProof.Summaries\IrRelationalSummaryBuilder.cs'
        Original = '            foreach (var provenance in dependency.DependencyProvenance)'
        Mutated = '            foreach (var provenance in Array.Empty<IrSummaryProvenance>())'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~MixedSourceAndImplementationSummariesSealDependencyEvidence'
    },
    [pscustomobject]@{
        Name = 'relational-instantiation-freshness'
        File = 'SharpProof.Summaries\IrRelationalSummaryInstantiator.cs'
        Original = (@'
        var type = factory.GetVariableInfo(template).Type;
        return factory.CreateVariable(
            "summary:" +
            instanceOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" + role + ":" +
            ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            type);
'@).Trim()
        Mutated = (@'
        _ = factory.GetVariableInfo(template);
        _ = instanceOrdinal;
        _ = role;
        _ = ordinal;
        return template;
'@).Trim()
        Project = 'SharpProof.Summaries.Test\SharpProof.Summaries.Test.csproj'
        Filter = 'FullyQualifiedName~CallCompositionUsesAReusableRelationAndFreshVariables'
    },
    [pscustomobject]@{
        Name = 'relational-worker-assumption-binding'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = (@'
            _summaryAssumptions.Add(new GuardedBodySummaryAssumption(
                prepared.CallIdentity,
                prepared.Origin,
                prepared.EvidenceSha256,
                prepared.EvidenceIdentity,
                guard,
                relation));
'@).Trim()
        Mutated = (@'
            _summaryAssumptions.Add(new GuardedBodySummaryAssumption(
                prepared.CallIdentity,
                prepared.Origin,
                prepared.EvidenceSha256,
                prepared.EvidenceIdentity,
                guard,
                factory.Boolean(true)));
'@).Trim()
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SourceCallContributesItsGuardedRelationalAssumption'
    },
    [pscustomobject]@{
        Name = 'relational-counterexample-classification'
        File = 'SharpProof.Worker\CallableCounterexampleReplayer.cs'
        Original = '                            body.SummaryCalls.ContainsKey(call.Id))'
        Mutated = '                            false)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~ExecutedRelationalSummaryCallIsNotAReplayableCounterexample'
    },
    [pscustomobject]@{
        Name = 'analyzer-partial-executable-owner'
        File = 'SharpProof.Analyzer.Core\AnalyzerFeaturePipeline.cs'
        Original = (@'
        if (method.PartialImplementationPart != null)
        {
            return;
        }
        if (method.PartialDefinitionPart != null &&
'@).TrimEnd()
        Mutated = (@'
        if (false)
        {
            return;
        }
        if (method.PartialDefinitionPart != null &&
'@).TrimEnd()
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~PartialMethodHasOneExecutableEffectOwner'
    },
    [pscustomobject]@{
        Name = 'protocol-exact-run-state-projection'
        File = 'SharpProof.Worker.Protocol\ProtocolJson.cs'
        Original = (@'
                response.RunStatus == expectedStatus &&
                response.FailureReason == expectedFailure,
'@).TrimEnd()
        Mutated = (@'
                WorkerProtocolMetadata.MatchesRunFailure(
                    response.RunStatus, response.FailureReason),
'@).TrimEnd()
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~AllProvenEvidenceRejectsFabricated'
    },
    [pscustomobject]@{
        Name = 'effects-rethrow-nearest-catch-owner'
        File = 'SharpProof.Effects\EffectExceptionFlow.cs'
        Original = (@'
            node.Ancestors()
                .TakeWhile(static ancestor =>
                    ancestor is not AnonymousFunctionExpressionSyntax and
                    not LocalFunctionStatementSyntax)
                .OfType<CatchClauseSyntax>()
                .FirstOrDefault()?.Block == block);
'@).TrimEnd()
        Mutated = (@'
            !node.Ancestors()
                .TakeWhile(ancestor => !ReferenceEquals(ancestor, block))
                .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));
'@).TrimEnd()
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~BareRethrowBelongsOnlyToItsNearestCatch'
    },
    [pscustomobject]@{
        Name = 'release-sbom-exact-license-authority'
        File = 'scripts\Test-SharpProofPackageDependencies.ps1'
        Original = '            [string]$matches[0].licenseDeclared -cne'
        Mutated = '            $false -and [string]$matches[0].licenseDeclared -cne'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~SbomLicensesMatchTheExactPackageAuthority'
    },
    [pscustomobject]@{
        Name = 'release-sbom-exact-release-identity'
        File = 'scripts\Test-SharpProofPackageDependencies.ps1'
        Original = '        [string]$Sbom.name -cne [string]$expected.Name -or'
        Mutated = '        [string]$Sbom.name -ceq [string]$expected.Name -and'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~SbomReleaseIdentityIsExact'
    },
    [pscustomobject]@{
        Name = 'release-third-party-component-projection'
        File = 'scripts\Test-SharpProofPackageDependencies.ps1'
        Original = (@'
    if ($actual.Count -ne $expected.Count -or
        ($actual | ConvertTo-Json -Depth 4 -Compress) -cne
            ($expected | ConvertTo-Json -Depth 4 -Compress)) {
'@).TrimEnd()
        Mutated = '    if ($false) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~ThirdPartyInventoryMatchesCatalogPayloadAndSbomOwnership'
    },
    [pscustomobject]@{
        Name = 'protocol-exact-runtime-provenance'
        File = 'SharpProof.Worker.Protocol\ProtocolJson.cs'
        Original = "                response.Summary?.Versions != null &&`n                VersionsEqual(response.Summary.Versions, expectedVersions),"
        Mutated = '                response.Summary?.Versions != null,'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RequestBoundValidationAuthenticatesRuntimeProvenance'
    },
    [pscustomobject]@{
        Name = 'release-exact-package-role-filenames'
        File = 'scripts\SharpProof.SymbolPackageValidator.cs'
        Original = (@'
        if (!string.Equals(
                Path.GetFileName(path),
                expectedName,
                StringComparison.Ordinal))
'@).TrimEnd()
        Mutated = '        if (false)'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~ReleasePackageRolesAuthenticateNamesArchivesAndNuspecs'
    },
    [pscustomobject]@{
        Name = 'portable-ir-exact-encoder-image'
        File = 'SharpProof.CompilerArtifact\PortableIrGraphCodec.cs'
        Original = '            actual.SequenceEqual(expected),'
        Mutated = '            true,'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DecoderRejectsMetadataOutsideTheCanonicalEncoderImage'
    },
    [pscustomobject]@{
        Name = 'release-exact-spdx-topology'
        File = 'scripts\Test-SharpProofPackageDependencies.ps1'
        Original = (@'
    if ($actualRelationships.Count -ne $expectedRelationships.Count -or
        ($actualRelationships | ConvertTo-Json -Compress) -cne
            ($expectedRelationships | ConvertTo-Json -Compress)) {
'@).TrimEnd()
        Mutated = '    if ($false) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~SbomTopologyIsTheExactAuthenticatedProjection'
    },
    [pscustomobject]@{
        Name = 'release-exact-spdx-checksum-row'
        File = 'scripts\Test-SharpProofPackageDependencies.ps1'
        Original = '    if ($rows.Count -ne 1 -or $null -eq $rows[0]) {'
        Mutated = '    if ($null -eq $rows[0]) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~SpdxChecksumRowsAreExact'
    },
    [pscustomobject]@{
        Name = 'worker-cache-post-publish-rollback'
        File = 'SharpProof.Worker\VerificationCache.cs'
        Original = '                if (published && path != null)'
        Mutated = '                if (false && published && path != null)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~CacheWriteRollsBackPublicationWhenPostValidationIsCanceled'
    },
    [pscustomobject]@{
        Name = 'release-sbom-symbol-checksum-substitution'
        File = 'scripts\Test-SharpProofPackageDependencies.ps1'
        Original = '            -ExpectedSha256 ([string]$main[0].sha256) `'
        Mutated = '            -ExpectedSha256 ([string]$symbol[0].sha256) `'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~SbomSymbolArtifactScopeTests'
    },
    [pscustomobject]@{
        Name = 'compiler-diagnostic-one-based-location'
        File = 'SharpProof.CompilerCollector\CompilerArtifact\CompilerManifestArtifactProducer.cs'
        Original = '                Line = source ? span.StartLinePosition.Line + 1 : 0,'
        Mutated = '                Line = source ? span.StartLinePosition.Line : 0,'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~CompilerDiagnosticLocationsUseOneBasedMappedCoordinates'
    },
    [pscustomobject]@{
        Name = 'release-version-authority-ordinal-comparison'
        File = 'scripts\Get-SharpProofReleaseVersion.ps1'
        Original = '    if (-not $ActualVersion.Equals('
        Mutated = '    if ($false -and -not $ActualVersion.Equals('
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~ReleaseVersionAuthorityTests'
    },
    [pscustomobject]@{
        Name = 'release-publication-plan-topology-alias-guard'
        File = 'scripts\SharpProof.PublicationPlanTopology.ps1'
        Original = (@'
        if ([string]$entry.path -ceq $OutputPath -or
            ($null -ne $outputIdentity -and
             [string]$entry.fileIdentity -ceq
                [string]$outputIdentity.fileIdentity)) {
'@).TrimEnd()
        Mutated = '        if ($false) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~PublicationPlanTopologyTests'
    },
    [pscustomobject]@{
        Name = 'release-publication-destination-mode-exclusivity'
        File = 'scripts\SharpProof.PublicationDestination.ps1'
        Original = '    if ($hasFixture -and ($hasMain -or $hasSymbols)) {'
        Mutated = '    if ($false) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~PublicationDestinationAuthorityTests'
    },
    [pscustomobject]@{
        Name = 'release-symbol-publication-action-authority'
        File = 'scripts\SharpProof.PublicationDestination.ps1'
        Original = "                symbolsAction = 'CollisionOnPush'"
        Mutated = "                symbolsAction = 'PreflightThenPush'"
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~PublicationDestinationModesAreExactAndAuthenticated'
    },
    [pscustomobject]@{
        Name = 'release-checksum-byte-comparison'
        File = 'scripts\SharpProof.ReleaseChecksums.ps1'
        Original = '    if ($actual.Length -ne $expected.Length -or'
        Mutated = '    if ($false -or'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~ReleaseChecksumAuthorityTests'
    },
    [pscustomobject]@{
        Name = 'release-exact-bundle-topology-consumer'
        File = 'scripts\Publish-SharpProofRelease.ps1'
        Original = (@'
    Test-SharpProofReleaseBundleTopology `
        -Directory $Directory `
        -Artifacts $artifacts `
        -Owner 'Publication release bundle'
'@).TrimEnd()
        Mutated = '    # release bundle topology validation removed'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~ReleaseBundleAuthorityGuardsEveryReleaseConsumerAndUpload'
    },
    [pscustomobject]@{
        Name = 'acceptance-restore-timeline-owner'
        File = 'eng\acceptance\Verify.ps1'
        Original = "Start-AcceptanceTimingPhase -Name 'restore'"
        Mutated = "Start-AcceptanceTimingPhase -Name 'static-validation'"
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~AcceptanceScriptOwnsRestoreInsideOuterTimeline'
    },
    [pscustomobject]@{
        Name = 'compiler-diagnostic-reserved-namespace'
        File = 'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs'
        Original = '            WorkerProtocolJson.IsCompilerDiagnosticCode(item.Code) &&'
        Mutated = '            true &&'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~CompilerDiagnosticCodesRequireTheExactReservedNamespace'
    },
    [pscustomobject]@{
        Name = 'publication-plan-strict-sbom-semantics'
        File = 'scripts\Publish-SharpProofRelease.ps1'
        Original = '    Test-SharpProofSbomTopology `'
        Mutated = '    # strict SBOM topology validation removed'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~PublicationPlanConsumesStrictReleaseSemanticsBeforeActions'
    },
    [pscustomobject]@{
        Name = 'documentation-support-contract-disconnected'
        File = 'eng\acceptance\Verify.ps1'
        Original = "& (Join-Path `$repositoryRoot 'scripts\Generate-Readme.ps1') -Verify"
        Mutated = '# documentation support-contract validation removed'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~DocumentationGatePrecedesPackagingAndReleaseEvidence'
    },
    [pscustomobject]@{
        Name = 'documentation-contract-api-silence-guard'
        File = 'scripts\Generate-Readme.ps1'
        Original = "    'disable contract analysis without a diagnostic',"
        Mutated = "    'unrelated stale claim',"
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~DocumentationSupportContractRejectsDrift'
    },
    [pscustomobject]@{
        Name = 'documentation-resource-concurrency-claim-count'
        File = 'scripts\Generate-Readme.ps1'
        Original = '        if ($claimCount -cne 1) {'
        Mutated = '        if ($false) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~DocumentationSupportContractRejectsDrift'
    },
    [pscustomobject]@{
        Name = 'dev-check-command-plan-package-build'
        File = 'scripts\Get-SharpProofDevCheckPlan.ps1'
        Original = "    Add-Command 'package-test-build' 'package-tests' `$Configuration `$false"
        Mutated = '    # package-test build removed from command plan'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~CommandPlanOwnsConfigurationSpecificBuildGraph'
    },
    [pscustomobject]@{
        Name = 'documentation-typed-effect-result-block'
        File = 'scripts\Generate-Readme.ps1'
        Original = '    $typedBlocks[0].Value -cne $expectedTypedResultBlock) {'
        Mutated = '    $false) {'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~DocumentationSupportContractRejectsDrift'
    },
    [pscustomobject]@{
        Name = 'api-spec-exact-null-substitution-type'
        File = 'SharpProof.Specs\ApiSpecInstantiation.cs'
        Original = '                ? new(factory.Null(peer.Term.Type), null)'
        Mutated = '                ? new(factory.Null(factory.ObjectType), null)'
        Project = 'SharpProof.Specs.Test\SharpProof.Specs.Test.csproj'
        Filter = 'FullyQualifiedName~ReferenceNullUsesTheExactSubstitutedOperandType'
    },
    [pscustomobject]@{
        Name = 'managed-flow-raw-syntactic-cycle'
        File = 'SharpProof.Effects\ManagedAbstractFlow.cs'
        Original = '        return !expected.HasValue ||'
        Mutated = '        return expected.HasValue ||'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~CompileTimeLoopConditionsControlReachabilityAndTermination'
    },
    [pscustomobject]@{
        Name = 'property-symbol-only-dispatch'
        File = 'SharpProof.Effects\PropertyDispatchFacts.cs'
        Original = "        return !IsStaticallyBound(property) &&`n               IsSymbolDispatchUncertain(accessor);"
        Mutated = '        return IsSymbolDispatchUncertain(accessor);'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~PropertyDispatchUsesTheOperationReceiver'
    },
    [pscustomobject]@{
        Name = 'implicit-empty-constructor-modeling'
        File = 'SharpProof.Effects\EffectCallSiteResolver.cs'
        Original = '        while (EffectMethodNodeBuilder.IsProvablyEmptyImplicitConstructorLayer('
        Mutated = '        while (false && EffectMethodNodeBuilder.IsProvablyEmptyImplicitConstructorLayer('
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~ProvablyEmptyImplicitConstructorsAreModeledExactly'
    },
    [pscustomobject]@{
        Name = 'container-archive-source-materialization'
        File = 'eng\container\entrypoint.sh'
        Original = '    if [[ "${source_has_git}" = "true" ]]; then'
        Mutated = '    if [[ "true" = "true" ]]; then'
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~FiniteCommandsRunFromAnArchiveWithoutGit'
    },
    [pscustomobject]@{
        Name = 'standalone-build-stage-nonroot-contract'
        File = 'eng\container\Dockerfile'
        Original = "COPY --chown=sharpproof:sharpproof . .`nUSER sharpproof"
        Mutated = "COPY --chown=sharpproof:sharpproof . .`nUSER root"
        Project = 'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj'
        Filter = 'FullyQualifiedName~NamedStagesHaveStandaloneNonRootExecutionContracts'
    }
)

$acceptanceContract = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng\acceptance\contract.json') -Raw |
    ConvertFrom-Json
$mutationPolicy = $acceptanceContract.mutationEvidence
$catalogCount = @($mutations).Count
$catalogSha256 = Get-SharpProofMutationCatalogSha256 -Mutations $mutations
if ($catalogCount -ne [int]$mutationPolicy.expectedCatalogCount -or
    $catalogSha256 -ne [string]$mutationPolicy.expectedCatalogSha256) {
    throw (
        'Trusted mutation registrations do not match the acceptance ' +
        'catalog policy. Actual count/digest: ' +
        "$catalogCount/$catalogSha256.")
}

$invalidTargets = [Collections.Generic.List[string]]::new()
foreach ($mutation in $mutations) {
    $targetPath = Join-Path $repositoryRoot ([string]$mutation.File)
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        $invalidTargets.Add(
            ([string]$mutation.Name) + ': target file was not found')
        continue
    }

    $content = Get-Content -LiteralPath $targetPath -Raw
    $needle = [string]$mutation.Original
    $first = $content.IndexOf($needle, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        $invalidTargets.Add(
            ([string]$mutation.Name) + ': target text was not found')
        continue
    }

    if ($content.IndexOf(
            $needle,
            $first + $needle.Length,
            [StringComparison]::Ordinal) -ge 0) {
        $invalidTargets.Add(
            ([string]$mutation.Name) + ': target text was not unique')
    }
}
if ($invalidTargets.Count -ne 0) {
    throw (
        "Trusted mutation target preflight failed:`n - " +
        ($invalidTargets -join "`n - "))
}

& git -C $repositoryRoot diff --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked working tree.'
}
& git -C $repositoryRoot diff --cached --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked index.'
}

$defaultShardWeight = [int]$acceptanceContract.automation.mutationDefaultWeight
if ($defaultShardWeight -lt 1) {
    throw 'The default mutation shard weight must be positive.'
}
$projectWeights = @{}
foreach ($property in @(
        $acceptanceContract.automation.mutationProjectWeights.PSObject.Properties)) {
    $weight = [int]$property.Value
    if ($weight -lt 1) {
        throw "Mutation project weight must be positive: $($property.Name)."
    }
    $projectWeights[[string]$property.Name] = $weight
}
if ($MutationName.Count -gt 0 -and $MutationShardCount -ne 1) {
    throw 'Named mutation selection cannot be combined with catalog sharding.'
}
if ($MutationShardIndex -ge $MutationShardCount) {
    throw 'MutationShardIndex must be less than MutationShardCount.'
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
elseif ($MutationShardCount -gt 1) {
    $selection = 'selected'
    $plan = Get-SharpProofWeightedMutationShards `
        -Mutations $mutations `
        -ShardCount $MutationShardCount `
        -ProjectWeights $projectWeights `
        -DefaultWeight $defaultShardWeight
    $selected = @($plan.Shards[$MutationShardIndex])
    $mutations = @($selected | ForEach-Object {
        $_.Mutation | Add-Member -NotePropertyName CatalogOrdinal `
            -NotePropertyValue ([int]$_.CatalogOrdinal) -PassThru
    })
    if ($mutations.Count -eq 0) {
        throw "Mutation shard $MutationShardIndex is empty."
    }
}
else {
    $selection = 'full'
}

if ($Resume -and $selection -ne 'full') {
    throw 'Resume is supported only for the complete mutation catalog.'
}

$completedResults = @()
if ($Resume -and (Test-Path -LiteralPath $output -PathType Leaf)) {
    $checkpoint = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $checkpointMutations = @($checkpoint.mutations)
    if ([int]$checkpoint.schemaVersion -ne 2 -or
        [string]$checkpoint.commit -ne $sourceCommit -or
        [string]$checkpoint.configuration -ne $Configuration -or
        [string]$checkpoint.selection -notin @('inProgress', 'full') -or
        [int]$checkpoint.catalogCount -ne $catalogCount -or
        [string]$checkpoint.catalogSha256 -ne $catalogSha256 -or
        [int]$checkpoint.mutationCount -ne $checkpointMutations.Count -or
        [int]$checkpoint.killedCount -ne $checkpointMutations.Count) {
        throw 'Mutation checkpoint does not match the current exact catalog run.'
    }

    if ($checkpointMutations.Count -gt $mutations.Count) {
        throw 'Mutation checkpoint contains more results than the catalog.'
    }
    for ($index = 0; $index -lt $checkpointMutations.Count; $index++) {
        $result = $checkpointMutations[$index]
        $registered = $mutations[$index]
        $name = [string]$result.name
        if ($name -ne [string]$registered.Name) {
            throw 'Mutation checkpoint is not a canonical catalog prefix.'
        }
        if ([string]$result.file -ne ([string]$registered.File).Replace('\', '/') -or
            [string]$result.project -ne ([string]$registered.Project).Replace('\', '/') -or
            [string]$result.test -ne [string]$registered.Filter -or
            [string]$result.original -ne [string]$registered.Original -or
            [string]$result.mutated -ne [string]$registered.Mutated -or
            -not [bool]$result.killed -or
            [int]$result.exitCode -eq 0 -or
            [int]$result.assertionFailureCount -lt 1 -or
            [string]$result.assertionProvenanceSha256 -notmatch
                '^[0-9a-f]{64}$' -or
            [string]$result.baselineInvocationSha256 -ne
                (Get-SharpProofMutationBaselineInvocation `
                    -Project ([string]$registered.Project) `
                    -Filter ([string]$registered.Filter) `
                    -Configuration $Configuration).Sha256 -or
            @($result.baselineSelectedTests).Count -eq 0 -or
            [string]$result.baselineTrxSha256 -notmatch '^[0-9a-f]{64}$') {
            throw 'Mutation checkpoint result does not match its catalog entry.'
        }
    }
    $completedResults = $checkpointMutations
    if ([string]$checkpoint.selection -eq 'full') {
        if ($completedResults.Count -ne $catalogCount) {
            throw 'Completed mutation evidence does not cover the full catalog.'
        }
        & (Join-Path $PSScriptRoot 'Test-SharpProofMutationCatalog.ps1') `
            -EvidencePath $output `
            -ExpectedCommit $sourceCommit
        Write-Host "Mutation evidence is already complete: $output"
        return
    }
}

$completedMutationNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($result in $completedResults) {
    [void]$completedMutationNames.Add([string]$result.name)
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
if (-not $Resume) {
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
}
$restoreElapsedMilliseconds = 0L
$baselineElapsedMilliseconds = 0L
$mutationElapsedMilliseconds = 0L
$baselineInvocationCount = 0
$mutationInvocationCount = 0
$mutationTimings = [Collections.Generic.List[object]]::new()
$lastInvocationElapsedMilliseconds = 0L

function Invoke-IsolatedDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$LogName
    )

    $log = Join-Path $logs $LogName
    $timer = [Diagnostics.Stopwatch]::StartNew()
    Push-Location $sourceRoot
    try {
        & (Join-Path $sourceRoot 'scripts\Invoke-SharpProofDotnet.ps1') `
            -TimeoutSeconds 600 `
            @Arguments *> $log
        return $LASTEXITCODE
    }
    finally {
        $timer.Stop()
        $script:lastInvocationElapsedMilliseconds =
            [long]$timer.Elapsed.TotalMilliseconds
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

function Write-MutationEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Results,

        [Parameter(Mandatory = $true)]
        [ValidateSet('inProgress', 'selected', 'full')]
        [string]$EvidenceSelection
    )

    $outputDirectory = Split-Path -Parent $output
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $temporaryOutput =
        $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [pscustomobject]@{
        schemaVersion = 2
        commit = $sourceCommit
        configuration = $Configuration
        selection = $EvidenceSelection
        catalogCount = $catalogCount
        catalogSha256 = $catalogSha256
        mutationCount = $Results.Count
        killedCount = @($Results | Where-Object killed).Count
        mutations = $Results
        timing = [ordered]@{
            restoreElapsedMilliseconds = $restoreElapsedMilliseconds
            baselineElapsedMilliseconds = $baselineElapsedMilliseconds
            mutationElapsedMilliseconds = $mutationElapsedMilliseconds
            baselineInvocationCount = $baselineInvocationCount
            mutationInvocationCount = $mutationInvocationCount
            mutations = @($mutationTimings)
        }
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $temporaryOutput -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporaryOutput -Destination $output -Force
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

    foreach ($mutation in $mutations) {
        $path = Join-Path $sourceRoot $mutation.File
        $content = [IO.File]::ReadAllText($path)
        Assert-UniqueMutationTarget `
            -Content $content `
            -Needle $mutation.Original `
            -Name $mutation.Name
    }

    $restoreExit = Invoke-IsolatedDotnet `
        -Arguments @('restore', 'SharpProof.sln') `
        -LogName 'restore.log'
    $restoreElapsedMilliseconds = $lastInvocationElapsedMilliseconds
    if ($restoreExit -ne 0) {
        throw "Mutation workspace restore failed; see $logs\restore.log."
    }

    $pendingMutations = @($mutations | Where-Object {
            -not $completedMutationNames.Contains([string]$_.Name)
        })
    if ($null -ne $baselineFile -and -not $BaselineOnly) {
        if (-not (Test-Path -LiteralPath $baselineFile -PathType Leaf)) {
            throw "Mutation baseline evidence is missing: $baselineFile"
        }
        $savedBaseline = Get-Content -LiteralPath $baselineFile -Raw |
            ConvertFrom-Json
        $savedTests = @($savedBaseline.tests)
        if ([int]$savedBaseline.schemaVersion -ne 2 -or
            [string]$savedBaseline.commit -ne $sourceCommit -or
            [string]$savedBaseline.configuration -ne $Configuration -or
            [string]$savedBaseline.selection -notin @('full', 'selected') -or
            [int]$savedBaseline.catalogCount -ne $catalogCount -or
            [string]$savedBaseline.catalogSha256 -ne $catalogSha256 -or
            [int]$savedBaseline.testCount -ne $savedTests.Count) {
            throw 'Mutation baseline evidence does not match this campaign.'
        }
        $baselineMap = [Collections.Generic.Dictionary[
            string, object]]::new([StringComparer]::Ordinal)
        foreach ($test in $savedTests) {
            $project = [string]$test.project
            $filter = [string]$test.filter
            $ledger = @($test.ledger)
            if ([string]::IsNullOrWhiteSpace($project) -or
                [string]::IsNullOrWhiteSpace($filter) -or
                [string]$test.configuration -ne $Configuration -or
                $ledger.Count -eq 0) {
                throw 'Mutation baseline evidence contains an invalid test row.'
            }
            $invocation = Get-SharpProofMutationBaselineInvocation `
                -Project $project -Filter $filter -Configuration $Configuration
            if ([string]$test.invocationSha256 -ne $invocation.Sha256) {
                throw 'Mutation baseline evidence has a mismatched invocation identity.'
            }
            $baselineRoot = Split-Path -Parent $baselineFile
            $baselineTrxPath = [IO.Path]::GetFullPath((Join-Path `
                    $baselineRoot ([string]$test.trx)))
            if (-not $baselineTrxPath.StartsWith(
                    $baselineRoot + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::Ordinal) -or
                -not [IO.File]::Exists($baselineTrxPath) -or
                [string]$test.trxSha256 -notmatch '^[0-9a-f]{64}$' -or
                (Get-FileHash -LiteralPath $baselineTrxPath -Algorithm SHA256).
                    Hash.ToLowerInvariant() -ne [string]$test.trxSha256) {
                throw 'Mutation baseline evidence has an invalid TRX receipt.'
            }
            $method = $filter.Substring('FullyQualifiedName~'.Length)
            [void](Read-SharpProofMutationTestEvidence `
                    -TrxPath $baselineTrxPath `
                    -EvidenceName ($project + ' saved baseline') `
                    -Mode Baseline `
                    -ProcessExitCode 0 `
                    -ExpectedMethodName $method `
                    -ExpectedLedger $ledger)
            $key = $invocation.Sha256
            if (-not $baselineMap.TryAdd($key, [object]$test)) {
                throw "Mutation baseline evidence duplicates '$project::$filter'."
            }
        }
        foreach ($mutation in $pendingMutations) {
            $invocation = Get-SharpProofMutationBaselineInvocation `
                -Project ([string]$mutation.Project) `
                -Filter ([string]$mutation.Filter) `
                -Configuration $Configuration
            $key = $invocation.Sha256
            if (-not $baselineMap.ContainsKey($key)) {
                throw (
                    "Mutation baseline evidence does not cover " +
                    "'$($mutation.Project)::$($mutation.Filter)'.")
            }
            $saved = $baselineMap[$key]
            $mutation | Add-Member `
                -NotePropertyName BaselineLedger `
                -NotePropertyValue @($saved.ledger)
            $mutation | Add-Member `
                -NotePropertyName BaselineInvocationSha256 `
                -NotePropertyValue $key
            $mutation | Add-Member -NotePropertyName BaselineTrx `
                -NotePropertyValue ([string]$saved.trx)
            $mutation | Add-Member -NotePropertyName BaselineTrxSha256 `
                -NotePropertyValue ([string]$saved.trxSha256)
        }
    }
    else {
        $baselineRows = [Collections.Generic.List[object]]::new()
        $baselineKeys = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $baselineGroupIndex = 0
        $baselinePlan = @(Get-SharpProofMutationBaselinePlan `
                -Mutations $pendingMutations `
                -Configuration $Configuration)
        foreach ($baselineGroup in $baselinePlan) {
            $baselineGroupIndex++
            $projectMutations = @($baselineGroup.Mutations)
            $invocation = $baselineGroup.Invocation
            $expectedMethodName = $invocation.Filter.Substring(
                'FullyQualifiedName~'.Length)
            $baselineTrxName = 'project-' +
                $baselineGroupIndex.ToString(
                    'D2', [Globalization.CultureInfo]::InvariantCulture) +
                '-baseline.trx'
            $baselineTrx = Join-Path $logs $baselineTrxName
            Remove-Item -LiteralPath $baselineTrx `
                -Force -ErrorAction SilentlyContinue
            $baselineExit = Invoke-IsolatedDotnet `
                -Arguments @(
                    'test',
                    $invocation.Project,
                    '-c',
                    $Configuration,
                    '--no-restore',
                    '--filter',
                    $invocation.Filter,
                    '--logger',
                    'console;verbosity=minimal',
                    '--logger',
                    "trx;LogFileName=$baselineTrxName",
                    '--results-directory',
                    $logs) `
                -LogName ('project-' + $baselineGroupIndex.ToString(
                        'D2', [Globalization.CultureInfo]::InvariantCulture) +
                    '-baseline.log')
            $baselineElapsedMilliseconds += $lastInvocationElapsedMilliseconds
            $baselineInvocationCount++
            Assert-SharpProofMutationBaselineResult `
                -ExitCode $baselineExit `
                -TrxPath $baselineTrx `
                -EvidenceName ($invocation.Project + '::' + $invocation.Filter)
            $baselineTestEvidence = Read-SharpProofMutationTestEvidence `
                -TrxPath $baselineTrx `
                -EvidenceName ($invocation.Project + ' baseline') `
                -Mode Baseline `
                -ProcessExitCode $baselineExit `
                -ExpectedMethodName $expectedMethodName
            $ledger = @($baselineTestEvidence.testLedgers[$expectedMethodName])
            $baselineEvidenceRoot = if ($null -ne $baselineFile) {
                Split-Path -Parent $baselineFile
            }
            else {
                Split-Path -Parent $output
            }
            $baselineTrxRelative = [IO.Path]::GetRelativePath(
                $baselineEvidenceRoot,
                $baselineTrx).Replace('\', '/')
            $baselineTrxSha256 = (Get-FileHash -LiteralPath $baselineTrx `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
            foreach ($mutation in $projectMutations) {
                $mutation | Add-Member `
                    -NotePropertyName BaselineLedger `
                    -NotePropertyValue $ledger
                $mutation | Add-Member `
                    -NotePropertyName BaselineInvocationSha256 `
                    -NotePropertyValue $invocation.Sha256
                $mutation | Add-Member -NotePropertyName BaselineTrx `
                    -NotePropertyValue $baselineTrxRelative
                $mutation | Add-Member -NotePropertyName BaselineTrxSha256 `
                    -NotePropertyValue $baselineTrxSha256
                $key = $invocation.Sha256
                if ($baselineKeys.Add($key)) {
                    $baselineRows.Add([pscustomobject]@{
                        project = [string]$mutation.Project
                        filter = [string]$mutation.Filter
                        configuration = $Configuration
                        invocationSha256 = $invocation.Sha256
                        ledger = $ledger
                        trx = $baselineTrxRelative
                        trxSha256 = $baselineTrxSha256
                    })
                }
            }
        }
        if ($BaselineOnly) {
            $baselineParent = Split-Path -Parent $baselineFile
            [IO.Directory]::CreateDirectory($baselineParent) | Out-Null
            $temporaryBaseline = $baselineFile + '.' +
                [Guid]::NewGuid().ToString('N') + '.tmp'
            [pscustomobject]@{
                schemaVersion = 2
                commit = $sourceCommit
                configuration = $Configuration
                selection = $selection
                catalogCount = $catalogCount
                catalogSha256 = $catalogSha256
                testCount = $baselineRows.Count
                tests = @($baselineRows | Sort-Object project, filter)
                timing = [ordered]@{
                    restoreElapsedMilliseconds = $restoreElapsedMilliseconds
                    baselineElapsedMilliseconds = $baselineElapsedMilliseconds
                    baselineInvocationCount = $baselineInvocationCount
                }
            } | ConvertTo-Json -Depth 7 |
                Set-Content -LiteralPath $temporaryBaseline -Encoding utf8NoBOM
            Move-Item -LiteralPath $temporaryBaseline `
                -Destination $baselineFile -Force
            Write-Host (
                "Recorded $($baselineRows.Count) exact mutation baselines " +
                "from $baselineInvocationCount focused invocations.")
            Write-Host "Baseline evidence: $baselineFile"
            return
        }
    }

    $results = @($completedResults)
    foreach ($mutation in $mutations) {
        if ($completedMutationNames.Contains([string]$mutation.Name)) {
            continue
        }
        $path = Join-Path $sourceRoot $mutation.File
        $originalContent = [IO.File]::ReadAllText($path)
        $mutatedContent = $originalContent.Replace(
            $mutation.Original,
            $mutation.Mutated,
            [StringComparison]::Ordinal)
        try {
            [IO.File]::WriteAllText(
                $path,
                $mutatedContent,
                [Text.UTF8Encoding]::new($false))
            $testTrxName = $mutation.Name + '-test.trx'
            $testTrx = Join-Path $logs $testTrxName
            Remove-Item -LiteralPath $testTrx -Force -ErrorAction SilentlyContinue
            $testExit = Invoke-IsolatedDotnet `
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
                    "trx;LogFileName=$testTrxName",
                    '--results-directory',
                    $logs) `
                -LogName ($mutation.Name + '-test.log')
            $mutationElapsedMilliseconds += $lastInvocationElapsedMilliseconds
            $mutationInvocationCount++
            $mutationTimings.Add([pscustomobject]@{
                name = $mutation.Name
                elapsedMilliseconds = $lastInvocationElapsedMilliseconds
            })
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
            if (-not (Test-Path -LiteralPath $testTrx -PathType Leaf)) {
                throw (
                    "Mutation '$($mutation.Name)' did not compile or did " +
                    "not produce test evidence; see " +
                    "$logs\$($mutation.Name)-test.log.")
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
            $result = [pscustomobject]@{
                name = $mutation.Name
                file = $mutation.File.Replace('\', '/')
                test = $mutation.Filter
                project = $mutation.Project.Replace('\', '/')
                original = $mutation.Original
                mutated = $mutation.Mutated
                killed = $true
                exitCode = $testExit
                executedCount = $testEvidence.executedCount
                failedCount = $testEvidence.failedCount
                assertionFailureCount = $testEvidence.assertionFailureCount
                assertionProvenanceSha256 =
                    $testEvidence.assertionProvenanceSha256
                selectedTests = $testEvidence.testLedger
                baselineInvocationSha256 =
                    $mutation.BaselineInvocationSha256
                baselineSelectedTests = @($mutation.BaselineLedger)
                baselineTrx = $mutation.BaselineTrx
                baselineTrxSha256 = $mutation.BaselineTrxSha256
                log = "mutation-logs/$runId/$($mutation.Name)-test.log"
                trx = "mutation-logs/$runId/$testTrxName"
                logSha256 = (Get-FileHash `
                    -LiteralPath $testLog `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
                trxSha256 = (Get-FileHash `
                    -LiteralPath $testTrx `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
            }
            if ($MutationShardCount -gt 1) {
                $result | Add-Member -NotePropertyName catalogOrdinal `
                    -NotePropertyValue ([int]$mutation.CatalogOrdinal)
            }
            $results += $result
            Write-MutationEvidence `
                -Results @($results) `
                -EvidenceSelection inProgress
        }
        finally {
            [IO.File]::WriteAllText(
                $path,
                $originalContent,
                [Text.UTF8Encoding]::new($false))
        }
    }

    $currentCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    & git -C $repositoryRoot diff --quiet --
    $trackedTreeChanged = $LASTEXITCODE -ne 0
    & git -C $repositoryRoot diff --cached --quiet --
    $trackedIndexChanged = $LASTEXITCODE -ne 0
    if ($currentCommit -ne $sourceCommit -or
        $trackedTreeChanged -or $trackedIndexChanged) {
        throw 'Repository identity changed while mutation evidence was produced.'
    }
    Write-MutationEvidence -Results @($results) -EvidenceSelection $selection
    Write-Host "Killed $($results.Count) trusted-boundary mutations."
    Write-Host "Evidence: $output"
}
finally {
    if (-not $KeepWorkspace -and
        (Test-Path -LiteralPath $workspace)) {
        $resolvedWorkspace = [IO.Path]::GetFullPath($workspace)
        $resolvedMutationRoot = [IO.Path]::GetFullPath($mutationRoot)
        [void](Resolve-SharpProofContainedPath `
            -Root $resolvedMutationRoot -Path $resolvedWorkspace `
            -ParameterName 'Mutation workspace')
        Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
    }
}
