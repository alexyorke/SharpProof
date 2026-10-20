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

Inventory checkpoint after IMM-M090-M100: 1,870 C# files yield 2,553 syntax candidates;
44 newly tested C# builder rows are classified, 1,715 rows remain Open. All 84 tracked
PowerShell scripts parse with zero errors and 1,093 candidates; 18 M095 argv-builder
rows are classified. Pending M101/M102 implementation and semantic gaps remain open
in this audit; syntax candidate removal alone does not close their validation.

IMM-M103: Main traced the semantic runner's fifteen remaining += sites: build argv,
checksum exclusion text, ordered worker descriptors, retained-results assembly/discovery
argv and per-task direct/coverage argv. Use scoped immutable builders and freeze before
native/process/task publication. Preserve descriptor order/process slots, duplicate
assemblies, discovery dump order, cohort filtering, settings property presence, isolated
output placement and coverage suffix order. Replace string append with Concat. Freeze
the coverage helper's native result immediately; leave scheduler/task lifecycle mutation
and process environment adapters separate. Compare extracted baseline/current build,
filter/descriptor and prepare blocks without launching builds/tests, then real semantic
workflow after existing project jobs finish. No concurrent project test runs.
Status: open before edits.

IMM-M104: Main checked seven TotalCallableVerifierTests callback dictionaries. Use fresh
ordinal immutable map builders within the existing per-run/test scopes, retain indexer
replacement semantics and freeze after awaited verification or Assert.ThrowsAsync returns.
Cancellation snapshots must include all evidence published before cancellation, with
callback assignment preceding cancellation. Entry callback/backend ownership and resource
counters are mutable subjects and remain unchanged. Validate repeated verification,
shared budget, entry publication, legacy lowering, refutation and cancellation cases.
Status: open before edits.

IMM-M105: Main traced eight remaining bounded Worker Q-Z construction sites across
seven files plus three adjacent read-only vectors/sets. Freeze Windows path observations,
API substitution input, checked-goal IDs, resource reads, backend creation order and
precision evidence. Launcher construction uses a builder and retains the native array
at RunMain. Saved environment data uses an ordinal builder plus first-seen key order,
then freezes in finally before restoration; retain strict Add/nonoverwriting TryAdd and
restore ordering. Keep actual environment/console mutations, backend resource counters,
parser mutations and measurement timing as mutable test subjects. Backend factories are
created serially and renewal is under the existing lane lock. Validate real launcher,
resource, backend disposal and native precision controls through Worker/semantic tests.
Status: open before edits.

IMM-M101 verified: full ordinary package integration suite passes 384 tests, zero failures,
one expected unsupported-host platform skip, zero build warnings/errors. Main reviewed
all 39 nodes and helper couplings, ZIP/JSON lifetime, pre-Prepare snapshots, native
adapters and XML IndexOf assertions. First compilation identified a redundant claim-path
copy after helper narrowing; direct immutable publication fixed it before this full run.

IMM-M106: TotalSourceCallSession owns four private synchronous sets/maps for source/IL
active membership, iterator classification and type-initialization eligibility. Main
traced every use: none publishes a collection root; nested lowerers share the session
synchronously and leave active membership through existing cleanup. Use readonly
symbol/ordinal builders, retaining budget/cancellation/count checks before membership
changes. Enter returns Add's boolean after existing guards; Leave remains idempotent.
Cache indexers preserve SetItem overwrite semantics and partial classification timing.
Session configuration properties and mutable budget/result flags remain separate scope.
Validate source-session reentry, source/IL recursion, budgets and initialization coverage.
Status: open before edits.

IMM-M107: Main reviewed all declaration classifier collection construction. Use array
builders for ordered results, safe/unsafe spans and per-line tokens, and an integer map
builder while accumulating. Freeze spans and each nested token value before filtering;
retain the valid-syntax short circuit, sorted unique output line numbers, duplicate
request paths and request order. Publish immutable result/line arrays to JSON. Compare
baseline/current classifier outputs through the canonical PS helper, including invalid
syntax, primary constructors, executable initializers and duplicate ordered requests.
Status: open before edits.

IMM-M108: Main verified every session-policy assignment. OpaqueCalls, OpaqueEffects,
ApproximateElementReads and PinElementReads are configured only before lowering or
in two test constructors. Append optional constructor/factory arguments and expose
get-only properties. Preserve null callback fallback and deferred callback invocation,
factory parameter guard timing and fresh per-pass session policy. Update the production
factory call and test constructors; validate full Frontend plus Worker source-call paths.
TotalLoweringContext phase/model state requires separate analysis, not blind init setters.
Status: open before edits.

IMM-M109: Package runner argv construction has eight collection appends and three
scalar timing additions. Use scoped string builders for both prerequisite builds and
per-shard direct/project arguments, immutable build descriptors after construction,
and an immediate immutable snapshot of coverage-helper native output. Preserve build
order, fast flag, isolated OutDir position, exclusive/direct selection, coverage suffix,
DOTNET_HOST_PATH and native adapters. Keep timing/scheduler/environment mutations.
Compare extracted baseline/current blocks for direct, exclusive, coverage, empty/null
and Debug/Release/Fast combinations before the real package workflow.
Status: open before edits.

IMM-M110: Main traced nine TotalLoweringContext private collection roots across every
partial file. Convert local/capture maps, concatenation operands, this/field/storage
captures, specification exclusions, exact continuation cache and assumptions to readonly
builders. No root escapes; variables/captures/entry assumptions already publish immutable
snapshots. Retain symbol/default comparers, strict AddUnique, overwrite/idempotent set
semantics and local order. Preserve add/remove before cache clear, cache clear before
graph/compilation reassignment, and assumption clear before entry-assumption clear.
Validate full Frontend and specification reuse/capture/concatenation ownership controls.
Status: open before edits.

IMM-M111: SourceCallFrame.Entry is assigned once at the CFG entry creation point.
Make it a validated get-only constructor input and pass a frame factory through
LowerSharedFrame into LowerCore. Invoke it only after the existing entry block creation,
after cancellation/preflight/model capture and construction limits, retaining _calls.Enter
and finally Leave, callback bodies, caller budget/continued block order and top-level
SetEntry behavior. No frame-dependent code runs before that point. Validate composed
source exception/filter, session reentry, construction-budget and cancellation paths.
Status: open before edits.

IMM-M107 verified: five baseline/current canonical classifier probes pass ordered and
duplicate requests, invalid syntax, primary-constructor executable boundaries and exact
JSON publication. Independent six-site review is clean; whitespace requires no changes.
Raw/interpolated literal checkpoint verifies 3,219 text tokens in 736 changed C# files.

IMM-M102-M105 live Release gate is not yet complete. Worker compiler/core/claim/other
shards pass 48/240/88/4,322 tests; heavy shard passes 928 and fails one golden
vc-shadow-core assertion after MethodTimeout (expected proof). Isolated reproduction
is pending; this is not a clean full-gate result and the cause is not established.

IMM-M112: Main traced four private exact-list/default-dictionary ownership sets and
all uses, including FreshCollections local aliases and the IsKnownList helper. Use
readonly symbol/CaptureId builders and mutate the chosen builder at each former
publication point, preserving partial traversal state and parameter specialization.
Narrow IsKnownList to private builder parameters; no collection escapes. Keep escape
queues/LIFO traversals, order sidecars, domain exclusions and work counters unchanged.
Validate list/dictionary domain, source capture and receiver ownership through Frontend.
Status: open before edits.

