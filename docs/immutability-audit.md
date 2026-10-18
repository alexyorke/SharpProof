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
| IMM-001 | `SharpProof.Analyzer.Core/Advisory/AdvisoryEffectSites.cs:39` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-002 | `SharpProof.CompilerCollector/CompilerArtifact/CompilerTotalIlBodyProvider.cs:238` | Fixed opcode lookup stored in a mutable dictionary; materialize an ImmutableDictionary. | Open |
| IMM-003 | `SharpProof.Gates/Corpus/AllocationIlOracle.cs:10` | Fixed opcode lookup stored in a mutable dictionary; materialize an ImmutableDictionary. | Open |
| IMM-004 | `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs:10` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-005 | `SharpProof.Host/TrustedChildEnvironment.cs:8` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-006 | `SharpProof.Host/TrustedChildEnvironment.cs:23` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-007 | `SharpProof.Testing/WellSortedIrGenerator.cs:51` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-008 | `SharpProof.Testing/WellSortedIrGenerator.cs:55` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-009 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:527` | Fixed enum membership stored in a mutable HashSet; use ImmutableHashSet. | Open |
| IMM-010 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:702` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-011 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:715` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-012 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:731` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-013 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:740` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-014 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:749` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-015 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:755` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-016 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:785` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-017 | `Tools/SharpProof.Fuzz/FiniteDomainSmtFuzzing.cs:27` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-018 | `Tools/SharpProof.Fuzz/FrontendFuzzing.cs:657` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |
| IMM-019 | `Tools/SharpProof.Fuzz/TotalProgramFuzzing.cs:47` | Fixed lookup/rule data stored in a mutable array; use ImmutableArray without changing order. | Open |

| IMM-020 | `eng/testing/DictionaryAnalyzerConfigOptions.cs:9` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-021 | `SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs:49` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-022 | `SharpProof.Frontend/RoslynTotalProgramLowerer.Calls.cs:8` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-023 | `SharpProof.Frontend/RoslynTotalProgramLowerer.Calls.cs:9` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-024 | `SharpProof.Gates/Corpus/OpenSourceCorpusRunner.cs:372` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-025 | `SharpProof.Specs/ApiSpecInstantiation.cs:150` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-026 | `SharpProof.Specs/ApiSpecInstantiation.cs:151` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-027 | `SharpProof.Specs/ApiSpecInstantiation.cs:152` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |
| IMM-028 | `SharpProof.Specs/ApiSpecInstantiation.cs:153` | Construction-only guard field remains assignable; declare readonly. | Implemented; validation pending |

| IMM-029 | `SharpProof.Testing/WellSortedIrGenerator.cs:6` | GeneratedIrCase retains a mutable dictionary through IReadOnlyDictionary; require ImmutableDictionary and construct it through persistent Add operations. | Implemented; validation pending |
| IMM-030 | `SharpProof.Testing/WellSortedIrGenerator.cs:34` | InterestingIntegers exposes an array through IReadOnlyList; publish ImmutableArray and use Length at callers. | Implemented; validation pending |

