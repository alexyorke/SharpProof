# Comprehensive immutability audit

Baseline: origin/master, commit 4d456246a4113c6c8f2ff0728ddd03bd684b10e1.
Branch: codex/comprehensive-immutability.

## Scope and ownership

The audit covers all 2,301 baseline tracked files, including 1,805 C# files.
Ten Luna agents at max reasoning perform read-only audits. The coordinator alone
writes this inventory and source, checks findings against definitions and callers,
and runs verification in the canonical Linux amd64 container.

Use immutable collections and value records for fixed data and snapshots, readonly
for construction-only fields, and const for compile-time values. Preserve the
behavior of mutable test subjects, including embedded source programs. Resource
handles, external setter contracts, and stateful algorithms require explicit review;
a readonly collection reference alone does not make its contents immutable.

The existing original checkout is separate and has not been edited by this work.
No vendored code will be changed.

## Verification

- Baseline canonical container solution build: passed, 0 warnings, 0 errors.
- Baseline source inventory: 2,301 files / 1,805 C# files.

## Confirmed violations

The following findings were traced by the coordinator before implementation.
Line numbers refer to the baseline. Status is updated after validation.

| ID | Baseline file / line | Violation and correction | Status |
| --- | --- | --- | --- |
| IMM-001 | `SharpProof.Analyzer.Core/Advisory/AdvisoryEffectSites.cs:39` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-002 | `SharpProof.CompilerCollector/CompilerArtifact/CompilerTotalIlBodyProvider.cs:238` | Fixed opcode lookup stored in a mutable dictionary; materialize an ImmutableDictionary. | Implemented; build and ordinary-suite verification passed |
| IMM-003 | `SharpProof.Gates/Corpus/AllocationIlOracle.cs:10` | Fixed opcode lookup stored in a mutable dictionary; materialize an ImmutableDictionary. | Implemented; build and ordinary-suite verification passed |
| IMM-004 | `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs:10` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-005 | `SharpProof.Host/TrustedChildEnvironment.cs:8` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-006 | `SharpProof.Host/TrustedChildEnvironment.cs:23` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-007 | `SharpProof.Testing/WellSortedIrGenerator.cs:51` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-008 | `SharpProof.Testing/WellSortedIrGenerator.cs:55` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-009 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:527` | Fixed enum membership stored in a mutable HashSet; use ImmutableHashSet. | Implemented; build and ordinary-suite verification passed |
| IMM-010 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:702` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-011 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:715` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-012 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:731` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-013 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:740` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-014 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:749` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-015 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:755` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-016 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:785` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-017 | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:27` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-018 | `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:657` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |
| IMM-019 | `Tools/SharpProof.Fuzz/TotalProgramFuzzing.cs:47` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Implemented; build and ordinary-suite verification passed |

| IMM-020 | `eng/testing/DictionaryAnalyzerConfigOptions.cs:9` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-021 | `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:49` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-022 | `SharpProof.Frontend/RoslynTotalProgramLowerer.Calls.cs:8` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-023 | `SharpProof.Frontend/RoslynTotalProgramLowerer.Calls.cs:9` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-024 | `SharpProof.Gates/Corpus/OpenSourceCorpusRunner.cs:372` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-025 | `SharpProof.Specs/ApiSpecInstantiation.cs:150` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-026 | `SharpProof.Specs/ApiSpecInstantiation.cs:151` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-027 | `SharpProof.Specs/ApiSpecInstantiation.cs:152` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |
| IMM-028 | `SharpProof.Specs/ApiSpecInstantiation.cs:153` | Construction-only guard field remains assignable; declare readonly. | Implemented; build and ordinary-suite verification passed |

| IMM-029 | `SharpProof.Testing/WellSortedIrGenerator.cs:6` | GeneratedIrCase retains a mutable dictionary through IReadOnlyDictionary; require ImmutableDictionary and construct it through persistent Add operations. | Implemented; build and ordinary-suite verification passed |
| IMM-030 | `SharpProof.Testing/WellSortedIrGenerator.cs:34` | InterestingIntegers exposes an array through IReadOnlyList; publish ImmutableArray and use Length at callers. | Implemented; build and ordinary-suite verification passed |

| IMM-031 | `SharpProof.Analyzer.Test/CompilerCaptureRequestWorkflowTests.cs:312` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-032 | `SharpProof.Analyzer.Test/FinalCompilationCollectorTests.cs:2009` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-033 | `SharpProof.Analyzer.Test/GeneratedContractForAnalyzerTests.cs:491` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-034 | `SharpProof.Analyzer.Test/GeneratedContractForAnalyzerTests.cs:492` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-035 | `SharpProof.Contracts.Test/TypedContractLoweringTests.cs:531` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-036 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:250` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-037 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:251` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-038 | `SharpProof.Fuzz.Test/FuzzRunnerTests.cs:616` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-039 | `SharpProof.Gates.Test/OrdinaryRepeatedAnalyzerWorkflowTests.cs:41` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-040 | `SharpProof.Ir.Test/IrFactoryInvariantRegressionTests.cs:280` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-041 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:3316` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-042 | `SharpProof.Smt.Test/CallableSolverSessionTests.cs:467` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-043 | `SharpProof.Smt.Test/NativeSmtRegressionTests.cs:969` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-044 | `SharpProof.Specs.Test/ApiSpecInstantiationCoverageTests.cs:577` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-045 | `SharpProof.Verify.Test/CallableReplayBoundaryTests.cs:193` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-046 | `SharpProof.Verify.Test/CallableTotalReplayTests.cs:217` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-047 | `SharpProof.Worker.Test/CapturedPrimaryConstructorStorageAuditTests.cs:596` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-048 | `SharpProof.Worker.Test/CountUpHoudiniResourceBoundaryTests.cs:385` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-049 | `SharpProof.Worker.Test/CountUpHoudiniResourceBoundaryTests.cs:386` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-050 | `SharpProof.Worker.Test/EmptyIteratorAllocationAuditTests.cs:558` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-051 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:724` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-052 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:11` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-053 | `SharpProof.Worker.Test/NightlyRefutationRegressionTests.cs:151` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-054 | `SharpProof.Worker.Test/PassiveCallableVcTests.cs:882` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-055 | `SharpProof.Worker.Test/WorkerRuntimeBoundaryCoverageTests.cs:111` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |
| IMM-056 | `SharpProof.Worker.Test/WorkerTests.cs:5976` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; build and ordinary-suite verification passed |

| IMM-057 | `SharpProof.Smt/HornInvariantSearch.cs:11` | Horn proposals escape as mutable dictionaries; publish and build ImmutableDictionary. | Implemented; build and ordinary-suite verification passed |
| IMM-058 | `SharpProof.Smt/BvEncoder.References.cs:10` | ReferenceFacts exposes a mutable List; use an immutable list with private persistent updates. Preserve recursive encoding additions. | Implemented; build and ordinary-suite verification passed |
| IMM-059 | `SharpProof.Smt/BvEncoder.cs:81` | Relations exposes a replaceable mutable dictionary; configure mode at construction and expose an immutable map. | Implemented; build and ordinary-suite verification passed |
| IMM-060 | `SharpProof.Contracts/ContractExpressionBinder.cs:40` | PreStateVariables exposes its mutable backing map; use persistent immutable map updates. | Implemented; build and ordinary-suite verification passed |
| IMM-061 | `SharpProof.Contracts/ContractCanonicalization.cs:376` | Canonical Parameters exposes a mutable List; use an immutable parameter sequence. | Implemented; build and ordinary-suite verification passed |
| IMM-062 | `SharpProof.Contracts/ContractForSymbolMatcher.cs:21` | Companion target index returns mutable Dictionary through read-only interface; freeze map and retain symbol comparer. | Implemented; build and ordinary-suite verification passed |
| IMM-063 | `SharpProof.Contracts/ContractForSymbolMatcher.cs:50` | Relationship inventory retains mutable caller-owned HashSets; copy into immutable sets with original comparers. | Implemented; build and ordinary-suite verification passed |
| IMM-064 | `SharpProof.Contracts/ContractClauseInventoryBuilder.cs:11` | Tree ordinals are fixed after construction; use ImmutableDictionary. | Implemented; build and ordinary-suite verification passed |
| IMM-065 | `SharpProof.Contracts/ContractCanonicalization.cs:22` | Type specializer maps are constructed then queried; freeze or use persistent immutable updates. | Implemented; build and ordinary-suite verification passed |
| IMM-066 | `SharpProof.Contracts/ContractCanonicalization.cs:349` | CreateSubstitutions erases an immutable return type; retain ImmutableDictionary in signature. | Implemented; build and ordinary-suite verification passed |
| IMM-067 | `eng/testing/GoldenTest.cs:197` | GoldenCase retains and exposes mutable byte-array snapshots; copy to ImmutableArray preserving missing-vs-empty expectations. | Implemented; build and ordinary-suite verification passed |
| IMM-068 | `scripts/SharpProof.PackageIdentity.psm1:4` | Fixed exported package IDs use a mutable array; publish a read-only or immutable collection. | Implemented; build and ordinary-suite verification passed |
| IMM-069 | `scripts/SharpProof.PackageIdentity.psm1:10` | Fixed push-order array is mutable; publish a read-only or immutable collection. | Implemented; build and ordinary-suite verification passed |
| IMM-070 | `scripts/SharpProof.ContainerExecution.psm1:481` | Fixed nested policy hashtables and override arrays are mutable; freeze every level. | Implemented; build and ordinary-suite verification passed |

| IMM-071 | `SharpProof.Analyzer.Core/CompilerArtifact/CompilerExceptionTypeIdentity.cs:29` | Hierarchy results expose arrays; return sorted ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-072 | `SharpProof.Analyzer.Core/DeclarativeModels.generated.cs:19` | EffectClaimEvaluation retains mutable diagnostic arguments; use ImmutableArray and adapt the Roslyn params boundary. | Implemented; build and ordinary-suite verification passed |
| IMM-073 | `SharpProof.Ir/IrInterpreter.cs:81` | Identity flags are construction-only; initialize readonly fields through a private constructor. | Implemented; build and ordinary-suite verification passed |
| IMM-074 | `SharpProof.Ir/IrSequenceReads.cs:10` | Readers exposes mutable HashSet; build and return ImmutableHashSet and propagate worker field type. | Implemented; build and ordinary-suite verification passed |
| IMM-075 | `SharpProof.Ir/IrSubstitution.cs:142` | Validated replacement map is mutable despite being fixed after capture; use ImmutableDictionary throughout rewrite. | Implemented; build and ordinary-suite verification passed |
| IMM-076 | `SharpProof.Ir/IrInterpreter.cs:920` | Evaluation snapshots are assigned only at construction; capture an immutable set in a get-only property. | Implemented; build and ordinary-suite verification passed |
| IMM-077 | `SharpProof.Ir/IrSequenceReads.cs:13` | Construction-only instruction snapshot uses an array; use ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-078 | `SharpProof.Ir/IrSubstitution.cs:113` | Construction-only root snapshot uses an array; use ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-079 | `SharpProof.BuildTasks/VerificationPublication.cs:8` | Publication path map is fixed after capture; snapshot ImmutableDictionary with ordinal comparer. | Implemented; build and ordinary-suite verification passed |
| IMM-080 | `SharpProof.Worker/Launcher/LauncherProgram.cs:677` | Parsed launcher options retain a mutable dictionary; capture ImmutableDictionary with ordinal comparer. | Implemented; build and ordinary-suite verification passed |
| IMM-081 | `SharpProof.Worker/Vc/PassiveCallableVcBuilder.cs:58` | Call marker lookup is built once; use ImmutableDictionary. | Implemented; build and ordinary-suite verification passed |
| IMM-082 | `SharpProof.Worker/CallableClaimResultAssembler.cs:119` | Effect claim ID set escapes mutable; return ImmutableHashSet. | Implemented; build and ordinary-suite verification passed |
| IMM-083 | `SharpProof.Worker/NativeEffectClaims.cs:96` | UsedRequires returns a mutable set; use persistent ImmutableHashSet. | Implemented; build and ordinary-suite verification passed |
| IMM-084 | `SharpProof.Worker.Protocol/ProtocolJson.cs:1128` | Callable assumption lookup retains dictionary and array values; use immutable map and arrays, preserving first duplicate and null lookup behavior. | Implemented; build and ordinary-suite verification passed |
| IMM-085 | `SharpProof.Worker.Protocol/ProtocolJson.cs:1159` | Identity index maps/nullable identity fields are constructor-only; use immutable map and readonly fields. | Implemented; build and ordinary-suite verification passed |

| IMM-086 | `SharpProof.Frontend/CompilationModelProvider.cs:81` | Tree owner map is constructor-built; snapshot ImmutableDictionary with reference identity comparer. | Implemented; build and ordinary-suite verification passed |
| IMM-087 | `SharpProof.Effects/EffectValues.cs:107` | Readonly throw-set value retains a mutable membership set; freeze it with symbol equality while preserving sorted type order. | Implemented; build and ordinary-suite verification passed |
| IMM-088 | `SharpProof.Frontend/FrontendSubset.cs:180` | Five result metadata properties are assigned only at object construction; use init and the existing netstandard polyfill. | Implemented; build and ordinary-suite verification passed |
| IMM-089 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:285` | Four diagnostic provenance properties are producer-only; initialize once and use init. | Implemented; build and ordinary-suite verification passed |
| IMM-090 | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:154` | Serialization canonicalizes caller-owned objects in place; isolate canonical data before serialization, preserving hashes/wire output. | Implemented; build and ordinary-suite verification passed |
| IMM-091 | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:243` | DecodeCallables overwrites compilation authority fields; hydrate a separate immutable snapshot. | Implemented; build and ordinary-suite verification passed |
| IMM-092 | `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:16` | Import plan retains mutable Updates array; use ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-093 | `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs:25` | Parser result exposes mutable data-line array; publish ImmutableArray and adapt nullable test capture. | Implemented; build and ordinary-suite verification passed |
| IMM-094 | `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs:365` | Source-ID validation returns mutable HashSet; use persistent immutable set and preserve duplicate rejection. | Implemented; build and ordinary-suite verification passed |
| IMM-095 | `Tools/SharpProof.Fuzz/FuzzSourceCaseExecutor.cs:291` | Construction-only private method array; use ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-096 | `Tools/SharpProof.Fuzz/MetadataProgramSourceReplay.cs:41` | Construction-only library/source member arrays; use ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-097 | `SharpProof.Gates/Corpus/CorpusFileTransaction.cs:321` | Transaction journal record retains mutable Entries array; coordinate immutable representation with null/malformed JSON checks. | Implemented; build and ordinary-suite verification passed |
| IMM-098 | `SharpProof.Frontend.Test/RegionFilterEdgeResolverTests.cs:189` | Fixture record retains mutable block map; publish immutable map. | Implemented; build and ordinary-suite verification passed |
| IMM-099 | `SharpProof.Ir.Test/IrIdentityCallbackTests.cs:25` | Comparisons exposes callback accumulator; publish immutable snapshots while preserving callback timing. | Implemented; build and ordinary-suite verification passed |
| IMM-100 | `SharpProof.Worker.Test/LongFuzz/LongFuzzSession.cs:265` | Statistics record exposes mutable sorted dictionaries; publish immutable sorted snapshots. | Implemented; build and ordinary-suite verification passed |
| IMM-101 | `SharpProof.Worker.Test/NativeTotalCandidateCoreTests.cs:362` | CoreRun exposes two producer-built mutable lists; publish ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-102 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:294` | IL observation record exposes mutable instructions; publish ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-103 | `SharpProof.Worker.Test/NullableDefaultBoxingZeroControlAuditTests.cs:353` | IL observation record exposes mutable instructions; publish ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-104 | `SharpProof.Worker.Test/PortableIrGraphCodecTests.cs:1184` | Codec fixture retains mutable Roots array; use ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-105 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:3270` | Source-consumer result exposes mutable assembly-path arrays; publish ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-106 | `SharpProof.Worker.Test/SemanticClaimRelativeSyntaxPathObservationTests.cs:314` | Runtime output record exposes mutable array; publish ImmutableArray preserving JSON array shape. | Implemented; build and ordinary-suite verification passed |
| IMM-107 | `SharpProof.Worker.Test/LongFuzz/TypedFeatureLongFuzzTests.cs:222` | Execution record retains mutable CLR run list; publish ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-108 | `SharpProof.Worker.Test/WorkerTests.cs:5850` | RuntimeContractCase retains read-only input array; publish ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-109 | `SharpProof.Worker.Test/WorkerTests.cs:6338` | TestProject source paths are fixed after construction; publish ImmutableArray. | Implemented; build and ordinary-suite verification passed |
| IMM-110 | `SharpProof.Worker.Test/SynchronizedAutoGetterPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; build and ordinary-suite verification passed |
| IMM-111 | `SharpProof.Worker.Test/SynchronizedBodiedGetterCallerPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; build and ordinary-suite verification passed |
| IMM-112 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; build and ordinary-suite verification passed |
| IMM-113 | `SharpProof.Worker.Test/SynchronizedInstanceMethodPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; build and ordinary-suite verification passed |
| IMM-114 | `SharpProof.Worker.Test/SynchronizedLocalFunctionPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; build and ordinary-suite verification passed |
| IMM-115 | `SharpProof.Worker.Test/SynchronizedMethodPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; build and ordinary-suite verification passed |
| IMM-116 | `SharpProof.Worker.Test/SynchronizedSourceCalleePurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; build and ordinary-suite verification passed |

| IMM-117 | `SharpProof.Analyzer.Test/AnalyzerCoreScalarPromotionBudgetTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-118 | `SharpProof.Contracts.Test/ConstructedGenericContractTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-119 | `SharpProof.Dataflow.Test/FiniteCfgConcreteOracleTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-120 | `SharpProof.Effects.Test/DotnetApiInventory.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-121 | `SharpProof.Frontend.Test/GoldenSourceAnchors.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-122 | `SharpProof.Package.Test/FinalCompilationProbeTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-123 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-124 | `SharpProof.Worker.Test/CapturedPrimaryConstructorStorageAuditTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-125 | `SharpProof.Worker.Test/ConditionalAttributeCompilerAuthorityBoundaryTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-126 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-127 | `SharpProof.Worker.Test/CountUpHoudiniResourceBoundaryTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-128 | `SharpProof.Worker.Test/EmptyIteratorAllocationAuditTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-129 | `SharpProof.Worker.Test/GenericFixedTestedTypeControlAuditTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-130 | `SharpProof.Worker.Test/GenericTestedTypeBoundaryAuditTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-131 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |
| IMM-132 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs` | Coordinator confirmed additional fixture snapshots retain mutable arrays, lists or constructor-built indexes; freeze stored data and adapt producers. Preserve runtime buffers, logs and tested aliases. | Implemented; build and ordinary-suite verification passed |

## Audit coverage and remaining review

All ten initial read-only reports have been received. The coordinator checked the
concrete findings above against definitions and callers. Additional local-state
and serialized-model candidates remain open; this inventory is not yet a claim
that every replaceable mutation has been eliminated.

### Reviewed partitions

- Agent 01: all 12 Dataflow and 33 IR C# files reviewed; result snapshots and constructor-only state traced by coordinator.
- Agent 02: all 38 Analyzer and Analyzer.Core C# files reviewed; a stricter follow-up and coordinator scan record remaining local constructions below.
- Agent 03: compiler artifacts and serialized models reviewed; coordinator traced the serialization and decoding side effects and added three compatibility regressions.
- Agent 05: worker, protocol, host and build-task findings checked for identity, comparer, ordering and mutable-budget contracts.
- Agents 06-08: disjoint test filename partitions reviewed; coordinator independently scanned shared collection declarations, checked producer/caller adapters, and verified original program literals.
- Agent 09: Gates, samples, templates and golden sources reviewed; journal and corpus snapshots converted while golden programs remain unchanged.
- Agent 04: all 13 SMT and 25 Contracts C# files reviewed; ten findings traced by coordinator.
- Agent 10: scripts, eng, root configuration, docs and CI reviewed; initial report missed GoldenCase byte arrays, found by coordinator cross-check and confirmed on follow-up. Four additional findings recorded above.

### Required mutable behavior verified so far

- `SharpProof.Worker.Test/WorkerResultAssemblerTests.cs:116`: `AssemblySnapshotsMutableBudgets` mutates the original budget to test snapshot isolation. Preserve this explicit mutable test subject.
- Mutable C# snippets and tutorial state examples retain their original semantics.
- Z3 native handles, disposal state, resource counters, and concurrency/lifecycle contracts require state transitions; immutable collection contents do not make those external resources immutable.

### Shared test-fixture collection inventory

Coordinator scan of all test projects found 306 fixed/shared collection declarations.
All declaration locations are outside embedded raw source and have no direct
element/collection writes in their declaring file. All 306 stored collections now use immutable types. Fixed expected data remains
immutable even where an API requires an array adapter. Two nested alias-identity
controls retain mutable inner arrays, while their shared outer tables are immutable.
The original programs and explicit mutable subjects remain intact.

