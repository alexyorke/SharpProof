# Configuration ownership

Keep a setting with the component that defines its meaning. Reuse that owner
at call sites instead of copying values. Equal numbers with different units
or failure behavior remain separate settings. This refactor preserves all
effective defaults, limits, wire formats, and explicit caller overrides.

## Runtime owners

| Configuration | Owner | Consumers |
| --- | --- | --- |
| Analyzer keys, MSBuild property names, profile/features tokens and defaults | [SharpProofConfigurationCatalog](../src/SharpProof.Contracts/SharpProofConfigurationCatalog.cs) | Compiler-semantics option registry and parsing, analyzer policy adapter, package-default alias handling |
| Solver query resource default | [SolverDefaults](../eng/shared/SolverDefaults.cs) | Source-linked into SMT options and worker protocol; existing public constants remain available |
| Worker budgets, request expression-depth ceiling, cache defaults, launcher publication deadline | [ProtocolModel.generated.cs](../src/SharpProof.Worker.Protocol/ProtocolModel.generated.cs) | Request validation, launcher, compiler collector and artifact admission |
| Convenience-operation deadline | [AnalysisDefaults](../src/SharpProof.Ir/AnalysisDefaults.cs) | Analyzer, compiler capture, contract binding, frontend queries, IR interpretation; explicit caller-token overloads keep caller deadlines |
| Dataflow widening and iteration defaults | [ForwardDataflowAnalysisOptions](../src/SharpProof.Dataflow/ForwardDataflowAnalysis.cs) | General analysis and advisory analysis; advisory analysis retains its separate iteration budget |
| Advisory iteration, term-work and synthetic-edge defaults | [CoreIrAdvisoryInterpreter](../src/SharpProof.Analyzer.Core/Advisory/AdvisoryInterpreter.cs) | Both Run overloads |
| Optional tool SDK-discovery limits | [SdkProbe](../src/SharpProof.Tool/SdkProbe.cs) | Tool doctor discovery: 15-second timeout, 5-second process cleanup wait, and 4096 characters per stdout/stderr stream; separate from solver budgets and analysis deadlines |
| Host contract environment names | [HostEnvironmentVariables](../src/SharpProof.Host/HostEnvironmentVariables.cs) | Contract readers and trusted child environment allowlist |
| Atomic publication retry count | [AtomicFile](../src/SharpProof.Ir/AtomicFile.cs) | Staging-name allocation and publication retries |
| Fuzz case, seed and parallelism settings | [FuzzOptions](../src/SharpProof.Fuzz/FuzzOptions.cs) | CLI parsing/help, runner validation and summary admission |
| Gate child-process cleanup deadline | [GateProcess](../src/SharpProof.Gates/GateProcess.cs) | Captured processes and corpus Git importer |
| Fuzz source replay step budget | [FuzzExecutionDefaults](../src/SharpProof.Fuzz/FuzzExecutionDefaults.cs) | Metadata and total-program replay oracles |
| Corpus execution and evidence presentation | [CorpusExecutionDefaults](../src/SharpProof.Gates/Corpus/CorpusExecutionDefaults.cs) | Wall-time threshold/message, diagnostic caps, IL body bound and allocation observation loops |

These are implementation defaults, not new public configuration switches.
Consumer overrides remain documented in [consumer configuration](configuration.md).

## Build and automation owners

[toolchain.json](../eng/container/toolchain.json) owns the container toolchain
versions, image pins, native payload identities and platform. Dockerfile and
SDK-policy copies are intentional format-specific representations.

[The acceptance contract](../eng/acceptance/contract.json) owns automation CPU/wall
budgets, fuzz campaign limits, and independently frozen worker/cache expectations. [ContainerExecution](../eng/scripts/SharpProof.ContainerExecution.psm1)
interprets automation settings. Its named missing-contract timeout fallback is
retained for minimal changed-test fixtures.

[Verifier defaults](../src/SharpProof.Verifier/buildTransitive/SharpProof.Verifier.defaults.props)
are the conditional MSBuild representation of worker defaults. The
[MSBuild integration tests](../src/SharpProof.Package.IntegrationTests/WorkerMsBuildIntegrationTests.cs)
evaluate those defaults and compare them with runtime constants. Keep both the
independent acceptance assertions and these parity checks when changing policy.
No build-time code generator or runtime configuration-file dependency is added.

## Repository audit coverage

The initial inventory was the complete tracked tree at master
f10651eaeb7efb531263e7e5ae5158b07507411d: 2,275 files, including 1,846 C#,
PowerShell and shell source files. Ten parallel audits partitioned analyzer,
worker, host/package/protocol, SMT/dataflow, compiler/frontend, models/contracts,
gates/fuzz/tools, eng/scripts/engineering, root/build/CI, and
fixtures/src/samples/templates/documentation. Project configuration also received
a cross-cutting scan.

The review inventoried every tracked path, searched source/configuration files
for settings, environment reads, defaults, budgets, timeouts and inline
configuration, and traced candidate duplicates to consumers and existing tests.
Candidate findings were verified centrally before extraction. This was a
configuration audit, not a line-by-line soundness or dependency-version audit.
Generated API inventories, dependency lock contents and frozen oracle outputs
were inventoried but excluded from behavioral configuration review.

The following repetitions intentionally remain:

- Semantic widths, protocol/schema versions, graph/instruction bounds, recursion
  limits and witness limits retain their distinct domain owners. Matching
  numeric values alone do not establish a shared policy.
- Direct worker cache paths use the project obj directory; MSBuild cache paths
  include the framework-specific IntermediateOutputPath. Both modes have tests.
- Sample/template activation choices, tutorial scenarios, golden depth/scenario
  selectors, test deadlines and explicit expected values describe independent
  inputs or oracles.
- The OSS corpus source environment name is a C#/PowerShell process interface.
  The importer and its launcher retain the same explicit name.
- CI job CPU caps and action pins remain workflow deployment policy. Different
  artifact-action revisions are outside a behavior-preserving runtime refactor.
- Coverage, release, retained-seed, package-project and third-party catalogs
  remain separate domain configuration files.

Configuration boundaries are covered by analyzer alias/default regressions,
collector/artifact/protocol validation, launcher and package MSBuild integration,
dataflow/interpreter tests, runtime environment isolation, atomic publication,
and fuzz/gate admission tests. Run the canonical container PR gates after changes.
