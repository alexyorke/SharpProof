# SharpProof soundness-first acceptance contract

This directory defines the active acceptance contract for the 1.0 preview.

The current contract is outcome-based. It intentionally does not freeze individual
test files, public metadata, diagnostic severities, or package layout. A change
is acceptable only when:

- the supported-language gate is exhaustive, unsupported unannotated analyzer
  methods remain quiet, and unsupported explicitly selected methods report
  SP0047;
- protocol version 9 binds compiler-manifest evidence and manifests every
  selected callable, postcondition, and selected effect-attribute occurrence
  with a stable semantic ID, every lowered callable exactly matches that manifest,
  and every response has exact manifest/result equality;
- every new `Proven` outcome is backed by hygienic evidence and every
  `Refuted` outcome has a replay-validated witness;
- proof construction and the discovery, lowering, execution, encoding, replay,
  policy, API-specification, and cache-validation TCB components stay within
  their exact, non-overlapping path inventories;
- replaced frontend, dataflow, effects, proof-kernel, execution, and replay
  algorithm files stay within the formatting-neutral Roslyn expression and
  decision-point ratchets declared in `algorithm-size-ratchets.json`;
- `Unknown`, timeout, cancellation, malformed, backend/replay, containment, and
  infrastructure results are never cached as semantic answers;
- cache, worklist, formatting, renaming, and concurrency variants produce the
  same canonical outcomes;
- every corpus case carries an explicit reviewed support label independent
  from its expected verdict and snapshot; `Supported` cases have zero
  tolerance for `Unknown`/`SilentUnknown`, while supported-case and supported
  OSS-method floors cannot decrease and total and per-reason Unknown counts for
  `IntentionallyUnsupported` cases cannot exceed the checked-in ratchet;
- the snapshot includes 200-500 distinct methods from pinned, licensed OSS
  source (currently 200 methods across 87 files); synthetic transformations do
  not count toward that floor;
- all API specifications resolve for every applicable target framework and
  every claim-bearing facet and postcondition has an executable runtime witness
  plus a deterministic mutation probe;
- deterministic trusted-boundary mutations, including independent
  postcondition replay and fail-closed effect-result assembly mutations, must
  all compile and be killed by their designated tests; nightly and release
  qualification retain the commit-bound evidence;
- the analyzer payload has no dependency on SharpProof verification or Z3
  assemblies;
- the build, test, package, corpus, fuzz, architecture, cancellation, and
  performance gates in `contract.json` pass.

The initial supported product is the effect cluster plus call-site
preconditions. Complexity, regex proofs, metadata IL inference, public runtime
hazard queries, arbitrary mutable-heap postconditions, and unsupported language
constructs are outside the current contract. `Ensures` verification is limited to
the bounded subset admitted by the worker's acyclic CFG executor; deep or
otherwise unsupported postconditions abstain.

The package defaults to `SharpProofProfile=advisory` and
`SharpProofFeatures=all`; unannotated code remains quiet.
The same feature selection enters the compiler artifact and filters manifest
discovery, so contract-only artifacts exclude effect annotations and
effect-only artifacts exclude postcondition claims.
`SharpProofProfile=strict` enables verification, defaults
`SharpProofVerifyPolicy` to `require-proven`, and defaults
`SharpProofAssumptionPolicy` to `error`. Fatal run states and refutations fail
under every policy. `SharpProofMode` is a deprecated preview compatibility
alias.

This acceptance contract covers compiler artifact schema version 8,
generated-tree accountability, portable whole-body lowered CFG/IR, exact
manifest/lowered-callable/result equality, compiler-diagnostic propagation, and
fail-closed option/provenance validation. The worker consumes that closed
artifact without constructing a Roslyn compilation or rereading reference
files. Compiler MVIDs and reference identities remain artifact provenance and
cache identity rather than runtime compatibility gates.
Exact backend-model closure and independent whole-body counterexample replay
are implemented for the admitted subset, and package validation covers the
exact Attributes -> portable analyzer -> Windows verifier dependency chain.
The release workflow validates and promotes the already-tested bytes with
hash, SBOM, repository, package-version, and tag checks. Optional SARIF 2.1.0
projects only validated worker responses. Owner-enforced
branch/tag/environment protection, pilot evidence, and independent human
release reviews remain open gates.

Changes to the trusted-kernel paths, assumption construction, complete effect
summaries, API specifications, or proof-producing outcome construction require
two human reviewers and a soundness note identifying the executable regression
that covers the change. CI enforces construction boundaries, exact TCB path
ownership, and structural-complexity budgets; the two-approval rule must also
be enabled in repository branch protection.

Run the active local gate from the repository root:

```powershell
.\scripts\Format-CSharp.ps1 -Verify
.\eng\acceptance\Verify.ps1
```

The verifier checks contract invariants, exact trusted-boundary path ownership,
and formatting-neutral production, coordinator, file, and member complexity
ratchets. Expression nodes, decision points, and declarations are release
gates; physical and nonblank lines are informational only. It then builds the
repository under the bounded Job Object wrapper and runs
every current architecture, semantic, corpus, fuzz, worker, package,
cancellation, and performance gate. Off-profile performance compares real
baseline and SharpProof-imported MSBuild rebuilds and separately checks the
loaded-but-off analyzer retention boundary. The full acceptance job currently
runs on Windows x64. Separate package-consumer CI restores the exact same three-package
artifacts and exercises the portable analyzer on Windows x64, Linux x64, macOS
x64, and macOS ARM64. Packaged worker execution remains Windows x64 only;
unsupported matrix hosts assert the explicit verification rejection.