| IMM-031 | `SharpProof.Analyzer.Test/CompilerCaptureRequestWorkflowTests.cs:312` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-032 | `SharpProof.Analyzer.Test/FinalCompilationCollectorTests.cs:2009` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-033 | `SharpProof.Analyzer.Test/GeneratedContractForAnalyzerTests.cs:491` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-034 | `SharpProof.Analyzer.Test/GeneratedContractForAnalyzerTests.cs:492` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-035 | `SharpProof.Contracts.Test/TypedContractLoweringTests.cs:531` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-036 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:250` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-037 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:251` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-038 | `SharpProof.Fuzz.Test/FuzzRunnerTests.cs:616` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-039 | `SharpProof.Gates.Test/OrdinaryRepeatedAnalyzerWorkflowTests.cs:41` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-040 | `SharpProof.Ir.Test/IrFactoryInvariantRegressionTests.cs:280` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-041 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:3316` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-042 | `SharpProof.Smt.Test/CallableSolverSessionTests.cs:467` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-043 | `SharpProof.Smt.Test/NativeSmtRegressionTests.cs:969` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-044 | `SharpProof.Specs.Test/ApiSpecInstantiationCoverageTests.cs:577` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-045 | `SharpProof.Verify.Test/CallableReplayBoundaryTests.cs:193` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-046 | `SharpProof.Verify.Test/CallableTotalReplayTests.cs:217` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-047 | `SharpProof.Worker.Test/CapturedPrimaryConstructorStorageAuditTests.cs:596` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-048 | `SharpProof.Worker.Test/CountUpHoudiniResourceBoundaryTests.cs:385` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-049 | `SharpProof.Worker.Test/CountUpHoudiniResourceBoundaryTests.cs:386` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-050 | `SharpProof.Worker.Test/EmptyIteratorAllocationAuditTests.cs:558` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-051 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:724` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-052 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:11` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-053 | `SharpProof.Worker.Test/NightlyRefutationRegressionTests.cs:151` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-054 | `SharpProof.Worker.Test/PassiveCallableVcTests.cs:882` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-055 | `SharpProof.Worker.Test/WorkerRuntimeBoundaryCoverageTests.cs:111` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |
| IMM-056 | `SharpProof.Worker.Test/WorkerTests.cs:5976` | Test helper guard field is assigned only at construction; declare readonly while retaining mutable referents where exercised. Outside embedded source. | Implemented; validation pending |

| IMM-057 | `SharpProof.Smt/HornInvariantSearch.cs:11` | Horn proposals escape as mutable dictionaries; publish and build ImmutableDictionary. | Implemented; validation pending |
| IMM-058 | `SharpProof.Smt/BvEncoder.References.cs:10` | ReferenceFacts exposes a mutable List; use an immutable list with private persistent updates. Preserve recursive encoding additions. | Implemented; validation pending |
| IMM-059 | `SharpProof.Smt/BvEncoder.cs:81` | Relations exposes a replaceable mutable dictionary; configure mode at construction and expose an immutable map. | Implemented; validation pending |
| IMM-060 | `SharpProof.Contracts/ContractExpressionBinder.cs:40` | PreStateVariables exposes its mutable backing map; use persistent immutable map updates. | Implemented; validation pending |
| IMM-061 | `SharpProof.Contracts/ContractCanonicalization.cs:376` | Canonical Parameters exposes a mutable List; use an immutable parameter sequence. | Implemented; validation pending |
| IMM-062 | `SharpProof.Contracts/ContractForSymbolMatcher.cs:21` | Companion target index returns mutable Dictionary through read-only interface; freeze map and retain symbol comparer. | Implemented; validation pending |
| IMM-063 | `SharpProof.Contracts/ContractForSymbolMatcher.cs:50` | Relationship inventory retains mutable caller-owned HashSets; copy into immutable sets with original comparers. | Implemented; validation pending |
| IMM-064 | `SharpProof.Contracts/ContractClauseInventoryBuilder.cs:11` | Tree ordinals are fixed after construction; use ImmutableDictionary. | Implemented; validation pending |
| IMM-065 | `SharpProof.Contracts/ContractCanonicalization.cs:22` | Type specializer maps are constructed then queried; freeze or use persistent immutable updates. | Implemented; validation pending |
| IMM-066 | `SharpProof.Contracts/ContractCanonicalization.cs:349` | CreateSubstitutions erases an immutable return type; retain ImmutableDictionary in signature. | Implemented; validation pending |
| IMM-067 | `eng/testing/GoldenTest.cs:197` | GoldenCase retains and exposes mutable byte-array snapshots; copy to ImmutableArray preserving missing-vs-empty expectations. | Implemented; validation pending |
| IMM-068 | `scripts/SharpProof.PackageIdentity.psm1:4` | Fixed exported package IDs use a mutable array; publish a read-only or immutable collection. | Implemented; validation pending |
| IMM-069 | `scripts/SharpProof.PackageIdentity.psm1:10` | Fixed push-order array is mutable; publish a read-only or immutable collection. | Implemented; validation pending |
| IMM-070 | `scripts/SharpProof.ContainerExecution.psm1:481` | Fixed nested policy hashtables and override arrays are mutable; freeze every level. | Implemented; validation pending |

| IMM-071 | `SharpProof.Analyzer.Core/CompilerArtifact/CompilerExceptionTypeIdentity.cs:29` | Hierarchy results expose arrays; return sorted ImmutableArray. | Implemented; validation pending |
| IMM-072 | `SharpProof.Analyzer.Core/DeclarativeModels.generated.cs:19` | EffectClaimEvaluation retains mutable diagnostic arguments; use ImmutableArray and adapt the Roslyn params boundary. | Implemented; validation pending |
| IMM-073 | `SharpProof.Ir/IrInterpreter.cs:81` | Identity flags are construction-only; initialize readonly fields through a private constructor. | Implemented; validation pending |
| IMM-074 | `SharpProof.Ir/IrSequenceReads.cs:10` | Readers exposes mutable HashSet; build and return ImmutableHashSet and propagate worker field type. | Implemented; validation pending |
| IMM-075 | `SharpProof.Ir/IrSubstitution.cs:142` | Validated replacement map is mutable despite being fixed after capture; use ImmutableDictionary throughout rewrite. | Implemented; validation pending |
| IMM-076 | `SharpProof.Ir/IrInterpreter.cs:920` | Evaluation snapshots are assigned only at construction; capture an immutable set in a get-only property. | Implemented; validation pending |
| IMM-077 | `SharpProof.Ir/IrSequenceReads.cs:13` | Construction-only instruction snapshot uses an array; use ImmutableArray. | Implemented; validation pending |
| IMM-078 | `SharpProof.Ir/IrSubstitution.cs:113` | Construction-only root snapshot uses an array; use ImmutableArray. | Implemented; validation pending |
| IMM-079 | `SharpProof.BuildTasks/VerificationPublication.cs:8` | Publication path map is fixed after capture; snapshot ImmutableDictionary with ordinal comparer. | Implemented; validation pending |
| IMM-080 | `SharpProof.Worker/Launcher/LauncherProgram.cs:677` | Parsed launcher options retain a mutable dictionary; capture ImmutableDictionary with ordinal comparer. | Implemented; validation pending |
| IMM-081 | `SharpProof.Worker/Vc/PassiveCallableVcBuilder.cs:58` | Call marker lookup is built once; use ImmutableDictionary. | Implemented; validation pending |
| IMM-082 | `SharpProof.Worker/CallableClaimResultAssembler.cs:119` | Effect claim ID set escapes mutable; return ImmutableHashSet. | Implemented; validation pending |
| IMM-083 | `SharpProof.Worker/NativeEffectClaims.cs:96` | UsedRequires returns a mutable set; use persistent ImmutableHashSet. | Implemented; validation pending |
| IMM-084 | `SharpProof.Worker.Protocol/ProtocolJson.cs:1128` | Callable assumption lookup retains dictionary and array values; use immutable map and arrays, preserving first duplicate and null lookup behavior. | Implemented; validation pending |
| IMM-085 | `SharpProof.Worker.Protocol/ProtocolJson.cs:1159` | Identity index maps/nullable identity fields are constructor-only; use immutable map and readonly fields. | Implemented; validation pending |

| IMM-086 | `SharpProof.Frontend/CompilationModelProvider.cs:81` | Tree owner map is constructor-built; snapshot ImmutableDictionary with reference identity comparer. | Implemented; validation pending |
| IMM-087 | `SharpProof.Effects/EffectValues.cs:107` | Readonly throw-set value retains a mutable membership set; freeze it with symbol equality while preserving sorted type order. | Implemented; validation pending |
| IMM-088 | `SharpProof.Frontend/FrontendSubset.cs:180` | Five result metadata properties are assigned only at object construction; use init and the existing netstandard polyfill. | Implemented; validation pending |
| IMM-089 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:285` | Four diagnostic provenance properties are producer-only; initialize once and use init. | Implemented; validation pending |
| IMM-090 | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:154` | Serialization canonicalizes caller-owned objects in place; isolate canonical data before serialization, preserving hashes/wire output. | Open; coordinated model migration |
| IMM-091 | `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs:243` | DecodeCallables overwrites compilation authority fields; hydrate a separate immutable snapshot. | Open; coordinated model migration |
| IMM-092 | `SharpProof.Gates/Corpus/OpenSourceCorpusImporter.cs:16` | Import plan retains mutable Updates array; use ImmutableArray. | Implemented; validation pending |
| IMM-093 | `SharpProof.Gates/Corpus/CorpusSnapshotFormat.cs:25` | Parser result exposes mutable data-line array; publish ImmutableArray and adapt nullable test capture. | Implemented; validation pending |
| IMM-094 | `SharpProof.Gates/Corpus/OpenSourceCorpusCatalog.cs:365` | Source-ID validation returns mutable HashSet; use persistent immutable set and preserve duplicate rejection. | Implemented; validation pending |
| IMM-095 | `Tools/SharpProof.Fuzz/FuzzSourceCaseExecutor.cs:291` | Construction-only private method array; use ImmutableArray. | Implemented; validation pending |
| IMM-096 | `Tools/SharpProof.Fuzz/MetadataProgramSourceReplay.cs:41` | Construction-only library/source member arrays; use ImmutableArray. | Implemented; validation pending |
| IMM-097 | `SharpProof.Gates/Corpus/CorpusFileTransaction.cs:321` | Transaction journal record retains mutable Entries array; coordinate immutable representation with null/malformed JSON checks. | Open; boundary verification pending |
| IMM-098 | `SharpProof.Frontend.Test/RegionFilterEdgeResolverTests.cs:189` | Fixture record retains mutable block map; publish immutable map. | Open; caller verification pending |
| IMM-099 | `SharpProof.Ir.Test/IrIdentityCallbackTests.cs:25` | Comparisons exposes callback accumulator; publish immutable snapshots while preserving callback timing. | Open; caller verification pending |
| IMM-100 | `SharpProof.Worker.Test/LongFuzz/LongFuzzSession.cs:265` | Statistics record exposes mutable sorted dictionaries; publish immutable sorted snapshots. | Open; caller verification pending |
| IMM-101 | `SharpProof.Worker.Test/NativeTotalCandidateCoreTests.cs:362` | CoreRun exposes two producer-built mutable lists; publish ImmutableArray. | Open; caller verification pending |
| IMM-102 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:294` | IL observation record exposes mutable instructions; publish ImmutableArray. | Open; caller verification pending |
| IMM-103 | `SharpProof.Worker.Test/NullableDefaultBoxingZeroControlAuditTests.cs:353` | IL observation record exposes mutable instructions; publish ImmutableArray. | Open; caller verification pending |
| IMM-104 | `SharpProof.Worker.Test/PortableIrGraphCodecTests.cs:1184` | Codec fixture retains mutable Roots array; use ImmutableArray. | Open; caller verification pending |
| IMM-105 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:3270` | Source-consumer result exposes mutable assembly-path arrays; publish ImmutableArray. | Open; caller verification pending |
| IMM-106 | `SharpProof.Worker.Test/SemanticClaimRelativeSyntaxPathObservationTests.cs:314` | Runtime output record exposes mutable array; publish ImmutableArray preserving JSON array shape. | Open; caller verification pending |
| IMM-107 | `SharpProof.Worker.Test/LongFuzz/TypedFeatureLongFuzzTests.cs:222` | Execution record retains mutable CLR run list; publish ImmutableArray. | Open; caller verification pending |
| IMM-108 | `SharpProof.Worker.Test/WorkerTests.cs:5850` | RuntimeContractCase retains read-only input array; publish ImmutableArray. | Open; caller verification pending |
| IMM-109 | `SharpProof.Worker.Test/WorkerTests.cs:6338` | TestProject source paths are fixed after construction; publish ImmutableArray. | Open; caller verification pending |
| IMM-110 | `SharpProof.Worker.Test/SynchronizedAutoGetterPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; validation pending |
| IMM-111 | `SharpProof.Worker.Test/SynchronizedBodiedGetterCallerPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; validation pending |
| IMM-112 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; validation pending |
| IMM-113 | `SharpProof.Worker.Test/SynchronizedInstanceMethodPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; validation pending |
| IMM-114 | `SharpProof.Worker.Test/SynchronizedLocalFunctionPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; validation pending |
| IMM-115 | `SharpProof.Worker.Test/SynchronizedMethodPurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; validation pending |
| IMM-116 | `SharpProof.Worker.Test/SynchronizedSourceCalleePurityAuditTests.cs` | Observation records retain mutable return-value, parameter-type, clause or IL-instruction collections; publish immutable arrays and persistent fact lists. | Implemented; validation pending |

## Audit coverage and remaining review

The ten partitions are under review. Additional findings and explicit exclusions
will be reconciled here before the audit is considered complete. This inventory
is not yet a claim that every violation has been identified.

### Reviewed partitions

- Agent 04: all 13 SMT and 25 Contracts C# files reviewed; ten findings traced by coordinator.
- Agent 10: scripts, eng, root configuration, docs and CI reviewed; initial report missed GoldenCase byte arrays, found by coordinator cross-check and confirmed on follow-up. Four additional findings recorded above.

### Required mutable behavior verified so far

- `SharpProof.Worker.Test/WorkerResultAssemblerTests.cs:116`: `AssemblySnapshotsMutableBudgets` mutates the original budget to test snapshot isolation. Preserve this explicit mutable test subject.
- Mutable C# snippets and tutorial state examples retain their original semantics.
- Z3 native handles, disposal state, resource counters, and concurrency/lifecycle contracts require state transitions; immutable collection contents do not make those external resources immutable.

### Shared test-fixture collection inventory

Coordinator scan of all test projects found 306 fixed/shared collection declarations.
All declaration locations are outside embedded raw source and have no direct
element/collection writes in their declaring file. Each still requires semantic
classification: fixed expected data must be converted even when an API needs an
array adapter; actual mutable subjects and alias-identity controls are preserved.
The table records candidates as candidates until that check is complete.

