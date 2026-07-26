# SharpProof soundness-first acceptance contract

This directory defines the active acceptance contract for the 0.2 preview.

The current contract is outcome-based. It intentionally does not freeze individual
test files, public metadata, diagnostic severities, or package layout. A change
is acceptable only when:

- the supported-language gate is exhaustive and unsupported analyzer methods
  emit no feature diagnostics;
- every new `Proven` outcome is backed by hygienic evidence and every
  `Refuted` outcome has a replay-validated witness;
- proof-kernel evidence and outcome construction stays within the explicit
  trusted-kernel file set and its nonblank LOC budget;
- replaced frontend, dataflow, effects, and proof-kernel algorithm files stay
  within the physical-file and Roslyn member-size ratchets declared in
  `algorithm-size-ratchets.json`;
- `Unknown`, timeout, cancellation, malformed, and infrastructure results are
  never cached as semantic answers;
- cache, worklist, formatting, renaming, and concurrency variants produce the
  same canonical outcomes;
- the snapshot includes 200-500 distinct methods from pinned, licensed OSS
  source (currently 200 methods across 87 files); synthetic transformations do
  not count toward that floor;
- all API specifications resolve for every applicable target framework and
  every claim-bearing facet and postcondition has an executable runtime witness
  plus a deterministic mutation probe;
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

Changes to the trusted-kernel paths, assumption construction, complete effect
summaries, API specifications, or proof-producing outcome construction require
two human reviewers and a soundness note identifying the executable regression
that covers the change. CI enforces the construction boundaries and LOC budget;
the two-approval rule must also be enabled in repository branch protection.

Run the active local gate from the repository root:

```powershell
.\eng\acceptance\Verify.ps1
```

The verifier checks contract invariants and the production-size ceiling, builds
the repository under the bounded Job Object wrapper, and runs every current
architecture, semantic, corpus, fuzz, worker, package, cancellation, and
performance gate. Default-off performance compares real baseline and
SharpProof-imported MSBuild rebuilds and separately checks the loaded-but-off
analyzer retention boundary. The full acceptance job currently runs on Windows
x64. Separate package-consumer CI exercises the analyzer on Windows x64, Linux
x64, and macOS Intel, but packaged worker execution remains Windows x64 only.
