# SharpProof soundness-first acceptance contract

This directory defines the active acceptance contract for the 0.2 preview
migration. `eng/acceptance/v1` remains immutable historical evidence for the
0.1 preview and is not an implementation constraint for this breaking release.

The v2 contract is outcome-based. It intentionally does not freeze individual
test files, legacy public metadata, diagnostic severities, or the old package
layout. A migration tranche is acceptable only when:

- the supported-language gate is exhaustive and unsupported analyzer methods
  emit no feature diagnostics;
- every new `Proven` outcome is backed by hygienic evidence and every
  `Refuted` outcome has a replay-validated witness;
- proof-kernel evidence and outcome construction stays within the explicit
  trusted-kernel file set and its nonblank LOC budget;
- `Unknown`, timeout, cancellation, malformed, and infrastructure results are
  never cached as semantic answers;
- cache, worklist, formatting, renaming, and concurrency variants produce the
  same canonical outcomes;
- the snapshot includes 200-500 distinct methods from pinned, licensed OSS
  source (currently 200 methods across 87 files); synthetic transformations do
  not count toward that floor;
- all API specifications resolve for every applicable target framework and
  have executable witness tests;
- the analyzer payload has no dependency on SharpProof verification or Z3
  assemblies;
- the build, test, package, corpus, fuzz, architecture, cancellation, and
  performance gates in `contract.json` pass.

The initial supported product is the effect cluster plus call-site
preconditions. Complexity, regex proofs, metadata IL inference, public runtime
hazard queries, arbitrary mutable-heap postconditions, and unsupported language
constructs are outside the v2 contract.

Changes to the trusted-kernel paths, assumption construction, complete effect
summaries, API specifications, or proof-producing outcome construction require
two human reviewers and a soundness note identifying the executable regression
that covers the change. CI enforces the construction boundaries and LOC budget;
the two-approval rule must also be enabled in repository branch protection.

Run the active local gate from the repository root:

```powershell
.\eng\acceptance\v2\Verify.ps1
```

The verifier checks the pinned historical tree and contract invariants, builds
the production-size ratchet, builds the repository under the bounded Job
Object wrapper, and runs every v2 architecture, semantic, corpus, fuzz, worker,
package, cancellation, and performance gate.
