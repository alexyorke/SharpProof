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
| MODEL-001 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:125` | `DisjointInputs`: `CompilerDisjointInputPair[]?` | Open; coordinated migration |
| MODEL-002 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:127` | `EffectsCompleteAtEntry`: `bool` | Open; coordinated migration |
| MODEL-003 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:128` | `ValidEffectClaimIds`: `string[]` | Open; coordinated migration |
| MODEL-004 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:130` | `IsBodyAbstraction`: `bool` | Open; coordinated migration |
| MODEL-005 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:132` | `HasReceiver`: `bool` | Open; coordinated migration |
| MODEL-006 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:133` | `Graph`: `PortableIrGraph` | Open; coordinated migration |
| MODEL-007 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:134` | `Parameters`: `CompilerTotalParameterArtifact[]` | Open; coordinated migration |
| MODEL-008 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:135` | `Result`: `int` | Open; coordinated migration |
| MODEL-009 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:136` | `Clauses`: `CompilerTotalClauseArtifact[]` | Open; coordinated migration |
| MODEL-010 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:137` | `ExceptionConstraints`: `CompilerTotalExceptionConstraintArtifact[]` | Open; coordinated migration |
| MODEL-011 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:138` | `CallPreconditions`: `CompilerTotalCallPreconditionArtifact[]` | Open; coordinated migration |
| MODEL-012 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:150` | `MetadataClause`: `CompilerMetadataClauseOrigin?` | Open; coordinated migration |
| MODEL-013 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:152` | `InstructionIndex`: `int` | Open; coordinated migration |
| MODEL-014 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:153` | `CalleeIdentity`: `string` | Open; coordinated migration |
| MODEL-015 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:154` | `ClauseOrdinal`: `int` | Open; coordinated migration |
| MODEL-016 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:155` | `ClauseSite`: `int` | Open; coordinated migration |
| MODEL-017 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:156` | `ValueRoot`: `int` | Open; coordinated migration |
| MODEL-018 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:157` | `SafeRoot`: `int` | Init-only; retained mutable members remain under review |
| MODEL-019 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:162` | `ClaimId`: `string` | Open; coordinated migration |
| MODEL-020 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:163` | `AllowedKinds`: `IrExceptionKind[]` | Open; coordinated migration |
| MODEL-021 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:168` | `Entry`: `int` | Open; coordinated migration |
| MODEL-022 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:169` | `Current`: `int` | Open; coordinated migration |
| MODEL-023 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:170` | `Old`: `int` | Open; coordinated migration |
| MODEL-024 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:175` | `Kind`: `CompilerContractKind` | Open; coordinated migration |
| MODEL-025 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:176` | `ValueRoot`: `int` | Open; coordinated migration |
| MODEL-026 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:177` | `SafeRoot`: `int` | Init-only; retained mutable members remain under review |
| MODEL-027 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:178` | `Operation`: `int` | Open; coordinated migration |
| MODEL-028 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:179` | `ClaimId`: `string?` | Open; coordinated migration |
| MODEL-029 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:180` | `AssumptionId`: `string?` | Open; coordinated migration |
| MODEL-030 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:190` | `SpecificationPackIds`: `string[]` | Open; coordinated migration |
| MODEL-031 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:191` | `SpecificationPackCatalogVersion`: `int` | Open; coordinated migration |
| MODEL-032 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:192` | `SpecificationPackCatalogSha256`: `string` | Open; coordinated migration |
| MODEL-033 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:197` | `CallableId`: `string` | Open; coordinated migration |
| MODEL-034 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:198` | `FailureReason`: `WorkerClaimReason` | Open; coordinated migration |
| MODEL-035 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:199` | `EffectClaims`: `CompilerEffectClaimArtifact[]` | Open; coordinated migration |
| MODEL-036 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:201` | `Total`: `CompilerTotalCallableArtifact?` | Open; coordinated migration |
| MODEL-037 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:203` | `TotalEntry`: `CompilerTotalCallableArtifact?` | Init-only; retained mutable members remain under review |
| MODEL-038 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:208` | `ClaimId`: `string` | Open; coordinated migration |
| MODEL-039 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:209` | `ContractKind`: `WorkerEffectContractKind` | Open; coordinated migration |
| MODEL-040 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:210` | `Outcome`: `WorkerClaimOutcome` | Open; coordinated migration |
| MODEL-041 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:211` | `Reason`: `WorkerClaimReason` | Open; coordinated migration |
| MODEL-042 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:212` | `Certainty`: `WorkerEffectEvidenceCertainty` | Open; coordinated migration |
| MODEL-043 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:213` | `Constraint`: `CompilerEffectConstraintArtifact` | Open; coordinated migration |
| MODEL-044 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:214` | `Evidence`: `string` | Open; coordinated migration |
| MODEL-045 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:215` | `EvidenceSha256`: `string` | Open; coordinated migration |
| MODEL-046 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:220` | `AllowedEffects`: `WorkerEffectSet` | Open; coordinated migration |
| MODEL-047 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:221` | `AllowedCapabilities`: `WorkerEffectCapabilitySet` | Open; coordinated migration |
| MODEL-048 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:222` | `AllowedExceptionTypes`: `string[]` | Open; coordinated migration |
| MODEL-049 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:280` | `Code`: `string` | Open; coordinated migration |
| MODEL-050 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:281` | `Message`: `string` | Open; coordinated migration |
| MODEL-051 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:282` | `IsSource`: `bool` | Init-only; retained mutable members remain under review |
| MODEL-052 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:283` | `Location`: `WorkerSourceLocation` | Open; coordinated migration |
| MODEL-053 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:285` | `SourceTreeOrdinal`: `int` | Init-only; provenance initialized by producer |
| MODEL-054 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:287` | `SourceTreePath`: `string` | Init-only; provenance initialized by producer |
| MODEL-055 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:289` | `SourceTreeSha256`: `string` | Init-only; provenance initialized by producer |
| MODEL-056 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:291` | `SourceLineMapSha256`: `string` | Init-only; provenance initialized by producer |
| MODEL-057 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:296` | `Schema`: `string` | Init-only; retained mutable members remain under review |
| MODEL-058 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:297` | `SchemaVersion`: `int` | Open; coordinated migration |
| MODEL-059 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:298` | `ProtocolVersion`: `string` | Open; coordinated migration |
| MODEL-060 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:299` | `RelationalSummarySchemaVersion`: `int` | Open; coordinated migration |
| MODEL-061 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:300` | `SpecificationPackSchemaVersion`: `int` | Open; coordinated migration |
| MODEL-062 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:301` | `SpecificationPackIds`: `string[]` | Open; coordinated migration |
| MODEL-063 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:302` | `SpecificationPackCatalogVersion`: `int` | Open; coordinated migration |
| MODEL-064 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:303` | `SpecificationPackCatalogSha256`: `string` | Open; coordinated migration |
| MODEL-065 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:304` | `Features`: `WorkerFeatureSet` | Open; coordinated migration |
| MODEL-066 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:305` | `Compilation`: `CompilerCompilationSnapshot` | Open; coordinated migration |
| MODEL-067 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:306` | `Manifest`: `WorkerClaimManifest` | Open; coordinated migration |
| MODEL-068 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:307` | `MaximumExpressionDepth`: `int` | Open; coordinated migration |
| MODEL-069 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:308` | `CompilerDiagnostics`: `CompilerDiagnosticArtifact[]` | Open; coordinated migration |
| MODEL-070 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:309` | `Callables`: `CompilerCallableArtifact[]` | Open; coordinated migration |
| MODEL-071 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:310` | `ReachableSource`: `CompilerReachableSourceArtifact?` | Init-only; retained mutable members remain under review |
| MODEL-072 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:317` | `CollectionComplete`: `bool` | Init-only; retained mutable members remain under review |
| MODEL-073 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:318` | `Roots`: `CompilerSourceRootArtifact[]` | Open; coordinated migration |
| MODEL-074 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:319` | `Bodies`: `CompilerSourceBodyArtifact[]` | Open; coordinated migration |
| MODEL-075 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:320` | `Documents`: `CompilerSourceDocumentArtifact[]` | Init-only; retained mutable members remain under review |
| MODEL-076 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:325` | `SourceTreeOrdinal`: `int` | Init-only; retained mutable members remain under review |
| MODEL-077 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:326` | `Path`: `string` | Open; coordinated migration |
| MODEL-078 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:327` | `MaximumBodyEnd`: `int` | Open; coordinated migration |
| MODEL-079 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:332` | `CallableId`: `string` | Open; coordinated migration |
| MODEL-080 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:333` | `BodyId`: `string` | Open; coordinated migration |
| MODEL-081 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:338` | `BodyId`: `string` | Open; coordinated migration |
| MODEL-082 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:339` | `MethodIdentity`: `string` | Open; coordinated migration |
| MODEL-083 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:340` | `CallIdentity`: `string` | Open; coordinated migration |
| MODEL-084 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:341` | `IsStatic`: `bool` | Open; coordinated migration |
| MODEL-085 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:342` | `ParameterTypes`: `string[]` | Open; coordinated migration |
| MODEL-086 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:343` | `ReturnType`: `string?` | Open; coordinated migration |
| MODEL-087 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:344` | `SourceTreeOrdinal`: `int` | Init-only; retained mutable members remain under review |
| MODEL-088 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:345` | `Start`: `int` | Open; coordinated migration |
| MODEL-089 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:346` | `Length`: `int` | Open; coordinated migration |
| MODEL-090 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:347` | `CallsComplete`: `bool` | Open; coordinated migration |
| MODEL-091 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:348` | `EffectsCompleteAtEntry`: `bool` | Open; coordinated migration |
| MODEL-092 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:349` | `Callees`: `string[]` | Open; coordinated migration |
| MODEL-093 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:350` | `Graph`: `PortableIrGraph?` | Open; coordinated migration |
| MODEL-094 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:351` | `IsCallSkeleton`: `bool` | Open; coordinated migration |
| MODEL-095 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:352` | `SourceCalls`: `CompilerSourceCallArtifact[]` | Open; coordinated migration |
| MODEL-096 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:357` | `InstructionIndex`: `int` | Open; coordinated migration |
| MODEL-097 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:358` | `CalleeBodyId`: `string` | Open; coordinated migration |
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
| MODEL-167 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:15` | `Semantics`: `IrExecutionSemantics` | Open; coordinated migration |
| MODEL-168 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:16` | `HasProgram`: `bool` | Open; coordinated migration |
| MODEL-169 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:17` | `Types`: `PortableIrType[]` | Open; coordinated migration |
| MODEL-170 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:18` | `Identities`: `int[]` | Open; coordinated migration |
| MODEL-171 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:19` | `Variables`: `PortableIrVariable[]` | Open; coordinated migration |
| MODEL-172 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:20` | `Members`: `PortableIrMember[]` | Open; coordinated migration |
| MODEL-173 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:21` | `Operations`: `PortableIrOperation[]` | Open; coordinated migration |
| MODEL-174 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:22` | `Terms`: `PortableIrTerm[]` | Open; coordinated migration |
| MODEL-175 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:23` | `Blocks`: `PortableIrBlock[]` | Open; coordinated migration |
| MODEL-176 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:24` | `Entry`: `int` | Open; coordinated migration |
| MODEL-177 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:25` | `Roots`: `int[]` | Open; coordinated migration |
| MODEL-178 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:36` | `Kind`: `IrTypeKind` | Open; coordinated migration |
| MODEL-179 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:37` | `Name`: `string` | Open; coordinated migration |
| MODEL-180 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:38` | `Element`: `int` | Open; coordinated migration |
| MODEL-181 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:40` | `Width`: `int` | Open; coordinated migration |
| MODEL-182 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:42` | `Signed`: `bool` | Open; coordinated migration |
| MODEL-183 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:44` | `ClosedSealedReference`: `bool` | Open; coordinated migration |
| MODEL-184 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:52` | `Name`: `string` | Open; coordinated migration |
| MODEL-185 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:53` | `Type`: `int` | Open; coordinated migration |
| MODEL-186 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:65` | `Identity`: `int` | Open; coordinated migration |
| MODEL-187 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:66` | `DeclaringType`: `int` | Init-only; retained mutable members remain under review |
| MODEL-188 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:67` | `Name`: `string` | Open; coordinated migration |
| MODEL-189 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:68` | `ReturnType`: `int` | Open; coordinated migration |
| MODEL-190 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:69` | `IsStatic`: `bool` | Open; coordinated migration |
| MODEL-191 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:70` | `ParameterTypes`: `int[]` | Open; coordinated migration |
| MODEL-192 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:71` | `DocumentationCommentId`: `string?` | Open; coordinated migration |
| MODEL-193 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:78` | `Description`: `string?` | Open; coordinated migration |
| MODEL-194 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:80` | `SourceSpan`: `PortableIrSourceSpan?` | Open; coordinated migration |
| MODEL-195 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:86` | `Document`: `string` | Open; coordinated migration |
| MODEL-196 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:88` | `Start`: `int` | Open; coordinated migration |
| MODEL-197 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:90` | `Length`: `int` | Open; coordinated migration |
| MODEL-198 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:91` | `Line`: `int` | Init-only; retained mutable members remain under review |
| MODEL-199 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:92` | `Column`: `int` | Open; coordinated migration |
| MODEL-200 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:107` | `Kind`: `IrTermKind` | Open; coordinated migration |
| MODEL-201 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:108` | `Type`: `int` | Open; coordinated migration |
| MODEL-202 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:109` | `A`: `int` | Open; coordinated migration |
| MODEL-203 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:110` | `B`: `int` | Open; coordinated migration |
| MODEL-204 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:111` | `C`: `int` | Open; coordinated migration |
| MODEL-205 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:112` | `D`: `int` | Open; coordinated migration |
| MODEL-206 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:113` | `Number`: `long` | Open; coordinated migration |
| MODEL-207 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:115` | `Bits`: `ulong` | Open; coordinated migration |
| MODEL-208 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:116` | `Text`: `string?` | Open; coordinated migration |
| MODEL-209 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:117` | `Items`: `int[]` | Open; coordinated migration |
| MODEL-210 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:128` | `Kind`: `IrLocationKind` | Open; coordinated migration |
| MODEL-211 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:129` | `Type`: `int` | Open; coordinated migration |
| MODEL-212 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:130` | `A`: `int` | Open; coordinated migration |
| MODEL-213 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:131` | `B`: `int` | Open; coordinated migration |
| MODEL-214 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:132` | `Items`: `int[]` | Open; coordinated migration |
| MODEL-215 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:145` | `Kind`: `IrInstructionKind` | Open; coordinated migration |
| MODEL-216 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:146` | `Operation`: `int` | Open; coordinated migration |
| MODEL-217 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:147` | `A`: `int` | Open; coordinated migration |
| MODEL-218 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:148` | `B`: `int` | Open; coordinated migration |
| MODEL-219 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:149` | `C`: `int` | Open; coordinated migration |
| MODEL-220 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:150` | `Items`: `int[]` | Open; coordinated migration |
| MODEL-221 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:151` | `Location`: `PortableIrLocation?` | Open; coordinated migration |
| MODEL-222 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:153` | `Origin`: `IrHavocOrigin` | Open; coordinated migration |
| MODEL-223 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:161` | `Name`: `string?` | Open; coordinated migration |
| MODEL-224 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:162` | `Instructions`: `PortableIrInstruction[]` | Open; coordinated migration |
| MODEL-225 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:36` | `ProtocolVersion`: `string` | Open; coordinated migration |
| MODEL-226 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:38` | `CompilerManifest`: `WorkerFileReference` | Open; coordinated migration |
| MODEL-227 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:40` | `Budgets`: `WorkerBudgets` | Open; coordinated migration |
| MODEL-228 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:42` | `Cache`: `WorkerCacheOptions` | Init-only; retained mutable members remain under review |
| MODEL-229 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:44` | `VerifyPolicy`: `WorkerVerifyPolicy` | Open; coordinated migration |
| MODEL-230 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:46` | `AssumptionPolicy`: `WorkerAssumptionPolicy` | Open; coordinated migration |
| MODEL-231 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:52` | `Path`: `string` | Open; coordinated migration |
| MODEL-232 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:54` | `Sha256`: `string` | Open; coordinated migration |
| MODEL-233 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:90` | `QueryRlimit`: `uint` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-234 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:92` | `MethodRlimit`: `uint` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-235 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:94` | `MethodWallTimeMilliseconds`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-236 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:96` | `ProjectWallTimeMilliseconds`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-237 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:98` | `MaxParallelism`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-238 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:100` | `MaximumExpressionDepth`: `int` | Preserve; explicit mutable-budget snapshot-isolation contract |
| MODEL-239 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:107` | `Enabled`: `bool` | Open; coordinated migration |
| MODEL-240 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:109` | `Directory`: `string?` | Open; coordinated migration |
| MODEL-241 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:111` | `MaximumBytes`: `long` | Open; coordinated migration |
| MODEL-242 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:158` | `Path`: `string` | Open; coordinated migration |
| MODEL-243 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:160` | `Start`: `int` | Open; coordinated migration |
| MODEL-244 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:162` | `Length`: `int` | Open; coordinated migration |
| MODEL-245 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:164` | `Line`: `int` | Init-only; retained mutable members remain under review |
| MODEL-246 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:166` | `Column`: `int` | Open; coordinated migration |
| MODEL-247 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:172` | `CallableId`: `string` | Open; coordinated migration |
| MODEL-248 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:174` | `SelectedFeatures`: `WorkerSelectedFeature[]` | Open; coordinated migration |
| MODEL-249 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:176` | `SelectionReasons`: `WorkerSelectionReason[]` | Open; coordinated migration |
| MODEL-250 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:178` | `Location`: `WorkerSourceLocation` | Open; coordinated migration |
| MODEL-251 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:180` | `ClaimIds`: `string[]` | Open; coordinated migration |
| MODEL-252 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:182` | `Assumptions`: `WorkerAssumptionEvidence[]` | Open; coordinated migration |
| MODEL-253 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:188` | `ClaimId`: `string` | Open; coordinated migration |
| MODEL-254 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:190` | `CallableId`: `string` | Open; coordinated migration |
| MODEL-255 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:192` | `Ordinal`: `int` | Open; coordinated migration |
| MODEL-256 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:194` | `Kind`: `WorkerClaimKind` | Open; coordinated migration |
| MODEL-257 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:196` | `Evidence`: `WorkerClaimEvidence` | Open; coordinated migration |
| MODEL-258 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:198` | `EffectContractKind`: `WorkerEffectContractKind` | Open; coordinated migration |
| MODEL-259 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:200` | `Location`: `WorkerSourceLocation` | Open; coordinated migration |
| MODEL-260 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:206` | `SchemaVersion`: `int` | Open; coordinated migration |
| MODEL-261 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:208` | `Hash`: `string` | Open; coordinated migration |
| MODEL-262 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:210` | `Callables`: `WorkerCallableManifestEntry[]` | Open; coordinated migration |
| MODEL-263 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:212` | `Claims`: `WorkerClaimManifestEntry[]` | Open; coordinated migration |
| MODEL-264 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:368` | `Id`: `string` | Open; coordinated migration |
| MODEL-265 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:370` | `Kind`: `WorkerAssumptionKind` | Open; coordinated migration |
| MODEL-266 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:372` | `Used`: `bool` | Open; coordinated migration |
| MODEL-267 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:378` | `CallableId`: `string` | Open; coordinated migration |
| MODEL-268 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:380` | `Coverage`: `WorkerCallableCoverage` | Open; coordinated migration |
| MODEL-269 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:382` | `Reason`: `WorkerCallableCoverageReason` | Open; coordinated migration |
| MODEL-270 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:384` | `Assumptions`: `WorkerAssumptionEvidence[]` | Open; coordinated migration |
| MODEL-271 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:390` | `ClaimId`: `string` | Open; coordinated migration |
| MODEL-272 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:392` | `Outcome`: `WorkerClaimOutcome` | Open; coordinated migration |
| MODEL-273 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:394` | `Reason`: `WorkerClaimReason` | Open; coordinated migration |
| MODEL-274 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:396` | `EffectCertainty`: `WorkerEffectEvidenceCertainty` | Open; coordinated migration |
| MODEL-275 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:398` | `EffectWitness`: `WorkerEffectViolationWitness?` | Open; coordinated migration |
| MODEL-276 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:400` | `Vacuity`: `WorkerVacuityKind` | Open; coordinated migration |
| MODEL-277 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:402` | `ProofCore`: `string[]` | Open; coordinated migration |
| MODEL-278 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:404` | `Model`: `WorkerModelValue[]` | Open; coordinated migration |
| MODEL-279 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:406` | `Assumptions`: `WorkerAssumptionEvidence[]` | Open; coordinated migration |
| MODEL-280 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:412` | `Kind`: `string` | Open; coordinated migration |
| MODEL-281 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:414` | `Detail`: `string` | Init-only; retained mutable members remain under review |
| MODEL-282 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:416` | `Effects`: `WorkerEffectSet` | Open; coordinated migration |
| MODEL-283 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:418` | `Capabilities`: `WorkerEffectCapabilitySet` | Open; coordinated migration |
| MODEL-284 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:420` | `ExactExceptionTypeHierarchy`: `string[]` | Open; coordinated migration |
| MODEL-285 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:422` | `Location`: `WorkerSourceLocation` | Open; coordinated migration |
| MODEL-286 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:428` | `Variable`: `string` | Open; coordinated migration |
| MODEL-287 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:430` | `Kind`: `string` | Open; coordinated migration |
| MODEL-288 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:432` | `Value`: `string` | Open; coordinated migration |
| MODEL-289 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:449` | `ProtocolVersion`: `string` | Open; coordinated migration |
| MODEL-290 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:451` | `ManifestSchemaVersion`: `int` | Init-only; retained mutable members remain under review |
| MODEL-291 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:453` | `CacheSchemaVersion`: `int` | Init-only; retained mutable members remain under review |
| MODEL-292 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:455` | `WorkerVersion`: `string` | Open; coordinated migration |
| MODEL-293 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:457` | `ApiSpecVersion`: `string` | Init-only; retained mutable members remain under review |
| MODEL-294 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:459` | `WorkerBinarySha256`: `string` | Init-only; retained mutable members remain under review |
| MODEL-295 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:461` | `ApiSpecContentSha256`: `string` | Init-only; retained mutable members remain under review |
| MODEL-296 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:467` | `CacheHit`: `bool` | Open; coordinated migration |
| MODEL-297 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:469` | `CacheStatus`: `WorkerCacheStatus` | Open; coordinated migration |
| MODEL-298 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:471` | `Versions`: `WorkerVersionSummary` | Open; coordinated migration |
| MODEL-299 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:473` | `Budgets`: `WorkerBudgets` | Open; coordinated migration |
| MODEL-300 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:475` | `ElapsedMilliseconds`: `long` | Open; coordinated migration |
| MODEL-301 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:481` | `Code`: `string` | Open; coordinated migration |
| MODEL-302 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:483` | `Message`: `string` | Open; coordinated migration |
| MODEL-303 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:489` | `ProtocolVersion`: `string` | Open; coordinated migration |
| MODEL-304 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:491` | `RequestHash`: `string` | Open; coordinated migration |
| MODEL-305 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:493` | `InputHash`: `string` | Open; coordinated migration |
| MODEL-306 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:495` | `Manifest`: `WorkerClaimManifest` | Open; coordinated migration |
| MODEL-307 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:497` | `RunStatus`: `WorkerRunStatus` | Open; coordinated migration |
| MODEL-308 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:499` | `FailureReason`: `WorkerRunFailureReason` | Open; coordinated migration |
| MODEL-309 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:501` | `CallableResults`: `WorkerCallableResult[]` | Open; coordinated migration |
| MODEL-310 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:503` | `ClaimResults`: `WorkerClaimResult[]` | Open; coordinated migration |
| MODEL-311 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:505` | `Summary`: `WorkerVerificationSummary` | Open; coordinated migration |
| MODEL-312 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:507` | `Errors`: `WorkerProtocolError[]` | Open; coordinated migration |

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
- Of the 312 inventoried model properties, 93 are now init-only, six budget
  properties retain the explicit mutable-budget snapshot test contract, and
  213 remain open for coordinated review/migration. Init-only collection
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
| FOLLOW-025 | Compiler/portable/protocol 213 open model properties plus init-only collection deep immutability | Open; preserve malformed/missing/null wire behavior and explicit public Canonicalize/SealManifest mutation contracts |

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