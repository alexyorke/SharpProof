# Resource bounds: milestone one

Base: latest fetched `origin/master`, cd3363c63 (2026-10-18).
Branch: `codex/resource-bounds-milestone-one`.

## Goal and semantics

Add opt-in, built-in analyzer results for proven loop execution upper bounds and
cumulative managed array allocation bounds. Results must explicitly say Unknown
when the closed supported subset cannot justify a bound. This is static analysis,
not benchmarking. Bounds cover all execution prefixes, including exceptional
exits, under ordinary synchronous CLR execution; they are upper bounds, not exact
trip counts. Input sizes are entry values. Symbolic arithmetic uses mathematical
nonnegative integers, independently of CLR counter arithmetic.

The first milestone uses conservative compiler-bound structural proof rules in
the portable analyzer. It does not add Z3 queries or worker protocol claims. This
keeps native libraries out of Roslyn and avoids presenting new results as existing
worker verification evidence. No changes to existing contract verdicts.

Memory units are allocation events (arrays) and array elements, not bytes, peak
live heap, retained output, or stack space. Primitive one-dimensional arrays only;
unknown constructors, calls, boxing, and other implicit allocation paths must
abstain. Allocations are charged each time their site executes.

## Implementation plan and status

- [x] Fetch master and create an isolated branch without disturbing existing work.
- [x] Commit this plan before implementation.
- [ ] Add a bounded symbolic cost algebra and explicit Proven/Unknown result model.
- [ ] Prove canonical int counted loops (zero start, strict upper comparison,
  unit increment, stable nonnegative bound), including independent nesting.
- [ ] Compose sequential work and worst-case branches; count primitive arrays
  and their cumulative elements under loop multiplicities.
- [ ] Reject unsupported control flow, mutation, calls, recursion, numeric shapes,
  implicit allocations, and analysis budget exhaustion with explicit reasons.
- [ ] Expose opt-in analyzer diagnostics with machine-readable properties and
  document activation and the exact supported subset.
- [ ] Test supported results, adversarial Unknown cases, analyzer integration,
  cancellation, disabled behavior, and independently counted small executions.
- [ ] Run targeted tests, relevant broader checks, and review the final diff.
- [ ] Record evidence, limitations, remaining work, and commit implementation.

## Verification procedure

Use the canonical Linux amd64 tooling container. Run the resource-bound test
fixture first, then the analyzer project suite and the smallest relevant
architecture/acceptance checks. Record exact commands, counts, and failures here.
Do not broaden checks after a passing gate without a specific unresolved concern.

## Remaining after milestone one

- Non-unit strides, descending/logarithmic loops, dependent nested bounds, and
  ranking-function synthesis beyond the checked canonical rule.
- Source/metadata call cost summaries and recursion/recurrence solving.
- Object/closure/string/boxing allocation costs, runtime-dependent byte layouts,
  liveness, escape/alias analysis, peak live memory, and stack bounds.
- Resource contracts, native-worker proof records, artifact/protocol/cache
  integration, and SARIF evidence beyond analyzer diagnostic properties.
- Tight asymptotic classification, amortized/average-case analysis, async and
  concurrent execution, and wall-clock performance measurements.

## Evidence and checkpoints

- Initial checkout has unrelated changes; all task edits live in the isolated
  worktree `artifacts/resource-bounds-milestone-one`.
- No implementation or verification results yet.
