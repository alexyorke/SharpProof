# SharpProof hardening audit - 2026-07-25

> Dated evidence: this note records the state reviewed on 2026-07-25. The
> current package now defaults to the advisory/all profile and uses accountable
> worker protocol version 3. See [Coverage and limits](../coverage-and-limits.md)
> and [Typed abstention reasons](../unknown-reasons.md) for current behavior.

This note records the soundness-focused follow-up to commit
`d2cd1dc03ce64eb01556075985b9c3d998e988bd`. It does not claim that elapsed-time,
human-review, hardware-matrix, or explicitly deferred roadmap checkpoints have
been completed.

## Closed defects and enforcement gaps

- Package verification no longer uses MSBuild incremental outputs that could
  skip a repeated refuted build. The package tests run both repeated successful
  and repeated refuted builds.
- Default package policy is checked structurally: the analyzer is omitted in
  `off` mode, verifier execution has one exact opt-in target and condition, and
  no alternate target can invoke the verifier core. The integration test omits
  `SharpProofVerify` entirely and supplies missing worker paths, so a successful
  build proves the default target did not execute.
- Default-off timing now compares real baseline and SharpProof-imported MSBuild
  rebuilds. A separate loaded-but-off analyzer canary asserts that no analysis
  session or diagnostics are created. Retained-memory checks exercise the same
  loaded-but-off boundary.
- Cache writes require a `CacheableWorkerResponse` containing only validated,
  canonical terminal outcomes. Cache reads revalidate that wrapper; `Unknown`,
  malformed, mismatched, and infrastructure outcomes are recomputed.
- Construction of proof outcomes and validated models is mechanically confined
  to the proof kernel. Cancellation exceptions may cross only audited entry
  boundaries, including an exact caller-token guard in the worker.
- Worker body verification uses a bounded, acyclic CFG executor for locals,
  reassignment, branches, multiple returns, entry-state `Old`, and resolved pure
  API specs. One narrow exception admits only the exact normal-return non-null
  and zero-cardinality facets of a direct static nullary `Array.Empty<T>()` call
  whose row has no postconditions. Its effects remain `Unknown`; the executor
  consumes only the immediately adjacent memory-only empty-variable havoc with
  the same operation identity and preserves no heap or ambient-state claims.
  Other effect-unknown calls, including `Enumerable.Empty<T>`, abstain. See the
  [result-domain soundness note](./2026-07-25-api-spec-result-domains.md).
  Loops, other stateful operations, unknown calls, and exceeded bounds abstain.
- CFG call/spec bindings verify the compiler identity of the lowered member, not
  only static/instance shape and arity.
- Call-site `Requires` replay evaluates receiver and arguments in compiler
  order. It emits no refutation when a receiver, argument, or preceding
  statement can throw. Preceding source operations are admitted only through a
  recursive, compiler-bound non-throwing check.
- Runtime string concatenation carries a managed-allocation effect. The IR
  interpreter and frontend agree on null string concatenation and exact
  reference-to-string cast behavior, including invalid-cast replay.
- Framework metadata identities are centralized in the spec layer, and an
  architecture test rejects new `System.*` semantic literals in consumers.
- Corpus output now reports explicit, silent, and total semantic Unknown counts
  and rates.
- Generated domain tests cover lattice order, join, bottom, monotonicity,
  widening termination, and havoc. A 256-graph finite nullness oracle compares
  abstract fixpoints with exhaustive concrete reachability. These tests exposed
  an interval-addition overflow monotonicity defect; possible endpoint overflow
  now returns `Top`.
- The runtime oracle compares 192 generated `Requires` call sites with the
  compiled predicate and checks throwing argument and prefix cases. Effect
  oracles now include runtime string allocation.
- Algorithm files and members are protected by checked-in physical and Roslyn
  source-span caps. New lowering and interval behavior was refactored back under
  the existing caps rather than weakening them.

## Validation evidence

- `eng/acceptance/Verify.ps1 -Configuration Release` passed on the isolated
  intended commit tree: the solution built with zero warnings and errors; 406
  tests passed with one expected unsupported-host skip; all 1,000 fuzz cases
  agreed with zero abstentions.
- The 480-case corpus, cache replay, concurrent replay, cancellation, package,
  architecture, and performance gates passed. Default-off package rebuild
  ratios were 1.004 median and 1.029 p95 in the final exact-code run.
- `scripts/Generate-Readme.ps1 -Verify`, `git diff --check`, and the changed-file
  LF/no-BOM scan passed.

## Remaining independent roadmap checkpoints

The following are not represented as completed by this audit:

- Roslyn-analyzers entity, points-to, and flow-framework integration, plus
  general source-callee modular assume/guarantee verification. Interval,
  cardinality, and nullness now have bounded worker result projections, but
  not general CFG, heap, or points-to integration.
- A literal current-minus-10-percent size baseline across every replaced layer.
  Current hard caps prevent growth, but reaching that stricter checkpoint
  requires substantive decomposition rather than formatting compression.
- General call-host differential generation for side-effectful evaluation
  order. The fixed semantic-edge lane now covers nullable lifting,
  conversions, short-circuiting, and invocation forms, with evaluation order
  checked structurally.
- Real Visual Studio, Rider, and Windows arm64 host-matrix validation.
- Four consecutive weekly clean corpus cycles, two independent human soundness
  reviewers, and protected-branch enforcement.
- The Phase 8 second promoted feature and the roadmap items explicitly deferred
  by the plan: deep `Ensures`, arbitrary loops, a mutable heap model, and broad
  SMT-backed verification.