Checkpoint before master #331 reconciliation: M102-M105 Release gate completed 23
framework summaries and six tasks: 11,773 passed, one vc-shadow-core MethodTimeout
failure, zero skips; builds zero warnings/errors. All 2,054 Frontend tests passed.
M103 additionally passes 43 baseline/current controls and script format. M109 passes
32 exact baseline/current argv/error/native probes; formatted script review is clean.
M106/M108/M110/M111 follow-up build found CA1868 redundant set membership guards
and SPINT001 lambda entry guard; main corrected those before rerun. M112 is reviewed
construction work pending full Frontend validation. Current Worker follow-up is still
running on its isolated pre-merge snapshot. These pending batches are checkpointed for
safe conflict resolution, not declared fully validated. Master advanced to 16063804e
(#331 dictionary source-helper state); merge review takes priority before more changes.

IMM-M113: Resolve master #331 conflicts by retaining constructor-origin dictionaries,
identityOnly escape rules, owner-independent dictionary field identity, receiver/argument
Pin calls and clause modeling. Keep scoped immutable construction from this branch:
new origin sets become symbol/default builders, expression dictionary cache becomes a
readonly builder with the new three-part local key and four-part interned identity,
new DefaultDictionaryState accepts an immutable argument snapshot with initialized
guard. Preserve source receiver specialization and immutable declaration/callee vectors.
Review automatic Effects/Gates merges and run new dictionary helper/contract/old-heap
controls plus full Frontend after the pre-merge Worker job ends.
Status: merge reconciliation in progress.

IMM-M106/M108/M110-M113 checkpoint validation: follow-up Worker run passes 131 tests,
including golden fixtures, session reentry, IL/stack boundaries and declaration accounting;
zero failures/skips/warnings/errors. The prior vc-shadow-core timeout did not reproduce;
its cause remains unproven. Resolved #331 merge passes all 2,064 Frontend tests with zero
failures/skips/warnings/errors. Independent four-file merge review confirms constructor
origins, identityOnly escape rules, field interning identity, Pin order and immutable
publication. Script format checks 87 PS/19 shell with zero changes; M109's 32 probes
pass after formatting. New dictionary Worker workflow controls are still running.
Inventory now covers 1,878 C# files/2,600 candidates (1,791 Open) and 84 PS scripts/
1,090 candidates (809 Open), zero PS parse failures. Newly checked builder rows still
await explicit classification. New master test collections remain in the open inventory.

IMM-M114: Main checked all eight private receiver/comparer roots and partial-file
callers. Use readonly symbol/default builders for entry/nullable/default comparers,
array receiver/capture representations and receiver-element substitutions. Retain both
first-seen order sidecars, SetItem overwrite semantics, nullable recursion guards and
symbol comparers. Add's boolean gates the entry-comparer order append. Preserve map
updates before TypeSpecializer/Result/Parameters rebuilding; no collection root escapes.
Validate full Frontend generic/array/comparer/capture controls after the dictionary gate.
Status: open before edits.

IMM-M115: Main verified 18 Fuzz test fixed-vector/set/singleton-map candidates across
12 files. Freeze ordered outer vectors for semantic cases, campaign/replay sources,
JSON mutation delegates and structural parents. Preserve nested runtime/reflection
arguments and mutation callbacks. CompileActual consumes IReadOnlyList and Count.
Freeze known-failure/child-source sets with ordinal comparer and three singleton
interpreter maps. Eight strict ToDictionary candidates remain separate pending explicit
construction; no blanket ToImmutableDictionary replacement. Validate all ordinary Fuzz.
Status: open before edits.

IMM-M116: Main traced the final 17 Frontend private map rebindings after #331.
Use readonly builders for list fields, closed-sealed type cache, CFG blocks, preserved
source calls, call preconditions, region catch/kind/entry/token/finally/filter maps and
explicit throw types. Preserve strict AddUnique and overwrite indexers; token ordinal
reads Count before insertion. Result freezes both published call maps. Filter resolver
keeps its immutable input contract and receives _blocks.ToImmutable after CFG block
registration. Keep traversal queues/stacks, phase scalars and frame lifecycle unchanged.
Validate full Frontend and source exception/filter, opaque/list and metadata call paths.
Status: open before edits.

IMM-M117: Main reviewed 30 Contracts and eight Fuzz fixed/projection/helper rows.
Freeze fixed/nested expected vectors and operation snapshots; make EntryValues/Values
return immutable maps and remove redundant recapture. Use the existing internal
ImmutableDictionaryCapture.Create (builder + explicit duplicate guard) for all twelve
strict ToDictionary projections, retaining selector order/comparers; grant only the two
test assemblies the same existing Ir friend access used by other test projects. Preserve
substitution indexer overwrite semantics with scoped builders. No new public helper API.
Retain native reflection/byte buffers and mutable test subjects. Run Contracts/Fuzz
ordinary suites plus layout after project metadata changes and literal text verification.
Status: open before edits.

IMM-M113 post-merge Worker controls verified: 1,167 passed, zero failures/skips and zero
build warnings/errors, including dictionary source helpers, old-heap state, provenance,
NativeSpecifiedCall and golden fixtures. Automatic Effects/Gates/expression-dispatch
merge review is clean. GitHub reports MR #323 MERGEABLE at pushed merge 66ed463ae.

IMM-M109 verified through canonical Release package workflow: 383 passed, zero failures,
one expected VerificationFailsExplicitlyOnUnsupportedHosts skip across 39 isolated
shards. Build zero warnings/errors; all 32 baseline/current controls and script format
already passed. This is distinct from M101's direct ordinary package suite count.

IMM-M114/M116 verified: all 2,064 Frontend tests pass after receiver and final private
map builder conversions, zero failures/skips/warnings/errors. Independent review confirms
no same-builder enumeration mutation across parent/child imports, call-map snapshots,
filter-resolver boundary and stable token ordinals. Remaining direct Frontend roots are
intentional immutable FIFO/LIFO traversal and atomic shared caches. M115/M117 Fuzz and
Contracts implementation remains under validation, including friend-access metadata.

IMM-M118: Main traced fifteen Worker Q-Z strict map projections and three membership
sets, plus six serial shadow-source collector scopes. Reuse strict builder capture with
existing comparers and eager selector/duplicate timing; preserve mutable IR graph builder
and native input arrays. Presentation labels are read-only input snapshots: derive the
incomplete map after asserting the complete map, leaving native proof unchanged. Freeze
shadow callback maps after awaited producers, outer result/check arrays after source loops
and ordered proof-loss strings after claim loops. Retain numeric outputs and lifecycle
state. Validate scalar, source/IL exception, graph, label/core and source-universe controls.
Status: open before edits.

IMM-M119: Main reviewed nine A-G/new-master input and traversal rows. Freeze producer
reason allow-list, ascending credit map and its nested vectors, dictionary interpreter
input maps, and DictionaryFields lookup. Preserve IrObjectState/CLR dictionary mutable
subjects, default/ordinal comparers, strict argument Add rejection and indexer overwrite.
Credits use sorted immutable keys matching original ascending insertion/assertion order.
DictionaryFields retains first-wins discovery and DFS order via the existing immutable
IrTraversal.PushChildren overload; keep params roots as an input adapter. Other A-G
callback/evidence collectors remain open; options/evidence JSON needs explicit property
order preservation before conversion. Validate Worker dictionary/coverage/producer tests.
Status: open before edits.

IMM-M115/M117 verified: all 302 ordinary Fuzz tests and all 640 Contracts tests pass,
zero failures/skips/build warnings/errors. First Contracts compilation raised CA1062
at existing string Split uses despite upfront guards; explicit coalescing guards at the
same use sites preserve the already-enforced invariant and the full rerun passes.
Independent reviews confirm strict duplicate/selector/comparer semantics, nested vectors,
substitution overwrite and runtime/native subject preservation. Shared internal capture
helper is reused; no public helper was added. Friend metadata layout validation remains
pending. Latest literal checkpoint: 3,231 raw/interpolated text tokens across 750 changed
C# files match master 16063804e. M118/M119 Worker batches remain under validation.

IMM-M120: Review DotnetApiInventory.Compare's private Index construction. Replace
immutable root reassignment with an ordinal immutable dictionary builder, keeping the
explicit duplicate guard/message and freezing at return. Validate existing inventory
and duplicate-identity controls. Status: open before edits.

IMM-M121: Freeze recursive Fuzz test input families using ordered immutable array
builders and immutable helper return values. Preserve candidate/source order, native
runtime Values subjects, batch outcomes and the missing-index sentinel. Freeze before
CompareBatch and assertions; update array length assertions and census counts only.
Status: open before edits.
IMM-M122: Trace six Worker test collector owners through synchronous diagnostic
publication and serial awaited verification/reflection helpers. Replace local lists
with array builders and callback maps with ordinal dictionary builders; freeze before
assertions/output, preserving callback indexer overwrite, append order and native inputs.
Scope: CompilerOrdinaryControlAndAbstractionCoverage, CompilerTotalCallRetirement,
FloatingEqualityEvaluationOrderAudit and three Generic tested-type audit files.
Status: open before edits. M118/M119 first build identified a private helper concrete
immutable-map parameter requirement (CA1859); narrow that already-immutable boundary.
IMM-M123: Review API usage-ranking construction: fixed expectation vectors, strict
inventory projections, overwrite-initialized inventory maps, ordered site/gap collectors,
ordinal sorted input hashes and known-intrinsic membership. Use scoped builders/frozen
publication, preserve strict duplicate checks and sorted JSON property order. Reuse the
existing internal strict map capture via test friend access. Validate full ranking tests
and output shape/count/order. Status: open before edits.

IMM-M124: Review shared test infrastructure and symbol package validator captures.
Freeze seven read-only validator projections, ordered API evidence rows, guard type/name
vectors and visited membership, and case-insensitive metadata-name membership. Use an
immutable FIFO for guard dependency discovery; native reflection/byte buffers remain.
Validate shared guards, metadata callers, evidence and symbol/package controls.
Status: open before edits. Production host live forwarded-argument alias remains open.
IMM-M125: Review production MSBuild host outer records/forwarding collectors, runtime
assembly projection and fixed name allow-list. Use ordered array builders and freeze at
final serialization; preserve each forwarded SDK argument object's live alias across
Execute and all native reflection argument adapters. Pinned runtime host diagnostic
metadata remains observable; no earlier forwarded snapshot is justified. Validate actual
pinned production inventory queries and diagnostic JSON order/shape. Status: open before edits.
IMM-M126: Review 27 additional Contracts test candidates: ten synchronous pre-state
callback trace collectors plus seventeen fixed case/diagnostic/clause/boolean/BigInteger
vectors and read-only snapshots. Use ordered array builders, freeze callback traces after
each producer including partial-failure traces, and immutable helper parameter contracts.
Preserve generated pre-state names, native reflection arguments, source text and runtime
mutable subjects. Validate all Contracts tests after the full batch. Status: open before edits.
IMM-M127: Review the remaining 19 safe Contracts callback/vector/snapshot rows and
six metadata-image publications. Freeze synchronous operation-site traces after BindValue
and immutable outer expected vectors without changing native runtime subjects. The six
image owners are locally created exposable MemoryStreams; copy their [0, Length) bytes
once into ImmutableArray at Roslyn CreateFromImage publication, preserving stream position
and avoiding a writable published image. Retain seven traced native reflection/Assembly
Load/Construct adapters and one actual mutable int[] SUT identity. Validate full Contracts.
Status: open before edits.
IMM-M125 verified on pinned Linux amd64 SDK 10.0.402: two actual baseline/current
production MSBuild queries pass and their complete diagnostic JSON responses are
byte-identical, including forwarded arguments and runtime metadata. The M124 symbol
validator compiles through its actual Add-Type boundary. Its behavioral regression
controls and the remaining shared/M120-M124/M126-M127 project gates are still pending.
M118/M119/M122 Release Worker build passes zero warnings/errors; full ordinary tests run.
IMM-M128: Review twelve remaining PowerShell compound construction rows across seven
scripts. Use scoped immutable argv, service-name, framework-package and XML-reference
builders with ordered frozen publication and final native string[] adapters. Preserve
case-sensitive service membership, Windows-only pack order, XML node identity, duplicate
entries, ++index option parsing and missing-value errors. Freeze the sample package
allow-list as ordinal membership. Compute documentation heading stages and final anchor
without scalar root reassignment. Validate baseline/current controls for all branches
and canonical script formatting before committing implementation. Status: open before edits.
IMM-M118/M119/M122 verified: all 5,696 ordinary Release Worker tests pass, zero
failures/skips/build warnings/errors, with exact embedded PDB export for 88 outputs.
The initial build's CA1859 private parameter requirement was corrected before this run.
Independent review confirms strict projections, callback publication, comparers, nested
mutable subjects, source-universe order and credit ordering. M119 plan correction:
DictionaryFields had one private params caller; main narrowed both call and helper to
ImmutableArray roots, preserving IsDefault guarding and DFS order. The earlier adapter
assumption was not retained. M128 passes 27 baseline/current controls and all PS parsing;
canonical format checked 87 PowerShell and 19 shell scripts, two files normalized.
IMM-M129: Trace context AllowObjectWidening across both LowerOnce passes, nested source
frames and contract materialization. Its value is exactly the existing immutable source
session MetadataRequiresEnabled option. Move it to expression-lowerer init configuration
populated in ordinary and region initializers; remove duplicate mutable context setter
and frame copy. Preserve null-session false and opaque-call widening behavior. Validate
metadata nominal/IL/stack and recursive-requires controls via the Release semantic gate.
Status: open before edits.
IMM-M128 verified: 30 baseline/current controls pass after adding missing positional
and null-argument probes. Main preserved the original object-array staging contract in
PlugAndPlay with an object builder; string conversion remains at ArgumentList.Add,
avoiding earlier null normalization. All seven scripts parse clean, independent review
confirms order/comparer/XML identity/Windows variants and duplicate heading anchors.
Canonical formatting checks 87 PowerShell and 19 shell files. Existing scalar counters
and native/SUT mutations remain separate reviewed state; other map construction is open.
IMM-M130: Working-tree inventory refresh covers 1,878 non-vendored C# files and 84
PowerShell scripts with zero parse errors: 2,486 C# candidates and 1,090 PS candidates.
Main reconciled 111 already-tested C# builder rows, eight traced native/SUT Contracts
adapters and 83 PS scalar/builder rows. Pending C# source owners remain open. Current
Open counts: 1,571 C# / 728 PS. Numeric member/indexed aggregate owners remain open;
scalar arithmetic classification does not exempt their mutable containers. M128 final
canonical gates pass zero format changes, 713 documentation links and container contract.
M124 embedded-symbol regressions pass exact pairing/checksum/malformed/missing controls.

IMM-M131: Review 41 additional Worker fixed-vector/map/membership/private-input rows
across A-G and Q-Z. Thirty-nine syntax sites use immutable snapshots/strict projections;
two coupled changes use ordered callable builders and an immutable ScriptedBackend
constructor input. Preserve target-first/last ordering, ordinal set membership, strict
map duplicates, native reflection/byte inputs and mutable backend queue/counters. Narrow
the private nullable membership helper and remove redundant immutable recapture. Validate
all affected fixtures after implementation. Status: open before edits.
IMM-M120/M121/M123/M124/M126/M127/M129 verified: full ordinary Release semantic gate
passes 11,854 tests in 23 framework summaries and six isolated scheduler tasks, zero
failures/skips/build warnings/errors, exact embedded PDB export for 225 outputs.
The initial build caught CS0841 in the new M121 null guard; declaration and ThrowIfNull
are now separate, and this complete rerun passes. Contracts preserves all runtime/native
subjects and six immutable image publications; M129 uses session-derived init options
for ordinary/region/nested lowering instead of a mutable context flag. This new clean
full gate supersedes the earlier failed timeout gate as current validation; the earlier
timeout's cause remains unproven. M131 next Worker vectors remain open and are excluded
from this gate and new builder classification. Literal checkpoint: 3,318 tokens across
768 changed C# files match master 16063804e; M131 literals require a later checkpoint.
IMM-M132: Review eighteen Package fixed vectors (PNG signature, frameworks, paths,
filenames and stable member order), the shared compilation-server ID cache and nested
expected package dependency data. Use immutable vectors, an ordinal atomic immutable
cache with the same GetOrAdd factory semantics, and immutable nested dependency vectors
with original indexer overwrite/comparer behavior. Retain native PNG read scratch bytes
and mutable package/compiler subjects. Validate canonical Release package workflow.
Status: open before edits.
IMM-M133: Review twenty-nine Analyzer test ToArray snapshots across fourteen files.
Consumers use Length/indexing/enumeration/LINQ/Task.WhenAll/NUnit and do not mutate or
retain native-array contracts. Freeze with ToImmutableArray while preserving eager
order, symbol/reference identity and diagnostics. Other Analyzer vectors, collectors,
private return shapes and actual mutable option-provider subjects remain open. Validate
all ordinary Analyzer tests. Status: open before edits.
IMM-M131 verified: all 711 affected ordinary Release Worker tests pass, zero
failures/skips/build warnings/errors; 88 exact embedded PDB outputs. Main corrected
an ImmutableArray.Create overload ambiguity by naming element constructors and added
the missing immutable-collections import before this clean rerun. Independent review
confirms target ordering, strict map/ordinal sets, private input coupling and preserved
mutable backend queue/counters. Removed the redundant parenthesized immutable recapture.
IMM-M133 verified: all 1,134 ordinary Release Analyzer tests pass, zero failures/skips/
build warnings/errors; 64 exact embedded PDB outputs. Review confirms Task.WhenAll
IEnumerable inputs, snapshot consumers, symbol identity and native/SUT preservation.
Canonical whitespace check of pending C# files requires zero changes. M132 Package
vectors/deep expected graph/atomic cache pass independent review; workflow remains pending.
IMM-M134: Review twenty-two PowerShell literal snapshot rows and coupled format argv/
developer-build argument scopes across seven scripts. Use immutable profile/source/path/
seed/report/plan/version snapshots and scoped argv builders with typed native adapters.
Preserve case-sensitive sorted path uniqueness, original PowerShell Unique comparers,
empty/singleton array shape, duplicate plan validation, parallel normalization completion
and error order, and nested native JSON/runtime subjects. Validate baseline/current
fragments and full script format/contract controls. Status: open before edits.
IMM-M132 verified: canonical Release package workflow passes 383 tests with one
expected VerificationFailsExplicitlyOnUnsupportedHosts skip across 39 isolated shards,
zero failures/build warnings/errors; 87 exact embedded PDB outputs. Independent review
confirms ordinal atomic cache GetOrAdd behavior, nested expected dependencies, fixed
vector order and preserved native PNG scratch/input subjects. M131/M133 targeted gates
and M132 workflow complete; M134 script implementation remains open.
M134 follow-up scope: freeze Doctor runtime stdout and Loop HEAD stdout after capture; preserve existing exit-status validation and HEAD error order. Native output alone is not a mutability exception.

IMM-M134 verified: 74 baseline/current controls pass for formatter native argv and
errors, tracked sources, HEAD validation, path merge/comparison, runtime output,
retained seeds, fuzz failures/wire arrays, Doctor unique versions, developer-plan
validation/build argv, and coverage identity/module order/parallel result cardinality.
Actual Doctor JSON/text/configuration/direct-central-transitive package regressions
pass. All 87 PowerShell and 19 shell files are format-clean; 713 local documentation
links and the canonical container contract pass. Independent review found no defect.
IMM-M135: Convert 31 reviewed Horn test snapshot rows in eleven Smt test files:
state/substitution dictionaries use scoped indexer builders and freeze once; clause
vectors and two private CounterSystem tuple results become immutable. Preserve
intentional wrong-arity input, clause order, default key equality and mutable native
solver ownership. Validate the affected Horn fixtures in the canonical container.
Status: planned before edits.

IMM-M135 verified: all 18 affected Horn tests pass in Release/net10.0, zero
failures/skips/build warnings/errors; 19 exact embedded PDB outputs. Canonical
whitespace requires zero changes. Main and independent review confirm all 31 rows,
private tuple coupling, indexer semantics, wrong arity and clause order preserved.
Inventory refresh: 1,878 C# units / 2,381 candidates / 1,394 Open, and 84 PowerShell
units / 1,097 candidates / 701 Open, zero parse failures. New pending source owners
were excluded from automatic verified-builder classification; candidates are not
all violations. Current verified implementation through M134 is pushed to draft #323.
IMM-M136: Convert twelve production Fuzz replay snapshot/map/requirements rows in
MetadataProgramSourceReplay and TotalProgramSourceReplay. Use strict duplicate
capture, preserve parameter order explicitly for requirements, freeze ordered clause
builders after alias relations are appended, and preserve inner reflection arrays
and mutable reference-identity alias state. Validate all ordinary Fuzz tests.
Status: planned before edits.

IMM-M137: Review Doctor's four remaining package snapshot assignments. Freeze outer
package vectors while preserving ordered inner wire records, existing no-target and
failed-evaluation empty results, assets error handling, direct/resolved preference,
JSON arrays and version order. Validate with canonical Doctor regressions plus
baseline/current empty/singleton/multiple package controls. Status: planned.

IMM-M136 verified: all 302 ordinary Release Fuzz tests pass, zero failures/skips/
build warnings/errors; 87 exact embedded PDB outputs. Canonical whitespace required
zero changes. Main and independent review confirm strict duplicate capture, ordered
parameter/alias observation, one-entry indexer semantics and preserved inner reflection
arrays/reference alias maps. The native byte-buffer boundary remains separate.
IMM-M137 verified: baseline/current direct JSON publication controls pass for empty,
singleton and plural package vectors, including missing versions and irrelevant package
filtering; actual Doctor regressions pass. Independent review confirms error timing,
assets fallback, ordering and direct/resolved precedence. Canonical formatting applied.
Literal checkpoint after M135/M136: all 3,471 raw/interpolated text tokens across
794 changed C# files match master 16063804e; interpolation expressions/ordinary strings
are outside this literal check and are reviewed separately.

IMM-M138: Replace internal fuzz seed List/HashSet accumulators with immutable array/
set builders; freeze schedule/manifest vectors at publication and native stdout at
capture. Preserve duplicate checks, case budgets, validation/callback/error ordering,
source cleanliness and evidence wire array shape. Keep the existing typed schedule
input contract while its consumers are reviewed. Validate baseline/current schedule,
manifest and Git-capture scenarios before commit. Status: planned before edits.
Latest canonical layout passed 2,620 source files, 59 projects, 151 unchanged pinned
common files and 59 evaluated projects.

IMM-M138 verified: 67 baseline/current controls pass for seed schedule budgets,
duplicates/order, exact Int32 manifest validation, callback mutation/timing, evidence
JSON arrays, acceptance single-seed lookup and clean source HEAD/status validation.
Main and independent review confirm Add return types and publication timing; canonical
script formatting applied. The typed RetainedSeeds input remains a coupled API review
item, rather than a blanket native exception.
IMM-M139: Replace TotalLoweringContext.FreshReceiver's mutable configuration bridge
with an init-only expression-lowerer option. Compute the existing predicate once per
LowerCore, pass through ordinary/region initialization, and retain default false for
pure contract clauses. Verify lowering/write-site behavior with Frontend and Contracts
ordinary suites. Status: planned before edits.

IMM-M140: Migrate 85 reviewed Analyzer-test vector/snapshot rows (56 fixed vectors,
29 materializations), excluding the 17 collector owners for a separate batch. Use
immutable arrays and strict ImmutableDictionaryCapture for all three ToDictionary
sites. Replace the pointer-array hierarchy's Array.Sort with an ordinally ordered
immutable snapshot. Preserve native byte images, reflection args, diagnostics/source
text and private mutable test subjects. Validate all ordinary Analyzer tests.
Status: planned before edits; coordinator corrected review's generic ToImmutableDictionary
suggestion to preserve strict duplicate behavior.

IMM-M140 scope correction: main found an additional conditional long[] samples vector in CoreIrScalarIntervalTransferTests omitted by the syntax inventory. Its consumers only enumerate/index the samples; convert the declaration to ImmutableArray<long> (86 total snapshot/vector sites).

IMM-M139 verified: all 2,064 ordinary Release Frontend and 640 Contracts tests pass,
zero failures/skips/build warnings/errors; 9 and 16 exact embedded PDB outputs.
Canonical formatting requires zero changes. Independent review confirms ordinary/
region/nested lowering propagation, pure-clause separation and no stale context use.

IMM-M141: Migrate 33 reviewed Worker A-G construction rows: fixed vectors, strict
lookup maps, scoped region/line-set builders and immutable DFS stacks. Preserve
NUnit/reflection adapters, intentional JSON mutation delegates, stream buffers,
wrong-case inputs and LIFO/source order. Validate affected fixtures after independent
review. Status: planned before edits. Analyzer collector follow-up remains separate.

IMM-M140 verified: all 1,134 ordinary Release Analyzer tests pass, zero failures/
skips/build warnings/errors; 64 exact embedded PDB outputs. Main and independent
review confirm all 86 vector/snapshot changes, three strict maps, ordinal hierarchy
sorting, graph reference identity and native/SUT preservation. Canonical whitespace
check required zero changes. Seventeen collector rows remain a separate follow-up.

IMM-M142: Replace seventeen Analyzer-test List collectors with scoped immutable
array builders and explicit snapshots after the final synchronous callback, await,
walk, source construction or cancellation assertion. Preserve intermediate count
assertions, callback ordering and source text. Mutable RangeCase.CallLine subjects
remain separate from their outer input vector, whose necessity is under review.
Validate all ordinary Analyzer tests after independent review. Status: planned.

IMM-M142 scope correction: main challenged the mutable-RangeCase exception for its outer cases vector. Independent tracing confirms no element replacement/reorder/array API; convert RES-51C303DB8BEB86FB to ToImmutableArray while retaining actual RangeCase.CallLine mutation (18 total sites). Final dispatch/nested-cancellation snapshots remain after all observations so late writes stay detectable.

IMM-M141 verified: all 169 affected ordinary Release Worker tests pass, zero
failures/skips/build warnings/errors; 88 exact embedded PDB outputs. Main and
independent review cover all 33 rows across eighteen files, both DFS stack orders,
strict maps, region IDs, declaration ordinals and native/mutable subjects. Canonical
formatting applied; long mutation/fixture vectors retain separate readable rows.

IMM-M142 verified: all 1,134 ordinary Release Analyzer tests pass, zero failures/
skips/build warnings/errors; 64 exact embedded PDB outputs. Main and independent
review confirm eighteen sites across eleven files, synchronous callback publication,
late-write/cancellation assertion strength, all nine initial source lines and preserved
mutable RangeCase identities. Canonical whitespace check requires zero changes.
All 87 PowerShell and 19 shell files are format-clean; 713 documentation local links
and the container contract pass after M137/M138. Draft MR #323 is mergeable.
Inventory refresh records 1,878 C# units / 2,253 candidates and 84 PowerShell units /
1,091 candidates, zero parse failures. Main traced 37 additional Frontend scoped
builders/grouping pairs/text builders and nine current PS snapshot/seed rows; those
are verified construction, not violations. Pending M142 builder rows were excluded
from automatic verified classification until their clean gate completed.

Post-M142 inventory classification: 2,253 C# candidates, 1,192 Open; 1,091 PowerShell candidates, 686 Open. All current source changes are committed and individually verified. The remaining candidates include typed native adapters, legitimate builders and pending refactors; nine Frontend configuration/phase properties remain explicitly open.

IMM-M143: Reopen four previously preserved Roslyn Construct test adapters, two
production Construct adapters, and one hidden params materialization. Pinned Roslyn
4.14 has immutable argument/nullable-annotation overloads. Verify baseline/native
versus immutable overloads for annotation/arity/constructed-symbol behavior before
changing the callers, then preserve exact argument order and nullable annotations.
The earlier claim that no immutable overload exists was incorrect. Status: planned.

IMM-M143 proof: all 30 pinned-Roslyn native/immutable overload controls pass, including top-level nullable annotations, nested and method symbols, wrong arity and constructed-type exception text/type. Main also found the fixture's hidden single-argument params allocation, making eight construction scopes to update. All symbol arguments originate from non-null compiler symbols or checked specialization results.

IMM-M144: Freeze nine inline ordered wire-record scopes across Doctor and fuzz
campaign/seed evidence. Use OrderedDictionary.AsReadOnly only around unique inline
backing maps with no mutable alias; retain order and direct dictionary property access,
remove the pscustomobject cast that would recreate writable note properties, and publish
existing immutable check/run vectors directly. This wrapper is shallow: parsed external
Doctor configuration remains a separately open nested-value scope. Validate real write
rejection, baseline JSON/property/filter/measure behavior, Doctor and seed regressions.
Status: planned before edits.

IMM-M142 full semantic checkpoint verified at pushed source 44e7c9b03: 11,854
ordinary Release tests pass, zero failures/skips/build warnings/errors, 23 framework
summaries across six isolated tasks using 32 scheduler slots; 225 exact embedded PDB
outputs. This checkpoint includes M131-M142 and excludes later M143/M144 source edits.
Latest literal check matches all 3,561 raw/interpolated text tokens across 823 changed
C# files against master16063804e. Earlier unproven vc-shadow timeout did not recur.

IMM-M143 verified: all 640 ordinary Release Contracts tests pass, zero failures/
skips/build warnings/errors; 16 exact embedded PDB outputs. Thirty pinned Roslyn4.14
native/immutable annotation/arity/exception controls pass and independent review
confirms all four files, target-layer increment, non-null symbol provenance and matching
nullable annotation vectors. Canonical whitespace check requires zero changes.

IMM-M144 probe correction: Measure-Object's property-name binding does not read keys from readonly OrderedDictionary run records. Use its explicit property expression { $_.observedCases } at the summary aggregation; legal run records always carry the validated integer field. Preserve the baseline zero-run fragment's pre-existing missing-Sum failure (real campaigns always schedule at least one run), and test scalar property/filter/measure/JSON behavior before committing.

IMM-M144 bounded controls verified: ten baseline/current wire/ordering/property/filter/
measure comparisons and 48 indexer/property write-rejection checks pass. All three
package snapshot controls, all 67 seed/validation/callback/source-capture controls,
actual Doctor regressions and 87-PS/19-shell format verification pass. Independent
review confirms nine inline backing maps have no escaping mutable alias, immutable
check/run vectors preserve JSON arrays, and the explicit measure adapter is necessary.
A minimal actual two-run campaign is planned after this commit to verify native runner
integration and clean-source evidence publication; external Doctor configuration remains
an open nested-record scope rather than a claimed deep immutable projection.

IMM-M144 actual workflow verified: the canonical private-clone campaign at committed
source 7517e5819 ran rotating seed17 and retained seed23063 with one case each.
Both native runner records passed validation; campaign.json reports passed=true,
requestedCases=2 and totalCases=2. Build/rebuild succeeded with zero warnings/errors.
Evidence is exported under artifacts/immutability-m144-campaign. This validates the
readonly records through the actual scheduler, encoded native runner, sum aggregation,
JSON publication and clean-source receipt, beyond fragment controls.
IMM-M145: Migrate twenty Package integration construction scopes across four files:
immutable packaged-analyzer item vectors and private coupled helpers, serial loader
observations, scoped command argv builders, an atomic ordinal-ignore-case loader cache
with a live resolver, strict framework artifact map and four environment snapshots.
Preserve native process/environment objects, dynamic dependency registration visibility,
last-write-wins cache behavior and argument/JSON ordering. Validate package workflow.
Status: implemented; package workflow validation in progress.

IMM-M145 scope clarification: the twenty planned scopes are in four source files (PackageLayoutSmokeTests, FinalCompilationProbeTests, VerifierPackageDefaultsTests, RuntimeEnvironmentIsolationTests), not five. Explicit duplicate rejection is retained for the artifact map; the live load-context resolver reads the current atomic cache so later registrations remain visible.

IMM-M145 initial package build identified three explicit-guard analyzer errors in the new forwarding wrapper/cache callback. Added equivalent ordered null guards; no runtime test failures occurred before this compile gate. Rerun the complete package workflow after correction.

IMM-M146: Migrate 26 Fuzz test scopes: twenty fixed outer vectors and six owned construction collectors/maps. Preserve nested CLR array and reflection identity subjects, callback aggregation overwrites, candidate insertion order and duplicate values. Freeze after all producer loops/awaited callbacks, before comparison/assertion publication. Validate the complete Fuzz test project after formatting and independent review. Status: planned before edits.

IMM-M146 coverage follow-up: coordinator checked 26 existing Gates production builder/text rows against construction, duplicate guards and freeze/consumption sites. Classified these as verified construction, not violations; the unchanged source passed the full M142 semantic gate and independent Gates review. Four production migrations and avoidable file-buffer adapters remain open.

IMM-M147: Migrate 97 reviewed Worker H-P test vectors/materializations across the current inventory. Keep sequential await evaluation, ordered/duplicate expected values, inner runtime objects/arrays, malformed input branches and the 65,536-byte zero payload. Use immutable outer vectors and equivalent immutable zero-byte construction; preserve conditional empty branches and runtime/native SUTs. Validate affected Worker tests after formatting and independent review. Status: planned before edits.

IMM-M145 verified: complete canonical Release package workflow passed 383 tests with one expected unsupported-host skip across 39 isolated shards; zero failures, build warnings or errors, 87 exact embedded PDB outputs. Independent review confirmed all twenty scopes, guarded forwarding overloads, dynamic cache visibility and native mutable environment boundaries. Formatter patch applied before testing; subsequent explicit-guard corrections preserved guard order. Status: implemented and verified.

IMM-M146 validation correction: the first Fuzz project command omitted the established ordinary semantic category filter. Build passed with zero warnings/errors, but the isolated Coverage console fixture correctly rejected a non-isolated testhost. Stopped only that exact verified task container and rerun with TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus; no source change is justified by this harness failure.

IMM-M148: Migrate 41 remaining avoidable Smt test collection scopes (38 outer snapshots/maps and three scoped collectors), plus the coupled private WithNativeModel predicate signature. Preserve native expression disposal, active selector ordinal comparison, query/expected order and duplicates, cycle Add Boolean behavior and callback timing. Overlay partial-write tests, native Z3 MkOr adapters and disposal subjects remain mutable by contract. Validate affected Smt tests after formatting and independent review. Status: planned before edits.

IMM-M146 verified: complete ordinary Fuzz project passed all 302 tests with zero failures/skips/build warnings/errors and 87 exact embedded PDB outputs. Independent review confirmed all 26 scopes; multiline vector reflow and whitespace verification completed before the passing gate. Original unfiltered harness run is superseded by the corrected ordinary-category invocation. Status: implemented and verified.

IMM-M147 compile correction: actual build found downstream Array.IndexOf consumers of the newly immutable CodecFixture roots, and two redundant ToImmutableArray calls rejected by CA2009. Use the corresponding immutable IndexOf (same default equality and first-index semantics) and pass existing immutable roots/conditional items directly. No native mutable subject is involved. Recheck consumers and rerun affected classes.

IMM-M149: Freeze nine validator/Doctor record scopes: three fixed validator property vectors, three ordered coverage maps, readonly validator result root, immutable empty failures and scalar Doctor configuration snapshot. Preserve ordered JSON, strict duplicate/unknown validation, callback resealing, scalar property lookup and Doctor text output. Keep locally owned ordered construction maps and expose readonly views only after their last write with no escaping mutable alias. Validate baseline/current wire/text/error controls and actual Doctor regressions. Status: planned before edits.

IMM-M148 semantic inventory gap: review found two conditional IrTerm[] predicate locals and their private NativeCompleted WithNativeModel signature outside scanner rows. Converted all three with the same default-array guard. Together with the CompilerOwned helper and explicit int[] expectation cast, this batch covers 41 inventory rows plus five additional declarations/expressions. Both native-model helpers preserve guard order and repeated enumeration.

IMM-M147 verified: all 1,868 ordinary tests in the 38 affected Worker classes passed, zero failures/skips/build warnings/errors and 88 exact embedded PDB outputs. Independent review rechecked all 45 files and downstream consumers after the IndexOf/CA2009 corrections. Sequential awaits, duplicate probes, IL expectations, zero-byte payload and mutable runtime SUTs are preserved. Status: implemented and verified.

IMM-M150 inventory scope expansion planned: scanner missed conditional explicitly typed array locals, cast collection expressions and private array helper parameters. Add ArrayStorage, ArrayCast and ordinary array ParameterInput categories so known gaps are recorded systematically. Preserve existing IDs/statuses for unchanged rows; new candidates require coordinator trace/classification and are not automatically violations. Inventory tooling itself now uses owned builders rather than repeated immutable root Add assignments. Refresh after pending source gates complete.

IMM-M148 compile correction: the conditional Array.Empty<Assumption> branch also needs an immutable empty value. CA2000 does not infer disposable fixture ownership through ImmutableArray.Create; retain each mutable disposal subject in a using local and assert decoder disposal before scope cleanup. The fixture Dispose methods only set IsDisposed=true, so final idempotent cleanup cannot satisfy or mask the earlier disposal assertions. Rerun all ordinary Smt tests after these corrections.

IMM-M150 additional gap: add ArrayEmpty candidates for explicit Array.Empty<T>() calls; the M148 conditional demonstrated that this array construction form also escaped the original scanner. New scanner categories are evidence coverage only, with native/public API/SUT exclusions still requiring trace.

IMM-M151: Migrate five Gates production collection scopes across OpenSourceCorpusCatalog, OpenSourceCorpusImporter and AnalyzerGateHost, with coupled Git-blob test/UTF8/hash consumers. Use immutable fixed source roots/link candidates/unreviewed IDs and an immutable owned Git blob snapshot; consume the owned compiler PE stream through a readonly span at File.WriteAllBytes instead of allocating a transient array copy. Preserve Git process cancellation/reaping, byte length/order, error messages and importer paths. Validate ordinary Gates tests. Status: planned before edits.

IMM-M149 verified: 35 baseline/current validator and Doctor controls passed, including real one-case and 1,000-case wire records, strict validation errors, callback mutation/resealing, property access and identical Doctor text/JSON. All 36 new write-rejection checks and earlier ten wire/48 write-rejection checks passed; actual Doctor regressions passed. All 87 PowerShell and 19 shell files are format-clean after applying the formatter patch. Independent review confirms unique backing ownership and no post-freeze writes. Status: implemented and verified; actual native campaign will be repeated at the combined clean-source checkpoint.

IMM-M148 verified: all 368 ordinary Smt tests passed with zero failures/skips/build warnings/errors and 19 exact embedded PDB outputs. This includes all 41 inventory scopes, two private helper signatures, two missed conditional array locals, the explicit cast expectation and Array.Empty branch. Independent review traced disposal subjects, native Z3 adapters, ordinal selectors and partial-write overlays; assertions still observe decoder disposal before idempotent using cleanup. Status: implemented and verified.

IMM-M150 inventory refreshed: parsed all 1,878 tracked/new non-vendored C# units and all 84 tracked PowerShell scripts with zero parse failures. The expanded C# inventory records 2,459 candidates, including 375 newly covered ArrayCast/ArrayEmpty/ArrayStorage/ordinary-array ParameterInput rows; 1,330 remain Open after reviewed construction classification. PowerShell records 1,095 candidates with 696 Open. These counts include legitimate builders, native boundaries and mutable SUTs still needing current-coordinate classification; increased coverage is not newly introduced mutability. Pending M151 source owners were excluded from verified construction classification. Roslyn Construct blanket exemptions were removed from the classifier.

IMM-M152: Convert five newly exposed Smt private-wrapper rows across KeyedEntryModelTests and NativeHornApplicationReconstructionTests to immutable input/expected vectors. Preserve default-array guards, predicate/model variable order, native Expr object identity and finally-based disposal of expression.Args. Validate affected classes after whitespace check and independent review. Status: planned before edits.

IMM-M153: Convert four remaining private Contracts test array wrappers/storage rows across PartialMethodContractTests, TypedContractLoweringTests and StaticCompilerFlowCaptureAdmissionTests. Use immutable inputs and explicit collection expressions at coupled variadic call sites under the pinned C#12 language level. Preserve default guards, indexing/short-input behavior, boxed switch values and source order. Three existing owned matcher builders are coordinator-verified construction, including local first-match removals; retain builders as requested rather than persistent root reassignments. Validate Contracts tests. Status: planned before edits.

IMM-M151 verified: all 474 ordinary Gates tests passed with zero failures/skips/build warnings/errors. Independent review confirmed exact span lengths, immutable Git blob ownership, all UTF8/hash consumers, synchronous native file writes and unchanged process cancellation/reaping. Canonical whitespace check required zero changes. Status: implemented and verified.

IMM-M154: Replace two owned placement-matrix array construction rows in NativeFailureLifecycleTests with row/outer immutable builders and frozen snapshots. Preserve the final native BoolExpr[] MkOr adapter, expression ownership, assertion order and actual timeout/runner retirement SUT. Coordinator rejected the broad native exclusion for these two outer construction rows. Twenty-three distinct Smt identity/overlay/lifecycle/native-adapter rows are now traced and classified. Validate M152 and M154 affected classes together. Status: planned before edits.

IMM-M155: Migrate four Specs test data scopes across three files: scoped substitution and runtime binding builders with frozen publication, strict duplicate rejection for bindings, immutable empty declaration input and immutable PE snapshot through the Roslyn overload. Preserve scalar/object referents, map evaluation order, invalid table errors and generated metadata bytes. Validate the complete ordinary Specs project. Status: planned before edits.

IMM-M152/M154 verified: all 41 ordinary tests in the three affected Smt classes passed with zero failures/skips/build warnings/errors and 19 exact embedded PDB outputs. Independent review confirmed default guards, two-pass predicate enumeration, expected native expression identity, row/outer freeze order, finally disposal and unchanged actual native timeout/runner retirement assertions. Whitespace check completed. Status: both batches implemented and verified.

IMM-M153 literal control: all 3,668 raw/interpolated literal tokens across 852 changed C# files match current merged master, including source blocks in the eight rewritten compilation-helper callers.

IMM-M153 verified: all 640 ordinary Contracts tests passed with zero failures/skips/build warnings/errors and 16 exact embedded PDB outputs. Main and independent review confirmed all eight compilation and twelve scalar-input callers, boxed switch values, default guards and ordinal indexing. Literal control confirms unchanged source blocks. Status: implemented and verified.

IMM-M156 actual workflow verified: canonical private clean clone at 6f42fc07f23e0fc4b1a7a98224549df88abd0f7d passed rotating seed17 and retained seed23063 with one case each. Both native runner records and validator records passed; campaign reports requestedCases=2,totalCases=2,passed=true. Four build/rebuild stages passed with zero warnings/errors. Exported artifacts/immutability-m156-campaign validates the M149 nested readonly coverage records through actual scheduling/native execution/sum aggregation/JSON/receipt publication. Coordinator also classified twenty current-coordinate Doctor/validator construction/display rows from the passing controls.

IMM-M157: Convert nineteen remaining avoidable Fuzz test array wrapper/storage/expectation scopes across eight files. Fully replace three private Exact/Closed params wrappers and their 34 expanded-form callers with immutable input vectors; preserve nested CLR sequence/object identity, record IReadOnlyList injection and null/alias SUTs. Other private replay/CLI inputs become immutable with minimal reflection adapters; assertion-only object vectors and conditional Boolean/status vectors become immutable. Validate all ordinary Fuzz tests after literal/format controls and independent review. Status: planned before edits.

IMM-M155 verified: all 150 ordinary Specs tests passed with zero failures/skips/build warnings/errors. Independent review confirmed indexer overwrite semantics, strict duplicate bindings including nullable values, tuple-key equality, unchanged empty-table failure and exact emitted byte capture through Roslyn's immutable overload. Canonical whitespace check required zero changes. Status: implemented and verified.

IMM-M158: Convert eight remaining Specs private array-input rows in ApiSpecRuntimeOracleTests plus the coupled ObserveEmptySequence delegate shape. Replace six params wrappers with immutable inputs and explicit collection-expression callers under C#12, and two constructor Type[] helper inputs with immutable vectors and minimal DynamicMethod/GetConstructor adapters. Preserve callback order, receiver mutation witnesses, native constructor exception timing/parameter names and valid empty sequences. Validate all ordinary Specs tests. Status: planned before edits.

IMM-M157 verified: all 302 ordinary Fuzz tests passed with zero failures/skips/build warnings/errors and 87 exact embedded PDB outputs. Independent review checked all nineteen scopes, all 34 expanded-form helper callers, nullable/nested CLR element identity, valid empty vectors, custom indexer-versus-enumerator ordering, null record injection and minimal native reflection adapters. Canonical formatter patch applied before the passing gate. Status: implemented and verified.

IMM-M157 current-coordinate coverage: refreshed all 1,878 C# units to 2,431 candidates after verified source batches. Coordinator traced/classified the remaining thirty Fuzz.Tests rows: two owned immutable builders, two source builders, twelve actual CLR alias/order subjects and fourteen minimal reflection/entrypoint adapters. The final Smt native MkOr adapter was also classified after both matrix layers migrated. No Open rows remain in the expanded Fuzz.Tests, Smt.Tests or Contracts/Contracts.Tests partitions at this checkpoint; production Fuzz and other projects remain separate open scopes.

IMM-M159: Migrate 55 reviewed Worker A-G outer vector/materialization rows: 25 fixed creations, four typed collection vectors, six conditional/staged storage vectors, four empty Roslyn/JSON inputs and sixteen ordered/set/strict-map snapshots. Preserve mutable PE buffers, native reflection inputs, nested referent identity, expected duplicates, comparers and strict duplicate rejection. Remove coupled redundant immutable conversions; one reported EffectSummaryFixpoint gap is the existing scanner row starting at the multiline invocation, not an extra site. Validate affected classes after formatting/review. Status: planned before edits.

IMM-M158 verified: all 150 ordinary Specs tests passed with zero failures/skips/build warnings/errors and 16 exact embedded PDB outputs. Renamed the private constructor type-vector parameter to types so the late default guard preserves the native exception parameter name and timing while satisfying CA2208. Main and independent review confirmed all coupled callback callers, immutable delegate shape and minimal reflection adapters. Status: implemented and verified.

IMM-M160: Convert Analyzer private diagnostic, fingerprint, intrinsic-invocation and initializer-owner snapshots in five test files. Use owned builders for ancestor/owner collection and freeze after all callbacks; return Roslyn immutable diagnostic vectors directly. Update both coupled fingerprint local-function return shapes. Preserve source/ancestor order, owner identity, diagnostic messages and count assertions. Validate the full ordinary Analyzer project. Status: planned before edits.

IMM-M161: Migrate dependency-audit owned source/project/framework/package collectors to scoped immutable builders, retaining comparer-sensitive duplicate checks. Sort ordered builders before frozen publication; retain native process argv and JSON array schema checks. Publish project records and ordered evidence/count records through uniquely owned readonly maps. Replace private helper array recapture with direct immutable results. Compare baseline/current ReportPath fixtures and output bytes before the canonical dependency gate. Status: planned before edits.

IMM-M159 verified: all 294 ordinary tests in the eighteen affected Worker classes passed with zero failures/skips/build warnings/errors and 88 exact embedded PDB outputs. Main and independent review confirmed all 55 selected sites, strict duplicate maps/comparers, unchanged native/SUT arrays, staged immutable storage and coupled conversion removal. The first build identified one concrete immutable helper parameter requirement (CA1859); corrected before the passing gate. Canonical formatting and diff checks pass. Master 0524596fa (#339) was integrated from the remote branch without conflicts. Status: implemented and verified.

IMM-M160 verified: all 1,134 ordinary Analyzer tests passed with zero failures/skips/build warnings/errors and 64 exact embedded PDB outputs. Main and independent review confirmed direct initialized Roslyn results, both coupled fingerprint local functions, traversal order and builder freeze after async owner capture; count assertions use immutable lengths. Canonical whitespace check required zero changes. Status: implemented and verified.

IMM-M161 verified: nineteen baseline/current ReportPath controls pass with asserted positive success and negative rejection, identical output bytes/console/error text, including single-project cardinality, duplicate source/project/key/framework, missing/invented coverage, non-array schema, vulnerabilities and problems. Canonical dependency-audit passed for all 51 projects using one approved source. Post-construction project/package sets are frozen before comparison/error publication. Main and independent review confirm ordinal sorting, comparer-sensitive Boolean Add checks, comma-protected immutable helper returns and ordered readonly wire records. All 87 PowerShell and 19 shell scripts are format-clean. Status: implemented and verified.

IMM-M160 coverage checkpoint: all 1,878 C# units refreshed to 2,359 candidates; after three builder and twenty-four coordinator-traced Specs classifications, 1,142 remain Open. Specs.Tests joins Fuzz.Tests, Smt.Tests and Contracts partitions with no unclassified current rows. PowerShell inventory has 1,082 candidates and 663 Open at the M161 source snapshot; new builder classifications remain pending. Literal control passes all 3,825 raw/interpolated tokens across 859 changed C# files. Exhaustive review of remaining partitions and final combined gates remain required.

IMM-M162: Convert ten fixed fixture/expectation vectors across ContractApiTests, IrTypedIntegerDifferentialTests and IrCSharpDifferentialOracleTests, plus the generated-category collector using an immutable set builder frozen before assertion. Preserve actual runtime arrays and reflection out-argument writeback. Validate ordinary Attributes and Testing projects after review/formatting. Status: planned before edits.

IMM-M163: Migrate 87 reviewed Worker H-Z/LongFuzz snapshot/vector rows across 26 files, including ten assertion-only array casts, conditional symbol/exception inputs, ordered diagnostic snapshots and two nested TypedWidth literal/materialization pairs. Preserve typed-width order/distinct behavior, actual runtime/reflection arrays and private native boundaries. Use immutable snapshots and strict map preservation; remove coupled redundant conversions found during main review. Validate affected ordinary Worker classes after formatting and independent review. Status: planned before edits.

IMM-M162 verified: all 24 ordinary Testing and all 21 ordinary Attributes tests passed with zero failures/skips/build warnings/errors. Main and independent review confirmed ten vector sites plus the category set builder, named tuple members, default enum comparers, original traversal order and unchanged native reflection writeback/runtime-array subjects. Canonical formatting and diff checks pass. Status: implemented and verified.

IMM-M164: Convert eight simple Frontend test branch/empty-symbol/type-argument scopes and fourteen PE helper/input rows across the reviewed conditional/contract identity/emission tests. Freeze owned default streams directly from their exact-length buffers, use immutable Roslyn/PEReader overloads, and retain minimal byte[] adapters only for actual runtime MemoryStream/file APIs on net8. Preserve nullable annotations, parser branch order, loaded assembly identity and default-vector guards. Validate all ordinary Frontend tests after main/independent review and formatting. Status: planned before edits.

IMM-M163 verified: all 530 ordinary tests in the affected Worker/LongFuzz regression classes passed with zero framework failures/skips/build warnings/errors. Explicit long-running fuzz campaigns remain outside the ordinary category gate. Main and independent review confirmed all 87 selected sites, typed-width ordered distinct projections, conditional inputs, typed NUnit expectations and JSON array shape. The native exception vector now passes directly without redundant copying. Canonical formatting and diff checks pass. Status: implemented and verified.

IMM-M165: Convert sixteen reviewed Worker A-G stream/hash/file/private-helper rows. Use exact-length spans for hashes and synchronous files, memory for async writes, and immutable images for Roslyn. Preserve actual Assembly.Load byte-array adapters. Migrate the secondary expectedNames params input and its two coupled callers to immutable vectors; change the private FixedChecksum hash input to ReadOnlySpan<byte>. The three deeper Alias/Bulk image scopes remain separate work. Validate affected Worker classes after review and formatting. Status: planned before edits.

IMM-M164 verified: all 2,064 ordinary Frontend tests passed with zero failures/skips/build warnings/errors. Main and independent review confirmed 22 rows across fourteen files, exact copied PE lengths from owned exposable streams, immutable Roslyn/PEReader consumers, default guards, nullable annotations and runtime/file adapters. Canonical formatter patch applied before the passing gate. Status: implemented and verified.

IMM-M166: Remove the two unused mutable-array ImmutableFixtureRows compatibility overloads (six syntax rows) after tracked caller review. Replace repeated immutable-root SetItem assignments in the retained reference-replacement helper with one lazily created scoped builder, frozen after all matches; return the original immutable vector when no match exists. Preserve reference identity matching, all matching positions, null/default guard order and single-position replacement semantics. Validate the shared helper through the full ordinary semantic gate because it is linked across test projects. Status: planned before edits.

IMM-M167: Make retained release evidence/SPDX records deeply readonly while preserving ordered JSON: wrap uniquely owned ordered maps, use immutable checksum/creator vectors and frozen package/file/relationship/license arrays, and finish the conditional SBOM builder before readonly publication. Preserve archive construction, license first-seen order and comparers. Compare baseline/current deterministic fixture artifact bytes and write rejection, then run the actual release-evidence regression. Status: planned before edits.

IMM-M165 verified: all 139 ordinary tests in twelve affected Worker classes passed with zero failures/skips/build warnings/errors. Main and independent review confirmed exact span/memory lengths, awaited writes within stream lifetime, Roslyn immutable images and both expected-name callers. The initial build required the repository's all-values-valid annotation for the ReadOnlySpan hash helper; empty spans are valid and every caller supplies non-null data. M166 shared helper was present in the passing retry; its full cross-project gate remains pending. Status: implemented and verified.

IMM-M168: Convert four Attributes public-documentation map/return scopes to immutable ordinal maps, preserving strict duplicate rejection and sorted member iteration with scoped builders. Freeze reflection parameter arrays immediately at the private helper boundary. Also remove the remaining owned default-stream ToArray().ToImmutableArray bridge in FrontendLoweringTests using an exact-length immutable span snapshot. Validate ordinary Attributes plus affected Frontend class after formatting and review. Status: planned before edits.

IMM-M167 verified: the actual release-evidence and attestation-policy regression fixture passed for both ordinary licenses and a nonempty LicenseRef branch. Across both variants, all six SPDX/manifest/checksum files match baseline bytes and 81 retained-map write attempts were rejected. Main and independent review confirm fifteen uniquely owned readonly maps, immutable nested vectors, conditional licensing appended before freeze, first-seen license order and unchanged archive/static-link/receipt checks. Canonical formatter patch applied; the LicenseRef control passed afterward. Status: implemented and verified.

IMM-M169: Convert ten reviewed IR scopes: immutable private enum input with three coupled callers; immutable FIFO identity queues for two staging controls; immutable substitution sets and scoped typed-identity uniqueness builder; two immutable expected-read vectors; immutable empty sequence inputs in factory/control. Preserve FIFO requests, callback counts, reference order, default/comparer semantics and actual mutable heap/stream subjects. Validate all ordinary IR tests after formatting/review. Status: planned before edits.

IMM-M168 verified: all 21 ordinary Attributes tests and all 63 affected FrontendLowering tests passed with zero failures/skips/build warnings/errors. Main and independent review confirmed CollectionExtensions.TryAdd strict duplicate semantics on both immutable map builders, ordinal sorted iteration, reflection snapshot capture and exact PE buffer copies. Combined M168/M169 whitespace check required zero changes after applying the M169 guard layout. Status: implemented and verified.

IMM-M169 verified: all 485 ordinary IR tests passed with zero failures/skips/build warnings/errors. Main and independent review confirmed all ten scopes, three enum callers, FIFO requests/exhaustion, immutable expected order and uniqueness-builder Add results. Actual heap/stream subjects remain unchanged. Status: implemented and verified.

IMM-M166 full checkpoint: 11,854 ordinary semantic tests passed with zero failures/skips/build warnings/errors, 23 framework summaries, six isolated tasks, 32 scheduler slots and 225 exact embedded PDB outputs. This snapshot includes M166 helper and verified source through M165/M167, before subsequent M168/M169 edits; their targeted gates are recorded above. Package callers remain pending the package workflow.

IMM-M169 current-coordinate coverage: refreshed 1,878 C# units to 2,209 candidates; the classifier excludes pending C# owners. Coordinator traced nine intentional Frontend lowering-builder/session state transitions, including tested Compilation bootstrap and cancellation/budget sentinels, and immutable callback/output values; preserve them under the user's mutable-SUT/builder exception. Eighteen M167 release construction/publication rows are verified by both license variants. Current remaining Open counts: 983 C# and 646 PowerShell. All 87 PowerShell and 19 shell files are format-clean, 715 local documentation links and container contract pass.

IMM-M170: Convert eight production input/publication scopes plus coupled replay consumers. Freeze two exact-capacity finite-domain assumption builders before proof queries. Make TotalProgramSourceCase outer inputs immutable and adapt once per case before reflection, reusing the native vector for writeback/alias/IR/reproducer observations. Use strict ordinal-type/reference-identity alias-cache builders; retain the existing private readonly session failure builders and frozen Result publication. Make the CLI wrapper readonly, write the library image through exact-length memory, and cache the Frontend enum universe as an immutable readonly vector. Preserve actual inner CLR arrays/objects. Validate all ordinary Fuzz and affected Frontend tests, then an actual bounded native campaign after commit. Metadata compilation lifecycle properties remain separate pending work. Status: planned before edits.

IMM-M166 package verification: all 39 isolated package shards passed, 383 tests passed with one expected unsupported-host skip and zero failures. Combined with the 11,854-test ordinary semantic checkpoint, this verifies both linked shared-helper owners. Main and independent review confirmed lazy builder ownership, all matching reference positions, no-match identity and unchanged null/default guard order. Status: implemented and verified.

IMM-M171: Replace MetadataSourceCompilation's three post-construction result properties with an immutable compilation result returned by CompileAsync. Keep resource ownership/disposal in the compilation scope, carry the artifact-phase start timestamp as a scalar and use local timing state for verification. Update all six production/test callers, preserving compile/artifact/verification callback order, phase coverage, actual CLR static-state subjects and cancellation/resource lifetime. Validate all ordinary Fuzz tests and actual bounded native replay. Status: planned before edits.

IMM-M172: Convert three deferred Worker emitted-image scopes and all coupled AliasOnlyConditionalAttributeAudit private PE consumers. Snapshot exact emitted spans/file bytes to immutable arrays, use span hashes and memory file writes, and use immutable PEReader images. Keep minimal runtime LoadFromStream adapters and actual reflection argument/type mutation subjects. Preserve byte lengths, MVID/hash observations and guard order. Validate all affected ordinary classes after formatting and independent review. Status: planned before edits.

IMM-M173 coverage: coordinator checked sixteen current dependency-audit builder/publication rows against source and the passing M161 controls. Scoped duplicate guards, immutable snapshots and uniquely owned ordered readonly records are verified; six local array projections and the private process-wrapper contract remain open. PowerShell Open count is now 630 at the existing source inventory checkpoint.

IMM-M174 integration: master c5b49937b (#335 Queue FIFO/nominal aliases) introduced eight content conflicts. Resolve by keeping immutable wire records/nullable immutable interface vectors and validation, immutable source projections, existing lowering builders, new owned nominal certificate/cache builders, sorted immutable interface publication and existing reentrant persistent identity caches. Retain Queue specialization/source-compilation propagation. Main/independent trace also caught two auto-merge interactions: nominal selector append must retain the immutable result, and new field traversal must use deterministic field order and existing observation tuples. Both corrected before integration gates. New master mutable additions remain subject to the next inventory review. Status: resolved, full semantic integration verification pending.

IMM-M170 targeted verification: all 302 ordinary Fuzz and 2,064 ordinary Frontend tests passed with zero failures/skips/build warnings/errors before the Queue master integration. Main and independent review confirmed exact-capacity proof freezing, shallow outer input snapshots, reused native reflection writeback vector, reference-comparer alias builders and exact library file bytes. Initial block-body style issue was corrected before the passing gate. Whitespace and literal checks pass (3,847 raw/interpolated tokens across 877 changed C# files against c5b49937b). Combined merge integration and actual native campaign remain pending.

IMM-M175: Migrate newly merged Queue/nominal construction collectors and solver-local immutable-root rebinding to scoped immutable builders. Preserve group first-seen numbering and metering order with an explicit immutable order snapshot, freeze exact-sized masks before membership checks, freeze solver selector inputs at each native check, and preserve callback/result order in new Worker tests. Keep actual Queue/CLR identity and mutation subjects. Validate ordinary SMT, Frontend and affected Worker tests after main/independent review. Status: planned before edits.

IMM-M174 build integration: adapt the new malformed class/interface metadata controls using scoped type/callable builders and deep immutable root replacement, preserving all sixteen invalid-wire branches and source-bound digest checks. Remove the stale context AllowObjectWidening copy (configuration already belongs to the expression lowerer) while retaining source-compilation propagation. Queue argument values use an exact-sized builder frozen before the existing immutable helper. The combined Release solution now builds with zero warnings/errors; full semantic tests are running. First full attempt was stopped after discovery of the init-only test assignments; second exposed the stale initializer and Queue helper input mismatch, both fixed in the third attempt. Independent reviews confirm wire/model/cache and M171 lifetime semantics. Status: build verified, full gate pending.

IMM-M176 coverage: coordinator traced thirteen existing Frontend test rows through producer, observer/publication and cleanup. Ten are scoped immutable/text builders or the session-entry cleanup journal; three are the actual AppendSymbols builder mutation/prefix-preservation SUT. Private convenience wrappers and outer invocation containers remain actionable, not native exemptions. Existing-checkpoint Open counts are 970 C# and 630 PowerShell; newly merged master scopes require inventory refresh.

IMM-M177: Replace three private Frontend params convenience wrappers (two compilation helpers and the preprocessor parser) with explicit immutable input vectors. Rewrite all 24 expanded-form callers, including initialized empty vectors, using syntax-checked argument insertion without modifying source literal contents. Preserve reference/symbol order and default/null guard position. Validate the full ordinary Frontend project together with M175's Frontend changes. Status: planned before edits.

IMM-M171 verified: all 302 ordinary Fuzz tests pass in the combined M174 semantic gate. Main and independent review confirm all six consumers retain the result inside the compilation resource lifetime, compile/artifact/verification timing boundaries are preserved, and the readonly result replaces three lifecycle setters. Canonical formatter required zero changes. Actual native campaign remains a later combined workflow check. Status: implemented and verified.

IMM-M172 verified: all affected Worker tests pass within the 5,835-test Worker partition of the full M174 gate. Main and independent review confirm exact immutable PE lengths, span hashes, awaited memory writes, immutable PEReader consumers and minimal runtime stream adapters. Actual reflection/array mutation subjects remain intact; formatting and source literal checks pass. Status: implemented and verified.

IMM-M174 full integration checkpoint: all 12,039 ordinary Release semantic tests passed with zero failures/skips/build warnings/errors, 23 framework summaries, six isolated tasks, 32 scheduler slots and 225 exact embedded PDB outputs. This snapshot includes M170/M171/M172 and the resolved Queue/lowering/metadata integration before M175/M177 follow-up edits. Worker 5,835, Frontend 2,094, SMT 382, IR 486 and Effects 121 include the new master coverage. Remaining newer source batches require their targeted gates. Status: merge integration verified.

IMM-M178: Replace six dependency-audit array projections and coupled XML/problem-summary recaptures with scoped immutable builders and scalar parser counts. Preserve structural-error precedence, JSON array schema checks, null elements, source order, culture-sensitive Sort-Object diagnostic ordering and join behavior. Extend the nineteen baseline/current controls with ten focused XML/problem/coverage cases before the actual dependency gate. Status: planned before edits.

IMM-M177 verified: all 2,094 ordinary Frontend tests passed with zero failures/skips/build warnings/errors. Main and independent review confirmed all 24 rewritten helper callers, initialized empty vectors, single evaluation/reference order, unchanged raw source strings and preserved ArgumentNullException default-input guard position/parameter names. Canonical whitespace check required zero changes; literal checkpoint passes 3,851 tokens across 883 changed C# files. Status: implemented and verified.

IMM-M175 verified: all 382 ordinary SMT, 2,094 ordinary Frontend and 1,195 affected Worker tests passed with zero failures/skips/build warnings/errors. Main and independent review confirm first-seen nominal group numbering/order, exact 64-slot-bounded mask construction, frozen native selector inputs including retry, ordinal active lookup, class-chain reverse order, callback completion before result snapshots and unchanged Queue/reference subjects. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M178 verified: all 29 baseline/current dependency-audit controls passed, with asserted positive success and negative rejection plus identical output bytes, console and exception text. Ten added cases cover XML shape/order/case/comments, null and multiple problem entries, and invented coverage. The actual canonical dependency audit passed for all 51 projects using one approved source. Main and independent review confirm parser error precedence, immutable snapshots, void Add behavior and unchanged culture-sensitive diagnostic ordering. All 87 PowerShell and 19 shell scripts are format-clean. Status: implemented and verified.

IMM-M179 workflow checkpoint: run the actual clean-clone native fuzz campaign after verified Queue/metadata/solver integration, using seeds 17 and 23063 with one case each. Require both native runner/validator records and the aggregate campaign receipt to pass, and preserve separate M156 evidence. This is a bounded workflow control; it does not replace the exhaustive source audit. Status: planned before execution.

IMM-M179 verified: the actual clean-clone native campaign at 0377011fa2f74896b662e575fdad03b2be660847 passed both one-case seed17/seed23063 runner and validator records, requestedCases=2,totalCases=2,passed=true. Four build/rebuild stages reported zero warnings/errors. This verifies the combined M170/M171/M175 production changes through actual scheduling/native execution/validation/receipt publication. Status: workflow verified.

IMM-M179 current-coordinate coverage: refreshed 1,890 C# units to 2,238 candidates and 84 PowerShell scripts to 1,081 candidates with zero parser failures. Main reconciled reviewed builder coordinates, six shifted Frontend phase-state rows, all 25 production Fuzz rows and 21 dependency-audit construction/publication rows. Production Fuzz now has zero unclassified rows; three CLR array subjects and one native reflection vector remain intentional. Existing-checkpoint Open counts are 964 C# and 623 PowerShell. Two newly exposed Fuzz test outer-wrapper rows remain actionable.

IMM-M180: Make TotalProgramAliasSourceReplayTests outer inputs and its private compiler/interpreter helper immutable; preserve nested object identities and all four native/IR alias cases. Keep only the final MethodInfo.Invoke array adapter and remove the redundant record-input conversion. Preserve the default/null guard order and parameter name. Validate all four affected tests after review/formatting. Status: planned before edits.

IMM-M180 verified: all four object-alias replay tests passed with zero failures/skips/build warnings/errors. Main and independent review confirmed both two-/three-parameter shared/distinct topologies, immutable outer vectors, native-only shallow reflection adaptation and unchanged IR element identities. Formatter required zero changes. Repository layout passed 2,639 sources, 59 projects and all 151 pinned common files after the next master merge. Canonical script controls also pass all 87 PowerShell/19 shell format checks, 715 documentation links and container contract. Status: implemented and verified.

IMM-M181: Master 79b9b0d88 (#337 integral Convert.ToInt64) merged cleanly at 2431a68d5. Main/independent review found the new marker/lowering/replay integration compatible with immutable contexts and snapshots. Migrate two read-only overload snapshots and one interpreter input map in the new IntegralConvertOracleTests; retain CultureInfo callback array writes and generated array subjects as intentional mutable SUTs. Validate ordinary Effects, Frontend and the affected Worker classes. Status: planned before edits.

IMM-M182: Reuse one fixed immutable argument vector in each of the two newly merged integral Convert lowering helpers, using the existing CallSnapshot API rather than params adapters. Replace the effect inventory's edge List/AddRange with an owned immutable builder frozen before iteration, preserving duplicate boundary values and order. Retain the actual culture-callback array mutation subject. Validate actual integral lowering/Worker paths and the complete Effects inventory after formatting/review. Status: planned before edits.

IMM-M181 verified: all 121 ordinary Effects, 2,094 ordinary Frontend and 1,170 affected Worker tests passed with zero failures/skips/build warnings/errors. The Worker gate also includes M182 production lowering. Main and independent review confirm ordered immutable overload snapshots and frozen interpreter input maps; actual culture callback array mutation remains unchanged. Canonical formatter required zero changes. Status: implemented and verified.

IMM-M183: Preserve Start-SharpProofEncodedPowerShell's public legacy array binder through its default parameter set while adding a named immutable internal path with stable positional compatibility. Freeze argument quoting and uniquely owned process options before publication; migrate all three internal callers. Compare legacy binding and encoded-command bytes against baseline, exercise immutable/default inputs, then run actual dependency and bounded native workflows. Status: planned before edits.

IMM-M182 verified: all 121 ordinary Effects inventory tests passed after the builder migration; the 1,170 affected Worker tests also cover both production Convert lowering helpers. Zero failures/skips/build warnings/errors. Main and independent review confirm duplicate edge values/order, shared immutable arguments and CallSnapshot behavior. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M184: Migrate five private Frontend path/metadata/parameter wrappers and their coupled callers to immutable vectors. Preserve ordered duplicate metadata, reflection identity, path containment and ArgumentNullException guard order; retain the final framework path adapter. Validate all three affected classes after main/independent review and formatting. Status: planned before edits.

IMM-M185: Replace per-evaluation interpreter result-map root rebinding and the reentrant comparer test's comparison-log root rebinding with uniquely owned builders. Preserve factory persistent identity buckets, callback recursion, insertion order/duplicates, result cache semantics and live scalar state. Freeze the comparison log on observation. Validate the full ordinary IR project after review/formatting. Status: planned before edits.

IMM-M183 compatibility checkpoint: all 33 canonical before/current controls pass, preserving named/positional legacy scalar/vector binding, duplicate/order/escaping, encoded-command bytes, redirects and rejection behavior. Every successful current call rejects mutation of the readonly process options. Default immutable input is rejected by the mandatory binder before the defensive IsDefault guard. All 87 PowerShell/19 shell scripts are format-clean. Actual dependency/native workflow verification remains pending. Main trace confirms all three internal callers use the named immutable path. Status: implemented; workflow checks pending.

IMM-M186: Migrate the Analyzer host/options helper contracts and fifteen construction maps across ten files to owned immutable builders with frozen call boundaries. Preserve comparers, optional specification packs, initializer indexer overwrite semantics, strict host Add semantics, and four post-default override fixtures. Validate the entire ordinary Analyzer project after syntax-checked coupled rewrites and independent review. Status: planned before edits.

IMM-M184 verified: all 29 tests in the three affected Frontend classes passed with zero failures/skips/build warnings/errors, including the authentic isolated Attributes rebuild and policy duplicate controls. Main and independent review confirm all seven path callers, four direct policy callers and two reflection-helper callers, immutable outer snapshots, duplicate order and ArgumentNullException guard ordering. Canonical formatter required zero changes. Status: implemented and verified.

IMM-M185 verified: all 486 ordinary IR tests passed with zero failures/skips/build warnings/errors, including portable golden/roundtrip, shared-DAG/depth, lazy observer concurrency and reentrant identity controls. Main and independent review confirm private per-evaluation builder ownership and unchanged Add equal-key semantics, comparison order and nested callback timing. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M187: Replace documentation discovery's retained array capture with an owned immutable builder frozen after the existing native Git exit check. Preserve culture-sensitive Sort-Object uniqueness/order, empty/singleton/multi-file behavior and Git failure text. Compare baseline/current fixture outputs and run the actual documentation automation plus local-link gate. Status: planned before edits.

IMM-M186 verified: all 1,134 ordinary Analyzer tests passed with zero failures/skips/build warnings/errors. Main and independent review confirm all fifteen map constructions, twelve external host calls, four override builders, ordinal/ignore-case comparers and strict host AddUnique behavior. Three generated alias controls use separate owned builders; original indexer-initializer semantics remain unchanged. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M188: Replace four Worker callback/observer lists with scoped immutable array builders and freeze after the awaited verifier or synchronous interpreter completes. Preserve callback order, last-outcome fallback, zero/one marker observations and absence of partial return/publication on failure. Keep actual runtime array subjects and live scalar observer counters. Validate all four affected classes after review/formatting. Status: planned before edits.

IMM-M183 verified: actual dependency audit passed all 51 projects using one approved source. The clean-clone native campaign at 421135a91c738eca0f8cea95e344605a00ca2c47 passed seed17 and seed23063 (one case each), both runner/validator receipts and aggregate requestedCases=2,totalCases=2,passed=true; four build/rebuild stages had zero warnings/errors. Main and independent review confirm all three internal named immutable callers and uniquely owned readonly process options. The first two setup attempts failed Git ownership trust before any build; container-local explicit safe-directory configuration corrected setup. Combined with all 33 compatibility controls and canonical script formatting, status: implemented and verified.

IMM-M187 verified: seven baseline/current documentation-discovery controls match output/error text, including empty, spaced path, ordered multi-file, duplicate anchors, broken-link ordering, missing-anchor and non-Git cases. Current successful paths retain ImmutableArray snapshots. Actual documentation automation passed drift/regeneration/idempotence/prose/link/anchor regressions; actual local links passed all 715 links. All 87 PowerShell/19 shell files are format-clean. Main reviewed the preserved native Git exit check and order-sensitive pipeline. Status: implemented and verified.

IMM-M188 verified: all 72 tests in the four affected Worker classes passed with zero failures/skips/build warnings/errors. Main and independent review confirm callback order, await/Execute completion before freeze, last-outcome fallback, zero/one marker counts and no partial publication on failure. Actual CLR identities/arrays and live scalar counters remain unchanged. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M189 coverage checkpoint: refreshed all 1,897 tracked/new non-vendored C# units (including seven integral Convert master additions) to 2,237 candidates and all 84 tracked non-vendored PowerShell scripts/modules to 1,093 candidates, with zero parse failures. Coordinator checked 62 C# and 50 PowerShell reviewed rows against builder ownership/freeze, actual mutation witnesses and applicable passing gates. Remaining Open counts: 905 C# and 597 PowerShell. The three non-vendored psd1 files are static PSScriptAnalyzer configuration serialized in the native required array/hashtable data format; pinned eng/common content remains excluded. The syntax scanner misses some target-typed/argument collection forms and semantic aliases; manual source reviews remain required and sampling is not exhaustive coverage. Corruption-fixture assembly and private array/params wrappers remain actionable unless a tested mutation contract or exact native adapter is demonstrated.

IMM-M190: Migrate four private Gates process wrappers and all expanded callers to immutable vectors, retain only the final CLR entrypoint reflection array adapter, and freeze the Git-process census. Change private durable-write/parser cores to ReadOnlyMemory/ReadOnlySpan with original empty/invalid behavior; retain the null-checking byte-array compatibility parser until coupled input fixture migration. Preserve process order, output/cancellation, durable flush lifetime, strict UTF-8/BOM/newline rejection and guard order. Validate the entire ordinary Gates project after review/formatting. Status: planned before edits.

IMM-M191 integration: master edc857df6 adds Stack support (#336) and List insert/range copies (#332), 27 changed files. Resolve the List helper insertion conflict by retaining all new range/copy semantics and the existing immutable helper contract. Convert newly introduced range/Stack private argument paths and Stack field cache to owned immutable builders/snapshots, preserving evaluation/copy/bounds/callback order. Verify all auto-merges and run full ordinary integration after formatting. M190 Gates test snapshot predates this merge and is recorded separately. Status: planned before resolution edits.

IMM-M192: Migrate five newly merged Stack/List replay input maps (including one empty map) and the SMT predicate vector to immutable construction/snapshots. Preserve opaque atom identities, input ordinal/indexer semantics, actual Stack/List/array mutation subjects and native reflection adapters. Reuse named immutable member APIs and immutable empty preprocessor vectors. Include these new master scopes in the pending full M191 integration gate after review/formatting. Status: planned before edits.

IMM-M193: Freeze shared GoldenTest file/source byte vectors at native reads and keep them immutable through GoldenCase, strict UTF-8 decoding, race comparisons and durable update. Unify private byte comparison paths on nullable initialized immutable vectors, retaining missing-vs-empty distinctions and guard order. Freeze eager directory discovery while preserving lazy name ordering. Validate GoldenHarness update/path/race regressions and all linked ordinary golden owners through the pending full semantic integration gate; package golden owner also requires its package workflow. Status: planned before edits.

IMM-M190 verified: all 474 ordinary Gates tests passed with zero failures/skips/build warnings/errors against the captured pre-Stack/List master snapshot. Main and independent review confirm all 51 expanded wrapper callers, unchanged process/cancellation order, immutable census output, native-only CLR entrypoint adapter, durable empty-file writes and strict parser rejection behavior. Original null-style private wrapper guards are intentionally retained for default immutable vectors. Canonical formatting required zero changes. The combined M191 integration gate remains pending after the new master merge. Status: implemented and verified.

IMM-M191 resolution checkpoint: master edc857df6 merged at 3aa12840c with the sole List helper conflict resolved. Main/independent trace confirms range copy direction, argument evaluation/identity, strict owned Stack cache, exact-capacity constructor freeze and immutable bounds/effect paths. SMT stack-count witness metering retains existing deterministic sidecars. No incoming IR/portable-codec changes or unmerged paths remain. Formatting is clean; 3,890 source literal tokens across 896 changed C# files match edc857df6. Full semantic integration pending.

IMM-M194: Freeze the Tool CLR Main argument vector once, use immutable vectors throughout RunMain/Run/Explain and the private SDK process wrapper, and migrate coupled test callers. Retain actual native Main(string[]) and process ArgumentList/stream read scratch boundaries. Preserve null-style guard order, CLI parsing/list patterns, cancellation exit code, SDK deadlines and command order. This batch follows the already captured M191 full-gate snapshot and requires its own ordinary Tool gate. Status: planned before edits.

IMM-M195: Convert CountUp resource-boundary callback/read/query collectors to owned immutable array builders. Freeze all four snapshots in the outer finally after verifier/session completion or fault, preserving partial evidence JSON publication and caps/counters; use frozen observations for post-run assertions. Freeze source-line lookup inputs and immutable expected status vectors. Keep actual runtime images, callback phase state and native solver behavior. Include this batch and M194 in the full M191 retry snapshot after the initial build's private-API/type-name corrections. Status: planned before edits.

IMM-M196: Refreshed conservative string-constant discovery across all 1,711 current non-subject C# units; three literal source locals remain eligible. Record them before editing, then check both source consumers, absence of reassignment/capture/ref/name/reference-identity-sensitive use, fixed assembly naming and unchanged literal tokens. Convert only these three source declarations and validate the affected Queue/Stack/List opaque replay classes after the already captured M191 snapshot. Numeric and binding-sensitive constants are not inferred safe by this string-only pass. Status: planned before edits.

IMM-M197: Freeze FixedChecksum's nested control vectors, private reference-checksum input and both partial result collectors. Use exact-capacity immutable builders for 20 outer/16 inner controls, keep native reflection argument arrays only at Invoke, and publish immutable partial snapshots at the same per-result points. Replace sixteen per-control immutable map root Add assignments with an owned builder frozen before interpreter use. Preserve singleton empty/partial publication before any fault and all checksum/mask/control order. The outer ordered observation map remains separately Open; this batch does not exempt it. Validate the actual construction-boundary test plus M196 three opaque classes after the current full/package gates. Status: planned before edits.

IMM-M191 full integration checkpoint: the retry passed all 12,266 ordinary Release semantic tests with zero failures/skips/build warnings/errors, 23 framework summaries, six isolated tasks, 32 scheduler slots and 225 exact embedded PDB outputs. Worker 6,045, Frontend 2,105, Effects 122, SMT 387, Analyzer 1,134 and Gates 474 include the new Stack/List master. This snapshot contains M192/M193/M194/M195, before later M196 keyword and M197 fixture edits. The initial attempt failed four build-only integration issues: a non-friend SMT test could not use the internal member snapshot API, and three Worker calls needed qualified OutputKind names. The retry retains the public empty-params compatibility adapter and fixed-name compilation overload, and compiles cleanly. Status: merge integration verified.

IMM-M193 verified: the full M191 gate passes all ordinary linked semantic golden owners, including update/original-source/race/symlink/missing-file controls. All three actual package golden/deadline/cancellation controls also passed in one isolated shard with zero failures/skips/build warnings/errors. Main and independent review confirm immutable native-read snapshots, nullable missing-vs-empty distinction, exact span writes/durable flush and unchanged file validation/publication/cleanup order. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M194 verified: all 41 ordinary Tool tests passed in the M191 retry (which includes M194), with zero failures/skips/build warnings/errors. Main and independent review confirm the sole native Main snapshot, all internal callers, list-pattern/option behavior, null-style guard order, cancellation exit130, bounded SDK output/deadlines and process cleanup. Native argument-list and stream scratch adapters remain intentional. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M195 verified: the actual CountUp construction/resource test passes within the 6,045-test Worker partition of the M191 retry, with zero framework failures/skips/build warnings/errors. Main and independent review confirm serialized callbacks/queries, read/query caps, exact partial-failure publication, four immutable finally snapshots, JSON array shape/order and unchanged source-line/PDB lookups. All post-run assertions consume frozen snapshots; live counters remain mutable SUT state. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M192 verified: the combined M191 retry passed all 122 Effects, 387 SMT and 6,045 Worker tests, including the new Stack/List replay maps and predicate vector. The final M196/M197 gate passed 41 affected Worker tests with zero failures/skips/build warnings/errors. Main review confirms fixed assembly names, opaque identities, indexer overwrite semantics and immutable inputs. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M196 verified: all three conservative source literal locals now use const string. Main and independent binding review found unchanged consumers, literals and assembly naming; all 41 affected Worker controls passed with zero failures/skips/build warnings/errors. Status: implemented and verified.

IMM-M197 verified: the actual checksum construction-boundary test and three opaque replay classes passed all 41 tests with zero failures/skips/build warnings/errors. Main and independent review confirm exact 20-by-16 control construction, builder ownership, per-result partial snapshots and native-only reflection adapters. The outer ordered observation map remains Open. Canonical formatting required zero changes. Status: implemented and verified.

IMM-M198: Integrate master deaa0a57c typed Array.Clear. Resolve the sole passive-builder field conflict by preserving existing immutable collectors and adding an owned immutable range-guard builder. Freeze ordinal-indexed argument construction in the new Array.Clear lowerer without changing source evaluation order, range checks, exception fallback or element-store semantics. Review auto-merges and new fixtures, then run IR, Frontend, Effects and affected Worker integration. Status: planned before lowering edits.

IMM-M199: Keep portable decoding term/variable vectors immutable through projection callbacks and named internal IR snapshot APIs. Preserve public params compatibility guards, member/call validation, havoc deduplication/order, recursive decode/cancellation order and canonical wire bytes, including incoming range writes. This batch covers decoder paths; encoder callback and DTO-constructor arrays remain separately Open. Validate full IR and portable Worker regression controls after main/independent review. Status: planned before edits.

IMM-M198 initial build checkpoint: four incoming portable range corruption assignments targeted properties made init-only earlier in this branch. Replaced those assignments with the existing immutable ReplaceRow/with helper, preserving every corruption shape. Initial build had four errors and zero warnings; retry is required. The decoder M199 batch is included in the next combined snapshot and will be reported separately.

IMM-M200: Replace portable encoder projection/result arrays with immutable vectors and direct immutable record initialization. Keep constructor compatibility adapters separately tracked; preserve canonical item order, sorting, nullable missing-item normalization, cancellation and recursive metadata-index evaluation. Freeze block instruction and member parameter projections without intermediate arrays. Verify portable golden/roundtrip and full IR after decoder checkpoint. Status: planned before edits.

IMM-M201: Migrate newly merged Array.Clear replay maps and fixed outer vectors in four fixture files. Preserve actual CLR arrays cleared by the subject, alias/reference identities, null replay-input behavior and corruption cases. Use immutable builders for multi-entry maps and direct singleton immutable maps without root rebinding; freeze range-program enumeration and static input constants. Validate all new range/clear controls after full M198/M199 checkpoint. Status: planned before edits.

IMM-M202: Replace PortableGraphChanges repeated immutable SetItem root assignment with a lazily allocated owned immutable-array builder. Preserve reference-based replacement of every alias, recursive block/location/span handling, original array identity when unchanged and default-array behavior. Freeze once after traversal. Validate portable malformed-wire/alias controls in the next Worker gate. Status: planned before edits.

IMM-M203 coverage checkpoint: refreshed 1,917 C# files to 2,252 syntax candidates and all 84 PowerShell scripts to 1,093 candidates with zero PS parse failures. Main verified ten shared-helper/Tool/oracle rows against source: six owned immutable/text builders, native Main and read scratch, one reflection ref-out destination and one nested CLR alias subject. Private params/reflection wrappers, read-only byte adapters and corruption-construction helpers remain actionable rather than receiving blanket native/SUT exemptions. Current coordinate Open counts after these ten classifications: 955 C#, 597 PowerShell; later M200-M202 edits await coordinate refresh. Status: checkpoint recorded, exhaustive work continues.

IMM-M203 reviewed ownership reconciliation: main traced another 22 current-coordinate rows to M195 finally snapshots, M197 exact-capacity/control/result freezes and GoldenWorker method-owned ToString publication. Main rejects classifying launcher RunMain private vectors, byte-stream convenience adapters and private reflection wrappers as unavoidable native contracts. Open C# count at this inventory checkpoint is now 933; PowerShell remains 597. Later source edits still require refresh.

IMM-M204: Complete remaining Effects private comparison/input wrappers: immutable generic edge vectors, strict dictionary parameter capture and immutable incoming List replay map. Preserve actual CLR List/Dictionary/Stack/Array.Clear subjects, comparison order and null guards. Exercise the existing empty region-set params overload without an explicit mutable empty vector. Run the entire ordinary Effects inventory after formatting/review. Status: planned before edits.

IMM-M198/M199 verified: the combined retry passed all 12,351 ordinary Release semantic tests with zero failures/skips/build warnings/errors across 23 framework summaries and six isolated tasks. It includes master deaa0a57c, immutable Array.Clear lowering, all corrected malformed range fixtures and decoder snapshot APIs. Later M200-M202/M204 changes are outside this snapshot and have separate gates pending. Main/independent reviews found no remaining merge defect.

IMM-M203 coordinate reconciliation: main rechecked 42 shifted prior rows by exact path, owner, kind and detail against committed reviewed evidence. Existing owned builders/native reflection adapters retain their classifications after merged ownership review; new range state is separately reviewed, not inferred by proximity. Inventory checkpoint now has 891 Open C# and 597 Open PowerShell candidates. M200-M202/M204 source changes await refresh.

IMM-M205: Replace the shared Analyzer tuple params constructor with an immutable vector and strict owned builder, rewriting all 23 tuple callers while preserving seven map/comparer overloads. Replace ProcessRunner's private params path and three callers with immutable vectors; freeze both NaN command declarations and preserve their evidence JSON. Retain native ProcessStartInfo.ArgumentList and cancellation/termination behavior. Validate Analyzer/Gates configuration controls, actual child termination and package NaN workflow after checked AST caller rewrite. Status: planned before edits.

IMM-M200-M204 partial gate: all 500 ordinary IR and 123 Effects tests passed with zero failures/skips/build warnings/errors. Worker build rejected two encoder initializer formatting locations (IDE0055) despite the folder formatter reporting no patch; split those initializers into conventional multiline form before retry. No semantic test failure was reported; Worker/codec verification remains pending.

IMM-M205 first build checkpoint: removing params exposes CA1068's cancellation-token-last rule. Moved the token to the final position in the private immutable overload and all three callers, retaining in-method guard order and command contents. Worker retry is required; later serialized gates did not start after the failed build. Encoder formatting now compiles cleanly.

IMM-M203 native lifecycle reconciliation: main traced seven reproducibility rows to ProcessStartInfo.ArgumentList.Add and Environment set/remove, all exact framework construction APIs. Retain those seven native writes; private argument vectors, maps and report records remain Open. PowerShell Open count is now 590 at the current checkpoint.

IMM-M198 layout/literal checkpoint: canonical layout passed 2,659 sources, 59 projects and all 151 pinned common files. All 3,890 raw/interpolated source literal tokens across 917 changed C# files match merged master deaa0a57c. No literal subjects or pinned Arcade common files were changed.

IMM-M205 Worker checkpoint: 339 portable/range/compiler and actual child-termination controls passed with zero failures/skips/build warnings/errors. The subsequent Analyzer build requires an explicit constructor-body default guard (SPINT001) even though Capture already rejects defaults in the initializer; retain both guards to satisfy that repository contract without changing successful behavior. Analyzer/Gates/package retry pending.

IMM-M206: Freeze reproducibility native receipts, representative/packed hash maps and discovered package files with owned immutable builders. Retain Ordinal sorted keys, strict duplicate detection after hashing, public package identity's object-array binder adapter, native stream/hash lifetimes and readonly ordered nested records. Root report/run/command construction remains separately actionable for M207; six before/current controls are prepared before edits, with packed/failure/report controls and actual exact-commit workflow still required.

IMM-M207: Finish reproducibility phase construction with immutable clone/argv/run/key/mismatch/comparison vectors and readonly ordered nested/root publication. Preserve Sort-Object uniqueness/culture, native argument order, partial report timing, JSON property/array shapes and Representative null package result. Private command inputs migrate with all five coupled call paths; final package identity binder adapter remains. Require before/current helpers, packed/partial report controls, canonical formatting and actual clean exact-commit Representative reproduction before claiming verified.

IMM-M199-M205 verified: main/independent reviews and canonical checks pass 500 IR, 123 Effects, 339 portable/range/compiler/actual-child-termination Worker controls, all 1,134 Analyzer tests, seven configuration/comparer contracts and the actual NaN public-package consumer workflow (one isolated shard). All completed gates report zero failures/skips/build warnings/errors. The explicit constructor-body guard was added after the Worker snapshot to satisfy SPINT001 and is verified by Analyzer/Gates/package builds. Encoder initializer formatting and token-last compliance are corrected; the final canonical format patch was applied. M206/M207 reproducibility source changes remain pending separate verification and are not included in this completion claim.

IMM-M206/M207 control checkpoint: all 28 baseline/current controls pass: nine helper/native argv cases (including null/default/initialized-empty arguments), eight packed payload/identity-binder/strict-duplicate cases and eleven full report/partial-failure scenarios. Root/nested reports and packed maps reject writes. Main corrected SortedDictionary's duplicate message to retain the key/value pair text, preserved legacy null argv via a nullable immutable vector and direct empty-snapshot branches, and replaced key union arrays with lazy Concat while retaining Sort-Object semantics. All 87 PowerShell/19 shell scripts are format-clean. Actual clean exact-commit Representative reproduction remains pending; the Full archive branch has fixture controls, not a claimed full pack reproduction.

IMM-M206/M207 workflow verified: actual Representative reproduction at clean commit 2feb8830624b9cde592e3464421fdb203b80e9ab passed two independent checkout paths, all six managed DLL/PDB outputs byte-identical, zero payload mismatches and 15 unchanged native inputs. All eight restore/build sequences completed; eight build logs report zero warnings/errors. Report preserves null packageBytesEqual and empty archiveComparisons for Representative scope. The Full branch remains fixture-verified rather than a claimed actual full archive reproduction.

IMM-M208 inventory checkpoint: refreshed all 1,917 C# units (2,244 syntax candidates) and 84 PowerShell scripts (1,085 candidates, zero parser failures). Main reviewed 28 shifted C# classifications, fifteen frozen builder rows, eleven actual mutable subjects, one final reflection adapter and seven existing public IR compatibility adapters. All 28 reproducibility rows are classified against the actual controls/workflow. Open counts at this checkpoint: 841 C#, 565 PowerShell. Five nullable-array DTO constructor inputs were found outside the current scanner's nullable-type handling and remain actionable; the next scanner expansion will record that gap explicitly.

IMM-M209: Expand syntax discovery to unwrap nullable array types across fields, properties, returns, local storage/casts and parameter inputs, and include mutable collection/interface/builder method and constructor parameter types already covered in stored types. This records previously missed inputs rather than treating absence as verification. Preserve prior exact-coordinate statuses, inspect all new groups and keep data/fixture/native exceptions evidence-backed. Status: planned before scanner edits.

IMM-M209 discovery checkpoint: expanded scanner covers all 1,917 current C# units and records 2,531 candidates, adding 287 rows: 278 parameter inputs, six nullable-array storage sites, two casts and one property. Two additions are excluded actual program subjects; 285 additions require review, bringing the C# Open count to 1,126. PowerShell remains 565 Open across 84 parsed scripts with zero parse failures. Exact five private portable constructor violations are RES-6E89004DD2081C3E, RES-693F0B41076236D9, RES-7A15AA47CA74AB78, RES-BE494C0F28DE7365 and RES-E043C9160D0C7625. Ten read-only reviewers have concrete partitions/quality checks; only the coordinator edits files. Status: expanded inventory committed before source fixes.

IMM-M210: Replace five private portable wire-record constructor array inputs with immutable vectors. Preserve omitted/default normalization to initialized empty metadata and leave parameterless JSON constructors plus explicit-null property/converter semantics unchanged. Remove the sole explicit null block-constructor argument; collection-expression fixture callers retarget immutably. Trace all five source owners, preserve canonical bytes and malformed-wire cases, then validate the full combined semantic suite after formatting/review. Status: planned before edits.

IMM-M211: Convert Native construction parameter and verifier callback/result collectors to owned immutable builders. Freeze callback snapshots after canceled verifier completion/ThrowsAsync and before assertions, retaining partial order, cancellation timing and resource counters. Narrow PortableGraphChanges.FindIndex to immutable rows, preserving first-match/default guard ordering across all graph callers. Validate affected construction/verifier/portable/alias classes after the already captured M210 full snapshot. Status: planned before edits.

IMM-M212: Narrow six private Contracts/Frontend input contracts to the immutable values already supplied by every caller. Preserve companion SymbolEqualityComparer maps and optional-null index behavior, source clause guard precedence, reversed finally-chain traversal and exception/unwind ordering. Keep public IrSubstitution caller-view snapshot semantics unchanged. Validate Contracts/Frontend and affected Worker companion/filter controls after M210's captured full snapshot. Status: planned before edits.


IMM-M214: Replace the remaining Analyzer validation List sink adapters with diagnostic callbacks and convert their two method-owned test collectors to immutable builders. Preserve diagnostics parameter names, null guard order and ReportRelationship/ReportValidation behavior. Narrow the Analyzer nesting assertion to its actual immutable result vector with an initialized-value guard. Validate Analyzer and the direct Worker companion null/relationship controls. Status: planned before edits.

IMM-M215 ownership checkpoint: main rechecked six Fuzz/Tool rows against source and independent audit: four scoped text/identity memo builders, one frozen Tool explanation builder and one generated long[] CLR invocation subject. Private outer read-only inputs remain actionable. Open C# count is 1,120 at the current coordinate checkpoint; PowerShell remains 565. No blanket exemption for read-only interfaces.

IMM-M213: Narrow private Worker proof-core, assumption-map, launcher and completion projections to their already-frozen immutable caller values. Retain map comparers, lazy projection early-return behavior, guard order and launcher text. Freeze once at existing owners rather than recopies or immutable-root accumulation. Validate affected Worker and launcher/package workflow tests. Status: planned before edits.

IMM-M210 verified: all 12,351 ordinary Release semantic tests pass across 23 framework summaries and six isolated tasks, with zero failures/skips/build warnings/errors. Snapshot includes HEAD 06be7541b plus the five constructor changes and block caller cleanup only; later M211-M214 source batches remain pending separate gates. Canonical formatter produced no changes. Main and independent source review confirmed JSON omitted/default/null behavior and immutable caller compatibility.

IMM-M216: Freeze partial-read observation history behind an owned immutable builder while retaining actual FillExactly destination buffers and final MemoryStream byte adapters. Replace fatal/timeout test Array.Reverse scratch vectors with immutable snapshots and conditional reversed snapshots, preserving input row order and result classification. Validate compiler stream-fill and fatal/timeout assembly controls. Status: planned before edits.

IMM-M217: Narrow seven coupled private Specs/SMT helper maps to the immutable values supplied by every caller: runtime binding evaluation, validated digest terms, native core active indices and reference-model assertions. Preserve native unsat-core disposal in finally, map comparers, recursive evaluation order and null guards. Validate ordinary Specs/SMT and native core disposal controls. Status: planned before edits.

IMM-M214 first build: the new Worker test builder lacked its explicit System.Collections.Immutable import. Build reported two CS0103 sites and no warnings; no tests ran. Added the import before retry. M216 and M217 source changes are included in the next combined retry snapshot.

IMM-M218: Extend discovery to syntactic self-rebinding Add/AddUnique/AddRange/SetItem/SetItems/Remove/RemoveRange/Clear updates, including persistent immutable construction previously absent from the mutability scanner. Record candidates before builder migration; classify required persistent snapshots separately from avoidable builder accumulation. This is syntax coverage, not semantic proof. Preserve existing exact-coordinate classifications and explicitly reconcile shifted rows against source. Status: planned before scanner edits.

IMM-M218 discovery checkpoint: all 1,917 C# units now produce 2656 syntax candidates, including 156 syntactic self-rebinding collection updates absent from previous scans. Current Open count is 1269 C# after source coordinate changes; shifted prior classifications await explicit reconciliation. PowerShell remains 565 Open across 84 scripts with zero parser failures. Persistent snapshot requirements will be traced; owned accumulators migrate to builders. Status: violations recorded before builder edits.

IMM-M219: Replace one-shot Horn Search cache roots and recursive application/constant accumulators with owned immutable builders. Keep ordinal comparers, strict AddUnique caches, overwrite semantics for state names/conversions, first-discovery quantifier/registration order and native Expr identity. Freeze local recursive outputs once before native calls; no search builder escapes. Validate Horn invariant/native SMT workflows and retained resource/arity failures. Status: planned before edits.

IMM-M220: Replace BvEncoder persistent-root update construction with session-owned immutable builders, retained across queries under the existing serialized runner. Keep immutable ReferenceFacts/Relations/RelationDeclarations snapshot properties, native wrapper identity, strict cache duplicate checks and first-encounter declaration/disposal order. Snapshot ordered traversals where encoding can append. Field observations use nested owned builders with indexed overwrite, and array observations freeze both levels once at return. Do not clear builders per query. Validate native model/string/array/field/Horn and cross-query solver behavior. Status: planned before edits.

IMM-M222 ownership checkpoint: main reviewed the complete CompilerIlStreamFillTests fixture and traced fourteen native byte-array/Stream contracts and actual mutable destination/sentinel/cancellation subjects. Read-only MemoryStream inputs remain final framework adapters; the avoidable observation List was migrated in M216 and awaits its gate. Source ownership matches the independent review.

IMM-M222 PowerShell reconciliation: main traced eighteen string/scalar builder and immutable ID-vector rows through semantic argv/duration/discovery/failure handling, TCB duplicate guards, JSON duplicate guards and third-party package-ID comparisons. Preserve ordinal comparers and sorted string snapshots. Eight proposed private argv adapter exemptions were not accepted wholesale; mutable task/report payloads and helper boundaries remain actionable. PowerShell Open count is now 547.

IMM-M223: Replace local Dataflow fixpoint roots, generated-domain vectors and CodeFix callback roots with owned immutable builders. Preserve seed-dependent append/self-index order, block update order, convergence behavior and callback registration order. Freeze only at result publication; exact initial vector seeding preserves positional initialization. Validate ordinary Dataflow and CodeFix tests after formatting/review. Status: planned before edits.

IMM-M224: Convert thirteen gate-protected private IrFactory interning/cache collections to owned immutable builders, preserving structural/ordinal/custom comparers, ID count/index order and existing lock boundaries. Keep external identity bucket entry vectors immutable because comparator callbacks run outside the gate. Narrow GetScoped to its actual owned immutable-list builder callers. Validate IR interning, concurrent external identity and full downstream semantic integration. Status: planned before edits.

IMM-M225: Complete owned SMT accumulation with three serialized solver selector-cache builders, reverse-disposal expression owner builder and one shared depth-validation builder across assumptions/goal. Freeze the nominal-retry test initial assumptions after optional assembly while preserving its separate alias-derived immutable branch. Preserve count-based selector names, reverse native disposal, cancellation/resource charges and depth limits. Validate all ordinary SMT and downstream native solver workflows. Status: planned before edits.

IMM-M226: Replace protocol validation/projection self-rebinding roots with owned immutable builders. Preserve first-owned claim IDs, first-wins identity indexes, ordinal comparers, conflict/duplicate scan order and error order. Freeze each validator Result and retained identity/projection map at publication so earlier results remain detached. Validate protocol malformed-wire/cancellation/projection controls and downstream Worker/package consumers. Status: planned before edits.

IMM-M211-M217 verified checkpoint: combined ordinary Release retry passes all 12,351 tests across 23 framework summaries and six isolated tasks, with zero failures/skips/build warnings/errors. Includes M211 collectors/graph search, M212 Contracts/Frontend inputs, M213 Worker maps/projections, M214 Analyzer callback/nesting helpers, M216 stream history/fatal-timeout immutable assembly and seven M217 Specs/SMT map narrowings. The missing M214 import is fixed. Snapshot predates M219/M220/M223-M226 builder changes; those remain separately gated. Main and independent reviews agree on ownership/order/guard behavior.

IMM-M222 Frontend/Contracts ownership reconciliation: main traced 36 owned builder/cache/recursive guard rows and five exact native/alias subjects against source and independent evidence. Includes two companion declaration/materialization rows for the same private owned field. Reconciled current coordinates by exact IDs first, otherwise unique exact path/owner/kind/detail. Private byte/reflection wrappers and persistent-root updates remain actionable. Current coordinate C# Open count is 1,214; PowerShell is 547.

IMM-M219-M226 first build: canonical analyzers rejected two HashSet-builder Contains/Add pairs (CA1868) in protocol owned-claim collection and encoded array-access tracking. Replaced each with the boolean Add result, preserving first-seen ordered append behavior. Build had two errors, zero warnings; no tests ran. Retry includes these fixes.

IMM-M227: Replace IR snapshot-selection fixture map/leaf accumulation and wrong-field-type input assembly with owned immutable builders. Freeze input maps before interpreter calls, freeze each binary-tree level, and retain actual mutable IrHeap subjects, source variable identity and ordered tree shape. Preserve separate valid/malformed immutable input snapshots rather than turning those controls into shared construction state. Validate ordinary IR after review/formatting. Status: planned before edits.

IMM-M228: Replace callable replay final-state and approximation-variable root accumulation with owned builders, preserving the original immutable model for pre-state lookup. Freeze final and approximation snapshots before domain checks/read-observer/interpreter evaluation. Narrow entry-constraint inputs to actual immutable callers. Use a local unsat-core hash builder with boolean Add to retain first-occurrence justification order. Validate replay/approximation/old-state/native proof controls after review. Status: planned before edits.

IMM-M229: Replace Fuzz structural-size visited roots and three source-oracle scenario/binding accumulators with owned immutable builders. Preserve DFS order, variable identity, existing immutable-map Add semantics and native reflection argument subjects. Freeze captured bindings after both sequential lowering calls and before all interpreter/oracle consumers. Validate ordinary Fuzz controls after review/formatting. Status: planned before edits.

IMM-M230: Finish owned IR construction roots in program drafts, sequence writer/reader discovery, traversal visited/variable sets and substitution replacement/argument assembly. Keep public caller-view snapshot validation order, strict replacement duplicate checks and source enumeration order. Clone opaque argument vectors lazily at the first changed element and freeze once; preserve unchanged term identity. Program/block drafts freeze before publication. Keep persistent fresh-set iteration snapshots and callback memo snapshots. Validate ordinary IR plus downstream lowerer/replay consumers. Status: planned before edits.

IMM-M231: Replace Worker IL-decoder, typed integer and deep-graph fixture root accumulation with owned immutable builders. Preserve instruction/root order, width and signedness, null placeholder counts and cyclic/noncyclic terminal links; freeze indexed vectors after construction. Retain actual runtime array subjects. Validate affected Worker codec/compiler controls. Status: planned before edits.

IMM-M219-M226 verified: canonical retry passes all 12,351 ordinary Release semantic tests across 23 framework summaries and six isolated tasks, with zero failures/skips/build warnings/errors. Includes M217 final helper maps, Horn/encoder/session/expression-owner builders, shared depth construction, Dataflow/CodeFix collectors, gate-protected IrFactory builders and protocol validation/projection snapshots. Both CA1868 pairs use boolean Add. Main and independent reviews found no semantic deviation. Later M227-M231 source changes are outside this captured snapshot and require their own gates.

IMM-M232: Replace Frontend stack-shape, cancellation-token, effect-region and purity callback construction roots with owned immutable builders. Publish each out-shape snapshot at the same indexed update point as before, preserving partial failure and spend-callback observations. Retain immutable worklists, token/region order, duplicate additions and nested reentrant callback order; freeze before consumers. Validate Frontend/Defensive/Effects and affected Worker IL controls. Status: planned before edits.

IMM-M233: Replace allocation initializer, publication lease acquisition and container required-property construction with owned immutable builders. Retain initializer order and mutable replay heap; freeze acquired streams before publishing the lease and dispose partial acquisitions in the original acquisition order on failure. Retain required-property original immutable encounter order for missing-name diagnostics while removing through a membership builder. Validate IR allocation, publication process/cancellation and container contract controls. Status: planned before edits.

IMM-M234: Refresh complete syntax inventories after builder batches, then reconcile classifications only against exact unchanged identities or coordinator-reviewed ownership/freeze evidence. The separate lexical review covers 1,917 C# units and 242 immutable-stack/queue advancements; retain these immutable worklists because their APIs have no direct builders and progression is required algorithm state. String transformations and immutable corruption/snapshot derivations are reviewed separately. Status: planned before refresh; syntax absence is not semantic proof.

IMM-M233 first build: CA1849 rejected direct Stream.Dispose calls inside the async acquisition catch. Freeze the partial builder into the still-private lease and use its existing synchronous Dispose path, preserving the original cleanup API, forward order and empty-after-dispose state. Build had one error, zero warnings; no tests ran. Retry required.

IMM-M235: Finish remaining Worker self-rebinding construction in conditional metadata references, golden/policy parameter and exception inputs, and CLR oracle observations. Snapshot references before each producer/prior/caller compilation, preserve shared static reference roots and ordering, freeze per-alias policy data before native checks, and retain actual mutable CLR static state/arguments. Validate golden package controls and affected Worker conditional/alias/exception/CLR oracle classes. Status: planned before edits.

IMM-M236: Replace per-execution havoc occurrence roots and optional receiver fixture assembly with owned immutable builders, retaining prior occurrence numbers, mutable execution lifecycle and immutable interpreter inputs. Merge partial-method declaration references through an owned builder and freeze before target filtering, preserving definition order and first-seen method IDs. Stable replay-value and callback memo snapshots remain persistent. Validate IR havoc/read-order and corpus callable-target workflows. Status: planned before edits.

IMM-M234 first reconciliation: refreshed 1,917 C# units to 2,690 candidates and all 84 PowerShell scripts to 1,085 candidates with zero parser failures. Historical self-rebinding rows decreased from 156 to 40 at refresh; subsequent M235/M236 edits await the next refresh. Main traced 139 builder materialization/storage/input rows across the verified M219-M226 owners and same-owner native/domain fixtures, including explicit freezes, exact-capacity nominal masks and ordered disposal. C# Open count is now 1,144; PowerShell remains 547. These counts are coordinate checkpoints, not an exhaustive completion claim.

IMM-M227-M236 verified: all 12,351 ordinary Release semantic tests pass across 23 framework summaries and six isolated tasks, with zero failures/skips/build warnings/errors. Includes IR draft/traversal/substitution and execution builders, replay/proof-core snapshots, Fuzz binding/size construction, Frontend partial shape publication, cancellation/effect callbacks, Worker IL/graph/reference/policy/oracle fixtures, publication lease cleanup and corpus partial-method reference assembly. Main and independent review preserved alias, order, failure and native mutable-subject behavior. Formatter is clean. GitHub reports the pushed parent 415966f4d mergeable; no local conflict state. M234 inventory reconciliation remains incomplete.

IMM-M237: Narrow four private Gates input contracts to their existing immutable callers. Preserve default/null guard positions, async transaction rollback and duplicate validation, ordinal observation lookup, native process argv order and diagnostic assignment errors. Freeze at callers rather than recopies. Validate corpus workflow controls.

IMM-M238: Keep two SMT relation/state argument vectors immutable throughout managed sort/name processing; materialize arrays only in actual Z3 MkApp/SubstituteVars calls. Preserve projection guards, ordinal names, arity failures and native ownership/meter order. Public Horn custom-comparer input semantics remain separately actionable.

IMM-M239: Narrow nine Fuzz private replay/selection/coverage inputs to immutable arrays supplied by all callers. Preserve record-clone and nested native argument identity, positional alignment, guards before length access, 64-failure cap, native invocation adapters and timing/cancellation order. Validate ordinary Fuzz and source-oracle controls.

IMM-M240: Narrow four internal CompilerCollector metadata/tree/reporting inputs to immutable vectors, preserving capture-cache ImmutableList identity and adapting only the two boundary callers. Keep null/absence guard precedence, stable representative selection, line-map JSON bytes, remembered-tree fast path and cancellation/ambiguity ordering. Validate reporting, capture and reference-module controls.

IMM-M241: Replace Coverage authority/discovery maps and sets with ordinal immutable builders and freeze completed maps. Nested project/compile key lists use owned immutable-array builders and freeze both levels after document processing. Preserve strict duplicate diagnostics, duplicate ordered keys, report/PDB ownership and original test block anchors. Canonical binding/duplicate probe passed before edits; saved original script for before/current controls. Remaining JSON records and hit/report/changed-TCB construction are separate work.

IMM-M240 first build: CA2009 rejected a redundant ToImmutableArray on the newly immutable metadata input (same diagnostic emitted twice, zero warnings). Return the initialized immutable vector directly. No tests ran; combined retry required.

IMM-M242: Replace private IR replay value/approximation roots with owned builders while retaining immutable Current, approximation and result snapshot properties at every existing consumer. Use a separate immutable previous-iteration snapshot for sequence freshness while live membership uses an owned builder. Narrow three internal argument-shape/operand inputs; preserve receiver validation precedence and argument order. Public semantic Conjoin/Disjoin snapshot caller views once before private immutable recursion. Validate IR and downstream replay/solver controls.

IMM-M243: Convert Coverage line-hit, direct-hit, range, credit and report accumulators to owned immutable builders. Retain the expectedLineHits/lineHits shared builder through the original report-merge window, then freeze nested snapshots. Preserve maximum repeated hits, overlapping credit order, empty-credit semantics, source cache ordinal comparison and readonly range/report record property order. Compare original/current authenticated accumulator controls before full coverage workflow.

IMM-M244 ownership checkpoint: coordinator re-read and reconciled 49 exact owned builder, native I/O/Roslyn adapter, decoder, adjacency and mutable heap rows against independent evidence. Builder freezes, comparers, strict duplicates and cancellation/publication were traced. Corrected mismatched reviewer Host/Protocol IDs against the isolated inventory before applying any decision. Private read-only inputs and serializer byte-array returns remain actionable. Current coordinate Open count is 1,081 C# and 547 PowerShell; no exhaustive completion claim.

IMM-M245: Replace BulkCore fixture parent/diagnostic collectors with owned immutable builders and freeze before assertions/results. Publish emitted image as immutable bytes via stream span rather than mutable array. Preserve embedded mutable List subjects and their source text. Convert fixed Dataflow oracle vectors/carriers/samples to immutable arrays without changing seed/order or mutable Join instrumentation. Validate targeted Worker/Dataflow controls.

IMM-M246: Narrow all seventeen private Dataflow test-helper input vectors to immutable arrays; preserve sample/tuple delegate identity, seeded Pick order, nonempty assertions, null/default guards and independent finite-CFG solver order. M245 already converts the two prerequisite fixtures. Validate generated domain and concrete-oracle suites.

IMM-M247: Remove unused internal EffectSummary params overload; retain fixed two/three argument joins. Convert private analyzer namespace and companion-diagnostic argument wrappers to immutable vectors, adapting only at Diagnostic.Create. Keep eight reverse namespace walks and five diagnostic call orders. Use Roslyn Construct immutable type arguments directly, preserving specialization guards and symbol comparer.

IMM-M248: Keep Horn recursive relation, operand and arithmetic projections immutable through managed sorting, name/cache construction and indexing. Reuse the immutable operand vector in retained application metadata; adapt arrays only at Z3 MkApp/MkAdd/MkSub/MkMul calls. Preserve native expression identity, conversion/ownership order and zero-extension behavior.

IMM-M249: Replace changed-TCB source/line/classification and diagnostic collectors with scoped immutable builders. Freeze changed paths after discovery, line sets after patch parsing, declaration intersections after all parse contexts and result vectors after accounting. Preserve canonical filtering, duplicate collapse, intersection/ExceptWith behavior, native argv/script adapters and final diagnostic sorting.

IMM-M250: Narrow nine ContractForGenerator test option maps to immutable dictionaries. Preserve OrdinalIgnoreCase/Ordinal comparers, duplicate-key indexer overwrite and null fallback semantics via owned builders and the existing immutable provider overload. Freeze two fixed string expectation vectors. Params source wrappers remain separate coordinated work.

IMM-M251: Replace all four private generator-test source params arrays with immutable tuple vectors. Coordinator-owned Roslyn rewrite wrapped all 25 test call argument groups, preserving syntax and every string/character token; private reference compilation also receives an immutable vector. Preserve source order, null/default guards and captured reference roots.

IMM-M252: Narrow CompilerTotalIlBodyProvider optional reference ownership to nullable immutable arrays. Preserve null lazy-capture sentinel, initialized-empty authoritative references and compilation guard precedence; reject present-default explicitly. Adapt the still-interface lowerer at the boundary and narrow three test reference helpers, preserving reference selection/decoder controls.

IMM-M237-M243 verified: combined canonical Release retry passes all 12,351 ordinary semantic tests across 23 framework summaries and six isolated tasks with zero failures/skips/build warnings/errors. Covers Gates immutable inputs, SMT argument snapshots, Fuzz private input vectors, CompilerCollector metadata/tree/reporting vectors, IR replay/freshness/argument builders and Coverage authority/hit/range/report builders. CA2009 redundant conversion is fixed; public params-array guards restored during independent review before retry. Later M245-M252 source and M249 changed-TCB edits require their separate gate. Package checkpoint f744cbc0e passed 383 tests plus one expected unsupported-host skip across39shards. Literal check verifies4,062 raw/interpolated text tokens across929changedC#files.

IMM-M245-M252 first build: pinned Roslyn has no one-argument ImmutableArray Construct overload (CS1503 plus propagated MSB4181; zero warnings). Existing immutable overload examples supply a separate nullable-annotation vector. Preserve the original one-argument nullability semantics using arguments.ToArray only at the final Roslyn Construct adapter, retaining immutable managed specialization processing. No tests ran; retry required.

IMM-M253: Keep protocol serializer UTF8 buffers within their owning hash operation. Replace the private mutable-byte-array return with ComputeBoundedUtf8Hash returning the immutable hash string. Preserve request null guard, JSON serialization/size validation, exact hash input and error order; serializer/hash array adapters remain local with no extra copy.

IMM-M254: Freeze owned Coverage query, measurement, project, aggregate, changed-TCB and summary record literals as read-only ordered records, preserving exact property order and JSON keys. Deserialized authority/baseline input records and vector output contracts remain separate work. Coordinator AST rewrite preserves nested literal bodies; compare saved original/current controls and final JSON shapes.

IMM-M255: Freeze two read-only fixture snapshots: repeated-analyzer outcome publication returns ImmutableArray, while mutable RecordCount remains actual instrumentation; runtime type identity field syntax projection becomes an immutable vector. Preserve declaration order, retained initial snapshot comparison and symbol identity.

IMM-M256: Refresh all tracked nonvendored C# and PowerShell syntax units after M237-M255, preserving exact prior row identities only. Reconcile new/shifted construction and native-boundary rows against coordinator source review and gates; separate remaining private input/fixture work. Syntax inventory is not semantic completion.

IMM-M257: Replace private selector/traversal memo root accumulation with owned builders while retaining immutable input/output APIs. Freeze every partial/success return in TrySelector and freeze before each FoldBottomUp combine callback, preserving earlier memo detachment, short-circuit order, cancellation/budget failure and callback-visible snapshots. Validate IR partial-budget/memo/callback and substitution workflows.

IMM-M258: Assemble the two swapped malformed assumption IDs through a local immutable-array builder and freeze once. Keep the original clause snapshot, tuple swap values, case/validation order and deliberately malformed published artifact unchanged. Single-step immutable control/corruption derivations remain legitimate persistent snapshots.

IMM-M256 checkpoint: all 1,917 C# units now yield 2,618 candidates; all 84 PowerShell scripts yield 1,056 candidates with zero parse failures. Recorded self-rebinding collection updates fell from156 to21; M257 replaces six callback/partial memo accumulators and M258 replaces two swap-construction updates after this snapshot. Coordinator traced and classified the remaining thirteen single-step fixture derivations/transactional cleanup roots. Current coordinate Open count after those decisions is1,057 C# and528 PowerShell. Remaining private inputs, fixtures and PowerShell records/vectors are still actionable.

IMM-M249 second gate: build is clean but six DeclarationCoverageAccountingTests contexts rejected an extra assignment to the private qualifiedUnmappedPaths workset; fixture requires one production initialization. This set never publishes and is only consulted by later membership checks after ExceptWith. Retain the construction-owned builder through its private scope, matching other local validation caches, and remove the unnecessary freeze/reassignment. Preserve declaration intersection maps and published immutable results. Retry the exact six controls before the combined gate.

IMM-M259: Freeze Gates fixed boundary/diagnostic vectors and use owned builders for corpus row/field drift assembly and unsupported-report syntax trees. Freeze before text rendering/compilation; preserve duplicate rows, replacement/removal order, exact messages and embedded CLR source subjects. Keep actual mutable JSON DOM/file subjects and resource ownership.

IMM-M260: Replace five package ConsumerProject process argv Lists and the framework SARIF marker collector with owned immutable-array builders. Freeze every argv handoff to RunDotNetAsync and marker assertion; preserve optional switches, duplicate -p arguments and property order, restore lifecycle state and partial resource cleanup. Validate actual package/MSBuild workflows separately.

IMM-M249 repair controls: all six existing classification contexts now match the saved original in twelve native PowerShell runs, including both invalid/valid context orders and empty classifications. Linked-module and changed-line controls also retain exact totals/errors across their before/current runs. PS vector probes confirm Count/Length, empty/singleton/two-item pipeline behavior and nested read-only JSON shape. M253-M260 sources are independently reviewed and formatted; combined gate includes the local-workset repair and all newer sources.

IMM-M256 Protocol/Host closure: coordinator re-read five Stream contract/forwarding boundaries, both final netstandard2 SHA256 bridges and two scoped acquisition/property builders; nine exact rows classified. Protocol/Host/Verify have no Open rows in this coordinate inventory; M253 hash ownership and later source changes still require their pending gates. Global Open count is1,048 C# and528 PowerShell.

IMM-M245-M260 verified: combined canonical Release gate passes 12,351 tests across 23 framework summaries and six isolated tasks, with zero failures/skips/build warnings/errors. Covers all current source edits including the declaration-workset repair, read-only Coverage records, memo callback snapshots and package argv builders. Actual package workflow for this combined source snapshot is running separately.

IMM-M264: Extend PowerShell syntax coverage to direct immutable collection self-rebinding assignments, including SetItem/SetItems and chained Add/Remove updates. Match actual assignment/member ASTs and the same variable path, including script scope and case-insensitive PowerShell names; ignore strings and unrelated receivers. Preserve prior inventory identities/statuses and record newly discovered violations before migration. Inventory tooling is outside the tracked source universe; coordinator is sole writer.

IMM-M260 package build caught five ambiguous AddRange collection-expression seed calls (CS0121, zero warnings); ordinary semantic projects do not compile this package harness. Explicitly target each seed as ImmutableArray<string>, preserving every argv item/order and the builder handoff. No package tests ran; canonical workflow retry required.

IMM-M265: Coordinator re-read eight CompilerCollector/Fuzz rows and independently traced local rank/cycle/binding builder ownership, immutable publication, final netstandard2 stream/hash scratch and immediate reflection adapter. Classified exact IDs after checking current source and callback freeze order. Remaining private input signatures are still actionable. Current C# coordinate Open count is 1,040; PowerShell is 529 after M264 discovered one cache-root update. M264 parsed all 84 scripts with zero failures and passed ten positive/negative assignment-AST controls.

IMM-M266: Coordinator traced and reconciled nine exact ContainerExecution rows: six final ProcessStartInfo argv/environment receiver mutations and three owned policy/build-argument builder mutations. Immutable input closures, helper return vectors and timing record graphs remain separate actionable work; no blanket native exemption. Current coordinate Open count is 1,040 C# and 520 PowerShell.

IMM-M237-M260 package verified: canonical Release workflow passes 383 tests with zero failures and one expected unsupported-host skip across 39 isolated shards. The repaired five explicit immutable seed vectors build with zero warnings/errors. Fresh literal check preserves 4,080 raw/interpolated text tokens across 936 changed C# files; C# formatter has no remaining patch and git diff --check is clean. This checkpoint combines the 12,351 ordinary semantic pass, linked/changed/declaration before-current PowerShell controls, and actual package workflow. Exhaustive migration remains in progress (1,040 C# and 520 PowerShell coordinate Open candidates).

IMM-M268: Replace six Worker fixture construction cohorts with owned immutable builders and immutable image snapshots. Freeze interpreter observer results immediately after execution; freeze serialized callback result/count maps after their awaits; preserve ordinal overwrite and strict ToDictionary duplicate rejection. Nightly option helpers accept the immutable case-insensitive map. Boxing measurements freeze after runtime cleanup and before evidence/assertions; image bytes remain immutable through file/span-hash adapters. Keep actual nested CLR array subjects, thread-safe analyzer exception callbacks and Assembly.Load's direct native byte adapter. Coordinator applied reviewer plan and will independently verify concrete diffs and exact workflow checks.

IMM-M269: Freeze the compiler exception-kind enumeration once as a readonly immutable vector while preserving enum order, duplicate enum values and missing-runtime early returns. Remove the avoidable Z3 MkAnd array adapter using the existing IEnumerable overload already exercised in this encoder. Narrow the Fuzz operator-coverage local helper to its sole caller's owned hash builder; the caller freezes before assertions. Keep native expression ownership/meter order and recursion behavior.

IMM-M271: Narrow mandatory compiler lowerer syntax-tree inputs and optional reference inputs to immutable arrays; keep CaptureTrees' cached ImmutableList identity and adapt its consumer boundary. Preserve cancellation/compilation/target/tree/authority guard order and ArgumentNullException parameter names, null optional-reference absence and initialized-empty authority. Coordinator Roslyn preview checked all 48 static and two explicit constructor sites, converted 38 caller groups across 25 files, and preserved syntax/string/character tokens. Qualified Corpus callers were independently reconciled after initial preview census detected them. Three missing-tree controls now use default immutable vectors; incomplete GetRange fixtures retain their original list/identity semantics. Two metadata fixture helper pairs now accept immutable references, with native codec interface remaining separate.

IMM-M270: Extend PowerShell coverage to eight immutable stack worklist advancements and 26 explicit returned-array syntax nodes, including unary-comma collection-object returns. All 84 scripts parse cleanly; 1,091 total candidates. Coordinator traced the eight Push/Pop updates to import/dependency worklists and preserved their persistent traversal semantics; immutable stacks have no builder API. New return nodes are review candidates, including already-immutable unary-comma snapshots, not automatic violations. Global coordinate Open count is 1,040 C# and 546 PowerShell. Newly discovered return candidates are recorded before migration.

IMM-M272: Replace package weighted scheduler bucket Lists and mutable estimated-time records with owned immutable-array/dictionary builders. Preserve LPT method order and estimate/count/index tie-breaking, duplicates and historical case-insensitive lookup. Publish readonly ordered bucket records with frozen Methods vectors in original index order. Retain current private object[]/hashtable input adapters pending their coordinated caller migration. Compare complete original/current schedule JSON controls before actual package workflow.

IMM-M271 review repair: nullable immutable reference inputs must distinguish null absence from a present-default invalid snapshot even when auto-accessor/empty shadow admission bypasses the provider. Reject present-default references immediately after the existing tree guard in both entrypoints, preserving prior guard precedence and initialized-empty/null behavior. Added two focused invalid-snapshot controls covering both entrypoints; ordinary semantic expected total increases by two. Reviewer census confirms all 50 caller sites and no redundant immutable conversions.

IMM-M273: Coordinator re-read and classified eight exact Frontend test rows whose actual CLR arrays/casts are observed by compound/floating mutation or ReferenceEquals/Is.SameAs assertions. Preserve those subjects and shared/distinct identities; outer argument vectors, retained byte snapshots and private helper inputs remain actionable. Current coordinate Open count is 1,032 C# and 546 PowerShell before current-batch reconciliation.

IMM-M274: Encapsulate CompilerProbe JSON construction in an owned private writer; nested objects receive scoped child writers and only immutable completed strings publish. Remove injected/exposed StringBuilder and mutable-writer callback parameters. Freeze consumed-option rows and option sets before ordered serialization; use a scoped immutable hash builder for reference-identity cycle detection. Immutable portable images stay immutable through ProbeHash.Bytes, with array materialization only inside the netstandard2 hash adapter; UTF8 hash buffers remain local. Preserve JSON property/row order, escaping and exact hash bytes. Save all three original sources for direct before/current serialization controls and actual compiler-probe package tests.

IMM-M274 controls: twelve direct original/current serializer/text/image-hash controls pass byte-for-byte, covering every UTF16 code unit, nested objects, empty/raw/repeated arrays and en-US/tr-TR cultures. Four before/current null/default guard controls also match. Independent review confirms all eleven writer construction sites, nested property ordering and immutable reference-image traversal. Actual compiler-probe package workflow remains pending for the new source batch.

IMM-M275: Coordinator reconciled five return-array scanner false positives: unary-comma PowerShell emission adapters whose published payloads are frozen recursive contract/policy/discovery maps or vectors. Traced each actual freeze/cache value and retained separate input/cache-root work. Current coordinate Open count is 1,032 C# and 541 PowerShell before current-batch reconciliation.

IMM-M276: Coordinator traced six Worker native subjects: legacy stream read buffers and helper forwarding with offset/EOF/slice assertions, covariant Array.Clear mutation and actual CLR allocation-fault creation. Preserve exactly those required mutable operations; source literals and surrounding immutable controls remain unchanged. Current coordinate Open count is 1,026 C# and 541 PowerShell before current-batch reconciliation.

IMM-M277: Completeness gap identified in the old const-local inventory: it covered conservative string literals only, not numeric/character/boolean/null/member constants. Record the concrete unassigned int.MaxValue allocation-oracle length candidate before changing it. A semantic constant/binding audit is required across all tracked nonvendored C# units; overload selection, ref/write/capture behavior and actual compiled subjects must be reviewed. Existing 36 implemented string rows remain valid but do not establish all-constant completion. Current source batch is frozen pending queued gates.

IMM-M278: Authentic all-phase coverage at committed605 collected126 reports (497,802,638 bytes), including passing23 semantic tasks,39 package shards,21 Attributes tests and two Fuzz console cancellation controls. Full validator then rejected two new Compile paths absent from TCB metadata: ImmutableDictionaryCapture and PortableIrArrayJsonConverter. Classify them under the existing irModelCatalog/compilerWireCatalog components; no coverage floor is relaxed. Attempt authenticated report reuse against exact rebuilt605 sources/PDB path with only this metadata repair before deciding whether recollection is needed.

IMM-M268-M274 first combined build: zero warnings, three errors (two CA1308 lowercase normalization calls and one SPINT001 unguarded identity lambda). Preserve lowercase hash bytes using the existing repository-supported Convert.ToHexStringLower API; guard the identity value selector after the already-null-rejecting filter/key selection. No tests ran; retry semantic and actual package workflows after repair.

IMM-M278 authenticated comparison: rebuilt exact605 sources at their original private path with the two metadata classifications. Both full validators accept PDB/module/source authority. Summary JSON is structurally identical except reportFiles ordering; every other value/key/order matches exactly. Both calculate31,927/31,992 lines (99.8%) against the unchanged100% floor, leaving65 uncovered lines to trace.

IMM-M279: Reproduce native PowerShell Sort-Object's missing dictionary-key access on readonly ordered report/bucket records. The original/current scheduler controls fail at17 buckets, exposing nondeterministic tie selection beyond the earlier small controls. Use explicit scriptblock access for report path and bucket Index. Expand scheduler controls to1/2/5/17/24 buckets and verify all63 authentic report records against the saved original summary. Full authenticated current summary must be byte-identical after repair; strict coverage remains separate from semantic equivalence.

IMM-M280: Record all65 authenticated uncovered module/source lines at605 in docs/immutability-coverage-gaps.tsv, with the exact compiled source revision and source text. Scope spans14 projects and31,927/31,992 covered lines. Trace existing controls, unreachable/dead helpers and required malformed-input behavior before changing tests/code; do not relax the100% floor or infer current coverage from a historical checkpoint. Current M268-M279 source has separate in-flight gates and will need a new exact-commit coverage qualification.

IMM-M281: Coordinator traced three strict duplicate capture paths, the ReadOnlyDictionary interpreter snapshot fallback and the one-element native params overload. Add focused controls for identical duplicate key/value rejection without builder/publication changes, input mutation during observation, and scalar/params equality. Mutable backing dictionary and native params array are actual boundary subjects. No production guard, coverage floor or source fixture is changed; new tests require targeted gates after the current package snapshot.

IMM-M282: Coordinator independently traced four reachable compiler snapshot guards and both portable-array converter malformed-token/EOF paths. Add six direct controls for initialized match/tree/line-map inputs, nullable-present default trees, non-array JSON and a non-final reader ending before an array completes. Preserve nullable-null absence and earlier document validation. No private API is widened and no guard is removed; targeted gates pending.

IMM-M268-M279 package verified: actual canonical Release workflow passes383 tests with zero failures, one expected unsupported-host skip and39isolated shards. Package-only build caught five missing child-writer callback guards; explicit guards preserve serializer behavior. Semantic checkpoint passes12,353 tests across23summaries with zero failures/skips. Coordinator reconciled60 exact migration/native/tutorial rows after re-reading source and independent review; snapshot coverage/constant completeness remains open.

IMM-M279 full authenticated strict comparison verified: exact605 was rebuilt at its original private source/PDB path with only current Coverage script and TCB metadata. All63 authenticated reports produce byte-identical70778-byte full JSON to the saved original (SHA256 F48C85D05FCC7FBA2FAE395B4C0713D7145392E1B05B4388C4881651E477F005). Unchanged100% strict floor still fails31,927/31,992lines;65baseline gaps require meaningful controls/invariant review and new exact-commit collection. This proves summary equivalence, not final coverage completion.

IMM-M283 reconciliation:60 exact C# rows and six weighted-scheduler rows reviewed against current source, independent findings and passing semantic/package gates. Current coordinate inventory is966 Open C# and535 Open PowerShell; completed rows retain their original discovery coordinates pending the next refresh.

IMM-M284: Build a private Roslyn semantic const audit against real compiler contexts. Before scaling,17 synthetic declaration/binding controls pass with zero build warnings/errors, including numeric/member/enum/null constants, reassignment/ref rejection, long-versus-byte overload changes, constructed generic calls, dynamic binder sensitivity, identity/capture review and top-level locals. Per-candidate clones compare diagnostics, constructed targets, parameter/ref kinds, operators, effective conversions and expression types. Fields remain explicitly separate API/write-review candidates. A small real-project performance/quality pilot is required before full source-universe collection; no new const is automatically applied from this prototype.

IMM-M285: PowerShell completeness check identified that the coordinate scanner does not yet record typed array/Hashtable/collection-interface ParameterAst signatures. Extend that discovery universe before migrating private scheduler/environment/helper inputs; native CLI bindings must be traced individually. Existing84-script constructor/mutation/return coverage remains valid but does not establish all input-signature completion.

IMM-M286: New M282 guard control reproduced a nullable immutable-array equality trap: Some(default array) has HasValue=true yet operator==null is true, while pattern is null is false. Native net10 controls verify absent/present-default/present-empty states. Replace source-tree and captured-reference absence checks with null patterns, preserving actual absence and rejecting the present-default invalid snapshot. Pin HasValue in the regression fixture and audit all nullable immutable-array null comparisons. Prior semantic/package checkpoints precede this repair; targeted compiler retry required.

IMM-M281-M286 verified checkpoint: four IR,17 Effects and eight Analyzer focused tests pass. The first Worker run exposed the nullable equality regression; after null-pattern repair,112 Worker compiler/codec/guard controls pass with zero failures/skips/build warnings/errors. Eleven original605 gap rows now have passing meaningful controls, with final authenticated coverage still pending. Layout passes2661sources/59projects/151pinnedcommon files; current raw/interpolated literal check preserves4088tokens across945changed C#files; C# and PowerShell formatting patches are empty. Const prototype17 synthetic controls pass; real Effects pilot reviews17sourceunits and186declarations in14seconds, with one BindingPreserved local still requiring role review. This is a measured pilot, not full const or exhaustive migration completion.

IMM-M287 latest-master integration: fetch found9e3b1ade2 (#340 primitive HashSet state and signed callback Count proofs). Resolve three conflicts by combining hashSetOrigins dispatch with existing immutable symbol/capture builders and explicit order, immutable ranking expectations including the new HashSet vector, and signed absolute Count bounds with the existing field-value/read snapshots. Convert the two imported origin sets to compatible owned immutable builders, preserving symbol comparer and mutation lifetime; freeze the imported method-name fixture vector. Actual mutable HashSet/Dictionary oracle subjects remain intact. All imported source fixtures are preserved against new master; full post-merge semantic/package gates pending. Previously found nullable codec/loop/golden comparisons remain a separate next repair.

IMM-M287 post-merge build repair: first full build catches two imported IrTerm[] helper handoffs incompatible with the already-immutable ListRuntimeEffects/EmitOpaqueCall cores (plus dependent architecture build failure; zero warnings). Use an owned exact-length argument builder, retain syntax evaluation order and ordinal slots, and freeze after all arguments before helper/callback publication. No tests ran; full semantic and package retry required.

IMM-M285 discovery verified: canonical AST pass records90 newly discovered collection-shaped parameter signatures across all84 scripts with zero parse failures:85 mutable storage, one enumerable facade, two read-only facades and two immutable controls (including nullable immutable input). Twenty-two positive/negative type controls and five script/function/inline/nested/class parameter scopes pass. All1091 prior rows and every prior status are preserved exactly;1181 total rows and625 Open candidates. Record input signatures before implementation; native argv/environment receivers and mutable timing sinks require individual ownership traces. Scanner construction now uses owned immutable builders instead of repeated root replacement.

IMM-M287 verified after repair: combined canonical semantic suite passes12,450tests across23summaries/sixisolatedtasks with zero failures/skips/buildwarnings/errors. Actual package workflow passes383tests with one expected unsupported-hostskip across39shards. Independent reviews confirm signed Count magnitudes, ordered snapshots, HashSet origin propagation and preserved mutable reentrant comparer/key subjects.

IMM-M288 nullable semantics and reviewed const candidates: repair remaining present-default equality sites. Built-in portable type metadata rejects Some(default) rather than accepting absence; a direct codec regression pins HasValue and exact rejection. Golden presence checks use null patterns; stack-shape validation explicitly requires initialized arrays. Loop cells use null presence plus the shared initialized-input guard, retaining fail-closed handling and the sole-writer null/initialized invariant; coordinator traced every _cells writer. Convert the semantically proven enum uncertainty marker to const and, after manual capture/binding/SUT review, make the runtime maximum-length oracle local const. The actual CLR allocation and AllocateBytes(int) call remain unchanged. Gates pending; source checkpoint707 precedes this new batch.

IMM-M289 Frontend outer argument vectors: coordinator Roslyn preview checks17files,17parameter/default guards,12outer locals,94scalar Case wrappers,55unchanged reference Case vectors,24StackType calls,15CompiledMethod calls and four final reflection adapters. Preserve original object versus object? element annotations and NUnit single-vector boxing. Keep nested CLR arrays, SameAs/mutation subjects and the nested ElementValues long[] reflection adapter. Coordinator verifies source hashes before applying every preview; syntax and all string/character tokens match. Independent review found no functional defect. Builders are unnecessary for fixed literal vectors; reflection receives a shallow final ToArray adapter. Formatting and full semantic/package gates pending.

IMM-M288-M289 verified checkpoint: all12,451 semantic tests pass across23summaries with zero failures/skips; the actual package workflow passes383tests with one expected unsupported-hostskip across39isolatedshards. Layout passes2666sourcefiles/59evaluatedprojects/151pinnedcommonfiles. All4090raw/interpolated literal tokens match latest mergedmaster9e3b1ade2 across954changedC#files. Independent nullable/vector reviews found no functional defects. Coordinator reconciles29implemented vector rows,20required nested CLR-array subjects and three owned builder rows; the two reviewed const rows are implemented. Current C# coordinate inventory is914Open, pending lossless coordinate refresh and full semantic const audit.

IMM-M290 audit quality checkpoint: all19synthetic const controls pass, including native-sized integers; metadata now records semantic source hashes, complete owners, language/defines/nullable context and compilation identity. Explicit enum/parameter/catch/query exclusions close previously silent declaration categories. Binding proof additionally traverses operation trees for member/constructor/argument/operator/conversion targets. Full real-context collection remains pending.

IMM-M292 conflict priority: fresh fetch findsmaster7f6e25f21 (#341 Dictionary capacity construction). Merge-tree identifies one conflict in ApiUsageRankingTests; preserve the new capacity ranking input while keeping immutable expected vectors. The fully passing M288-M289 checkpoint is committed before integration; separate Fuzz validation changes remain unstaged and are not covered by that checkpoint.

IMM-M292 merge resolution: combine capacityMembers and the revised five-indexer ranking expectation with immutable vectors. Before compilation, coordinator catches imported IrTerm[] constructorValues handoffs to immutable helpers; replace with an exact-length owned builder, evaluate in syntax order, assign parameter-ordinal slots and freeze after evaluation. New Dictionary capacity/alias/negative-capacity CLR subjects remain unchanged. Separate Fuzz working changes are excluded from the merge commit; post-merge validation is pending.

IMM-M291 Fuzz validation batch: add seven default-input controls covering all three oracle status vectors, source campaigns, metadata compilation and both replay result counters. Move metadata temp-directory/load-context construction after validated source materialization, preventing resource creation for rejected inputs. The private synchronous coverage counter consumes read-only spans from its two initialized frozen producers, so default/empty is representable as zero work and unreachable default-array guard branches disappear. First build requires explicit total-domain span comments; second catches NUnit delegate-overload ambiguity and a missing test string guard. After typed Action/Func delegates and guard repair, all309ordinary Fuzz tests pass with zero warnings/errors/failures/skips. Post-master full semantic/package validation remains pending.

IMM-M293 TRX cache/parser: replace the script cache and class maps with immutable dictionaries, use owned builders for definitions/rows/timing sums, freeze nested row records/vectors before publishing a completed parse, and retain the empty/pipeline emission adapter. Thirteen before/current controls preserve duplicate-ID last-write behavior, case-sensitive classes, case-insensitive keys/method sums, first method spelling, document/file order, deferred invalid-duration failures, malformed-XML retry and cache hits. First comparison catches builder key-spelling replacement; TryGetKey retains the original canonical spelling before sum updates. Final timing records are read-only ordered records with explicit dictionary-key sorting. Actual package workflow and formatting still pending.

IMM-M292 ranking integration repair: actual full-solution compilation catches three imported Scan helper type mismatches that text merge and initial review missed. Use strict ImmutableDictionaryCapture with the original ordinal comparer and construction-owned site/gap builders; freeze both after Scan. The next full-solution compilation succeeds with zero warnings/errors. Its later sample-only restore fails because those consumers require the repository's local package feed; this is a separate compiler-context route, not a source compile failure. Keep the failed and partial raw census reports separate; do not claim all-source const completion.

IMM-M294 build-engine observations: replace three escaping fixture Lists with private immutable-array builders and locked snapshot getters. LogMessageEvent appends under the lock, releases it before MessageObserver, then completes WorkerReported; callback exception/completion ordering and event-object identities stay unchanged. Coordinator and independent review trace every caller: all reads/enumeration/indexing, no external mutation. Seven NUnit count assertions use Length. Actual package workflow pending.

IMM-M295 PDB identifier comparison: replace the owned20-byte mutable vector with an exact-capacity immutable builder. Append16Guid bytes followed by four native-endian stamp bytes and MoveToImmutable only after the proven20-byte fill. Preserve native Guid/BitConverter adapters and byte comparison order. Native symbol-package validation remains required.

IMM-M296 semantic audit authority: real IR.Test census reviews62units/2229rows in11seconds; it contains no eligible local binding candidates, so this measures census throughput rather than clone-proof throughput. Expanded synthetic controls cover26candidate/language-form locals/fields plus ten enum/parameter/catch/query declarations and one actual compiled source, including linked-path spelling and native-integer limits. Full-solution census reveals linked-source path normalization must be explicit; preserve all raw compiler paths and normalize against the actual project directory before tracked-universe matching. All missing units remain visible until compilation or documented subject/native review.

IMM-M296 normalized solution authority verified:51actual compiler contexts cover1857compiled-unit instances/1712distinct tracked paths and74353declaration/unit rows in116seconds, with zero build warnings/errors. All24eligible locals prove binding preservation (23ordinary, one capture-review string-allocation length); no binding-change/operation-root gap remains. Explicit211unbound paths are208physical compiler/runtime subjects plus three dynamic helper sources; no unexpected missing source path. Linked-path pilot proves normalization for six actual eng/shared inputs. Twenty-seven synthetic controls plus eleven language-form declarations pass, including implicit in-parameter storage rejection and native integer maxima.

IMM-M297 reviewed const batch: coordinator and independent reviewers approve24locals across18files after actual compiler clone proof and use-site/identity/capture/SUT review. Preserve null! for the null-receiver oracle, derived budget expressions and original literal spelling. Roslyn preview verifies source hashes, parse options and all literal tokens; coordinator preflights all18source/preview hashes before applying. The one captured allocation length has no observed delegate/closure identity or layout contract. Full semantic/package gates remain pending. Field review traces21real lifecycle/readonly/cache candidates; mutable logical-cache removal is being evaluated separately rather than eagerly changing exception timing.

IMM-M298 immutable clause validity: replace the atomic lazy integer cache with a readonly constructor-derived tri-state. Invalid/default shapes retain the exact lazy predicate, preserving repeated NullReferenceException for default arrays, ArgumentNullException(clause) before the first valid entry, and valid-before-null short circuit. Sixteen native real-class profiles compare fresh construction plus0/1/2/4reads; all values/errors match and allocation remains48bytes/inventory. Eager construction adds approximately1-19ns in these samples when no read occurs, while the32-entry read profiles improve; this microbenchmark does not establish an end-to-end speedup. Add seven direct constructor/read controls. Twenty other field candidates are reconciled as traced runtime/lifecycle counters or already-readonly runtime configuration. Full gates pending.

IMM-M296 tracked const-unit manifest: docs/immutability-const-units.tsv lists all1923tracked C#paths, including zero-candidate units, census/current source hashes and compiler-context identities. Exactly1712paths have actual solution compiler census evidence,208physical compiler/runtime subjects retain their input semantics, and three dynamically compiled helper sources have explicit manual local/field review. Source changes are marked rather than silently inheriting old hashes. This manifest scopes const review; it does not claim exhaustive collection mutability coverage or automatic semantic proof for dynamic helpers.

IMM-M291-M298 verified checkpoint: all12494semantic tests pass across23summaries with zero failures/skips/build warnings/errors. Actual package workflow passes383tests with one expected unsupported-hostskip across39isolatedshards. Clause-inventory focused25tests pass. Layout passes2668sources/59evaluatedprojects/151pinnedcommonfiles; all4097raw/interpolated literal tokens match mergedmaster7f6e25f21 across961changed C#files. C# formatting patch is empty; PowerShell formatter indentation patch was applied before gates. Reconcile24constlocals, readonly validity cache, three logger snapshot properties and15TRX/cache rows. Independently reviewed/coordinator-traced Fuzz, Frontend, package native subjects and stale traversal memo builders are reconciled individually; actionable outer vectors remain Open. Native symbol helper uses an exact20-byte immutable builder and awaits clean exact-commit pack validation. Initial pack attempt correctly refuses the dirty worktree; commit this verified source checkpoint before that native gate. Additional const context manifest records all51actual compilation reports, including two generated-only wrapper projects.

IMM-M295 exact-commit native validation passed at2ce34520e: canonical clean pack builds with zero warnings/errors, exports239assembly PDB outputs, creates the three product packages and their required symbol packages, and validates exact portable/embedded symbol pairing, third-party18-component inventory and source digests. This validates the dynamic Add-Type SymbolPackageValidator path that the solution C# build alone does not compile.

IMM-M299/M304 pending batch: immutable codec roots/variables/operations/index mappings replace19read-only facades; identity/block builders freeze once in graph order. Decode metadata default validation stays inside the existing malformed-input/cancellation flow. Nine companion diagnostic/lowered/pack/metadata-reference signatures now take immutable inputs, preserving absence separately from Some(default). Add five codec shape controls and extend pack/ownership/diagnostic-order boundary assertions. C# formatter patch is empty; focused real Worker/compiler tests are running. Broader gates and inventory reconciliation remain pending.

IMM-M305 module snapshots/builders: replace acceptance-cache SetItem publication with a scoped ToBuilder/indexer/freeze; preserve case-insensitive keys and cached snapshot identity. Use owned builders for filtered diagnostics, Git stdout/stderr detail lines, framework XML projection, SDK version output and completed-process snapshots; assembly name is a scalar first-nonempty projection. Capture native SDK exit status immediately after enumeration. All27before/current cache/summary/assembly/Git-error/SDK controls pass. Actual before/current native scheduler workflows pass completion order, reserved slots, failure retention, timeout child cleanup, preparation-failure cleanup and parallel build success/failure. PowerShell formatting checked87scripts/19shellscripts; its10indent-line patch is applied. Combined full gates pending.

IMM-M306 census evidence hygiene: independent review verifies stored1923-path universe,1857compiled-unit rows/1712distinct paths,211documented absent units, zero unexpected paths and zero stored hash mismatches. Current source has advanced; manifests explicitly distinguish census/current hashes and final real-context collection remains required. Fix the exporter so a partial rerun cannot mix previous TSVs into current results: preserve each prior run directory, publish one fresh run and record exit code. Native controls prove stale rows absent, prior evidence retained and failed-run exit status7 preserved. Existing M296 evidence remains historical declaration-clone proof, not a claim that current source was recollected.
M296 frozen proof identity: HEAD=37956186b1b0f8a3f8394289ffcfc5fdfe4d236b; workingPatchSha256=f6ddb0bceade85ab1f312c3de7b1c2be4e8ef81859f94cea7679840c59bc7ad4; analyzerSha256=24598081a109a1cbf7e9ba489ea65014055b99e912c99eafe2a32bc4ead9e06f. Per-source and compiler-context hashes are recorded in the tracked manifests.

IMM-M299-M305 verified checkpoint: all12500semantic tests pass across23summaries with zero failures/skips/build warnings/errors. Actual package workflow passes383tests with one expected unsupported-hostskip across39isolatedshards. Focused compiler/Worker185tests pass. Layout passes2668sources/59evaluatedprojects/151pinnedcommonfiles. All4097raw/interpolated literal tokens match mergedmaster7f6e25f21 across961changed C#files. Reconcile28immutable input signatures and sevenPowerShell builder/projection rows; native symbol pack at2ce is separately validated. Coordinator source-traces25Worker callback/lifecycle/construction rows, preserving actual mutable subjects and owned builders with frozen observation/publication. Current const-unit source hashes are refreshed; final real-context recollection remains pending. Next reviewed previews M307-M310 have not yet been applied to this checkpoint.

IMM-M307/M308 targeted verification: pinned Z3 enumerable overloads accept immutable vectors directly; remove only redundant native adapters, retaining array-only SubstituteVars/MkSub boundaries. Callable selectors remain BoolExpr and meter/cancellation/disposal order is unchanged. Remove the unused mutable-stack traversal overload and migrate its sole test to ImmutableStack; fixed IR fixture vectors/helpers are immutable. All404SMT tests and83focusedIR tests pass.

IMM-M309/M310 targeted verification: analyzer worklists use ImmutableStack and owned builders, fixed runtime range cases use a sealed immutable record with CallLine finalized before publication. All30focusedAnalyzer tests pass. Frontend emitted/file-read image roots freeze immediately and use direct immutable PE/metadata overloads; JSON receives AsMemory and net8 file writes retain final ToArray adapters. Initial build catches production-only RepositoryParameterGuard alias in three test-helper calls; explicit IsDefault/ArgumentException checks repair those calls. Retry passes75focusedFrontend tests. Preserve actual owned/disposed metadata subjects and native load-context/file adapters.

IMM-M312 applied: migrate ConsumerProject14property signatures, local/freshness wrappers and all callers together to immutable ordered tuple vectors. Preserve duplicate MSBuild properties, validation order and single-name reflection resolution. Use builders for prepended/appended properties and freeze collision workloads before Task.WhenAll while retaining the construction journal for finally cleanup. EvaluatePropertiesAsync preserves strict duplicate rejection after value evaluation; stable file snapshots own immutable byte values. Independent review verifies tuple literal/order counts, all callers and manifest hashes. Package workflow validation pending.

IMM-M313/M314 applied: freeze Worker option lookup maps and private IL-parser publication; preserve ordered option evidence through an immutable entry vector and ordered JSON writer. NormalizeTargets receives fresh owned builders because its mutation is intentional. Fixed generic-return boundary values are immutable while runtime CLR array subjects remain mutable. Roslyn source-type construction uses the pinned two-immutable-vector overload with annotations derived from substituted symbols; add a focused nullable-generic substitution control. Coordinator corrects test-only guards to explicit IsDefault checks. Targeted and broader gates pending.

IMM-M315 applied: shared Analyzer assertion/diagnostic and two tuple helper inputs use immutable vectors;119calls across43files retain all original string/character/raw/interpolated literals. Preserve count overloads and freeze framework array inputs at the private assertion boundary. Independent review catches three conditional vectors accidentally nested by the preview rewrite; remove the outer brackets before applying. Strict release-rule maps retain duplicate NUnit failure through ContainsKey/assert/Add and freeze once.

IMM-M316/M318 applied: MetadataTestSubject and parameter-attribute emitted images own immutable byte snapshots; direct immutable PE readers preserve original bytes and metadata identity. MutateImage keeps its deliberate cloned mutable corruption buffer. Independent review catches obsolete LongLength on ImmutableArray; replace with an explicit long Length value. Assumption mutation fixture uses one clause builder across the switch and freezes once afterward; ID/site/swapped-ID and unchanged-clause graph mutations preserve their original ordering and identities.

IMM-M307-M318 semantic checkpoint passes12501tests across23summaries with zero failures/skips/build warnings/errors. All4215raw/interpolated literal tokens match mergedmaster7f6e25f21 across990changed C#files. Focused new/current cohorts pass83IR,404SMT,30Analyzer,75Frontend,64Worker and7substitution tests. Initial package build catches CA1849 for the now-direct synchronous stable-file read; use sequential awaited ReadAllBytesAsync before freezing each immutable byte value. This repair is package-test-only; actual package workflow retry and layout remain pending. Inventory reconciliation is prepared as83exact IDs,81currentlyOpen, and is not applied before the remaining gates.

IMM-M317 prepared only: policy/scheduler input previews remove CPU-policy splats, receive immutable policy vectors through readonly interfaces and freeze sorted/selected outer task vectors. Canonical parser checks and50baseline/current comparisons pass, including23CPU policy cases. A first unknown-policy comparison exposes changed error timing; resolve it by performing the method-based policy branch test before indexing argument values. Six baseline/current native scheduler workflow groups pass completion order, reserved-slot exclusion, failure retention, timeout/preparation child cleanup and parallel build success/failure. Four previews remain separate from the source snapshot under current gates.

IMM-M307-M318 verified checkpoint: full semantic suite passes12501tests across23summaries with zero failures/skips/build warnings/errors. After the package-only CA1849 async-read repair, actual package workflow passes383tests with one expected unsupported-hostskip across39isolatedshards, including14freshness controls and94BuildTask controls. Layout passes2668sourcefiles/59evaluatedprojects/151pinnedcommonfiles. Fixture check preserves4215raw/interpolated literal tokens against mergedmaster7f6e25f21 across990changed C#files. Formatter patch applied before semantic gates; subsequent repair changes only one package-test file-read expression. Coordinator reconciles83exactrows,81previouslyOpen and two prior single-update exceptions now implemented with one shared fixture builder. Current coordinate inventory is735Open C# and603Open PowerShell, with lossless refresh and current authenticated coverage still pending. M317/M319/M320 previews remain unapplied and do not inherit this source checkpoint's test results.

IMM-M317/M319/M320/M322 verified checkpoint: all12506semantic tests pass across23summaries with zero failures/skips/build warnings/errors. Actual package workflow passes383tests with one expected unsupported-hostskip across39isolatedshards. Layout passes2668sources/59evaluatedprojects/151pinnedcommonfiles;4215raw/interpolated tokens match mergedmaster7f6e25f21 across991changed C#files. All14source files were frozen throughout the full gates. Current const-unit source hashes are refreshed; historical compiler proof remains separately identified.

IMM-M317 replaces policy splats with direct policy branches, accepts immutable vectors through readonly scheduler contracts and freezes sorted/selected outer task vectors. Fifty baseline/current policy/cache controls and six native scheduler groups preserve completion order, slots, failure retention and cleanup. Unknown-policy error timing is preserved by branching before indexing. Shard records remain separately Open.

IMM-M319 finalizes only the EvaluationState heap reference in its constructor; IrHeap remains the mutable subject. Ordered immutable observation entries replace an unused mutable dictionary contract while the in-place overlay buffer retains partial writes on failure. Default observations reject before writes. Focused105IR and12SMT tests pass; an initial no-match IR filter is not counted.

IMM-M320 snapshots emitted PE images and uses immutable helper inputs, direct PE readers and native adapters only at CLR load boundaries. Measurement rounds use an exact-capacity builder and freeze outside measured windows, preserving allocation subjects and KeepAlive. Ten focused Worker controls pass. Net10 asynchronous file writes accept immutable memory; a separate net8 API probe rejects that overload, so net8 byte-array adapters remain required.

IMM-M322 adds four direct default-input controls for transaction updates, snapshot rendering, Git arguments and assignment counts. They verify parameter identity and absence of filesystem/process effects. Eight focused Gates controls pass. Remove only the redundant private parser initialization guard after tracing both callers; public boundary guards remain. Four coverage rows become ControlPassed and one becomes InvariantRemoved; neither status claims fresh authenticated coverage.

IMM-M321 verified byte pipeline:62source files migrate together, including89caller rewrites. Shared/protocol/artifact hash inputs, exact file reader outputs, strict UTF-8 decode, launcher storage/request/publication and atomic async content now use immutable byte roots. Preserve four stream-hash callers, native ReadExact scratch and final netstandard SHA/file array adapters. Existing mutable PE corruption/replacement fixtures keep their mutable subjects, with readonly boundary snapshots. Default arguments preserve parameter exceptions; initialized-empty exact reads and digests have direct controls.

IMM-M321 verification:143focusedWorker and13focusedIR tests pass; full semantic suite passes12509tests across23summaries with zero failures/skips/build warnings/errors. Actual package workflow passes383tests with one expected unsupported-hostskip across39isolatedshards. Layout passes2668sources/59evaluatedprojects/151pinnedcommonfiles. All4219raw/interpolated fixture tokens match mergedmaster7f6e25f21 across1002changed C#files. Formatting produces an empty patch; the driver initially rejects that empty patch, then skips application and runs actual tests. Coordinator verifies all62source/preview hashes and reconciles19exact rows, including three superseded native-boundary exceptions and one retained native read scratch buffer. Source remained frozen during gates.

IMM-M326 census tooling pilot only:coordinator adds semantic residual observations beside the real compiler const census, with source-sensitive compiler context identities, inferred arrays/builders/facades/generics/ref-like holders, zero-candidate units and explicit duplicate ambiguity. Native synthetic pilot passes22observations across two source units and proves coordinate-shift-stable IDs while source context identity changes. Existing27const/binding and11language-form controls pass after a tool-only Roslyn symbol cast repair. Full current collection, absent-unit conservation, semantic alias/escape review and lossless legacy reconciliation remain required; this pilot does not close source rows or claim exhaustive census completion.

IMM-M323-M328 verified checkpoint: all 12518 semantic tests pass across 23 summaries with zero failures/skips. Actual package workflow passes 383 tests with one expected unsupported-host skip across 39 shards. Layout passes 2668 sources, 59 evaluated projects and 151 pinned common files. All 4219 raw/interpolated literal tokens match merged master 7f6e25f21 across 1004 changed C# files. C# formatting was applied before gates; PowerShell formatting checked 87 scripts and 19 shell scripts with no changes.

IMM-M323/M324/M327 replace canonical hash, compiler authority image and package fixture boundaries with immutable byte/vector inputs. Native stream scratch and final CLR/MSBuild adapters retain required mutable representations. Nullable prior images preserve absence, and owned reference/instruction/argument builders freeze before publication. Coordinator source-traces and reconciles 27 exact C# rows; native mutable subjects retain their behavior.

IMM-M325 adds direct default/empty/normalization/relationship controls for ten coverage gaps. Initial targeted compilation exposed ambiguous NUnit async delegate overloads and block-body formatting requirements; explicit awaited Func<Task> delegates and existing body style repair those issues. Focused IR 23, Worker 147, Tool 43 and Gates 8 tests pass. ControlPassed does not claim a fresh authenticated coverage collection.

IMM-M328 timing history uses three case-insensitive immutable dictionary builders and readonly published maps. Sixteen before/current native controls preserve casing, duplicate overwrite, malformed-file partial updates, empty history, lane exclusions and partitioning. The first actual package attempt caught one remaining empty-hashtable call argument; replacing it with an empty immutable map and rerunning the actual workflow passes. Fourteen exact PowerShell rows are reconciled; unrelated shard/record vectors remain Open.

IMM-M326 real compiler pilot: IR.Tests collection passes with 62 compiled paths/instances, one source-sensitive compiler context and 4908 semantic observations. The 1923-path universe is conserved, with 1861 paths explicitly outside this selected pilot; zero build warnings/errors. This is a pilot, not full current solution collection or alias/escape proof. Lossless legacy reconciliation and exhaustive residual review remain pending.

IMM-M329-M331 verified checkpoint: all 12519 semantic tests pass across 23 summaries with zero failures/skips/build warnings/errors. Actual package workflow passes 383 tests with one expected unsupported-host skip across 39 shards. Layout passes 2668 sources, 59 evaluated projects and 151 pinned common files. All 4242 raw/interpolated literal tokens match merged master 7f6e25f21 across 1015 changed C# files. Final C# formatting patch is empty; PowerShell formatting checks 87 scripts and 19 shell scripts with no changes. Source remained frozen throughout full gates.

IMM-M329 migrates 45 analyzer files together: exact emitted immutable image roots, immutable graph vectors, private helper inputs and required native file/CLR adapters. All 1135 Analyzer tests pass. PatchAscii retains its owned mutable corruption output. Four remaining pre-existing immutable-image PEReader copies stay Open for the separate M333 cleanup, rather than being incorrectly classified as required native adapters.

IMM-M330 migrates the conditional metadata rejection fixture to immutable images and helper inputs, retaining nullable marker absence and a private native corruption buffer. The mutated image freezes before metadata observation, file publication and return. Eight focused Worker tests pass, preserving MVID/table/IL/allowed-offset and runtime assertions.

IMM-M331 narrows fuzz batch/source/CLI contracts to immutable vectors and adapts the native CLR entrypoint once. An exact-capacity builder snapshots indexed semantic arguments for IR while reflection receives the original custom argument view; the swapped-enumeration subject remains intact. Initial targeted compilation catches two missing Slice lengths and two analyzer-required initialization guards; repair those and all 310 Fuzz tests pass. A ninth caller control changes null to default for the value-type contract. Coordinator reconciles 44 exact C# rows; current real-context census and authenticated coverage remain pending.

IMM-M332-M340 verified checkpoint: all 12525 semantic tests pass across 23 summaries with zero failures/skips/build warnings/errors. Actual package workflow passes 383 tests with one expected unsupported-host skip across 39 shards. Layout passes 2668 sources, 59 evaluated projects and 151 pinned common files. All 4253 raw/interpolated literal tokens match merged master 7f6e25f21 across 1019 changed C# files. Targeted suites pass Analyzer 130, Worker 14, IR 12, SMT 79 and Gates 80 tests. C# formatter edits and one PowerShell indentation patch were applied before targeted tests; the final PowerShell check formats zero of 87 scripts/19 shell scripts. Source stays frozen throughout full gates.

IMM-M332/M338 planning: immutable bucket roots survive PowerShell conditionals, ordinal planned-fixture/failure builders freeze, and six ordered readonly shard descriptors finalize filters before publication. Optional keys retain Slots-first precedence, including postflight descriptors containing both Slots and Exclusive. Eighteen partition, six root-selection, twelve descriptor and twenty-five compatibility controls pass. Native baseline/current scheduler groups preserve completion order, slots, failure retention, timeout/preparation cleanup and parallel build success/failure. Initial native control dropped a one-item baseline array; restore its native wrapper before comparison. Add the direct Filter property assignment missed by the legacy scanner as an explicit resolved audit row.

IMM-M333/M334/M335/M337/M339: migrate analyzer/Worker images, five unnecessary PEReader copies, IL spans, corpus fixed vectors/strict maps and scoped census builders, direct Horn contracts and native manifest hash inputs. Default/empty Horn inputs have direct controls. Corpus malformed inner byte arrays, reflection/native adapters and actual allocation subjects remain intact. All existing raw/string/char fixtures are conserved; typed immutable vectors retain boxing/order and original dictionary semantics.

IMM-M336: three coverage findings gain direct controls, including two invalid overlay-index cases. Default call operands reject before receiver evaluation; rejected immutable overlays retain input identity; comparer-aware providers keep global options out of file options. ControlPassed records passing controls, not fresh authenticated coverage.

IMM-M340 native parameter binding: a canonical probe proves that replacing string[] with IReadOnlyList<string> breaks object[] callers and changes null/empty rejection. Retain the legacy native parameter set and add explicit immutable paths. Their immutable parameter is nonmandatory with AllowEmptyCollection because mandatory binding enumerates default arrays before a guard; explicit default rejection then runs in the helper. Fifty-five legacy/content/default/empty controls and eight actual native wrapper argv/capture/exit comparisons pass. Internal helpers use owned builders and freeze once; wrapper recapture arrays are removed. Native scheduler and actual semantic/package workflows validate the resulting script path.

IMM-M341 census tooling control: exclude Roslyn INameOfOperation from false unbound-invocation reports while keeping dynamic invocations explicit. Twenty-six synthetic observations across two units pass source-sensitive context, zero-candidate, shifted-ID and ambiguity controls. The historical real pilot remains historical; full current collection and lossless multi-epoch ledger reconciliation remain required.

Coordinator reconciles 109 exact C# rows, 28 existing PowerShell rows and three coverage rows, plus the manually discovered Filter write. Source traces distinguish stale already-immutable builders from actual mutable input subjects, framework arrays and atomic selector-memo publication. Recovery marker fixture construction and a ratchet stream copy remain Open for M344; native stream input signatures are not automatically exempted merely because the final API needs an array. Independent reviews remain read-only and main-only writes are preserved.

IMM-M342-M346 verified checkpoint: 12527 semantic tests across 23 summaries pass with zero failures/skips/build warnings/errors. Package workflow passes 383 tests plus one expected unsupported-host skip across 39 shards. Layout passes 2668 sources, 59 evaluated projects and 151 pinned common files; 4262 raw/interpolated fixture tokens match merged master across 1022 changed C# files. Focused Analyzer 93, Worker 33, Gates 74 and IR 63 tests pass. Two new Boolean-combination default/empty controls retain factory-first ArgumentNullException behavior.

IMM-M342/M343/M344: immutable compiler/reference/platform-token images and fixed vectors reach direct PE/metadata or span/memory APIs. Reflection/native mutable subjects remain intact. Async corruption writes remain awaited. Nine recovery-marker corruption cases retain builder/indexer/removal semantics and named-property JSON decoding. Exact stream-buffer UTF8 decoding removes an unnecessary ratchet copy.

IMM-M345: migrate production execution maps, Boolean vectors, snapshot sets and free-variable contracts together with all callers. Main corrects an exception-type mismatch before application; independent review finds an omitted snapshot-depth caller and opaque oracle map. Initial build catches redundant ToImmutableHashSet normalization and a missing import; fix with explicit WithComparer and immutable namespace import, then focused/full gates pass. Native mutable-view interpreter subjects remain unchanged.

IMM-M346: package fixtures and private process/digest helpers use immutable roots and scoped builders, including immutable expected arguments with mutable arrays only for VerificationPublication.Prepare mutation subjects. Actual package compilation catches net10 AddRange overload ambiguity; typed immutable collection expressions resolve it, and the complete package workflow passes. The only post-semantic source repair is confined to the package-only BuildTaskTests fixture.

IMM-M356 source reconciliation: retain CLR attribute metadata/alias requirements and the intentionally inconsistent live substitutions facade. Verify six pre-existing owned compiler/specification builders with frozen publication and exact-capacity MoveToImmutable proofs. Reconcile 67 exact historical C# rows in total; the coordinator remains the only writer.

IMM-M355 historical provenance archive: preserve all 59 ledger epochs through b13bfa19e as exact compressed Git blobs, plus 166441 row relations keyed by epoch/legacy ID/row ordinal. The initial 4135 rows/845 paths and latest 2618 rows/643 paths are conserved; no status transfers occur. Historical scanner provenance is unknown, and current semantic matching remains separate. Canonical image lacks Python; archive extraction uses the available host Python for read-only Git/data processing, with no repository build or acceptance command on the host.
IMM-M357 conflict-first integration: merge master 8556188d120fc7e76d724cf36aa1fafa764b5dfd and resolve ten conflicts. Preserve immutable initial maps/vectors and init-only replay options while incorporating ranked exception constructors, resource callback effects, concrete lock completion and the updated corpus ratchet. New exception argument producers use an exact-capacity builder, assigned in source evaluation order and frozen before allocation/effects. Four new oracle fixture files use immutable vectors/builders; actual exception/culture/reflection subjects retain their required behavior. An initial compile exposed the upstream array producer after helper migration; fix it before the final gates. Eleven manually discovered master findings carry original source hashes and explicit resolved rows.

Final source-frozen verification: Frontend 23, Specs 54 and Worker 80 targeted tests pass; 12694 semantic tests across 23 summaries and 383 package tests plus one expected skip across 39 shards pass. Layout passes 2677 sources, 59 projects and 151 pinned common files. All 4262 raw/interpolated literal tokens match merged master across 1027 changed C# files. Formatting and Git whitespace checks pass.

IMM-M358 current unit conservation: refresh current hashes while preserving historical compiler hashes/status. Track all 1932 current C# paths, including nine newly tracked units explicitly marked CurrentCompilerCensusPending. No historical compiler proof is transferred to new sources. Full current compiler collection remains required; historical compiler and legacy ledger evidence remain separate. The coordinator remains the only writer.
IMM-M359 full current actual-compiler census: at pushed merge 873f54c6f, conserve all 1932 tracked C# paths. Actual compilations cover 1721 distinct paths/1866 unit instances in 49 nonempty contexts; 51 report files include two generated-only package compilations. Explicit absences are 208 physical compiler/runtime subjects and three dynamic helpers; unexpected missing paths are zero. All 141072 residual observations have unique (ContextSha256, StableId) keys, including zero-candidate Unit rows. All 74877 const observations contain zero eligible locals. The coordinator traces 20 field candidates to runtime budgets/session/fixture state or constructor-configured readonly storage. New summary/field evidence files record hashes and limits; current unit fields remain separate from historical proof and explicitly flag the 21 subsequently changed C# sources. Repair collector setup for the exact read-only Git mount, allow clean empty patches, and fix the conservation check for generated-only projects before the successful full collection. Historical compiler results are not overwritten.

IMM-M347-M354/M360 verified checkpoint: migrate launcher input contracts and all affected callers, package tuple/process/snapshot fixtures, staged byte inputs, replay/outcome fixtures and long-fuzz lexical scopes. Scoped builders preserve overwrite/append and callback completion boundaries; immutable snapshots cross publication boundaries. Exact-capacity MoveToImmutable is used only where count/capacity are proven. Main catches net10 AddRange ambiguity and preserves cancellation-before-copy order. Fuzz baseline/current reproduces 20006 exact Source/Mode/Claim observations. The coordinator checks and reconciles 58 exact C# rows and 24 PowerShell rows. Native manifest/performance report nested-array rows remain Open for the prepared follow-up rather than claiming deep immutability prematurely.

Validation: 267 focused tests pass (IR9, Verify55, Worker184, CodeFix19). All 12695 semantic tests across 23 summaries pass with zero failures/skips/build warnings/errors. Package workflow passes 383 tests plus one expected unsupported-host skip across 39 shards. Layout passes 2679 source files, 59 evaluated projects and 151 pinned common files. All 4262 raw/interpolated fixture tokens match merged master across 1032 changed C# files. Seven PowerShell formatter edits preserve every non-newline parser token exactly; formatting then requires no further edits. Full semantic execution used the equivalent pre-format script tokens; package execution uses formatted sources. Fourteen native readonly-record/JSON controls, actual developer plans/contract output and baseline/current native scheduler lifecycle controls already pass. The main agent remains the sole writer.

IMM-M365 lossless reconciliation: extend the historical archive through 873f54c6f to 61 epochs and 171688 full historical rows. Preserve all original coordinate/kind/owner/detail/status columns and exact raw Git ledger blobs, plus 42931 source authorities/2552 source blobs. Conserve the full 141072 current residual/Unit observations separately. Relations explicitly distinguish changed source, missing tracked paths, outside selected contexts, unrepresented forms, ambiguity and exact-source/location candidates requiring scanner replay. Every legacy effective status remains NoAutomaticTransfer because historical scanner authority is unknown; 1592 current location candidates and 139480 unmatched current rows are conserved. No rename is inferred and missing paths remain explicit tombstones. Archives remain local evidence pending final current recollection and role review; this is not a claim that all semantic observations are violations or have been resolved.
IMM-M361-M368 verified checkpoint: 64 focused tests pass (IR44, Frontend2, Worker18). All 12695 semantic tests across 23 summaries and 383 package tests plus one expected skip across 39 shards pass. Layout passes 2679 source files, 59 projects and 151 pinned common files. Raw/interpolated fixture conservation remains 4262 tokens across 1032 changed C# files. Final script formatting changes zero files. A Worker compile catches one redundant ToImmutableArray after manifest-byte input migration; remove it and rerun the affected Worker checks followed by both full suites. Unaffected IR/Frontend checks are retained.

M361 freezes private byte inputs and digest vectors while preserving the stream's mutable native storage, concurrent argument mutation witnesses, boxed byte-array dispatch and reentrant/enumeration state. Scalar Guid construction preserves its eight zero tail bytes. M363 initializes eight input builders directly and freezes before each evaluation; it removes repeated immutable-root SetItem/Remove assignment while preserving distinct valid/malformed snapshots. M367 freezes retained PE/metadata/file images and native hash inputs; direct PEReader inputs avoid unnecessary streams, native loader adapters preserve Assembly.Load semantics, and awaited exact-length buffer memory writes preserve emission order. Main corrects two array-only consumers during review before application. Two pre-existing caller observation builders already freeze after all synchronous producers complete.

M362/M364/M366 freeze coverage top-level vectors, fifteen consumer snapshots and three consumer assertion builders, plus six native/performance/developer-plan report vectors. Actual native controls pass nine coverage-record JSON comparisons/42 mutation checks, 45 consumer projection JSON/cardinality/identity comparisons/seven assertion-builder comparisons, and twelve report-record JSON comparisons plus actual Debug/Release plan/contract output. Outer records remain ordered and readonly. The newly discovered nested project uncoveredLines vector and ordinal helper SortedSet remain Open for the prepared M371 follow-up; the whole summary is not falsely closed as deeply immutable.

Coordinator source reconciliation closes 42 exact historical C# rows: 20 implementations, 11 existing owned immutable builder/vector sites and 11 deliberate mutable subjects/native boundaries. This completes the 25-row IR.Tests coordinate partition. Record 24 exact current compiler observations with their full StableId/context/source authority: eleven retained array roots, two private hash parameters and eleven self-rebinding observations. Eight input construction updates now use builders; three direct immutable fixture forks/transactional disposal resets remain intentional. These are independent current-source attestations, with no automatic historical status transfer. Close 25 PowerShell rows backed by actual controls. Remaining Open counts: C#359, PowerShell505; authenticated coverage work remains unchanged.

Evidence hardening: reconstruct the original residual auditor source and verify its SHA256 exactly against the successful M359 compiler report (283967b6a98acb4c8b2e94ba835e2493fe434b69fec3676691c2ecd3a7ef93dd). Preserve that source separately before extending ignored audit tooling with actual-symbol type authority reports. Existing 26-observation synthetic controls still pass. A preliminary agent classification incorrectly called the remaining SortedSet an immutable builder; coordinator source inspection rejects it, keeps the affected rows Open and prepares the compatible builder replacement. Native binding alone does not exempt retained local mutable snapshots from this task.

IMM-M376 merge verification: integrate master 1cdf105b02dbf8d9bd53b1f05cebc5f3185d469a and resolve the ApiUsageRankingTests conflict while retaining both proven-HashSet ICollection semantics and immutable helper contracts. Record four manually observed new-master host constructions: strict inventory capture, site/gap builders that freeze after Scan, and the fixed immutable membership-name vector. Mutable runtime HashSet/ICollection subjects and deliberately reimplemented comparers remain unchanged. Independent read-only review finds no merge defect; main verifies every change.

All 118 targeted Effects/Gates/Worker tests pass. The full semantic run observes 12740 passes and one corpus-update worker timeout across 23 summaries, with zero skipped tests or build warnings/errors. Rerunning exactly the failed workflow in isolation passes in 37 seconds; retain that timeout and the successful rerun explicitly rather than claiming a clean original run. Package validation passes 383 tests plus one expected unsupported-host skip across 39 shards. Layout passes 2680 source files, 59 evaluated projects and 151 pinned common files. All 4263 raw/interpolated fixture tokens match integrated master across 1032 changed C# files. Script formatting changes zero files. The current unit manifest conserves 1933 tracked C# paths; the new native-interface test has an explicit pending current-compiler-census state.

IMM-M380/M382 source ownership reconciliation: main traces 21 exact Gates/compiler rows to eight Gates and eight compiler scoped immutable builders, two Gates native entry/delegate boundaries and three compiler stream/IL native boundaries. Transaction entries freeze before marker publication; failure paths snapshot only after producer unwinding. Recovery/visited/membership/deconstruction builders remain local. Source collection freezes roots/callees/bodies after all synchronous producers finish. NormalizeTargets intentionally transforms an owned instruction builder before freezing; replacing it with freeze/rebuild would add unnecessary copying. The native allocation delegate reuses its prepared array through warmup and GC measurement. Main corrects an agent's misidentification of the HasUncollectedCall deconstruction work builder as a retained-body set. Each resolution records exact current source authority, without transferring unknown historical scanner statuses. Remaining Open coordinate candidates: C#338, PowerShell505.

IMM-M373 tooling controls pass 33 semantic observations, two compiled units, zero-candidate conservation, coordinate-shift-stable IDs and duplicate ambiguity. Actual-symbol side reports distinguish source record/init properties, mutable source setters and metadata types. A reference-only assembly byte change changes compiler context while preserving source hashes and structural observation IDs; every synthetic metadata reference has an exact file SHA256. Preserve the original M359 auditor source independently. Future collections export their complete auditor source alongside evidence; no new census authority is retroactively assigned to earlier results.

IMM-M386 visibility/reference merge: integrate master 11a70a2f15c73fde56c14ef2918c9c9e0dff9d04. Resolve three files by retaining the immutable architecture map with all four changed dependency edges, combining internal visibility with sixteen sealed protocol records, and preserving/deduplicating the 28-member IR friend union. Independent source review confirms all edges, internal partial declarations, protocol converters and the hand-maintained model authority. Canonical compilation then catches a real interaction missed by source review: new Frontend.Test friendship exposes the IR helper alongside its old linked ImmutableDictionaryCapture source. Remove that redundant Compile item, preserving the shared helper and strict duplicate-key semantics. Preserve the failed 36-error build evidence; the subsequent full build and gates pass with zero warnings/errors.

All 12741 semantic tests pass across 23 summaries with zero failures/skips. Package workflow passes 383 tests plus one expected unsupported-host skip across 39 shards. Layout passes 2681 source files, 59 evaluated projects and 151 pinned common files. All 4263 raw/interpolated fixture tokens match integrated master across 1032 changed C# files. C# formatting produces an empty patch and script formatting changes zero files. The source unit manifest conserves 1934 tracked C# paths and explicitly flags current source drift and pending compiler authority; it does not transfer the old compiler census to the visibility changes.

IMM-M384 source reconciliation closes 22 exact Dataflow/Architecture rows after main checks the source scopes and producer/consumer paths: twenty scoped immutable builders and two intentionally mutable JoinCallCount observations. Fixpoint/transfer/edge/generator returns freeze before publication. Architecture helpers freeze returned inventories; completed local assertion builders have no later producer writes. The counters remain mutable because tests assert exact observed callback counts. Remaining C# Open coordinate candidates: 316.

Correct two earlier shallow PowerShell attestations in the release-manifest inventory: immutable dictionary publication still contains owned mutable hashtable values. Reopen those exact rows rather than claiming deep immutability before applying the prepared readonly-record fix. Native preview controls already prove both developer plans byte-identical and six actual manifest cases equivalent in TSV bytes, decoded JSON, acceptance and exact validation messages; four readonly inventory mutation controls pass. JSON object key order is explicit in the preview. This implementation remains preserved for the next batch, not included in this merge checkpoint. PowerShell Open coordinate candidates: 507.

Preserve the fourteen-file M370/M371/M377/M378/M379/M381 batch with exact Git patch and per-file SHA256 before the visibility merge. Master touches none of those paths, so exact restoration is possible after this checkpoint. Additional symbol preview controls pass nine real portable-PDB and nine facade archive cases, including duplicate/missing embedded records, payload hashes/lengths, XML cardinality and unsafe executable content. Those previews are not claimed as applied changes here.

### IMM-M389: immutable helper inputs, coverage snapshots and package wire records

Migrated production Present/namespace inputs, corpus publication, callable snapshots, private Worker source/byte inputs, coverage query snapshots and nested owned vectors. Builders remain scoped until publication. Package inventories now freeze nested ordered records; symbol validation uses duplicate-preserving immutable set/map builders and immutable query snapshots. Native reflection, stream and parser adapters retain their required behavior. The coordinator checked source and caller contracts and reconciled 17 C# plus 25 PowerShell rows with current source hashes; 299 C# and 482 PowerShell rows remain open.

Validation: 4 Analyzer controls, 1 protected Present control and 119 affected Worker tests pass. Full Linux amd64 Release gates pass 12,742 semantic tests (23 project summaries), 384 package tests plus one expected skip (39 shards), and layout (2,681 sources, 59 projects, 151 pinned files). Native controls cover coverage projections and mutation rejection, Debug/Release plan JSON, six package archive cases, nine Portable PDB cases and nine facade cases. Lock JSON preserves decoded values with explicit ordered keys. Literal conservation preserves 4,263 raw/interpolated tokens across 1,035 changed C# files against master 11a70a2 (later integrated master changes are .gitignore-only). Earlier guard/format failures were repaired before these gates. Current source inventory remains 1,934 C# units; the older compiler census is explicitly distinguished from current source hashes.

### IMM-M390 / M392 / M398: exact compilation contracts and current ownership review

The coordinator migrated both exact-assembly-name TestCompilation helper overloads to initialized immutable tuple vectors under the distinct CreateExact name. The complete 305-call syntax inventory conserves 125 exact-name calls across 106 files and 180 unchanged suffixing calls. Actual overload probes and independent read-only review checked the dispatch; nine new boundary cases preserve guard order, empty/default handling, exact names, duplicate paths, source order and suffixing behavior.

Linux amd64 Release validation passes 12,751 semantic tests with zero failures/skips across 23 project summaries; package validation passes 384 tests plus one expected skip across 39 shards. Literal conservation preserves 4,288 raw/interpolated tokens across 1,044 changed C# files. Current source inventory is 1,935 paths; older compiler-census evidence remains explicitly separate.

Current-source review reconciles 64 PowerShell rows (46 already frozen builder/record sites, three scoped cache/classification builders, ten scalar accumulators/cursors and five direct Git boundaries), plus 40 C# rows (19 Golden text/immutable builders, 13 oracle builders/cycle caches, seven protocol builders/native file fixtures and the migrated compilation input). The coordinator traced construction, consumption and publication and recorded exact source hashes. Borrowed parser records are not claimed deeply immutable. Remaining coordinate candidates: 259 C# and 418 PowerShell; 27 coverage gaps remain open.

### IMM-M393: prepare authenticated final compiler recollection

The ignored census tool now exports explicit TypeId/AssemblyIdentity on residual observations and ContextSha256 on compiler-path rows. Conservation requires reference/type/residual reports for every compiler context, including generated-only contexts, exact residual-to-type joins, source-path authority, canonical aliases and authenticated metadata SHA256. Missing/unhashed references are retained as explicit gaps and fail the final gate. Synthetic real-compiler controls preserve 33 observations and two units, all type/reference property multisets under a metadata-only byte change, and 31 exact type joins. Eleven independently corrupted-report cases are rejected. A real generated-only project conserves one context, zero tracked units and zero residual/type rows. These controls are preparation, not a claim that the final current repository census has run.

IMM-M390 layout also passes: 2,682 source files, 59 evaluated projects and 151 pinned common files.

### IMM-M399 / M400 / M401: integrate master default-comparer models

Integrated master 1b292a43f (#345), resolving five conflicts in API ranking, replay configuration/interpreter state, SMT model decoding and callable replay. New callback configuration is init-only or constructor-captured. Comparer maps, ordered cache publication and nested view maps use scoped immutable builders; the new ProofKernel observation map freezes after callback evaluation. Duplicate rejection, cache insertion order, reference identity, cancellation and concrete opaque-value propagation are retained.

The actual comparer regression caught a non-textual merge defect: upstream ReferenceFacts.Add targeted this branch's immutable projection and discarded the result. Routing the append through the existing AddReferenceFact builder restored the non-null constraint and all eight model cases. New artifact tests now construct the same six malformed/valid payloads through member/term builders and record copies, and retained replay arguments freeze before final params adapters. The first constructor build also enforced CancellationToken-last (CA1068); all listed final gates run after its repair. Earlier failing logs remain separate evidence.

Validation: Smt 8, Effects API inventory 47, Worker 1,381 and Gates 21 targeted cases pass. Complete Linux amd64 Release gates pass 12,821 semantic tests (23 summaries, zero failures/skips), 384 package tests plus one expected skip (39 shards), and layout (2,688 sources, 59 evaluated projects, 151 pinned files). Literal conservation preserves 4,289 raw/interpolated tokens across 1,048 changed C# files against 1b292a43f. Current source inventory is 1,941 paths; final actual-compiler recollection remains pending.

Added 14 independent current-master observations, including one new Open retained IL-byte evidence vector; native API origin alone does not excuse a retained mutable owner. M400 preserves five DictionaryAnalyzerConfigOptions mutable-producer capture contracts: actual tests mutate producers after construction and verify snapshot/comparer behavior. M396's proposed overload removal was rejected because prefreezing at test call sites would make those controls vacuous. Current coordinate candidates: 255 Open C#, 420 Open PowerShell, 27 Open coverage gaps.

M403 preparation reopened a shallowly verified governance receipt row and added its missing wire-root row. A source preview freezes nested ordered receipt records, root, output/path vectors and a Windows package vector; eight actual extracted-function native argv/output/exit/diagnostic cases, three actual wire projections and package selection/binding controls pass. Those script source edits remain pending and are not claimed implemented here.

M402 extends final-census preparation with recursive symbol identities (including generic-owner and array-element authority), explicit collision rejection and discarded immutable-update detection. Real compiler controls pass 36 observations/two units, exact type joins, metadata-only conservation, eleven corruption rejections and one generated-only context. This does not replace the pending final repository census.

### IMM-M407: immutable fixture inputs and evidence ownership

Applied sixteen reviewed source files: immutable differential-oracle maps, effect-region vectors, exact metadata/strong-name image snapshots, Worker project source tuples, protocol assertion/hash inputs, fuzz case/shift vectors and retained reflection envelopes. Scoped immutable builders collect additions before publication. Nested mutable CLR arrays, tampering buffers, native reflection frames, cancellation, ordering, comparers and duplicate behavior retain their roles. The first Worker build caught an ImmutableArray.AsSpan overload mismatch; body.AsSpan(1..) repairs the adapter, and all subsequent listed gates pass. The exact typed-width seed closure preserves 20,006 seed/name/source/claim/input comparisons; the explicit full fuzz campaign is not claimed executed.

Linux amd64 Release validation passes Attributes 3, Effects 26, Testing 23 and Worker 217 targeted cases; complete semantic validation passes 12,821 cases across 23 summaries with zero failures/skips. Package validation passes 384 cases plus one expected skip across 39 shards. Layout passes 2,688 source files, 59 evaluated projects and 151 pinned common files. Source inventory conserves 1,941 tracked C# paths and keeps historical compiler drift explicit; final authenticated compiler recollection remains pending.

Applied the governance/Windows package-vector migration from M403: output/path snapshots, read-only nested ordered receipts and read-only wire root. Previously captured eight native-helper cases, three zero/one/four wire projections and package-selection/native-binding controls preserve values, bytes, order and exceptions. Full actual governance workflow remains pending the restoration-baseline follow-up. Windows-native acceptance execution is not claimed on Linux.

The coordinator reconciled 88 exact C# rows and 18 PowerShell rows against current source ownership, preserving native/SUT exceptions explicitly. Added two independent C# observations: the already-migrated subject tuple image and an Open concurrent analyzer exception collector. Corrected another earlier shallow PowerShell closure: immutable API baseline outer vectors still own mutable byte arrays. Reopened that row and recorded three additional read-only project/source/DLL restoration byte roots. These baselines can freeze; the actual corrupted files/clones remain mutable. Counts now stand at 168 C# Open, 406 PowerShell Open and 27 Open coverage gaps. No historical status transfer is used.

Pending previews are separate from applied work: M408 symbol byte evidence/provider inputs, M409 sample/tutorial vectors and records, M410 atomic immutable callback error collection, M411 restoration baselines, and M412 coverage helper/result vectors. M409 actual native controls already preserve sixteen sample scenarios, two restore/build property cases, eight tutorial scenarios, ordered case metadata and strict missing-claim lookup; both sample helpers also preserve rejection of null/empty/invalid-element vectors. Those preview controls do not claim the source migration or the complete native workflows have finished.

M407 literal conservation also passes: all 4,289 raw/interpolated fixture tokens match integrated master 1b292a43f across 1,051 changed C# files.

### IMM-M413: native script snapshots, immutable byte evidence and atomic callback collection

Applied nine reviewed source files from M408-M412. PDB evidence and restoration baselines freeze immediately after native reads; provider inputs use immutable images. Native Encoding/File/SHA256/reflection boundaries adapt at final calls. Actual corrupted checksum/PE/DLL buffers remain mutable subjects. Samples/tutorials freeze argv, output, inventories, case records, transcript and claim vectors, with read-only result records. Coverage freezes retained parser/filesystem/query/result vectors and narrows private helper contracts; unary commas preserve immutable values across pipeline/conditional publication. The range-hit replacement retains Measure-Object's double result type as well as null/zero/maximum behavior. Native analyzer callback failures now use atomic ImmutableStack publication, preserving serial LIFO observation without a shared unsynchronized builder.

Actual formatted-source validation passes 73 affected Worker analyzer/runtime/exact-replay regressions. Native controls preserve sixteen sample output/argv/exit/assertion scenarios, null/empty/invalid-element rejection, both restore/build property vectors, and both complete eight-project sample inventory functions. Eight tutorial native scenarios preserve byte logs, argv and exits; all eight case records retain values/order and the exact strict first/missing claim lookup. Coverage controls preserve ordinal/default/zero/one/many results, thirty semantic-line cases plus empty input, nine exact-credit cases, fifteen source/hash/authority cases, native source-root order/duplicates, measurement JSON and scalar range maxima. Real AST classifier zero/multiple request cases preserve result path/declaration order; real csc and classifier-process failures preserve diagnostics and rejection.

Complete actual script-tool, coverage-evidence, source-document (20 checks), and embedded-symbol regression workflows pass. Public API governance runs the four actual builds successfully: baseline, missing API files with RS0016, oblivious API with RS0041, and restored files. Three extracted actual Windows baseline/clone/XOR/write/finally statement cases preserve every byte and distinguish immutable restoration evidence from the mutable corruption subject; Windows-native proof acceptance is not claimed on Linux. Full sample/tutorial consumer execution remains part of final native verification.

The coordinator reconciled fourteen exact C# rows (including atomic callback collection and thirteen current native/scoped-builder ownership traces), seventy-two PowerShell rows, and six newly recorded owned byte/record roots against exact current hashes. Main preserves the CorpusSnapshotFormat byte-ingress shim because its existing ReadOnlySpan core retains no alias and replacing the shim would alter its ArgumentNull contract. Current counts: 154 Open C#, 334 Open PowerShell and 27 Open coverage gaps. Source inventory still conserves 1,941 paths; historical compiler evidence remains separately identified pending final authenticated recollection. M407's complete semantic/package/layout gates are recorded above; the only subsequent C# edit is the independently tested callback collector and changes no raw/interpolated fixture tokens.

### IMM-M414: integrate master CI scheduling

Merged master cf42dd52dcc1311ff5f03daa1679e6f833fb58ca (#346), resolving both PowerShell conflicts. Immutable nested prefix groups, descriptor records and cohort task snapshots preserve the upstream four-shard filters, names, slot reservations and receipt expectations. Exact native controls compare six coverage/parallelism policies; 646 current/fixture/future class names each select exactly one shard. Both canonical cohort task vectors match upstream. Refreshed generated source-reference documentation from the canonical generator.

Canonical Linux amd64 repository-gates passes documentation, container contract, layout (2688 sources, 59 evaluated projects, 151 pinned files), inventory, facade, embedded-symbol, release-evidence/attestation fixtures and actual public API baseline/missing/nullable/restored controls. This is source/merge validation; final semantic/package gates and authenticated census remain pending. Open audit counts remain 154 C#, 334 PowerShell and 27 coverage gaps.

### IMM-M415 / M416: worker publication and helper boundaries

Worker lane membership is immutable. Fixed-count concurrent result slots use a scoped immutable array builder, locked for every write and snapshot; backend renewal uses a separate per-run shared gate. Retired lanes, cancellation, duplicate backend rejection, disposal/publication order and callable output ordering remain intact. Private guard/reflection/type/witness/image inputs, fixed assertions and payload baselines use immutable collections and builders. Fifteen byte-image callers pass immutable emissions directly, while four stream callers retain stream behavior. Guard discovery uses an append builder with a FIFO cursor.

The coordinator independently reconciled 76 current C# observations: 18 migrated sites, 27 scoped immutable/string builders, 18 final native frames and 13 intentional mutable subjects/buffers. Four superficially similar retained roots remain Open: three PE-byte vectors reused for hashing and a shared invocation argument vector. Two mutable passive clause lists and ordered evidence dictionaries also remain Open. Current inventory: 78 Open C#, 334 Open PowerShell, 27 Open coverage gaps; 1941 C# source paths conserved. Final authenticated compiler recollection remains pending.

Release semantic validation compiled cleanly and passed 12809 cases across the actual four-shard scheduler, but failed twelve pre-existing embedded PowerShell integration controls after the earlier M413 coverage helper migration. Restored borrowed enumerable ingress and legacy query/ordinal pipeline shapes; owned inputs/results still freeze in scoped builders or at assignment/publication. All twelve unchanged actual embedded fixtures pass their original assertions, and both affected .NET classes pass all 20 cases after the repair. C# fixture literal contents remain unchanged. The initial missing-guard/formatting builds and failed complete run are retained as failure evidence; a new complete semantic gate remains required after this repair and subsequent source changes. Script formatting passes with zero changes. Four new current-source compatibility attestations record the repaired ingress/egress rather than masking mutable fixture controls.

### IMM-M417: retained evidence and ordered builders

Three floating-predicate PE vectors freeze before asynchronous file output and subsequent hash evidence. A reusable reflection argument vector is immutable, preserving object identity with final native invocation frames. Two passive clause lists use scoped immutable builders. Checksum evidence uses ordered immutable-entry/index builders; serialization freezes entries before writing. Overwriting a key retains its original position, preserving exact JSON property order and partial-failure evidence.

All 111 affected actual Worker tests pass in Linux amd64 Release, including both checksum workflows and the floating/reflection/passive controls. The exact ordered helper passes 2002 byte-identical JSON comparisons with the former dictionary, 2000 lookups/overwrites, nested immutable values, partial-failure fields and snapshot conservation. Canonical C# formatting produces an empty patch. Eight exact current-source observations are reconciled; remaining counts are 70 Open C#, 334 Open PowerShell and 27 coverage gaps. Full final semantic/package gates, authenticated census and coverage remain pending.

### IMM-M418 / M419: formatter inputs and synchronization evidence

The private C# formatter helper accepts existing immutable argument snapshots directly; its builder appends verification options before the final native frame. PowerShell literal-token evidence is immutable. Native CLI binding/argv and an immediate boolean filtered-count expression remain reviewed boundaries/scalars. Eight exact formatter argv/timeout controls and eight actual pinned token-aware formatting cases pass. Full script-tool regressions pass; all 87 PowerShell files and 19 shell scripts verify with zero formatting changes.

Nine synchronization audit units now freeze emitted PE, manifest and retained native IL bytes. Hash and decoder inputs are initialized immutable arrays with readonly native span adapters. Source programs and actual synchronization/field mutation subjects remain unchanged. All 20 affected actual Worker cases pass in Linux amd64 Release. Representative exact extracted helpers preserve 2049 PE/SHA cases, five old/new IL vectors and four named null/default guards. Canonical C# formatting produces an empty patch.

The coordinator reconciled 19 C# and five PowerShell observations, and explicitly recorded 22 additional retained manifest/IL byte roots missed by the historical construction-only inventory. Native API origin is not an exemption for retained owned evidence. Remaining coordinate candidates: 51 Open C#, 329 Open PowerShell, 27 Open coverage gaps. Current C# source inventory remains 1941 paths; final compiler/type/reference recollection, complete semantic/package gates, authenticated coverage and exact-commit packaging remain pending.

### IMM-M420: reconcile the remaining C# coordinate backlog

Ten source units replace remaining owned array-oracle inventories, clause/diagnostic/boolean collectors, repeated marker vectors, callable lookup, scalar inputs and nullable cache payload vectors with immutable collections or scoped builders. Actual mutable CLR array elements, clones and mutation/identity subjects remain unchanged. Null versus empty cache JSON retains its rejection behavior. All 165 affected Worker cases pass in Linux amd64 Release; canonical C# formatting produces an empty patch.

The coordinator individually traced all 51 remaining C# coordinates: 13 migrated observations, 18 intentional mutable/concrete-runtime subjects, ten final native/string/file frames and ten scoped immutable/string builders. Every decision records the current source hash; no historical status transfer is claimed. The existing C# coordinate ledger now has zero Open rows. This does not establish final exhaustive C# completion: a fresh authenticated compiler/type/reference census and current-source reconciliation are still required. PowerShell remains at 329 Open rows and coverage at 27 gaps; complete final semantic/package gates and exact-commit native workflows remain pending.

### IMM-M421: fresh authenticated compiler census

The real Release compiler census at a5d4694e165af93b498b69bb6b67e69883a111c9 passes with 1941 tracked C# paths conserved: 1730 compiled and 211 explicit absent subjects/helpers, 1879 unit instances, 49 tracked contexts and two generated-only contexts. It records 141278 semantic and 75197 const observations. Exact type/reference authority covers 16269 types and 139263 residual joins with zero metadata gaps. The 136 explicit unbound records remain visible (114 catch declarations without a local binding and 22 collection expressions requiring binding review). There are zero eligible const locals; all twenty field candidates were independently checked against current runtime writes, setup, session/configuration or serialization lifecycle behavior.

Fresh shape labels are hypotheses, including broad framework-namespace labels on readonly KeyValuePair carriers and exceptions. No automatic historical status transfer or blanket ownership exemption is applied. The old census export is preserved separately, and the new raw export retains its actual auditor sources and hashes. Lossless historical/current archive extension and complete fresh role reconciliation remain pending.

Current source review found two real readonly-reference gaps in static exception-constructor HashSet/Dictionary tables and two immutable fixture self-updates suitable for builders. Four new Open observations record these independently; one disposable cache Clear transition remains explicitly preserved. Exact old/preview lookup controls already conserve all 55 set IDs and 159 map IDs through 860 ordinal membership/value probes and reject writes to both immutable publications. The prepared source changes are not yet applied at this checkpoint. Remaining Open counts: C# 4, PowerShell 329, coverage 27.

### IMM-M422: correct the independently found constructor tables and fixture edits

Static callback-free and null-message exception-constructor lookup tables now publish ImmutableHashSet/ImmutableDictionary data. The dictionary factory uses a scoped immutable builder with ordinal keys and the former indexer overwrite semantics. Two fixture edits use scoped builders before publishing immutable replacements; their negative duplicate-ID and immutable-origin observations remain intact. The disposable native allocation cache reset is unchanged.

Exact extracted old/new controls preserve all 55 set IDs and 159 map IDs across 860 ordinal membership/value probes and reject mutable-interface writes. All 2147 Frontend tests and 215 affected Worker cases pass in Linux amd64 Release; C# formatting produces an empty patch. The four independent current-census findings are reconciled. Three source hashes are now explicitly marked changed since the a5d4694e1 census; a final recollection remains required. Current Open coordinates: C# 0, PowerShell 329, coverage 27. Broad ownership/array review, historical archive extension and final native/full gates remain outstanding.

### IMM-M423: immutable container command publication

The command entrypoint constructs argument maps with ordinal-ignore-case immutable builders, freezes each published map, and stores fixed maps as readonly ordered records. Retained project, report, shard and authority vectors are immutable; authority append operations use scoped builders. Native argv ingress preserves null versus empty and the original null extra-slot behavior.

Coordinator controls pass against formatted actual source: 24 helper map cases, 384 actual branch map combinations, three actual build argv cases, eighteen zero/one/many vector expressions and four coverage-authority gate cases. Full script-tool regressions pass with zero formatting differences, and repository gates pass through the updated entrypoint, including actual public API baseline, missing-file, nullable and restored builds. The first formatter run encountered a PSScriptAnalyzer null-reference exception; isolated analysis and the full rerun pass. Twenty-seven PowerShell observations are independently reconciled with the current source hash. PowerShell has 302 Open rows, C# coordinate rows remain at zero, and coverage has 27 Open gaps. Final current-source ownership reconciliation, compiler recollection, complete semantic/package gates and exact-commit native workflows remain pending.

### IMM-M425: integrate managed comparable and immutable pair master changes

Master b0ce85e23544140698dda435f57dd5089411fc8a adds constrained comparable receiver semantics and runtime evidence for immutable KeyValuePair getters. The single conflict in RoslynTotalProgramLowerer preserves incoming managed-value receiver tracking and the local immutable pass-state construction. All 2168 combined-source frontend cases pass with zero build warnings or errors. The affected native Worker suite is running; no passing Worker result is claimed yet.

The source universe now contains 1948 tracked C# paths. The unit manifest preserves the authenticated a5d4694e census separately and explicitly marks new or changed source; incoming files receive no inherited compiler authority. Independent incoming-source review records nine Open collection/evidence observations for subsequent correction. PowerShell remains at 302 Open rows and coverage at 27 gaps. Full final gates and recollection remain pending.

### IMM-M424: immutable container authority observations

The container-contract validator freezes retained line/stage inventories, fixed authority records and declaration observations. A scoped ordinal-ignore-case immutable dictionary builder replaces declaration records and preserves first-match text/index and duplicate counts. Enumerable ingress retains PowerShell array binding compatibility; each helper immediately freezes its owned snapshot, and stage slices publish immutable vectors.

All 126 exact Dockerfile/Compose outcomes and six stage-slice controls pass against formatted actual source. The full actual container contract and script-tool regressions pass. Nineteen independent PowerShell coordinates close, leaving 283 Open. The incoming-master Worker run also finishes successfully: 1500 actual native Worker cases pass, with zero build warnings or errors, alongside 2168 frontend cases. Nine incoming C# observations and 27 coverage gaps remain Open; final comprehensive verification continues.

### IMM-M426: migrate incoming comparable construction and retained pair evidence

Six incoming-master source units use immutable receiver-map builders, branch/fixed vectors, observed havoc messages, runtime-reference paths and getter IL evidence. Default equality, native reflection frames and intentional mutable receiver/callback observations remain intact. The current-source review found one further retained reference inventory, bringing this batch to ten independently reviewed observations.

All 2168 frontend cases, 89 affected Worker cases, ten runtime-specification cases and one effects case pass with clean builds. The earlier narrower Worker selection passed eight cases; the expanded 89-case selection covers the complete constrained/primitive comparable behavior. Exact literal conservation against master b0ce85e23 verifies 4291 raw/interpolated tokens across 1062 changed C# files. Ten coordinates close, returning the C# ledger to zero Open rows while current ownership review and final compiler recollection remain outstanding. The 1948-path manifest preserves new/changed source independently of the earlier authenticated compiler snapshot. PowerShell remains at 283 Open rows and coverage at 27 gaps.

### IMM-M427: immutable execution-helper construction and coverage publication

Fixture Git argument construction uses scoped immutable builders. The fixed parallelism policy tree and build environment map are readonly records; policy override vectors are immutable and recursive policy conversion preserves zero/one/many publication. Coverage ranges freeze at ingress, line credits publish immutable arrays, and the validator retains that result directly. Existing native process/Task frames, scoped immutable scheduler builders and slot counters preserve their roles. Both timing consumers already pass immutable array builders through the compatible append interface.

All 22 exact fixture/policy/coverage comparisons, four immutable credit publications, empty-universe rejection, two actual timing-builder ingress cases and the actual parallel-build process adapter pass against formatted source. Full script-tool regressions and all 22 actual .NET coverage cases pass. Twenty-six historical coordinates close: six migrations, ten native/compatibility boundaries, nine builder/scheduler states and one scalar false positive. Two newly observed policy/publication roots receive separate current attestations. PowerShell now has 257 Open rows; C# remains at zero coordinate rows and coverage at 27 gaps. Final ownership reconciliation, compiler/coverage collection and full native gates remain outstanding.

### IMM-M428: immutable reusable consumer evidence and arguments

The plug-and-play consumer stores its package-file inventory and reusable build arguments in immutable arrays, extends the failure command with a scoped builder, and publishes readonly command-result records. Native ProcessStartInfo argument/environment setup and process lifecycle remain intentional API frames. All 28 native stand-in command comparisons pass on Linux across SDK and Visual-Studio mapping branches with success/nonzero exits, exact argv/environment/output and readonly result publication. Three zero/one/many file inventories and actual build/failure vectors also pass. Full script-tool regressions pass with zero formatting differences.

Seventeen historical coordinates close (three migrated construction observations and fourteen native frames), with a separate new current attestation for the passive result record. PowerShell has 240 Open rows; C# has zero coordinate rows and coverage has 27 gaps. These controls do not claim native Windows acceptance or final authenticated exact-commit package consumer execution; those remain pending with the other final gates.

### IMM-M429: immutable package consumer construction and framework records

Package, symbol, version and generated source/config inventories are immutable. A scoped builder constructs the final native identity argv. Nine owned records, including the nested SDK JSON and framework package/result descriptors, are readonly. Seven newly observed passive records receive independent current attestations. Property projection explicitly extracts Pattern before Select-Object uniqueness: direct ExpandProperty does not read dictionary keys, and the current controls caught and corrected that consumer adaptation.

Nine actual framework-feed outcomes pass with real nuspec ZIP parsing, native file copies and exact output archive hashes. SDK query and download responses are explicit injected protocol boundaries. Eighteen source/config constructions, two SDK JSON byte comparisons, six actual package/symbol inventory prefixes and two null/value version shapes match previous behavior; all retained new vectors are immutable. Full script-tool regressions pass. Sixteen historical rows close, leaving 224 Open PowerShell rows, zero Open C# coordinates and 27 coverage gaps. Final exact-commit package/native consumer acceptance and the other comprehensive gates remain pending.

### IMM-M431: repair incoming Gates helper integration

The full Release semantic attempt at 18f411100 stops during compilation with three argument-type errors in the new incoming ApiUsageRankingTests comparable case: a mutable dictionary and two lists no longer match the migrated Scan contract. The test now uses an ordinal immutable inventory builder/snapshot and scoped site/gap builders, freezing observations before assertions. Current review also replaces a retained invocation syntax array. Four new observations are recorded and independently closed; no old coordinate status is inherited.

All thirteen actual ApiUsageRankingTests cases pass, and literal conservation still verifies 4291 raw/interpolated tokens across 1062 changed C# files against master b0ce85e23. The 1948-path unit manifest refreshes the changed source hash without transferring compiler authority. The full semantic gate has not yet passed at this checkpoint; restart follows semantic-script validation. PowerShell has 224 Open rows, C# has zero Open coordinates, and coverage has 27 gaps. Final ownership/binding reconciliation, compiler/coverage collection and native package gates remain pending.

### IMM-M430: immutable semantic task construction

Project/task snapshots, build/discovery inventories, copied task-property records, callback environments and timing vectors use immutable collections or scoped builders. Nested solution records are readonly. Native Sort-Object descriptors remain immediate API frames. The current source preserves project spelling/duplicates, shard selection, process slots, estimated-duration ordering and serialized timing bytes.

Exact controls pass for 72 project/build combinations, three task groups, native estimate/name ordering, six discovery inventories, 64 actual prepare argv/environment combinations and six timing JSON byte cases. Independent shard controls preserve four filters/names/descriptors across six coverage/slot policies; 647 class names each select exactly one shard, and both canonical task vectors match. Full script-tool regressions pass. Fifteen historical coordinates close, with eight separate current ownership/native-boundary attestations. PowerShell now has 209 Open rows, C# has zero coordinate rows and coverage has 27 gaps. The full semantic gate is about to restart with the M431 Gates integration repair; no passing full gate is claimed yet. Final census/coverage reconciliation and native package workflows remain pending.

### IMM-M432: immutable Visual Studio consumer observations

Visual Studio selection/candidate, package-file and reusable build vectors are immutable. Native vswhere/MSBuild adapters retain the exact argument frames and exit-code capture, and command results publish readonly records. Twelve native Linux engine stand-in cases and 24 native vswhere cases preserve versions 17/18, both separators, zero/one/many candidate output, success/failure and exact argv/environment/output. Three file inventories and reusable build construction also match; full script-tool regressions pass with zero formatting changes.

Fourteen old rows close (four migrated roots, nine native frames and one scalar false positive), with one separate new result-record attestation. PowerShell now has 195 Open rows; C# remains at zero coordinate rows and coverage at 27 gaps. Native Windows acceptance is not claimed by Linux adapter controls. The complete semantic run at d655f6049 built successfully and is still executing; completed shards are passing, but no final full semantic result is claimed yet. Current-source ownership/census/coverage reconciliation and native package workflows remain pending.

### IMM-M433: immutable lint observations and case inventory

Ten retained native-output/diagnostic/result/file roots and the fixed baseline/current case inventory are immutable. Native argv ingress and invalid/mutable compiler/tool subjects retain their behavior. Twelve actual zero/one/many stdout/stderr/rejection outcomes preserve native exit codes and observed text. Full canonical native regressions pass for actionlint, zizmor, ShellCheck, markdownlint, CSpell, PSScriptAnalyzer, all five link-retry cases, the production lint runner, container authority and real baseline/current package builds with ApiCompat rejection. Full script-tool regressions pass with zero formatting changes.

Eleven old rows close (ten migrated snapshots and one native ingress), plus one new independent fixed-case attestation. PowerShell has 184 Open rows; C# has zero coordinate rows and coverage has 27 gaps. Full current ownership, compiler/coverage reconciliation and final native workflows remain pending.

### IMM-M434: complete ordinary semantic and package checkpoints

The complete canonical Linux amd64 Release semantic run from source snapshot d655f604943c000e79554c2a5dfebdcf81b89b41 passes all 12944 cases with zero failures/skips across 25 project summaries and eight isolated tasks. It uses the ordinary canonical filter excluding Performance, Coverage and Corpus. This validates the M431 incoming Gates helper repair within the full suite. The subsequent Windows-helper commit does not belong to that private source snapshot; no final-current-head claim is inferred.

The complete package gate at clean pushed HEAD 2d3456b4a37be816038f2cbd4409d159a312b1e2 passes 384 cases with zero failures and one expected skip across 39 shards. Both source and HEAD stay unchanged throughout that run. The skipped VerificationFailsExplicitlyOnUnsupportedHosts case explicitly skips on supported canonical Linux amd64, as confirmed in WorkerMsBuildIntegrationTests. These are verified intermediate checkpoints, not final-commit evidence for later script changes. The latest raw/interpolated conservation remains 4291 tokens across 1062 changed C# files against master b0ce85e23. Final compiler/coverage collection, archive/ownership reconciliation, final exact-head native workflows and platform acceptance remain pending.

### IMM-M435: fresh current compiler authority and retained-array review

The actual compiler census at clean frozen HEAD fe9144dde6e04b6c0c0231797171afdee5e68e80 passes with 1948 source paths conserved: 1737 compiled, 211 explicit physical/dynamic subjects/helpers, 1886 unit instances and 141745 semantic observations. There are 75423 const observations, zero eligible locals and twenty independently rechecked runtime-state/configured-readonly fields. Type/reference authority covers 51 contexts (49 tracked and two generated-only), 16291 types and 139723 exact joins with zero metadata gaps. The 136 explicit unbound observations remain visible for review.

Current shape labels remain hypotheses. Deduplicated review retains 524 array-root/materialization sites in 245 units and 92 non-value framework construction observations in 25 units; final native adapters and intentional mutable subjects require individual classification. Direct current-source review independently identifies six additional retained result/byte/enum inventories in Analyzer tests. They are recorded Open, with no inherited coordinate status. C# now has six Open observations; PowerShell has 184 and coverage 27. The sole immutable self-update is the required disposable assembly-cache lifecycle reset. Full ownership/binding review, archive extension, authenticated coverage and final native workflows remain pending.

### IMM-M436: freeze newly observed Analyzer inventories

Five Analyzer test units freeze six retained concurrency-result, byte and enum inventories. The source hash reads a readonly span; the backing-file write retains its final native byte-array adapter. Actual mutable analyzer sessions, file/compiler subjects and callback observations remain unchanged. All 86 focused cases and all 1137 Analyzer cases pass with zero build warnings/errors, and literal conservation verifies 4291 raw/interpolated tokens across 1063 changed C# files against master b0ce85e23.

Six independently observed rows close, returning the coordinate ledger to zero Open. The current array/ownership review is still in progress; zero coordinates is not exhaustive completion. The 1948-path manifest marks these five files changed since the fe9144dde compiler census rather than transferring authority. PowerShell has 184 Open rows and coverage has 27 gaps. Final ownership/archive/binding reconciliation, compiler/coverage recollection and final exact-head native workflows remain pending.

### IMM-M437: immutable production metadata/path observations and required publication mutation

Six production units freeze ten retained serialization-byte, IL/key, decimal/type and corpus/SARIF path inventories. Source consumers and final native frames preserve their contracts. The eleventh reviewed root must remain mutable: VerificationPublication.Prepare rewrites child argument slots to prepared paths at its indexer assignment. An attempted freeze before that operation discarded the rewrite and failed the cancellation/publication test with an existing-file error. The coordinator traced the mutation and restored the original argument frame, honoring the intentional-mutation exception.

After restoration, all 647 Contracts, 98 corpus/metadata, 123 Worker and 96 build-task/null-input cases pass. Exact literal conservation remains 4291 tokens across 1063 changed C# files. The eleven new observations close as ten immutable migrations and one intentional mutable transformation. The unit manifest marks the six changed sources independently of the fe9144dde compiler authority. C# has zero coordinate rows Open, PowerShell 184 and coverage 27; the current 524-site array/ownership review and final archive/binding/compiler/coverage/native reconciliation are still in progress.

### IMM-M438: immutable canonical receipt and case publication

Canonical TRX/discovery cases, completion items, receipts and summary records are readonly; retained payload and sorted vectors are immutable. Seven compatibility parameters retain native legacy binding while the function owns a frozen snapshot. The producer keeps its pipeline contract. Null input needs special handling: freezing null as empty initially removed an existing failure; preserving null separately restores exact legacy behavior while freezing real collections.

Actual TRX/discovery JSON and readonly records, four legacy null/empty/one/many input shapes and three actual receipt JSON byte comparisons pass using controlled private fixture Git/run identity. Existing canonical regressions preserve ordering, encoded collisions, name multiplicity, missing definitions and duplicate execution/discovery identities. Full script-tool/formatting checks pass. Twelve old coordinates close, with six independently observed owned records added. PowerShell now has 172 Open rows; C# has zero coordinate rows and coverage has 27 gaps. These fixtures do not claim real GitHub cohort authentication; final current ownership/archive/binding/compiler/coverage/native reconciliation remains pending.

### IMM-M439: immutable benchmark observations and source construction

Benchmark package/status/budget/timing inventories and fixed scenario/variant vectors are immutable; an immutable array builder constructs the eight method-source lines. Native measured argv/timers and scalar counts retain their roles. Five exact source outputs, four odd/even median/report outputs with sixteen budget selections, three package inventories and three actual Git status/exit captures pass. Full script-tool regressions pass with zero formatting changes. These are behavior controls, not measurements of a speedup or a fresh end-to-end benchmark run.

Eleven old rows close (four migrated roots and seven scalar/native/existing-builder or immutable roles), plus three independently observed construction attestations. PowerShell has 161 Open rows, C# zero coordinate rows and coverage 27 gaps. Current-source ownership/array review, archive/binding reconciliation, final compiler/coverage collection and exact-final-head native workflows remain pending.

### IMM-M440: immutable layout inventories and ordered report records

Eight retained inventories, four owned records and the fixed exclusion vector are immutable. Native evaluated SDK properties are copied into a scoped ordered builder and published readonly, preserving property order. Report payloads retain immutable snapshots directly. The Git inventory text remains a scalar; its split inventory is frozen.

Full canonical before/after layout validation passes for 2695 source files, 59 projects, 151 pinned common files and all 59 evaluated projects. Complete JSON report bytes match exactly. Full script-tool regressions pass. Ten old coordinates close with five independent owned-construction attestations; PowerShell now has 151 Open rows. C# coordinate rows remain zero and coverage gaps 27. Full current ownership/archive/binding reconciliation, compiler/coverage recollection and exact-final-head native workflows remain pending.

### IMM-M441: immutable JSON property and canonical path observations

Retained actual/expected JSON property vectors and split source/trusted-path components are immutable. The trusted-path producer publishes its existing immutable builder snapshot directly while preserving pipeline enumeration. Legacy typed ingress, readonly membership and scoped dictionary construction keep their contracts.

Twenty exact before/after property/path outcomes and failure texts match, including duplicate/case sensitivity, path rejection and output order. All twenty real source-document regressions and full script-tool regressions pass with zero formatting changes. Seven old rows close and three retained snapshots receive independent attestations. PowerShell now has 144 Open rows; C# coordinate rows remain zero and coverage gaps 27. Exhaustive current ownership/archive/binding reconciliation and final compiler/coverage/native gates remain pending.

### IMM-M443: immutable ZIP fixture construction and deliberate invalid subjects

ZIP content and verifier-file construction use scoped immutable dictionary builders with ordinal-ignore-case keys. Retained fixture bytes are immutable, with native hash/write adapters. The invalid inventory graphs remain mutable because the tests deliberately overwrite paths/digests, create duplicates and remove digest declarations before invoking the validator.

Actual complete before/after inventory regressions produce matching output and preserve all missing/unsafe/duplicate/altered/foreign/missing-digest/orphan checks. Full script-tool regressions pass with zero formatting changes. The first preview incorrectly invoked an extension-only ToArray from PowerShell; the final native byte-vector adapter fixes it. An ignored shell harness CRLF path failure was separately repaired. Thirteen old coordinates close plus one independent retained-byte attestation. PowerShell has 131 Open rows. Current corpus C# changes remain under verification; exhaustive ownership/archive/binding and final compiler/coverage/native gates remain pending.

### IMM-M442: immutable corpus IL, transaction and replay evidence

Five production units freeze eight retained IL, serialized/readback byte, call-marker encoding and concurrent replay-result inventories. IL decoding uses readonly spans; durable transaction writes use readonly memory/spans; serialization keeps final native write/encoding adapters. Missing/empty IL classification, transaction recovery and cancellation contracts remain intact.

All 76 corpus transaction/oracle cases and all 510 IR cases pass with zero build warnings/errors. Literal conservation verifies 4291 raw/interpolated tokens across 1063 changed C# files. Eight independently observed roots close, returning C# coordinate rows to zero Open. The source manifest marks these five changed units separately from the fe9144dde census authority. PowerShell has 131 Open rows and coverage 27 gaps; full current ownership/archive/binding and final compiler/coverage/native reconciliation remain pending.

### IMM-M445: immutable documentation inventories and change descriptors

Documentation source observations are immutable strings rather than retained mutable XML nodes. Fixed source/directory inventories are immutable; seven mutation descriptors are readonly records in an immutable vector. The files deliberately changed by documentation tests remain mutable. The existing line/anchor builders already publish frozen snapshots; source resolution keeps its scalar/native ingress contract.

Complete generated source-reference bytes match before/after. Actual drift, regeneration, idempotence, authored-prose, link and anchor regressions pass both before and after, plus twenty source-document checks and full script-tool regressions. Thirteen old coordinates close and twelve additional fixed inventory/descriptor roots receive independent attestations. PowerShell has 118 Open rows. Callback observation conversions remain under verification; full ownership/archive/binding and final compiler/coverage/native gates remain pending.

### IMM-M444: synchronized immutable construction for concurrent test observations

Seven callback/claim observation roots in six Worker test units use scoped immutable array builders with synchronized writes. Assertions consume frozen snapshots after awaited completion. The analyzer and scheduler remain the mutable subjects; their callbacks, claim accounting and retirement coordination retain their behavior.

All 27 affected Worker cases pass with zero build warnings/errors after correcting two formatting errors caught by the initial build. Literal conservation verifies 4293 raw/interpolated tokens across 1064 changed C# files; the source manifest independently marks all six units changed since current census. Seven new observations close, returning C# coordinates to zero Open. PowerShell has 118 Open rows and coverage 27 gaps. Full current ownership/archive/binding and final compiler/coverage/native reconciliation remain pending.

### IMM-M446: immutable evidence fixture vectors and manifest records

Canonical/coverage fixture and observed-result outer vectors are immutable. Thirteen fixed fixture/publication records are readonly. Coverage descriptor records deliberately overwritten by alias/architecture rejection tests retain shared mutable identity. Cohort and shard payloads use immutable vectors directly; native compatibility ingress is borrowed read-only and projected into existing immutable snapshots.

Actual canonical and coverage regressions pass before/after. Real cohort/shard JSON bytes match exactly and their hash/file-set verifier passes. Full script-tool regressions pass. The initial ignored control used the wrong nested cohort layout; it was corrected to the actual shard contract before comparison. Twelve old rows close and thirteen owned records receive independent attestations. PowerShell has 106 Open rows, C# coordinate rows zero and coverage gaps 27. Final exhaustive ownership/archive/binding and compiler/coverage/native reconciliation remain pending.

### IMM-M447: complete framework-construction ownership classification

The current fe9144dde candidate universe contains 92 non-value framework construction observations, deduplicated to 54 source sites. The new framework-ownership ledger conserves all 54 with census/current source hashes and individual ownership evidence: seven observation roots migrated in M444, 46 required native mutable runtime subjects, and one ordinary configuration fixture migrated here. The retained mutable subjects include actual CoreLib warmup operations, alias/comparer mutation, collection runtime fault/count oracles and snapshot backing-input tests. This classification does not extend to custom references or the separate retained-array universe.

The ordinary provider fixture now uses an immutable dictionary builder/snapshot with ordinal-ignore-case keys. All nine configuration contract cases pass; the genuinely mutated producer input remains mutable. Literal conservation passes for 4293 tokens across 1064 changed C# files. C# coordinate rows return to zero Open. PowerShell has 106 Open rows and coverage gaps 27. The 524-site retained-array review, full custom-reference/ownership/archive/binding review and final compiler/coverage/native gates remain pending.

### IMM-M448: immutable package discovery, environments and timing publication

Package class/history filters and retained name inventories are immutable. Native dictionary inputs accept readonly maps; fixed descriptions/minimum metadata are readonly. Scoped immutable builders construct environment and test-count updates. Timing method producers preserve pipeline enumeration while constructing immutable vectors; the scheduler report and nested inventories publish readonly/immutable values directly.

Actual discovery regressions pass before/after. Sixteen exact native argv/environment constructions, four class-filter constructions, three timing JSON byte comparisons and twenty-four history lookup/precedence cases pass. Full script-tool regressions pass. Nineteen old coordinates close with fifteen additional owned roots independently attested. PowerShell has 87 Open rows, C# coordinate rows zero and coverage gaps 27. A complete frozen-head package gate is the next validation checkpoint; full ownership/archive/binding and final compiler/coverage/native reconciliation remain pending.

### IMM-M449: immutable native lint inventories and complete package checkpoint

The native lint runner freezes six retained Git/filter/path/diagnostic snapshots. ShellCheck retains its final native string-vector adapter. Actual native PowerShell and Shell lint pass before/after with matching complete reports. Actual repository layout passes for 2696 source files, 59 projects and 151 pinned common files; full script-tool regressions pass. Seven old rows close, leaving 80 PowerShell Open observations. C# coordinate rows remain zero and coverage gaps 27.

The complete package gate at clean pushed frozen HEAD d2587aeb3ce630cd53cad06703ac5e13f4e4673f passes 384 cases, zero failures and one established supported-host skip across 39 shards with parallelism 28. Tracked source and HEAD remain unchanged throughout the run. This validates package discovery, command/environment construction and timing publication within the actual full workflow. The subsequent lint source change is separately verified by actual native lint and does not inherit exact-head package authority. Exhaustive retained-array/custom-reference/ownership/archive/binding review and final compiler/coverage/native gates remain pending.

### IMM-M451: reviewable census sources and conserved array review universe

Thirteen exact collector/auditor/control source snapshots are now tracked as text with a SHA-256 manifest and reproduction notes. Six sources come directly from the successful fe9144dde census capture; seven retained entry/control sources are distinguished explicitly. Their exact bytes are independently verified; these snapshots do not replace raw local exports or the lossless archive and do not claim a new collector execution.

The retained-array review ledger now conserves all 524 deduplicated candidates in 245 units, including census identity/owner/source text and current source hashes. All start explicitly unchecked for independent ownership/native-adapter/SUT reconciliation, including already-migrated roots; coordinate or source-hash matching transfers no status. This makes remaining coverage reviewable without claiming that every candidate is a violation. Ten M450 C# roots are currently under validation; PowerShell has 80 Open rows and coverage 27 gaps. Exhaustive ownership/archive/binding reconciliation and final compiler/coverage/native gates remain pending.

### IMM-M453: distinguish archived source payloads from executable scripts

Repository shebang discovery treated historical shell text snapshots as active scripts and caught an existing SC2155 pattern in the captured collector. The original evidence must remain byte-exact. Each archived text file now has an explicit documentation header; the manifest records its stored-file hash, exact payload byte offset and original payload hash. All thirteen original source payloads restore exactly and verify against their captured SHA-256 values. Reproduction notes explain the byte-level restoration. No executable lint exemption or alteration of historical auditor payloads is introduced. Script-tool verification is rerun with this corrected artifact representation as part of the pending bounded-JSON preview gate.

### IMM-M450: immutable witness metadata and solver inventories

Four production units freeze ten retained parameter/domain/argument/replacement inventories or sequential native witness constructions. Scoped immutable builders replace index-based argument accumulation for exception/static-write witnesses, followed by final reflection adapters. Allocation witnesses preserve their reusable native argument frame: it is populated before warmup and reused within the measured invocation loop, where new materialization would alter the allocation result.

All 77 actual native/oracle and 65 Horn solver cases pass with zero build warnings/errors. Literal conservation verifies 4293 tokens across 1064 changed C# files; the unit manifest explicitly records these four changed sources. Ten new coordinate observations close; the current array ledger independently classifies those ten migrations and the required measured frame, leaving 513 candidates unchecked. C# coordinate rows return to zero Open, PowerShell has 80 Open rows and coverage gaps 27. Exhaustive remaining array/custom-reference/ownership/archive/binding review and final compiler/coverage/native gates remain pending.

### IMM-M452: immutable bounded JSON publication and accepted byte ownership

The bounded reader publishes a readonly record and immutable accepted bytes after completing native reads. The read buffer remains mutable for I/O, and JsonDocument retains its required disposal lifecycle. Accepted evidence resealing uses a final native byte-vector adapter, preserving the bytes validated before callbacks.

Seven exact bounded JSON, invalid UTF8 and byte-limit outcomes match before/after with explicit readonly snapshot checks. The actual FuzzResultValidationTests case passes, including its malformed and duplicate evidence rejections; full script-tool regressions pass with archived historical sources properly represented as documentation. The existing readonly result row and two newly observed ownership roots close, leaving 79 PowerShell Open rows. C# coordinate rows remain zero; 513 retained-array candidates and 27 coverage gaps remain unresolved. A full current fuzz campaign and final ownership/archive/binding/compiler/coverage/native gates are still pending.

### IMM-M454: immutable host profiles and nullable native adapters

Host fixed profile/environment maps are readonly, and optional/default command arguments use scoped immutable builders. Native dispatch boundaries retain typed arrays and exact null binding. The first preview changed a legacy empty branch into a non-null empty vector, removing an existing native null slot; the final adapter explicitly preserves the original null shape.

All 240 actual dispatch branch constructions and sixteen direct null/empty/one/many argument/environment restoration cases match before/after. Docker/Git effects are controlled observations, not native Windows acceptance. Full script-tool regressions pass with zero formatting changes. Six old coordinates close with four additional map/adapter attestations. PowerShell has 73 Open rows. Two M455 C# byte observations are under validation; 513 retained-array candidates and 27 coverage gaps remain unchecked/unresolved. Final ownership/archive/binding and compiler/coverage/native reconciliation remain pending.

### IMM-M457: readonly script-tool test descriptors and native facade boundaries

The script-tool fixture file map and eight case descriptors are readonly; the case vector is immutable. Physical fixture files remain deliberately mutable subjects. All 78 embedded string tokens, including malformed PowerShell/shell snippets and whitespace data, are conserved exactly.

Actual full script-tool regressions pass, including parse/lint/format, extensionless files, spaces, pinned/unpinned Arcade bytes and readonly verification. Actual facade ZIP identity/dependency/provenance, duplicate/archive/payload/DTD regressions pass. Its remaining historical observations are scalar paths or immediate native invalid-input frames. Six old rows close, plus nine independently traced readonly descriptors. PowerShell has 67 Open rows; two M455 C# observations are completing verification. Remaining array/custom-reference/ownership/archive/binding and final compiler/coverage/native gates remain pending.

### IMM-M455: immutable publication and serialized hash bytes

PublicationFile retains immutable UTF8 evidence and writes a readonly span. Protocol bounded hashing retains immutable serialized bytes through size checks and hashing. Native copy/read scratch storage remains mutable by design. The initial check named a nonexistent Host.Tests project; corrected checks target the actual Worker and package integration projects.

All 284 protocol/contract and 77 actual build-task publication cases pass, with one established supported-host skip. Literal conservation verifies 4293 raw/interpolated tokens across 1065 changed C# files. Two new observations close, returning C# coordinate rows to zero Open. The unit manifest marks these two sources changed since the current compiler census.

### IMM-M456: current production/shared-harness array ownership reconciliation

The coordinator independently traced every one of the 61 retained-array/materialization sites outside identified test project directories, including three shared harness adapters. All 61 are reconciled in the array ledger: thirty immutable migrations verified by M437/M442/M450/M455 and thirty-one required mutable/native/public contracts. Preserved cases include writable I/O scratch buffers, actual generated CLR array subjects, reusable allocation measurement frames, mutation-bearing publication argv, shipped public attribute array identity and final native API adapters. Public/native contracts are distinguished from owned immutable observations; no compiler shape or old coordinate automatically determines a role.

The array universe still has 463 unchecked test/harness candidates. PowerShell has 67 Open observations and coverage 27 gaps. These bounded classifications do not establish exhaustive completion; current custom-reference ownership, archive/binding reconciliation and final compiler/coverage/native workflows remain pending.

### IMM-M458: immutable retained test IL observations

Thirteen test units freeze fifteen retained emitted IL snapshots. Opcode/metadata/hash/hex consumers use readonly spans; existing span helpers receive spans directly. The first build exposed six adapter errors, including a redundant AsSpan on an existing span and missing hex conversion adapters; those were corrected before successful execution. Native compiled subjects, runtime certificates and allocation measurement frames stay intact.

All 44 actual Worker cases and both Gates cases pass with zero build warnings/errors. Exact literal conservation verifies 4299 raw/interpolated tokens across 1066 changed C# files; every current test class name is checked against the actual filter and all thirteen source hashes are recorded. Fifteen new coordinate/array observations close, returning C# coordinates to zero Open and leaving 448 array candidates unchecked. PowerShell has 67 Open rows and coverage 27 gaps. Full ownership/archive/binding reconciliation and final compiler/coverage/native workflows remain pending.

### IMM-M459: immutable production inventory records and native report conservation

Fourteen owned source/query/request/range/document/module/project/options/analyzer/authority records are readonly. Three retained native output vectors and three producer vector constructions are immutable, preserving original pipeline enumeration. Legacy native inputs, readonly compile membership and scoped compiler argument construction keep their contracts. Native SDK/metadata objects retain their API/disposal lifecycles.

The first report comparison detected changed ordering because named-property Sort-Object does not resolve the new readonly dictionary keys. Explicit key projections restore exact order. Complete actual MSBuild evaluation of all 51 solution projects now yields byte-identical reports containing 26 production projects, 156 analyzer inputs and 32 additional files. A separately compiled actual portable-PDB fixture produces identical module/document/hash/range JSON with explicit readonly record checks. Full script-tool regressions pass. Private fixture Git identity is controlled; this is not exact-current production PDB or authenticated coverage qualification.

Six old coordinates close and seventeen owned records/native observations receive independent attestations. PowerShell has 61 Open rows, C# coordinate rows zero, array candidates 448 unchecked and coverage gaps 27. Full remaining ownership/archive/binding reconciliation and final compiler/coverage/native workflows remain pending.

### IMM-M461: immutable authority observations and required XML transformation

The normalizer retains an immutable authority-node inventory while preserving the native document mutation under test. Coverage runsettings RemoveAll remains the required mutable XML transformation before selector insertion; the normalization byte sum is scalar query output.

Seven exact native XML success/rejection outcomes and full output bytes match before/after, including preexisting authority, path normalization, foreign paths and malformed input. Actual coverage evidence and full script-tool regressions pass with zero formatting changes. Three old rows close, leaving 58 PowerShell Open observations. Eight collector-byte observations are completing C# verification. Authenticated coverage still has 27 unresolved gaps; full current ownership/archive/binding and final compiler/coverage/native reconciliation remain pending.

### IMM-M460: immutable collector byte evidence and patch-builder publication

Six retained file snapshots and two ASCII comparison vectors are immutable. Patch construction now uses an immutable array builder, publishes a frozen return and leaves only a final native file-write adapter. The actual corrupted backing file and native assembly-loading subjects retain their behavior. All twenty-nine candidate sites in this unit are independently reconciled: eleven migrations and eighteen final native byte-array adapters.

All 71 collector cases pass for the original eight observation migrations. The subsequent builder/frozen-return refinement passes the actual affected metadata-mismatch case; other root blocks stay unchanged. Final literal conservation verifies 4299 raw/interpolated tokens across 1066 changed C# files, and the unit manifest records current source drift. Eleven new coordinate observations close, returning C# coordinates to zero Open. The array universe has 419 unchecked candidates; PowerShell has 58 Open rows and coverage gaps 27. Remaining ownership/archive/binding reconciliation and final compiler/coverage/native workflows remain pending.

### IMM-M462: readonly developer-check command and timing construction

Two owned build descriptors, package arguments and the timing report are readonly. NoBuild is initialized directly in the fixed package map, and timing phases publish the immutable builder snapshot. Actual plan inputs retain their native parser contract.

Four actual build descriptor/argv constructions, six package argument maps and three complete timing JSON byte comparisons pass. Real Debug/Release plan outputs match before/after; full script-tool regressions pass. Execution effects in construction controls are explicit stand-ins, and no fresh full developer-check campaign is claimed. One old coordinate closes with three newly observed owned records, leaving 57 PowerShell Open rows. C# coordinate rows are zero, 419 array candidates remain unchecked and coverage gaps 27. Remaining ownership/archive/binding and final compiler/coverage/native workflows remain pending.

### IMM-M463: immutable loop byte observations and native path decoding

Git-output and left/right file-byte observations are immutable. The empty path producer keeps pipeline enumeration through an immutable empty vector. UTF8 decoding retains one explicit reusable native byte-array adapter, avoiding repeated full-vector copies per path; native process argv construction and CLI ingress keep their contracts.

Six actual Git NUL/UTF8 inventory cases and six actual file-byte comparison outcomes match before/after, including empty, Unicode, newline/tab, leading-dash and ignored-package paths. Full script-tool regressions pass with zero formatting changes. Ignored control repairs select one actual Git executable and observe empty output through explicit JSON input. Three old coordinates close with four additional snapshot/adapter attestations, leaving 54 PowerShell Open rows. No full continuous watcher is started or claimed. C# coordinates remain zero, arrays 419 unchecked and coverage gaps 27; remaining ownership/archive/binding and final compiler/coverage/native reconciliation remain pending.

### IMM-M464: immutable Windows PE observations and import construction

PE module names use a scoped immutable builder. Table/name content remains in the native API's immutable vectors; import/filter/package inventories are immutable. Two descriptors are readonly in an immutable vector. Explicit final BitConverter/ASCII byte-array frames preserve PowerShell binding without changing parser decisions.

Six exact native PE metadata outcomes match before/after: actual hash-verified pinned x64/arm64 DLLs, architecture mismatches, malformed and missing input. Full script-tool regressions pass. Eight old coordinates close with four descriptor/vector/adapter attestations, leaving 46 PowerShell Open rows. Linux PE metadata parsing does not claim Windows native load/export execution. C# coordinates remain zero, array candidates 419 unchecked and coverage gaps 27. Full remaining ownership/archive/binding and final compiler/coverage/native workflows remain pending.

### IMM-M465: immutable qualification inventories and actual packaged consumers

Compatibility package sets, offline package/SDK observations, template package/native rejection output and macOS package/claim inventories are immutable. Fixed checkout/role/template vectors are immutable, with readonly checkout descriptors. Native argv and deliberately edited template/native payload subjects preserve their contracts.

A private checkout at 14482847d builds one exact package set. Actual offline stack-trace qualification passes before/after for shipped analyzer, generator, build-task and worker roles without sidecar symbols. Actual portable/verified template workflows pass before/after, including SP0027 rejection and complete proof output. Actual baseline/current APICompat passes against f10651eaeb7efb531263e7e5ae5158b07507411d; three zero/one/many macOS inventory controls and full script-tool regressions pass. A direct lint-container launch initially lacked the worktree Git metadata mount; the validated readonly mount fixes that orchestration error. macOS execution and final-head native qualification remain unclaimed.

Twelve old coordinates close with six independent fixed-vector/descriptor attestations, leaving 34 PowerShell Open rows. C# coordinates remain zero, arrays 419 unchecked and coverage gaps 27. Remaining ownership/archive/binding and final compiler/coverage/native workflows remain pending.

### IMM-M466: freeze retained vocabulary and reflection evidence

Six owned enum, reflection, source-file and baseline inventories become immutable. Actual targeted tests pass: 18 attribute, one generator, one contract and two effects cases. Native runtime subjects retain their contracts. Six current coordinate observations and six array ownership sites close independently, leaving zero C# coordinates Open and 413 array candidates unchecked. Literal and source-unit conservation are separately recorded in the canonical M466 verification log.

### IMM-M467: current reproducibility and dependency helper ownership

Current helpers already publish immutable receipt, payload, package, source and project inventories. Canonical controls verify two native receipt records, six payload hashes, five actual ZIP identities with four payloads, five actual native null/default/empty/one/many argv cases and dependency XML projections. Borrowed native JSON arrays retain validation shape. The initial native probe depended on unavailable Python; the successful probe uses canonical PowerShell instead. Seven old coordinates close on independent current-source controls, leaving 27 PowerShell Open rows. Full independent package reproduction and online dependency audit remain unclaimed. Remaining ownership, coverage, binding, archive and final gates remain pending.

### IMM-M468/M469: immutable symbol-export and release-fixture ownership

Symbol export freezes its owned path inventory and non-null embedded PDB snapshot, preserving native CLI ingress, null semantics and final hash/file adapters. Baseline/current explicit and discovered exports produce identical PDB bytes. The full embedded-symbol regressions pass, including missing sources/symbols and malformed debug records. An initial grouping parse defect was repaired before successful native tests.

Release fixtures construct archive payloads with an immutable dictionary builder and immutable bytes, freeze fixed package vectors and use readonly splat arguments. The deliberately omitted SBOM component and altered native/package files remain mutable corruption subjects. Actual release-evidence and attestation policy regressions pass before/after; no real signature qualification is inferred. Full script-tool gates pass for both batches. Seven old PowerShell coordinates close, leaving 20 Open rows; array ownership remains 413 unchecked and coverage 27 Open. Final collection and remaining ownership/binding/archive work remain pending.

### IMM-M470: freeze nested package identities

Identity collections previously retained mutable record objects. All three owned identity record publications are readonly ordered maps. Three downstream package selectors now use explicit key access, preserving PowerShell dictionary semantics. The public object-array parameter and final caller binding remain native compatibility frames.

Actual release-evidence, attestation policy, facade and full script-tool regressions pass. Five synthetic ZIP identities independently verify readonly outer, entry and nested identity records; all three main identity selections pass. Existing native argv, payload and dependency helper controls pass. Full final package-consumer qualification remains pending. Two old native-boundary coordinates close with six new direct record/consumer attestations, leaving 18 PowerShell Open rows. Remaining array/coverage/archive/binding and final verification work remains pending.

### IMM-M472: verify current native ingress and immutable scheduling boundaries

Native Git argv and dotnet CLI ingress remain compatibility frames. Current project/reference vectors are immutable. Actual canonical Git line/path queries, an Effects changed-project plan and dotnet version output capture pass. Three seed schedules publish readonly records and detached immutable seed vectors; mutating the ingress afterward cannot alter the publication. Executing the actual campaign-summary assignment and completion helper preserves serialized evidence bytes with immutable runs and a readonly record. This is scoped ownership verification, not a complete fuzz campaign. Eight old coordinates close independently, leaving ten PowerShell Open rows, all in optional-tool consumer qualification.
