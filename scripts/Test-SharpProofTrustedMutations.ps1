[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath = 'artifacts\mutation\summary.json',

    [string[]]$MutationName = @(),

    [switch]$KeepWorkspace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
if (-not $output.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the repository: $output"
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
        Original = '        return value >= 0 && value < values.Length'
        Mutated = '        return value >= 0'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~DecoderRejectsUnknownWireEnumCodes'
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
        Filter = 'FullyQualifiedName~DecoderRejectsNonCanonicalSlots'
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
        Original = "CSharpScalarSemantics.RequiresCheckedArithmetic(operation.OperatorKind) &&`n                !operation.IsChecked)"
        Mutated = "CSharpScalarSemantics.RequiresCheckedArithmetic(operation.OperatorKind) &&`n                operation.IsChecked)"
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~OverflowAndConversionShapesAreExactOnlyWhenRepresentable'
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
        Original = '            IArrayElementReferenceOperation => EffectRegionSet.Unknown,'
        Mutated = '            IArrayElementReferenceOperation element => ClassifyRegion(element.ArrayReference, aliasSource),'
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
        Name = 'advisory-contract-candidate-detection'
        File = 'SharpProof.Frontend\ContractApiMetadata.generated.cs'
        Original = '            "Ensures",'
        Mutated = '            "Requires",'
        Project = 'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj'
        Filter = 'FullyQualifiedName~ContractCandidateActivationRunsClausePlacementValidation'
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
        Original = '!string.Equals(payload.ManifestHash, manifest.Hash, StringComparison.Ordinal) ||'
        Mutated = 'false ||'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RehashedCacheSealedForDifferentManifestMissesAndRecomputes'
    },
    [pscustomobject]@{
        Name = 'protocol-manifest-result-equality'
        File = 'SharpProof.Worker.Protocol\ProtocolJson.cs'
        Original = "actual.OrderBy(static value => value, StringComparer.Ordinal)`n            .SequenceEqual(expected.OrderBy(static value => value, StringComparer.Ordinal),`n                StringComparer.Ordinal)"
        Mutated = 'actual.Concat(expected).All(static _ => true)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~StrictResponseValidationRequiresExactManifestAndResultSets'
    },
    [pscustomobject]@{
        Name = 'launcher-kill-on-close'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = 'NativeMethods.JobObjectLimitFlags.KillOnJobClose |'
        Mutated = 'NativeMethods.JobObjectLimitFlags.ActiveProcess |'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~WorkerContainmentIsMandatoryOnTheSupportedHost'
    }
)

if ($MutationName.Count -gt 0) {
    $knownNames = @($mutations.Name)
    $requestedNames = @($MutationName | Select-Object -Unique)
    $unknownNames = @($requestedNames | Where-Object { $_ -notin $knownNames })
    if ($unknownNames.Count -gt 0) {
        throw "Unknown mutation name(s): $($unknownNames -join ', ')."
    }
    $mutations = @($mutations | Where-Object { $_.Name -in $requestedNames })
}

$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) 'SharpProof-mutation'
$workspace = Join-Path $mutationRoot (
    'workspace-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $workspace 'source'
$archive = Join-Path $workspace 'source.zip'
$logs = Join-Path (Split-Path -Parent $output) 'mutation-logs'
New-Item -ItemType Directory -Path $sourceRoot, $logs -Force | Out-Null

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
        HEAD
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
                'console;verbosity=minimal') `
            -LogName ($mutation.Name + '-baseline.log')
        if ($baselineExit -ne 0) {
            throw (
                "Baseline for mutation '$($mutation.Name)' failed; see " +
                "$logs\$($mutation.Name)-baseline.log.")
        }
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
                    'console;verbosity=minimal') `
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
            $results += [pscustomobject]@{
                name = $mutation.Name
                file = $mutation.File.Replace('\', '/')
                test = $mutation.Filter
                killed = $true
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
    $sha = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    [pscustomobject]@{
        schemaVersion = 1
        commit = $sha
        configuration = $Configuration
        mutationCount = $results.Count
        killedCount = @($results | Where-Object killed).Count
        mutations = $results
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $output -Encoding utf8NoBOM
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