| ID | Baseline location | Field and baseline type | Status |
| --- | --- | --- | --- |
| FIX-001 | `SharpProof.Analyzer.Test/AnalyzerArchitectureTests.cs:13` | `ExpectedAnalyzerReferences`: `string[]` | Implemented; API adapters under validation |
| FIX-002 | `SharpProof.Analyzer.Test/AnalyzerArchitectureTests.cs:25` | `ExpectedAnalyzerOutputAssemblies`: `string[]` | Implemented; API adapters under validation |
| FIX-003 | `SharpProof.Analyzer.Test/AnalyzerCoreFunctionPointerDispatchDomainTests.cs:9` | `DispatchControls`: `(string TypeName, int Expected)[]` | Implemented; API adapters under validation |
| FIX-004 | `SharpProof.Analyzer.Test/AnalyzerCoreMalformedCalleeDeclarationTests.cs:14` | `MissingBodyErrorIds`: `string[]` | Implemented; API adapters under validation |
| FIX-005 | `SharpProof.Analyzer.Test/AnalyzerCoreMissingFieldInitializerCoverageTests.cs:11` | `MissingValueCompilerErrors`: `string[]` | Implemented; API adapters under validation |
| FIX-006 | `SharpProof.Analyzer.Test/AnalyzerCoreScalarPromotionBudgetTests.cs:16` | `s_cases`: `FrozenCase[]` | Implemented; API adapters under validation |
| FIX-007 | `SharpProof.Analyzer.Test/CompilerCaptureRequestWorkflowTests.cs:13` | `SelectedPackIds`: `string[]` | Implemented; API adapters under validation |
| FIX-008 | `SharpProof.Analyzer.Test/CompilerLinkedModuleCountWorkflowTests.cs:11` | `ExpectedModuleNames`: `string[]` | Implemented; API adapters under validation |
| FIX-009 | `SharpProof.Analyzer.Test/CompilerMetadataAliasWorkflowTests.cs:13` | `AlternateAlias`: `string[]` | Implemented; API adapters under validation |
| FIX-010 | `SharpProof.Analyzer.Test/ContractForCycleAnalyzerTests.cs:11` | `DiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-011 | `SharpProof.Analyzer.Test/InitializerIntrinsicConstructorMatrixAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-012 | `SharpProof.Analyzer.Test/InitializerIntrinsicPlacementAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-013 | `SharpProof.Analyzer.Test/PrimaryConstructorIntrinsicPlacementAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-014 | `SharpProof.Analyzer.Test/PrimaryConstructorIntrinsicPlacementBoundaryAuditTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-015 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:11` | `AllowedReferences`: `Dictionary<string, string[]>` | Implemented; API adapters under validation |
| FIX-016 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:66` | `NonProductionProjects`: `HashSet<string>` | Implemented; API adapters under validation |
| FIX-017 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:77` | `CompilerFacingProjects`: `string[]` | Implemented; API adapters under validation |
| FIX-018 | `SharpProof.ArchitectureTest/ProjectDagTests.cs:88` | `Z3Consumers`: `string[]` | Implemented; API adapters under validation |
| FIX-019 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingAdditionalDomainTests.cs:11` | `PredicateInputs`: `long[]` | Implemented; API adapters under validation |
| FIX-020 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingAdditionalDomainTests.cs:12` | `MarkerNames`: `string[]` | Implemented; API adapters under validation |
| FIX-021 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingAdditionalDomainTests.cs:14` | `MetadataIntegerDiagnostics`: `string[]` | Implemented; API adapters under validation |
| FIX-022 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingComponentTests.cs:11` | `Modes`: `IrExecutionSemantics[]` | Implemented; API adapters under validation |
| FIX-023 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingComponentTests.cs:12` | `ExpectedDiagnostics`: `string[]` | Implemented; API adapters under validation |
| FIX-024 | `SharpProof.Contracts.Test/ClosedContractAttributeBindingComponentTests.cs:13` | `Inputs`: `long[]` | Implemented; API adapters under validation |
| FIX-025 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:12` | `TopLevelInventoryKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-026 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:16` | `InventoryKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-027 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:24` | `InventoryPlacements`: `ContractClausePlacement[]` | Implemented; API adapters under validation |
| FIX-028 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:32` | `InventoryOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-029 | `SharpProof.Contracts.Test/ContractClauseInventoryTests.cs:33` | `MisplacedPlacements`: `ContractClausePlacement[]` | Implemented; API adapters under validation |
| FIX-030 | `SharpProof.Contracts.Test/ContractInvocationMaterializationBoundaryTests.cs:14` | `BooleanValues`: `bool[]` | Implemented; API adapters under validation |
| FIX-031 | `SharpProof.Contracts.Test/ContractInvocationMaterializationBoundaryTests.cs:15` | `FullKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-032 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:14` | `BooleanValues`: `bool[]` | Implemented; API adapters under validation |
| FIX-033 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:15` | `BooleanClauseKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-034 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:21` | `RequiresOnlyKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-035 | `SharpProof.Contracts.Test/ContractInvocationMaterializationExtendedMatrixTests.cs:22` | `CompanionClauseKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-036 | `SharpProof.Contracts.Test/ContractInvocationMaterializationFacadeBaselineTests.cs:11` | `BooleanValues`: `bool[]` | Implemented; API adapters under validation |
| FIX-037 | `SharpProof.Contracts.Test/ContractInvocationMaterializationFacadeBaselineTests.cs:12` | `FullKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-038 | `SharpProof.Contracts.Test/ContractInvocationMaterializationFacadeBaselineTests.cs:18` | `RequiresOnlyKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-039 | `SharpProof.Contracts.Test/ContractVariableTransferBoundaryTests.cs:13` | `BooleanValues`: `bool[]` | Implemented; API adapters under validation |
| FIX-040 | `SharpProof.Contracts.Test/ExternAliasClosedAttributeBindingTests.cs:13` | `Inputs`: `int[]` | Implemented; API adapters under validation |
| FIX-041 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:13` | `IntegerDomains`: `(SpecialType Type, int Width, bool Signed)[]` | Implemented; API adapters under validation |
| FIX-042 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:26` | `IntegerBoundarySamples`: `long[][]` | Implemented; API adapters under validation |
| FIX-043 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:39` | `LongBoundParameterOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-044 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:40` | `ReferenceParameterOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-045 | `SharpProof.Contracts.Test/MetadataValueProjectionAdditionalMatrixTests.cs:41` | `ReferenceValueKinds`: `IrTypeKind[]` | Implemented; API adapters under validation |
| FIX-046 | `SharpProof.Contracts.Test/MetadataValueProjectionRepresentativeTests.cs:12` | `SingleAttributeNames`: `string[]` | Implemented; API adapters under validation |
| FIX-047 | `SharpProof.Contracts.Test/MetadataValueProjectionRepresentativeTests.cs:13` | `SingleParameterOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-048 | `SharpProof.Contracts.Test/MetadataValueProjectionRepresentativeTests.cs:14` | `PairParameterOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-049 | `SharpProof.Contracts.Test/TotalClosedAttributeBoundaryMatrixTests.cs:17` | `Types`: `string[]` | Implemented; API adapters under validation |
| FIX-050 | `SharpProof.Contracts.Test/TotalContractInvocationMaterializationAdditionalMatrixTests.cs:14` | `ThreeClauseKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-051 | `SharpProof.Contracts.Test/TotalContractInvocationMaterializationAdditionalMatrixTests.cs:22` | `RequiresOnlyKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-052 | `SharpProof.Contracts.Test/TotalContractInvocationMaterializationAdditionalMatrixTests.cs:23` | `OrderedDiagnosticTexts`: `string[]` | Implemented; API adapters under validation |
| FIX-053 | `SharpProof.Dataflow.Test/GeneratedDomainPropertyTests.cs:249` | `Moduli`: `int[]` | Implemented; API adapters under validation |
| FIX-054 | `SharpProof.Dataflow.Test/NullnessDomainTests.cs:6` | `Samples`: `NullnessValue[]` | Implemented; API adapters under validation |
| FIX-055 | `SharpProof.Effects.Test/DotnetApiInventoryTests.Comparisons.cs:9` | `IntegerComparisonEdges`: `(string Type, string[] Values)[]` | Implemented; API adapters under validation |
| FIX-056 | `SharpProof.Effects.Test/DotnetApiInventoryTests.TestEvidence.cs:11` | `EqualityEdges`: `(string? Left, string? Right)[]` | Implemented; API adapters under validation |
| FIX-057 | `SharpProof.Effects.Test/ExternalEffectBoundaryTests.cs:8` | `s_exceptionNames`: `string[]` | Implemented; API adapters under validation |
| FIX-058 | `SharpProof.Frontend.Test/BulkSourceApiDomainCoverageTests.cs:13` | `EnabledSymbols`: `string[]` | Implemented; API adapters under validation |
| FIX-059 | `SharpProof.Frontend.Test/BulkSourceApiDomainCoverageTests.cs:14` | `ConditionalNullErrors`: `string[]` | Implemented; API adapters under validation |
| FIX-060 | `SharpProof.Frontend.Test/CompilerErrorBinaryApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-061 | `SharpProof.Frontend.Test/CompilerErrorCoalesceNullApiCoverageTests.cs:14` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-062 | `SharpProof.Frontend.Test/CompilerErrorConditionalApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-063 | `SharpProof.Frontend.Test/CompilerErrorEmptyConcatenationApiTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-064 | `SharpProof.Frontend.Test/CompilerErrorNameOfFieldAssignmentApiTests.cs:14` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-065 | `SharpProof.Frontend.Test/CompilerErrorReferenceNullComparisonApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-066 | `SharpProof.Frontend.Test/CompilerErrorUnaryApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-067 | `SharpProof.Frontend.Test/CompilerErrorUnaryPlusApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-068 | `SharpProof.Frontend.Test/ConditionalInvalidReturnCoverageTests.cs:17` | `VoidReturnErrors`: `string[]` | Implemented; API adapters under validation |
| FIX-069 | `SharpProof.Frontend.Test/ConditionalInvalidReturnCoverageTests.cs:18` | `NonVoidConditionalErrors`: `string[]` | Implemented; API adapters under validation |
| FIX-070 | `SharpProof.Frontend.Test/ConditionalMetadataSymbolReaderTests.cs:13` | `ExpectedSymbols`: `string[]` | Implemented; API adapters under validation |
| FIX-071 | `SharpProof.Frontend.Test/ConditionalMethodIdentityPolicyTests.cs:17` | `DuplicateErrors`: `string[]` | Implemented; API adapters under validation |
| FIX-072 | `SharpProof.Frontend.Test/CSharpScalarSemanticsTests.cs:12` | `ExpectedIntegerTypes`: `Dictionary<SpecialType, Type>` | Implemented; API adapters under validation |
| FIX-073 | `SharpProof.Frontend.Test/ElementGuardNarrowInputCoverageTests.cs:10` | `ExpectedFaults`: `IrExceptionKind[]` | Implemented; API adapters under validation |
| FIX-074 | `SharpProof.Frontend.Test/ExceptionKindBoundaryCoverageTests.cs:13` | `OverflowKinds`: `IrExceptionKind[]` | Implemented; API adapters under validation |
| FIX-075 | `SharpProof.Frontend.Test/ExplicitLayoutFieldAdmissionTests.cs:19` | `Int32FieldSignature`: `byte[]` | Implemented; API adapters under validation |
| FIX-076 | `SharpProof.Frontend.Test/ForeignSyntaxTreeSymbolHelperCoverageTests.cs:10` | `DefinedSymbols`: `KeyValuePair<string, object>[]` | Implemented; API adapters under validation |
| FIX-077 | `SharpProof.Frontend.Test/FrontendBoundaryCoverageTests.cs:12` | `ExpectedDefinedSymbols`: `string[]` | Implemented; API adapters under validation |
| FIX-078 | `SharpProof.Frontend.Test/FrontendLoweringTests.cs:16` | `ElementValues`: `long[]` | Implemented; API adapters under validation |
| FIX-079 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:22` | `EnabledSymbols`: `string[]` | Implemented; API adapters under validation |
| FIX-080 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:23` | `OmittedTargetIl`: `byte[]` | Implemented; API adapters under validation |
| FIX-081 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:24` | `UnderlyingArities`: `int[]` | Implemented; API adapters under validation |
| FIX-082 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:25` | `NativeArities`: `int[]` | Implemented; API adapters under validation |
| FIX-083 | `SharpProof.Frontend.Test/GenericNativeIntegerConditionalEmissionApiTests.cs:190` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-084 | `SharpProof.Frontend.Test/NativeIntegerConditionalEmissionApiTests.cs:23` | `EnabledSymbols`: `string[]` | Implemented; API adapters under validation |
| FIX-085 | `SharpProof.Frontend.Test/NativeIntegerConditionalEmissionApiTests.cs:259` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-086 | `SharpProof.Frontend.Test/NestedLocalConditionalErrorCoverageTests.cs:15` | `ExpectedErrorIds`: `string[]` | Implemented; API adapters under validation |
| FIX-087 | `SharpProof.Frontend.Test/NullValueCompilerApiCoverageTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-088 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:11` | `ExpectedBatchResults`: `bool[]` | Implemented; API adapters under validation |
| FIX-089 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:12` | `ExpectedBatchCalls`: `string[]` | Implemented; API adapters under validation |
| FIX-090 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:13` | `ExpectedReentryCalls`: `string[]` | Implemented; API adapters under validation |
| FIX-091 | `SharpProof.Frontend.Test/OperationPurityScopeCoverageTests.cs:14` | `ExpectedBothScopeCalls`: `string[]` | Implemented; API adapters under validation |
| FIX-092 | `SharpProof.Frontend.Test/OwnedVoidReturnRecoveryCoverageTests.cs:16` | `RecoveryErrorIds`: `string[]` | Implemented; API adapters under validation |
| FIX-093 | `SharpProof.Frontend.Test/Pr275BulkFrontendApiTests.cs:13` | `ExpectedShortArrayFaults`: `IrExceptionKind[]` | Implemented; API adapters under validation |
| FIX-094 | `SharpProof.Frontend.Test/ReferencedTypeSymbolsTests.cs:11` | `ExpectedTraversalOrder`: `string[]` | Implemented; API adapters under validation |
| FIX-095 | `SharpProof.Frontend.Test/RequiresOnlyNestedInitializerContinuationTests.cs:17` | `RuntimeInputs`: `bool[]` | Implemented; API adapters under validation |
| FIX-096 | `SharpProof.Frontend.Test/RequiresOnlySwitchArmScopeTests.cs:21` | `expectedClrReturns`: `int[]` | Implemented; API adapters under validation |
| FIX-097 | `SharpProof.Frontend.Test/RequiresOnlyTerminalSpecificationTests.cs:17` | `RuntimeInputs`: `bool[]` | Implemented; API adapters under validation |
| FIX-098 | `SharpProof.Frontend.Test/ScalarPatternConstantApiCoverageTests.cs:14` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-099 | `SharpProof.Frontend.Test/SourceNativeIntegerConditionalEmissionApiTests.cs:19` | `EnabledSymbols`: `string[]` | Implemented; API adapters under validation |
| FIX-100 | `SharpProof.Frontend.Test/SourceNativeIntegerConditionalEmissionApiTests.cs:20` | `OmittedTargetIl`: `byte[]` | Implemented; API adapters under validation |
| FIX-101 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:44` | `Categories`: `string[]` | Implemented; API adapters under validation |
| FIX-102 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:45` | `JoinTable`: `string?[][]` | Implemented; API adapters under validation |
| FIX-103 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:54` | `ReferenceTable`: `bool[][]` | Implemented; API adapters under validation |
| FIX-104 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:64` | `AcceptanceTable`: `bool[][]` | Implemented; API adapters under validation |
| FIX-105 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:73` | `Targets`: `string[]` | Implemented; API adapters under validation |
| FIX-106 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:74` | `TargetColumns`: `int[]` | Implemented; API adapters under validation |
| FIX-107 | `SharpProof.Frontend.Test/StackTypeCalculusCoverageTests.cs:75` | `Methods`: `Dictionary<string, string>` | Implemented; API adapters under validation |
| FIX-108 | `SharpProof.Frontend.Test/SwitchExpressionTerminalElisionTests.cs:20` | `EnabledSymbols`: `string[]` | Implemented; API adapters under validation |
| FIX-109 | `SharpProof.Frontend.Test/SwitchExpressionTerminalElisionTests.cs:21` | `Inputs`: `(int Value, int Other)[]` | Implemented; API adapters under validation |
| FIX-110 | `SharpProof.Frontend.Test/ValidatedArrayInitializerDepthCoverageTests.cs:13` | `Inputs`: `int[]` | Implemented; API adapters under validation |
| FIX-111 | `SharpProof.Frontend.Test/ValueTypeValidationBoundaryTests.cs:13` | `CSharpError`: `string[]` | Implemented; API adapters under validation |
| FIX-112 | `SharpProof.Frontend.Test/ValueTypeValidationBoundaryTests.cs:14` | `VisualBasicError`: `string[]` | Implemented; API adapters under validation |
| FIX-113 | `SharpProof.Frontend.Test/VisualBasicObjectShiftCountCoverageTests.cs:17` | `RuntimeMethodNames`: `string[]` | Implemented; API adapters under validation |
| FIX-114 | `SharpProof.Frontend.Test/VisualBasicPrintStatementRecoveryApiTests.cs:16` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-115 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:11` | `SequenceValue`: `long[]` | Implemented; API adapters under validation |
| FIX-116 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeCaseTests.cs:12` | `UnsupportedSequenceValue`: `ulong[]` | Implemented; API adapters under validation |
| FIX-117 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeTopologyControls.cs:13` | `OverloadedNames`: `string[]` | Implemented; API adapters under validation |
| FIX-118 | `SharpProof.Fuzz.Test/FrontendSemanticEdgeTopologyControls.cs:14` | `OverloadedParameterCounts`: `int[]` | Implemented; API adapters under validation |
| FIX-119 | `SharpProof.Fuzz.Test/FuzzEmissionLifecycleTests.cs:17` | `ParameterBindingMethodNames`: `string[]` | Implemented; API adapters under validation |
| FIX-120 | `SharpProof.Fuzz.Test/FuzzSourceBooleanInputControls.cs:13` | `FalsePair`: `bool[]` | Implemented; API adapters under validation |
| FIX-121 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:13` | `FrontendStatuses`: `FuzzOracleStatus[]` | Implemented; API adapters under validation |
| FIX-122 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:15` | `FiniteStatuses`: `FuzzOracleStatus[]` | Implemented; API adapters under validation |
| FIX-123 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:17` | `PartialStatuses`: `FuzzOracleStatus[]` | Implemented; API adapters under validation |
| FIX-124 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:19` | `FailureKeys`: `(int Case, string Oracle)[]` | Implemented; API adapters under validation |
| FIX-125 | `SharpProof.Fuzz.Test/FuzzSourceCampaignReplayTests.cs:21` | `FaultMethodNames`: `string[]` | Implemented; API adapters under validation |
| FIX-126 | `SharpProof.Gates.Test/CorpusTransactionWorkflowTests.cs:9` | `ExpectedFiles`: `string[]` | Implemented; API adapters under validation |
| FIX-127 | `SharpProof.Gates.Test/CorpusWallTimePolicyTests.cs:9` | `ExpectedFailure`: `string[]` | Implemented; API adapters under validation |
| FIX-128 | `SharpProof.Gates.Test/OrdinaryCorpusDiagnosticWorkflowTests.cs:13` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-129 | `SharpProof.Gates.Test/OrdinaryPartialDiagnosticRegressionTests.cs:17` | `ExpectedDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-130 | `SharpProof.Gates.Test/OrdinaryReferenceEmissionWorkflowTests.cs:11` | `ExpectedAttributeNames`: `string[]` | Implemented; API adapters under validation |
| FIX-131 | `SharpProof.Ir.Test/IrIdentifierTests.cs:9` | `s_identifierPrefixes`: `string[]` | Implemented; API adapters under validation |
| FIX-132 | `SharpProof.Ir.Test/IrTerminatorAdmissionTests.cs:8` | `s_terminalKinds`: `IrInstructionKind[]` | Implemented; API adapters under validation |
| FIX-133 | `SharpProof.Package.Test/PackagedProductFeed.cs:29` | `s_expectedPackageIds`: `string[]` | Implemented; API adapters under validation |
| FIX-134 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:36` | `ExpectedAnalyzerEntryFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-135 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:40` | `ExpectedCodeFixEntryFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-136 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:44` | `ExpectedGeneratorEntryFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-137 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:48` | `ExpectedAnalyzerDependencyFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-138 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:69` | `ExpectedCollectorEntryFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-139 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:73` | `ExpectedSourceAnalyzerProjectFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-140 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:81` | `ExpectedCollectorDependencyFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-141 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:90` | `ExpectedConditionalAnalyzerEntries`: `string[]` | Implemented; API adapters under validation |
| FIX-142 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:118` | `ExpectedToolEntries`: `string[]` | Implemented; API adapters under validation |
| FIX-143 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:136` | `ExpectedNativeZ3Entries`: `string[]` | Implemented; API adapters under validation |
| FIX-144 | `SharpProof.Package.Test/PackageLayoutSmokeTests.cs:143` | `ExpectedDependencyAttributes`: `string[]` | Implemented; API adapters under validation |
| FIX-145 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:25` | `s_publicPolicyProperties`: `string[]` | Implemented; API adapters under validation |
| FIX-146 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:32` | `s_analyzerConfigurationProperties`: `string[]` | Implemented; API adapters under validation |
| FIX-147 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:36` | `s_compilerManifestProperties`: `string[]` | Implemented; API adapters under validation |
| FIX-148 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:41` | `s_reconstructionArguments`: `string[]` | Implemented; API adapters under validation |
| FIX-149 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:47` | `s_reconstructionListArtifacts`: `string[]` | Implemented; API adapters under validation |
| FIX-150 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:53` | `s_manualConsumerElementOrder`: `string[]` | Implemented; API adapters under validation |
| FIX-151 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:63` | `s_manualConsumerImportOrder`: `string[]` | Implemented; API adapters under validation |
| FIX-152 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:69` | `s_runtimeClosureProperties`: `string[]` | Implemented; API adapters under validation |
| FIX-153 | `SharpProof.Package.Test/WorkerMsBuildIntegrationTests.cs:74` | `s_removedPackagePathProperties`: `string[]` | Implemented; API adapters under validation |
| FIX-154 | `SharpProof.Worker.Test/ActualOwnedSessionEntryCompletionTests.cs:22` | `RuntimeControlArguments`: `object[]` | Implemented; API adapters under validation |
| FIX-155 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:29` | `RetainedInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-156 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:30` | `ZeroInput`: `int[]` | Implemented; API adapters under validation |
| FIX-157 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:31` | `OneInput`: `int[]` | Implemented; API adapters under validation |
| FIX-158 | `SharpProof.Worker.Test/AliasOnlyConditionalAttributeAuditTests.cs:33` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-159 | `SharpProof.Worker.Test/AnalyzerCoreAdvisoryLanguageBoundaryTests.cs:16` | `ExpectedIds`: `string[]` | Implemented; API adapters under validation |
| FIX-160 | `SharpProof.Worker.Test/AnalyzerCorePublicDriverSharedTreeBoundaryTests.cs:13` | `RefusalDiagnosticIds`: `string[]` | Implemented; API adapters under validation |
| FIX-161 | `SharpProof.Worker.Test/BulkFrontendIlMetadataIdentityBoundaryTests.cs:28` | `Inputs`: `bool[]` | Implemented; API adapters under validation |
| FIX-162 | `SharpProof.Worker.Test/BulkFrontendIlMetadataIdentityBoundaryTests.cs:29` | `ExpectedCodes`: `string[]` | Implemented; API adapters under validation |
| FIX-163 | `SharpProof.Worker.Test/BulkFrontendIlMetadataIdentityBoundaryTests.cs:30` | `ExpectedPreconditions`: `bool[]` | Implemented; API adapters under validation |
| FIX-164 | `SharpProof.Worker.Test/BulkFrontendIlModuleScopeBoundaryTests.cs:30` | `Inputs`: `bool[]` | Implemented; API adapters under validation |
| FIX-165 | `SharpProof.Worker.Test/BulkFrontendIlModuleScopeBoundaryTests.cs:31` | `ExpectedCodes`: `string[]` | Implemented; API adapters under validation |
| FIX-166 | `SharpProof.Worker.Test/BulkFrontendIlModuleScopeBoundaryTests.cs:32` | `ExpectedPreconditions`: `bool[]` | Implemented; API adapters under validation |
| FIX-167 | `SharpProof.Worker.Test/BulkFrontendIlNullOperandOrderTests.cs:20` | `ExpectedLeft`: `string[]` | Implemented; API adapters under validation |
| FIX-168 | `SharpProof.Worker.Test/BulkFrontendIlNullOperandOrderTests.cs:21` | `ExpectedRight`: `string[]` | Implemented; API adapters under validation |
| FIX-169 | `SharpProof.Worker.Test/BulkMetadataDefaultWorkBoundaryTests.cs:18` | `DivisionInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-170 | `SharpProof.Worker.Test/BulkMetadataDefaultWorkBoundaryTests.cs:19` | `MetadataInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-171 | `SharpProof.Worker.Test/BulkMetadataDefaultWorkBoundaryTests.cs:20` | `ExpectedPreconditions`: `bool[]` | Implemented; API adapters under validation |
| FIX-172 | `SharpProof.Worker.Test/CacheEvidenceOrderingControlTests.cs:10` | `s_unordered`: `string[]` | Implemented; API adapters under validation |
| FIX-173 | `SharpProof.Worker.Test/CacheEvidenceOrderingControlTests.cs:11` | `s_ordered`: `string[]` | Implemented; API adapters under validation |
| FIX-174 | `SharpProof.Worker.Test/CacheEvidenceOrderingControlTests.cs:12` | `s_orderedClaims`: `string[]` | Implemented; API adapters under validation |
| FIX-175 | `SharpProof.Worker.Test/CallerBoxingInterfaceAuditTests.cs:24` | `TargetNames`: `string[]` | Implemented; API adapters under validation |
| FIX-176 | `SharpProof.Worker.Test/CallerBoxingInterfaceAuditTests.cs:25` | `IdentityBody`: `byte[]` | Implemented; API adapters under validation |
| FIX-177 | `SharpProof.Worker.Test/CallerBoxingInterfaceAuditTests.cs:26` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-178 | `SharpProof.Worker.Test/CapturedPrimaryConstructorStorageAuditTests.cs:30` | `s_opCodes`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-179 | `SharpProof.Worker.Test/CapturedPrimaryConstructorStorageAuditTests.cs:36` | `s_cases`: `Case[]` | Implemented; API adapters under validation |
| FIX-180 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:19` | `DenseOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-181 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:20` | `FailingShadowInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-182 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:21` | `PotentialOwnerGapReasons`: `string[]` | Implemented; API adapters under validation |
| FIX-183 | `SharpProof.Worker.Test/ClaimManifestBuilderTests.cs:23` | `CompanionEvidence`: `WorkerClaimEvidence[]` | Implemented; API adapters under validation |
| FIX-184 | `SharpProof.Worker.Test/CompilerClosedParameterCaptureBoundaryTests.cs:17` | `ExpectedParameterOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-185 | `SharpProof.Worker.Test/CompilerClosedParameterCaptureBoundaryTests.cs:18` | `ExpectedAttributeKinds`: `string[]` | Implemented; API adapters under validation |
| FIX-186 | `SharpProof.Worker.Test/CompilerEvidenceValidationBoundaryTests.cs:17` | `ExpectedClaimIds`: `string[]` | Implemented; API adapters under validation |
| FIX-187 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:17` | `ScalarCases`: `(byte[] Bytes, string Code, long Operand, int Cursor, int Target)[]` | Implemented; API adapters under validation |
| FIX-188 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:29` | `OffsetInput`: `byte[]` | Implemented; API adapters under validation |
| FIX-189 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:30` | `UnknownSingle`: `byte[]` | Implemented; API adapters under validation |
| FIX-190 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:31` | `KnownSingleNeighbor`: `byte[]` | Implemented; API adapters under validation |
| FIX-191 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:32` | `UnknownDouble`: `byte[]` | Implemented; API adapters under validation |
| FIX-192 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:33` | `KnownDoubleNeighbor`: `byte[]` | Implemented; API adapters under validation |
| FIX-193 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:34` | `StringOperand`: `byte[]` | Implemented; API adapters under validation |
| FIX-194 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:35` | `TruncatedPrefix`: `byte[]` | Implemented; API adapters under validation |
| FIX-195 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:36` | `TruncatedInteger`: `byte[]` | Implemented; API adapters under validation |
| FIX-196 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:37` | `EndTarget`: `byte[]` | Implemented; API adapters under validation |
| FIX-197 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:38` | `NegativeTarget`: `byte[]` | Implemented; API adapters under validation |
| FIX-198 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:39` | `OverflowTarget`: `byte[]` | Implemented; API adapters under validation |
| FIX-199 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:40` | `ForwardBoundary`: `byte[]` | Implemented; API adapters under validation |
| FIX-200 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:41` | `ForwardInterior`: `byte[]` | Implemented; API adapters under validation |
| FIX-201 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:42` | `BackwardBoundary`: `byte[]` | Implemented; API adapters under validation |
| FIX-202 | `SharpProof.Worker.Test/CompilerIlByteParserTests.cs:43` | `StringControls`: `(int Input, int Expected)[]` | Implemented; API adapters under validation |
| FIX-203 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:11` | `ExpectedPartialReads`: `(int Offset, int Count)[]` | Implemented; API adapters under validation |
| FIX-204 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:12` | `ExpectedUntouchedDestination`: `byte[]` | Implemented; API adapters under validation |
| FIX-205 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:13` | `TwoByteSource`: `byte[]` | Implemented; API adapters under validation |
| FIX-206 | `SharpProof.Worker.Test/CompilerIlStreamFillTests.cs:14` | `OneByteSource`: `byte[]` | Implemented; API adapters under validation |
| FIX-207 | `SharpProof.Worker.Test/CompilerOwnedMethodBodyGraphTests.cs:15` | `ExpectedOwnerNames`: `string[]` | Implemented; API adapters under validation |
| FIX-208 | `SharpProof.Worker.Test/CompilerReferenceStackMergeCoverageTests.cs:11` | `Choices`: `bool[]` | Implemented; API adapters under validation |
| FIX-209 | `SharpProof.Worker.Test/CompilerReportingTests.cs:22` | `CapturedPathCases`: `string[][]` | Implemented; API adapters under validation |
| FIX-210 | `SharpProof.Worker.Test/ConcatenationCallOperandAllocationTests.cs:19` | `s_spliced`: `string[]` | Implemented; API adapters under validation |
| FIX-211 | `SharpProof.Worker.Test/ConcatenationCallOperandAllocationTests.cs:31` | `s_separate`: `string[]` | Implemented; API adapters under validation |
| FIX-212 | `SharpProof.Worker.Test/ConcatenationIdentityCastAllocationTests.cs:19` | `s_spliced`: `string[]` | Implemented; API adapters under validation |
| FIX-213 | `SharpProof.Worker.Test/ConcatenationIdentityCastAllocationTests.cs:29` | `s_separate`: `string[]` | Implemented; API adapters under validation |
| FIX-214 | `SharpProof.Worker.Test/ConditionalAttributeCompilerAuthorityBoundaryTests.cs:24` | `RetainedInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-215 | `SharpProof.Worker.Test/ConditionalAttributeCompilerAuthorityBoundaryTests.cs:25` | `OmittedInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-216 | `SharpProof.Worker.Test/ConditionalAttributeCompilerAuthorityBoundaryTests.cs:29` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-217 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:24` | `RetainedInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-218 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:25` | `OmittedInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-219 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:26` | `TwoByteColumns`: `int[]` | Implemented; API adapters under validation |
| FIX-220 | `SharpProof.Worker.Test/ConditionalMetadataRejectionAuditTests.cs:31` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-221 | `SharpProof.Worker.Test/CountUpHoudiniResourceBoundaryTests.cs:125` | `RuntimeInputs`: `int[]` | Implemented; API adapters under validation |
| FIX-222 | `SharpProof.Worker.Test/EmptyIteratorAllocationAuditTests.cs:34` | `s_opCodes`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-223 | `SharpProof.Worker.Test/EmptyIteratorAllocationAuditTests.cs:39` | `s_cases`: `Case[]` | Implemented; API adapters under validation |
| FIX-224 | `SharpProof.Worker.Test/FloatingEqualityEvaluationOrderAuditTests.cs:17` | `ExplicitArgumentKinds`: `ArgumentKind[]` | Implemented; API adapters under validation |
| FIX-225 | `SharpProof.Worker.Test/FloatingEqualityEvaluationOrderAuditTests.cs:18` | `FloatingComparisonKinds`: `BinaryOperatorKind[]` | Implemented; API adapters under validation |
| FIX-226 | `SharpProof.Worker.Test/GenericFixedTestedTypeControlAuditTests.cs:44` | `Forms`: `string[]` | Implemented; API adapters under validation |
| FIX-227 | `SharpProof.Worker.Test/GenericImplicitTypeTestAllocationTests.cs:17` | `s_implicit`: `string[]` | Implemented; API adapters under validation |
| FIX-228 | `SharpProof.Worker.Test/GenericImplicitTypeTestAllocationTests.cs:31` | `s_folded`: `string[]` | Implemented; API adapters under validation |
| FIX-229 | `SharpProof.Worker.Test/GenericNullableTypeTestAllocationTests.cs:15` | `s_nullable`: `(string Members, string Run, bool ReleaseAllocates)[]` | Implemented; API adapters under validation |
| FIX-230 | `SharpProof.Worker.Test/GenericNullableTypeTestAllocationTests.cs:37` | `s_nonNullable`: `(string Members, string Run)[]` | Implemented; API adapters under validation |
| FIX-231 | `SharpProof.Worker.Test/GenericTestedTypePatternControlAuditTests.cs:26` | `ClosedRuntimeTypes`: `string[]` | Implemented; API adapters under validation |
| FIX-232 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:33` | `s_ints`: `int[]` | Implemented; API adapters under validation |
| FIX-233 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:34` | `s_arrays`: `int[]?[]` | Implemented outer table; mutable alias referents preserved |
| FIX-234 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:35` | `s_strings`: `string?[]` | Implemented; API adapters under validation |
| FIX-235 | `SharpProof.Worker.Test/LongFuzz/AnalyzerPipelineLongFuzzTests.cs:37` | `s_boundaryInputs`: `(int X, int Y, int[]? Array, string? Text)[]` | Implemented outer table; mutable alias referents preserved |
| FIX-236 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:43` | `s_operations`: `Operation[]` | Implemented; API adapters under validation |
| FIX-237 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:54` | `s_scopes`: `Scope[]` | Implemented; API adapters under validation |
| FIX-238 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:66` | `s_inputs`: `int[]` | Implemented; API adapters under validation |
| FIX-239 | `SharpProof.Worker.Test/LongFuzz/CompositionalLongFuzzTests.cs:69` | `s_cycle`: `(int Operation, int Scope)[]` | Implemented; API adapters under validation |
| FIX-240 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:17` | `s_modes`: `string[]` | Implemented; API adapters under validation |
| FIX-241 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:18` | `s_comparisons`: `string[]` | Implemented; API adapters under validation |
| FIX-242 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:19` | `s_simpleStatements`: `int[]` | Implemented; API adapters under validation |
| FIX-243 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:20` | `s_simpleInts`: `int[]` | Implemented; API adapters under validation |
| FIX-244 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:21` | `s_compoundOperators`: `string[]` | Implemented; API adapters under validation |
| FIX-245 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:22` | `s_exceptions`: `string[]` | Implemented; API adapters under validation |
| FIX-246 | `SharpProof.Worker.Test/LongFuzz/ContractClaimProgramGenerator.cs:23` | `s_extremes`: `string[]` | Implemented; API adapters under validation |
| FIX-247 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:24` | `s_values`: `int[]` | Implemented; API adapters under validation |
| FIX-248 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:25` | `s_specMaxExpressions`: `string[]` | Implemented; API adapters under validation |
| FIX-249 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:32` | `s_specMaxMaximum`: `int[][]` | Implemented; API adapters under validation |
| FIX-250 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:33` | `s_specMaxValue`: `int[][]` | Implemented; API adapters under validation |
| FIX-251 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:65` | `s_domains`: `Domain[]` | Implemented; API adapters under validation |
| FIX-252 | `SharpProof.Worker.Test/LongFuzz/ContractMatrixLongFuzzTests.cs:73` | `s_jointDomains`: `string[]` | Implemented; API adapters under validation |
| FIX-253 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:30` | `s_wrappers`: `string[]` | Implemented; API adapters under validation |
| FIX-254 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:75` | `s_leaves`: `string[]` | Implemented; API adapters under validation |
| FIX-255 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:107` | `s_manual`: `string[]` | Implemented; API adapters under validation |
| FIX-256 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:129` | `s_cleanupScopes`: `(string Name, string Body)[]` | Implemented; API adapters under validation |
| FIX-257 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:139` | `s_cleanupExits`: `(string Name, string Body)[]` | Implemented; API adapters under validation |
| FIX-258 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:150` | `s_disposalCases`: `(string Name, string Body)[]` | Implemented; API adapters under validation |
| FIX-259 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:159` | `s_modes`: `string[]` | Implemented; API adapters under validation |
| FIX-260 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:162` | `s_tails`: `string[]` | Implemented; API adapters under validation |
| FIX-261 | `SharpProof.Worker.Test/LongFuzz/ControlFlowLongFuzzTests.cs:164` | `s_inputs`: `int[]` | Implemented; API adapters under validation |
| FIX-262 | `SharpProof.Worker.Test/LongFuzz/LongFuzzRuntimeCoverageRegressionTests.cs:14` | `SingleItem`: `int[]` | Implemented; API adapters under validation |
| FIX-263 | `SharpProof.Worker.Test/LongFuzz/LongFuzzRuntimeCoverageRegressionTests.cs:15` | `ClaimValidity`: `bool[]` | Implemented; API adapters under validation |
| FIX-264 | `SharpProof.Worker.Test/LongFuzz/RoslynSyntaxMutationRegressionTests.cs:18` | `Expressions`: `string[]` | Implemented; API adapters under validation |
| FIX-265 | `SharpProof.Worker.Test/LongFuzz/TypedFeatureLongFuzzTests.cs:25` | `s_anchors`: `int[]` | Implemented; API adapters under validation |
| FIX-266 | `SharpProof.Worker.Test/LongFuzz/TypedFeatureLongFuzzTests.cs:26` | `s_families`: `string[]` | Implemented; API adapters under validation |
| FIX-267 | `SharpProof.Worker.Test/MetadataInvocationBindingInvariantTests.cs:17` | `ExpectedOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-268 | `SharpProof.Worker.Test/MetadataInvocationBindingInvariantTests.cs:18` | `ExpectedRecoveryDiagnostics`: `string[]` | Implemented; API adapters under validation |
| FIX-269 | `SharpProof.Worker.Test/MetadataZeroArgumentCallDepthApiTests.cs:16` | `LeafInstructions`: `string[]` | Implemented; API adapters under validation |
| FIX-270 | `SharpProof.Worker.Test/MetadataZeroArgumentCallDepthApiTests.cs:17` | `CallingInstructions`: `string[]` | Implemented; API adapters under validation |
| FIX-271 | `SharpProof.Worker.Test/NativeMetadataShadowRequiresTests.cs:20` | `SuccessfulObservation`: `bool[]` | Implemented; API adapters under validation |
| FIX-272 | `SharpProof.Worker.Test/NativeTotalCandidateCoreTests.cs:14` | `TwoOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-273 | `SharpProof.Worker.Test/NativeTotalCandidateCoreTests.cs:15` | `InitialAndFinalOrdinals`: `int[]` | Implemented; API adapters under validation |
| FIX-274 | `SharpProof.Worker.Test/NullableDefaultBoxingZeroControlAuditTests.cs:23` | `IdentityBody`: `byte[]` | Implemented; API adapters under validation |
| FIX-275 | `SharpProof.Worker.Test/NullableDefaultBoxingZeroControlAuditTests.cs:24` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-276 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:24` | `WitnessValues`: `decimal[]` | Implemented; API adapters under validation |
| FIX-277 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:25` | `IdentityBody`: `byte[]` | Implemented; API adapters under validation |
| FIX-278 | `SharpProof.Worker.Test/OptimizedDiscardedConcreteBoxingAuditTests.cs:26` | `OpCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-279 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:13` | `FullClauseKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-280 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:15` | `RequiresClauseKinds`: `BoundContractKind[]` | Implemented; API adapters under validation |
| FIX-281 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:17` | `FullClauseTexts`: `string[]` | Implemented; API adapters under validation |
| FIX-282 | `SharpProof.Worker.Test/Pr275BulkContractsApiTests.cs:19` | `RequiresClauseTexts`: `string[]` | Implemented; API adapters under validation |
| FIX-283 | `SharpProof.Worker.Test/PrimitiveArrayAliasControlTests.cs:9` | `Pairs`: `string[][]` | Implemented; API adapters under validation |
| FIX-284 | `SharpProof.Worker.Test/ProtocolJsonTests.cs:20` | `s_requestProperties`: `string[]` | Implemented; API adapters under validation |
| FIX-285 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:209` | `SupportedCases`: `ScalarCase[]` | Implemented; API adapters under validation |
| FIX-286 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:244` | `WideningCases`: `WideningCase[]` | Implemented; API adapters under validation |
| FIX-287 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:268` | `ArithmeticCases`: `ArithmeticCase[]` | Implemented; API adapters under validation |
| FIX-288 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:353` | `RequiredReferenceFileNames`: `string[]` | Implemented; API adapters under validation |
| FIX-289 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:360` | `UnaryParameterNames`: `string[]` | Implemented; API adapters under validation |
| FIX-290 | `SharpProof.Worker.Test/ScalarDifferentialMatrixTests.cs:361` | `BinaryParameterNames`: `string[]` | Implemented; API adapters under validation |
| FIX-291 | `SharpProof.Worker.Test/ScalarPatternFlowTests.cs:20` | `Inputs`: `int[]` | Implemented; API adapters under validation |
| FIX-292 | `SharpProof.Worker.Test/SynchronizedAutoSetterCallerPurityAuditTests.cs:21` | `s_inputValues`: `int[]` | Implemented; API adapters under validation |
| FIX-293 | `SharpProof.Worker.Test/SynchronizedBodiedGetterCallerPurityAuditTests.cs:21` | `s_inputValues`: `int[]` | Implemented; API adapters under validation |
| FIX-294 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:20` | `s_inputValues`: `int[]` | Implemented; API adapters under validation |
| FIX-295 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:21` | `s_expectedReturns`: `bool[]` | Implemented; API adapters under validation |
| FIX-296 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:22` | `s_expectedFilterOpCodes`: `string[]` | Implemented; API adapters under validation |
| FIX-297 | `SharpProof.Worker.Test/SynchronizedFilterSharedFramePurityAuditTests.cs:23` | `s_opCodesByValue`: `Dictionary<ushort, OpCode>` | Implemented; API adapters under validation |
| FIX-298 | `SharpProof.Worker.Test/SynchronizedInstanceMethodPurityAuditTests.cs:19` | `s_inputValues`: `int[]` | Implemented; API adapters under validation |
| FIX-299 | `SharpProof.Worker.Test/SynchronizedLocalFunctionPurityAuditTests.cs:20` | `s_inputValues`: `int[]` | Implemented; API adapters under validation |
| FIX-300 | `SharpProof.Worker.Test/SynchronizedMethodPurityAuditTests.cs:19` | `s_inputValues`: `int[]` | Implemented; API adapters under validation |
| FIX-301 | `SharpProof.Worker.Test/SynchronizedSourceCalleePurityAuditTests.cs:20` | `s_inputValues`: `int[]` | Implemented; API adapters under validation |
| FIX-302 | `SharpProof.Worker.Test/TypedMetadataConversionTests.cs:12` | `NumericTypes`: `string[]` | Implemented; API adapters under validation |
| FIX-303 | `SharpProof.Worker.Test/VerificationTargetSchedulerTests.cs:11` | `Retirements`: `(WorkerCallableCoverageReason Callable, WorkerClaimReason Claim)[]` | Implemented; API adapters under validation |
| FIX-304 | `SharpProof.Worker.Test/WorkerNativeEffectEntryLockCoverageTests.cs:11` | `EntryLockContradictoryCore`: `string[]` | Implemented; API adapters under validation |
| FIX-305 | `SharpProof.Worker.Test/WorkerTests.cs:121` | `InvalidBudgetErrorCodes`: `string[]` | Implemented; API adapters under validation |
| FIX-306 | `SharpProof.Worker.Test/WorkerTests.cs:131` | `RequiredReferenceFileNames`: `string[]` | Implemented; API adapters under validation |

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
| MODEL-018 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:157` | `SafeRoot`: `int` | Open; coordinated migration |
| MODEL-019 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:162` | `ClaimId`: `string` | Open; coordinated migration |
| MODEL-020 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:163` | `AllowedKinds`: `IrExceptionKind[]` | Open; coordinated migration |
| MODEL-021 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:168` | `Entry`: `int` | Open; coordinated migration |
| MODEL-022 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:169` | `Current`: `int` | Open; coordinated migration |
| MODEL-023 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:170` | `Old`: `int` | Open; coordinated migration |
| MODEL-024 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:175` | `Kind`: `CompilerContractKind` | Open; coordinated migration |
| MODEL-025 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:176` | `ValueRoot`: `int` | Open; coordinated migration |
| MODEL-026 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:177` | `SafeRoot`: `int` | Open; coordinated migration |
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
| MODEL-037 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:203` | `TotalEntry`: `CompilerTotalCallableArtifact?` | Open; coordinated migration |
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
| MODEL-051 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:282` | `IsSource`: `bool` | Open; coordinated migration |
| MODEL-052 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:283` | `Location`: `WorkerSourceLocation` | Open; coordinated migration |
| MODEL-053 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:285` | `SourceTreeOrdinal`: `int` | Implemented; validation pending |
| MODEL-054 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:287` | `SourceTreePath`: `string` | Implemented; validation pending |
| MODEL-055 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:289` | `SourceTreeSha256`: `string` | Implemented; validation pending |
| MODEL-056 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:291` | `SourceLineMapSha256`: `string` | Implemented; validation pending |
| MODEL-057 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:296` | `Schema`: `string` | Open; coordinated migration |
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
| MODEL-071 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:310` | `ReachableSource`: `CompilerReachableSourceArtifact?` | Open; coordinated migration |
| MODEL-072 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:317` | `CollectionComplete`: `bool` | Open; coordinated migration |
| MODEL-073 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:318` | `Roots`: `CompilerSourceRootArtifact[]` | Open; coordinated migration |
| MODEL-074 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:319` | `Bodies`: `CompilerSourceBodyArtifact[]` | Open; coordinated migration |
| MODEL-075 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:320` | `Documents`: `CompilerSourceDocumentArtifact[]` | Open; coordinated migration |
| MODEL-076 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:325` | `SourceTreeOrdinal`: `int` | Open; coordinated migration |
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
| MODEL-087 | `SharpProof.CompilerArtifact/CompilerArtifactModel.generated.cs:344` | `SourceTreeOrdinal`: `int` | Open; coordinated migration |
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
| MODEL-098 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:20` | `ProjectDirectory`: `string` | Open; coordinated migration |
| MODEL-099 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:22` | `AssemblyName`: `string` | Open; coordinated migration |
| MODEL-100 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:24` | `AssemblyIdentity`: `string` | Open; coordinated migration |
| MODEL-101 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:26` | `TargetFramework`: `string` | Open; coordinated migration |
| MODEL-102 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:28` | `CompilerVersion`: `string` | Open; coordinated migration |
| MODEL-103 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:30` | `CompilerMvid`: `string` | Open; coordinated migration |
| MODEL-104 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:32` | `CSharpCompilerVersion`: `string` | Open; coordinated migration |
| MODEL-105 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:34` | `CSharpCompilerMvid`: `string` | Open; coordinated migration |
| MODEL-106 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:36` | `SpecificationPackIds`: `string[]` | Open; coordinated migration |
| MODEL-107 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:38` | `SpecificationPackCatalogVersion`: `int` | Open; coordinated migration |
| MODEL-108 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:40` | `SpecificationPackCatalogSha256`: `string` | Open; coordinated migration |
| MODEL-109 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:42` | `Options`: `CompilerCompilationOptionsSnapshot` | Open; coordinated migration |
| MODEL-110 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:44` | `SyntaxTrees`: `CompilerSyntaxTreeSnapshot[]` | Open; coordinated migration |
| MODEL-111 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:46` | `References`: `CompilerReferenceSnapshot[]` | Open; coordinated migration |
| MODEL-112 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:48` | `AdditionalFiles`: `CompilerAdditionalFileSnapshot[]` | Open; coordinated migration |
| MODEL-113 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:63` | `OutputKind`: `CompilerOutputKind` | Open; coordinated migration |
| MODEL-114 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:64` | `MainTypeName`: `string` | Open; coordinated migration |
| MODEL-115 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:65` | `OptimizationLevel`: `CompilerOptimizationLevel` | Open; coordinated migration |
| MODEL-116 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:66` | `Platform`: `CompilerPlatform` | Open; coordinated migration |
| MODEL-117 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:67` | `NullableContext`: `CompilerNullableContext` | Open; coordinated migration |
| MODEL-118 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:68` | `MetadataImportOptions`: `CompilerMetadataImportOptions` | Open; coordinated migration |
| MODEL-119 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:69` | `WarningLevel`: `int` | Open; coordinated migration |
| MODEL-120 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:70` | `GeneralDiagnosticOption`: `CompilerReportDiagnostic` | Open; coordinated migration |
| MODEL-121 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:71` | `SpecificDiagnosticOptions`: `CompilerDiagnosticOptionSnapshot[]` | Open; coordinated migration |
| MODEL-122 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:72` | `ReportSuppressedDiagnostics`: `bool` | Open; coordinated migration |
| MODEL-123 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:73` | `CheckOverflow`: `bool` | Open; coordinated migration |
| MODEL-124 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:74` | `AllowUnsafe`: `bool` | Open; coordinated migration |
| MODEL-125 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:75` | `Deterministic`: `bool` | Open; coordinated migration |
| MODEL-126 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:76` | `ReferencesSupersedeLowerVersions`: `bool` | Open; coordinated migration |
| MODEL-127 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:77` | `AssemblyIdentityComparer`: `CompilerAssemblyIdentityComparer` | Open; coordinated migration |
| MODEL-128 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:78` | `Usings`: `string[]` | Open; coordinated migration |
| MODEL-129 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:79` | `ResolverPolicy`: `CompilerResolverPolicy` | Open; coordinated migration |
| MODEL-130 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:84` | `Id`: `string` | Open; coordinated migration |
| MODEL-131 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:85` | `ReportDiagnostic`: `CompilerReportDiagnostic` | Open; coordinated migration |
| MODEL-132 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:90` | `SourceStart`: `int` | Open; coordinated migration |
| MODEL-133 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:91` | `SourceLength`: `int` | Open; coordinated migration |
| MODEL-134 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:92` | `MappedPath`: `string` | Open; coordinated migration |
| MODEL-135 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:93` | `MappedLine`: `int` | Open; coordinated migration |
| MODEL-136 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:94` | `MappedColumn`: `int` | Open; coordinated migration |
| MODEL-137 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:95` | `CharacterOffset`: `int` | Open; coordinated migration |
| MODEL-138 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:100` | `Path`: `string` | Open; coordinated migration |
| MODEL-139 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:101` | `Sha256`: `string` | Open; coordinated migration |
| MODEL-140 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:102` | `Encoding`: `string` | Open; coordinated migration |
| MODEL-141 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:103` | `ChecksumAlgorithm`: `string` | Open; coordinated migration |
| MODEL-142 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:104` | `RoslynChecksum`: `string` | Open; coordinated migration |
| MODEL-143 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:105` | `LineMapSha256`: `string` | Open; coordinated migration |
| MODEL-144 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:106` | `TextLength`: `int` | Open; coordinated migration |
| MODEL-145 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:107` | `LineMap`: `CompilerSourceLineMapEntry[]` | Open; coordinated migration |
| MODEL-146 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:108` | `LanguageVersion`: `string` | Open; coordinated migration |
| MODEL-147 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:109` | `DocumentationMode`: `string` | Open; coordinated migration |
| MODEL-148 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:110` | `Kind`: `string` | Open; coordinated migration |
| MODEL-149 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:111` | `PreprocessorSymbols`: `string[]` | Open; coordinated migration |
| MODEL-150 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:112` | `EffectivePreprocessorSymbols`: `string[]` | Open; coordinated migration |
| MODEL-151 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:113` | `Features`: `CompilerFeatureSnapshot[]` | Open; coordinated migration |
| MODEL-152 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:118` | `Kind`: `string` | Open; coordinated migration |
| MODEL-153 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:119` | `EmbedInteropTypes`: `bool` | Open; coordinated migration |
| MODEL-154 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:120` | `HasRecursiveAliases`: `bool` | Open; coordinated migration |
| MODEL-155 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:121` | `Aliases`: `string[]` | Open; coordinated migration |
| MODEL-156 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:122` | `Identity`: `string` | Open; coordinated migration |
| MODEL-157 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:123` | `Modules`: `CompilerReferenceModuleSnapshot[]` | Open; coordinated migration |
| MODEL-158 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:128` | `Name`: `string` | Open; coordinated migration |
| MODEL-159 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:129` | `Mvid`: `string` | Open; coordinated migration |
| MODEL-160 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:130` | `Path`: `string` | Open; coordinated migration |
| MODEL-161 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:131` | `Sha256`: `string` | Open; coordinated migration |
| MODEL-162 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:132` | `SizeBytes`: `long` | Open; coordinated migration |
| MODEL-163 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:137` | `Path`: `string` | Open; coordinated migration |
| MODEL-164 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:138` | `Sha256`: `string` | Open; coordinated migration |
| MODEL-165 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:143` | `Key`: `string` | Open; coordinated migration |
| MODEL-166 | `SharpProof.CompilerArtifact/CompilerCompilationModel.generated.cs:144` | `Value`: `string` | Open; coordinated migration |
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
| MODEL-187 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:66` | `DeclaringType`: `int` | Open; coordinated migration |
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
| MODEL-198 | `SharpProof.CompilerArtifact/PortableIrModel.generated.cs:91` | `Line`: `int` | Open; coordinated migration |
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
| MODEL-228 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:42` | `Cache`: `WorkerCacheOptions` | Open; coordinated migration |
| MODEL-229 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:44` | `VerifyPolicy`: `WorkerVerifyPolicy` | Open; coordinated migration |
| MODEL-230 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:46` | `AssumptionPolicy`: `WorkerAssumptionPolicy` | Open; coordinated migration |
| MODEL-231 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:52` | `Path`: `string` | Open; coordinated migration |
| MODEL-232 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:54` | `Sha256`: `string` | Open; coordinated migration |
| MODEL-233 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:90` | `QueryRlimit`: `uint` | Open; coordinated migration |
| MODEL-234 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:92` | `MethodRlimit`: `uint` | Open; coordinated migration |
| MODEL-235 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:94` | `MethodWallTimeMilliseconds`: `int` | Open; coordinated migration |
| MODEL-236 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:96` | `ProjectWallTimeMilliseconds`: `int` | Open; coordinated migration |
| MODEL-237 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:98` | `MaxParallelism`: `int` | Open; coordinated migration |
| MODEL-238 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:100` | `MaximumExpressionDepth`: `int` | Open; coordinated migration |
| MODEL-239 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:107` | `Enabled`: `bool` | Open; coordinated migration |
| MODEL-240 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:109` | `Directory`: `string?` | Open; coordinated migration |
| MODEL-241 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:111` | `MaximumBytes`: `long` | Open; coordinated migration |
| MODEL-242 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:158` | `Path`: `string` | Open; coordinated migration |
| MODEL-243 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:160` | `Start`: `int` | Open; coordinated migration |
| MODEL-244 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:162` | `Length`: `int` | Open; coordinated migration |
| MODEL-245 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:164` | `Line`: `int` | Open; coordinated migration |
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
| MODEL-281 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:414` | `Detail`: `string` | Open; coordinated migration |
| MODEL-282 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:416` | `Effects`: `WorkerEffectSet` | Open; coordinated migration |
| MODEL-283 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:418` | `Capabilities`: `WorkerEffectCapabilitySet` | Open; coordinated migration |
| MODEL-284 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:420` | `ExactExceptionTypeHierarchy`: `string[]` | Open; coordinated migration |
| MODEL-285 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:422` | `Location`: `WorkerSourceLocation` | Open; coordinated migration |
| MODEL-286 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:428` | `Variable`: `string` | Open; coordinated migration |
| MODEL-287 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:430` | `Kind`: `string` | Open; coordinated migration |
| MODEL-288 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:432` | `Value`: `string` | Open; coordinated migration |
| MODEL-289 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:449` | `ProtocolVersion`: `string` | Open; coordinated migration |
| MODEL-290 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:451` | `ManifestSchemaVersion`: `int` | Open; coordinated migration |
| MODEL-291 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:453` | `CacheSchemaVersion`: `int` | Open; coordinated migration |
| MODEL-292 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:455` | `WorkerVersion`: `string` | Open; coordinated migration |
| MODEL-293 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:457` | `ApiSpecVersion`: `string` | Open; coordinated migration |
| MODEL-294 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:459` | `WorkerBinarySha256`: `string` | Open; coordinated migration |
| MODEL-295 | `SharpProof.Worker.Protocol/ProtocolModel.generated.cs:461` | `ApiSpecContentSha256`: `string` | Open; coordinated migration |
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

- Before implementation: solution build passed, 0 warnings / 0 errors.
- Initial immutable fixed production tables: solution build passed, 0 warnings / 0 errors.
- Initial broad test snapshot: worker 5,326 passed; all completed semantic project
  suites passed. One package test failed because direct Compose omitted worktree
  Git metadata. Subsequent commands use the documented wrapper and read-only
  metadata mounts. This was not a full pass for the later source state.
- Expanded core/record checkpoint: solution build passed.
- Comprehensive 306-fixture conversion with adapters: solution build passed,
  0 warnings / 0 errors (37.80 seconds). No performance conclusion is inferred
  from builds run under different concurrent workloads.
- Canonical PowerShell probes passed immutable array/map conversion, ordering,
  case-insensitive policy lookup, string-array parameter binding and repeated
  module imports. Exported package bindings use ReadOnly, because Constant
  prevents Import-Module -Force from removing and re-importing them. The private
  parallelism-policy binding is Constant.
- Full semantic validation and final source formatting are in progress. The
  coordinator fixes compilation/adapter issues before accepting each batch.

### Stopping condition

The comprehensive effort is complete only when every candidate is classified,
all confirmed replaceable mutability is fixed, explicit mutable test subjects and
external mutation contracts are evidenced, and the final source state passes the
required gates. Unchecked candidates and coordinated model migration work remain
visible; this document does not mark them absent or complete.