| ID | Baseline location | Field and baseline type | Status |
| --- | --- | --- | --- |
| FIX-001 | `SharpProof.Analyzer.Test/AnalyzerArchitectureTests.cs:13` | `ExpectedAnalyzerReferences`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-002 | `SharpProof.Analyzer.Test/AnalyzerArchitectureTests.cs:25` | `ExpectedAnalyzerOutputAssemblies`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-003 | `SharpProof.Analyzer.Test/AnalyzerCoreFunctionPointerDispatchDomainTests.cs:9` | `DispatchControls`: `(string TypeName, int Expected)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-004 | `SharpProof.Analyzer.Test/AnalyzerCoreMalformedCalleeDeclarationTests.cs:14` | `MissingBodyErrorIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-005 | `SharpProof.Analyzer.Test/AnalyzerCoreMissingFieldInitializerCoverageTests.cs:11` | `MissingValueCompilerErrors`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-006 | `SharpProof.Analyzer.Test/AnalyzerCoreScalarPromotionBudgetTests.cs:16` | `s_cases`: `FrozenCase[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-007 | `SharpProof.Analyzer.Test/CompilerCaptureRequestWorkflowTests.cs:13` | `SelectedPackIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-008 | `SharpProof.Analyzer.Test/CompilerLinkedModuleCountWorkflowTests.cs:11` | `ExpectedModuleNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-009 | `SharpProof.Analyzer.Test/CompilerMetadataAliasWorkflowTests.cs:13` | `AlternateAlias`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-010 | `SharpProof.Analyzer.Test/ContractForCycleAnalyzerTests.cs:11` | `DiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-011 | `SharpProof.Analyzer.Test/InitializerIntrinsicConstructorMatrixAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-012 | `SharpProof.Analyzer.Test/InitializerIntrinsicPlacementAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-013 | `SharpProof.Analyzer.Test/PrimaryConstructorIntrinsicPlacementAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-014 | `SharpProof.Analyzer.Test/PrimaryConstructorIntrinsicPlacementBoundaryAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-015 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:11` | `AllowedReferences`: `Dictionary<string, string[]>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-016 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:66` | `NonProductionProjects`: `HashSet<string>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-017 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:77` | `CompilerFacingProjects`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-018 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:88` | `Z3Consumers`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-019 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingAdditionalDomainTests.cs:11` | `PredicateInputs`: `long[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-020 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingAdditionalDomainTests.cs:12` | `MarkerNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-021 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingAdditionalDomainTests.cs:14` | `MetadataIntegerDiagnostics`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-022 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingComponentTests.cs:11` | `Modes`: `IrExecutionSemantics[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-023 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingComponentTests.cs:12` | `ExpectedDiagnostics`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-024 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingComponentTests.cs:13` | `Inputs`: `long[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-025 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:12` | `TopLevelInventoryKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-026 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:16` | `InventoryKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-027 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:24` | `InventoryPlacements`: `ContractClausePlacement[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-028 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:32` | `InventoryOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-029 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:33` | `MisplacedPlacements`: `ContractClausePlacement[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-030 | `SharpProof.Contracts.Test/ContractInvocationMaterializationBoundaryTests.cs:14` | `BooleanValues`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-031 | `SharpProof.Contracts.Test/ContractInvocationMaterializationBoundaryTests.cs:15` | `FullKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-032 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:14` | `BooleanValues`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-033 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:15` | `BooleanClauseKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-034 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:21` | `RequiresOnlyKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-035 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:22` | `CompanionClauseKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-036 | `SharpProof.Contracts.Test/ContractInvocationMaterializationFacadeBaselineTests.cs:11` | `BooleanValues`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-037 | `SharpProof.Contracts.Test/ContractInvocationMaterializationFacadeBaselineTests.cs:12` | `FullKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-038 | `SharpProof.Contracts.Test/ContractInvocationMaterializationFacadeBaselineTests.cs:18` | `RequiresOnlyKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-039 | `SharpProof.Contracts.Test/ContractVariableTransferBoundaryTests.cs:13` | `BooleanValues`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-040 | `SharpProof.Contracts.Test/ExternAliasClosedAttributeBindingTests.cs:13` | `Inputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-041 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:13` | `IntegerDomains`: `(SpecialType Type, int Width, bool Signed)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-042 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:26` | `IntegerBoundarySamples`: `long[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-043 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:39` | `LongBoundParameterOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-044 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:40` | `ReferenceParameterOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-045 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:41` | `ReferenceValueKinds`: `IrTypeKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-046 | `SharpProof.Contracts.Test/MetadataValueProjectionRepresentativeTests.cs:12` | `SingleAttributeNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-047 | `SharpProof.Contracts.Test/MetadataValueProjectionRepresentativeTests.cs:13` | `SingleParameterOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-048 | `SharpProof.Contracts.Test/MetadataValueProjectionRepresentativeTests.cs:14` | `PairParameterOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-049 | `SharpProof.Contracts.Test/TotalClosedAttributeBoundaryMatrixTests.cs:17` | `Types`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-050 | `SharpProof.Contracts.Test/TotalContractInvocationMaterializationAdditionalMatrixTests.cs:14` | `ThreeClauseKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-051 | `SharpProof.Contracts.Test/TotalContractInvocationMaterializationAdditionalMatrixTests.cs:22` | `RequiresOnlyKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-052 | `SharpProof.Contracts.Test/TotalContractInvocationMaterializationAdditionalMatrixTests.cs:23` | `OrderedDiagnosticTexts`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-053 | `SharpProof.Dataflow.Test/GeneratedDomainPropertyTests.cs:249` | `Moduli`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-054 | `SharpProof.Dataflow.Test/NullnessDomainTests.cs:6` | `Samples`: `NullnessValue[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-055 | `SharpProof.Effects.Test/DotnetApiInventoryTests.Comparisons.cs:9` | `IntegerComparisonEdges`: `(string Type, string[] Values)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-056 | `SharpProof.Effects.Test/DotnetApiInventoryTests.TestEvidence.cs:11` | `EqualityEdges`: `(string? Left, string? Right)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-057 | `SharpProof.Effects.Test/ExternalEffectBoundaryTests.cs:8` | `s_exceptionNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-058 | `SharpProof.Frontend.Test/BulkSourceApiDomainCoverageTests.cs:13` | `EnabledSymbols`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-059 | `SharpProof.Frontend.Test/BulkSourceApiDomainCoverageTests.cs:14` | `ConditionalNullErrors`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-060 | `SharpProof.Frontend.Test/CompilerErrorBinaryApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-061 | `SharpProof.Frontend.Test/CompilerErrorCoalesceNullApiCoverageTests.cs:14` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-062 | `SharpProof.Frontend.Test/CompilerErrorConditionalApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-063 | `SharpProof.Frontend.Test/CompilerErrorEmptyConcatenationApiTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-064 | `SharpProof.Frontend.Test/CompilerErrorNameOfFieldAssignmentApiTests.cs:14` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-065 | `SharpProof.Frontend.Test/CompilerErrorReferenceNullComparisonApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-066 | `SharpProof.Frontend.Test/CompilerErrorUnaryApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-067 | `SharpProof.Frontend.Test/CompilerErrorUnaryPlusApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-068 | `SharpProof.Frontend.Test/ConditionalInvalidReturnCoverageTests.cs:17` | `VoidReturnErrors`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-069 | `SharpProof.Frontend.Test/ConditionalInvalidReturnCoverageTests.cs:18` | `NonVoidConditionalErrors`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-070 | `SharpProof.Frontend.Test/ConditionalMetadataSymbolReaderTests.cs:13` | `ExpectedSymbols`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-071 | `SharpProof.Frontend.Test/ConditionalMethodIdentityPolicyTests.cs:17` | `DuplicateErrors`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-072 | `SharpProof.Frontend.Test/CSharpScalarSemanticsTests.cs:12` | `ExpectedIntegerTypes`: `Dictionary<SpecialType, Type>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-073 | `SharpProof.Frontend.Test/ElementGuardNarrowInputCoverageTests.cs:10` | `ExpectedFaults`: `IrExceptionKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-074 | `SharpProof.Frontend.Test/ExceptionKindBoundaryCoverageTests.cs:13` | `OverflowKinds`: `IrExceptionKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-075 | `SharpProof.Frontend.Test/ExplicitLayoutFieldAdmissionTests.cs:19` | `Int32FieldSignature`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-076 | `SharpProof.Frontend.Test/ForeignSyntaxTreeSymbolHelperCoverageTests.cs:10` | `DefinedSymbols`: `KeyValuePair<string, object>[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-077 | `SharpProof.Frontend.Test/FrontendBoundaryCoverageTests.cs:12` | `ExpectedDefinedSymbols`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-078 | `SharpProof.Frontend.Test/FrontendLoweringTests.cs:16` | `ElementValues`: `long[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-079 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:22` | `EnabledSymbols`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-080 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:23` | `OmittedTargetIl`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-081 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:24` | `UnderlyingArities`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-082 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:25` | `NativeArities`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-083 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:190` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-084 | `SharpProof.Frontend.Test/NativeIntegerConditionalEmissionApiTests.cs:23` | `EnabledSymbols`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-085 | `SharpProof.Frontend.Test/NativeIntegerConditionalEmissionApiTests.cs:259` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-086 | `SharpProof.Frontend.Test/NestedLocalConditionalErrorCoverageTests.cs:15` | `ExpectedErrorIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-087 | `SharpProof.Frontend.Test/NullValueCompilerApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-088 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:11` | `ExpectedBatchResults`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-089 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:12` | `ExpectedBatchCalls`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-090 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:13` | `ExpectedReentryCalls`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-091 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:14` | `ExpectedBothScopeCalls`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-092 | `SharpProof.Frontend.Test/OwnedVoidReturnRecoveryCoverageTests.cs:16` | `RecoveryErrorIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-093 | `SharpProof.Frontend.Test/Pr275BulkFrontendApiTests.cs:13` | `ExpectedShortArrayFaults`: `IrExceptionKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-094 | `SharpProof.Frontend.Test/ReferencedTypeSymbolsTests.cs:11` | `ExpectedTraversalOrder`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-095 | `SharpProof.Frontend.Test/RequiresOnlyNestedInitializerContinuationTests.cs:17` | `RuntimeInputs`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-096 | `SharpProof.Frontend.Test/RequiresOnlySwitchArmScopeTests.cs:21` | `expectedClrReturns`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-097 | `SharpProof.Frontend.Test/RequiresOnlyTerminalSpecificationTests.cs:17` | `RuntimeInputs`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-098 | `SharpProof.Frontend.Test/ScalarPatternConstantApiCoverageTests.cs:14` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-099 | `SharpProof.Frontend.Test/SourceNativeIntegerConditionalEmissionApiTests.cs:19` | `EnabledSymbols`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-100 | `SharpProof.Frontend.Test/SourceNativeIntegerConditionalEmissionApiTests.cs:20` | `OmittedTargetIl`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-101 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:44` | `Categories`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-102 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:45` | `JoinTable`: `string?[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-103 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:54` | `ReferenceTable`: `bool[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-104 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:64` | `AcceptanceTable`: `bool[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-105 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:73` | `Targets`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-106 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:74` | `TargetColumns`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-107 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:75` | `Methods`: `Dictionary<string, string>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-108 | `SharpProof.Frontend.Test/SwitchExpressionTerminalElisionTests.cs:20` | `EnabledSymbols`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-109 | `SharpProof.Frontend.Test/SwitchExpressionTerminalElisionTests.cs:21` | `Inputs`: `(int Value, int Other)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-110 | `SharpProof.Frontend.Test/ValidatedArrayInitializerDepthCoverageTests.cs:13` | `Inputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-111 | `SharpProof.Frontend.Test/ValueTypeValidationBoundaryTests.cs:13` | `CSharpError`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-112 | `SharpProof.Frontend.Test/ValueTypeValidationBoundaryTests.cs:14` | `VisualBasicError`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-113 | `SharpProof.Frontend.Test/VisualBasicObjectShiftCountCoverageTests.cs:17` | `RuntimeMethodNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-114 | `SharpProof.Frontend.Test/VisualBasicPrintStatementRecoveryApiTests.cs:16` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-115 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:11` | `SequenceValue`: `long[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-116 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:12` | `UnsupportedSequenceValue`: `ulong[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-117 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeTopologyControls.cs:13` | `OverloadedNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-118 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeTopologyControls.cs:14` | `OverloadedParameterCounts`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-119 | `SharpProof.Fuzz.Test/FuzzEmissionLifecycleTests.cs:17` | `ParameterBindingMethodNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-120 | `SharpProof.Fuzz.Test/FuzzSourceBooleanInputControls.cs:13` | `FalsePair`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-121 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:13` | `FrontendStatuses`: `FuzzOracleStatus[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-122 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:15` | `FiniteStatuses`: `FuzzOracleStatus[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-123 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:17` | `PartialStatuses`: `FuzzOracleStatus[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-124 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:19` | `FailureKeys`: `(int Case, string Oracle)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-125 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:21` | `FaultMethodNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-126 | `SharpProof.Gates.Test/CorpusTransactionWorkflowTests.cs:9` | `ExpectedFiles`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-127 | `SharpProof.Gates.Test/CorpusWallTimePolicyTests.cs:9` | `ExpectedFailure`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-128 | `SharpProof.Gates.Test/OrdinaryCorpusDiagnosticWorkflowTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-129 | `SharpProof.Gates.Test/OrdinaryPartialDiagnosticRegressionTests.cs:17` | `ExpectedDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-130 | `SharpProof.Gates.Test/OrdinaryReferenceEmissionWorkflowTests.cs:11` | `ExpectedAttributeNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-131 | `SharpProof.Ir.Test/IrIdentifierTests.cs:9` | `s_identifierPrefixes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-132 | `SharpProof.Ir.Test/IrTerminatorAdmissionTests.cs:8` | `s_terminalKinds`: `IrInstructionKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-133 | `SharpProof.Package.Test/PackagedProductFeed.cs:29` | `s_expectedPackageIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-134 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:36` | `ExpectedAnalyzerEntryFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-135 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:40` | `ExpectedCodeFixEntryFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-136 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:44` | `ExpectedGeneratorEntryFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-137 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:48` | `ExpectedAnalyzerDependencyFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-138 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:69` | `ExpectedCollectorEntryFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-139 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:73` | `ExpectedSourceAnalyzerProjectFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-140 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:81` | `ExpectedCollectorDependencyFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-141 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:90` | `ExpectedConditionalAnalyzerEntries`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-142 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:118` | `ExpectedToolEntries`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-143 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:136` | `ExpectedNativeZ3Entries`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-144 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:143` | `ExpectedDependencyAttributes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-145 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:25` | `s_publicPolicyProperties`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-146 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:32` | `s_analyzerConfigurationProperties`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-147 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:36` | `s_compilerManifestProperties`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-148 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:41` | `s_reconstructionArguments`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-149 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:47` | `s_reconstructionListArtifacts`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-150 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:53` | `s_manualConsumerElementOrder`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-151 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:63` | `s_manualConsumerImportOrder`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-152 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:69` | `s_runtimeClosureProperties`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-153 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:74` | `s_removedPackagePathProperties`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-154 | `SharpProof.Worker.Test/ActualOwnedSessionEntryCompletionTests.cs:22` | `RuntimeControlArguments`: `object[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-155 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:29` | `RetainedInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-156 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:30` | `ZeroInput`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-157 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:31` | `OneInput`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-158 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:33` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-159 | `SharpProof.Worker.Test/AnalyzerCoreAdvisoryLanguageBoundaryTests.cs:16` | `ExpectedIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-160 | `SharpProof.Worker.Test/AnalyzerCorePublicDriverSharedTreeBoundaryTests.cs:13` | `RefusalDiagnosticIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-161 | `SharpProof.Worker.Test/BulkFrontendIlMetadataIdentityBoundaryTests.cs:28` | `Inputs`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-162 | `SharpProof.Worker.Test/BulkFrontendIlMetadataIdentityBoundaryTests.cs:29` | `ExpectedCodes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-163 | `SharpProof.Worker.Test/BulkFrontendIlMetadataIdentityBoundaryTests.cs:30` | `ExpectedPreconditions`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-164 | `SharpProof.Worker.Test/BulkFrontendIlModuleScopeBoundaryTests.cs:30` | `Inputs`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-165 | `SharpProof.Worker.Test/BulkFrontendIlModuleScopeBoundaryTests.cs:31` | `ExpectedCodes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-166 | `SharpProof.Worker.Test/BulkFrontendIlModuleScopeBoundaryTests.cs:32` | `ExpectedPreconditions`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-167 | `SharpProof.Worker.Test/BulkFrontendIlNullOperandOrderTests.cs:20` | `ExpectedLeft`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-168 | `SharpProof.Worker.Test/BulkFrontendIlNullOperandOrderTests.cs:21` | `ExpectedRight`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-169 | `SharpProof.Worker.Test/BulkMetadataDefaultWorkBoundaryTests.cs:18` | `DivisionInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-170 | `SharpProof.Worker.Test/BulkMetadataDefaultWorkBoundaryTests.cs:19` | `MetadataInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-171 | `SharpProof.Worker.Test/BulkMetadataDefaultWorkBoundaryTests.cs:20` | `ExpectedPreconditions`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-172 | `SharpProof.Worker.Test/CacheEvidenceOrderingControlTests.cs:10` | `s_unordered`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-173 | `SharpProof.Worker.Test/CacheEvidenceOrderingControlTests.cs:11` | `s_ordered`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-174 | `SharpProof.Worker.Test/CacheEvidenceOrderingControlTests.cs:12` | `s_orderedClaims`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-175 | `SharpProof.Worker.Test/CallerBoxingInterfaceAuditTests.cs:24` | `TargetNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-176 | `SharpProof.Worker.Test/CallerBoxingInterfaceAuditTests.cs:25` | `IdentityBody`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-177 | `SharpProof.Worker.Test/CallerBoxingInterfaceAuditTests.cs:26` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-178 | `SharpProof.Worker.Test/CapturedPrimaryConstructorStorageAuditTests.cs:30` | `s_opCodes`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-179 | `SharpProof.Worker.Test/CapturedPrimaryConstructorStorageAuditTests.cs:36` | `s_cases`: `Case[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-180 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:19` | `DenseOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-181 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:20` | `FailingShadowInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-182 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:21` | `PotentialOwnerGapReasons`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-183 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:23` | `CompanionEvidence`: `WorkerClaimEvidence[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-184 | `SharpProof.Worker.Test/CompilerClosedParameterCaptureBoundaryTests.cs:17` | `ExpectedParameterOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-185 | `SharpProof.Worker.Test/CompilerClosedParameterCaptureBoundaryTests.cs:18` | `ExpectedAttributeKinds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-186 | `SharpProof.Worker.Test/CompilerEvidenceValidationBoundaryTests.cs:17` | `ExpectedClaimIds`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-187 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:17` | `ScalarCases`: `(byte[] Bytes, string Code, long Operand, int Cursor, int Target)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-188 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:29` | `OffsetInput`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-189 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:30` | `UnknownSingle`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-190 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:31` | `KnownSingleNeighbor`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-191 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:32` | `UnknownDouble`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-192 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:33` | `KnownDoubleNeighbor`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-193 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:34` | `StringOperand`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-194 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:35` | `TruncatedPrefix`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-195 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:36` | `TruncatedInteger`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-196 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:37` | `EndTarget`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-197 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:38` | `NegativeTarget`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-198 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:39` | `OverflowTarget`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-199 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:40` | `ForwardBoundary`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-200 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:41` | `ForwardInterior`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-201 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:42` | `BackwardBoundary`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-202 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:43` | `StringControls`: `(int Input, int Expected)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-203 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:11` | `ExpectedPartialReads`: `(int Offset, int Count)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-204 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:12` | `ExpectedUntouchedDestination`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-205 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:13` | `TwoByteSource`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-206 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:14` | `OneByteSource`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-207 | `SharpProof.Worker.Test/CompilerOwnedMethodBodyGraphTests.cs:15` | `ExpectedOwnerNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-208 | `SharpProof.Worker.Test/CompilerReferenceStackMergeCoverageTests.cs:11` | `Choices`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-209 | `SharpProof.Worker.Test/CompilerReportingTests.cs:22` | `CapturedPathCases`: `string[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-210 | `SharpProof.Worker.Test/ConcatenationCallOperandAllocationTests.cs:19` | `s_spliced`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-211 | `SharpProof.Worker.Test/ConcatenationCallOperandAllocationTests.cs:31` | `s_separate`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-212 | `SharpProof.Worker.Test/ConcatenationIdentityCastAllocationTests.cs:19` | `s_spliced`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-213 | `SharpProof.Worker.Test/ConcatenationIdentityCastAllocationTests.cs:29` | `s_separate`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-214 | `SharpProof.Worker.Test/ConditionalAttributeCompilerAuthorityBoundaryTests.cs:24` | `RetainedInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-215 | `SharpProof.Worker.Test/ConditionalAttributeCompilerAuthorityBoundaryTests.cs:25` | `OmittedInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-216 | `SharpProof.Worker.Test/ConditionalAttributeCompilerAuthorityBoundaryTests.cs:29` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-217 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:24` | `RetainedInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-218 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:25` | `OmittedInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-219 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:26` | `TwoByteColumns`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-220 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:31` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-221 | `SharpProof.Worker.Test/CountUpHoudiniResourceBoundaryTests.cs:125` | `RuntimeInputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-222 | `SharpProof.Worker.Test/EmptyIteratorAllocationAuditTests.cs:34` | `s_opCodes`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-223 | `SharpProof.Worker.Test/EmptyIteratorAllocationAuditTests.cs:39` | `s_cases`: `Case[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-224 | `SharpProof.Worker.Test/FloatingEqualityEvaluationOrderAuditTests.cs:17` | `ExplicitArgumentKinds`: `ArgumentKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-225 | `SharpProof.Worker.Test/FloatingEqualityEvaluationOrderAuditTests.cs:18` | `FloatingComparisonKinds`: `BinaryOperatorKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-226 | `SharpProof.Worker.Test/GenericFixedTestedTypeControlAuditTests.cs:44` | `Forms`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-227 | `SharpProof.Worker.Test/GenericImplicitTypeTestAllocationTests.cs:17` | `s_implicit`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-228 | `SharpProof.Worker.Test/GenericImplicitTypeTestAllocationTests.cs:31` | `s_folded`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-229 | `SharpProof.Worker.Test/GenericNullableTypeTestAllocationTests.cs:15` | `s_nullable`: `(string Members, string Run, bool ReleaseAllocates)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-230 | `SharpProof.Worker.Test/GenericNullableTypeTestAllocationTests.cs:37` | `s_nonNullable`: `(string Members, string Run)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-231 | `SharpProof.Worker.Test/GenericTestedTypePatternControlAuditTests.cs:26` | `ClosedRuntimeTypes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-232 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:33` | `s_ints`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-233 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:34` | `s_arrays`: `int[]?[]` | Implemented outer table; mutable alias referents preserved |
| FIX-234 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:35` | `s_strings`: `string?[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-235 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:37` | `s_boundaryInputs`: `(int X, int Y, int[]? Array, string? Text)[]` | Implemented outer table; mutable alias referents preserved |
| FIX-236 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:43` | `s_operations`: `Operation[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-237 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:54` | `s_scopes`: `Scope[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-238 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:66` | `s_inputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-239 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:69` | `s_cycle`: `(int Operation, int Scope)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-240 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:17` | `s_modes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-241 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:18` | `s_comparisons`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-242 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:19` | `s_simpleStatements`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-243 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:20` | `s_simpleInts`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-244 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:21` | `s_compoundOperators`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-245 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:22` | `s_exceptions`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-246 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:23` | `s_extremes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-247 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:24` | `s_values`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-248 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:25` | `s_specMaxExpressions`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-249 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:32` | `s_specMaxMaximum`: `int[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-250 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:33` | `s_specMaxValue`: `int[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-251 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:65` | `s_domains`: `Domain[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-252 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:73` | `s_jointDomains`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-253 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:30` | `s_wrappers`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-254 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:75` | `s_leaves`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-255 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:107` | `s_manual`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-256 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:129` | `s_cleanupScopes`: `(string Name, string Body)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-257 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:139` | `s_cleanupExits`: `(string Name, string Body)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-258 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:150` | `s_disposalCases`: `(string Name, string Body)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-259 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:159` | `s_modes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-260 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:162` | `s_tails`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-261 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:164` | `s_inputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-262 | `SharpProof.Worker.Test/LongFuzz/LongFuzzRuntimeCoverageRegressionTests.cs:14` | `SingleItem`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-263 | `SharpProof.Worker.Test/LongFuzz/LongFuzzRuntimeCoverageRegressionTests.cs:15` | `ClaimValidity`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-264 | `SharpProof.Worker.Test/LongFuzz/RoslynSyntaxMutationRegressionTests.cs:18` | `Expressions`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-265 | `SharpProof.Worker.Test/LongFuzz/TypedFeatureLongFuzzTests.cs:25` | `s_anchors`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-266 | `SharpProof.Worker.Test/LongFuzz/TypedFeatureLongFuzzTests.cs:26` | `s_families`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-267 | `SharpProof.Worker.Test/MetadataInvocationBindingInvariantTests.cs:17` | `ExpectedOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-268 | `SharpProof.Worker.Test/MetadataInvocationBindingInvariantTests.cs:18` | `ExpectedRecoveryDiagnostics`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-269 | `SharpProof.Worker.Test/MetadataZeroArgumentCallDepthApiTests.cs:16` | `LeafInstructions`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-270 | `SharpProof.Worker.Test/MetadataZeroArgumentCallDepthApiTests.cs:17` | `CallingInstructions`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-271 | `SharpProof.Worker.Test/NativeMetadataShadowRequiresTests.cs:20` | `SuccessfulObservation`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-272 | `SharpProof.Worker.Test/NativeTotalCandidateCoreTests.cs:14` | `TwoOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-273 | `SharpProof.Worker.Test/NativeTotalCandidateCoreTests.cs:15` | `InitialAndFinalOrdinals`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-274 | `SharpProof.Worker.Test/NullableDefaultBoxingZeroControlAuditTests.cs:23` | `IdentityBody`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-275 | `SharpProof.Worker.Test/NullableDefaultBoxingZeroControlAuditTests.cs:24` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-276 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:24` | `WitnessValues`: `decimal[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-277 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:25` | `IdentityBody`: `byte[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-278 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:26` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-279 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:13` | `FullClauseKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-280 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:15` | `RequiresClauseKinds`: `BoundContractKind[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-281 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:17` | `FullClauseTexts`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-282 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:19` | `RequiresClauseTexts`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-283 | `SharpProof.Worker.Test/PrimitiveArrayAliasControlTests.cs:9` | `Pairs`: `string[][]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-284 | `SharpProof.Worker.Test/ProtocolJsonTests.cs:20` | `s_requestProperties`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-285 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:209` | `SupportedCases`: `ScalarCase[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-286 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:244` | `WideningCases`: `WideningCase[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-287 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:268` | `ArithmeticCases`: `ArithmeticCase[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-288 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:353` | `RequiredReferenceFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-289 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:360` | `UnaryParameterNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-290 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:361` | `BinaryParameterNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-291 | `SharpProof.Worker.Test/ScalarPatternFlowTests.cs:20` | `Inputs`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-292 | `SharpProof.Worker.Test/SynchronizedAutoSetterCallerPurityAuditTests.cs:21` | `s_inputValues`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-293 | `SharpProof.Worker.Test/SynchronizedBodiedGetterCallerPurityAuditTests.cs:21` | `s_inputValues`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-294 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:20` | `s_inputValues`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-295 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:21` | `s_expectedReturns`: `bool[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-296 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:22` | `s_expectedFilterOpCodes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-297 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:23` | `s_opCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; solution build and ordinary-suite verification passed |
| FIX-298 | `SharpProof.Worker.Test/SynchronizedInstanceMethodPurityAuditTests.cs:19` | `s_inputValues`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-299 | `SharpProof.Worker.Test/SynchronizedLocalFunctionPurityAuditTests.cs:20` | `s_inputValues`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-300 | `SharpProof.Worker.Test/SynchronizedMethodPurityAuditTests.cs:19` | `s_inputValues`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-301 | `SharpProof.Worker.Test/SynchronizedSourceCalleePurityAuditTests.cs:20` | `s_inputValues`: `int[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-302 | `SharpProof.Worker.Test/TypedMetadataConversionTests.cs:12` | `NumericTypes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-303 | `SharpProof.Worker.Test/VerificationTargetSchedulerTests.cs:11` | `Retirements`: `(WorkerCallableCoverageReason Callable, WorkerClaimReason Claim)[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-304 | `SharpProof.Worker.Test/WorkerNativeEffectEntryLockCoverageTests.cs:11` | `EntryLockContradictoryCore`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-305 | `SharpProof.Worker.Test/WorkerTests.cs:121` | `InvalidBudgetErrorCodes`: `string[]` | Implemented; solution build and ordinary-suite verification passed |
| FIX-306 | `SharpProof.Worker.Test/WorkerTests.cs:131` | `RequiredReferenceFileNames`: `string[]` | Implemented; solution build and ordinary-suite verification passed |

### Deduplicated C# coverage

- tutorial sources: 8 files.
- eng and script support/analyzers: 27 files.
- samples/templates: 12 files.
- production projects/Tools: 399 files.
- test projects: 1171 files.
- golden fixtures: 188 files.

Agents 01-10 have returned their initial audits. The coordinator is reconciling
follow-up classifications and implementation. Agent partition counts included
some shared/golden overlaps; the deduplicated manifest above is authoritative.

### Serialized model migration inventory

These hand-maintained generated files contain 312 baseline writable properties.
They require coordinated producer/canonicalizer/caller changes and wire-format
checks; serializer support for init is not itself an exemption. The explicit
mutable-budget test remains excluded where mutation is the behavior under test.

| ID | Baseline location | Property | Status |
| --- | --- | --- | --- |
| MODEL-001 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:125` | `DisjointInputs`: `CompilerDisjointInputPair[]?` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-002 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:127` | `EffectsCompleteAtEntry`: `bool` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-003 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:128` | `ValidEffectClaimIds`: `string[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-004 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:130` | `IsBodyAbstraction`: `bool` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-005 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:132` | `HasReceiver`: `bool` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-006 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:133` | `Graph`: `PortableIrGraph` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-007 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:134` | `Parameters`: `CompilerTotalParameterArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-008 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:135` | `Result`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-009 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:136` | `Clauses`: `CompilerTotalClauseArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-010 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:137` | `ExceptionConstraints`: `CompilerTotalExceptionConstraintArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-011 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:138` | `CallPreconditions`: `CompilerTotalCallPreconditionArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-012 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:150` | `MetadataClause`: `CompilerMetadataClauseOrigin?` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-013 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:152` | `InstructionIndex`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-014 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:153` | `CalleeIdentity`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-015 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:154` | `ClauseOrdinal`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-016 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:155` | `ClauseSite`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-017 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:156` | `ValueRoot`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-018 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:157` | `SafeRoot`: `int` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-019 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:162` | `ClaimId`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-020 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:163` | `AllowedKinds`: `IrExceptionKind[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-021 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:168` | `Entry`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-022 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:169` | `Current`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-023 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:170` | `Old`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-024 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:175` | `Kind`: `CompilerContractKind` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-025 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:176` | `ValueRoot`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-026 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:177` | `SafeRoot`: `int` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-027 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:178` | `Operation`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-028 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:179` | `ClaimId`: `string?` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-029 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:180` | `AssumptionId`: `string?` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-030 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:190` | `SpecificationPackIds`: `string[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-031 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:191` | `SpecificationPackCatalogVersion`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-032 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:192` | `SpecificationPackCatalogSha256`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-033 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:197` | `CallableId`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-034 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:198` | `FailureReason`: `WorkerClaimReason` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-035 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:199` | `EffectClaims`: `CompilerEffectClaimArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-036 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:201` | `Total`: `CompilerTotalCallableArtifact?` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-037 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:203` | `TotalEntry`: `CompilerTotalCallableArtifact?` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-038 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:208` | `ClaimId`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-039 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:209` | `ContractKind`: `WorkerEffectContractKind` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-040 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:210` | `Outcome`: `WorkerClaimOutcome` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-041 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:211` | `Reason`: `WorkerClaimReason` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-042 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:212` | `Certainty`: `WorkerEffectEvidenceCertainty` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-043 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:213` | `Constraint`: `CompilerEffectConstraintArtifact` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-044 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:214` | `Evidence`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-045 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:215` | `EvidenceSha256`: `string` | Preserve; explicit in-place Seal digest replacement contract |
| MODEL-046 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:220` | `AllowedEffects`: `WorkerEffectSet` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-047 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:221` | `AllowedCapabilities`: `WorkerEffectCapabilitySet` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-048 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:222` | `AllowedExceptionTypes`: `string[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-049 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:280` | `Code`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-050 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:281` | `Message`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-051 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:282` | `IsSource`: `bool` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-052 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:283` | `Location`: `WorkerSourceLocation` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-053 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:285` | `SourceTreeOrdinal`: `int` | Init-only; provenance initialized by producer |
| MODEL-054 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:287` | `SourceTreePath`: `string` | Init-only; provenance initialized by producer |
| MODEL-055 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:289` | `SourceTreeSha256`: `string` | Init-only; provenance initialized by producer |
| MODEL-056 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:291` | `SourceLineMapSha256`: `string` | Init-only; provenance initialized by producer |
| MODEL-057 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:296` | `Schema`: `string` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-058 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:297` | `SchemaVersion`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-059 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:298` | `ProtocolVersion`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-060 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:299` | `RelationalSummarySchemaVersion`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-061 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:300` | `SpecificationPackSchemaVersion`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-062 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:301` | `SpecificationPackIds`: `string[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-063 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:302` | `SpecificationPackCatalogVersion`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-064 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:303` | `SpecificationPackCatalogSha256`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-065 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:304` | `Features`: `WorkerFeatureSet` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-066 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:305` | `Compilation`: `CompilerCompilationSnapshot` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-067 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:306` | `Manifest`: `WorkerClaimManifest` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-068 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:307` | `MaximumExpressionDepth`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-069 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:308` | `CompilerDiagnostics`: `CompilerDiagnosticArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-070 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:309` | `Callables`: `CompilerCallableArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-071 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:310` | `ReachableSource`: `CompilerReachableSourceArtifact?` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-072 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:317` | `CollectionComplete`: `bool` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-073 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:318` | `Roots`: `CompilerSourceRootArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-074 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:319` | `Bodies`: `CompilerSourceBodyArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-075 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:320` | `Documents`: `CompilerSourceDocumentArtifact[]` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-076 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:325` | `SourceTreeOrdinal`: `int` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-077 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:326` | `Path`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-078 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:327` | `MaximumBodyEnd`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-079 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:332` | `CallableId`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-080 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:333` | `BodyId`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-081 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:338` | `BodyId`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-082 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:339` | `MethodIdentity`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-083 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:340` | `CallIdentity`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-084 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:341` | `IsStatic`: `bool` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-085 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:342` | `ParameterTypes`: `string[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-086 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:343` | `ReturnType`: `string?` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-087 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:344` | `SourceTreeOrdinal`: `int` | Implemented; record/init and immutable array members; focused checks passed |
| MODEL-088 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:345` | `Start`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-089 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:346` | `Length`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-090 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:347` | `CallsComplete`: `bool` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-091 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:348` | `EffectsCompleteAtEntry`: `bool` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-092 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:349` | `Callees`: `string[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-093 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:350` | `Graph`: `PortableIrGraph?` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-094 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:351` | `IsCallSkeleton`: `bool` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-095 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:352` | `SourceCalls`: `CompilerSourceCallArtifact[]` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-096 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:357` | `InstructionIndex`: `int` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-097 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:358` | `CalleeBodyId`: `string` | Implemented; record/init and immutable arrays; 193 focused checks passed; broad check pending |
| MODEL-098 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:20` | `ProjectDirectory`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-099 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:22` | `AssemblyName`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-100 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:24` | `AssemblyIdentity`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-101 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:26` | `TargetFramework`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-102 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:28` | `CompilerVersion`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-103 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:30` | `CompilerMvid`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-104 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:32` | `CSharpCompilerVersion`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-105 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:34` | `CSharpCompilerMvid`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-106 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:36` | `SpecificationPackIds`: `string[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-107 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:38` | `SpecificationPackCatalogVersion`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-108 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:40` | `SpecificationPackCatalogSha256`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-109 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:42` | `Options`: `CompilerCompilationOptionsSnapshot` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-110 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:44` | `SyntaxTrees`: `CompilerSyntaxTreeSnapshot[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-111 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:46` | `References`: `CompilerReferenceSnapshot[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-112 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:48` | `AdditionalFiles`: `CompilerAdditionalFileSnapshot[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-113 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:63` | `OutputKind`: `CompilerOutputKind` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-114 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:64` | `MainTypeName`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-115 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:65` | `OptimizationLevel`: `CompilerOptimizationLevel` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-116 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:66` | `Platform`: `CompilerPlatform` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-117 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:67` | `NullableContext`: `CompilerNullableContext` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-118 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:68` | `MetadataImportOptions`: `CompilerMetadataImportOptions` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-119 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:69` | `WarningLevel`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-120 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:70` | `GeneralDiagnosticOption`: `CompilerReportDiagnostic` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-121 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:71` | `SpecificDiagnosticOptions`: `CompilerDiagnosticOptionSnapshot[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-122 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:72` | `ReportSuppressedDiagnostics`: `bool` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-123 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:73` | `CheckOverflow`: `bool` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-124 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:74` | `AllowUnsafe`: `bool` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-125 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:75` | `Deterministic`: `bool` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-126 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:76` | `ReferencesSupersedeLowerVersions`: `bool` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-127 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:77` | `AssemblyIdentityComparer`: `CompilerAssemblyIdentityComparer` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-128 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:78` | `Usings`: `string[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-129 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:79` | `ResolverPolicy`: `CompilerResolverPolicy` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-130 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:84` | `Id`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-131 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:85` | `ReportDiagnostic`: `CompilerReportDiagnostic` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-132 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:90` | `SourceStart`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-133 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:91` | `SourceLength`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-134 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:92` | `MappedPath`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-135 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:93` | `MappedLine`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-136 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:94` | `MappedColumn`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-137 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:95` | `CharacterOffset`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-138 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:100` | `Path`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-139 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:101` | `Sha256`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-140 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:102` | `Encoding`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-141 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:103` | `ChecksumAlgorithm`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-142 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:104` | `RoslynChecksum`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-143 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:105` | `LineMapSha256`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-144 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:106` | `TextLength`: `int` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-145 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:107` | `LineMap`: `CompilerSourceLineMapEntry[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-146 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:108` | `LanguageVersion`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-147 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:109` | `DocumentationMode`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-148 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:110` | `Kind`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-149 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:111` | `PreprocessorSymbols`: `string[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-150 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:112` | `EffectivePreprocessorSymbols`: `string[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-151 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:113` | `Features`: `CompilerFeatureSnapshot[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-152 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:118` | `Kind`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-153 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:119` | `EmbedInteropTypes`: `bool` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-154 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:120` | `HasRecursiveAliases`: `bool` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-155 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:121` | `Aliases`: `string[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-156 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:122` | `Identity`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-157 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:123` | `Modules`: `CompilerReferenceModuleSnapshot[]` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-158 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:128` | `Name`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-159 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:129` | `Mvid`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-160 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:130` | `Path`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-161 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:131` | `Sha256`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-162 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:132` | `SizeBytes`: `long` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-163 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:137` | `Path`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-164 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:138` | `Sha256`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-165 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:143` | `Key`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-166 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:144` | `Value`: `string` | Implemented; record/init and immutable array collections; 460 affected Worker checks passed |
| MODEL-167 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:15` | `Semantics`: `IrExecutionSemantics` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-168 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:16` | `HasProgram`: `bool` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-169 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:17` | `Types`: `PortableIrType[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-170 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:18` | `Identities`: `int[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-171 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:19` | `Variables`: `PortableIrVariable[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-172 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:20` | `Members`: `PortableIrMember[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-173 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:21` | `Operations`: `PortableIrOperation[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-174 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:22` | `Terms`: `PortableIrTerm[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-175 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:23` | `Blocks`: `PortableIrBlock[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-176 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:24` | `Entry`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-177 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:25` | `Roots`: `int[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-178 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:36` | `Kind`: `IrTypeKind` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-179 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:37` | `Name`: `string` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-180 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:38` | `Element`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-181 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:40` | `Width`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-182 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:42` | `Signed`: `bool` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-183 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:44` | `ClosedSealedReference`: `bool` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-184 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:52` | `Name`: `string` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-185 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:53` | `Type`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-186 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:65` | `Identity`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-187 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:66` | `DeclaringType`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-188 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:67` | `Name`: `string` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-189 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:68` | `ReturnType`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-190 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:69` | `IsStatic`: `bool` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-191 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:70` | `ParameterTypes`: `int[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-192 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:71` | `DocumentationCommentId`: `string?` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-193 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:78` | `Description`: `string?` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-194 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:80` | `SourceSpan`: `PortableIrSourceSpan?` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-195 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:86` | `Document`: `string` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-196 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:88` | `Start`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-197 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:90` | `Length`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-198 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:91` | `Line`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-199 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:92` | `Column`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-200 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:107` | `Kind`: `IrTermKind` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-201 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:108` | `Type`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-202 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:109` | `A`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-203 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:110` | `B`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-204 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:111` | `C`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-205 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:112` | `D`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-206 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:113` | `Number`: `long` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-207 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:115` | `Bits`: `ulong` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-208 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:116` | `Text`: `string?` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-209 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:117` | `Items`: `int[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-210 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:128` | `Kind`: `IrLocationKind` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-211 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:129` | `Type`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-212 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:130` | `A`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-213 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:131` | `B`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-214 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:132` | `Items`: `int[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-215 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:145` | `Kind`: `IrInstructionKind` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-216 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:146` | `Operation`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-217 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:147` | `A`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-218 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:148` | `B`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-219 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:149` | `C`: `int` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-220 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:150` | `Items`: `int[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-221 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:151` | `Location`: `PortableIrLocation?` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-222 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:153` | `Origin`: `IrHavocOrigin` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-223 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:161` | `Name`: `string?` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-224 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:162` | `Instructions`: `PortableIrInstruction[]` | Implemented; record/init and immutable arrays; 706 affected Worker checks passed |
| MODEL-225 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:36` | `ProtocolVersion`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-226 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:38` | `CompilerManifest`: `WorkerFileReference` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-227 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:40` | `Budgets`: `WorkerBudgets` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-228 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:42` | `Cache`: `WorkerCacheOptions` | Init-only; retained mutable members remain under review |
| MODEL-229 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:44` | `VerifyPolicy`: `WorkerVerifyPolicy` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-230 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:46` | `AssumptionPolicy`: `WorkerAssumptionPolicy` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-231 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:52` | `Path`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-232 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:54` | `Sha256`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-233 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:90` | `QueryRlimit`: `uint` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-234 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:92` | `MethodRlimit`: `uint` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-235 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:94` | `MethodWallTimeMilliseconds`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-236 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:96` | `ProjectWallTimeMilliseconds`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-237 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:98` | `MaxParallelism`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-238 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:100` | `MaximumExpressionDepth`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-239 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:107` | `Enabled`: `bool` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-240 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:109` | `Directory`: `string?` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-241 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:111` | `MaximumBytes`: `long` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-242 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:158` | `Path`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-243 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:160` | `Start`: `int` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-244 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:162` | `Length`: `int` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-245 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:164` | `Line`: `int` | Init-only; retained mutable members remain under review |
| MODEL-246 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:166` | `Column`: `int` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-247 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:172` | `CallableId`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-248 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:174` | `SelectedFeatures`: `WorkerSelectedFeature[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-249 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:176` | `SelectionReasons`: `WorkerSelectionReason[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-250 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:178` | `Location`: `WorkerSourceLocation` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-251 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:180` | `ClaimIds`: `string[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-252 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:182` | `Assumptions`: `WorkerAssumptionEvidence[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-253 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:188` | `ClaimId`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-254 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:190` | `CallableId`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-255 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:192` | `Ordinal`: `int` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-256 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:194` | `Kind`: `WorkerClaimKind` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-257 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:196` | `Evidence`: `WorkerClaimEvidence` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-258 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:198` | `EffectContractKind`: `WorkerEffectContractKind` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-259 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:200` | `Location`: `WorkerSourceLocation` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-260 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:206` | `SchemaVersion`: `int` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-261 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:208` | `Hash`: `string` | Preserve; public in-place SealManifest digest contract |
| MODEL-262 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:210` | `Callables`: `WorkerCallableManifestEntry[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-263 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:212` | `Claims`: `WorkerClaimManifestEntry[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-264 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:368` | `Id`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-265 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:370` | `Kind`: `WorkerAssumptionKind` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-266 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:372` | `Used`: `bool` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-267 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:378` | `CallableId`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-268 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:380` | `Coverage`: `WorkerCallableCoverage` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-269 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:382` | `Reason`: `WorkerCallableCoverageReason` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-270 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:384` | `Assumptions`: `WorkerAssumptionEvidence[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-271 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:390` | `ClaimId`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-272 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:392` | `Outcome`: `WorkerClaimOutcome` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-273 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:394` | `Reason`: `WorkerClaimReason` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-274 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:396` | `EffectCertainty`: `WorkerEffectEvidenceCertainty` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-275 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:398` | `EffectWitness`: `WorkerEffectViolationWitness?` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-276 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:400` | `Vacuity`: `WorkerVacuityKind` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-277 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:402` | `ProofCore`: `string[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-278 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:404` | `Model`: `WorkerModelValue[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-279 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:406` | `Assumptions`: `WorkerAssumptionEvidence[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-280 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:412` | `Kind`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-281 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:414` | `Detail`: `string` | Init-only; retained mutable members remain under review |
| MODEL-282 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:416` | `Effects`: `WorkerEffectSet` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-283 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:418` | `Capabilities`: `WorkerEffectCapabilitySet` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-284 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:420` | `ExactExceptionTypeHierarchy`: `string[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-285 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:422` | `Location`: `WorkerSourceLocation` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-286 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:428` | `Variable`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-287 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:430` | `Kind`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-288 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:432` | `Value`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-289 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:449` | `ProtocolVersion`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-290 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:451` | `ManifestSchemaVersion`: `int` | Init-only; retained mutable members remain under review |
| MODEL-291 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:453` | `CacheSchemaVersion`: `int` | Init-only; retained mutable members remain under review |
| MODEL-292 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:455` | `WorkerVersion`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-293 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:457` | `ApiSpecVersion`: `string` | Init-only; retained mutable members remain under review |
| MODEL-294 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:459` | `WorkerBinarySha256`: `string` | Init-only; retained mutable members remain under review |
| MODEL-295 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:461` | `ApiSpecContentSha256`: `string` | Init-only; retained mutable members remain under review |
| MODEL-296 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:467` | `CacheHit`: `bool` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-297 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:469` | `CacheStatus`: `WorkerCacheStatus` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-298 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:471` | `Versions`: `WorkerVersionSummary` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-299 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:473` | `Budgets`: `WorkerBudgets` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-300 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:475` | `ElapsedMilliseconds`: `long` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-301 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:481` | `Code`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-302 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:483` | `Message`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-303 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:489` | `ProtocolVersion`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-304 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:491` | `RequestHash`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-305 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:493` | `InputHash`: `string` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-306 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:495` | `Manifest`: `WorkerClaimManifest` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-307 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:497` | `RunStatus`: `WorkerRunStatus` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-308 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:499` | `FailureReason`: `WorkerRunFailureReason` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-309 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:501` | `CallableResults`: `WorkerCallableResult[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-310 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:503` | `ClaimResults`: `WorkerClaimResult[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |
| MODEL-311 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:505` | `Summary`: `WorkerVerificationSummary` | Implemented; record/init scalars; 566 affected Worker and 336 Package checks passed |
| MODEL-312 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:507` | `Errors`: `WorkerProtocolError[]` | Implemented; ImmutableArray storage; setter preserves explicit public canonicalization; 666 affected checks passed |

### Remaining analyzer local-state candidates

A strict coordinator re-scan after the stored-snapshot changes found 35 direct
mutable collection construction sites in the Analyzer and Analyzer.Core partitions.
These are implementation state, not mutable program subjects; allocation cost alone
does not exempt them. Shared diagnostic sinks need a coordinated immutable-return
refactor. Concurrent callback caches retain an explicit concurrency contract.

| Site | Current construction | Status |
| --- | --- | --- |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryEffectSites.cs:114` | `var reachable = new HashSet<IrBlockId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryEffectSites.cs:115` | `var pending = new Stack<IrBlockId>(roots);` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryEffectSites.cs:132` | `var escaping = new HashSet<IrBlockId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryEffectSites.cs:133` | `var pending = new Stack<IrBlockId>(program.Blocks` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:244` | `var reachable = new HashSet<IrBlockId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:245` | `var pending = new Stack<IrBlockId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:313` | `var owned = _program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrAssignInstruction>().ToDictionary(assign => assign is null ? throw new System.ArgumentNullException(nameof(assign)) : (assign.Id));` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:314` | `var selected = new Dictionary<IrInstructionId, IrAssignInstruction>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:327` | `var ids = _program.Blocks.Select((b, i) => b is null ? throw new System.ArgumentNullException(nameof(b)) : ((b.Id, i))).ToDictionary(p => p.Id, p => p.i);` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:328` | `var blocks = new List<DataflowBlock<CoreIrAdvisoryState>>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:329` | `var edges = new List<DataflowEdge>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:432` | `var pending = new Stack<IrTerm>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:797` | `var seen = new HashSet<IrVarId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:834` | `var seen = new HashSet<IrVarId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:852` | `var seen = new HashSet<IrVarId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryLowering.cs:40` | `var paths = documents.ToDictionary(item => item.Tree, item => item.Path);` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryRequiresDiagnostics.cs:21` | `var reported = new HashSet<(Location Location, string Message)>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryRequiresDiagnostics.cs:91` | `var variables = new HashSet<IrVarId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryRequiresDiagnostics.cs:92` | `var terms = new Stack<IrTerm>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryRequiresDiagnostics.cs:99` | `var visited = new HashSet<IrId>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Advisory/AdvisoryRequiresDiagnostics.cs:120` | `var candidates = new List<IMethodSymbol>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs:136` | `var scopes = new List<ISymbol> { symbol };` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/CompilerArtifact/CompilerExceptionTypeIdentity.cs:16` | `var argumentAssemblies = new List<string>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs:137` | `var candidates = new List<(string Key, string Value)>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:72` | `var targetMatches = targets.ToDictionary(` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:76` | `var candidateMatches = candidates.ToDictionary(` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:95` | `var byTarget = targetMatches.ToDictionary(` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:99` | `var byCandidate = candidateMatches.ToDictionary(` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/ContractForValidation/ContractForCompanionValidator.cs:106` | `var diagnosed = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/ContractForValidation/ContractForValidationEngine.cs:18` | `var diagnostics = new List<Diagnostic>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/ContractForValidation/ContractForValidationEngine.cs:156` | `var overlapping = new HashSet<ResolvedCompanion>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/OverridePreconditionDiagnostics.cs:72` | `var identities = right.Variables.ToDictionary(` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/OverridePreconditionDiagnostics.cs:87` | `var variables = new Dictionary<IrVarId, IrTerm>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/TotalBodyLowering.cs:76` | `var pending = new Stack<IOperation>();` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |
| `SharpProof.Analyzer.Core/TotalBodyLowering.cs:235` | `var pending = new Stack<IrTerm>(terms);` | Implemented; immutable persistent collections and ordered callback sink; analyzer 1,085 passed |

### Coordinator review limits

- The three test partitions are deduplicated by project path and filename. Golden
  sources, eng helpers and production Testing support are counted only in their
  actual owner partitions.
- Fixed arrays used by NUnit or params APIs are not automatically exempt. Convert
  the stored data, then adapt the call boundary while retaining order and aliasing
  when those are the behavior under test.
- Local worklists, caches and builders reported as algorithmic state still need
  per-use review. They are not globally waived merely because they are local.
- Public attribute setters/Type arrays and MSBuild setters have external setter
  contracts. Preserve those boundary contracts while checking retained aliases.

### Validation checkpoints and retained decisions

- Baseline canonical Linux amd64 solution build passed, 0 warnings / 0 errors.
- Fixed production tables, expanded records, and the 306 shared-fixture conversions
  each passed solution builds before broader testing.
- Final verification uses the solution ordinary-suite run plus full reruns of the
  corrected Effects, IR and Worker projects: **19 projects, 11,590 passed,
  1 existing package skip, 0 remaining failures**. Filter:
  `Category!=Performance&Category!=Coverage&Category!=Corpus`.
  Performance, coverage, corpus and explicitly enabled fuzz campaigns are not
  claimed as executed by this checkpoint.
- The final full Worker rerun passed 5,329 tests, including three new artifact
  serialization/decoding regressions. The final IR rerun passed 471 tests,
  including two new adversarial duplicate-enumeration cases. Effects passed 118.
- Initial validation exposed a reflection-only fixture cast and two later
  compatibility assumptions. The cast now reads ImmutableArray; enum controls
  compare canonical output while asserting producer arrays stay unchanged;
  inventory indexing explicitly rejects identical duplicates. IR replacement
  capture also rejects identical duplicate keys and retains input validation order.
- Direct Compose initially omitted linked-worktree Git metadata and caused one
  SourceLink package failure. Subsequent task runs used the documented host
  wrapper with read-only metadata mounts; the package suite passed 336 tests
  with one existing skip.
- Canonical PowerShell probes passed immutable array/map conversion, ordering,
  case-insensitive policy lookup, string-array parameter binding and repeated
  imports. Exported package bindings use ReadOnly because Constant breaks
  Import-Module -Force. The private policy binding is Constant.
- Final repository formatting passed with an empty exported patch. Pinned
  IDE0044 analysis produced no further readonly-field changes.
- Pinned Roslyn parsing verified **1,581 original raw/interpolated program literal
  tokens unchanged across 226 changed C# files**. Subsequent edits changed only
  method-body style and the nonliteral replacement-validation implementation.
  Golden, sample, template and vendored sources have no changes.
- Of the 312 inventoried model properties, 290 are now init-only, six budget
  properties and two in-place seal digests retain explicit mutable contracts, and
  14 protocol collection properties remain open for coordinated migration. Init-only collection
  properties still require deep immutability review; the inventory does not
  silently count retained arrays as immutable.
- The 35 sites in the strict analyzer local-state appendix are implemented; targeted analyzer tests passed (1,085).
  Other local worklists/builders are not blanket-exempt and remain part of the
  comprehensive follow-up review.
- Per-project results and bounded run logs are exported under artifacts in this
  worktree. No performance conclusion is inferred from concurrently loaded runs.

### Stopping condition

The comprehensive effort is complete only when every candidate is classified,
all confirmed replaceable mutability is fixed, explicit mutable test subjects and
external mutation contracts are evidenced, and the final source state passes the
required gates. Unchecked candidates and coordinated model migration work remain
visible; this document does not mark them absent or complete.

### Strict follow-up inventory at f6e5ee08a

The coordinator is recording the second read-only pass separately from verified
implementation. Findings are hypotheses until traced in source and tested.
No local collection, memo, builder, runtime buffer, or cache is exempt solely
because it implements an algorithm. Native/framework mutation and mutation that
is itself under test require an explicit boundary justification.

| ID | Partition and exact targets | State |
| --- | --- | --- |
| FOLLOW-001 | DataflowGraph edge set, adjacency staging, Tarjan discovery/low-link/on-stack/result vectors and DFS stacks; ForwardDataflowAnalysis inputs/outputs/updateCounts, sorted pending/affected sets, ordered changedOutputs and reachability stack | Implemented with persistent immutable lists/sets/stacks; 118 tests passed |
| FOLLOW-002 | IrFactory external identity candidate array under gate | Implemented immutable array snapshot; pending IR gate |
| FOLLOW-003 | IrSequenceReads writer map/list, fresh set and term worklist | Implemented persistent map/list/set/stack; pending IR gate |
| FOLLOW-004 | IrBlockOrder indexed states/result and stack; IrTraversal Any/CollectVariables/Traverse/FoldBottomUp; IrSnapshotReads worklist/memo; IrPrinter, IrSemanticTerms and IrSubstitution memo maps and opaque argument rewrite array | Open coordinated IR migration |
| FOLLOW-005 | IrInterpreter EvaluationState retained Variables, Results and SnapshotSelectors; IrProgramInterpreter occurrence/replay/current maps and builders; IrProgramBuilder construction lists; IrFactory interning registries | Open per-use review; locks, identity, ordering and deliberate heap APIs must be preserved |
| FOLLOW-006 | BvEncoder.cs, BvEncoder.Arrays/Fields/References/Strings: 19 collection fields and local depth/decoder/observation accumulators, text construction, array witness updates | Implemented persistent collections with separate declaration/witness encounter order; SMT 356 passed |
| FOLLOW-007 | CallableSolverSession query caches/selectors/core indices; HornInvariantSearch maps/lists/recursive accumulators and retained Expr[]; Z3ExpressionOwner reverse disposal list | Implemented persistent maps/lists and ordered quantifier traversal; SMT 356 passed; native solver/assert/disposal and resource counters remain lifecycle boundaries |
| FOLLOW-008 | SmtNativeUtilities retained/mutated managed arrays | Managed reconstruction arrays converted; explicitly in-place ApplyArrayObservations retained for partial-write tests; SMT 356 passed |
| FOLLOW-009 | ContractExpressionBinder bound/receiver/pre-state sets, receiver order and substitution map | Implemented persistent sets/maps and separate receiver order; Contracts 640 passed |
| FOLLOW-010 | ContractClauseInventoryBuilder found/directClauseCache/ordinals/counters/body and clause builders | Implemented persistent discovery/cache/ordinal collections; Contracts 640 passed |
| FOLLOW-011 | ContractForSymbolMatcher buckets/byType/incoming/FIFO/accepted/symbol sets/type-layer stack/unmatched constraints and matched flags | Implemented persistent maps/queue/set/list with separate first-seen type order; Contracts 640 passed |
| FOLLOW-012 | ContractVariableTransfer, TotalContractInvocationMaterialization local maps/sets; ClosedContractAttributeBinding, ContractClauseSubstitution, ContractInvocationMaterialization, ContractIntrinsicValidator, ContractCanonicalization, ContractBinder and TotalContractExpressionBinder accumulators | Implemented immutable accumulators and owned callback sinks; strict AddUnique and SetItem remain distinct; Contracts 640 passed |
| FOLLOW-013 | WorkerMsBuildIntegrationTests CompilerManifestArtifact byte/string arrays | Implemented immutable arrays with hash boundary adapters; pending package gate |
| FOLLOW-014 | TypedWidthLongFuzzTests GeneratedCase nested Inputs and ShiftTypes Counts/Values; ScalarDifferentialMatrixTests six object[] properties; TypedFeatureLongFuzzTests Inputs/RunClr | Open; reflection adapters must preserve argument shape/nulls/order |
| FOLLOW-015 | SynchronizedCompoundGetterPurityAuditTests ReadTargetIl; WorkerTests CacheFiles/CreateRuntimeContractCases; WorkerVcShadowSourceGateTests QualificationCases/QualificationBaseline/AssertNativeResults; WorkerVcDivisionPrecisionTests, WorkerVcLoopTests, WorkerVcSourceCallTests postconditions; SemanticClaimRelativeSyntaxPathObservationTests IndependentPath; TotalProgramSourceReplayTests observation arrays | Open |
| FOLLOW-016 | WellSortedIrGenerator CollectVariables; SequenceCardinalityDomainTests IReadOnlyList fixed fixture | Open; seeded random lifecycle and actual mutable subjects retained |
| FOLLOW-017 | SharpProof.JsonDefaults.Indented and ApiTestEvidence.JsonOptions | Implemented MakeReadOnly with populated resolver; pending solution gate |
| FOLLOW-018 | DictionaryAnalyzerConfigOptions retained dictionary facade | Implemented immutable snapshot; preserves Dictionary/ImmutableDictionary comparers; opaque inputs require an explicit comparer; three new regression cases; final Gates gate pending |
| FOLLOW-019 | ContainerExecution timeout fallback; build.ps1 forcedConfigurations; Invoke-SharpProofContainer reusable commands; Doctor package/property names; SemanticTests class tables; PackageTests fixture groups | Implemented constant immutable arrays/map and ReadOnly module fallback; canonical parser/ordering/comparer/reimport probes passed |
| FOLLOW-020 | VerificationPublication construction dictionaries and path binding/restoration | Open; MSBuild public setters/item arrays are external binding contracts |
| FOLLOW-021 | AnalyzerGateHost concurrent outcomes/config map; NativeAllocationWitnessOracle collectible assembly cache | Open; persistent maps need atomic update/lock, losing native/context instances must not leak |
| FOLLOW-022 | CorpusFileTransaction retained updates across awaits, duplicate/path sets and entries; AllocationIlOracle map/stack/visited/branch arrays; CompilerClrTypeMatcher and NativeExceptionWitnessOracle name stacks | Open |
| FOLLOW-023 | CorpusGate sets/counters; NativeAllocationWitnessOracle and NativeStaticWriteWitnessOracle initial/alias maps; NativeCorpusVerifier lookup/outcomes; OpenSourceCorpusCatalog map/sets; OpenSourceCorpusImporter dedupe; OpenSourceCorpusRunner diagnostics and indexed assignment counters | Open; do not blanket-exempt algorithm counters |
| FOLLOW-024 | Host TrustedChildEnvironment and Fuzz FiniteDomainSmtFuzzing/FrontendFuzzing/TotalProgramFuzzing retained snapshots | Unchecked detail; agent shell unavailable during final re-read; boundary-level lead only |
| FOLLOW-025 | Protocol 14 open collection properties plus remaining init-only collection deep immutability | Open; preserve malformed/missing/null wire behavior and explicit public Canonicalize/SealManifest mutation contracts |

Follow-up coverage: IR/Dataflow 45 production files / 9,163 lines; SMT 13 files /
2,043 lines; Contracts 25 files / 5,124 lines; S-Z/lowercase test/support 344 files.
The infrastructure review covers 59 scripts, 54 non-vendored eng paths, 8
BuildTasks paths, root/config/GitHub/transitive build definitions. eng/common is
excluded and untouched. The Host/Fuzz detail gap is explicitly unchecked.

### Follow-up implementation evidence

- All 35 analyzer local sites are converted. Analyzer suite: 1,085 passed.
- Dataflow indexed vectors, SCC worklists and sorted scheduling use persistent
  collections. Dataflow suite: 118 passed.
- IR traversal, shared bottom-up rewrite/depth memos, snapshot selectors, block
  order, sequence-reader analysis and replay value/occurrence maps use immutable
  collections. Partial selector memo entries survive budget exhaustion through
  explicit returned state. IR suite: 471 passed before the final printer/free-set
  cleanup, which requires the next gate.
- CompilerCompilationModel now uses records/init and immutable collections for
  options, trees, references, modules, feature rows and line maps. Producer/test
  updates use record copies. CompilerTotalIlBodyProvider snapshots references,
  caches immutable bytes and opens PEReader over immutable image data.
  Targeted compiler/reporting/metadata/VC/companion Worker suite: 460 passed.
- Contract discovery/binding accumulators and companion graph classification use
  persistent collections with original comparers, FIFO/LIFO semantics and order.
  Attribute callbacks append to one owned immutable list. Contracts: 640 passed.
- SMT caches, native wrapper ownership list, authored-constant quantifier order,
  model aliases, array observations and text decoding use persistent collections.
  Separate immutable encounter-order lists preserve native declarations and
  witness decisions. SMT: 356 passed, including native disposal checks.
- The first broad Worker rerun ended with a Docker transport EOF before a final
  result. It is not counted as a passed run. A subsequent targeted Worker run
  completed successfully. Full final-suite verification remains required.
- Follow-up S-Z helper changes include immutable postconditions, cached file lists,
  runtime contract cases, decoded IL and generated-variable worklists; their
  latest source awaits the broader solution/Worker gate.
- The second A-H test detail pass was not received; the initial audit remains
  recorded, and strict residual coverage is still open. No missing report is
  counted as an absent violation.


### Syntax residual inventory

`docs/immutability-residuals.tsv` records syntax candidates from all tracked/new
non-vendored C# files, including test harness code, while Roslyn parsing naturally
excludes declarations inside embedded program strings. The unit is a source
syntax site (path, line, column, kind), not an asserted defect. Storage, primary
constructor inputs, array/read-only return shapes, allocations, and materializers
are separate review sites; one logical storage migration may resolve several.
Open rows are not confirmed violations and are not waived. Each needs source
review and either a verified replacement or an explicit boundary/test rationale.
Golden/sample/template program subjects are identified separately; eng/common
remains excluded by its pinned byte-for-byte contract.

The baseline's command surface has no `tooling layout` command. The attempted
canonical invocation is recorded in artifacts/immutability-layout-command.log;
the ordinary Architecture suite passed all five project/layout checks instead.
No project was moved or introduced; the new helper is included in the existing
IR project by its standard compile glob.

### Follow-up checkpoint: persistent algorithms and publication ownership

The coordinator checked all ten second-pass reports against current source.
The A-H report was received and its remaining fixture/helper findings are open;
the earlier note about a missing A-H report describes an earlier checkpoint.

- Compiler compilation snapshot models now use records, init-only properties,
  and immutable arrays. Producer rewrites use copies. The publication helper
  exposes its published copy; 66 fixture setup sites now use that copy.
  A regression verifies that publication does not rewrite the producer.
- All 13 methods with the 42 publication hash failures from the first broad
  follow-up run now pass in the 90-case targeted rerun.
- IR traversal, substitution, depth and selector memo state are persistent.
  Partial cache results are returned even on incomplete searches. Factory
  registries and block drafts now use persistent collections under their
  existing locking/lifecycle rules. The latest ordinary IR run passed 471 tests.
- Contracts and Analyzer callback caches now use ImmutableInterlocked with their
  original symbol/reference comparers; weak compilation ownership remains.
  The coordinator and read-only peer review found no stale memo/atomic updates.
- Scalar/typed fuzz retained case arrays are immutable, including nested input
  rows. Reflection converts the immutable outer argument row at invocation.
- Corpus transactions snapshot caller updates before awaits. Their paths,
  duplicate sets and staged entries are persistent. Allocation IL traversal
  and Host contract property validation now use persistent worklists/sets.
- The syntax inventory was regenerated with flattened detail fields and added
  return/primary-constructor coverage: 4,135 candidates across 845 paths from
  1,806 parsed files at that scan. Twelve program-subject sites are excluded.
  These counts are review candidates, not a claim that 4,123 defects remain.
- Canonical formatting passed; exported changes were reviewed and applied.
  The earlier broad run passed 18 unaffected projects, including Package
  (336 passed, one existing skip). A fresh full ordinary run is in progress
  against this checkpoint; its result is not yet counted as passed.

Remaining work includes Portable/compiler artifact/protocol models, the detailed
retained helper/input findings, and classification or replacement of remaining
syntax sites. This checkpoint is not the comprehensive completion condition.

### Verified follow-up and Portable IR checkpoint

- Commit d0be2e21b passed all 19 ordinary projects: 11,594 passed, zero
  failures, one existing package skip. Worker passed 5,330; Gates 470;
  Analyzer 1,085. Evidence: artifacts/immutability-followup-all-tests2.log.
- All ten Portable IR wire model declarations are records with init-only
  properties and immutable arrays. Producer constructors defensively capture
  arrays; explicit parameterless JSON constructors avoid exposing positional
  record members or normalizing explicit JSON null.
- A focused canonical .NET probe established that the default ImmutableArray
  JSON converter rejects null. PortableIrArrayJsonConverter preserves the
  former wire null sentinel. Omitted/empty arrays remain initialized empty;
  explicit null remains default and reaches the existing decoder checks.
  WireArraysPreserveOmittedEmptyAndExplicitNull is the regression.
- Encoding tables, index maps and block snapshots are persistent. Recursive
  table encoding computes each row before rebinding its slot, preserving rows
  appended by recursive encoding. Published decoded collections/maps are
  immutable snapshots behind their existing read-only API shapes.
- Malformed fixture updates now return copied rows/graphs and publish them
  back to artifact owners. The pure test helper preserves reference aliases
  for shared rows, source spans, locations and instructions. Sequential
  updates return both the new row and graph.
- The first focused run passed 705 of 706 cases; its sole failure exposed a
  missing graph reattachment in the golden void-call corruption fixture.
  After correction, all 706 passed. Canonical full formatting passed with
  zero output changes. Evidence: artifacts/immutability-portable-tests2.log
  and artifacts/immutability-portable-format.log.
- NativeAllocation, NativePurity, WorkerTcb and PortableTotalExecution copy
  propagation were independently reviewed against current disk; no lost
  mutation was found. The golden program and expected output are unchanged.

Commit f248435fe passed all 19 ordinary projects: 11,595 passed, zero failures,
one existing package skip (Worker 5,331; Gates 470). Evidence:
artifacts/immutability-portable-all-tests.log. Performance, coverage and corpus
categories were excluded.
Compiler/protocol model fields, remaining helper findings and syntax candidate
classification remain open; this is not the comprehensive completion condition.
Final Portable checkpoint preservation check passed: 1,992 original
raw/interpolated literal tokens across 340 changed C# files are unchanged.
The refreshed syntax inventory parses 1,808 tracked/new non-vendored C# files
and records 4,052 candidates, including 12 explicit program-subject exclusions.
Evidence: artifacts/immutability-portable-preservation2.log.
### Compiler artifact and retained helper follow-up

- CompilerArtifact wire records now use init-only properties and immutable array
  storage, preserving nullable DisjointInputs and default-array wire null sentinels.
  EvidenceSha256 remains settable for the explicitly tested in-place Seal API.
- Producer attachment and canonicalization return copies. Reachable-source queues,
  maps, stacks and sets are persistent, preserving FIFO/LIFO order, symbol/ordinal
  comparers, reentrant enqueue publication and final canonical sorting.
- Corruption fixtures replace rows and publish them through owning artifacts.
  Peer review caught lost helper returns and compound assignments; corrections
  preserve append/bitmask operations and artifact ownership. Filtered assumption
  clause aliases map back by original reference into the complete clause list.
- All 193 focused compiler/diagnostic/source-assumption/retained-input checks pass.
  Evidence: artifacts/immutability-compiler-model-focused2.log. The full ordinary
  run completed all 19 projects: 11,590 passed, five CodeFix assertion failures,
  one existing package skip. Worker passed 5,331, Gates 470 and Package 336.
  The five failures were Has.Count reflection against ImmutableArray; commit
  0e1cefcee changes those assertions to Has.Length. All 20 CodeFix tests then
  passed in artifacts/immutability-codefix-array-tests.log. This is combined
  verification; a fresh full run of the corrected commit has not been claimed.
- Retained PE bytes, fixed region tables, generated domain samples, diagnostic
  observations, options snapshots and code-action lists now use immutable storage.
  Additional metadata traversal and corpus/fuzz lookup state is persistent.
  Script validation tables preserve explicit key order and immutable values.
- Canonical script/C# formatting passed; the exported patch contained only script
  indentation and was reviewed and applied. Literal preservation verified 2,018
  original raw/interpolated tokens across 369 changed C# files. Evidence:
  artifacts/immutability-compiler-model-format.log and
  artifacts/immutability-compiler-model-preservation.log.
The refreshed inventory parses 1,809 tracked/new non-vendored C# files and records
3,887 syntax candidates (12 explicit program-subject exclusions). These remain
review candidates; the comprehensive stopping condition is still open.

### PowerShell exhaustive syntax inventory

- Universe: all 58 tracked .ps1/.psm1 files outside artifacts and pinned eng/common,
  including executable documentation and container scripts. Canonical PowerShell
  AST parsing passed all 58; no parse failures.
- docs/immutability-powershell-residuals.tsv records 912 deduplicated syntax sites:
  140 generic collection constructors, 205 mutation calls, 74 compound assignments,
  35 increments, 49 indexed assignments and 409 assignments containing literal
  collections. Literal detection includes nested RHS syntax, so these are review
  candidates rather than 912 confirmed violations. Framework/native adapters and
  explicit mutable subjects require evidence-backed classification.
- Stable content-based IDs avoid shifting every row when a candidate is removed.
  Coverage, source path, line/column, owner, syntax detail and review status are
  recorded for each candidate. Main remains the sole writer.
- The first inventory attempt saw Git's container ownership guard and produced an
  empty inventory. The corrected run configured a read-only per-process safe
  directory, asserted a nonempty tracked universe and regenerated the file.
  Evidence: artifacts/immutability-powershell-inventory2.log.
- Completion requires classifying every recorded candidate and resolving each
  confirmed unnecessary mutation. This script inventory closes the coverage gap;
  it does not close the remaining implementation work.
PowerShell count reconciliation: the scanner initially printed 915 observations
before deduplication; the stored table has 912 distinct rows (205 mutation calls).
Reporting now counts the distinct stored rows, matching the review universe.

### Protocol scalar and ownership checkpoint

- Converted 59 remaining protocol scalar properties to init and their owners to
  records. Fourteen wire collection properties remain for the next phase.
  Both public in-place canonicalization APIs, SealManifest, compiler Seal and the
  six budget setters retain their explicitly tested mutation contracts.
- Response assembly, serialization, cache reads, SARIF and publication now copy
  owners before canonicalization or path/hash rebinding. Already-canonical
  admitted manifest identity is retained after comparing all sorted collections;
  unsorted owners are copied rather than rewriting producer data.
- Fixture scalar corruptions use returned record copies. Compound operators,
  six manifest-hash callbacks, 18 SetUnknown callers, alias propagation and
  foreach replacement semantics were checked. A peer review found one SARIF
  manifest alias that an existing certainty assertion did not detect; its owner
  is rebound and exported claim kind/evidence/contract metadata is now asserted.
- The solution builds with zero warnings/errors. All 566 affected Worker checks
  and all 336 Package checks pass (one existing package skip). Follow-up cache,
  ownership and SARIF checks pass: 145 Worker plus 25 Package cases. Evidence:
  artifacts/immutability-protocol-scalars-worker-tests2.log,
  artifacts/immutability-protocol-scalars-package-tests.log,
  artifacts/immutability-protocol-copy-cache-tests2.log and
  artifacts/immutability-protocol-copy-sarif-tests2.log.
- Canonical full formatting passed with zero changes. Literal preservation
  verified 2,042 original raw/interpolated tokens across 388 changed C# files.
  The refreshed inventory parses 1,809 tracked/new non-vendored C# files and
  records 3,822 syntax candidates, including 12 program-subject exclusions.
  Evidence: artifacts/immutability-protocol-scalars-format.log and
  artifacts/immutability-protocol-scalars-preservation.log.
- The C# candidate IDs now use content/location hashes, matching the stable-ID
  strategy for scripts. Shared fixture replacement helpers moved to eng/testing
  and compile into the two consuming test projects. No project was added/moved.
  The baseline layout command remains unavailable; architecture checks passed
  in the earlier full ordinary run.
PowerShell follow-up traced 12 ProcessStartInfo.ArgumentList adapters and seven
already-immutable dictionary chains; their exact IDs are classified in the TSV.
The remaining 893 script candidates are open. Rescanning preserves verified
statuses only when the content/location identity matches.

### Protocol collections and persistent construction checkpoint

- All 14 protocol collection properties now retain ImmutableArray values. Their
  setters remain for the tested public in-place canonicalization contract.
  A custom converter preserves explicit null as default, initialized empty as
  [], and null elements for validation. JsonRequired still rejects omissions.
- Cache envelope collections use the same null sentinel. Admission rejects
  defaults before enumeration or Length and checks both input and manifest
  hashes before canonicalization. Peer review caught an accidentally removed
  input-hash comparison; it was restored and a tampered-envelope regression
  was added before committing this batch.
- All 666 focused protocol/cache/compiler/result/golden Worker checks passed
  before the tampered-envelope addition and decoder follow-up. These later
  changes have separate checks in progress; that result is not attributed to
  the later working tree. Evidence:
  artifacts/immutability-protocol-arrays-worker-tests4.log.
- Compiler Total decoding now uses persistent sets, lists, maps and queues.
  Strict duplicate rejection, comparer choices, FIFO/append order, prologue
  state joins and byte-for-byte metadata digest input were reviewed. Compiler
  metadata resolution keeps reference-key equality, ordinal hash matching,
  first representative selection and ambiguous first-two ordering.
- IR opaque argument snapshots occur before validation/locking, recursive
  rebuilding preserves pure/impure normalization, and external identity buckets
  are getter-only immutable holders. Reentrant comparisons still execute
  outside the gate and retry against the latest bucket before publication.
- Effects and analyzer diagnostic construction now rebind persistent collections;
  symbol and ordinal comparers and diagnostic publication order are preserved.
- 135 exact read-only IrVarId/IrValue fixture maps in 49 I-R test files now use
  immutable maps. Runtime values retain object identity. Seven files with
  explicit malformed-state tests or construction updates were excluded from
  this mechanical batch and remain individually tracked.
- Host lease ownership and publication path snapshots use immutable collections;
  file handles retain their required disposal lifecycle. Native corpus replay
  environment and result lookups are persistent.
- Changed-project selection uses immutable sets, stacks, nested maps and project
  records. Remove's old Boolean result is preserved by Contains before Remove.
  Six isolated Git repository workflows match the original selector exactly:
  dependency changes, shared compile inputs, documentation, package changes,
  embedded-newline paths and deleted projects. Evidence:
  artifacts/immutability-selector-check.log. The actual worktree PlanOnly also
  selected the same expected 17 ordinary projects plus package shards.
- Portable decoding now uses immutable indexed state and publishes immutable
  rows. Recursive type results are computed before reading/publishing the latest
  persistent collection, so recursive children are not overwritten by a stale
  receiver. Peer review and focused graph/cycle checks are in progress.
- Remaining C# and script inventory entries are still candidates requiring
  individual review. Local builders, callback logs and algorithm worklists are
  not blanket exemptions. The comprehensive stopping condition remains open.
Follow-up evidence for this checkpoint:

- Both 19-project ordinary runs completed. The first passed 11,597 checks with
  one failure; the fixture-map run passed 11,598 with one failure. Both had one
  existing package skip. The sole failure in each was the stale NUnit
  Matches<string[]> predicate over an immutable proof core. It now uses
  ImmutableArray<string>; its isolated rerun passed. These are not reported as
  passing full-suite runs. Evidence:
  artifacts/immutability-protocol-arrays-all-tests.log,
  artifacts/immutability-fixture-maps-all-tests.log and
  artifacts/immutability-proof-core-array-test.log.
- The decoder/cycle/cache follow-up passed all 198 Worker checks, including the
  new cache-envelope input-hash corruption case. The finite-domain search now
  passes immutable environments through recursion and uses the immutable IR
  traversal stack; all 302 ordinary Fuzz checks passed.
- A second exact fixture batch converted another 101 constructions in 72 files;
  nine maps requiring producer adapters and two golden-owned helpers remain for
  individual follow-up. Across both batches, 236 fixture maps are immutable.
- The current affected-fixture run passed 2,345 checks across eight projects:
  203 IR, 23 Testing, 27 Effects, 441 Frontend, 327 Contracts, 34 Analyzer,
  three Fuzz and 1,287 Worker. Gates compilation rejected a negative-capacity
  compatibility guard under CA2208/CA1512. The guard now validates a named
  capacity parameter and explicitly preserves the former exception's absent
  ActualValue; focused corpus validation and compilation are in progress.
- Catalog/importer construction and corpus parallel observations now publish
  persistent collections. Task.WhenAll preserves input task order without a
  shared indexed buffer. Semaphore cancellation/release behavior is unchanged.
- Literal preservation verified 2,471 original raw/interpolated tokens across
  498 changed C# files. The scanner parses 1,810 non-vendored C# files and records
  3,374 candidates, including 12 program-subject exclusions and 21 classified
  protocol mutation contracts. C# rescans now preserve classifications when
  candidate identity matches. Evidence:
  artifacts/immutability-array-fixture-preservation2.log.
- The refreshed PowerShell inventory has 898 candidates across 58 scripts with
  zero parse failures: 53 are classified and 845 remain open. Main traced 34
  additional persistent mutation-shaped calls in changed-test selection.
  Evidence: artifacts/immutability-powershell-inventory5.log.
- Canonical script/C# formatting passed with no patch; later capacity-guard
  changes are validated separately. Evidence:
  artifacts/immutability-array-fixture-format2.log.
Corpus follow-up completed: all 63 selected checks passed. The final capacity
guard builds with zero warnings/errors and preserves the old capacity parameter
name and absent ActualValue. Evidence: artifacts/immutability-corpus-persistent-tests.log
and artifacts/immutability-corpus-guard-build.log. The 2,345 affected fixture checks
passed; their solution invocation failed only because of the now-corrected Gates
compile-time guard diagnostics.

## Updated construction preference

The user clarified that immutable collection builders are preferred for local
construction and accumulation, instead of repeated assignments such as
items = items.Add(value). Scoped ImmutableArray, ImmutableList, ImmutableHashSet,
and ImmutableDictionary builders satisfy this policy when their mutable state
does not escape and completed results are frozen. Existing builder candidates
must be reviewed for ownership and publication, rather than removed merely
because the builder itself mutates. Shared concurrent caches continue to publish
immutable snapshots through atomic updates. Earlier persistent accumulator
conversions are being reviewed against this clarified preference.

Construction and compiler follow-up checkpoint:

- Scoped immutable builders now cover 39 additional accumulators in 28 files,
  plus hand-reviewed contract, effect, IR, compiler-reference and fixture
  producers. Only completed immutable values cross publication boundaries.
  Main reviewed the restricted syntax transformation and all receiver/freeze
  boundaries; an independent read-only review confirmed all 39 units.
- Frontend symbol/purity caches preserve symbol/reference/ordinal comparers,
  recursive memo entries, weak compilation ownership, and atomic concurrent
  publication. CaptureTrees publishes a cached ImmutableList to preserve its
  reference-identity contract. References and line maps publish ImmutableArray
  snapshots; module/closure limits now propagate cumulative immutable budget
  records across the entire reference closure.
- Two malformed/operand test helpers now use immutable outer environments:
  malformed runtime payloads and missing operands remain the actual subjects.
  Another 22 fixture maps and their producer adapters were converted earlier
  in this checkpoint. Construction helpers now prefer scoped builders.
- The changed-test selector uses scoped builders for graph/input construction
  and freezes nested graph values and argument lists before consumption.
  Native ArgumentList remains a BCL adapter. Main traced every remaining
  selector candidate; 18 native argument-routing scenarios match HEAD.
  Explicit GetEnumerator is required when freezing PowerShell dictionary
  builders. Evidence: immutability-selector-final-builder-arguments-check.log.
- The latest full solution build passed with zero warnings/errors before the
  final scoped-accumulator additions. The latest ordinary-suite invocation is
  still completing. It exposed an over-applied fixture ToImmutable call on an
  already immutable map; that call is corrected and all 640 Contracts tests
  pass. The earlier full invocation exposed one stale Has.Length assertion on
  the new ImmutableList capture cache; Has.Count is correct and all four
  capture-coverage follow-up tests pass. Neither full invocation is reported
  as a successful full-suite run.
- Canonical C#/PowerShell formatting completed successfully. A batched Git
  object check independently verified 2,592 original raw/interpolated literal
  tokens across 523 changed C# files, including the final assertion correction.
  Evidence: immutability-scoped-builders-batched-literals.log.
- The refreshed inventories contain 3,336 C# syntax candidates across 1,810
  non-vendored files and 907 PowerShell candidates across 58 scripts, with zero
  script parse failures. Thirty-nine C# builder rows are explicitly verified;
  the remaining open candidates still require individual review. Inventory
  counts include permissible builders and native adapters.
- On pushed commit 2622d8fe4, CodeQL C# and Actions pass. Python CodeQL fails
  because extraction processed zero Python modules (GitHub job 117765993351).
  This is recorded as a CI configuration limitation, not a passing check.
  The comprehensive stopping condition remains open.

## Scoped builder and lowering checkpoint

The construction preference is implemented in protocol/portable JSON converters,
dataflow adjacency and indexed cycle state, expression argument buffers, source
call buffers, compiler metadata matching and specification-pack selection. All
published collections freeze before leaving their construction owner. Persistent
maps remain in recursive lowering caches and memoized results.

Region/finally/filter lowering preserves insertion order with explicit ordered
builders for finalization. Candidate unwind prefixes freeze at each capture;
finally/filter transfer builders stay private to the construction phase. The
continuation planner preserves prior HashSet slot-reuse priority with a bounded
ordered construction helper and a regression test. Canonical contract variables
are an explicit builder session: deferred pre-state creation remains live until
the final bound-variable snapshot, while parameter/result properties expose no
setters. Main checked the read-only peer reviews and traced cross-partial users.

Production inventory and source-document resolution now use scoped immutable
builders and frozen arrays. Ordered PowerShell objects remain JSON wire adapters,
and native process argument/output arrays remain transient boundary adapters.
A real SDK fixture compares exact prior/current JSON in source-only and portable
PDB modes (one source project, eight sequence points). Both match. The source
resolver's existing 20 regression checks pass. The final inventory fixture also
passes after freezing SDK references, MSBuild results and CodeView records.

Verification evidence for this checkpoint:

- The ordinary solution suite completed with 11,601 passed, zero failed, and one
  existing platform skip across 19 projects; performance, coverage and corpus
  categories were excluded. Evidence: immutability-continuation-expression-all-tests2.log.
- Subsequent region/call changes passed all 1,969 Frontend tests. The final
  filter-prefix freeze passed 26 focused Total checks. Compiler IL/pack builders
  passed 45 checks. Canonical variable builders passed all 640 Contracts checks.
  Evidence: immutability-region-call-builders-frontend-tests.log,
  immutability-region-filter-freeze-tests.log,
  immutability-il-pack-builders-worker-tests.log,
  immutability-canonical-variable-builder-tests.log.
- The literal-preservation check verifies 2,592 original raw/interpolated tokens
  across 538 changed C# files. The refreshed C# inventory contains 3,308 syntax
  candidates across 1,810 files; 123 newly reviewed candidate rows were classified.
  The PowerShell inventory contains 901 candidates across 58 scripts with zero
  parse failures; all 48 current inventory/resolver rows are classified. Evidence:
  immutability-region-contract-preservation.log,
  immutability-inventory-final-residual-refresh.log,
  immutability-inventory-final-builder-fixture-check2.log.
- At pushed e1ad8413c9b1da199bbbeb2de45e713d812bc909, CodeQL C# and Actions
  pass. Python CodeQL again fails with no Python source processed (exit 32,
  run 39161811639/job 117776502533). No unrelated CI configuration was changed.

The comprehensive stopping condition remains open. Outstanding work includes
remaining production compiler/portable validation and construction state, replay
configuration, helper input ownership, test fixture producers, and PowerShell
orchestration. Syntax candidates still require individual classification; a
permitted scoped builder is not counted as an unresolved mutability violation.

## Replay, portable graph, solver, and orchestration checkpoint

Portable graph encoding uses construction-owned dictionary/array builders while
preserving recursive index reservation before row encoding and replacement after
recursion. Hot index lookup reads the builder directly; only publication freezes
the index maps. Decoded graph arrays and encoded index maps now expose immutable
types. Three static projection catalogs use scoped builders for 30 unique entries.
Reachable-source collection uses scoped builders for registered bodies, roots,
admission sets and deconstruction work; published bodies freeze after reachability
pruning. Existing FIFO/DFS worklists and strict duplicate behavior are preserved.

Replay options and expression-lowerer configuration now use init-only properties.
Their observers are installed at construction. The IR project links the existing
IsExternalInit polyfill for its netstandard target. Evaluation snapshots variable
maps, retaining existing immutable maps and known Dictionary comparers. Mutable
heaps, object referents and observer side effects remain execution subjects. Two
regressions prove that an observer cannot rewrite a captured environment and that
a custom input dictionary comparer survives the snapshot. Those two mutable test
maps are explicitly classified as subjects, not unresolved construction violations.

Solver model maps, local depth state, unsat-core indices, proposals, rules, text
units, bounds and congruence construction now prefer scoped builders. The shared
AddUnique builder overload retains strict duplicate rejection. The immutable array
overlay helper builds a detached result; the distinct explicitly in-place overlay
API retains its original partial-write behavior. A review caught an over-broad
edit to that API; it was removed before the successful solution-wide run.

PowerShell orchestration uses scoped builders for build arguments, scheduler
worklists/results, failure lists, coverage-credit sets and timing rows. Results
freeze before publication. The acceptance cache stores a recursively immutable
contract graph; all 621 original values match, including scalar types and array
order. Timing accepts IList<object> with AllowEmptyCollection, supporting both
existing List<object> inputs and immutable builders. Native process state remains
a runtime adapter. Six malformed/optional automation timeout shapes match the
prior outputs/errors, including array/scalar fallback and explicit-null rejection.

Evidence:

- The latest ordinary solution run passes all 19 projects: 11,603 passed, zero
  failed, one existing platform skip. Performance, coverage and corpus categories
  are excluded. Evidence: immutability-replay-il-smt-all-tests.log. Earlier targeted
  compilation failures from the missing IR init polyfill and new guard sites were
  corrected; their failed logs are not treated as successful checks.
- Real native baseline/current scheduler fixtures pass completion ordering, slot
  exclusion, failure retention, timeout cleanup and preparation-failure cleanup.
  Two independent project builds and missing-project failures also pass for both
  versions. Evidence: immutability-container-native-build-scheduler-check.log.
- Cache/timing/coverage checks pass, including action output, record-on-failure,
  detached timing snapshots and the int[] coverage boundary. Evidence:
  immutability-container-cache-timing-check3.log. Six timeout output/error shapes
  match: immutability-container-timeout-shapes-check2.log.
- Canonical C# whitespace and PowerShell formatting pass. The literal checker
  preserves 2,592 original raw/interpolated tokens across 540 changed C# files.
  Inventory: 3,295 C# candidates across 1,811 files; 890 PowerShell candidates
  across 58 scripts with zero parse failures. There are 3,050 C# and 744
  PowerShell rows still open. Evidence: immutability-replay-il-smt-preservation.log,
  immutability-container-final-residual-refresh.log.
- Pushed e4b333b8e9e669bc73955c0c9fb58a3f7144c60e passes CodeQL C# and
  Actions. Python extraction again processes no code and fails with exit 32,
  run 39164861594/job 117785570316. This unrelated configuration gap remains open.

Next work includes remaining compiler/portable validation collections, fuzz/replay
outer-container ownership, local constant candidates, and the remaining script
producers. A read-only literal-local review identified 16 provisional const-string
candidates; main verification and numeric/computed-local review remain pending.
Comprehensive completion is not claimed while these coverage gaps remain.

## Local constant follow-up inventory

The focused string/bool literal review is recorded in
[immutability-const-audit.tsv](immutability-const-audit.tsv). Main independently
checked all declarations and their binding/mutation contexts with Roslyn. There
are 17 verified const-string candidates across 14 files, including one additional
reference-case helper literal. That helper already points to the interned literal
"a"; const preserves the shared string identity and leaves the actual mutable
array subjects unchanged. Raw program text remains byte-for-byte identical.
Evidence: immutability-const-local-review2.log. Implementation and focused tests
follow this committed inventory. Numeric/computed constants and other filtered
binding-sensitive cases still require semantic review; this focused inventory
is not represented as exhaustive local-constant coverage.


## Master conflict integration checkpoint

Merged source: origin/master e97c6c56d58fba9f5c5c20ac59f4c7f44ff4424a.
The prior branch and unfinished const/fuzz work are preserved by local checkpoint
fab1ec540d7d50b7296d21037def5ae7ec12f7c4.

The coordinator resolved all 23 conflicted paths after read-only reviews by the
ten existing agents. Master's src/ project layout, eng/scripts/ paths, compiler
semantics split, strong-name payload checks, package publication order, indexed
field-store codec and keyed replay/resource accounting are retained. The older
combined analyzer/worker test source was split into the two canonical fixtures;
the immutable fixture updates were transferred to the worker file.

New integration sites requiring adaptation:
- ToolApplicationTests constructs a copied immutable response instead of assigning
  init-only fields.
- KeyedEntryModelTests accepts the decoder's immutable variable map.
- ExactCounterexampleReplayTests copies immutable model rows while preserving the
  deliberately mutable invalid-model array subject and its null/slice/reorder tests.
- AnalyzerPipelineLongFuzzTests explicitly adapts the protocol snapshot to the
  replay helper's array boundary.
- ListRuntimeEffects adapts its local argument array to the immutable opaque-call
  API.
- PackagedProductFeed materializes immutable package arrays before transferring
  TempDirectory ownership; its validation helper accepts immutable expected IDs.
- RunIndexedAsync keeps the cancellation token last and replay status projections
  retain explicit guards. Its indexed builder freezes only after every task joins.

Canonical tooling image rebuilt for the new entrypoint and pinned runtime.
Layout passed: 2,607 source files, 59 projects, 151 pinned Arcade common files and
59 evaluated projects. The new-master literal comparison preserves 2,591
raw/interpolated tokens across 547 changed C# files; newly introduced files are
outside this comparison. C# whitespace required no changes on the resolved files.
Script formatting checked 87 PowerShell files and 19 shell scripts; the one
PackageIdentity formatting patch was applied.

The first four merged-tree builds exposed the compatibility sites above; their
failed logs are not successful validation. The next solution build passed with
zero warnings and zero errors (immutability-master-all-tests5.log). Its unfiltered
test run included an isolated-host-only Coverage fixture; that run was stopped.
Ordinary solution verification is in progress with the repository filter excluding
Performance, Coverage and Corpus in immutability-master-ordinary-tests.log.
Evidence: immutability-master-layout.log,
immutability-master-literals.log and immutability-master-script-format.log.
The comprehensive residual inventories still describe the preceding checkpoint;
current-path coverage must be refreshed after master integration.


## Current master follow-up coverage

After the conflict integration, inventories were regenerated against the current
tracked tree. Verified Git renames mapped 3,270 C# rows, 860 PowerShell rows and
all 17 const rows before regeneration. Status transfers require the same mapped
path, line, column, kind and detail; changed candidates return to open review.

Current syntax inventory: 1,865 non-vendored C# files / 3,376 candidates and
84 PowerShell scripts / 1,139 candidates, with zero PowerShell parse failures.
There are 3,174 open C# candidates and 1,023 open PowerShell candidates.
These are syntax candidates, including builders, subject arrays and native
adapters, rather than a claim that every row is a confirmed violation.

The coordinator traced the following new-master findings before implementation:

| ID | Current file / member | Confirmed correction | Status |
| --- | --- | --- | --- |
| IMM-M001 | ToolApplication.ExplainAsync | Replace duplicate-option set and entry/map accumulation with scoped immutable builders; freeze publication, retain strict duplicate checks and rendered order. | Implemented; focused checks passed |
| IMM-M002 | ToolApplication.ExplanationEntry | Preserve validating constructor and JSON property names in a sealed record. | Implemented; focused checks passed |
| IMM-M003 | CompilerConfiguration.ReadOptionAliases | Replace repeated candidates = candidates.Add with a scoped immutable array builder and frozen read phase. | Implemented; focused checks passed |
| IMM-M004 | TotalLoweringContext.TrackFreshCollections | Build definitions/captures and exclusion sets immutably; preserve explicit insertion order, symbol identity comparer, FIFO escape propagation, LIFO traversal and shared work budget. | Implemented; focused checks passed |
| IMM-M005 | TotalLoweringContext dictionary/list tracking fields | Publish persistent immutable freshness sets after each insertion, preserving partial-state visibility and accumulation across both tracking passes. | Implemented; focused checks passed |
| IMM-M006 | RoslynTotalExpressionLowerer dictionary/list field caches | Use persistent immutable maps with existing tuple equality and strict duplicate behavior. | Implemented; focused checks passed |
| IMM-M007 | RoslynTotalExpressionLowerer dictionary/list argument assembly | Build positional arguments through scoped builders and freeze before IR publication; retain evaluation order and immutable CallSnapshot/member signature APIs. | Implemented; focused checks passed |

Read-only review of the seven compiler semantics session/model/effect files found
no unsafe retained mutable data. Lazy caches and the identity allocating IrFactory
remain intentional resource/state contracts. The new compiler compilation models
already use records, init properties and immutable arrays. Tool process handles,
console subscriptions, cancellation sources and bounded stream buffers remain
native/resource adapters. Numeric literals alone are immutable and are not counted
as violations merely because they could be given a named constant.

The tool review's suggested MoveToImmutable for an unconstrained entry builder
was rejected: capacity need not equal count. Publication must use ToImmutable.


## Optional CLI acceptance fixture integration

Exact pushed commit 77f76ca97 failed optional CLI qualification in CI run
39169170159, job 117798439704. The log reports CS8852 in the C# program generated
by eng/scripts/Test-SharpProofToolConsumer.ps1: it assigns CacheStatus and four
WorkerVersionSummary properties after construction. These protocol snapshots are
now init-only. IMM-M008 records the required compatibility correction: construct
Summary and Versions inside the response initializer, retaining all existing
fixture values and the explicit manifest sealing contract. This generated reader
fixture is an API consumer adapter, not a mutable program subject.

CodeQL C#, Actions and Python checks succeeded on exact pushed commit 77f76ca97.
The previous Python no-code failure is therefore historical and does not describe
the current head.


## Builder follow-up validation checkpoint

IMM-M001 through IMM-M007 are implemented. Tool/configuration selection: 103
tests passed across seven matching projects. Dictionary/list/freshness frontend:
83 passed. Worker dictionary/list oracles: 193 passed. The initial frontend
attempt reported four IDE0055 formatting errors; the canonical whitespace patch
was applied before the successful rerun. An independent read-only review checked
comparers, explicit encounter ordering, FIFO/LIFO behavior, positional builder
capacity, and incremental publication of freshness sets.

Evidence: immutability-master-tool-config-builders-tests.log,
immutability-master-fresh-collections-frontend-tests2.log and
immutability-master-fresh-collections-worker-tests.log.
Script formatting: 87 PowerShell files / 19 shell files, zero changes required.

The first merged ordinary solution run built without warnings/errors and recorded
12,062 passed, one failed and one platform skip across 20 projects. Its sole
failure was StrictPublicPackageBuildRejectsOpaqueDoubleEqualityProof: expected
one recorded claim, observed zero. Both exact master e97c6c56 and the current
merged tree pass that test in isolation. Full baseline and current package runs
are in progress to distinguish test ordering/build provenance from a migration
regression. This failure is not waived or represented as a successful suite.
The failed run copied merged source while HEAD was fab1ec540, so that version in
its artifact is expected; a pre-merge version label alone does not prove stale
source. Evidence: immutability-master-ordinary-tests.log,
immutability-master-baseline-nan-test.log and
immutability-master-current-nan-test.log.

Follow-up audits identified additional scoped fixture accumulators in Frontend
and Worker tests, corpus/fuzz production accumulation and new script producers.
Those remain open for coordinator tracing and implementation. In particular, a
fake backend queue, fuzz statistics maps and fixture construction lists are not
automatically exclusions: immutable roots or builders can preserve their behavior.
Reference/alias identity must be retained at the element level, which does not by
itself require a mutable outer cache. No broad agent exclusion is accepted
without coordinator evidence.


## Fixture ownership follow-up inventory

The coordinator inspected these additional sites and their consumers before edits:

| ID | Current file / member | Correction | Status |
| --- | --- | --- | --- |
| IMM-M009 | Frontend.Tests/AuthenticImplicitContractReferenceApiTests.AuthenticMissingReferenceResolver.Requests | Keep resolver reference identity and constructor guards; record requests in a private immutable-array builder and expose frozen snapshots. | Implemented; focused checks passed |
| IMM-M010 | Frontend.Tests/BulkFrontendCallsActiveSessionBoundaryTests observer lists | Use scoped immutable-array builders for prepared/entered method records; preserve callback order and reverse Leave order. | Implemented; focused checks passed |
| IMM-M011 | Worker.Tests/LongFuzz/LongFuzzSession counters | Use private immutable dictionary builders with Ordinal keys; retain scalar counters/resource clocks and freeze sorted statistics at publication. | Implemented; focused checks passed |
| IMM-M012 | Worker.Tests/WorkerTests.TestProject._additionalReferencePaths | Use a construction-owned immutable-array builder and snapshot before constructing metadata references. | Implemented; focused checks passed |
| IMM-M013 | Worker.Tests/WorkerTcbEdgeCaseTests.ScriptedBackend queue | Copy constructor inputs into an immutable queue and dequeue the immutable root; preserve result order, cancellation checks and call counting. | Implemented; focused checks passed |
| IMM-M014 | Worker/Vc/NativeEffectSiteVerifier readFlags | Declare the unchanged WorkerEffectSet constant as const; AnyStateReads is already a compile-time enum constant, with one read and no mutation/ref/capture/nameof-sensitive use. | Implemented; focused checks passed |

Both current and exact-master full package cohorts now pass 384 tests with one
platform skip. The optional CLI acceptance workflow passes after packing exact
commit 5bacb4c2d and installing/running that feed. A canonical Release semantic
cohort is running separately, following the PR workflow's separation from package
tests. The earlier all-project failure remains recorded while build interference
is investigated; it has not reproduced in either isolated or full package runs.

The literal comparison now covers 2,618 raw/interpolated tokens across 552 changed
C# files, all unchanged. The increase reflects the newly changed tool/frontend
files entering the comparison, not rewritten program text.


## Fixture and canonical verification checkpoint

IMM-M009 through IMM-M014 are implemented. Thirty-four focused frontend/worker
tests pass, including resolver requests, active-session Leave ordering, scripted
backend results and an added implementation-reference workflow. A bounded real
analyzer-pipeline fuzz run passes with seeds 1 and 2: two cases, zero hits,
compiled=2, manifest=2 and deterministically sorted statistic keys.
Evidence: immutability-fixture-owned-builders-tests.log,
immutability-fixture-longfuzz-stats-test.log and
immutability-longfuzz-stats-check/stats-analyzer-pipeline.json.

Exact committed source 5bacb4c2d passes the canonical Release semantic cohort:
11,679 tests, zero failures/skips across six isolated tasks. Current and exact
master package cohorts separately pass 384 tests with one platform skip.
The optional CLI packs and executes from the exact 5bacb4c2d feed.
Evidence: immutability-master-canonical-semantic-tests.log,
immutability-master-current-package-tests.log,
immutability-master-baseline-package-tests.log,
immutability-master-tool-pack.log and immutability-master-tool-consumer.log.

The all-project ordinary invocation's earlier NaN failure is retained as a
workflow-sensitive observation. Read-only tracing found no Gates/Worker writer
to the package's root Release outputs; those workflows use private temp copies.
No feed tampering was found. The NaN consumer lacks the SharedCompilationId set
by other package helpers, which is a plausible isolation gap, not a proven
cause. No production cache or source change was made on that hypothesis.

The original 17 const-string rows are implemented and covered by the semantic
and package checks. The additional enum constant is recorded as IMM-M014.
Additional test worklist and observation-return findings remain open.

## Frontend traversal follow-up inventory

IMM-M015: Twelve frontend test files retain 24 mutable traversal, observation,
or membership construction sites (BulkFrontendCallsBothConstructorCaptureRegression,
BulkFrontendCallsElidedConstructorCapture, BulkFrontendCallsConditionalFinallyMutationControls,
BulkFrontendCoreElisionTerminal, GenericCatchSemanticAgreement, LocalLifetimeRegionWorkApiCoverage,
OperationFreeRegionCycleApiCoverage, RequiresOnlyNestedInitializerContinuation,
SourceFinallyNoTransferApiCoverage, TypedExceptionSearch, TypedRegionProgramLowering,
and StackTypeCalculusCoverage tests). Coordinator inspected each traversal and consumer.
Use immutable stacks for LIFO worklists, scoped array/hash-set builders for observations
and visited membership, and freeze ordered observations before consumers. Preserve
successor push order, reversed child order in exception tracing, iterator laziness,
default comparers, and reference identity of CFG nodes. Region-ID maps and two
reachability membership snapshots can be immutable. Status: open, recorded before edits.
## Contract scoped construction follow-up inventory

IMM-M016: ContractClauseInventoryBuilder.CreateCore accumulates occurrences,
classification ordinals and a direct-clause cache through repeatedly rebound roots.
Use scoped array/dictionary builders, freeze occurrences before stable source sorting,
and keep ordinal updates inside the existing eager projection. Cache helpers receive
the owned builder without ref; strict AddUnique semantics remain. Retained compilation
caches remain immutable roots. Status: open.

IMM-M017: ContractExpressionBinder.BindOldArgument, ContractVariableTransfer.CreateSubstitutions,
TotalContractInvocationMaterialization.Bind and TotalContractExpressionBinder.BindTotalCore
have method-owned substitution/allowed-variable/ordered-clause accumulation. Use builders,
preserve overwrite versus duplicate rejection, and freeze substitutions before lowerer use
and clauses before publication. Retained binder pre-state caches remain immutable roots.
Coordinator inspected callsites and failure paths before edits. Status: open.
## Worker observation-return follow-up inventory

IMM-M018: Coordinator traced nine helper return shapes in BulkMetadataDefaultWorkBoundaryTests
(DivisionInstructions, MetadataInstructions), FixedChecksumAnchorConstructionBoundaryTests.Controls,
LongFuzz/CompositionalLongFuzzTests.BuildCycle, LongFuzz/ControlFlowLongFuzzTests.Run,
NativeMetadataShadowRequiresTests.Execute, ScalarPatternFlowTests.Execute,
TotalClaimResultTests.Checks, and WorkerVcShadowSourceGateTests.QualificationBaseline.
Use scoped builders and return frozen arrays/maps. Preserve null nontermination versus
initialized empty compile-failure results, JSON element cloning, Ordinal keys, tuple order,
and inner int[] reflection/SUT arguments. Adapt count assertions and unwrap nullable
immutable results before LINQ. Status: open, recorded before implementation.
## Script scoped construction follow-up inventory

IMM-M019: Assert-SharpProofJsonProperties duplicate-name set, Get-SharpProofDevCheckPlan
command list, Invoke-SharpProofDevCheck build list, Invoke-SharpProofDoctor check list,
Invoke-SharpProofLint check/summary lists, and Sync-SharpProofDocumentation lines use
mutable construction collections. Coordinator traced callers and output boundaries.
Use comparer-preserving immutable builders, freeze before JSON/filter/text publication,
and retain required object[] process/scheduler adapters and ordered output records.
These run in the canonical PowerShell runtime. The Windows PowerShell bootstrap
eng/build.ps1 is a separate compatibility boundary. Status: open before edits.
## Builder verification and next construction inventory

IMM-M015 passed 268 focused frontend tests. IMM-M016/M017 passed all 640 ordinary
Contracts tests. IMM-M018 passed 87 focused Worker tests with zero build warnings/errors;
this compiles both updated LongFuzz harnesses, including nullable immutable results.
The latest literal preservation check passes 2,724 tokens across 563 changed C# files.
Evidence: immutability-frontend-traversal-tests.log,
immutability-contract-scoped-builders-tests.log, immutability-worker-observation-tests.log,
and immutability-master-literals.log. IMM-M015 through IMM-M018: implemented.

IMM-M020: ContractForSymbolMatcher has scoped accumulation in BuildCompanionTargetIndex,
ClassifyCompanionRelationships, GetTypeLayers, TypeParameterConstraintsMatch and
UnmanagedCallingConventionTypesMatch. Coordinator traced consumers: preserve symbol
comparers, per-bucket companion order, first-seen type order, FIFO cycle elimination,
outermost-first type layers and first matching constraint removal. Use nested builders
and freeze at returned inventories. Status: open before edits.

IMM-M021: ForwardDataflowAnalysis.AnalyzeCore repeatedly replaces method-owned input,
output and update-count vectors, changed-output lists and sorted scheduling sets.
Use indexed array builders and sorted-set builders, snapshot each sorted pending batch
before clearing, and freeze final results. FindReachableBlocks can use a sorted-set
builder while retaining the DFS immutable stack. Preserve domain callback order,
widening thresholds, cycle ordering and exception checks. Status: open before edits.
## Additional worker observation returns

IMM-M022: Twelve return helpers in AliasOnlyConditionalAttributeAuditTests,
CompilerOwnedMethodBodyGraphTests, ConditionalAttributeCompilerAuthorityBoundaryTests (2),
ConditionalMetadataRejectionAuditTests, four Generic*TestedType* audit fixtures,
LongFuzz/ControlFlowRetypeCompileRegressionTests, and WorkerVcShadowSourceGateTests (2)
return mutable diagnostic/attribute/graph/IL/qualification/result arrays. Coordinator
inspected helper bodies and consumers. Return initialized immutable snapshots, use
scoped IL instruction builders, immutable opcode lookups, and retain native byte[]
method-body and reflection arguments. JSON arrays and sequence ordering stay unchanged.
Nested attribute parameter/argument projections can also be immutable. Status: open.

IMM-M019 now passes parsing, plan ordering, duplicate JSON property rejection,
doctor JSON/text and documentation drift/regeneration checks. The lint runner and its
negative fixtures pass in the canonical lint image. Formatting checks 87 PowerShell
and 19 shell files with zero formatted files. Evidence: immutability-script-builders-checks2.log
and immutability-builder-script-format.log. The first tooling-only lint probe lacked
actionlint; the verified run uses the repository's lint image. Status: implemented.

IMM-M020 passes 55 companion tests. IMM-M021 passes 118 dataflow tests; a subsequent
Worker dependency build found CA1868 on Contains-before-Add. It is corrected to the
builder Add Boolean guard, preserving insertion and push order. The bounded worker/fuzz
rerun is pending. Evidence: immutability-companion-builders-tests.log,
immutability-dataflow-builders-tests.log, immutability-worker-observation-longfuzz-tests.log.
## Fuzz/corpus construction follow-up inventory

IMM-M023: StructuralShrinkCandidates.GetCandidates uses rebound candidate/seen roots;
NativeCorpusVerifier.VerifyCallPreconditionsAsync uses a rebound method map and outcome
list; NativeEffectOracleGate allocates a Boolean array solely to count selected modes;
NativeStaticWriteWitnessOracle builds a read-only owner array. Coordinator inspected
callbacks and consumers. Use scoped builders and frozen publication, retain identity
comparer/deduplication and method-ID overwrite semantics, preserve oracle order and native
reflection argument arrays. Boolean mode counting can use scalar arithmetic. Status: open.

The corrected Worker transitive build has zero warnings/errors; the checksum construction
test passes. The two Explicit LongFuzz tests were not selected by the name-only filter;
a category-qualified bounded run is still required. This run is not evidence of their
execution. Evidence: immutability-worker-observation-longfuzz-tests2.log.
## Portable projection input follow-up inventory

IMM-M024: PortableIrModel projection decoder contexts and corresponding Decode* helpers
accept int[] despite initialized immutable wire Items. Coordinator traced all three callers:
PortableIrGraphCodec validates term/instruction/location Items before invoking projections.
Accept ImmutableArray<int> and remove seven transient Items.ToArray copies. Retain the
invalid/default wire rejection, legacy constructor adapters and params IrTerm[]/IrVarId[]
results required by IR APIs. DecodeMember can use the existing GetOrCreateMemberSnapshot
with immutable parameter types; preserve its preceding wire guard and scope validation.
Do not normalize default decoder input to empty. Status: open before edits.
## Script campaign and TCB construction inventory

IMM-M025: Get-SharpProofTcbPaths owns path/visited/pipeline/compile/inventory collections;
Invoke-SharpProofFuzzCampaign owns run records; Measure-SharpProofConsumerPerformance
owns measurements and median maps. Coordinator inspected constructors and publication.
Use immutable builders and frozen snapshots, preserve Ordinal path duplicate rejection,
first-seen pipeline order through a sidecar, rotating-before-retained fuzz runs, case-insensitive
median keys and finally-block report publication. Preserve process argument buffers,
ordered JSON records and the existing string[] TCB consumer boundary. Status: open.

Current regenerated syntax inventories cover 1,865 C# files (3,338 candidates) and
84 PowerShell files (1,133 candidates, zero parse errors). Fifty-two newly added C#
builder rows are coordinator-verified by exact changed line; 3,089 C# and 1,017 script
candidates remain open. Counts are syntax coverage, not confirmed violation counts.
## Verified construction checkpoint

IMM-M020 through IMM-M024 are implemented. The main coordinator reviewed actual
builder/decoder changes and agent hypotheses; no agent wrote code or audit files.
Checks now pass: 174 Worker/portable tests, 104 Gates corpus/oracle tests, 121 Fuzz
shrinker tests, and two explicitly selected LongFuzz tests with two cases each and
zero hits. The earlier nullable diagnostic-property compile error is corrected with
an explicitly nullable ImmutableArray<object>, preserving null versus array JSON.
The initially name-only LongFuzz selection and failed transitive builds remain in
logs as failures/selection gaps, not successes. Current literal preservation covers
2,737 raw/interpolated tokens across 564 changed C# files, all unchanged.
Evidence: immutability-worker-return-portable-snapshots-tests2.log,
immutability-gates-builders-tests.log, immutability-fuzz-shrink-builders-tests.log,
immutability-worker-observation-longfuzz-tests4.log, immutability-master-literals.log,
and immutability-observation-longfuzz-check/stats-{compositional,control-flow}.json.

IMM-M025 passes canonical parsing and direct TCB checks for order, Ordinal equality,
duplicate paths/projects, and first-error precedence. Campaign and performance
runtime publication remain to be verified against clean committed source/packages.
Evidence: immutability-tcb-builders-check.log. No performance improvement is claimed.
Further production/test/script candidate groups remain open; this is a checkpoint,
not completion of the comprehensive audit.
## Second master conflict checkpoint

After pushing 6825da042, master advanced to 6b062e611 (generic source List receiver
modeling, PR 328). GitHub reported four new conflicts. The coordinator fetched master
and started a merge from a clean worktree, prioritizing conflict resolution.
Conflicts: TotalLoweringContext.FreshCollections, TotalLoweringContext.Lists,
TotalSourceCallSession, and BvEncoder.References. Preserve exact List provenance,
source type specialization and bounded List count models from master while retaining
immutable roots, construction builders, ordered field observations and native adapters.
New SpecializeListParameter must publish the immutable Add result; IsKnownList must
accept immutable sets; fresh tracking publications must use the exact-list field names.
SMT count bounds must enumerate the ordered field sidecar and immutable observations,
then adapt frozen Boolean bounds to the Z3 params array. Status: merge unresolved.
Second master merge verification: all four conflicts are resolved, no markers or
unmerged paths remain, and the full solution builds with zero warnings/errors.
The affected filter passes 1,118 tests (1,001 Worker, 40 Frontend, 40 SMT, 29 Effects,
8 Gates), zero failures/skips. This is targeted coverage; projects with no matches
are not counted as tested. All 2,737 checked literal tokens remain unchanged versus
new master 6b062e611. The current universe is 1,867 C# files / 3,338 candidates and
84 PowerShell scripts / 1,126 candidates, with zero PowerShell parse failures.
Evidence: immutability-second-master-tests.log, immutability-second-master-literals.log,
immutability-second-master-inventory.log and immutability-second-master-whitespace.log.
The main coordinator inspected both sides of each conflict and all auto-merged
List/source-specialization changes. Status: resolved and verified; ready to push.
## Analyzer and effect fixpoint construction inventory

IMM-M026: EffectSummaryFixpoint.ComputeValidated/HasCycle owns body/edge/predecessor/
summary maps, DFS/SCC lists and worklists. Coordinator inspected the full algorithm.
Use immutable builders, explicit body encounter order, frozen adjacency and component
arrays, immutable DFS stacks/FIFO queues and Ordinal identity sets. Preserve strict
body duplicates, duplicate predecessor edges, sorted callee/component order, callback
and cancellation ordering, and summary overwrites. CompilerExceptionTypeIdentity can
append recursive generic-assembly identities to one scoped array builder and freeze;
retain containing-type-before-argument order and ordinal hierarchy sorting. Reject the
agent's unproven MoveToImmutable suggestion; use ToImmutable. Status: open before edits.

IMM-M027: Coordinator inspected scoped rebinding in AnalyzerGeneratedCodePolicy scopes,
OverridePreconditionDiagnostics substitutions, ContractForValidationEngine diagnostics/
candidates/overlaps, AdvisoryRequiresDiagnostics report identities/variables/callee
candidates, and AdvisoryEffectSites visited sets. Use builders with original comparers,
preserve first-report/candidate/traversal ordering, and freeze before deferred LINQ or
publication. Retain immutable traversal stacks and callback lifecycle. Status: open.

The read-only conflict review also found no defect in pushed merge f0e3b6fc8;
GitHub confirms its exact head is MERGEABLE. Campaign runtime verification uses a
private clean clone of that exact commit, independently of subsequent edits.
## IR and finite-domain fixture construction inventory

IMM-M028: Coordinator inspected observer/budget/term lists in IrPurityWriteWitnessTests,
IrTotalExecutionTests, Pr275AcyclicOrderCoverageTests, Pr275SemanticGuardCoverageTests,
and IrTraversalTests; expected operator maps in IrOperatorCatalogTests; ascending
chains in GeneratedDomainPropertyTests; and finite CFG edge/transfer construction in
FiniteCfgConcreteOracleTests. Use scoped array/hash/map builders and freeze before
assertions, evaluation and graph publication. Preserve callback order, duplicate edges,
finite transfer order, default enum/IR comparers and raw domain values.

The coordinator rejects the agent's IrTraversal Stack conversion: this test explicitly
passes a mutable stack to the mutable PushChildren overload and observes its mutation.
Keep that SUT stack; only its separate popped-observation list is construction state.
Likewise keep IrInputSnapshot mutation/comparer tests and keyed heap mutation subjects.
Fixed expected operation arrays and occurrence vectors can be immutable snapshots.
Status: open before edits.

IMM-M025 campaign publication is verified on exact clean f0e3b6fc8: rotating seed 1 and
retained seed 23063, one case each, two validated agreements, zero failing runs.
Evidence: immutability-campaign-builders-runtime.log and its campaign.json.
IMM-M027 passes 1,134 Analyzer tests; the M026 build's moved null guard hit CA2208,
then was changed to the repository guard helper retaining parameter name body.
Evidence: immutability-analyzer-scoped-builders-tests.log and
immutability-effect-fixpoint-builders-tests.log; corrected fixpoint rerun pending.
## Worker native evidence snapshot inventory

IMM-M029: Coordinator traced completed/pending result maps and effect lookups in
CallableVerificationPolicy, assumption declarations in CallableClaimResultAssembler,
trusted-boundary membership in EffectClaimResultAssembler, native claim/Requires
snapshots and used-ID accumulation in NativeEffectClaims, and exception/site verifier
snapshots. These do not reach native array APIs. Use scoped result-map/set builders
and immutable filtered arrays/maps, preserving Ordinal keys, claim order, first invalid
results, assumption ordinals and callback overwrite semantics. Freeze used IDs on return.
Private VerifyValidAsync accepts initialized immutable claims and rejects default input.

Reject plain ToImmutableDictionary for old strict ToDictionary sites: identical duplicate
key/value pairs may be accepted by immutable-map construction. Use the established
ImmutableDictionaryCapture.Create helper with explicit selectors to retain strict rejection.
Status: open before edits.

IMM-M026 passes 77 focused Worker tests after the guard fix. IMM-M028 passes all
485 ordinary IR tests and 118 Dataflow tests, including existing mutation-under-test
coverage. Evidence: immutability-effect-fixpoint-builders-tests2.log,
immutability-ir-fixture-builders-tests.log and immutability-dataflow-fixture-builders-tests.log.
## Analyzer/native verification checkpoint

IMM-M026-M029 are implemented and verified: 1,134 Analyzer tests, 77 fixpoint/identity
Worker tests, 485 IR tests, 118 Dataflow tests, and 205 native-policy/effect tests pass.
All builds have zero warnings/errors. The initial M029 build identified three new
identity selectors requiring explicit guards (SPINT001); guarded selectors now pass.
No duplicate-key behavior was relaxed. Literal preservation passes 2,738 tokens across
571 changed C# files against master 6b062e611. Layout verifies 2,609 source files,
59 projects and all 151 pinned common files, including 59 evaluated projects.
Evidence: immutability-analyzer-scoped-builders-tests.log,
immutability-effect-fixpoint-builders-tests2.log, immutability-ir-fixture-builders-tests.log,
immutability-dataflow-fixture-builders-tests.log, immutability-native-evidence-builders-tests2.log,
immutability-second-master-literals.log and immutability-second-master-layout.log.

The median builder now retains object-valued Int64/Double results like the original
hashtable, while preserving case-insensitive keys. Canonical parsing/TCB checks still
pass; performance publication is awaiting an exact package feed.

The current universe remains 1,867 C# files and 84 PowerShell scripts. Regeneration
records 3,324 C# candidates (3,023 open) and 1,126 PowerShell candidates (1,010 open).
Fifty new builder rows and seven reviewed SMT scratch/native-adapter rows are classified.
The mutable PushChildren stack remains an explicit SUT exception. Agent exclusions
based only on scoped state, identity caches or local arrays are not accepted: outer
builders can preserve identity, and immutable snapshots can preserve local read-only use.
Further reviewed frontend/worker/corpus/script and test-host findings remain open.
## Corpus observation and allocation cache construction inventory

IMM-M030: Coordinator inspected OpenSourceCorpusRunner diagnostic buckets, native
outcome/tree-target lookups, assignment-count vector and invalid-assignment projection.
Use strict immutable map capture, scoped array builders, immutable nested arrays and
frozen counts before validation. Keep tree reference equality and diagnostic order;
widen the validation helper's input to IReadOnlyList<int> for existing array callers.

NativeAllocationWitnessOracle input-slot maps, generic-choice vectors and per-invocation
alias maps can use builders/immutable snapshots while retaining IrValue/runtime object
identity and the original default object comparer. Its persistent assembly cache can
use an immutable dictionary root with an ordered construction sidecar for unloading;
keep resource handles mutable and preserve insertion/unload order, strict Add behavior,
cache hits, failed-load cleanup and disposal reset. Reflection argument/type arrays
remain explicit native API adapters. The coordinator rejects a blanket mutable-cache
exclusion based solely on the presence of aliasing or resource lifecycle.
Status: open before edits.
## Corpus and consumer publication verification checkpoint

IMM-M030 passes 150 targeted Gates tests with zero warnings/errors. Exact c21e6bcd3
Release packaging and the consumer performance workflow completed successfully with
15 measurement rows (three variants, restore plus four build scenarios), clean source
and matching package repository commits. This validates builder publication and report
shape; one iteration does not establish a performance improvement. Evidence:
immutability-corpus-allocation-builders-tests.log, immutability-builders-package-pack.log,
immutability-consumer-performance-runtime.log and its measurements.json.

## Companion and advisory construction inventory

IMM-M031: Coordinator traced ContractForCompanionValidator matching bucket construction
and diagnostic membership. Replace repeated immutable map/array/set rebinding with
scoped nested builders, strict initial key capture, symbol comparer preservation and
frozen buckets before diagnostics. Preserve Diagnose-before-union in ambiguous target
cases and membership-before-callback in mismatch/candidate cases.

AdvisoryInterpreter reachable/selected sets/maps and graph blocks/edges are construction
state; use builders and freeze at marker/graph publication. Run-owned gaps and marker
observations may use fresh builders at existing reset boundaries, frozen in every
returned result; preserve null resets and budget/cancellation behavior. ForgetOrigins
must enumerate the original state while removing from a builder. Alias cycle guards
may use hash builders. Persistent abstract-state dictionaries remain snapshots for
joins. Status: open before edits.
## Additional constant declaration inventory

IMM-M032: Coordinator inspected 16 scalar/enum constant declarations across 12 test
files identified by the read-only 1,248-file test/helper audit. These are fixed values
passed to explicitly typed APIs, integer arithmetic, assertions or generated-source
counts; no writes/ref/out uses occur. Convert callerValue, localCount, attribute
capabilities, effect unknown/allocation/uncertainty markers, expectedOwnerKind, two
operation stages and invalid stage, specification version/hash, natural-budget call
and field counts, and publication grace/expected milliseconds to typed constants.
Keep actual caller-owned exception arrays and captured/mutated variables unchanged.
The lexical audit does not establish exhaustive CFG coverage; other candidates remain
open. Status: open before edits.
## Specification validation and generated input construction inventory

IMM-M033: Coordinator traced ApiSpecTable's validatedTerms through the only internal
Validate/ValidateAndCompile callers, recursive ValidationContext and expansion memo.
Use immutable dictionary builders with DeclarationReferenceComparer.Instance, preserve
recursive lookup-before-add, strict memo Add and validated-node index overwrite, then
freeze once into ApiSpecTemplate. This is owned compiler construction, not an alias
or mutable SUT contract. Update internal sink parameters together.

WellSortedIrGenerator.CreateCase fills an optional sequence buffer and environment;
use array/map builders and freeze before Factory/GeneratedIrCase publication. Preserve
all random draws even when sequence values are not referenced, null/empty semantics,
value identity and traversal order. CollectVariables visited/output sets are local
construction guards and can use builders; its DFS stack remains immutable. Status:
open before edits.
## Differential oracle construction and identity inventory

IMM-M034: Coordinator traced IrCSharpDifferentialOracle's variable rank/type maps,
postorder term list, visited/DFS state, type memo and runtime identity trackers through
private consumers. Use sorted/dictionary/array/hash builders with default ID comparers,
immutable DFS stack preserving reverse child push order, and frozen variable/term
snapshots before source generation. Runtime identity maps and nested compared-array
sets may use scoped builders retaining ReferenceEqualityComparer.Instance; publish
runtime arrays into the memo before recursive materialization exactly as before.
Actual Array.CreateInstance/SetValue reflection subjects and StringBuilder output
remain native construction adapters. Status: open before edits.
## Build task path construction inventory

IMM-M035: Coordinator traced InvalidatePublishedResult output/input/compiler/publication
path arrays and capture paths, ResetPublishedVerification paths, and VerificationPublication
required options/private input arrays. These are read-only sequences consumed by
PublicationLease/PublicationPaths IEnumerable APIs; freeze as immutable arrays without
changing ordering or path comparers. Publication option/path maps are scoped construction:
use Ordinal builders and freeze before iteration and publication. Retain current Add
semantics and option exception order.

VerificationPublication.Prepare(string[]) explicitly rewrites its caller's argument
array; BuildTaskTests assert this mutable contract at lines 204-210 and 223-226.
Preserve that mutable subject and RunVerifier's writable argument adapter for now.
Public MSBuild setter/ITaskItem[] contracts and process char buffers are required
framework adapters. Status: open before edits.
## Script formatter and coverage receipt construction inventory

IMM-M036: Coordinator read Format-Scripts.ps1 and SharpProof.CoverageShardEvidence.ps1.
Formatter Arcade lookup and collected PowerShell/shell paths/failures can use Ordinal
map and array builders, freezing before lookup, traversal, native invocation/reporting.
Preserve tracked Git order, strict duplicate rejection, first-60 failures and pinned
vendor verification. Keep ProcessStartInfo.ArgumentList, text editing StringBuilder
and writable read buffers as explicit native adapters. Native shell tools receive a
string[] argument adapter from the frozen paths.

Coverage observed-assembly membership and per-receipt definitions can use Ordinal
hash/map builders, freezing before final membership and result checks respectively.
Keep conflict-before-overwrite checks, same-value duplicate allowance, receipt order
and required architecture assertions. Status: open before edits.
## Frontend interpreter input map inventory

IMM-M037: Coordinator inspected 27 ToDictionary interpreter input maps across 19
Frontend test files (ActualParenthesized, BoundSourceCallImport, BulkScalarHelper,
CompilerCfgConstruction, CompoundShiftPromotion, ElementGuardInput/NarrowInput,
DeferredStringConcatenation, ParamsCollection/OptionalAttributeShadowAdmission,
Pr275SourceSessionModelOwnership, RepeatedRequiresDefault/FilterWork, SourceLoweringNull,
SourceVariableBinding, SwitchExpressionTerminalElision, TypedLoopLowering,
CSharpOperationSemantics and BulkVisualBasicScalarDomain coverage). Use strict immutable
capture retaining default key equality, selector evaluation and value references.
All consumers accept IReadOnlyDictionary. ElementGuardNarrowInput's first environment
is deliberately edited between evaluations: use a scoped builder and freeze separately
for each interpreter call, retaining the shared sequence value identity. No embedded
source or native mutable input arrays change. Status: open before edits.
## Frontend reference memo and visit construction inventory

IMM-M038: Coordinator inspected TypedReferenceProgramLoweringTests' two alias roots,
private Value helper and input maps. Use nested scoped dictionary builders retaining
default IrTypeId and ReferenceEqualityComparer.Instance inner equality; preserve
lookup-before-materialization and strict Add after recursive values. Freeze the two
interpreter input maps through strict capture. Its sequential shared/different model
comparison can edit an owned builder and freeze per evaluation. Native input arrays
remain mutation/identity subjects. CompilerElisionContinuationPlannerTests' local
continuation set can use a hash builder and frozen snapshot before assertion/readout.
Status: open before edits.
## Release manifest and evidence construction inventory

IMM-M039: Coordinator read both release exporters. Package manifest TSV rows and ZIP
name membership can use immutable builders. Inventory needs an OrdinalIgnoreCase map
builder plus insertion-order sidecar and final ordered JSON adapter; preserve lower-case
IDs, duplicate rejection and final TSV newline.

Release evidence packages/files/relationships/checksums can use scoped array builders,
freezing at serialization. Archive IDs retain case-insensitive overwrite behavior;
entry IDs retain Ordinal lookup/reuse semantics. Extracted licenses need a case-insensitive
map builder plus first-insertion order sidecar, retaining later value overwrites and
final ordered value output. Keep ordered JSON records as serializer adapters; preserve
all package/component/file/relationship/checksum order and exact hashing inputs.
Status: open before edits.
## M030-M039 verified implementation checkpoint

M030 corpus tests pass 150; M031 ordinary Analyzer tests pass 1,134; M032-M034 focused
solution cohort passes 292 across nine projects; M035 build task/publication tests pass
95; M037-M038 full ordinary Frontend suite passes 2,051. Builds have zero warnings/errors.
M036 formatter/coverage regressions pass; M039 release security/evidence/attestation
fixtures pass, including 27 release-security cases and real pinned NuGet content probes.
Script formatter checks 87 PowerShell and 19 shell files with zero changes. Layout
passes 2,609 source files, 59 projects, 151 pinned common files and 59 evaluated projects.
Literal preservation passes 2,803 raw/interpolated tokens across 598 changed C# files.

The initial M031 build caught redundant set guards (CA1868), corrected using Add's
Boolean result. M034's first build caught a missed DFS-frame deconstruction, now fixed.
M037/M038 builds exposed missing helper access and a missed builder factory call.
Broad IR friend access also exposed existing null-validation diagnostics; that approach
was rejected. The test project now links the self-contained capture helper, with
explicit immutable imports and standard coalescing null guards. Its full ordinary
suite passes; unrelated guarded tests were not modified. Failed attempts remain in
artifacts/immutability-frontend-input-alias-builders-tests*.log for traceability.

Current regenerated universe: 1,867 non-vendored C# files, 3306 candidates
(2954 open), and 84 PowerShell scripts, 1117 candidates (966 open),
with zero parse failures. Coordinator classified 48 new C# builder/snapshot rows,
33 script builder rows and 16 additional verified constants. Open rows remain syntax
candidates requiring contract-based classification; this checkpoint is not completion.

Evidence: immutability-advisory-publication-builders-tests2.log,
immutability-spec-oracle-constant-tests2.log, immutability-build-task-paths-tests.log,
immutability-frontend-input-alias-builders-tests6.log, immutability-m036-script-tests.log,
immutability-m039-script-tests.log, immutability-m039-script-format.log,
immutability-m039-layout.log and immutability-second-master-literals.log.
## Passive verification-condition builder inventory

IMM-M040: Coordinator traced PassiveCallableVcBuilder's 23 private list/map fields,
per-block state, exception-code membership and substitution construction through plan,
edge and exit publication. Use scoped/lifecycle immutable builders and freeze getters,
Edge.State, Exit.State and substitutions. Preserve strict Dictionary.Add with AddUnique,
index overwrites and append duplicates/order. Incoming edge buckets stay builder-owned.

Two insertion orders affect freshness facts/labels: per-block state.Values and entry
field-reference values. Retain explicit first-insertion order sidecars, including
assignment overwrites and RHS evaluation before insertion. Heap.Stores, Edge/Exit
state and fact snapshots remain persistent immutable values; historical returns retain
their exact heap contents. IrSnapshotReads' immutable selector memo contract remains
unchanged in this bounded batch. Status: open before edits.
## Loop cutting and component construction inventory

IMM-M041: Coordinator traced both PassiveLoopCutter files, including DFS/SCC walks,
loop discovery, cell analysis and encoded block queue. Convert private collection fields
and scoped maps/sets/vectors to immutable builders, preserving strict Add with AddUnique.
Retain DFS stacks/FIFO queue as immutable roots and all Spend/cancellation locations.

Reachable discovery, loop-header insertion and each loop's node insertion affect
validation, instruction/fact order and work-budget sequencing. Preserve them with
explicit array sidecars; membership sets remain separate. Component nodes are already
unique after the global visited.Add gate, so an array builder preserves their original
insertion enumeration directly. Freeze copies before substitution, cells before storage,
and graph maps/sets before Encoding/Loop publication. Preserve nullable cell state as
heap-forgotten. Use existing CallSnapshot for immutable call arguments; Havoc has only
params-array APIs, so retain those explicit boundary adapters. Status: open before edits.
## Fuzz statistics observation correction inventory

IMM-M042: The full ordinary Worker run exposed two downstream reflection-based controls
that still cast LongFuzzSession._counts to Dictionary<string,int> after the earlier
statistics-builder conversion. Both fail with InvalidCastException before checking
purity outcomes. Coordinator traced all _counts consumers; only these two casts remain.
Expose an immutable Counts snapshot from the test session and read it directly. The
lock control needs separate snapshots before and after its second RunCase; preserve
pre-control refutation scalars and post-control proven count. Keep the private builder
and the session's intended mutable counting behavior. Status: open before edits.
## Candidate admission and invariant construction inventory

IMM-M043: Coordinator traced PassiveCallableCandidate validation sets/maps and
LoopInvariantCandidates construction. Use hash/map/array builders with existing default
comparers, strict capture for prior ToDictionary calls and immutable State result
vectors. Instruction admission needs an ordered array sidecar filled during strict
capture to retain instruction enumeration and exception sequencing in writers/reads/
reserved-marker validation. Preserve canonical parameter distinctness and marker checks.

Invariant conditions/lengths retain append order; variable and constant sets are sorted
before output. Freeze entering membership and substitution maps before use; preserve
copy overwrites/null conflict markers, cancellation checks and maximum candidate order.
State's three direct consumers (Generate, PassiveCallableSolver, checksum evidence)
only use enumeration/Length/projection and accept immutable vectors. Status: open before edits.
## Callable projection and entry observation inventory

IMM-M044: Coordinator traced TotalCallableVerifier entry labels/IDs, staged requires/
assumption projections, depth/core vectors and used-ID membership. Use scoped builders
and freeze at completion/callback setup boundaries, retaining Ordinal label/ID equality,
strict duplicate checks, canonical assumption order and existing projection stage order.
Widen CompleteEntry/ProjectEntry lookup parameters to IReadOnlyDictionary so old array/
dictionary test callers stay compatible while production supplies frozen roots.

The two direct fixture consumers can freeze builder labels/IDs, constraint vectors and
callback checks; native IR/runtime invocation argument adapters remain. Their read-only
prime and callable-target vectors can be immutable. Preserve before/after resource
accounting and rejection assertions. Status: open before edits.
## Solver core and Horn state construction inventory

IMM-M045: Coordinator traced PassiveCallableSolver's five local hash sets and Horn
state map. Use builders with Ordinal core labels/default operation/ordinal comparers,
strict AddUnique for the former state dictionary, and frozen Horn state/clauses.
HornInvariantSearch only queries the state map with TryGetValue; no map ordering escapes.

Call-precondition aggregate cores/assumptions are explicitly sorted before publication.
Houdini's successful union currently publishes HashSet insertion order, so preserve
first occurrence with array sidecars alongside builder membership sets. Keep candidate
removal, proof/witness priorities and cancellation/resource checks unchanged. Status:
open before edits.
## Layout validator construction inventory

IMM-M046: Coordinator read Test-SharpProofRepositoryLayout.ps1. Its failure/record/
evaluation and explicit-package-import mapping lists can use array builders, freezing
before mapping filters and final ordered report serialization. Covered-project and
unique-project-name membership can use Ordinal/OrdinalIgnoreCase hash builders;
freeze covered projects after solution discovery and preserve Add's duplicate result
for names. Keep source traversal, failure first-20 order, static import controls,
project evaluation order, pinned common validation and JSON report schema unchanged.
The report's ordered records remain serialization adapters. Status: open before edits.
## Protocol mutable-subject contract review

IMM-M047: Coordinator verified all 21 remaining ProtocolModel.generated.cs setters
against production mutators and explicit tests, rather than excluding them merely for
JSON. Fourteen ImmutableArray owner-replacement setters and manifest Hash support
public in-place Canonicalize/SealManifest APIs (ProtocolManifest.cs and ProtocolJson.cs).
ProtocolBoundaryContractTests observes canonicalization mutations; ProtocolJsonTests
separately verifies serialization copies do not rewrite producer owners. Preserve these
mutable subjects while their retained collection values remain immutable.

The six WorkerBudgets setters are an explicit mutable input subject:
WorkerResultAssemblerTests.AssemblySnapshotsMutableBudgets mutates the original query
limit after response creation and asserts the retained snapshot stays unchanged. Making
that subject init-only would remove the behavior under test. Preserve it under the
user's mutable-SUT exception. The generated file is hand-maintained; no active generator
needs regeneration. Null/default/empty wire-array distinctions remain unchanged.
Status: verified exclusions from setter removal; no protocol code changes.
## Frontend receiver, identity and modifier fixture inventory

IMM-M048: Coordinator inspected ArrayReceiverModelOwnershipTests, NullableComparerDomainTests,
ReferenceIdentityOracleTests, SourceCallImportBudgetSweepTests and ConditionalCustomModifierIdentityTests.
Replace multi-entry input/modifier maps with scoped builders and frozen snapshots, retaining
default IR-key equality and Ordinal modifier keys with index-overwrite semantics. Single-entry
input maps may use direct immutable singleton construction without incremental reassignment.
Freeze write observations after synchronous replay; retain the actual array/reference runtime
subjects and IrValue identities. Method/local syntax vectors can be immutable; the PE image
byte[] passed to MetadataReference.CreateFromImage remains its native adapter. Status: open before edits.
## M040-M048 implementation checkpoint and next frontend inventory

Coordinator implementation and read-only reviews confirm builder comparer, insertion-order
sidecars, snapshot boundaries and mutable-subject exclusions described above. Canonical
Linux tests pass: all 5,590 ordinary Worker tests; 1,686 loop tests; 174 candidate tests;
the two corrected LongFuzz counter snapshot controls; and all 42 M048 frontend tests.
Builds report zero warnings/errors. Repository layout validates 59 projects and 151
unchanged pinned common files. Raw/interpolated preservation checks 2,808 tokens across
611 changed C# files against master 6b062e611. The initial full Worker run had two stale
reflection casts, corrected and covered by the complete rerun. M044's missing namespace
and redundant immutable materialization compile errors were corrected before that rerun.
Inventory classifications cover 122 added builder/snapshot rows and eight layout-script
builder operations; 21 protocol setters are verified mutable-subject exclusions (M047).

IMM-M049: Coordinator inspected CSharpScalarOperatorSemanticsTests, FrontendLoweringTests
and GoldenLoweringTests. Fixed expected operator maps can use indexer-populated builders
and frozen snapshots; membership tables and retained syntax vectors can be immutable.
The operation visit counter uses reference equality and must freeze after Lower completes.
Golden recursive term memo must publish each key before recursion, retain first-entry
TryAdd semantics and sort output by term ID; use a scoped builder and freeze before
printing. Callback observations freeze after replay. Preserve native interpreter argument
arrays, emitted PE byte buffers and StringBuilder's text writer contract. No embedded
source/literal text may change. Status: open before edits.

IMM-M049 validation: all 219 scalar operator, frontend lowering and golden lowering
cases pass in the canonical Linux container, with zero build warnings/errors. Reference
operation maps retain ReferenceEqualityComparer.Instance; recursive memo snapshots sort
by term ID, and replay observation vectors retain insertion order. Native byte and
reflection argument arrays remain unchanged. Read-only review pending before commit.

IMM-M050: Coordinator inspected both NativeIntegerConditionalEmissionApiTests and
GenericNativeIntegerConditionalEmissionApiTests. TraceCalls returns a read-only sequence
of decoded Trace calls; its List<string> can become a scoped ImmutableArray builder and
its return type ImmutableArray<string>. Keep byte[] input/PE buffers, BitConverter and
metadata readers unchanged. Generic candidate method lookup vector can be immutable.
Existing Has.Length checks and JSON trace serialization accept immutable vectors.
Status: open before edits.

IMM-M051: Coordinator read Test-SharpProofPublicApiGovernance.ps1 and the read-only
agent review. Receipt accumulation can use an ImmutableArray<object> builder, frozen
once before ordered controls.json serialization. The outer API rollback vector can use
an ImmutableArray<byte[]> builder frozen before the private deletion control; preserve
each native byte[] and indexed restoration order. Keep project XML, both API files and
nullable source restoration in their existing finally blocks. Required process arguments,
file bytes and text operations remain adapters. Validate the actual four controls in
the canonical container: baseline, RS0016 missing files, RS0041 nullable metadata, restored.
Status: open before edits.

IMM-M052: Coordinator read the worker cache eviction and launcher parser/projection
sources, including the read-only agent findings. Freeze sorted cache file observations
with ToImmutableArray; actual filesystem metadata remains live. Launcher callable-ID
lookup maps use strict ImmutableDictionaryCapture.Create with Ordinal keys, preserving
Dictionary duplicate/null failures. Parser construction uses an Ordinal builder and
TryAdd's false-on-duplicate behavior, freezing before arguments retention. Presentation
assumption vectors/membership and path candidate vectors can be immutable snapshots.
SARIF's normalized path segment stack uses an array builder with identical RemoveAt
semantics, freezing before Join. Preserve serializer dictionary property order, native
CLI/string[] and PE/file byte[] adapters, and runtime mutable lane/result buffers until
their synchronization/indexed-write contracts are separately addressed. Validate launcher,
SARIF, cache eviction and path admission tests. Status: open before edits.

IMM-M053: Coordinator inspected RuntimeDependencyTests, LongFuzz/ExactCounterexampleReplay,
Pr275NativeAssumptionMapTests and Pr275OwnedSpecTermDepthAdmissionTests. Runtime assembly
closure traversal uses immutable FIFO queue plus an Ordinal hash builder for visited
membership, preserving enqueue/dequeue and load order. Exact replay display strings and
binding validation use scoped builders; preserve false-on-duplicate/null InvalidModel
returns, freeze after four-entry validation, and retain original runtime model decoding.
Read-only one-entry/empty interpreter and proof maps become immutable values. Preserve
runtime assembly resource objects, model elements, byte/reflection boundaries and the
string decoder's text writer. Status: open before edits.

IMM-M054: Coordinator read semantic scheduler construction/timing/finally paths in
Invoke-SharpProofSemanticTests.ps1. Prior durations use an OrdinalIgnoreCase builder,
retaining later timing-file overwrite semantics, frozen before task estimates. Tasks,
timings and failures use scoped array builders; freeze selected tasks after estimates
and before sorting/parallel scheduler consumption, timings inside finally for partial
evidence, and failures before ordered count/join. Coverage-group filtering must retain
append order and duplicate tasks without a mutable List rebind. PSCustomObject task
records remain scheduler/serialization adapters, fully constructed before publication;
native process arguments and environment maps retain their required APIs. Validate
ordinary canonical Release semantic workflow, including scheduler and timing output.
Status: open before edits.

IMM-M055: Coordinator inspected all CompilerLoweredArtifact.Decode/DecodeEffects and
CompilerReachableSourceValidator.Validate paths. Callable and grouped-claim maps use
strict immutable capture with Ordinal keys. Duplicate/null effect ID validation remains
hash-builder membership, including the first null value before later codec validation.
Reachable-source document/body/graph maps use strict builders and phase snapshots;
iterate the original validated artifact vectors to retain Dictionary insertion-order
validation/cancellation order. Keep body/callee/parameter/manifest alignment checks and
all InvalidDataException/JsonException failure gates. DFS uses an immutable stack with
unchanged push/pop order; membership sets and call vectors can be immutable. No file,
wire, decoder or graph semantics change. Status: open before edits.

IMM-M056: Coordinator read MetadataProgramDifferentialOracle.RunAsync and
TotalProgramDifferentialOracle.RunAsync plus replay IReadOnlyList batch boundaries.
Use exact-capacity positional immutable builders for generated source/input vectors,
initialized Count for default false/null slots; freeze before indexed batch projection.
Body category counters use a seven-slot int builder; preserve all random calls, masks,
ordinal branching and array-read counters. Generated batch vectors become immutable.
Nested object?[] inputs and int[] reference subjects remain exact runtime/reflection
arguments with alias identity preserved. Do not alter generated source strings. Validate
existing metadata/total fuzz and emission lifecycle tests in the canonical container.
Status: open before edits.

IMM-M057: Coordinator read IrFieldSites.cs and its interpreter/SMT field consumers.
IrObjectState retains its public in-place WithField receiver identity; private/internal
storage can use get-only immutable-dictionary builders without changing that contract.
Execution-owned IrHeap maps/sets can use readonly builders, retaining IrValueIdentity,
ObjectKey and FieldKey comparers, nested int/tuple-key default equality, strict insertion
and existing index-overwrite behavior. Preserve clear-on-invalidate, fresh/default field
precedence, exact IrValue/object references and ConsumedApproximation timing. Internal
mutable execution flags remain live evaluation outcome state. Review consumers for
ordering/concrete Dictionary assumptions and validate heap/array/keyed/snapshot tests.
Status: open before edits.

M049-M057 validation checkpoint: M049 passed 219 frontend tests; M050 passed 12 native
call-trace tests; M051 passed all four real governance controls including RS0016/RS0041
negative controls and restored baseline. Two bypass-entrypoint attempts failed Git safe-
directory setup; the canonical host wrapper dev entrypoint supplied correct metadata
configuration and the actual control rerun passed. M052 passed 156 Worker cache/launcher
and 76 package launcher/SARIF tests. M053-M055 targeted artifact/replay cohort passed
344 tests. M056 passed 13 real metadata/total differential and emission lifecycle tests.
M057 passed all 485 IR tests. The M054 Release semantic source snapshot (before M055-M057)
passed 11,734 tests with zero failures/skips and zero build warnings/errors in six
scheduler tasks. Source/script format checks pass (87 PowerShell, 19 shell scripts).
All implementation diffs received separate read-only semantic review and coordinator
verification. Later M055-M057 changes remain covered by targeted cohorts rather than
being misrepresented as part of that earlier full semantic snapshot.

IMM-M058: Read-only exhaustive Frontend.Tests review identified 159 live ToArray calls:
142 ordinary/eager read-only snapshots and 17 required native/alias subjects. Coordinator
verified the exclusion locations against regenerated source, correcting two shifted
FrontendLoweringTests IDs. Use Roslyn syntax spans to replace only selected invocation
names with ToImmutableArray, preserving embedded source text, eager ordering and exact
IrValue/IOperation/runtime element references. Add immutable imports only where absent.
Preserve rank buffers, Roslyn Construct arguments, PE/file/Assembly byte arrays, params
interpreter arguments and the two distinct-copy alias witnesses. Compile the complete
frontend project and run its ordinary suite; fix internal readonly helper type assumptions
where necessary without changing native argument contracts. Status: open before edits.

M057 read-only consumer review confirms no field-store enumeration/concrete Dictionary
consumer; nested map creation retains AddUnique and all reference/key comparers. Public
WithField still returns the same mutable subject. The current regenerated universe is
1,867 C# files (3,276 candidate rows) and 84 PowerShell scripts (1,106 rows); raw/interpolated
source preservation verifies 2,875 tokens across 618 changed C# files against master.

M058 dry-run coordinator correction: SourceParameterAdmissionBudgetCoverageTests:18 is
also a current object[] adapter for TypedProgramSubject.Invoke/Execute, not a read-only
snapshot conversion. Preserve it along with the previous 17 exclusions. The checked
syntax-token selection is therefore 141 replacements, with 18 native/alias buffers kept.
The scratch syntax tool's initial missing CompilationUnitSyntax cast was corrected before
any source write; the dry-run parsed all selected tokens and preserved raw source text.

IMM-M059: Coordinator read the four remaining Frontend.Tests ToDictionary interpreter
input maps (TypedProgramLoweringTests and three Visual Basic shift fixtures), the local
CFG stack, and GoldenSourceAnchors. Use strict ImmutableDictionaryCapture.Create for
inputs, preserving evaluation/duplicate/null order and the original IrValue instances.
Use immutable DFS Push/Pop in NestedLocalCfgFilteredExceptionBoundaryTests. Build token
start/end maps locally and freeze before constructor publication; the unexposed anchor
memo can use a readonly builder with existing immutable Add equality semantics. Build
spelling vectors with an exact-capacity array builder before Join. Preserve golden text,
span boundaries, occurrence counting, source-token order and native reflection arrays.
Status: open before edits.

IMM-M060: Coordinator read Test-SharpProofDocumentation.ps1 and the source-document
membership regression plus its IReadOnlySet consumer. Markdown anchor membership uses
an Ordinal hash builder; duplicate-heading counts use an OrdinalIgnoreCase dictionary
builder and retain increment/suffix rules. Files and inline/reference link targets use
ordered array builders, frozen before traversal, preserving generated-file inclusion,
PowerShell case-insensitive -notin behavior and duplicate link checks. Git output remains
its native process adapter. The source compile-membership input can freeze an Ordinal
hash builder before passing to IReadOnlySet; preserve the negative uncompiled-document
control and filesystem symlink containment checks. Validate actual link traversal and
all source-document controls in the canonical container. Status: open before edits.

IMM-M061: Coordinator read the remaining Worker projection/lane construction and passive
plan publication paths. Sorted/interrupted targets and settled result projections can
be immutable arrays; retain the concurrent indexed result array until all lanes settle
and retain the exact lane array lock identity. Lane construction uses an array builder,
with original backend ReferenceEquals rejection and cleanup order. Project immutable
callable/flattened-claim arrays without deduplication, preserving positional order.
Pass frozen entry assumptions, goals and normal-completion term directly into the plan
constructor; remove builder publication setters while preserving freeze/Fresh/Fact order.
Make plan allocation status get-only; freeze its two local filtered vectors. Replace the
launcher allowed-name factory input with an immutable array. Preserve mutable solver
resource lifecycle, cache read status and intentionally exercised CoreLib dictionary SUT.
Status: open before edits; validate full ordinary Worker suite after the change.

IMM-M062: Coordinator read third-party inventory validation, package discovery and the
existing regression consumers. Discovery returns an OrdinalIgnoreCase immutable map of
case-sensitive sorted immutable method vectors, preserving index overwrite and minimum-
count failures; retain mutable minimum-count fixtures and native discovery input arrays.
Third-party ZIP entry lookup and membership accumulators use scoped immutable builders,
freezing after archive/component discovery. Preserve strict duplicate-entry rejection,
null/schema validation, Ordinal key/path semantics, digests and disposal. Binary and
first-seen declared-path sidecars preserve failure diagnostics and first missing-pin order.
Read-only package/identity vectors can be immutable; parsed invalid/mutable JSON inputs,
ZIP/stream objects and raw file bytes remain validation/native subjects. PowerShell has
no general extension-method dispatch: replace Dictionary.TryAdd with explicit ContainsKey
then Add on the builder, retaining the original error before publication. Validate real
third-party regression controls and package-discovery class/case/minimum controls.
Status: open before edits.

IMM-M063: Coordinator inspected the host-only Invoke-SharpProof.ps1 wrapper. Its
incremental Docker argument vector currently rebinds arrays with +=; use a scoped
ImmutableArray<string> builder, append the existing worktree volumes, optional token
name, service and task arguments in the same order, then materialize the native string[]
once for Docker splatting. Keep input CLI arguments, Git path discovery, temporary Git
pointer ownership and cleanup, lint build selection and exit-code propagation unchanged.
This wrapper must remain host orchestration; all repository validation still runs in the
canonical Linux amd64 container. Validate through the real worktree-aware layout call.
Status: open before edits.

IMM-M064: Coordinator reviewed the exhaustive Frontend.Tests fixed-array findings.
Of 98 ArrayCreation and 18 typed collection-expression rows, preserve 26 native/alias
array creations and 13 typed runtime input buffers. The 72 fixed/materialized array
creations include the spelling buffer already fixed in M059; migrate the other 71 and
five readonly typed domains to ImmutableArray values. Preserve order, duplicates,
nullable element types, explicit ISymbol/IrTypeId typing and nested runtime array
identity. Use checked Roslyn nodes against the inventory's d670e2eff source checkpoint,
accounting for shifted imports/snapshot calls; change only initializer delimiters/types.
Reject mismatched or unsupported nodes before any write. Run the ordinary frontend
suite and keep reflection/rank/mutation arrays at their concrete native boundaries.
Status: open before edits.

IMM-M058-M064 verified checkpoint: M058 and the final M064 ordinary Frontend suites
passed all 2,051 tests; M059 focused maps/anchors passed 386; M057 SMT checks passed
368. M061 ordinary Worker suite passed all 5,590 tests, including heap builders and
cancellation/lane publication. All .NET runs reported zero warnings and errors.
M060 validated 713 documentation links and 20 source-document controls. M062 package
class/case/minimum discovery and third-party positive/negative inventory controls passed.
M063 real canonical layout passed (2,609 source files, 59 projects, 151 pinned common
files); argument probe retained all nine arguments, including empty and spaced values.
C# and PowerShell formatting passed. Literal comparison against master 6b062e611
preserved 2,959 raw/interpolated tokens across 646 changed C# files. Refreshed syntax
inventory: 1,867 C# files, 3,047 candidates; 84 PowerShell scripts, 1,096 candidates,
zero parser errors. Classified 8 new reviewed C# builder rows and 24 PowerShell builder
rows; remaining open rows still require coordinator review and are not completion claims.
GoldenSourceAnchors' builder cache remains confined to synchronous local consumers;
no concurrency contract was found. Native runtime arrays and mutable test subjects stay.
Status: M058-M064 implementation verified; comprehensive migration remains open.

Master synchronization checkpoint: fetched 7e0e485a14ff3d09511c34009008ef83cdba5964
(opaque List values and generic helper storage, PR #330). Commit the verified batch
before merging; retain new List provenance/storage semantics and existing immutable
snapshots. Recheck affected tests and literals against this new master before pushing.

IMM-M065: Coordinator and read-only agent reviewed CanonicalCohortEvidence.ps1 and
its real consumers. Replace case/name arrays, strict identity membership and lookup
construction with scoped immutable builders, freezing at phase boundaries. Preserve
Ordinal identities and sorting, OrdinalIgnoreCase former PowerShell map lookup, encoded
TRX collisions, historical aliases, duplicate-name multisets and custom missing-definition
errors. Keep ordered receipt/case objects and explicit native/JSON array boundaries.
Use builder.Sort(Ordinal) instead of mutable temporary sort arrays; append required
Worker assembly through a builder. Validate actual parser controls plus negative collision,
missing-definition and duplicate execution cases; retain authenticated GitHub/Git gates.
Status: open before edits.

Master 7e0e485a merge verified at 6821867d9: no textual conflicts; coordinator and
read-only agent reviewed automatic List/receiver/session merges and preserved opaque
value-domain and generic helper semantics. Full ordinary Release semantic suite passed
11,771 tests in 23 framework summaries and six isolated scheduler tasks, zero failures
or skips. Raw/interpolated comparison preserved all 2,959 tokens across 646 changed
C# files against this master. This run includes M058-M064; M065 began afterward.

IMM-M065 outcome: scoped builders and frozen internal snapshots preserve case-insensitive
map lookup, strict duplicate identity checks, ordinal sorted multisets and Worker assembly
inclusion. Parser regression controls pass deterministic ordering, encoded-name collision,
parameterized multiplicity, missing-definition diagnostics and duplicate execution/NUnit
identities. A separate synthetic publication probe produced byte-identical cohort JSON
against the pre-edit implementation and confirmed singleton/plural array wire shape.
Synthetic metadata was confined to that probe and is not authenticated CI evidence.
Script formatter checked 87 PowerShell and 19 shell files, formatting one file; the
publication probe passed again after formatting. Comprehensive residual audit stays open.

IMM-M066: Coordinator traced 23 remaining mutable construction sites in compiler
ClaimManifestBuilder, CompilerOwnedSyntaxPath and SemanticClaimIdentity. Use scoped
immutable array builders for LIFO worklists and reversed path emission, preserving push,
pop and child order. Hash builders preserve SymbolEqualityComparer/default object
comparers; ordinal rank/ID map builders retain index overwrites and strict AddUnique
rejection. Preserve first-seen required-method order explicitly before grouped ordinal
assignment; freeze guarded syntax, ordinal maps, selected trees and published IDs at
phase boundaries. Replace the operation child vector with an immutable snapshot. Hash
writers remain resource/stateful sinks. Validate manifest, query/structural identity,
reference ownership/budgets and original syntax-path sentinel controls.
Status: open before edits.

IMM-M067: Coordinator rechecked all remaining Worker production collection candidates.
Twenty-eight rows are existing owned builders, seven are required concurrent lane/slot,
compiler byte, mutable Dictionary probe, per-read status, native char or text-writer
boundaries. Seven SARIF arrays/maps remain confirmed violations, not mandatory mutable
serializer contracts. Convert result/notification accumulation to scoped array builders,
fixed nested vectors to ImmutableArray, and special-name JSON maps/document to private
readonly record shapes with explicit property names/order. Preserve exact SARIF key
order, outcome/policy projection and byte serialization; validate launcher/SARIF cases.
Status: open before edits.

IMM-M068: Coordinator reviewed five remaining frontend construction boundaries with
agent evidence. Replace the 126-entry operation-decision temporary dictionary with an
immutable map builder using original indexer overwrite semantics. Handler ordinal
membership uses a hash builder without enumerating it. Three comparison argument
buffers become Count-initialized immutable array builders, preserving default slots,
source evaluation order, named argument ordinal placement and receiver-first faults.
Freeze the single-symbol Distinct vector and narrow RegionFilterEdgeResolver's retained
map to ImmutableDictionary; all callers already pass immutable values. Keep original
null/missing-edge exceptions, SymbolEqualityComparer and lexical handler order. Validate
full ordinary Frontend and Worker comparison/reference-identity cases.
Status: open before edits.

IMM-M066 verified: 97 Worker manifest/path/evidence tests and 32 Analyzer semantic/
structural/reference ownership/budget tests pass with zero warnings/errors. Compiler
nullable flow required a local successful TryGetValue binding before assigning parentId;
this preserves the old successful lookup behavior. Read-only review found no traversal,
identity-byte, comparer or strict-duplicate regressions.

IMM-M067 verified: 23 existing SARIF/launcher integration cases pass in a private
canonical workspace, comparing every successful serialization byte-for-byte against the
pre-edit implementation. Explicit guarded constructors/get-only record properties meet
the repository guard analyzer. Preserve exact schema/property order, source roots,
fingerprints, notification fallback and result ordering. Formatter applied required
layout before successful checks. Reconciled current Worker production inventory rows:
29 existing scoped builder rows and seven native/state/SUT boundaries; no Open rows
remain in that syntax inventory scope. This is not a claim of repository completion.

IMM-M068 verified: all 2,051 ordinary Frontend tests and 1,021 Worker native-call,
reference-identity and comparison tests pass with zero warnings/errors. The initial
text transform missed the cast enum entry (OperationKind)52; corrected it and expanded
coordinator equality checking to all 126 ordered decision entries, including that cast.
The original 125-entry symbolic-only check was incomplete and is superseded. The
read-only review's other four migration sites preserve their original behavior.

M065-M068 refreshed coverage: 1,869 non-vendored C# files, 3,041 syntax candidates,
2,438 Open; 84 PowerShell scripts, zero parser errors, 1,094 candidates, 872 Open.
Open includes unchecked/stale/valid construction candidates as well as remaining actual
violations. Classified 29 new C# builder rows and 28 canonical PowerShell builder rows.
Literal check against master 7e0e485a preserved 2,959 raw/interpolated tokens across
651 changed C# files. Comprehensive classification and final exact-commit gates remain.

IMM-M069: Coordinator traced exact counterexample replay's retained model vector and
all three callers. It is internal validation input, not a native runtime argument array.
Use ImmutableArray<WorkerModelValue> through decoder, helper and analyzer harness;
reuse the protocol's immutable model root directly. Build intentionally invalid binding
vectors with ToBuilder and freeze before decoding, preserving missing/duplicate/null/
foreign/value cases and reverse-display order. Runtime CLR argument arrays and copies
remain native SUT invocation buffers. The replay test's observation count map becomes
an Ordinal builder; retain all source snippets and UTF-16 model displays unchanged.
Validate the full exact replay fixture, including its real analyzer/worker/CLR case.
Status: open before edits.

IMM-M070: Coordinator traced AnalyzerPipelineLongFuzzTests' internal metadata and
instrumentation state. Replace analyzer configuration map construction with an
OrdinalIgnoreCase immutable builder and freeze before the provider; its private option
adapters can retain the immutable map directly. Replace six read-only syntax/literal
query snapshots with immutable arrays. Construct TextChange and alias vectors through
scoped builders, freezing before phase traversal and WithChanges. Narrow private source
integer/string-length/array-length domains to ImmutableArray<int>, retaining first-seen
Distinct/sort semantics and initialization guards. Membership of existing runtime array
lengths becomes an immutable hash set. Keep concurrent analyzer exception collection,
assembly resources, generated-source text, reflection object[] and actual int[] CLR
inputs/copies intact. Validate exact replay plus ref-state/runtime coverage controls.
Status: open before edits.

IMM-M071: Coordinator and read-only review traced coverage shard evidence and package
identity metadata. Read-only filesystem/XML/ZIP/name/path vectors can be immutable while
preserving enumeration, Sort-Object comparers/duplicates and archive lifetimes. Keep
coverage Expected/Completed input arrays and inner mutable descriptor references, since
regression controls intentionally mutate Target. Preserve existing Ordinal builders and
conflicting-definition checks. Freeze identity construction in file order and publish
immutable identity/commit snapshots to internal consumers; keep explicit native arrays
at JSON tasks/files boundaries and preserve ordered wire records. Validate coverage
receipt negatives and real facade package identity controls, then bounded shard hash/
file-set controls for manifest publication.
Status: open before edits.

IMM-M069-M070 verified: all 33 exact replay tests pass, including the real analyzer/
worker/compiled CLR case. Combined replay, ref-state and runtime coverage fixtures pass
all 76 tests, zero failures/skips and zero build warnings/errors, including a rerun after
malformed-model boundary correction. Read-only review found that direct model-root reuse
moved a default-model failure outside the extraction try. Coordinator reproduced pinned
baseline default(ImmutableArray<int>).ToArray(): NullReferenceException. Explicit default
rejection now remains inside that try before outcome publication, preserving accumulated
WorkerResponseInvalid/WorkerException evidence and preventing replay. The diagnostic
now names the uninitialized model. Empty/null-element/duplicate model checks remain.
Metadata domain order, runtime array identities and source text are preserved. All 2,959
raw/interpolated tokens across 651 changed C# files still match master 7e0e485a.

IMM-M071 verified: real coverage receipt controls and facade identity/archive/provenance
positive/negative controls pass, including three main and two symbol packages. Additional
shard probes pass JSON array shape, phase mismatch, changed content hash, extra file and
missing-file checks. Read-only review identified empty descriptor indexing: retain the
old null result through an explicit Length guard before later validation. Existing controls
pass after that correction and after formatting. Final formatter checked 87 PowerShell
and 19 shell scripts with zero changes; inventory parsed all 84 scripts with zero errors.
Classified coordinator-reviewed frozen snapshot/builder rows, including earlier canonical
CreateRange capture wrappers that the syntactic scanner still reports as array literals.
The repository migration remains open; no unchecked row has been treated as absent.

IMM-M072: Coordinator checked 14 Effects.Tests ToArray candidates against current
master330 source and consumers. Preserve the emitted MemoryStream byte[] image adapter;
convert the other 13 read-only diagnostic, inventory, syntax, reflection-attribute and
instruction query snapshots in eight files. Narrow Collect/Markdown/intrinsic evidence
row vectors and FindExecutableCatalogSyntax to ImmutableArray, retaining initialized
input guards, order, duplicates and all embedded source text. Leave mutable List/Dictionary/
Queue/Stack SUT probes and runtime reflection arrays unchanged. Validate the entire
ordinary Effects test project, including new opaque List/generic storage controls.
Status: open before edits.

IMM-M073: Coordinator traced 13 readonly IR/Specs query snapshots identified by the
read-only audit, plus the coupled evidence publication. Immutable snapshots cover task
publication handles (WhenAll accepts IEnumerable), assertion/query results, reflection
field-value observations and enumerable collection-constructor inputs. Preserve actual
mutable runtime subjects, byte/native/params and concurrent Volatile buffers. Evidence
rows use a scoped immutable builder and ToImmutable (capacity is not proven), with an
immutable helper return; nullable witness query fallbacks become typed immutable Empty.
Use checked source nodes/IDs and validate the full ordinary IR and Specs projects,
including missing evidence controls and runtime collection probes.
Status: open before edits.

IMM-M072 verified: all 120 ordinary Effects tests pass, zero warnings/errors. Coordinator
and read-only review confirmed immutable inventory/helper signatures, node snapshots and
all 13 changed query calls. Master330's opaque List additions are present (41 lines added
to the previous 87-line file); its mutable List source and reflection arguments remain.
The earlier read-only statement that those additions were absent was incorrect.

IMM-M073 verified: all 485 ordinary IR tests and all 150 ordinary Specs tests pass,
zero failures/skips and zero warnings/errors. Evidence rows freeze through ToImmutable;
no exact-capacity assumption was made. Typed empty fallbacks preserve missing facet/
postcondition evidence. Actual native/concurrent/SUT arrays remain. Refreshed C# scanner
covers 1,869 files and 3,004 candidates; newly reviewed evidence builder classified.
Literal check against master 7e0e485a preserved 2,968 raw/interpolated tokens across
659 changed C# files. Additional tokens reflect newly changed source files in the
comparison universe. The comprehensive residual audit remains open.

Master synchronization: fresh fetch found cd3363c63108d54c31e5958ca602d967519f9b36
(PR329, sealed frontend classes and TotalParameterBinding composition). Commit the
verified M072-M073 changes before merging. Preserve composition-based input projection
and separate RegionIncompleteException catches alongside immutable snapshots.

IMM-M074: Coordinator rechecked the remaining Effects test construction sites. Convert
19 fixed array/typed collection domains, including only the outer array-of-array test
case vector; nested int[] operands, their clones and mutable IList probes stay native.
Convert evidence accumulation to an immutable array builder, two interpreter input maps
to strict immutable construction, and enum-value lookup to a strict Ordinal immutable map.
Preserve original Dictionary indexer overwrite semantics with builder indexers, and
ToDictionary duplicate rejection with ImmutableDictionaryCapture. Adapt EffectSummary's
params consumer to JoinFrom(Bottom, immutable effects). Preserve all source literals,
case order/identity and deliberately mutable collection/culture callback subjects.
Validate all 120 ordinary Effects cases, including compiler/CLR identity controls.
Status: open before edits.

IMM-M075: Coordinator rechecked the staged-publication and read-observer cases that the
read-only audit initially excluded too broadly. AtomicFileTests stages filenames only
locally before concurrent publication, so use a scoped immutable array builder and freeze
before task creation; retain native file bytes and barrier/atomic file behavior. Its
TemporaryFiles helper can publish an immutable directory snapshot. Read observer lists
in IrTotalExecutionTests are aggregation, not mutable subjects: use one builder per task,
freeze after settlement, and preserve exact lazy read order. Freeze their fixed expected/
task vectors and Task.WhenAll result snapshot. Keep concurrent tasks' observers separate
and preserve interpreter inputs/callback timing. Validate all ordinary IR tests.
Status: open before edits.

Master cd3363c63 verified at merge f253d2f6b: no textual conflicts. Coordinator and
read-only review confirmed composition-based Input projections, parameter/receiver order,
immutable storage and the separate RegionIncompleteException catch. Full ordinary Release
semantic suite passed 11,774 tests in 23 framework summaries and six isolated scheduler
tasks, zero failures/skips. This snapshot includes M072-M073 and the master merge,
before M074-M075. Literal comparison against cd3363c63 preserved 2,968 tokens across
659 changed C# files; later M074-M075 comparison preserves 2,968 across 661 files.

IMM-M074 verified: 120 ordinary Effects tests pass with zero warnings/errors after
replacing inaccessible internal capture helpers with local strict builder construction.
The previous ToDictionary duplicate contract remains explicit (compute value, check key,
then Add); original indexer initializer overwrites remain indexer writes. Read-only review
confirmed outer immutable case vectors retain nested mutable operands and source text.
Classified four new Effects builders and all 13 remaining deliberate native/SUT/text
boundaries. Current Effects syntax inventory has no Open rows; this scope statement does
not imply completion of other projects or untracked mutation shapes.

IMM-M075 verified: all 485 ordinary IR tests passed staged-publication and observer
builder changes. Then the optional observer input map was converted from persistent root
reassignment to a local builder, and all 65 IrTotalExecutionTests passed again, zero
warnings/errors. Each concurrent task owns its builder, snapshots publish after WhenAll,
and native bytes/atomic publication semantics are retained. TemporaryFiles publishes an
immutable directory snapshot. Nine reviewed new builder rows classified across M074-M075.
Latest inventory: 1,870 C# files, 2,980 syntax candidates, 2,370 Open. Remaining Open rows
and semantic coverage gaps still require review; comprehensive migration remains open.

IMM-M076: Coordinator validated the read-only agent's current selection of 50 fixed
ArrayCreation and three typed collection-expression domains across 26 IR/Specs test
files. These are read-only expectations, enumeration inputs and assertion operands;
retain six native byte inputs, two params-array contracts and actual mutable runtime
subjects. Use checked syntax nodes against current source, retaining element types,
nullable/tuple typing and literal contents. Couple AssertDistinct with immutable operator/
operand signatures and forced IrTerm element typing; tuple children become immutable;
counting enumerable keeps its enumeration counter and AsEnumerable adapter; nullable
facet filtering and reached.IndexOf retain original semantics. Validate all ordinary IR
and Specs tests, keeping traversal's explicit mutable Stack overload probe intact.
Status: open before edits.

IMM-M077: Coordinator traced FrontendFuzzing's seven readonly method/type/batch-slice
snapshots, name lookup, ordered method vector and environment/identity maps. Use immutable
arrays and scoped builders, freezing before ordered lookup/callback/publication. Preserve
TryAdd's duplicate-shape failure (not a thrown duplicate), strict environment/memo Add
semantics, default variable/type comparers and ReferenceEqualityComparer for IrValue/
CLR-array keys. Nested memo entries must publish only after recursive sequence creation,
as before; retain native long[] operands and exact sequence origin references.
FrontendSemanticEdgeCase.Arguments needs a separate design: a deliberate test exposes
different indexer/enumerator views, so do not collapse that public input boundary to a
single vector in this batch. Record it as pending, not excluded or complete. Validate
full ordinary Fuzz tests, including identity mismatch/topology and emission controls.
Status: open before edits.

IMM-M076 verified: all 485 ordinary IR and 150 ordinary Specs tests pass, zero failures/
skips and zero warnings/errors. Main reviewed all 53 checked-node excerpts before writes;
read-only review confirmed the coupled operator/operand signatures, tuple child typing,
counting enumerator, facet null filtering and IndexOf equality/first-match behavior.
Six native byte inputs, explicit params arrays and mutable traversal Stack remain SUT/API
contracts. Fixed vectors preserve order and all raw/embedded program contents.

IMM-M077 verified: all 302 ordinary Fuzz tests pass with zero warnings/errors. Read-only
review confirmed strict AddUnique semantics, TryAdd false-on-duplicate failures, ordered
method lookup and recursive memo insertion timing. All sequence origin keys retain
ReferenceEqualityComparer and exact original CLR array objects.
Coordinator resolved the deferred Arguments decision by tracing its actual tests:
SameContentSequenceIdentityMismatchIsDetected supplies different indexed/enumerated views;
FuzzDomainBehaviorTests supplies an invalid with-clone null before validation. The oracle
intentionally indexes only lowered bindings and separately enumerates for reflection.
Eager capture could add unused indexed accesses or move caller exceptions. Retain only
this explicit input adapter contract, not a blanket exemption for readonly interfaces.

Refreshed inventory: 1,870 C# files, 2,919 syntax candidates; eight new scoped builder
rows classified and the two argument-view input boundary rows retained. Literal check
against cd3363c63 preserves 2,968 raw/interpolated tokens across 666 changed C# files.
All remaining open rows and unchecked semantic shapes remain in scope.

IMM-M078: Coordinator selected 25 fixed IR/Specs input-map/postcondition initializers
from the verified 64f90998e source inventory (24 dictionaries and one fixed list).
These only supply read-only interpreter/spec inputs; scoped mutable substitution and
per-edge accumulation are separate remaining candidates. Convert fixed maps to immutable
factory expressions, preserving Dictionary.Add strictness through AddUnique and indexer
last-write semantics through SetItem. Fixed postconditions become ImmutableArray.
Use exact Roslyn nodes against saved source, accounting for shifted imports and earlier
array edits; reject changed nodes, unsupported constructors/comments and unexpected peers
before writes. No persistent root is reassigned during fixed expression construction.
Validate ordinary IR and Specs projects and preserve all invalid binding/value probes.
Status: open before edits.

IMM-M078 verified: all 485 ordinary IR and 150 ordinary Specs tests pass, with
zero failures/skips and zero compiler warnings/errors. The 25 fixed maps/list preserve
key/value evaluation order, indexer overwrite behavior and reference-valued operands.
Postcondition assertions use immutable Length. All embedded program literals remain
unchanged. Remaining dynamic substitution and binding accumulators stay in scope.

IMM-M079: Release manifest, release evidence, tool evidence, contained-path resolution
and embedded-symbol lookup retain read-only array captures. Freeze package/file/ZIP-entry
and artifact projections with typed immutable arrays; preserve their enumeration order,
case-sensitive filters, JSON array adapters and native package-identity parameter shape.
Reuse PEReader.ReadDebugDirectory's existing immutable result. Freeze path components
and explicitly enumerate the remaining suffix rather than assuming native range indexing.
Retain scalar loop/read counters, ordered JSON dictionaries and native PE/PDB buffers.
Validate release security/evidence and embedded-symbol regression scripts in the canonical
container, plus path containment, nonexistent suffix and symlink escape controls.
Status: open before edits.

IMM-M079 verified: release security (27 policy/content cases and real SDK repository
signature positive/wrong-pin/tampered controls), release evidence/SBOM/tool identity and
embedded PDB malformed-input regressions all pass. Path controls pass existing files,
missing suffixes, symlink/lexical escapes and non-directory traversal. Typed captures
retain original order, object identity, case-sensitive membership and JSON array shape.
Native PDB buffers and resource lifetimes remain unchanged. Current C# literal check
preserves 2,980 raw/interpolated tokens across 671 changed files against master cd3363c63.

M079 additional verification: 20 source-document checks and script formatting pass
(87 PowerShell, 19 shell, zero changes). Canonical reflection confirms PEReader's
ReadDebugDirectory returns ImmutableArray<DebugDirectoryEntry>; root reuse is immutable.

IMM-M080: Worker.Tests H-P read-only audit identifies 42 ToArray materializations
in local assertions, syntax/diagnostic projections and indexed construction scratch.
Convert these selected exact Roslyn invocation tokens to ToImmutableArray, retaining
order and referenced objects. Review coupled array-specific helper signatures, null/default
fallbacks and post-construction indexing before tests. Native PE/reflection/params and
intentionally mutable runtime aliases are excluded from this batch. Main validates all
reported sites, formats changed files and runs the ordinary Worker project tests.
Status: open before edits.

IMM-M081: Compiler producer still copies claim evidence into a temporary CLR array,
scalar-domain collection materializes ToArray, and wire-enum completeness creates a
read-only bool array. Use a capacity-scoped immutable builder and frozen evidence,
ToImmutableArray, and eager ImmutableArray.Create(...).All respectively. Preserve
all seven completeness calls in left-to-right order; do not short-circuit them.
EvidenceSha256 remains an explicit in-place Seal lifecycle contract: guard tests read
the same instance's digest and synchronization preparation aliases the sealed claim.
Changing it to init/with would alter the subject under test. Reviewed stale owned
builder inventory rows and native PE/hash/params adapters can be classified separately.
Status: open before edits.

IMM-M082: Specs instantiation already snapshots public substitution inputs, and template
slot/validated-term maps are immutable. Narrow only the private Instantiation constructor
and three retained fields; give the existing captured substitutions a typed local name.
Narrow validator variables through Validate, ValidateAndCompile and ValidationContext;
all callers supply the same immutable bySlot map. Preserve guard/evaluation order and
DeclarationReferenceComparer, validation builders and final table publication. Public
input API and failure contracts remain unchanged. Validate the ordinary Specs project.
Status: open before edits.

Coordinator checked 105 stale Open production rows against current source and the
read-only builder/ownership traces: owned immutable builders remain within lowering,
validation, traversal or capture owners; publication uses frozen collections. These
rows are classified as verified, not counted as additional source conversions.

IMM-M083: Worker.Tests Q-Z read-only audit covers 160 materializations: seven existing
builders, 110 read-only ToArray projections, fourteen strict map captures, four set
captures and 25 native/test-mutation boundaries. This batch selects only the 110
read-only array projections. Preserve enumeration/reference order and narrow coupled
helper signatures where needed; directly reuse already immutable roots. Keep native
PE/metadata/char/reflection/params buffers and Array.Reverse mutation fixtures.
TestProject source paths become immutable through its private constructor, with the
existing initialized-input guard retained. Main reviews exact invocation excerpts before
writes and validates the ordinary Worker project. Other maps/builders remain in scope.
Status: open before edits.

M083 coordinator correction before edits: RES-F7DEB076725D3937 is MemoryStream.ToArray
feeding File.WriteAllBytesAsync, not an enumerable projection. Preserve this native PE
buffer; select 109 rows. IndependentPath uses an immutable path plus LIFO immutable
stack; compiled outputs retain their already immutable publication type. TestProject
requires an immutable overload alongside its native params adapter. Reuse proof/core
roots that are already immutable rather than copying them again.

IMM-M084: CompilerTotalIlBodyProvider caches are immutable roots repeatedly rebound
with Add. Use readonly ordinal ImmutableDictionary builders for its two private caches,
retaining negative null-body caching and already-frozen PE images. Add behavior matches
the former ImmutableDictionary.Add (including equal-value duplicate acceptance).
Lifecycle trace: one provider per synchronous lowering, sequential lowering passes share
that owner, PrepareIl calls synchronously, Read never recursively resolves and no cache
escapes. Retain lazy captured references and native read buffers. Validate stream fills,
metadata body admission, scalar IL decoding and source-image-mutation cache identity.
A separate scan found 42 other production map/set rebindings needing ownership/fork
review; immutable stack/queue traversal rebindings are separate legitimate operations.
Status: open before edits.

IMM-M082 verified: all 150 ordinary Specs tests pass, zero failures/skips and zero
warnings/errors. Main traced the sole instantiation constructor and immutable bySlot
callers; public input capture, guards and reference comparer behavior are preserved.

IMM-M085: BuildTasks production review covers all 39 inventory rows: three existing
immutable constructions and 36 remaining framework/native contract rows. Main checked
MSBuild target parameter bindings, Required/Output task properties, native ITaskItem[]
shapes, Prepare(string[]) in-place publication rewriting and bounded pipe-draining
buffers. Preserve these explicit mutable APIs and stream/text resource owners. No
BuildTasks source change is needed; all current production syntax rows are classified.
Agent's additional source scan found cancellation/process ownership state and params
adapters only. This classification does not close unreviewed scopes elsewhere.

IMM-M080-M081 verified: all 5,627 ordinary Worker tests pass, zero failures/skips,
with zero build warnings/errors. First build correctly rejected two redundant immutable
copies (CA2009); main replaced those with direct initialized immutable root reuse and
reran the full project. The remaining 40 selected projections retain order and references.
Compiler evidence uses a scoped builder, scalar-domain output is frozen, and all seven
wire-enum checks still execute eagerly left-to-right before All. Main verified test
helper consumers and publication shapes; read-only review agrees. M083/M084 remain
pending their own validation on later source snapshots.

IMM-M084 verified: all 117 targeted stream-fill, metadata body admission, scalar IL,
typed metadata-call and relative-syntax-path tests pass, zero failures/skips/warnings/
errors. These also cover M083's final immutable expected-vector helper and default
guard. The cache owner, ordinal comparers and positive/negative cache identity remain
unchanged; no synchronization or external mutation contract was added.

IMM-M086: Main traced ten reported Worker H-P input-map candidates and selected eight
fixed/default-comparer initializers. Use immutable empty roots and SetItem chains,
retaining original indexer evaluation/overwrite behavior and nested runtime objects.
A reported OpaqueList input map is actually populated in a loop: defer to a scoped
builder batch. NightlyRefutation's case-insensitive option map needs provider adapter
review; defer separately. Corrected a mistyped agent ID against the authoritative TSV.
Validate ListStateOracle, MetadataClauseOrigin, MetadataSourceNativeProducerControls,
NativeSpecifiedCall and Pr275AssumptionIdRequiredInput tests. Preserve all mutable SUT
arrays, reflection arguments and runtime aliases embedded in these test scenarios.
Status: open before edits.

IMM-M087: Production semantic scan found 42 additional immutable map/set/list roots
reassigned during construction and private sequential state updates. Main first traced
12 sites in final pack parsing, closed-parameter sequence admission, metadata-resolution
caches/module capture/hash grouping, specification-pack memo and manifest feature/reason
construction. Convert these five owners to scoped/read-only immutable builders and freeze
at existing publication boundaries, preserving reference/symbol/ordinal comparers, cached
nulls, Add/SetItem behavior, duplicate guards, reason order and final sort order.
The earlier proposed blanket exclusions for memo/recursive state were rejected: no
escaped root, rollback snapshot or concurrent caller was demonstrated. Remaining 30
sites require their own source/ownership review. Validate collector, metadata/spec pack,
claim-manifest and final-compilation tests plus the relevant broader gate.
Status: open before edits.

IMM-M086 verified: all 1,072 targeted Worker map/oracle tests pass with zero
failures/skips/warnings/errors. Main reviewed all eight exact initializer nodes before
applying changes; interpreter/substitution keys, values and nested SUT objects retain
their original identity and evaluation order.

IMM-M083 verified: all 5,484 selected ordinary Worker tests excluding LongFuzz pass,
zero failures/skips and zero build warnings/errors. The final expected-vector helper
change is additionally covered by M084's 117 passing focused tests. Main checked all
109 selected excerpts and coupled syntax-path, compiled-output and TestProject input
changes. Three proof/core roots are reused directly, native emitted-image bytes remain
unchanged, and JSON evidence still publishes arrays/nulls in the same ordered shapes.
The 143-test count difference from M080's full Worker run reflects the explicit
LongFuzz filter, not a claim that the broader universe was retested here.

IMM-M088: Main traced the remaining 30 compiler root rebindings in three files:
initialization closure/recursive admission state, Portable IR decoder type/term cycle
state and ordered program construction, and callable-codec uniqueness/count/dataflow
state. No persistent root escapes or rollback snapshot is observed. Use owned readonly
builders for private caches, cycle-state vectors and uniqueness sets; preserve exact
1-to-2 transition order, AddUnique, active-set finally removal, symbol comparers and
count/dataflow join expressions. Build block/instruction/parameter/clause vectors in
builders and freeze where existing helpers/results receive immutable collections.
Keep actual FIFO/LIFO worklists as immutable queues/stacks. Validate malformed/cyclic
portable graphs, callable metadata admission, exception initialization and existing
compiler round-trip/cache tests before committing this batch.
Status: open before edits.

IMM-M087 verified: 126 targeted Worker and 74 targeted Analyzer tests pass, zero
failures/skips/warnings/errors. The Analyzer gate ran after all earlier project jobs
finished, following automatic approval review's concurrency restriction. Main traced
all 12 rebindings in five owners, comparers, negative cache entries and freeze points.

IMM-M089: Main traced nine PowerShell argument += sites in four files: profile wrapper,
doctor evaluation, loop Git inventory, and declaration classifier compilation. Build
ordered argv with immutable string builders and freeze only at native/process/helper
array boundaries. Preserve sorted environment/reference order, duplicates, explicit
null/empty input behavior, optional filter/Fast/framework branches and environment
restoration. The loop's decoded path list also becomes a builder with frozen enumerable
publication; native NUL-delimited Git bytes remain unchanged. Replace declaration
compiler argv array concatenation with builder assembly. Validate old/new argv, Git
inventory outputs, declaration classification and doctor evaluation in canonical Linux.
Status: open before edits.

IMM-M088 verified: all 492 targeted Worker portable-graph, callable-codec, initialization,
exception-constraint, metadata-admission and specification-pack tests pass, zero failures/
skips/warnings/errors. Main checked decoder publication/cycle order, canonical-role
freeze, join expressions and recursive finally bookkeeping; read-only review found no
concrete defect. Inventory now records 2,776 C# syntax candidates across 1,870 files;
52 added owned-builder rows classified. Literal check preserves 3,035 raw/interpolated
tokens across 699 changed C# files against current master. Other Open rows remain in scope.

IMM-M090: Worker A-G read-only audit covers 215 ToArray inventory rows. Main selected
142 projection/assertion vectors across 47 files for exact-node review. Preserve native
stream/source/destination buffers, PE/reflection/params argv, mutable aliases and separate
map materializers. Review coupled parameter-row reversal, Array.IndexOf replacement,
compiler-input overload selection and already immutable source properties before writes.
Use the explicit OutputKind/IEnumerable TestCompilation overload where the former params
path preserves exact assembly names. Reverse immutable parameter rows through LINQ and
freeze the result; preserve metadata row order and limits. Run ordinary Worker checks
and retain the separate remaining candidates; this selection is not a whole-scope close.
Status: open before edits.

IMM-M089 verified: 25 baseline/current controls pass for ordered argv, environment
restoration, empty/null/null-element inputs, optional test/framework switches, zero/one/
many Git paths (including whitespace, newline, Unicode and ignored packages), doctor
reports and zero/one/many declaration classifications. Documentation checks 713 local
links; formatting checks 87 PowerShell/19 shell files with zero remaining changes.
All 84 tracked PowerShell scripts parse, zero errors, 1,094 syntax candidates.
The first comparison caught PowerShell's string-array cast normalizing null to empty;
Add(null) had the same coercion. The final native adapter allocates a string buffer and
copies the immutable snapshot directly, retaining a trailing null for explicit null
AdditionalArguments. This preserves the old API behavior and required native shape.
Other collection captures in these scripts remain separately open.

IMM-M091: Fuzz/Gates test materialization universe has 60 ToArray rows: select 51
read-only projections, reuse three already immutable roots and retain six actual
runtime/reflection/parser-byte adapters. Main traced boolean parameter controls and
Total source observations: the outer false-input vector and result aggregation arrays
are not mutable SUTs. Freeze boolean inputs and build observation values/classifications
with scoped builders, preserving reflection argument arrays and result order. Narrow
private names/boolean helper inputs to concrete immutable vectors; keep native case
Arguments untouched. Nested corpus groups become immutable vectors, with initialized
vector guards replacing now-inapplicable null checks. Validate complete Fuzz tests and
relevant Gates tests plus required corpus controls; keep embedded source bytes intact.
Status: open before edits.

IMM-M092: ContractApiIdentityResolver's unreadable-payload reason is assigned only
while its private constructor checks trusted contract metadata. Make the property
get-only; return the payload policy's reason through an out parameter and assign it
once after the existing short-circuited shape check. Initialize the local to null so
absent/source/untrusted candidates still leave no unreadable reason. Preserve metadata
resolution, guard order, exact diagnostic text and cache publication. Atomic concurrent
ImmutableInterlocked caches remain immutable roots. Validate resolver/rebuild/malformed,
owned metadata lifetime and authentic implicit-reference policy tests.
Status: open before edits.

IMM-M093: Main traced Gates diagnostic projection, corpus transaction staging/recovery,
and allocation IL reachability. Use scoped immutable array/hash/dictionary builders for
private sequential accumulation; freeze destinations before staging and transaction
entries before marker publication. Keep a frozen completed entry vector for rollback;
partial staging cleanup freezes the builder without moving its storage. Preserve
ordinal duplicate checks, cancellation order, durable marker behavior, publication
indices, native file/IL buffers and LIFO traversal. Diagnostic text/order/limits remain
unchanged. Validate transaction failure/recovery and ordinary allocation oracle gates.
Status: open before edits.

IMM-M090 verified: all 5,484 ordinary Worker tests excluding LongFuzz pass, zero
failures/skips/warnings/errors. Main checked all 142 selected projections and coupled
compiler overload, metadata reversal, direct immutable roots and member-index lookup.
The first build identified one redundant immutable attribute copy; direct publication
fixed it before this passing complete run. Native mutable SUT buffers remain intact.

IMM-M094: RoslynOperationLowerer specialization and custom callbacks are configured
before lowering and never changed during use. Make the exposed callbacks getter-only
and backing custom delegate readonly; configure through a private constructor and an
internal factory. Preserve public construction, known-pure callbacks, concrete replay
compiler constants and identity/default callbacks. Replace all three configuring callers,
retaining callback observation builders. Keep RoslynTypeMapper's separate runtime
receiver specialization contract unchanged. Validate Frontend lowering/replay and Contracts.
Status: open before edits.

IMM-M095: Main traced the container command dispatcher's eleven argv += sites plus
solution build concatenation. Use separate scoped string builders and freeze at the
required-dotnet native argv boundary. Preserve restore/build/export order, Fast flags,
NoBuild behavior, package exclusion/filter branches, project parallelism and explicit
null/empty additional build arguments. Compare AST-extracted baseline/current functions
and default test dispatch with recorded native calls before accepting changes.
Status: open before edits.

IMM-M091 Fuzz verified: all 302 ordinary Fuzz tests pass, zero failures/skips/warnings/
errors. Main preserved actual reflection/parser/native buffers while freezing read-only
boolean vectors and ordered runtime observations. Gates half remains pending separately.

IMM-M096: Main checked nineteen fixed Worker A-G vectors across twelve owners against
current syntax nodes. Freeze expected values, paired responses, compiler syntax inputs,
metadata rows and process argv accepted as IEnumerable. Keep byte read buffers and
reflection invocation arrays mutable. Preserve repeated rows/values, explicit byte and
nullable enum element types and compiler constraint base types. Convert the two empty
conditional branches with their nonempty vectors so both branches stay immutable.
Validate the affected Worker classes; callback/maps remain a separate following batch.
Status: open before edits.

IMM-M091 Gates and IMM-M093 verified: all 473 ordinary Gates tests plus explicit corpus
catalog workflow coverage pass, zero failures/skips/warnings/errors. Main and read-only
review checked partial-entry cleanup, frozen rollback state, marker publication, ordinal
path preflight, IL target ordering and diagnostic projection. The first build required
explicit configured-factory callback guards (M094); those are now present.

IMM-M095 verified: 60 baseline/current argv, export/package sequencing and native
failure controls pass for project, main/other solution, default target and solution
build paths. Null/empty/additional argv cases preserve binding errors as well as values;
the native adapter copies the frozen builder snapshot without null normalization.
Formatting checks all 87 PowerShell and 19 shell scripts, zero changes remaining.
Main verified native shapes and read-only review found no concrete defect.

IMM-M097: Main traced seven further Gates construction accumulators: catalog files,
source IDs, source locations and declaration hashes; importer declaration deduplication
and round-robin candidate selection; static-write replay parameter inputs. Use scoped
immutable builders. Freeze catalog files before declaration indexing/publication and
source IDs at return; keep ordinal duplicate diagnostics and first-seen selection order.
Preserve candidate count validation/insufficient/diversity guard order and freeze only
after validation. Replay inputs retain indexer overwrite semantics when entry/current
IDs coincide, then freeze before execution. Validate catalog/import and real native
static-write witness workflows. Resource-owning allocation caches remain separate.
Status: open before edits.

IMM-M092 and IMM-M094 verified: all 2,054 Frontend and 640 Contracts tests pass, zero
failures/skips/warnings/errors. Main traced constructor short-circuit payload reasons,
known-pure/default/replay callback paths and all configuring callers; independent
read-only review found no concrete defect. Explicit configured-factory guards satisfy
the repository defensive analyzer. Public lowering APIs and runtime mapper state remain.

IMM-M098: ContractExpressionBinder owns six sequential accumulation roots: bound and
receiver membership, pre-state mapping/value membership and two first-seen ordered
vectors. Main traced recursive old() binding, source-result tracking and publication.
Use readonly immutable builders; freeze receiver/pre-state snapshots in getters so
previously published views remain stable across later Bind calls. Keep map lookup then
Add, exact variable naming, callback order and partial-failure accumulation unchanged.
Validate the full Contracts suite and generic/flow-capture materialization controls.
Status: open before edits.

IMM-M096 verified: all 145 affected Worker tests across eleven files pass, zero failures/
skips/warnings/errors. Nineteen fixed vectors preserve values, duplicate rows, expected
byte/nullable enum types and process/compiler input order. The plan's twelve-owner
count was corrected to eleven files after exact-node selection. Mutable SUT buffers
and reflection arrays remain unchanged.

IMM-M099: TypeSpecializer builds substitution and signature maps only in its constructor.
Use local symbol-comparer builders threaded through private static construction helpers,
then assign readonly immutable fields once. The partial-counterpart TypesMatch callback
must read the live construction substitutions builder synchronously; Specialize reads
only frozen fields after construction. Preserve substitution indexer overwrite semantics,
signature first-seen behavior, recursive type shape and nullable annotations. Validate
constructed/partial generic contracts, flow capture and all Contracts tests.
Status: open before edits.

IMM-M100: Main traced nineteen Worker test construction/worklist scopes across nine
files: synchronous advisory/replay observations, serial verifier callback maps, allocation
oracle observations outside measured intervals, compiler argv, flag-combination snapshot
maps, identity membership and DFS traversal. Use scoped immutable builders and freeze
after producers complete, before assertions/JSON publication. Keep enum-map indexer
semantics and pre-mutation snapshots, callback/order/duplicate observations, LIFO child
order, native reflected argv and all actual mutable SUT buffers. Verifier callbacks are
awaited serially and replay observers run inline; no new synchronization is required.
Validate affected classes, preserving raw source literals and measurement boundaries.
Status: open before edits.

IMM-M097 verified: all 473 ordinary Gates/corpus-catalog workflow tests pass, zero
failures/skips/warnings/errors. Main and independent review checked ordinal duplicate
diagnostics, frozen parsed-file publication, round-robin selection guards, and static
write replay overwrites. The first build caught a redundant Contains/Add guard in the
concurrent M098 binder edit; using Add's boolean result preserved first-seen order.

IMM-M101: Package integration materialization universe contains 47 ToArray nodes:
select 39 read-only projections across eight files, retaining eight native mutable
publication/ITaskItem/reflection/hash byte adapters (two start from immutable bytes).
Main traced ZIP/JSON lifetime, argument pre-mutation expectations and probe helper
couplings. Narrow private probe constructor and canonical/path/item/project helper
results to immutable vectors; preserve initialized-input guards and direct publication.
Freeze adjacent fixed SARIF/project vectors and two assertion-only XML lists. Archive
expected-entry inputs are immutable vectors with a default guard. Preserve ordering,
wire text, eager materialization before disposal and mutable Prepare(string[]) subjects.
Validate package integration/real MSBuild workflow and compare source literal tokens.
Status: open before edits.

IMM-M098 and IMM-M099 verified: all 640 Contracts tests pass, zero failures/skips/
warnings/errors. Main and independent review checked recursive old() binding, first-seen
ordering, stable published snapshots, constructor-only symbol maps, nullable comparers
and partial generic callback timing. The new callback now has its explicit null guard,
as required by the repository analyzer; construction builders do not escape.

IMM-M100 verified: all 118 affected Worker tests pass, zero failures/skips/warnings/
errors. Main verified callback serial execution, producer completion/freeze boundaries,
pre-mutation map snapshots, measured allocation intervals, native compiler argv and
LIFO traversal. The flag maps retain indexer semantics and observation arrays preserve
order and duplicates. No mutable subject buffers were changed.

IMM-M102: Main traced three private Frontend owners: continuation planner maps/sets,
nested operation-purity depth caches and operation binding/capture/per-lowering caches.
Use readonly scoped builders for retained private accumulation and a fresh builder for
each lowering scope; save/restore the prior scope in the existing finally. Keep symbol/
reference comparers, strict AddUnique, overwrite/duplicate-owner semantics, capture
names/order and immutable published bindings. Planner owner indexing must publish its
private builder before scanning so budget/cancellation failures retain the old partial
index state. Queue traversal stays immutable FIFO. Nested purity builders retain all
recursive depth entries. Validate full Frontend and compiler continuation/purity/capture
boundary tests; keep callbacks/region work accounting and explicit failure paths intact.
Status: open before edits.
