# Bug backlog

Bugs found by a read-only code review on 2026-09-16, many of which were then
reproduced on 2026-09-17 and 2026-09-18 with scratch harnesses built from an
unmodified copy of the sources; several later entries were found directly by
those harnesses (differential fuzzers and targeted probes). No production code was changed while compiling this list.
Every `Confirmed` P0 entry, and most P1 and P2 entries, were then re-run
against that same build after being written, and the few claims that did not
survive re-verification were corrected or narrowed in place. Every entry names
the file, describes the defect, gives a concrete failure scenario, and proposes
a fix. Confidence reflects how certain the reviewer is that the behavior is
reachable and wrong:

- **Confirmed:** reproduced. The analyzer and worker were built from an
  unmodified copy of the sources in a scratch directory. Analyzer findings
  were run through `CompilationWithAnalyzers` on the scenario source, with
  `SharpProofFeatures=effects` unless the entry says otherwise. Worker and SMT
  findings called the built worker types (for example `CallableVerifier` with
  the bundled Z3 through `IrSmtBackend`) from a scratch console harness.
  Each probe included a control that behaved as expected, so the difference
  is attributable to the reported defect.
- **High:** the defect follows directly from the code as written.
- **Medium:** the defect depends on a plausible runtime or library behavior
  that should be confirmed with a targeted test.
- **Low:** a hazard or latent defect worth a regression test, but it may be
  unreachable through current callers.

Priority definitions:

- **P0 - Critical:** Can make a false proof or refutation trusted, or break verification/publication integrity.
- **P1 - High:** Can produce a wrong supported-workflow result, hide a mandatory check, or broadly crash, hang, or lose authoritative results.
- **P2 - Medium:** Usually fails closed or causes false positives, incomplete diagnostics, bounded reliability problems, or narrower correctness errors.
- **P3 - Low:** Minor precision, canonicalization, test, documentation, or low-impact operational issue.

The list currently has 81 entries: 23 P0, 14 P1, 15 P2 and 29 P3. The final
section records the areas that were probed without finding a defect.

## P0 - Critical

The effect-analysis entries in this section also affect verification
builds, not only analyzer diagnostics. The compiler collector uses the same
effect summaries as effect-claim evidence. In an end-to-end probe (unmodified
collector analyzer, manifest, `SharpProofWorker` with Z3), three examples were
published as `Proven` effect claims with a `compiler-effect:<sha256>` proof
core: the nested-`finally` example, `[DoesNotThrow] int DecimalRound(decimal price) => (int)price;`,
and the struct field-initializer catch example. A control method that only
writes static state was `Unknown(EffectContractNotEstablished)`. Later
probes added more published false effect proofs (the `using`-last
fall-through shape, intercepted calls, captured primary-constructor
parameters, external exception constructors, interpolation of
`ISpanFormattable` values, non-exhaustive switch expressions in callees,
custom interpolated-string handler constructors, `foreach` enumerator
disposal inside a `using` block, `catch` handlers reached only by a
nullable unwrap, `using` inside `catch` or `finally`, checked enum and
nullable conversions, always-throwing `Deconstruct` methods, `++` or `--`
through user-defined implicit conversions, positional patterns matched
through `ITuple`, slice patterns on arrays, declaration patterns that box,
`ref` or `out` references to elements of covariant arrays, and exceptions
raised by the runtime under `[ZeroAllocations]`). Two
entries produce
false *postcondition* proofs instead (the symbolic join depth defect and
string literals reaching Z3), and the interceptor entry produces both
kinds.

### Effect CFG walk drops the outer `finally` whenever control leaves a nested try region

- **File:** `SharpProof.Effects/EffectMethodNodeBuilder.cs`
  (`AnalyzeControlFlowGraph`, local functions `AddReachableFinallyEntries`
  and `AddReachableFinallyEntriesForBlock`)
- **Confidence:** Confirmed. `[EnforcePure]` on the scenario method below
  produced no diagnostic, while the control
  `try { return; } finally { s_state++; }` reported SP0002, and so did
  `try { throw new InvalidOperationException(); } finally { s_state++; }`. Each of
  the following variants also produced no diagnostic, even though its outer
  `finally` performs `s_state++`:
  - `throw new InvalidOperationException()` inside the inner `try`;
  - `break` out of a nested `try` inside a `for` loop;
  - `goto` to a label after both regions;
  - `return` through an inner `try`/`catch`/`finally`;
  - three nested levels of `try`/`finally`;
  - the everyday shape `try { using (new Calm()) { return; } } finally { s_state++; }`,
    where the `using` supplies the inner `finally`.

  No jump is needed. Re-checking showed that ordinary fall-through loses the
  outer `finally` too, whenever the nested `try`/`finally` (or `using`) is the
  last statement of the outer `try`:
  `try { using (new Calm()) { a = a + 1; } } finally { s_state++; }` and
  `try { try { a = a + 1; } finally { a = a + 1; } } finally { s_state++; }`
  were both accepted as `[EnforcePure]`, although every call writes
  `s_state`. Through the collector and worker, the `using` form was published
  as a `Proven` effect claim, while the same method with one extra statement
  after the `using` was `Unknown(EffectContractNotEstablished)`. Adding any
  statement after the inner `try` inside the outer `try`
  (for example `a = a + 1;`) makes SP0002 appear, and so does a nested
  `try`/`catch` without a `finally`. The same loss occurs when the last
  statement of an inner `finally` is itself a `try`/`finally`. A throw that
  passes through a nested `try`/`finally` into an outer `catch`, and a
  `try`/`finally` nested inside a `catch` or followed by another statement
  inside a `finally`, were all analyzed correctly. Any non-empty inner
  `finally` triggers the loss (an assignment, a lone local declaration, a
  call, or the compiler-generated `finally` of a `using`); only an empty
  `finally { }` does
  not, because the compiler removes that region. Statements that follow the
  whole construct are analyzed normally, so the loss is confined to the outer
  `finally` body.

  An independent differential fuzz campaign rediscovered this defect without
  being aimed at it: once the generator could emit `goto` out of a nested
  `try`/`finally`, 500 random batches produced 10 methods that the analyzer
  accepted and that then misbehaved when executed - 6 `[DoesNotThrow]` methods
  that threw `NullReferenceException` from an outer `finally`, and 4
  `[EnforcePure]` methods whose outer `finally` wrote a static field or mutated
  an argument object. Every accepted-but-violating method found in the whole
  campaign traced back to this one defect. A separate `[ZeroAllocations]`
  campaign, which measured managed allocation of every accepted method, found
  3 methods that allocated 64 bytes per call; all three had a string
  concatenation in an outer `finally` that followed a nested `try`/`finally`
  reached by fall-through or by `return`.

  The loss is not limited to writes. With the same nested shape,
  `[ZeroAllocations]` was proven for an outer `finally { _ = new Thing(); }`,
  and `[DoesNotThrow]` was proven for an outer `finally { Throws(); }` that
  calls a method that always throws. Only a literal `throw` statement or a
  `lock` in that outer `finally` was still reported (SP0046 / SP0016),
  because `ScanLexicalControlEffects` finds those operations syntactically.
- **What is wrong:** Finally-region entry blocks have no regular predecessors
  and are explicitly excluded from `AddExceptionalEntries`
  (`IsExceptionalEntryReachable` returns `false` inside a `Finally` region),
  so the only way a `finally` body is scanned is through these two helpers.
  Both walk the leaving/enclosing regions from innermost to outermost and
  `return` right after adding the first `finally` they find. A Roslyn branch
  that exits several try regions at once lists every region in
  `LeavingRegions`/`FinallyRegions`, but only the innermost `finally` is added.
  Such a branch arises from a `return`, `break`, `goto` or `throw` inside a
  nested `try`, and also from plain fall-through when the nested
  `try`/`finally` is the last statement of the outer `try`, because Roslyn
  then emits a single branch that leaves both regions.
  The exit edge of that inner `finally` has
  `ControlFlowBranchSemantics.StructuredExceptionHandling` (no destination),
  which `AddRegularSuccessor` ignores. The outer `finally` is reached only by
  accident, when the inner `finally` step happens to have a non-empty `Throws`
  set. Source-method calls contribute no throws at node-build time (their
  effects are joined later in `ComputeSummaries`), so that accident usually
  does not happen.
- **Failure scenario:**
  `static void M() { try { try { return; } finally { Helper(); } } finally { s_state++; } }`
  where `Helper` is a source method. The `return` block schedules only the
  inner `finally`; `Helper()` contributes `DirectCall` with no throws, so the
  outer `finally` block is never visited. The static write `s_state++` (and a
  call site for anything the outer `finally` invokes) is missing from `M`'s
  effect summary, so `[EnforcePure]`, `[ZeroAllocations]`, `[DoesNotThrow]` or
  capability checks on `M` can be accepted although the method mutates static
  state. The same happens for `throw` inside a nested `try` without a
  matching catch, and, most commonly, for
  `try { using (var r = Open()) { Use(r); } } finally { Log(); }` completing
  normally, where `Log()`'s effects never reach the summary. A regression test
  needs a non-empty inner `finally`: an empty `finally { }` is removed by the
  compiler and does not reproduce the defect. The existing tests `NestedFinallyAfterDivergence` and
  `AfterNestedNonreturningFinally` expect "no write" and would pass with this
  defect, so they do not guard it.
- **Suggested fix:** Remove the early `return` in both helpers and add the
  entry of every `finally` region the transfer passes through (for control
  transfers, iterate `branch.FinallyRegions` and map each region to its first
  block; for exceptional exits, walk all enclosing try regions). The codebase
  already does this correctly elsewhere: `RequiresCallSiteTreeAnalyzer.RegularSuccessors`
  yields the first block of every region in `branch.FinallyRegions`, and
  `LeavingFinallysMayComplete` in the same file as the defect walks every
  leaving region without stopping. Adding more
  `finally` entries is always safe for a may-effect analysis. Add regression
  tests for `return`, `break`, `goto` and `throw` leaving two and three nested
  try/finally regions whose outermost `finally` writes a static field, and for
  plain fall-through where the inner `try`/`finally` or `using` is the last
  statement of the outer `try`.

### Catch reachability ignores constructor member initializers and implicit copy constructors

- **File:** `SharpProof.Effects/ExceptionHandlerReachability.cs`
  (`GetCallableExceptions`, `GetImplicitConstructorExceptions`, and the
  `IObjectCreationOperation` / `IWithOperation` branches of
  `GetPotentialExceptions`)
- **Confidence:** Confirmed for the struct field-initializer scenario:
  `[EnforcePure]` on `M` produced no diagnostic, while a control whose `try`
  contains `if (b) throw new InvalidOperationException();` reported SP0002.
  Also confirmed for a sealed class with a throwing instance field
  initializer and an explicit empty constructor, and for
  `new ChainedInitializer(1)`, where that constructor delegates with
  `: this()` to the initializer-running constructor. Both produced no
  diagnostic. A derived class whose *base* class has the throwing initializer
  did report SP0002, because base constructors are followed. Re-verified: the
  same sealed class *without* an explicit constructor (only the implicit
  parameterless one) also reports SP0002, so a regression test must declare
  the empty constructor explicitly.
  The record `with` variant could not be reproduced. A selected method using
  `with` is rejected by the subset gate (SP0047, `UnsupportedOperationKind
  (With)`), and a callee using `with` already makes the caller's summary
  unknown. That part stays a latent defect.
- **What is wrong:** Whether a `catch` handler is reachable is decided from
  the exceptions a protected block may throw. For a source constructor the
  callee exceptions come from `model.GetOperation(declaration)`, which is the
  `IConstructorBodyOperation` (the `: base(...)`/`: this(...)` initializer plus
  the body). Roslyn does not put instance field or property initializers in
  that tree; they are separate `IFieldInitializerOperation`s. For an implicitly
  declared constructor, `GetImplicitConstructorExceptions` only follows the
  base constructor, and any other implicitly declared constructor (for example
  a record's synthesized copy constructor, which calls the base record's
  possibly user-written copy constructor) returns `EmptyPotential`. The effect
  side does scan member initializers (`EffectMethodNodeBuilder.ScanMemberInitializers`),
  so the two halves disagree.
- **Failure scenario:**
  `struct S { private int _x = Fail(); public S() { } static int Fail() => throw new InvalidOperationException(); }`
  and `static void M() { try { _ = new S(); } catch (InvalidOperationException) { s_state++; } }`.
  The constructor body is empty, so no potential exception is found and the
  handler is classified unreachable. `OperationEffectScanner.IsReachable`
  filters out every operation in the handler, so the static write is not in
  `M`'s summary, and the exception from the constructor call is removed by
  `EffectExceptionFlow.KeepEscaping` because the enclosing catch matches.
  `M` then looks like it neither throws nor writes static state, so a purity or
  write-free effect contract on `M` is accepted incorrectly. In principle the
  same gap applies to
  `record Base { protected Base(Base o) { throw ...; } } record Derived : Base;`
  and `d with { }` inside a try whose catch mutates state, although `with`
  is currently not analyzable (see Confidence).
- **Suggested fix:** When the callee is a constructor that does not delegate
  with `: this(...)`, union the potential exceptions of
  `EffectMethodNodeBuilder.GetMemberInitializerOperations(..., staticInitializers: false, ...)`
  (returning `UnknownPotential` for a null operation). Return
  `UnknownPotential` (or follow the base copy constructor) for implicitly
  declared constructors other than the parameterless one, instead of
  `EmptyPotential`. Add tests for struct and class field initializers,
  property initializers and derived-record `with` expressions.

### Unmodeled external exception constructors are silently dropped inside `throw`

- **File:** `SharpProof.Effects/OperationEffectScanner.cs`
  (`ScanObjectCreation`, `ScanLexicalControlEffects`, `ScanThrow`),
  `SharpProof.Effects/ExceptionHandlerReachability.cs` (object-creation branch
  of `GetPotentialExceptions`)
- **Confidence:** Confirmed. The scenario method produced no diagnostic,
  while `[AllowedExceptions(typeof(AggregateException))] static void Control() => throw new ArgumentNullException("x");`
  reported SP0046.
- **What is wrong:** `ScanObjectCreation` sets `suppressExternalConstruction`
  when the creation is an external exception constructor without a
  non-throwing API spec *and any syntax ancestor* is a throw statement or
  expression. `ScanObjectConstruction` then uses `EffectSummary.Empty` instead
  of `ResolveConstruction`, and `ScanLexicalControlEffects` adds only
  `ExceptionConstructionThrow(EffectSummary.Empty, thrownType)`, which keeps
  `Completeness.Complete`, no unknown throws, and no unknown reads, writes or
  capabilities (only `Termination` becomes unknown, and no contract facet reads
  termination). Outside a throw, the same constructor correctly yields an
  `UnknownBoundary` (`ExceptionConstructorsRequireExactSpecsAndGateThrowWitnesses`
  asserts that). `docs/coverage-and-limits.md` states that exception
  constructors are not implicitly trusted and that
  `AggregateException(IEnumerable<Exception>)` produces an incomplete result.
  The reachability side has the same gap: a metadata exception constructor
  without an API spec contributes `EmptyPotential`.
- **Failure scenario:**
  `[AllowedExceptions(typeof(AggregateException))] static void M() => throw new AggregateException((IEnumerable<Exception>)null!);`
  The summary is `Throws = {AggregateException}`, complete, so the claim is
  `Proven`, but at runtime the constructor throws `ArgumentNullException`. The
  same holds for a third-party exception type whose constructor writes a
  static field: with `VendorException` compiled into a *referenced assembly*
  (its constructor doing `Constructed++`),
  `[EnforcePure] static void Fail() => throw new VendorException("x");` was
  published as a `Proven` effect claim by the collector and worker, although
  every call writes static state. The type must come from metadata: the same
  exception declared in the analyzed compilation is summarized normally and
  reports SP0002. The related shape
  `throw new InvalidOperationException(new VendorException("x").Message)`,
  where the creation is not the thrown operand, was re-checked and did *not*
  reproduce (the claim was `Unknown(EffectSummaryIncomplete)`), so only the
  directly thrown construction is confirmed.
- **Suggested fix:** Never replace an unmodeled constructor with
  `EffectSummary.Empty`. Keep the `ResolveConstruction` result (an
  `UnknownBoundary`) joined with the thrown type, and only suppress the direct
  throw *witness*. Keep any remaining special case restricted to
  `ReferenceEquals(UnwrapHarmlessValue(throw.Exception), creation)`, which is
  the only shape that reproduces. In
  `ExceptionHandlerReachability`, return `UnknownPotential` for metadata
  exception constructors without a spec. Add an analyzer test that the
  `[AllowedExceptions(typeof(AggregateException))]` example above is
  `Unknown`.

### Source-order null proof ignores loop back edges and deconstruction assignments

- **File:** `SharpProof.Effects/OperationNullnessEvaluator.cs`
  (`IsSourceDefinitelyNull`, used by `IsProvenNull`, `GetNullProofs`,
  `GetNullState` and `GetNullStatePreferNull`)
- **Confidence:** Confirmed. `[EnforcePure]` on the scenario method produced
  no diagnostic, while the same loop with `s` initialized to `"y"` reported
  SP0002. The backward-`goto` form (`Again: if (i == 1) { _ = s!.Length; s_state++; } s = "x"; i++; if (i < 2) goto Again;`)
  and a `do`/`while` loop in a non-selected callee also produced no diagnostic.
  A second failure mode was found and confirmed while probing: in a
  non-selected callee
  `string? s = null; (s, var t) = ("x", 1); _ = s.Length; s_state++;`,
  a selected `[EnforcePure]` caller produced no diagnostic. The same callee
  reported SP0002 when `s` started as `"y"`, when the `_ = s.Length;` line was
  removed, and when both tuple elements were existing locals (then the
  abstract flow answered first).
- **What is wrong:** A local initialized with `null` is reported as
  definitely null at `origin` when no assignment, by-reference argument, ref
  alias or local-function call appears *between the declaration and the
  origin in source span order*. That is a flow-insensitive text check. In a
  `for`/`while` loop (admitted by the language subset gate), or after a
  backward `goto`, an assignment that appears later in the source reaches the
  origin through the back edge. The "proven null" result makes
  `OperationCompletionEvaluator.CanCompleteInvocation`, `CanCompleteProperty`,
  `CanCompleteField`, `CanCompleteArrayElement` and
  `OperationEffectScanner.PotentialNullCheck` report that the access cannot
  complete. `AnalyzeControlFlowGraph` then stops scanning the rest of the
  block and does not add its successors. The managed abstract flow, which
  would give the correct answer, is unavailable for exactly these methods
  because it returns `Cyclic` for any loop. The same check also recognizes a
  write only when an `IAssignmentOperation` target *is* the local. A
  deconstruction assignment (`IDeconstructionAssignmentOperation`) targets an
  `ITupleOperation` that merely contains the local, so `(s, var t) = (...)`
  is not seen as an assignment. When the abstract flow does not produce a
  state (as happened for a deconstruction that also declares a variable), the
  local is still reported as definitely null.
- **Failure scenario:**
  `[EnforcePure] static void M() { string? s = null; for (var i = 0; i < 2; i++) { if (i == 1) { _ = s.Length; s_state++; } s = "x"; } }`
  On the second iteration `s` is `"x"` and `s_state++` runs. The analyzer
  treats `s.Length` as a guaranteed `NullReferenceException`, never scans
  `s_state++`, and the summary contains no static write, so `[EnforcePure]` is
  proven. Likewise, a callee containing
  `string? s = null; (s, var t) = ("x", 1); _ = s.Length; s_state++;` is
  summarized without the static write, so `[EnforcePure]` on a caller is
  proven although `s` is `"x"` and the write always runs.
- **Suggested fix:** Only use `IsSourceDefinitelyNull` when the origin is not
  inside any loop body, condition or incrementor that also contains the
  declaration's scope, and when the method contains no backward `goto` or
  labeled statement before the origin. Better, derive definite nullness from
  the Roslyn CFG (a reaching-definitions check) instead of span order. At a
  minimum, treat any `IDeconstructionAssignmentOperation` whose target tree
  contains a reference to the local (or an alias) as an assignment.

### Symbolic join drops a reachable block when its merged terms exceed the depth limit

- **File:** `SharpProof.Worker/AcyclicBlockPredicateExecutor.cs` (`Run.Merge`,
  `Run.Execute`)
- **Confidence:** Confirmed end-to-end from C# source with the real Z3
  backend. In a scratch harness, the unmodified collector analyzer
  (`FinalCompilationCollectorAnalyzer`) produced a compiler manifest for the
  C# method in the failure scenario. `SharpProofWorker` (with an
  `IrSmtBackend`, cache disabled, default budgets including
  `MaximumExpressionDepth = 64`) then reported the `result == 0`
  postcondition as `Proven`, with run status `Complete`, for both the
  59-operand and the 60-operand version of the condition. The same method
  shape with 50 to 55 operands was correctly `Refuted` (counterexample
  `v = -1`). Conditions of 56 to 58 operands abstained, and 61 or more
  failed as `UnsupportedBody` because the branch itself was too deep. A
  direct IR-level harness showed the same pattern: `if (v == 0) return 0; if (a) { if (b) { } } return -1;`
  is `Proven` at depth 6 and 7 and `Refuted` at 64. A `Proven` result with an
  empty proof core passes `ClaimResultRules`, so nothing downstream rejects
  it.
- **What is wrong:** `Merge` returns `null` in two ways when a join is too
  deep: when the disjunction of the incoming predicates fails `Supported`, and
  when a merged phi value (the `Conditional` chain) fails `Supported`. Neither
  path sets `_reason`. `Execute` reads a `null` state with
  `_reason == WorkerClaimReason.None` as "no incoming edge" (an unreachable
  block) and simply `continue`s. The join block is never executed, so none of
  its successors receive incoming states, and every return reachable only
  through that join disappears from `_returns`. `CallableEvidenceBuilder` then
  adds `body:normal-completion`, the disjunction of the *remaining* return
  predicates, as an assumption, so the solver only considers executions
  that end at the surviving returns. Postconditions are checked only
  on those paths. The normal-completion probe only flags vacuity when no
  return survives, so the result is an ordinary `Proven`. Branch transfer
  (`TransferBranch`) already fails the whole body when a branch predicate
  is too deep. Only the join step drops paths without reporting a failure.
- **Failure scenario:** A join with three incoming edges, such as the end of
  `if (cond) { if (y) { ... } }`, an `else if` chain, a `switch`, or a
  lowered `&&`/`||`, combines predicates of different depths. Its balanced
  disjunction can be two levels deeper than the deepest incoming edge, while
  each branch that produced those edges was still within the limit. The
  confirmed C# example is:

  ```csharp
  public static long Deep59(long v, bool c0, /* ... */ bool c58, bool y)
  {
      Contract.Ensures(Contract.Result<long>() == 0);
      if (v == 0) { return 0; }
      long n = 0;
      if (c0 == (c1 == (c2 == /* ... */ (c57 == (c58))))) { if (y) { n = 3; } }
      return -1;
  }
  ```

  The join after the nested `if` is skipped, only `return 0` remains, the
  normal-completion assumption becomes `v == 0`, and the obligation
  `v == 0 ⇒ 0 == 0` holds, so the worker publishes `Proven` although the
  method returns `-1` for every nonzero `v`. The deep condition is only a
  compact way to reach the limit within the collector's 64-block body cap.
  (Sequential `if`/`else` chains long enough to reach depth 64 exceeded that
  cap and abstained.) Other shapes that build deep branch predicates or phi
  values within the cap, such as long boolean conditions, nested ternaries or
  arithmetic in conditions, are exposed the same way. With a lower
  configured `SharpProofVerifyMaximumExpressionDepth`, much shallower code is
  affected.
- **Suggested fix:** Set `_reason` (for example to
  `WorkerClaimReason.ResourceLimit` or `UnsupportedBody`) before returning
  `null` for either depth failure in `Merge`, or make `Merge` return a
  tri-state result (`Unreachable`, `State`, `Failed`) so that only a genuinely
  empty incoming list is treated as unreachable. Add a regression test that
  builds a join whose incoming predicates are exactly at the limit, next to an
  early return, and asserts the body fails instead of yielding a single return.

### `decimal` arithmetic and conversions are modeled as non-throwing

- **File:** `SharpProof.Effects/ConversionEffectClassifier.cs` (`Classify`,
  `CheckedOverflow`), `SharpProof.Effects/OperationEffectScanner.cs`
  (`IntegralDivisionExceptions`, `TryGetIntegralDivisionSemantics`),
  `SharpProof.Effects/OperationEffectScanner.Expressions.cs` (`ScanBinary`),
  and `eng/RoslynCfgThrowFacts.cs` (`BuiltInOperationMayThrow`, used by
  `ExceptionHandlerReachability.CanThrowUnknown`)
- **Confidence:** Confirmed. A scratch harness ran the current analyzer
  (built from a copy of the sources) with `SharpProofFeatures=effects`. The
  control `[DoesNotThrow] int ControlDivide(int l, int r) => l / r;` reported
  SP0046, and a control static write reported SP0002. `Round`, `Total`, a
  `(decimal)double` conversion, and the `try`/`catch (OverflowException)`
  example below each reported no diagnostic.
  A separate Roslyn probe showed that `(int)d`, `(decimal)x` for a `double`,
  and `a + b` and `a / b` over `decimal` are built-in operations with
  `IsChecked == false` and `OperatorMethod == null`.
- **What is wrong:** C# `decimal` operations throw regardless of the
  `checked`/`unchecked` context. `+`, `-`, `*`, `++` and `--` throw
  `OverflowException`. `/` and `%` throw `DivideByZeroException` (and `/` can
  also overflow). Explicit conversions from `decimal` to an integral type throw
  `OverflowException` when the value is out of range. Conversions from `float`
  or `double` to `decimal` throw `OverflowException` for NaN, infinity or
  out-of-range values. The effect scanner only adds `OverflowException` when
  `IsChecked` is true (`CheckedOverflow`), and `IntegralDivisionExceptions`
  only recognizes integer and native-integer types, so all of these
  operations get an empty summary. `LanguageSubsetGate.SupportsOperationShape`
  admits them because `OperatorMethod` is `null`. On the catch-reachability
  side, `BuiltInOperationMayThrow` only treats checked arithmetic, checked
  conversions and division/remainder as throwing, so a
  `catch (OverflowException)` around `decimal` addition or conversion is
  classified unreachable. That handler's effects are then dropped as well (the
  same mechanism as the constructor-initializer entry above). The same gap
  exists for an explicit nullable unwrap `(int)nullable`: the effect side
  models `InvalidOperationException`, but `BuiltInOperationMayThrow` does not,
  so `catch (InvalidOperationException)` around it is also considered
  unreachable.
- **Failure scenario:**
  `[DoesNotThrow] static int Round(decimal price) => (int)price;` or
  `[DoesNotThrow] static decimal Total(decimal a, decimal b) => a + b;` is
  proven, but `Round(1e20m)` and `Total(decimal.MaxValue, 1m)` throw
  `OverflowException`. Likewise,
  `[EnforcePure] static int M(decimal d) { try { return (int)d; } catch (OverflowException) { s_failures++; return 0; } }`
  is accepted as pure because the handler that writes static state is
  considered unreachable.
- **Suggested fix:** Treat `decimal` (and nullable `decimal`) as always-checked
  in `CheckedOverflow` for `Add`, `Subtract`, `Multiply`, `Divide`, increment
  and decrement. Add `DivideByZeroException` for `decimal` `Divide` and
  `Remainder`. Add `OverflowException` for explicit numeric conversions whose
  source or target is `decimal` (except integral to `decimal` and `decimal` to
  `float`/`double`). Mirror every one of these, plus explicit nullable-unwrap
  conversions, in `RoslynCfgThrowFacts.BuiltInOperationMayThrow`. Add
  regression tests for each operator and conversion under both a
  `DoesNotThrow` contract and a matching `catch`.

### String literals reach Z3 through a lossy, escape-parsing constructor

- **File:** `SharpProof.Smt/IrSmtBackend.cs` (`QueryEncoder.EncodeString`,
  and the `Equal`/`NotEqual` and `EncodeConditional` paths that compare the
  encoded values)
- **Confidence:** Confirmed on Windows (ANSI code page 1252) with the bundled
  Z3 4.12.2. The unmodified collector analyzers produced a compiler manifest
  from the source below, and the built worker checked it through
  `IrSmtBackend`. `Language`, `BestFit`, `Escape` and `Branch` were
  published as `Proven`, but each postcondition is false when the parameter
  is `true`. In the same probe manifests, `flag ? "B" : "A"` (the same shape with ASCII
  literals), `flag ? @"\x41" : "A"` and `flag ? "\U000000E9b" : "eb"` were
  correctly `Refuted` with the model `parameter:0=true`. A direct Z3 probe
  (`solver.Assert(!(MkString(a) == MkString(b)))`) returned `UNSATISFIABLE`,
  meaning Z3 treats the strings as equal, for `@"\u{41}"` and `@"\u0041"` versus `"A"`,
  `"\U0001F600"` versus `"??"`, `"\U00004E2D"` versus `"?"`, `"\U00000100"`
  versus `"A"`, and a lone `'\uD800'` versus `"?"` (the frontend already
  rejects ill-formed UTF-16 literals, so that last pair is not reachable from C#). `MkString("\U00004E2D\U00006587")`
  printed as `"??"`.
- **What is wrong:** `EncodeString` passes the managed string to
  `Context.MkString`, and the only guard rejects embedded NUL characters. The
  4.12.2 binding forwards that to `Z3_mk_string` as a narrow C string. On
  Windows the default P/Invoke string marshaling converts UTF-16 to the ANSI
  code page with best-fit mapping, so characters outside that code page
  become `?` or a similar-looking ASCII letter, and surrogate pairs become
  `??`. `Z3_mk_string` then parses SMT-LIB escape sequences such as
  `\u{41}`, so a C# literal that contains a backslash sequence can be
  rewritten to a different character. Z3 therefore decides equality over a
  different string than C# compares. Literal-to-literal comparisons are
  folded in `IrTermServices` before they reach the solver, which is why
  `"B" == "A"` is right. But when a string literal flows through a
  conditional, a local, or a join, the `Equal` or `MkITE` over the encoded
  literals is decided by Z3 and can equate distinct strings. On non-Windows
  hosts the marshaling is UTF-8 and Z3 reads the bytes differently again, so
  the same manifest can verify differently on different machines or code
  pages. Only the Windows behavior was reproduced.
- **Failure scenario:**

  ```csharp
  public static bool Language(bool chinese)
  {
      Contract.Ensures(Contract.Result<bool>());
      string name = chinese ? "\U00004E2D\U00006587" : "\U000065E5\U0000672C";
      return name == "\U000065E5\U0000672C";
  }

  public static bool BestFit(bool flag)
  {
      Contract.Ensures(Contract.Result<bool>());
      string name = flag ? "\U00000100b" : "Ab";
      return name == "Ab";
  }

  public static bool Escape(bool flag)
  {
      Contract.Ensures(Contract.Result<bool>());
      string text = flag ? @"\u{41}" : "A";
      return text == "A";
  }

  public static int Branch(bool flag)
  {
      Contract.Ensures(Contract.Result<int>() == 1);
      string text = flag ? @"\u{42}\u{43}" : "BC";
      if (text == "BC")
      {
          return 1;
      }

      return 2;
  }
  ```

  All four are `Proven`. `Language(true)` compares two different two-character
  CJK strings, `BestFit(true)` compares "Ab" with a macron A to plain "Ab",
  and `Escape(true)` and `Branch(true)` compare six- or twelve-character
  backslash strings to one or two letters. Every one of them returns `false`
  or `2` at run time. The opposite direction is caught by counterexample
  replay but still fails closed for the whole run. For
  `Contract.Ensures(!Contract.Result<bool>()); string text = flag ? @"\u{41}" : "B"; return text == "A";`,
  which always holds, Z3 produced the model `flag = true`, the interpreter
  could not replay it, and the worker reported
  `Unknown(CounterexampleReplayFailed)` for that claim and
  `run=Failed failure=CounterexampleReplayFailed` for the whole manifest.
  The ASCII control (`flag ? "C" : "B"`) was `Proven` in the same run.
- **Suggested fix:** Do not use `Z3_mk_string` for program strings. The
  backend only needs equality and `ite` over string literals, so the simplest
  sound encoding interns each distinct ordinal string value in the query to a
  distinct integer (or a constant of an uninterpreted sort with a `distinct`
  axiom). If sequence semantics are needed later, build the literal from
  UTF-16 code units with `Z3_mk_u32string` (or `MkUnit`/`MkConcat` over
  explicit characters) so no marshaling or escape parsing happens. Until then,
  reject any string that is not printable ASCII without a backslash as
  `UnsupportedIrEncodingException`. Add backend tests that assert `Refuted`
  for `ite(b, s1, s2) == s2` with `s1` set to `@"\u{41}"`, `"\U00000100"`,
  a surrogate pair, a lone surrogate and a CJK string, each paired with its
  Z3 look-alike.

### Intercepted call sites are analyzed as calls to the original method

- **File:** every consumer of `IInvocationOperation.TargetMethod`, in particular
  `SharpProof.Frontend/RoslynProgramLowerer.cs` (`LowerInvocation`),
  `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`
  (source summaries), the effect scanner in `SharpProof.Effects`
  (`OperationEffectScanner`, `ManagedAbstractFlow`), and the compilation
  admission checks in `SharpProof.CompilerCollector/FinalCompilationCollector.cs`
  and `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`
- **Confidence:** Confirmed end to end. A scratch harness compiled two syntax
  trees with the .NET 9 reference pack and the parse-options feature
  `InterceptorsNamespaces=Interception`, which is what
  `<InterceptorsNamespaces>` sets in an SDK build. The first tree is shown
  below. The second tree declares
  `[InterceptsLocation(version, data)] public static long NotTheHelper(long p)`
  for each `Helper.Value(p)` call, using the values from
  `SemanticModel.GetInterceptableLocation`. Emitting and running the
  assembly showed the interception is real: `Caller(5)` returned `0`, and
  `NoThrow` and `Pure` threw `InvalidOperationException` after incrementing a
  static counter. The unmodified collector analyzers reported no diagnostics
  and wrote a manifest. The built worker (bundled Z3) returned:
  - `M:Target.Caller(System.Int64)~System.Int64 [Postcondition] outcome=Proven core=[source-summary:M:Helper.Value(System.Int64)]`
  - `M:Target.NoThrow(System.Int64)~System.Int64 [Effect] outcome=Proven core=[compiler-effect:...]`
  - `M:Target.Pure(System.Int64)~System.Int64 [Effect] outcome=Proven core=[compiler-effect:...]`

  A search of the repository for "intercept" found no handling.
- **What is wrong:** C# interceptors replace a call site with a different
  method at emit time. Roslyn's semantic model and IOperation tree still
  report the original `TargetMethod`, so every SharpProof layer that follows
  `TargetMethod` analyzes the wrong callee. The frontend lowers the call as
  `call:M:Helper.Value`, the relational summary provider summarizes
  `Helper.Value`'s body, and the effect engine uses `Helper.Value`'s effect
  summary. None of these ask Roslyn which interceptor, if any, is bound to
  the location, and the collector does not reject compilations that enable
  interceptors. The compilation capture records the interceptor syntax tree
  as ordinary source, so nothing downstream can notice either.
- **Failure scenario:**

  ```csharp
  public static class Helper
  {
      public static long Value(long p) => p;
  }

  public static class Target
  {
      public static long Caller(long p)
      {
          Contract.Ensures(Contract.Result<long>() == p);
          return Helper.Value(p);
      }

      [DoesNotThrow]
      public static long NoThrow(long p) => Helper.Value(p);

      [EnforcePure]
      public static long Pure(long p) => Helper.Value(p);
  }
  ```

  A source generator (or a hand-written file) in the same project intercepts
  those `Helper.Value(p)` calls with a method that returns something else,
  writes state, or throws. All three claims are published as `Proven`, and
  the build succeeds under `require-proven`, although none of them holds for
  the emitted assembly. Interceptors are a supported, non-preview feature in
  current SDKs and are emitted by shipping generators (for example the
  ASP.NET Core request delegate and configuration binding generators, and
  third-party AOT generators), so projects can enable them without writing
  one by hand.
- **Suggested fix:** For every invocation the analyzer, collector or
  frontend relies on, ask Roslyn for the bound interceptor
  (`SemanticModel.GetInterceptorMethod` on the invocation syntax, available
  in the Roslyn versions that support interceptors). If one exists, treat the
  call as a call to the interceptor or abstain (`UnsupportedCallable`). As a
  simpler first step, make the collector and analyzer fail closed when the
  parse options enable `InterceptorsNamespaces`/`InterceptorsPreviewNamespaces`
  or when any method carries `System.Runtime.CompilerServices.InterceptsLocationAttribute`,
  and include that feature state in the compilation fingerprint. Add an
  end-to-end regression test with the three claims above.

### Flow facts about captured primary-constructor parameters survive calls that reassign them

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs` (`TryStorage`,
  `HavocCall`, `HavocArguments`). The helper that already recognizes this
  case is `SharpProof.Effects/PrimaryConstructorParameterOwnership.cs`
  (`IsReceiverBacked`), which is used by the effect scanner's write
  classification but not by the flow domain.
- **Confidence:** Confirmed in the analyzer and end to end. Analyzer probe
  (`SharpProofFeatures=effects`): the `Counter` and `Holder` methods below
  reported nothing. The same classes written with ordinary private fields
  (`private int count;` or `private string? text;`, plus a constructor)
  reported SP0046 for both methods. `[EnforcePure]` on methods that write the
  captured parameter correctly reported SP0002, so only the flow facts are
  wrong. Running the unmodified collector analyzers and the built worker on
  the same source published `M:Counter.Divide~System.Int32 [Effect]` and
  `M:Holder.Length~System.Int32 [Effect]` as `Proven` with `compiler-effect`
  cores, while the field-based control was
  `Unknown(EffectContractNotEstablished)`.
- **What is wrong:** In a class or struct with a primary constructor, a member
  that uses a constructor parameter reads and writes a compiler-generated
  instance field, but Roslyn still exposes each use as an
  `IParameterReferenceOperation`. `ManagedAbstractFlow.TryStorage` treats
  every parameter reference as method-local storage, so a branch condition
  such as `count != 0` or `text != null` refines that storage. At an ordinary
  call, `HavocCall` only forgets the storage of `ref`/`out` arguments (or
  everything for local functions and delegates). An instance method called on
  `this` can reassign the captured field, but the refined fact survives, so
  the later division or dereference is classified as non-throwing. Real
  fields never become tracked storage, which is why the field-based control
  is handled correctly.
- **Failure scenario:**

  ```csharp
  public class Counter(int count)
  {
      [DoesNotThrow]
      public int Divide()
      {
          if (count != 0)
          {
              Reset();
              return 100 / count; // DivideByZeroException
          }

          return 0;
      }

      private void Reset() { count = 0; }
  }

  public class Holder(string? text)
  {
      [DoesNotThrow]
      public int Length()
      {
          if (text != null)
          {
              Clear();
              return text.Length; // NullReferenceException
          }

          return 0;
      }

      private void Clear() { text = null; }
  }
  ```

  Both methods always throw once the guarded branch is taken, yet both effect
  claims are proven. The same stale fact can also produce wrong SP0027
  call-site results when a captured parameter is passed to a method with a
  precondition after an intervening call.
- **Suggested fix:** In `TryStorage` and in the parameter read in
  `EvaluateBounded` (`state.Get(parameter.Parameter)`), do not treat a parameter
  reference as local storage when
  `PrimaryConstructorParameterOwnership.IsReceiverBacked(parameter, currentMethod)`
  is true. Model it as receiver field state that is never refined, or havoc
  all receiver-backed parameter facts at every call that is not provably
  unable to reach the receiver (any instance call, any call receiving `this`
  or an alias, and any delegate or local function call). Add analyzer and
  end-to-end regression tests with the two classes above and their
  field-based controls.

### String interpolation of an `ISpanFormattable` value is analyzed as `IFormattable.ToString`, but the runtime calls `TryFormat`

- **File:** `SharpProof.Effects/StringConcatenationEffectResolver.cs`
  (`ResolveFormattedValueCall`, `TryResolveIFormattableToString`,
  `IsDispatchUncertain`).
- **Confidence:** Confirmed, including a published false proof. For the
  types below, the analyzer (`SharpProofFeatures=effects`) reported nothing
  for `InterpolateStruct` (`[EnforcePure]`), `InterpolateStructThrows`
  (`[DoesNotThrow]`) and `InterpolateSealedClass` (`[EnforcePure]`). Running
  the collector and worker on the same source published all three as
  `Proven` effect claims with `compiler-effect:<sha256>` cores, while a
  control method that writes a static field was
  `Unknown(EffectContractNotEstablished)`. Compiled and run on .NET 9,
  `InterpolateStruct` incremented `Money.Calls` and returned an empty string
  (so `ToString` never ran), `InterpolateStructThrows` threw
  `InvalidOperationException`, and `InterpolateSealedClass` incremented
  `Price.Calls`. `string.Format("{0}", m)` and `$"{m:C}"` were reported or
  unsupported, and `"x" + m` (which does call the pure `ToString()`) was
  correctly accepted.
- **What is wrong:** For an interpolation hole, `ResolveFormattedValueCall`
  picks `IFormattable.ToString(string, IFormatProvider)` when the type
  implements `IFormattable`, and otherwise `ToString()`. On .NET 6 and later
  the compiler lowers `$"..."` to `DefaultInterpolatedStringHandler`, whose
  `AppendFormatted<T>` checks `ISpanFormattable` first and calls
  `TryFormat(Span<char>, out int, ReadOnlySpan<char>, IFormatProvider)`
  (again after growing the buffer when it returns `false`). For such types
  `ToString` is never called, so the effects the analyzer imports belong to
  a method that does not run, and the effects of the method that does run
  are ignored. `IsDispatchUncertain` only treats type parameters and
  unsealed reference types as uncertain, so structs and sealed classes get
  an exact, wrong target.
- **Failure scenario:**

  ```csharp
  public readonly struct Money : ISpanFormattable
  {
      public static int Calls;
      public string ToString(string? format, IFormatProvider? provider) => "m";
      public override string ToString() => "m";
      public bool TryFormat(Span<char> destination, out int charsWritten,
          ReadOnlySpan<char> format, IFormatProvider? provider)
      {
          Calls++;                                            // static write
          if (Calls > 1000) { throw new InvalidOperationException(); }
          charsWritten = 0;
          return true;
      }
  }

  [EnforcePure] public static string InterpolateStruct(Money m) => $"{m}";
  [DoesNotThrow] public static string InterpolateStructThrows(Money m) => $"{m}";
  ```

  Value types that implement `ISpanFormattable` (money, identifiers,
  vectors, dates) are common in exactly the code that wants these
  contracts, and a `TryFormat` that caches, logs, counts, or validates and
  throws makes the published purity or no-throw proof false.
- **Suggested fix:** When the receiver type implements `ISpanFormattable`
  and the interpolation is lowered to an interpolated-string handler,
  resolve the call to the type's `TryFormat` implementation (via
  `FindImplementationForInterfaceMember`) and join it with the
  `IFormattable.ToString` implementation, because the call that runs depends
  on the target framework (`string.Format` on older frameworks calls
  `ToString`). Treat the call as possibly repeated. If the target cannot be
  resolved exactly, return `Unsupported()` as for other unresolved
  formatting. Add analyzer and collector regressions for a struct and a
  sealed class whose `TryFormat` writes static state and throws.

### A callee's non-exhaustive switch expression throws `SwitchExpressionException` that effect summaries never record

- **File:** `SharpProof.Effects/EffectMethodNodeBuilder.cs` (the CFG walk
  that scans `block.BranchValue` but ignores `ControlFlowBranchSemantics.Throw`),
  `SharpProof.Effects/OperationEffectScanner.cs` (`ScanObjectCreation`
  returns `EffectSummary.Empty` for `creation.IsImplicit`;
  `ScanLexicalControlEffects` only collects `IThrowOperation` nodes from the
  operation tree), and `SharpProof.Analyzer.Core/LanguageSubsetGate.cs`
  (which rejects switch expressions only in the *selected* method).
- **Confidence:** Confirmed, including published false proofs. With
  `SharpProofFeatures=effects` the analyzer reported nothing for the three
  `[DoesNotThrow]` callers below, and the collector and worker published all
  three as `Proven` effect claims with `compiler-effect:<sha256>` cores.
  Compiled and run on .NET 9, `CallsPartial(3)`, `CallsAllNamed((Shade)7)`
  and `CatchWrongType(3)` each threw
  `System.Runtime.CompilerServices.SwitchExpressionException`. The same
  switch expressions directly in a selected method are rejected with SP0047
  (`UnsupportedOperationKind (SwitchExpression)`), and a caller with a
  `catch (SwitchExpressionException)` that writes a static field was
  reported (SP0002), which shows the handler-reachability side knows the
  throw exists.
- **What is wrong:** Roslyn does not represent the "no arm matched" path of a
  switch expression as an `IThrowOperation` in the operation tree. In the
  control-flow graph it becomes a block whose `BranchValue` is an *implicit*
  `new SwitchExpressionException(value)` (or `InvalidOperationException` on
  frameworks without that type) and whose fall-through successor has
  `ControlFlowBranchSemantics.Throw`. Effect summaries collect thrown types
  lexically from `IThrowOperation` nodes, so this throw is never seen. The
  CFG walk does scan the branch value, but `ScanObjectCreation` returns
  `EffectSummary.Empty` for implicit creations, and nothing converts the
  block's `Throw` semantics into a thrown type. `OperationEffectScanner`
  does have a `ScanSwitchExpression` that adds
  `SwitchExpressionException` when `SwitchExpressionFacts.HasReachableUnmatchedPath`
  holds, but the CFG walk never sees an `ISwitchExpressionOperation`: in the
  lowered graph the switch expression is already split into blocks, so that
  code never runs for method bodies. The callee summary is therefore
  complete with no exceptions (and no allocation), and every caller imports
  it. `ExceptionHandlerReachability` models the unmatched
  path through `SwitchExpressionFacts.HasReachableUnmatchedPath`, so the two
  halves of the analysis disagree.
- **Failure scenario:**

  ```csharp
  public enum Shade { Light, Dark }

  private static int Partial(int x) => x switch { 1 => 10, 2 => 20 };
  private static int AllNamed(Shade s) => s switch { Shade.Light => 1, Shade.Dark => 2 };

  [DoesNotThrow] public static int CallsPartial(int x) => Partial(x);
  [DoesNotThrow] public static int CallsAllNamed(Shade s) => AllNamed(s);
  [DoesNotThrow] public static int CatchWrongType(int x)
  {
      try { return Partial(x); }
      catch (ArgumentException) { return -1; }
  }
  ```

  A switch expression over every named value of an enum is the common case:
  the compiler only warns (CS8524, often suppressed) because an out-of-range
  enum value still falls through, and relational patterns that miss a value
  are easy to write. Any `[DoesNotThrow]`, `[AllowedExceptions]` or
  `[EffectContract]` claim that reaches such a helper is published as
  proven. `[ZeroAllocations]` also misses the exception allocation:
  `[ZeroAllocations] int CallsPartialZ(int x) => Partial(x);` was silent,
  while the same attribute reported SP0045 for a helper with an explicit
  `throw new InvalidOperationException()`.
- **Suggested fix:** In the CFG walk, treat any block whose fall-through
  successor has `Throw` semantics as throwing the static type of its
  `BranchValue`, including implicit ones, and resolve the construction and
  allocation of an implicit thrown `IObjectCreationOperation` instead of
  returning `EffectSummary.Empty`. Better, derive escaping exceptions from
  the CFG throw blocks rather than from lexical `IThrowOperation` nodes, so
  compiler-synthesized throws cannot be missed. Until then, apply the
  switch-expression rejection of `LanguageSubsetGate` to callee bodies as
  well. Add regressions for the three callers above and for an allocation
  claim.

### Custom interpolated-string handler constructors are skipped as implicit object creations

- **File:** `SharpProof.Effects/OperationEffectScanner.cs`
  (`ScanObjectCreation`: `if (creation.IsImplicit) return EffectSummary.Empty;`).
  The same line hides the exception allocation in the switch-expression entry
  above.
- **Confidence:** Confirmed, including published false proofs. For the code
  below the analyzer reported nothing for `C1` (`[EnforcePure]`) and `C3`
  (`[DoesNotThrow]`), and the collector and worker published both as
  `Proven` effect claims with `compiler-effect:<sha256>` cores. Compiled and
  run on .NET 9, `C1` incremented `HLog.Count` and `C3` threw
  `InvalidOperationException`. A handler whose effect is in
  `AppendLiteral` instead (`C2`) was correctly reported, and the same
  interpolation written directly in a selected method is rejected with
  SP0047 (`UnsupportedOperationShape (Invocation)`).
- **What is wrong:** When an interpolated string is converted to a type
  marked `[InterpolatedStringHandler]`, Roslyn represents the handler
  construction as `IInterpolatedStringHandlerCreationOperation.HandlerCreation`,
  an `IObjectCreationOperation` with `IsImplicit = true` that calls the
  handler's (user-written) constructor. `ScanObjectCreation` returns
  `EffectSummary.Empty` for every implicit creation, so the constructor's
  reads, writes, exceptions and allocations are all dropped, while the
  appends that follow are scanned normally. The selected-method gate hides
  this in the annotated method itself, but not in the helpers it calls.
- **Failure scenario:**

  ```csharp
  [InterpolatedStringHandler]
  public ref struct CtorHandler
  {
      public CtorHandler(int literalLength, int formattedCount) { HLog.Count++; }
      public void AppendLiteral(string s) { }
      public void AppendFormatted<T>(T value) { }
  }

  public static int TakeCtor(ref CtorHandler handler) => 0;
  private static int UsesCtorHandler(int x) => TakeCtor($"a{x}");

  [EnforcePure] public static int C1(int x) => UsesCtorHandler(x);  // published as Proven
  ```

  Logging and tracing libraries use exactly this shape: the handler
  constructor checks whether the log level is enabled (reading ambient
  state), rents a buffer (allocating or writing a pool), and may throw.
  Wrapping such a call in a helper gives a proven `[EnforcePure]`,
  `[ZeroAllocations]` or `[DoesNotThrow]` claim that is false at run time.
- **Suggested fix:** Scan implicit object creations like explicit ones
  (resolve the constructor and its allocation), and only skip creations that
  are known to be effect-free, such as `Nullable<T>` wrapping synthesized by
  the CFG, by an explicit allow list. Add analyzer and collector tests for a
  handler whose constructor writes static state, throws, and allocates,
  called both directly and through a helper.

### A `foreach` enumerator's `Dispose` inside a `using` block is skipped as the using's own disposal

- **File:** `SharpProof.Effects/UsingDisposalEffectResolver.cs`
  (`IsSynthesizedSynchronousDispose`) and its caller
  `SharpProof.Effects/OperationEffectScanner.cs` (`ScanInvocation` returns
  `EffectSummary.Empty` when that predicate matches).
- **Confidence:** Confirmed, including a published false proof. In the code
  below, `C1` was silent in the analyzer and was published by the collector
  and worker as a `Proven` effect claim (`compiler-effect:<sha256>` core).
  Compiled and run on .NET 9, `C1` incremented `DLog.Count` once, from the
  enumerator's `Dispose`. The same loop without the surrounding `using`
  (`C2`) was reported (SP0002) and published as
  `Unknown(EffectSummaryIncomplete)`.
- **What is wrong:** `IsSynthesizedSynchronousDispose` decides that an
  implicit `Dispose()` invocation is the synthesized disposal of a `using`
  resource if the invocation's *syntax ancestors* include a
  `UsingStatementSyntax` (or a `using` local declaration). The resource
  disposal itself is scanned separately by `UsingDisposalEffectResolver.Scan`,
  so `ScanInvocation` drops every matching invocation. But Roslyn's CFG also
  synthesizes an implicit `Dispose()` for the enumerator of a `foreach`, and
  a `foreach` nested anywhere inside a `using (...) { ... }` block has that
  `using` statement among its ancestors. Its disposal is dropped and nothing
  scans it, so the enumerator's `Dispose` effects (writes, exceptions,
  allocation, and the dispatch uncertainty that otherwise makes the summary
  incomplete) vanish.
- **Failure scenario:**

  ```csharp
  public sealed class Items
  {
      public Enumerator GetEnumerator() => new Enumerator();
      public struct Enumerator : IDisposable
      {
          public int Current => 0;
          public bool MoveNext() => false;
          public void Dispose() { DLog.Count++; }
      }
  }

  private static int LoopInsideUsing(Items items)
  {
      var total = 0;
      using (new Resource())
      {
          foreach (var item in items) { total = item; }
      }
      return total;
  }

  [EnforcePure] public static int C1(Items items) => LoopInsideUsing(items); // published as Proven
  ```

  Iterating inside a `using` block (a reader, a lock handle, a pooled
  buffer) is ordinary code, and enumerators whose `Dispose` returns a pooled
  array or releases a lock are common. Any such helper gives a proven
  `[EnforcePure]`, `[ZeroAllocations]` or capability claim for its callers.
- **Suggested fix:** Identify the synthesized resource disposal structurally
  instead of by syntax ancestry: match the implicit `Dispose` invocation to
  the specific `IUsingOperation`/`IUsingDeclarationOperation` resource it
  disposes (for example, the invocation's syntax must be the using
  statement or declaration itself, and its receiver must be the resource
  capture), and let every other implicit `Dispose` (foreach enumerators in
  particular) go through normal call resolution. Add a regression with the
  loop above, with a throwing `Dispose`, and with the `foreach` in the
  resource expression of the `using`.

### Catch reachability treats an explicit `Nullable<T>` unwrap as non-throwing, so handler effects are dropped

- **File:** `SharpProof.Effects/ExceptionHandlerReachability.cs` (the
  built-in `IConversionOperation` branch of the potential-exception walk,
  which only adds exceptions for unboxing and explicit reference casts) and
  the shared predicates in `eng/RoslynCfgThrowFacts.cs`
  (`BuiltInOperationMayThrow` counts a conversion only when it is `checked`;
  `OperationMayThrow` adds reference casts and reference-to-value unboxing
  but not value-to-value nullable unwrapping).
- **Confidence:** Confirmed, including published false proofs. Every
  `[EnforcePure]` method below was silent in the analyzer and was published
  by the collector and worker as a `Proven` effect claim
  (`compiler-effect:<sha256>` core). Compiled and run on .NET 9,
  `FromLocalNull()` set `s_state` to 5 and `ViaHelper(null)` set it to 7.
  The same conversion is classified correctly for effect summaries: a
  `[DoesNotThrow]` method returning `(int)n` reported SP0046 naming
  `System.InvalidOperationException`, and so did
  `[AllowedExceptions(typeof(ArgumentException))]`. Catches reached by
  unboxing, checked narrowing, array covariance, division, negative array
  sizes, null dereference, property getters, type initializers and
  `Dispose` were all correctly treated as reachable.
- **What is wrong:** An explicit conversion from `Nullable<T>` to `T` (or to
  another type through `T`, such as `(long)intNullable`) reads `.Value` and
  throws `InvalidOperationException` when there is no value. It is neither
  `checked`, nor a reference conversion, nor unboxing, so
  `ExceptionHandlerReachability` adds no potential exception for it, and a
  `catch` whose only possible source is such a conversion is classified as
  unreachable. `OperationEffectScanner.IsReachable` then skips every
  operation in that handler. `ConversionEffectClassifier.ClassifyNullableConversion`
  already knows the exception; the two components disagree.
- **Failure scenario:**

  ```csharp
  private static int s_state;

  [EnforcePure]
  public static int FromLocalNull()
  {
      int? n = null;
      try { return (int)n; }                              // always throws
      catch (InvalidOperationException) { s_state = 5; return 0; } // always runs
  }

  private static int Unwrap(int? n) => (int)n;

  [EnforcePure]
  public static int ViaHelper(int? n)
  {
      try { return Unwrap(n); }
      catch (InvalidOperationException) { s_state = 7; return 0; }
  }
  ```

  The same happens with `catch (Exception)`, `catch (SystemException)`, a
  bare `catch`, and `(long)n`. Unwrapping a nullable inside `try` and
  falling back in the handler is a common pattern, and any effect in that
  fallback (logging, a static cache, a counter, an allocation, a throw)
  disappears from `[EnforcePure]`, `[ZeroAllocations]`,
  `[AllowedCapabilities]` and `[DoesNotThrow]` results.
- **Suggested fix:** Derive the potential exceptions of every built-in
  conversion from `ConversionEffectClassifier.Classify(...).Throws` instead
  of a hand-maintained subset, and treat
  `Conversion.IsNullable && IsExplicit` with a `Nullable<T>` operand and a
  non-nullable target as throwing `InvalidOperationException` in
  `RoslynCfgThrowFacts` as well (it is also used by the SP0027 call-site
  analysis). Add a test that asserts, for every conversion kind, that
  catch reachability and the effect summary agree on the thrown types.

### A `using` inside a `catch` or `finally` block loses its `Dispose` effects

- **File:** `SharpProof.Effects/UsingDisposalEffectResolver.cs` (`Scan` skips
  every `IUsingOperation`/`IUsingDeclarationOperation` for which
  `_flow.IsReachable(operation)` is false) and
  `SharpProof.Effects/ManagedAbstractFlow.cs` (`ManagedFlowResult.IsReachable`
  reports an operation reachable only if a recorded flow state exists for
  it, and the managed flow records none inside exception handlers). Compare
  `OperationEffectScanner.IsReachable`, which treats `finally` as reachable
  and asks handler reachability about `catch` clauses.
- **Confidence:** Confirmed, including published false proofs. In the code
  below, every `[EnforcePure]` method and `ThrowingDisposeInFinally` were
  silent in the analyzer, and the collector and worker published all seven
  as `Proven` effect claims (`compiler-effect:<sha256>` cores). The same
  catch with a plain static write (`PlainWriteInCatch`) was reported, and a
  `using` in the `try` or method body was reported in every exit shape
  tried (return, throw, `goto`, nested blocks, two declarations). Compiled
  and run on .NET 9 with a one-element array, `DeclarationInCatch`,
  `StatementInFinally` and `ViaHelper` each incremented `HLog2.Count`, and
  `ThrowingDisposeInFinally` threw `InvalidOperationException` on every
  call. The same happens to a `using` placed *after* a `try` whose body
  always ends in a `throw` that its `catch` handles
  (`try { throw new InvalidOperationException(); } catch (InvalidOperationException) { } using (new Writer5()) { }`):
  it was silent while a plain static write in the same position was
  reported.
- **What is wrong:** Resource disposal is not in the operation tree, so
  `UsingDisposalEffectResolver` scans `using` operations lexically and filters
  them with the managed flow's reachability. That reachability comes from the
  forward dataflow, which does not propagate states into `catch` or `finally`
  regions (nor to code reached only through a handler that completes
  normally), so every such `using` looks unreachable and its disposal is
  dropped. `OperationEffectScanner`'s constructor already knows this ("absence
  of a fact cannot prove an operation unreachable after a normally completing
  handler") and turns abstract reachability off for any method containing a
  `try`, but the using resolver receives the raw `ManagedFlowResult` and
  ignores that switch. The dropped effects are writes, allocation,
  capabilities and exceptions thrown by `Dispose`. A `finally` block always
  runs, so `ThrowingDisposeInFinally` throws on every call yet is proven not
  to throw.
- **Failure scenario:**

  ```csharp
  [EnforcePure]
  public static int StatementInFinally(int[] a)
  {
      try { return a.Length; }
      finally { using (new Writer2()) { } }   // Writer2.Dispose writes static state
  }

  [DoesNotThrow]
  public static int ThrowingDisposeInFinally(int[] a)
  {
      try { return 1; }
      finally { using (new Thrower2()) { } }  // always throws
  }
  ```

  Cleanup and recovery code in handlers routinely uses `using` (a pooled
  buffer, a lock handle, a log scope). Any `[EnforcePure]`,
  `[ZeroAllocations]`, `[AllowedCapabilities]` or `[DoesNotThrow]` claim
  whose method, or any helper it calls, has such a handler is published as
  proven.
- **Suggested fix:** Filter `using` operations with the scanner's
  handler-aware `IsReachable` (pass it into the resolver instead of the raw
  `ManagedFlowResult`), or make `ManagedFlowResult.IsReachable` return
  "unknown" rather than "unreachable" for operations inside exception
  regions it did not analyze. Add regressions for a `using` statement and a
  `using` declaration in a `catch`, a bare `catch`, and a `finally`, with a
  writing and a throwing `Dispose`, directly and through a helper.

### Checked enum and nullable conversions are treated as non-throwing because Roslyn reports `IsChecked = false` for them

- **File:** `SharpProof.Effects/ConversionEffectClassifier.cs` (the
  `{ IsNumeric: true } or { IsEnumeration: true }` branch passes
  `operation.IsChecked` to `CheckedOverflow`) and `eng/RoslynCfgThrowFacts.cs`
  (`BuiltInOperationMayThrow`/`OperationMayThrow` count a built-in
  conversion as throwing only when `IsChecked` is true), which feed the
  effect scanner and `ExceptionHandlerReachability`.
- **Confidence:** Confirmed, including published false proofs. Roslyn's
  `IConversionOperation` for `checked((SmallE)x)` (an explicit enumeration
  conversion to a `byte`-backed enum) reports `IsChecked = False`, while
  `checked((byte)x)` reports `IsChecked = True`; both compile to
  overflow-checked IL. Compiled and run on .NET 9, `checked((SmallE)x)`
  threw `OverflowException` for `x = 300` and `x = -1`. In the analyzer every
  method below was silent, and the collector and worker published all eight
  as `Proven` effect claims (`compiler-effect:<sha256>` cores). The
  `unchecked` variant is correctly proven. With
  `CheckForOverflowUnderflow=true`, a plain `(SmallP)x` cast was silent while
  `(byte)x` in the same method shape was reported. Nullable conversions have
  the same gap: Roslyn also reports `IsChecked = False` for
  `checked((byte?)x)` and `checked((byte)x)` with `int? x`, and
  `checked((TinyE?)x)`. All three threw `OverflowException` for 300 at run
  time. `[DoesNotThrow] byte? LiftedNarrow(int? x) => checked((byte?)x);`
  was silent, and
  `[AllowedExceptions(typeof(InvalidOperationException))] byte UnwrapNarrow(int? x) => checked((byte)x!);`
  was accepted although it can also throw `OverflowException`, while the
  same attribute on the non-nullable `checked((byte)x)` reported
  `System.OverflowException`. Checked enum increments, `e + 1`, `e += 1`,
  enum subtraction and lifted `checked(a + b)` were reported correctly.
- **What is wrong:** C# treats an explicit enumeration conversion as an
  explicit numeric conversion between the underlying types, so in a checked
  context (a `checked(...)` expression, a `checked` block, or the project
  option) it throws `OverflowException` when the value does not fit.
  Roslyn's operation tree marks `IsChecked` only on (non-nullable) numeric
  conversions, not on enumeration or nullable conversions, even though it
  emits `conv.ovf.*` for all of them.
  `ConversionEffectClassifier` clearly means to handle enumeration
  conversions the same way as numeric ones, and passes the flag into
  `ClassifyNullableConversion` for nullable ones, but it relies on
  `operation.IsChecked`, so the overflow is never added. Catch reachability
  uses the same flag, so a handler for that `OverflowException` is also
  considered unreachable and its effects are dropped.
- **Failure scenario:**

  ```csharp
  public enum SmallE : byte { A = 1, B = 200 }

  [DoesNotThrow] public static SmallE IntToByteEnum(int x) => checked((SmallE)x);   // throws for 300
  [DoesNotThrow] public static int ByteEnumToSByte(SmallE e) => checked((sbyte)e);  // throws for B

  private static int s_state;
  [EnforcePure]
  public static int CatchOverflow(int x)
  {
      try { return (int)checked((SmallE)x); }
      catch (OverflowException) { s_state = 1; return 0; }   // runs for x = 300
  }
  ```

  Converting wire values, database codes or `int` parameters to enums in a
  checked context is a standard way to validate them, and projects that turn
  on `CheckForOverflowUnderflow` get this on every enum cast. Each such cast
  in an annotated method or a helper makes `[DoesNotThrow]` and
  `[AllowedExceptions]` claims false, and hides the effects of the handler
  that catches the overflow.
- **Suggested fix:** Decide checkedness from the conversion kind and the
  checked context rather than `IConversionOperation.IsChecked` alone: for an
  explicit enumeration conversion, an explicit nullable conversion, and
  explicit numeric conversions between an enum and an integer type,
  compute the underlying source and target
  types and apply the same range check as for numeric conversions, using
  the enclosing `checked`/`unchecked` context or
  `CompilationOptions.CheckOverflow`. Apply the same rule in
  `RoslynCfgThrowFacts` and `ExceptionHandlerReachability`. Add tests for
  `checked` expressions, `checked` blocks and the project option, for enums
  with `byte`, `short`, `int` and `ulong` underlying types, for lifted and
  unwrapping nullable narrowing, and for a `catch (OverflowException)`
  handler with an effect.

### A deconstruction whose `Deconstruct` always throws is summarized as divergence, dropping its exception and every other effect

- **File:** `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
  (`ScanDeconstruction`: `phasesComplete ? EffectSummaryOperations.Unsupported() : EffectSummaryOperations.MayDiverge()`),
  fed by `OperationCompletionEvaluator.CanCompleteDeconstructionPhases`.
- **Confidence:** Confirmed, including published false proofs. For the code
  below, the analyzer was silent for `C1` and `C2`, and the collector and
  worker published both as `Proven` effect claims (`compiler-effect:<sha256>`
  cores). Compiled and run on .NET 9, `C1(new AlwaysThrowsD())` threw
  `InvalidOperationException`. When `Deconstruct` throws only for some
  inputs (`if (V < 0) throw ...`), or when it is called explicitly as
  `t.Deconstruct(out var a, out var b)`, the claim was correctly `Unknown`
  (`UnsupportedOperation`). Everything else the always-throwing
  `Deconstruct` does is lost as well: with a body
  `{ DLog2.Count++; throw new InvalidOperationException(); }`, a helper that
  deconstructs it was accepted by both `[EnforcePure]` (despite the static
  write) and `[ZeroAllocations]` (despite the exception allocation).
- **What is wrong:** Deconstruction calls a user `Deconstruct` method, which
  the operation tree does not show as an invocation. `ScanDeconstruction`
  handles that phase coarsely: if the phase can complete normally it adds
  `Unsupported()` (sound), but if the completion evaluator decides it can
  *never* complete normally (because `Deconstruct` always throws), it adds
  only `MayDiverge()`, which records possible non-termination with no
  thrown types and no uncertainty. Under partial correctness, divergence
  does not violate `[DoesNotThrow]`, so a method that always throws is
  accepted. The code treats "does not complete normally" as if it meant
  "does not terminate".
- **Failure scenario:**

  ```csharp
  public sealed class AlwaysThrowsD
  {
      public void Deconstruct(out int a, out int b) => throw new InvalidOperationException();
  }

  private static int H1(AlwaysThrowsD t) { var (a, b) = t; return a + b; }
  [DoesNotThrow] public static int C1(AlwaysThrowsD t) => H1(t);   // published as Proven
  ```

  `Deconstruct` methods that throw `NotSupportedException` or
  `InvalidOperationException` unconditionally are a common way to block
  deconstruction of a type, or a placeholder in an interface
  implementation. Any `[DoesNotThrow]` or `[AllowedExceptions]` claim that
  reaches such a deconstruction is published as proven, and so are purity,
  allocation and capability claims when the method writes, allocates or
  synchronizes before throwing.
- **Suggested fix:** Resolve the `Deconstruct` method (the
  `DeconstructionInfo` of the operation) and use its effect summary, including
  its thrown exceptions, or return `Unsupported()` in both branches. Never
  replace a phase that cannot complete normally with `MayDiverge()` unless
  it is known not to throw. Add regressions for an always-throwing
  `Deconstruct` directly and through a helper.

### `++` and `--` through user-defined implicit conversions run two user methods that effect analysis never sees

- **File:** `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
  (`ScanIncrementOrDecrement`), `SharpProof.Effects/OperationCompletionEvaluator.cs`
  (`CanCompleteIncrementValue`), `eng/RoslynCfgThrowFacts.cs`
  (`OperationMayThrow`), and the same pattern in
  `SharpProof.Effects/ExceptionHandlerReachability.cs`.
- **Confidence:** Confirmed, including published false proofs. For the code
  below, the analyzer was silent for all six annotated methods, and the
  collector and worker published all six as `Proven` effect claims
  (`compiler-effect:<sha256>` cores). Compiled and run on .NET 9, `Increment`
  and `IncrementDirect` each added 3 to `CLog.Count`, `ThrowIncrement` and
  `ThrowDecrementDirect` threw `InvalidOperationException`, and
  `AllocIncrement` and `AllocIncrementDirect` each allocated 24 bytes. The
  equivalent compound assignment `w += 1` was correctly reported in every
  form, as were the same conversions reached through `??`, tuple
  conversions, tuple deconstruction, tuple equality, `if` and `switch`.
- **What is wrong:** When a type has no `++` operator but converts
  implicitly to and from a type that does, C# accepts `w++` and compiles it
  as `w = (Wrapper)((int)w + 1)`, calling the user-defined conversion
  `Wrapper -> int` and then `int -> Wrapper`. Roslyn's
  `IIncrementOrDecrementOperation` does not expose these conversions:
  `OperatorMethod` is `null` (the predefined `int` operator), and there is
  no `InConversion`/`OutConversion` pair like the one
  `ICompoundAssignmentOperation` has. `ScanIncrementOrDecrement` therefore
  adds only the target read, the checked-overflow effect and nothing for the
  operator, `CanCompleteIncrementValue` treats `OperatorMethod == null` as
  always completing, and `OperationMayThrow` treats an unchecked increment
  with no operator method as unable to throw. Whatever the two conversion
  operators write, allocate or throw is dropped. Because the throw is
  invisible to catch reachability as well, a `catch` around `w++` is treated
  as unreachable and its own effects are dropped: `[EnforcePure]` accepted
  `try { w++; } catch (InvalidOperationException) { DLog.Count++; }`
  directly and through a helper, while the same body with `w += 1` was
  reported.
- **Failure scenario:**

  ```csharp
  public struct Wrapper
  {
      public int V;
      public static implicit operator int(Wrapper w) { CLog.Count++; return w.V; }
      public static implicit operator Wrapper(int v) { CLog.Count += 2; return new Wrapper { V = v }; }
  }
  public struct ThrowWrapper
  {
      public int V;
      public static implicit operator int(ThrowWrapper w) => throw new InvalidOperationException();
      public static implicit operator ThrowWrapper(int v) => throw new InvalidOperationException();
  }
  public sealed class BoxedInt
  {
      public int V;
      public static implicit operator int(BoxedInt b) => b.V;
      public static implicit operator BoxedInt(int v) => new BoxedInt { V = v };
  }

  private static Wrapper IncrementHelper(Wrapper w) { w++; return w; }
  private static ThrowWrapper ThrowIncrementHelper(ThrowWrapper w) { w++; return w; }
  private static BoxedInt AllocIncrementHelper(BoxedInt b) { b++; return b; }

  [EnforcePure] public static Wrapper Increment(Wrapper w) => IncrementHelper(w);                  // Proven
  [DoesNotThrow] public static ThrowWrapper ThrowIncrement(ThrowWrapper w) => ThrowIncrementHelper(w); // Proven
  [ZeroAllocations] public static BoxedInt AllocIncrement(BoxedInt b) => AllocIncrementHelper(b);   // Proven
  [EnforcePure] public static Wrapper IncrementDirect(Wrapper w) { w++; return w; }                 // Proven
  [DoesNotThrow] public static ThrowWrapper ThrowDecrementDirect(ThrowWrapper w) { --w; return w; } // Proven
  [ZeroAllocations] public static BoxedInt AllocIncrementDirect(BoxedInt b) { b++; return b; }      // Proven
  ```

  Wrapper types with implicit conversions to and from a primitive are a
  common idiom (strongly typed IDs, units, handles), and conversions that
  validate their input and throw, or that allocate a new wrapper object,
  are ordinary. Every effect claim that reaches such a `++` or `--` is
  published as proven.
- **Suggested fix:** Treat an increment or decrement whose `OperatorMethod`
  is `null` as supported only when the target type (after removing
  `Nullable<T>`) has a predefined `++`/`--`: the integral types, `char`,
  `float`, `double`, `decimal`, enumerations and pointers. For any other
  target type, the compiler used user-defined conversions, so either
  resolve them (`Compilation.ClassifyConversion` from the target type to
  the operator type and back, then summarize both `MethodSymbol`s as calls,
  the way `ScanCompoundAssignment` summarizes `InConversion` and
  `OutConversion`) or return `Unsupported()`. Apply the same rule in
  `CanCompleteIncrementValue`, `OperationMayThrow` and
  `ExceptionHandlerReachability`. Add regressions for `++`, `--`, prefix and
  postfix forms, directly, through a helper and inside a `try` with a
  `catch`.

### Positional patterns on `object` call a user `ITuple` implementation that effect analysis treats as field reads

- **File:** `SharpProof.Effects/OperationEffectScanner.cs`
  (`ScanRecursivePattern`: `if (pattern.DeconstructSymbol is not IMethodSymbol deconstruct) return ScanChildren(pattern);`),
  `SharpProof.Effects/ExceptionHandlerReachability.cs` (the
  `IRecursivePatternOperation` case), and
  `SharpProof.Effects/OperationCompletionEvaluator.cs`
  (`GetPatternCompletionFacts`).
- **Confidence:** Confirmed, including published false proofs. For the code
  below, the analyzer was silent for `C1` to `C5`, and the collector and
  worker published all five as `Proven` effect claims
  (`compiler-effect:<sha256>` cores). Compiled and run on .NET 9, `C1`, `C2`
  and `C5` each incremented `FLog.Count`, `C3` threw
  `InvalidOperationException`, and `C4` allocated 24 bytes. A positional
  pattern on a type with a `Deconstruct` method, a list pattern with a user
  `Slice`, and record, anonymous-type and tuple equality over a field type
  with a side-effecting `Equals` were all correctly reported.
- **What is wrong:** When the input of a positional pattern such as
  `o is (1, 1)` has no `Deconstruct` and is not a value tuple (for example
  `object` or `ITuple`), C# matches it through
  `System.Runtime.CompilerServices.ITuple`: it type-tests the input for
  `ITuple`, reads `Length`, and reads `this[i]` for each subpattern. These
  are interface calls to whatever type the caller passes. In the operation
  tree the pattern has `DeconstructSymbol == null`, which is also the shape
  for value tuples, where the elements are plain field reads. The scanner
  handles every `DeconstructSymbol == null` pattern as the value-tuple case
  and scans only the subpatterns, so the `ITuple` calls contribute nothing.
  Catch reachability and completion make the same assumption, so a `catch`
  whose only source is an `ITuple` member is treated as unreachable:
  `[EnforcePure]` accepted a helper
  `try { return o is (1, 1) ? 1 : 0; } catch (InvalidOperationException) { GLog.Count++; return 0; }`
  called with a throwing `ITuple`, while the same helper over a type whose
  `Deconstruct` throws was reported.
- **Failure scenario:**

  ```csharp
  public sealed class MyTuple : ITuple
  {
      public int Length { get { FLog.Count++; return 2; } }
      public object? this[int index] => 1;
  }
  public sealed class ThrowingTuple : ITuple
  {
      public int Length => throw new InvalidOperationException();
      public object? this[int index] => throw new InvalidOperationException();
  }
  public sealed class AllocTuple : ITuple
  {
      public int Length => 2;
      public object? this[int index] => new object();
  }

  private static bool IsPair(object o) => o is (1, 1);
  private static int SwitchPair(object o) => o switch { (1, 1) => 1, _ => 0 };
  private static bool IsPairTyped(ITuple t) => t is (_, _);

  [EnforcePure] public static bool C1(MyTuple t) => IsPair(t);            // Proven
  [EnforcePure] public static int C2(MyTuple t) => SwitchPair(t);         // Proven
  [DoesNotThrow] public static bool C3(ThrowingTuple t) => IsPair(t);     // Proven
  [ZeroAllocations] public static bool C4(AllocTuple t) => IsPair(t);     // Proven
  [EnforcePure] public static bool C5(MyTuple t) => IsPairTyped(t);       // Proven
  ```

  Positional patterns on `object` are common in message and AST matching
  code, and any argument that implements `ITuple` (including user types
  and `System.Tuple<...>`, whose indexer boxes value-type elements) reaches
  user code. The selected method itself is not affected here, because the
  language gate rejects `is` patterns and switch expressions there; the
  false proofs come from helper summaries.
- **Suggested fix:** In `ScanRecursivePattern`, distinguish the two
  `DeconstructSymbol == null` cases. When `MatchedType` (or the input type)
  is a tuple type, keep scanning the subpatterns. Otherwise, if the pattern
  has deconstruction subpatterns, the compiler uses `ITuple`: resolve
  `ITuple.Length` and `ITuple[int]` as interface dispatch on the input (which
  is unknown unless the receiver's exact type is known), or return
  `Unsupported()`. Mirror the rule in `ExceptionHandlerReachability` and
  `GetPatternCompletionFacts`. Add regressions for `is`, `switch`
  statements and switch expressions over `object` and `ITuple` inputs,
  inside and outside a `try`.

### Slice patterns on arrays allocate a new array that allocation analysis never sees

- **File:** `SharpProof.Effects/SwitchExpressionFacts.cs`
  (`GetCallableListPatternMember` and `IsCompilerIntrinsicListPatternMember`),
  used by `ScanListPattern` in `SharpProof.Effects/OperationEffectScanner.cs`.
- **Confidence:** Confirmed, including published false proofs. For the code
  below, the analyzer was silent for `C1`, `C2` and `C3`, and the collector
  and worker published all three as `Proven` effect claims
  (`compiler-effect:<sha256>` cores). Compiled and run on .NET 9 with
  `new[] { 0, 1, 2 }`, each call allocated 32 bytes (the two-element
  sub-array). The same patterns on `string` (which calls `Substring`), on
  `List<int>` and on `ReadOnlySpan<int>` were reported, and `a is [_, ..]`
  (a slice with no subpattern, which allocates nothing) was accepted, which
  is correct.
- **What is wrong:** A slice subpattern with a nested pattern, such as
  `.. var rest` or `.. [1, 2]`, needs the slice as a value. For an array the
  compiler produces it with `RuntimeHelpers.GetSubArray<T>(T[], Range)`,
  which allocates a new array. Roslyn does not expose that call:
  `ISlicePatternOperation.SliceSymbol` is `null` for array inputs (it is
  `string.Substring` for strings and the user `Slice` method for other
  types). `GetCallableListPatternMember` returns `null` for a `null`
  `SliceSymbol`, so the scanner adds nothing for the slice and goes straight
  to the nested pattern. `IsCompilerIntrinsicListPatternMember` also treats
  every metadata member of an array list pattern as a compiler intrinsic
  without effects, which would hide the allocation even if the symbol were
  present.
- **Failure scenario:**

  ```csharp
  private static bool HasTail(int[] a) => a is [_, .. var rest] && rest.Length > 0;
  private static bool EndsWithPair(int[] a) => a is [_, .. [1, 2]];
  private static int TailSum(int[] a) => a switch { [var head, .. var tail] => head + tail.Length, _ => 0 };

  [ZeroAllocations] public static bool C1(int[] a) => HasTail(a);     // Proven, allocates
  [ZeroAllocations] public static bool C2(int[] a) => EndsWithPair(a); // Proven, allocates
  [ZeroAllocations] public static int C3(int[] a) => TailSum(a);       // Proven, allocates
  ```

  Head/tail matching over arrays is the idiomatic way to write recursive
  list processing with list patterns, and code that cares about
  `[ZeroAllocations]` is exactly the code that would otherwise have
  switched to spans. Each such claim is published as proven although every
  successful match allocates.
- **Suggested fix:** When a slice subpattern has a nested pattern and the
  list pattern's input type is an array, add a managed allocation (and the
  `GetSubArray` call's other effects) instead of skipping the slice, and
  stop classifying array list-pattern members as allocation-free
  intrinsics. More generally, treat a `null` `SliceSymbol` on a slice with a
  subpattern as `Unsupported()` unless the input is a span. Add regressions
  for `.. var rest`, a nested list pattern on the slice, and the discard
  slice `..`, in `is` expressions, `switch` statements and switch
  expressions.

### Declaration patterns that box a value type are not counted as allocations

- **File:** `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
  (`ScanCoreOperationTail`: `IIsPatternOperation isPattern => ScanChildren(isPattern)` and
  `IPatternOperation => ScanDefaultPattern(operation)`), with boxing
  recognized only for `IConversionOperation` in
  `SharpProof.Effects/ConversionEffectClassifier.cs` (`ClassifyBoxing`).
- **Confidence:** Confirmed, including published false proofs. For the code
  below, the analyzer was silent for all six `[ZeroAllocations]` methods,
  and the collector and worker published all six as `Proven` effect claims
  (`compiler-effect:<sha256>` cores). Compiled and run on .NET 9 (Release),
  `C1`, `C2`, `C3`, `C5` and `C6` each allocated 24 bytes per call. `C4`
  measured 0 bytes because the JIT removed the box in that shape, but the
  compiled IL still contains `box`, and the claim is published either way.
  The same conversions written as casts (`(IComparable)x`, `(object)x`)
  are reported.
- **What is wrong:** A declaration or recursive pattern that tests a value
  of value type against an interface or `object` (`x is IComparable c`,
  `m is IMarker k`, `case IMarker k:`, a switch-expression arm with a
  designation) has to box the value to produce the pattern variable. The
  operation tree does not show that box as an `IConversionOperation`; it
  is implied by the pattern's input type and `MatchedType`. The scanner
  handles `IIsPatternOperation` and ordinary patterns by scanning their
  children, and boxing is classified only for conversion operations, so the
  allocation is never added. Nullable inputs (`int? x; x is IComparable c`)
  box the underlying value in the same way.
- **Failure scenario:**

  ```csharp
  public interface IMarker { }
  public struct Marked : IMarker { public int V; }

  private static bool H1(int x) { if (x is IComparable c) { return c is not null; } return false; }
  private static bool H2(int x) { if (x is object o) { return o is not null; } return false; }
  private static bool H3(Marked m) { if (m is IMarker k) { return k is not null; } return false; }
  private static int H4(Marked m) => m switch { IMarker k when k is not null => 1, _ => 0 };
  private static bool H5(int? x) { if (x is IComparable c) { return c is not null; } return false; }
  private static object? H6(Marked m) => m is IMarker k ? k : null;

  [ZeroAllocations] public static bool C1(int x) => H1(x);           // Proven, allocates
  [ZeroAllocations] public static bool C2(int x) => H2(x);           // Proven, allocates
  [ZeroAllocations] public static bool C3(Marked m) => H3(m);        // Proven, allocates
  [ZeroAllocations] public static int C4(Marked m) => H4(m);         // Proven
  [ZeroAllocations] public static bool C5(int? x) => H5(x);          // Proven, allocates
  [ZeroAllocations] public static object? C6(Marked m) => H6(m);     // Proven, allocates
  ```

  Matching a struct against an interface pattern is an easy way to write a
  hidden box, and it is exactly what an allocation contract should catch.
  The selected method itself is not affected, because the language gate
  rejects `is` patterns there; the false proofs come from helper summaries.
- **Suggested fix:** When a declaration, type or recursive pattern's input
  type is a value type (or `Nullable<T>`) and its `MatchedType` (or
  `NarrowedType`) is a reference type, add the same managed-allocation
  effect `ClassifyBoxing` adds for a boxing conversion, at least when the
  pattern binds a variable or has nested subpatterns that consume the boxed
  value. Add regressions for `is`, `switch` statement case labels, switch
  expression arms, `Nullable<T>` inputs and generic `T` inputs.

### Writable references to elements of covariant arrays omit `ArrayTypeMismatchException`

- **File:** `SharpProof.Effects/OperationEffectScanner.cs` (`ScanArrayElement`
  adds `ArrayTypeMismatchException` only when `access == EffectAccess.Write`;
  `ScanArgumentValues` skips `out` arguments, and `ref` arguments and `ref`
  locals are scanned as reads).
- **Confidence:** Confirmed, including published false proofs. For the code
  below, the analyzer was silent for `C1` to `C4`, and the collector and
  worker published all four as `Proven` effect claims
  (`compiler-effect:<sha256>` cores). Compiled and run on .NET 9 (Debug and
  Release) with `object[] arr = new string[1]`, all four threw
  `ArrayTypeMismatchException`. The control, which stores into the same
  element with an assignment, was reported, and its exception list
  included `ArrayTypeMismatchException`; for the `ref` and `out` forms the
  list was only `IndexOutOfRangeException` and `NullReferenceException`.
  Multi-dimensional arrays behave the same way: `ref object r = ref a[0, 0];`
  over an `object[,]` was charged with the same two exceptions, and threw
  `ArrayTypeMismatchException` at run time for `new string[1, 1]`. A `ref`
  return of `ref a[0]` through a helper was also accepted by
  `[DoesNotThrow]` inside the same `catch` pair.
- **What is wrong:** Taking a writable managed reference to an element of
  an array whose element type is an unsealed reference type (`ref a[0]`,
  `out a[0]`, `ref object r = ref a[0];`, a `ref` return of `a[i]`) compiles
  to `ldelema`, which checks that the array's runtime element type is
  exactly the static element type and throws `ArrayTypeMismatchException`
  otherwise, whether or not anything is ever written through the
  reference. `ScanArrayElement` models the covariance check only for a
  direct store, where the operation is scanned with `EffectAccess.Write`.
  A `ref` argument or `ref` local initializer is scanned as a read, and an
  `out` argument's element expression is not scanned as a value at all, so
  the check is never added. `in` and `ref readonly` references use
  `readonly. ldelema`, which skips the check, so only writable references
  are affected.
- **Failure scenario:**

  ```csharp
  private static void SetOut(out object o) { o = new object(); }
  private static void Touch(ref object o) { }
  private static int H1(object[] a) { try { SetOut(out a[0]); } catch (IndexOutOfRangeException) { } catch (NullReferenceException) { } return 0; }
  private static int H2(object[] a) { try { Touch(ref a[0]); } catch (IndexOutOfRangeException) { } catch (NullReferenceException) { } return 0; }
  private static int H3(object[] a) { try { ref object r = ref a[0]; } catch (IndexOutOfRangeException) { } catch (NullReferenceException) { } return 0; }
  private static int H4(object[] a) { Touch(ref a[0]); return 0; }

  [DoesNotThrow] public static int C1(object[] a) => H1(a);   // Proven
  [DoesNotThrow] public static int C2(object[] a) => H2(a);   // Proven
  [DoesNotThrow] public static int C3(object[] a) => H3(a);   // Proven
  [AllowedExceptions(typeof(IndexOutOfRangeException), typeof(NullReferenceException))]
  public static int C4(object[] a) => H4(a);                  // Proven

  object[] arr = new string[1];
  C1(arr);   // throws ArrayTypeMismatchException
  ```

  Passing an array element by `ref` or `out` (`int.TryParse(s, out values[i])`
  is the everyday version, though with a value-type element it is safe) is
  common, and with `object[]` or any unsealed class element type the
  check is always emitted.
- **Suggested fix:** Scan the element of a `ref` or `out` argument, a `ref`
  local initializer, a `ref` assignment and a `ref` return with a
  "writable reference" access that adds `ArrayTypeMismatchException` under
  the same `ArrayStoreIsDefinitelyCompatible` rule used for stores (element
  type sealed or a value type, or an array known to be exactly that type),
  and keep `in`/`ref readonly` as reads. Add regressions for each form,
  including an `out` argument whose value is otherwise not read.

### Exceptions raised by the runtime allocate, but `[ZeroAllocations]` never charges them

- **File:** `SharpProof.Effects/OperationEffectScanner.Expressions.cs`
  (`Throw(params string[])`, which builds implicit-exception effects with
  `EffectSummaryOperations.Throw` only) and
  `SharpProof.Effects/EffectSummaryOperations.cs` (`Throw`), used by every
  implicit exception source in `SharpProof.Effects/OperationEffectScanner.cs`
  (`ScanArrayElement`, null receivers, division, checked arithmetic,
  conversions and so on).
- **Confidence:** Confirmed, including published false proofs. The
  allocation mode of a third differential fuzzer (helpers with patterns,
  property getters and single-level `try`/`catch`) accepted 2,692
  `[ZeroAllocations]` methods over 1,000 batches; the only two that
  allocated at run time (232 and 464 bytes) did so because a helper divided
  by zero and caught the `DivideByZeroException`. Reduced to the code below,
  the analyzer was silent for `C1` to `C4`, and the collector and worker
  published all four as `Proven` effect claims (`compiler-effect:<sha256>`
  cores). Measured on .NET 9 (Release, second call), `C1(1, 0)` and
  `C2(null)` allocated 192 bytes each, and `C3(new int[1], 5)` and
  `C4(int.MaxValue)` 296 bytes each. The control `K1`, which throws and
  catches the same `DivideByZeroException` explicitly, was reported
  (`may-effect summary includes allocation: Managed`) and was `Unknown` in
  the worker. Throwing an *existing* exception object allocates as well,
  because the runtime captures a stack trace into managed arrays: with
  `ThrowGiven(GivenFail e) { try { throw e; } catch (GivenFail) { return -1; } catch (NullReferenceException) { return -2; } }`,
  `[ZeroAllocations] C1(GivenFail e) => ThrowGiven(e)` was silent in the
  analyzer and `Proven` in the worker, and each call allocated 2,032 bytes.
- **What is wrong:** Every exception is a managed object. For an explicit
  `throw new X()` the scanner sees the object creation and charges a
  managed allocation. For exceptions the runtime raises itself (division by
  zero, a null dereference, an out-of-range index, checked overflow, an
  invalid cast, unboxing, array covariance and so on) the scanner adds only
  the thrown type through `EffectSummaryOperations.Throw`, with no
  allocation. Even an explicit `throw` of an existing object is charged
  nothing, although throwing always records a stack trace in managed
  memory. The analysis already models these exceptions as possible
  (they are what makes `[DoesNotThrow]` fail), so it knows the object is
  created; it just does not charge for it. Whether the exception escapes or
  is caught inside the method, the allocation has happened.
- **Failure scenario:**

  ```csharp
  private static int SafeDivide(int a, int b) { try { return a / b; } catch (DivideByZeroException) { return 0; } }
  private static int SafeValue(Node n) { try { return n.V; } catch (NullReferenceException) { return -1; } }
  private static int SafeIndex(int[] a, int i) { try { return a[i]; } catch (IndexOutOfRangeException) { return -1; } }
  private static int SafeChecked(int a) { try { return checked(a + 1); } catch (OverflowException) { return int.MaxValue; } }
  private static int ExplicitThrow(int b) { try { if (b == 0) { throw new DivideByZeroException(); } return 1; } catch (DivideByZeroException) { return 0; } }

  [ZeroAllocations] public static int C1(int a, int b) => SafeDivide(a, b);     // Proven, allocates
  [ZeroAllocations] public static int C2(Node n) => SafeValue(n);               // Proven, allocates
  [ZeroAllocations] public static int C3(int[] a, int i) => SafeIndex(a, i);    // Proven, allocates
  [ZeroAllocations] public static int C4(int a) => SafeChecked(a);              // Proven, allocates
  [ZeroAllocations] public static int K1(int b) => ExplicitThrow(b);            // reported (control)
  ```

  Saturating arithmetic written as `try { return checked(a * b); } catch (OverflowException) { ... }`
  is a real idiom in numeric code, which is where allocation contracts are
  used. Each overflow allocates several hundred bytes (plus stack-trace
  capture) while the contract is published as proven. If excluding runtime
  exception objects is intended, the documentation does not say so, and it
  is inconsistent with charging the same exception when it is thrown
  explicitly.
- **Suggested fix:** Make every implicit exception source, and every
  `throw` and `throw;` of an existing object, add
  `Allocate(EffectAllocationKind.Managed)` alongside its thrown type (a
  helper such as `ImplicitThrow(...)` used in place of `Throw(...)`), so
  that `[ZeroAllocations]` fails wherever `[DoesNotThrow]` would. If the
  project instead decides that exception objects do not count, apply that
  rule to explicit throws too and document it in `SEMANTICS.md` next to the
  purity definition. Add the four helpers above as analyzer and worker
  regression tests.

## P1 - High

### A guard on a joined local crashes managed flow analysis with a false monotonicity failure

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs`
  (`ManagedAbstractValue.Integer`, `ManagedAbstractValue.Join`,
  `ManagedFlowState.LessThanOrEqual`, `RefineInteger`) and
  `SharpProof.Dataflow/ForwardDataflowAnalysis.cs` (`AnalyzeCore`, which throws
  `InvalidOperationException("Block N transfer must be monotone as its input grows.")`)
- **Confidence:** Confirmed. A differential `[DoesNotThrow]` fuzzer hit the
  exception in 50 of 400 batches of eight random guarded methods, and it was
  the only analyzer exception it found, and a second fuzzer that puts
  richer code in helper callees hit it in 159 of 1,000 batches. A third
  fuzzer whose helpers use patterns, property getters and user-defined
  operators hit it in 8, 9 and 5 of 1,000 batches in its `[DoesNotThrow]`,
  `[EnforcePure]` and `[ZeroAllocations]` modes, even with the selected
  methods restricted to guards on parameters; the two crashes that were
  reduced both had the same shape (a local initialized to a constant,
  conditionally reassigned, then guarded). A relational guard triggers it as well as
  `!= 0`: `int x = 1; if (a > 0) { x = Get(a); } if (x > 0) { return 10 / x; } return 0;`
  crashed in a `[DoesNotThrow]` method. It was
  minimized to a three-statement method. In the analyzer
  probe (`SharpProofFeatures=effects`), each method below raised the exception
  from `ForwardDataflowAnalysis.AnalyzeCore` through
  `ManagedAbstractFlow.Analyze`, `EffectMethodNodeBuilder.Build` and
  `EffectContractDiagnostics.Evaluate`, and the analyzer reported no SP
  diagnostic for it. `Unguarded` divides by zero whenever `b > 0`, so SP0046
  is missing. A `[DoesNotThrow]` method that only calls an unannotated helper
  with this body also lost its SP0046. Other methods in the same compilation
  were still reported. The unmodified collector analyzers on the same source
  reported
  `SP0049: SharpProof could not emit the final compiler manifest: InvalidOperationException: Block 12 transfer must be monotone as its input grows.`
  and wrote no manifest. Variants inside `try`/`finally`, with `if` instead of
  `?:`, or with `[EnforcePure]` instead of `[DoesNotThrow]`, crashed the same
  way. A later differential SP0027 fuzzer (callers whose locals start from
  literals, `SharpProofFeatures=contracts` only) hit the same exception in
  37 of 300 batches of 25 callers, and in 44 of 600 batches of a second
  campaign that added loops and `switch` statements, this time through
  `RequiresCallSiteDiscovery.Get` and `ManagedAbstractFlow.Analyze`. The minimized caller has no parameter at
  all:

  ```csharp
  public static long T(long a, long b) { Contract.Requires(b == 100); return 0; }

  public static long C()
  {
      T(1, 5);                    // SP0027 is lost
      long b = -4;
      if (b % 2 != 0) { b = 3; }
      return T(1, b > 0 ? 1 : 2); // the call-site analysis throws here
  }
  ```

  The analyzer reported AD0001 for `C` and dropped the SP0027 for the literal
  violation `T(1, 5)`, while the same call in another method was still
  reported. With the guard written as `b + 2 == 1` (which folds), both SP0027
  diagnostics appeared.

  `TryArithmetic` folds only `+`, `-` and `*`, so `b % 2` (and `b / 2`) on a
  constant stays undecided, both branches reach the join, and the later
  `b > 0` test takes the constant-condition shortcut in the first iteration
  only. The same body in a `[DoesNotThrow]` method
  (`return b > 0 ? 1 : 100 / p;`) lost its SP0046. With a non-negative
  constant (`b = 4`) the guard happens to stay one-sided and nothing crashes.
  The collector wrote a manifest for the contracts-only caller.
- **What is wrong:** The flow order is checked syntactically.
  `ManagedFlowState.LessThanOrEqual` tests `Join(left, right) == right` using
  record-struct equality, and `ManagedAbstractValue.Join` computes
  `ExcludesZero` as `left.ExcludesZero && right.ExcludesZero`. The flag is not
  normalized. `Integer(IntervalValue.Constant(1))` carries
  `ExcludesZero = false` even though its interval excludes zero, while
  `RefineInteger` sets `ExcludesZero = true` for the joined interval `[0, 1]`
  after `x != 0`. In the first iteration the guard block sees `x = {1}`. The
  condition is a constant, so `Assume` returns the state unchanged with the
  flag false. After the join the block sees `x = [0, 1]`, and the refinement
  gives `[0, 1]` with the flag true. Both outputs denote `{1}`, but the join of
  the old and new values is `[0, 1]` with the flag false, which is not equal
  to the new value. The monotonicity guard throws. The comment in
  `RefineInteger` shows a similar non-monotone flag was fixed for range
  bounds, but constants and the constant-condition shortcut were not
  normalized. `ManagedAbstractFlow.Analyze` already catches the sibling
  `DataflowConvergenceException` with the comment "Reaching the iteration
  bound must not escape as AD0001", so letting the monotonicity failure
  escape the analyzer callback is clearly unintended.
- **Failure scenario:**

  ```csharp
  [DoesNotThrow]
  public static int Guarded(int b)
  {
      int x = 1;
      if (b > 0) { x = 0; }
      return x != 0 ? x / x : 0;
  }

  [DoesNotThrow]
  public static int Unguarded(int b)
  {
      int x = 1;
      if (b > 0) { x = 0; }
      return x != 0 ? 1 : 100 / x; // DivideByZeroException when b > 0
  }
  ```

  In an IDE or ordinary build the analyzer exception surfaces only as an
  AD0001 warning, and the SP0046 for `Unguarded` never appears (SP0046 is an
  Info diagnostic by default, but teams that raise it to an error rely on
  it), so the broken contract builds silently. In a
  verification build every such method makes the whole manifest fail with
  SP0049, so no claim in the project is verified. This guard pattern (a flag
  or count initialized to a constant, updated on one branch, then tested
  before a division, dereference or index) is very common.
- **Suggested fix:** Normalize the flag where values are built. Have
  `Integer(value, excludesZero)` store `excludesZero || !value.Contains(0)`,
  and compute `Join`'s flag from `IsDefinitelyNonZero` on each side. Better,
  make `ManagedFlowState.LessThanOrEqual` a semantic order (interval
  inclusion plus implication of the zero-exclusion fact, with values that
  already exclude zero treated as having the flag). Also make `Assume` apply
  the same refinement when the condition folds to a constant. In
  `ForwardDataflowAnalysis`, report a monotonicity failure as an
  incomplete-analysis result (SP0047 and `Unknown`) instead of throwing out
  of the analyzer callback. Fold `/` and `%` in `TryArithmetic` when both
  operands are constants (with C# truncation and the `MinValue / -1`
  overflow), which removes the remainder trigger. Add the methods above as
  analyzer and collector regression tests, including the contracts-only
  caller.

### Long expression chains and long methods overflow the stack and kill the compiler process

- **File:** `SharpProof.Effects/OperationCompletionEvaluator.cs`
  (`CanCompleteNormally`, `CanCompleteNormallyCore`, `CanCompleteBinary`,
  `ChildrenCanComplete`), reached from `EffectAnalysisSession` and
  `EffectMethodNodeBuilder` in both the analyzer and the compiler collector;
  and `SharpProof.Effects/ManagedAbstractFlow.cs` (`IsAcyclic`, a recursive
  depth-first search over basic blocks), called unconditionally at the end of
  `EffectMethodNodeBuilder`'s CFG walk; and `DefiniteOperationFacts`
  (`MethodCanCompleteNormally`, `MayCompleteNormally`,
  `InvocationMayCompleteNormally`, in `SharpProof.Effects/ManagedAbstractFlow.cs`),
  which follows calls into callee bodies. No SharpProof component calls
  `RuntimeHelpers.EnsureSufficientExecutionStack`.
- **Confidence:** Confirmed. A `[DoesNotThrow]` method whose body is
  `a + 1 + 1 + ... + 1` crashed the analyzer probe process with
  `Stack overflow.` at 1,000 terms (950 terms still completed). The repeated
  frames were `OperationCompletionEvaluator.CanCompleteNormally` →
  `CanCompleteNormallyCore` → `CanCompleteBinary` → `ChildrenCanComplete`,
  one group per binary node. The same crash happened when the 2,000-term
  expression was in an *unannotated* private helper called by a
  `[DoesNotThrow]` method, and for a 1,500-term string concatenation
  `s + "x" + "x" + ...` under `[EnforcePure]`. Running the unmodified collector
  analyzers on a `[DoesNotThrow]` method with a 2,000-term chain also died with
  `Stack overflow.` (exit code 127). A contract-only method with a 2,000-term
  `checked` sum did not crash the collector, and long `&&` chains and long
  `else if` chains were stopped by the analysis budgets (SP0047) instead.
  A second recursion is reached by long *statement lists*, which are far
  more common: a `[DoesNotThrow]` method with 2,500 sequential
  `if (a > k) { x = x + 1; }` statements killed the probe process with
  `Stack overflow.` (2,000 statements produced the expected SP0047
  `BlockBudgetExceeded`). The repeated frames were
  `ManagedAbstractFlow.IsAcyclic` → `Visit` → `VisitIncluded`, about 4,600
  pairs deep. The same 3,000-statement body in an unannotated helper called
  by a `[DoesNotThrow]` method crashed the analyzer and the collector (exit
  code 127); the helper alone, with no effect claim reaching it, did not.
  A third recursion follows *call chains*: `DefiniteOperationFacts` asks
  whether each callee can complete normally by walking the callee's body,
  which asks the same of its callees, guarded only against cycles. An
  `[EnforcePure]` method at the top of a chain of one-line helpers
  (`M(a) => M'(a) + 1`) crashed at 200 levels (100 were fine), and with
  ten-line helper bodies the analyzer crashed at 80 levels (60 were fine).
  The collector died on the 100-level chain with exit code 127. The depth
  is the longest acyclic path of source methods reachable from an
  annotated method, which large projects can reach through layered
  services or recursive-descent parsers. `EffectCallGraph` bounds the same
  walk at `MaximumCallGraphDepth = 512` for exactly this reason, but the
  completion facts do not use it.
- **What is wrong:** C# binary expressions are left-nested, so a chain of *n*
  operators is an operation tree of depth *n*. The completion evaluator walks
  it recursively, several frames per level, with no depth bound and no
  `EnsureSufficientExecutionStack` check. The analysis budgets that protect
  other paths (block and operation counts) are applied elsewhere and do not
  bound this recursion. `IsAcyclic` recurses once per block along a CFG
  path, and `EffectMethodNodeBuilder` calls it on the full graph after its
  walk even when `ManagedAbstractFlow.Analyze` has already given up on the
  block budget (which it checks *before* its own `IsAcyclic` call). A
  `StackOverflowException` cannot be caught, so the
  failure is not an analyzer exception (AD0001) but the death of the whole
  process: `csc`, the `VBCSCompiler` server, or the IDE's analyzer host.
- **Failure scenario:** Generated code routinely contains long left-nested
  chains: string concatenations that build SQL, logs or templates, large
  arithmetic in generated checksums, or `hash = hash * 31 + field` expansions.
  Long generated methods (parsers, state machines, mappers, validation code)
  routinely contain thousands of statements. If such a method is annotated,
  or is merely called by an annotated method, every build of the project
  terminates the compiler with a stack overflow and no diagnostic that
  points at SharpProof or at the method. In a verification build the
  collector hits the same recursion inside `csc`.
- **Suggested fix:** Evaluate binary chains iteratively (walk the left spine
  with an explicit stack, as Roslyn's own binder does), or bound the
  recursion depth and treat anything deeper as "may not complete normally /
  incomplete" (SP0047). Make `IsAcyclic` iterative (an explicit stack, or
  Roslyn's `ControlFlowGraph` back-edge information) and apply the block
  budget before any whole-graph traversal in `EffectMethodNodeBuilder`.
  Bound `MethodCanCompleteNormally`'s call depth (returning "may complete",
  which is the conservative answer for a may-query) and memoize it per
  method. As a
  general safety net, call
  `RuntimeHelpers.EnsureSufficientExecutionStack()` (or
  `TryEnsureSufficientExecutionStack`) at the entry of every recursive
  visitor in the effect engine and turn `InsufficientExecutionStackException`
  into an incomplete-analysis result. Add analyzer and collector tests with a
  5,000-term arithmetic chain, a 5,000-term string concatenation and a
  5,000-statement method, directly in a selected method and in a callee.

### Completion facts re-walk shared callees on every path, so analysis time grows exponentially with call depth

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs` (`DefiniteOperationFacts.MethodCanCompleteNormally`,
  `MayCompleteNormally`, `InvocationMayCompleteNormally`): each call site
  asks whether the callee can complete normally by walking the callee's
  body again, with only a per-thread "active methods" set to stop cycles and
  no cache of finished results.
- **Confidence:** Confirmed. An annotated method at the top of a chain in
  which each helper calls the next one twice
  (`M_i(a) => M_{i+1}(a) + M_{i+1}(a + 1)`) took 2 seconds to analyze at 12
  levels, 6 seconds at 18, 60 seconds at 22, and did not finish within 600
  seconds at 26 (the analyzer eventually reports nothing, since the code
  cannot throw). With three calls per level (two on one branch, one on the
  other) and a `try`/`catch` in the annotated method, 14 levels already took
  88 seconds. A CPU trace of the 18-level run was dominated by
  `DefiniteOperationFacts.MayCompleteNormally`,
  `SequenceMayCompleteNormally`, `InvocationMayCompleteNormally` and
  `MethodCanCompleteNormally`. Each level roughly doubles the work because
  both calls re-evaluate the same callee body. Contract-only builds are
  affected too, because the SP0027 call-site analysis asks
  `DefiniteOperationFacts` whether each argument completes normally: with
  `SharpProofFeatures=contracts` and a single call `T(M1(1))` into the same
  chain, analysis took 3 seconds at 18 levels, 14 at 22 and 204 at 26.
- **What is wrong:** A method's "can complete normally" answer does not
  depend on the call site, but it is recomputed for every call site on every
  path through the call graph. The effect summaries elsewhere are memoized
  per method (and `EffectCallGraph` bounds its depth), so this walk is the
  only part of the analysis whose cost is the number of *paths* through the
  call graph instead of the number of methods.
- **Failure scenario:** Shared helpers are the norm: a validation routine
  called twice by a parser step, a formatting helper called from both
  branches of a method, and so on through a few dozen layers. Any annotated
  method whose reachable call graph has that shape makes the analyzer, and
  the compiler collector in verification builds, run for minutes to hours.
  In an IDE this pins a CPU core in the analyzer host; in CI it looks like a
  hung build with no diagnostic that points at SharpProof.
- **Suggested fix:** Memoize `MethodCanCompleteNormally` per method (results
  computed while the recursion guard short-circuited a cycle should either
  be excluded from the cache or computed per strongly connected component),
  and bound the call depth as `EffectCallGraph` does. Add a test with a
  30-level doubling call chain and a time budget.

### Catch and foreach variables are classified as owning no caller-visible state

- **File:** `SharpProof.Effects/ConversionOwnershipClassifier.cs`
  (`BuildLocalRegions`, `ClassifyLocal`)
- **Confidence:** Confirmed for a catch variable with a public field.
  `catch (MyException ex) { ex.Handled = true; }` with `public bool Handled;`
  produced no diagnostic, while `error.Handled = true;` on the parameter itself
  reported SP0002. With an auto-property setter instead of a field, SP0002 was
  reported, so use the field form of the scenario below. The foreach variant
  was not tested.
- **What is wrong:** `BuildLocalRegions` seeds every
  `IVariableDeclaratorOperation` in the original operation tree with
  `EffectRegionSet.Empty` and then only learns regions from declarator
  initializers and explicit assignments in that tree. A catch variable
  (`catch (MyException ex)`) and a foreach iteration variable are declarators
  without initializers; the runtime (or the CFG's synthesized
  `ex = <caught exception>` / `item = Current` assignment, which is not in the
  original tree) assigns them. Their region stays empty, so `ClassifyLocal`
  reports that writes through them touch no receiver, parameter, static or
  unknown region.
- **Failure scenario:**
  `[EnforcePure] static void M(MyException error) { try { throw error; } catch (MyException ex) { ex.Handled = true; } }`
  where `Handled` is a public field (`public bool Handled;`) on a source
  exception type. The handler is reachable, but the field write through `ex`
  is attributed to the empty region of `ex`, so the summary contains no
  writes and `IsObservablePure` accepts the method even though it mutates the
  caller's argument. (With an auto-property setter the write was reported, so
  only direct field writes are affected in practice.) The same classification
  applies to a foreach iteration variable, for example
  `foreach (var item in new SourceEnumerable(items)) { item.Count = 0; }` when
  the enumerator members are source methods.
- **Suggested fix:** Seed declarators that have no initializer with
  `EffectRegionSet.Unknown` when they are catch variables or foreach control
  variables (or, more simply, whenever the declarator's parent is an
  `ICatchClauseOperation` or `IForEachLoopOperation`), or build local regions
  from the CFG operations that contain the synthesized assignments.

### String formatting resolves a `new`-hiding `ToString` instead of the virtual slot

- **File:** `SharpProof.Effects/StringConcatenationEffectResolver.cs`
  (`ResolveToString`, `IsDispatchUncertain`)
- **Confidence:** Confirmed with `Derived` declared `sealed`.
  `[EnforcePure] static string Label(Derived d) => "x" + d;` produced no
  diagnostic, while the same method taking a sealed `Base` subclass without a
  hiding `ToString` reported SP0002.
- **What is wrong:** For built-in string concatenation and interpolation the
  resolver walks the operand type and its base types and takes the first
  parameterless instance `ToString()` returning `string`. It does not require
  that the method is `object.ToString` or an override of it. A method declared
  `public new string ToString()` hides the virtual slot, but the compiler
  formats the operand through `Object.ToString()` (a virtual/constrained
  call), which dispatches to the most-derived override of `Object.ToString`
  and never reaches the hiding method. Because the hiding method is not
  virtual, `HasOpenVirtualDispatch` also reports the dispatch as certain.
- **Failure scenario:**
  `class Base { public override string ToString() { s_count++; return "b"; } }`,
  `class Derived : Base { public new string ToString() => "d"; }`, and
  `[EnforcePure] static string Label(Derived d) => "x" + d;`. The resolver
  binds `Derived.ToString` (pure), while the runtime calls `Base.ToString`,
  which writes static state, so the purity claim is proven incorrectly. A
  struct with `public new string ToString()` is analyzed as that method,
  although the runtime calls `ValueType.ToString()` (reflection and
  allocation).
- **Suggested fix:** Resolve the target as
  `FindOverride(receiverType, object.ToString)`: walk base types looking only
  for methods whose `OverriddenMethod` chain ends at `System.Object.ToString`
  (or `object.ToString` itself), and treat the call as dispatch-uncertain for
  any non-sealed reference type unless the resolved override is sealed.

### Abstract flow treats a user-defined `operator !` as built-in negation

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs` (`Assume`,
  `EvaluateUnary`)
- **Confidence:** Confirmed. A selected
  `[EnforcePure] static void M() => Callee(new Flag());` produced no
  diagnostic, while the same callee written with `!flag.IsSet` reported
  SP0002.
- **What is wrong:** Every other refinement in `Assume` requires
  `OperatorMethod: null` (and `IsLifted: false`), but the
  `IUnaryOperation { OperatorKind: UnaryOperatorKind.Not }` arm does not.
  For `if (!flag)` where `flag` is a class with
  `public static bool operator !(Flag f)`, the edge transfer recurses into
  `Assume(state, flag, !expected)`, which falls through to
  `TryStorage(condition)` and stores `Boolean(false)` into the reference-typed
  local or parameter. `EvaluateUnary` likewise folds `!x` to the negated
  Boolean of the operand without looking at `OperatorMethod`. The user operator
  can read heap state that changes between two evaluations, but
  `ManagedMutationFacts.HasMutation` does not treat operator calls or field
  writes as mutations of the local, so the stored Boolean survives.
- **Failure scenario:** In a non-selected callee:
  `if (!flag) { flag.IsSet = true; if (!flag) { } else { s_state++; } }` with
  `operator !(Flag f) => !f.IsSet`. At runtime the inner `else` runs. The
  abstract state still says `!flag == true`, so the inner false edge becomes
  bottom, `ManagedFlowResult.IsReachable` returns false for `s_state++`, and
  `AnalyzeControlFlowGraph` skips it. The callee summary omits the static
  write and a selected caller marked `[EnforcePure]` can be proven. (The
  language subset gate only rejects user-defined operators in the selected
  method itself, not in callees whose summaries are imported.) Re-verified
  with the `Flag` created as a local inside the callee, so the static write in
  the `else` branch is the only impurity: the caller was silent, while the
  identical shape written against a plain `bool` field (no user-defined
  operator) reported SP0002. A regression test must use a local receiver,
  because a write through a parameter is reported on its own.
- **Suggested fix:** Match only `IUnaryOperation { OperatorKind: Not,
  OperatorMethod: null, IsLifted: false }` in `Assume`, and return
  `TopForType(unary.Type)` from `EvaluateUnary` when `OperatorMethod` is not
  null. Never store a Boolean abstract value into storage whose type is not
  `System.Boolean`.

### Throw branches inside conditionally elided calls refine facts after the call

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs`
  (`CreateDataflowGraph`, `Successors`, `TransferCore`)
- **Confidence:** Confirmed, with an adjusted scenario (see Failure
  scenario). `[AllowedExceptions(typeof(ArgumentOutOfRangeException))]` on
  `Scale` produced no diagnostic, while the same body without the `Log` call
  reported SP0046 for `DivideByZeroException`.
- **What is wrong:** `TransferCore` skips operations whose enclosing call is
  conditionally elided (`[Conditional]` symbol not defined, or an
  unimplemented partial method), but the CFG shape inside the elided argument
  is still used. Branch edges apply `Assume` to the branch condition, and a
  throw-expression block contributes no regular successor, so after the elided
  call only the non-throwing path survives. The refinement learned on that path
  (for example `count >= 1`, or `s != null` from `s is null ? throw ... : ...`)
  flows to code after the call, although at runtime the whole invocation,
  including the guard, does not exist.
- **Failure scenario:**
  `[Conditional("TRACE")] static void Log(int value) { }` and
  `static int Scale(int count) { Log(count > 0 ? count : throw new ArgumentOutOfRangeException()); return 100 / count; }`
  compiled without `TRACE`. The false edge leads only to the throw block, so
  `count` is refined to `[1, int.MaxValue]` and `ProvesNonZero` removes
  `DivideByZeroException` from `Scale`'s summary. The elided throw itself is
  still counted, so `[DoesNotThrow]` reports SP0046 naming only
  `ArgumentOutOfRangeException`, an exception that can never be thrown here.
  `[AllowedExceptions(typeof(ArgumentOutOfRangeException))]` on `Scale` is
  proven, although `Scale(0)` throws `DivideByZeroException`. So the same
  mechanism produces a false positive (the elided exception) and a false
  proof (the dropped one). `Debug.Assert(s is null ? throw new ArgumentNullException() : true);`
  followed by `s.Length` loses its `NullReferenceException` the same way.
- **Suggested fix:** When a block's branch value or any operation in a block
  is inside an elided invocation, do not apply `Assume` on its outgoing edges,
  and treat the elided region as a no-op: join the state at the entry of the
  elided invocation into the state after it (for example by adding a synthetic
  edge from the first block of the elided expression to the block containing
  the elided invocation).

### Abstract flow keeps facts about `ref`/`in` parameters across calls

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs` (`CreateEntryState`,
  `HavocCall`, `HavocArguments`)
- **Confidence:** Confirmed, with a different scenario than first written
  (see Failure scenario). Passing `ref` to a static field makes the caller's
  exception set unknown, so a static-field example does not reproduce, but an
  alias to an instance field does.
- **What is wrong:** Entry state tracks every parameter, including `ref`,
  `in` and `ref readonly` parameters, as an ordinary value. Refinements such as
  `x != 0` or `i < array.Length` are kept after an unrelated call because
  `HavocCall` only forgets the storage of `ref`/`out` arguments passed to that
  call (or everything for local-function/delegate calls). A parameter passed
  by reference aliases caller storage (a static field, an array element, a
  field of a shared object) that the callee of the intervening call can
  overwrite. Ref locals are already handled by `WithUntrackedAlias`, but
  by-reference parameters are not.
- **Failure scenario:** With `sealed class Holder { public int Value; }` (no
  field initializer, which would make construction unmodeled), the non-selected
  helpers are
  `static void Reset(Holder h) { h.Value = 0; }`,
  `static int N(ref int x, Holder h) { if (x != 0) { Reset(h); return 10 / x; } return 0; }`
  and `static int Mid() { var h = new Holder(); h.Value = 1; return N(ref h.Value, h); }`.
  The selected method is
  `[AllowedExceptions(typeof(NullReferenceException))] static int K() => Mid();`.
  `ProvesNonZero` still sees the refined `x` after `Reset(h)`, so
  `IntegralDivisionExceptions` omits `DivideByZeroException` and `K` is
  proven, although `K()` always throws `DivideByZeroException`. A control
  that calls `Reset(h)` without the `x != 0` guard reports SP0046 for
  `DivideByZeroException`. Replacing `Reset(h)` with a direct `h.Value = 0;`
  in `N` also stays proven, so writes through an alias are missed as well as
  calls. `NullReferenceException` is allowed only because the callee
  summary conservatively reports it for `h.Value`. Array-bound
  (`ProvesArrayAccess`) and null (`ProvesNonNull`) facts on by-reference
  parameters are exposed the same way.
- **Suggested fix:** Treat parameters with `RefKind` other than `None` like
  untracked ref locals: seed them with `TopForType` and do not refine them, or
  forget all by-reference parameter facts after any invocation, object
  creation, property access or assignment through a non-local target.

### A diverging `Dispose` loses its own effects

- **File:** `SharpProof.Effects/UsingDisposalEffectResolver.cs`
  (`ResolveResource`); the same rule appears in
  `ExceptionHandlerReachability.CanDisposalUnwind`
- **Confidence:** Confirmed. `[EnforcePure] static void Use() { using var pin = new Pin(); }`
  produced no diagnostic, while the same method with a `Dispose` that only
  increments the static counter reported SP0002.
- **What is wrong:** When the resolved `Dispose` can neither complete
  normally nor throw (it definitely diverges), `ResolveResource` returns
  `(EffectSummary.Empty, false)`. Effects executed by `Dispose` before it
  diverges are therefore omitted, which contradicts the `EffectStep`
  convention used elsewhere ("retain effects from a definitely non-completing
  step while suppressing effects that are only reachable after it").
- **Failure scenario:** `sealed class Pin : IDisposable { public void Dispose() { s_disposed++; while (true) { } } }`
  and `[EnforcePure] static void Use() { using var pin = new Pin(); }`. The
  synthesized dispose call is skipped by `ScanInvocation`
  (`IsSynthesizedSynchronousDispose`) and the resolver returns an empty
  summary, so the static write is missing and purity is accepted.
- **Suggested fix:** Return `(ResolveCall(dispose), false)`: keep the
  callee's summary and only use `canUnwind` to stop scanning earlier
  resources.

### Goto continuation for catch reachability only includes the enclosing block

- **File:** `SharpProof.Effects/ExceptionHandlerReachability.cs`
  (`GetGotoTargetContinuation`)
- **Confidence:** Confirmed. `[EnforcePure]` on the scenario method (with
  `c`, `values` and `index` as parameters) produced no diagnostic, while the
  control `try { _ = values[index]; } catch (IndexOutOfRangeException) { s_state++; }`
  reported SP0002.
- **What is wrong:** The continuation of a `goto label` is approximated by
  the remaining operations of the label's immediate parent block (or switch
  section) plus, when that yields no invocation, *one* following invocation
  found by source position (`Take(1)`). Statements after the parent block that
  run when the block completes normally, and any non-invocation operation that
  can throw (division, array access, casts, `throw`), are not included when
  normal flow into them was cut off by a non-completing statement.
- **Failure scenario:**
  `try { { if (c) goto L; throw new ArgumentException(); L: ; } _ = values[index]; } catch (IndexOutOfRangeException) { s_state++; }`.
  Normal flow through the inner block is blocked by the `throw`; the goto
  continuation contains only the empty labeled statement, and the heuristic
  finds no invocation, so `IndexOutOfRangeException` is never added, the
  handler is considered unreachable, and the static write is dropped.
- **Suggested fix:** Compute handler reachability from the Roslyn CFG of the
  protected region (the blocks in the `Try` region and their possible
  exceptions) instead of reconstructing goto continuations from syntax, or
  return `UnknownPotential` whenever a goto target is not the start of a
  statement in the protected block itself.

### SMT backend charges cumulative Z3 resource counts to each query

- **File:** `SharpProof.Smt/IrSmtBackend.cs` (`CheckCore`, `ReadResourceCount`,
  `QueryResourceMeter.ConsumeNative`)
- **Confidence:** Confirmed on Windows with the bundled Z3. A scratch
  harness reused one `IrSmtBackend` (default `QueryRlimit`) for repeated
  `CallableVerifier` runs on the same five-block method. It passed
  `() => backend.ConsumedResourceCount` to `MethodResourceBudget`, as worker
  lanes do. `ConsumedResourceCount` was 1,167 after the first method,
  7.3 million after 100 and 727 million after 1,000. Methods 1 through
  4,128 were correctly `Refuted`. From method 4,129 on (about 40 seconds
  in), every identical method returned `Unknown(ResourceLimit)`, and all
  11,872 remaining runs stayed that way. In a second probe with
  `QueryRlimit = 200,000,000`, a hard nonlinear query ran until it returned
  `Unknown(ResourceLimit)`. The next query on the same backend had the goal
  `true`, and it also returned `Unknown(ResourceLimit)`. The underlying Z3
  behavior was measured directly with the bundled 4.12.2: six separate
  `Solver` instances on one `Context` reported `rlimit count` 14,935, 28,874,
  93,426, 166,216, 282,777 and 303,391, and a trivial `true` query on that
  same context then reported 303,395, while the identical trivial query on a
  fresh `Context` reported 4. The statistic is therefore the context's
  lifetime total, not the cost of the check that just ran.
- **What is wrong:** One `Microsoft.Z3.Context` is created per backend and
  reused for every query (the backend "outlives hundreds of queries per lane").
  After `solver.Check()`, `ReadResourceCount` reads the `rlimit count` entry
  from `solver.Statistics` and `ConsumeNative` adds that whole value to the
  per-query meter. In Z3 the `rlimit count` statistic is reported from the
  context's resource limit (`m().limit().count()`), which is a monotonically
  increasing counter for the lifetime of the context, not a per-solver or
  per-check delta. The solver's own `rlimit` parameter is relative (Z3 pushes
  `m_count + delta`), so the solver finishes normally, but the managed meter
  sees the cumulative total.
- **Failure scenario:** A worker lane verifies many methods with one backend
  (lanes renew their backend only after a method timeout). Once the
  cumulative native count passes `QueryRlimit` (3,000,000 by
  default), `ConsumeNative` throws `QueryResourceLimitException` right after an
  `UNSATISFIABLE` result, so every later query, even a trivial one, becomes
  `Unknown(ResourceLimit)`. `ConsumedResourceCount` also grows roughly
  quadratically, so `MethodResourceBudget` (which reads deltas of it) reports
  exhausted method budgets far earlier than intended. The existing test
  `ResourceAccountingTreatsEachSolverSnapshotAsFresh` only asserts the second
  query cost is at most one million, which the cumulative behavior also
  satisfies, so it cannot detect the problem.
- **Suggested fix:** Read the `rlimit count` statistic immediately before
  `solver.Check()` and charge only the difference, or create a fresh `Context`
  per query. Strengthen the test to assert that the second (inexpensive)
  query's accounted cost is strictly less than the first (expensive) query's
  native cost, and run a loop of a few hundred trivial queries on one backend
  asserting that none returns `ResourceLimit`.

### A helper called from an earlier contract makes the whole compiler manifest undecodable

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`
  (`Create`, one `IrFactory` shared by every callable),
  `SharpProof.Frontend/RoslynOperationLowerer.cs` (opaque invocation member
  names), `SharpProof.Ir/IrFactory.cs` (`GetOrCreateMember`),
  `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs` (`Encode`) and
  `SharpProof.CompilerArtifact/PortableIrGraphCodec.cs`
  (`RequireCanonicalEncoderImage`, `CallDocumentationCommentId`)
- **Confidence:** Confirmed. A scratch harness ran the unmodified collector
  analyzers through `CompilationWithAnalyzers` with the global options that
  the package passes (`SharpProofFeatures=all`, a manifest path, and so on).
  The scenario below reported only
  `SP0049: SharpProof could not emit the final compiler manifest: JsonException: The compiler manifest callable payload is invalid.`
  and wrote no manifest, on every run. A second harness called
  `CompilerCallableLowerer.Prepare`, `CompilerLoweredArtifact.Encode` and
  `PortableIrGraphCodec.Decode` directly. The later callable's graph had a
  member named `opaque:Invocation:<assembly>::M:SharedMember.Helper(System.Int64)...`
  whose `documentationCommentId` was `M:SharedMember.Helper(System.Int64)`.
  `Decode` rejected it with "Portable IR metadata is not the canonical
  encoder image" because the canonical re-encode expects `null`. The failure
  was first found by a C# fuzz batch. It also reproduced with an
  `[EnforcePure]` helper, with a helper in another class, and with a
  `Contract.Ensures` that calls the helper instead of `Contract.Requires`.
  Controls: swapping the method names so that the body call sorts first
  emitted a manifest, and so did a single method that calls the helper in
  both its contract and its body.
- **What is wrong:** `Create` lowers all callables in ordinal callable-ID
  order with one `CompilerCallableLowerer` and one `IrFactory`, and the
  lowerer's `ContractBinder` uses that factory too. `GetOrCreateMember`
  hash-conses members by declaring type, identity, return type, staticness
  and parameter types. The display name is not part of the key, so the first
  caller chooses the name for every later use of the symbol. When contract
  binding lowers a call inside `Contract.Requires` or `Contract.Ensures`,
  `RoslynOperationLowerer` names the member
  `opaque:Invocation:<symbol>:result=<type>`. When a body invocation is
  lowered, `RoslynProgramLowerer.LowerInvocation` asks for the same symbol
  with the purpose `call:`, but gets the existing `opaque:` member back.
  `CompilerLoweredArtifact.Encode` then sets
  `member.DocumentationCommentId = call.CallIdentity` for every summary and
  spec call. `RequireCanonicalEncoderImage` rebuilds that field only from the
  name through `CallDocumentationCommentId`, which returns `null` unless the
  name starts with `call:`. The encoded graph therefore never matches its
  canonical image. `CompilerManifestArtifactJson.HasDecodableCallables`
  (in `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`) swallows the
  `InvalidDataException` and throws a generic `JsonException`, and
  `FinalCompilationCollector` reports SP0049.
- **Failure scenario:** Normal code that uses one helper in a contract and
  in a later method's body:

  ```csharp
  public static class SharedMember
  {
      private static long Helper(long value) => value;

      public static long A(long value)
      {
          Contract.Requires(Helper(value) > 0);
          Contract.Ensures(Contract.Result<long>() > 0);
          return value;
      }

      public static long B(long value)
      {
          Contract.Ensures(Contract.Result<long>() == value);
          return Helper(value);
      }
  }
  ```

  `M:SharedMember.A...` sorts before `M:SharedMember.B...`, so binding A's
  precondition names the `Helper` member first. A itself gets no graph (its
  contract call is unsupported), but B's encoded graph fails validation. A
  verification build then fails with SP0049, and the package targets also
  raise SP0049 ("did not receive the required final compiler manifest").
  Every claim in the compilation is lost, including methods that have nothing
  to do with `Helper` (a third method `C` with its own `Ensures` was not
  verified either). The outcome depends only on ordinal method and type
  names, so renaming `A` to `Z` makes the same project build. The effect is
  the same in the `Alpha`/`Beta` form with the helper on a third class.
- **Suggested fix:** Keep member identity and display names consistent.
  Either include the purpose or name in the `StructuralKey`, so `opaque:` and
  `call:` uses of a symbol become separate members, or give each callable its
  own `IrFactory` (the encoder already writes a self-contained graph for each
  callable). Better still, record the call identity in the member itself
  rather than inferring it from the display name, and have
  `RequireCanonicalEncoderImage` compare against that stored identity. Also
  surface the underlying `InvalidDataException` message in SP0049 and name the
  failing callable. Add a collector regression test with the `SharedMember`
  source above and the name-swapped control.

### Nested IL summaries duplicate evidence rows and make the manifest invalid

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerRelationalSummaryProvider.cs`
  (`_cache`, `_active`, `SummaryEvidenceAuthorities`),
  `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
  (`MetadataResolutionContext.Resolve`, `Translator.ResolveMethod`,
  `Translator.Call`) and `SharpProof.CompilerArtifact/CompilationFingerprint.cs`
  (`ValidSummaryEvidence`)
- **Confidence:** Confirmed. The .NET 9 reference pack plus a separately
  compiled `Lib.dll` (an implementation assembly) was used with the
  unmodified collector analyzers:

  ```csharp
  public static class Lib
  {
      public static int Inner(int a) => a;
      public static int Outer(int a) => Inner(a);
  }
  ```

  A project whose only verified method returns `Lib.Outer(p)` emitted a
  manifest. A project that calls both `Lib.Outer` and `Lib.Inner`, either
  in one method (`int y = Lib.Outer(p); return Lib.Inner(y);`) or in two
  different methods, reported
  `SP0049: SharpProof could not emit the final compiler manifest: JsonException: The compiler compilation evidence is invalid.`
  A first-chance trace showed the exception came from
  `CompilationFingerprint.ValidateShape`. Reading
  `CompilerCallableLowerer.SummaryEvidenceAuthorities` after lowering both
  methods listed two identical `ImplementationIl` rows for
  `M:Lib.Inner(System.Int32)` (same module, token and SHA-256) plus one for
  `M:Lib.Outer(System.Int32)`. The failure was first found by a differential
  IL-summary fuzzer, where most seeds hit it. The same shape written with
  source helpers (`Outer` calling `Inner`, and callers of both) emits a
  manifest and proves all three claims, which isolates the cause to the
  separate metadata compilation used by the IL path.
- **What is wrong:** `CompilerRelationalSummaryProvider` caches summaries and
  their evidence authority in a `Dictionary<IMethodSymbol, ...>` using
  `SymbolEqualityComparer.Default`. A direct call from source resolves
  `Lib.Inner` to a symbol of the user's compilation. When the IL translator
  summarizes `Outer`, it resolves the nested call target with
  `_metadataAssembly.GetTypeByMetadataName(...)`. That assembly comes from
  `MetadataResolutionContext.Resolve`, which creates a second compilation with
  `_compilation.WithOptions(... MetadataImportOptions.All)`. Symbols from two
  compilations never compare equal, so the provider translates `Inner` twice
  and stores two cache entries with the same call identity and evidence hash.
  `SummaryEvidenceAuthorities` orders the entries but does not deduplicate
  them. `ValidSummaryEvidence` requires strictly increasing keys
  (`Compare(previous, key) >= 0` is rejected), so the snapshot is invalid and
  the whole manifest is refused. The same split also defeats the `_active`
  recursion guard across the two symbol families and repeats IL translation
  work.
- **Failure scenario:** A verified project references a NuGet package or a
  `netstandard2.0` class library (both are compiled against implementation
  assemblies, so IL summaries apply). One method calls a library helper that
  calls another public helper, and any verified method also calls that
  second helper directly. The verification build fails with SP0049, and no
  claim in the project is verified. Removing either call, or moving the
  direct call into an unverified method, makes the build pass again.
- **Suggested fix:** Key the summary cache and the active set by a
  compilation-independent identity, such as (module name, module SHA-256 or
  MVID, metadata token), or by the documentation comment ID plus the owning
  module, instead of `IMethodSymbol` equality. Alternatively, map
  metadata-compilation symbols back to the user's compilation before
  consulting the cache. Also deduplicate `SummaryEvidenceAuthorities` by the
  validation key (origin, call identity, evidence identity, SHA-256) when
  the rows are identical, and fail closed only on conflicting rows. Add a
  collector test with a two-method library like the one above, calling both
  methods.

### A literal argument folds away a branch and orphans a summary existential

- **File:** `SharpProof.Summaries/IrRelationalSummaryInstantiator.cs`
  (`Instantiate`, which returns every fresh variable it created),
  `SharpProof.Summaries/IrRelationalSummaryBuilder.cs`
  (`_existentials.AddRange(instantiated.FreshVariables)`) and
  `SharpProof.CompilerArtifact/CompilerLoweredArtifact.cs`
  (`HasValidSummaryFreeVariableRoles`, and the `DecodeBody` check that throws
  "A lowered source-call relation is invalid.")
- **Confidence:** Confirmed. Nested-helper C# fuzzing hit it in 2 of 140 seeds
  (40 methods each), and it minimized to the source below, which the unmodified collector analyzers
  reject with
  `SP0049: SharpProof could not emit the final compiler manifest: JsonException: The compiler manifest callable payload is invalid.`
  A first-chance trace showed `InvalidDataException: A lowered source-call relation is invalid`
  from `CompilerLoweredArtifact.DecodeBody`. An in-process probe over the
  prepared body confirmed the cause: the summary call to `Choose` records two
  existential variables, and one of them does not occur in the instantiated
  relation (`existentials=2 missingFromRelation=1`). Calling the same helper
  with non-constant arguments gives `missingFromRelation=0` and emits a
  manifest.
- **What is wrong:** When a summarized helper makes a nested summary call
  inside a branch, its relation is a disjunction whose arms mention the fresh
  variables of each nested instantiation only in the arm for that branch. At a call site,
  `IrRelationalSummaryInstantiator.Instantiate` substitutes the actual
  arguments, and the IR simplifier folds the branch guard when an argument is
  a literal, so the arm for the untaken branch disappears along with its
  existential variables. `Instantiate` still returns all the fresh variables,
  and the builder records them all as existentials. The artifact validator
  then requires every recorded existential to occur in the relation, so the
  callable is rejected and the whole compilation's manifest is refused.
- **Failure scenario:**

  ```csharp
  private static long H0(long a, long b) => a;
  private static long H1(long a, long b) => b;

  private static long Choose(long a, long b)
  {
      if (b > 0) { return H0(a, b); }
      return H1(a, b);
  }

  public static long M(long p)
  {
      Contract.Ensures(Contract.Result<long>() == p);
      return Choose(p, 1); // literal argument folds the else-branch away
  }
  ```

  Both `Choose(p, 1)` and `Choose(p, 0)` fail; `Choose(p, q)` for a parameter
  `q` succeeds, as does hoisting the literal into a local (`long flag = 1;`)
  or constraining it with `Contract.Requires(q > 0)`. One nested call is
  enough when the literal removes its branch: with
  `static long H(long a, long b) { if (b > 0) { return H0(a, b); } return a; }`,
  the call `H(p, 0)` fails (`existentials=1 missingFromRelation=1`), while
  `H(p, 1)`, which keeps the branch that makes the call, succeeds. The
  `int`-parameter variant of the fuzzer hit this form in 2 of its first 42
  seeds. Implementation-IL summaries are affected the same way: with a
  separately compiled library containing
  `public static int Outer(int a, int b) { if (b > 0) { return Inner(a); } return a; }`,
  a verified method returning `FoldLib.Outer(p, 0)` made the collector report
  SP0049, while `FoldLib.Outer(p, 1)` was proven through `il-summary:`
  evidence. Passing a literal flag,
  limit or mode to a helper that dispatches to other helpers is ordinary code,
  and one such call site makes every claim in the project fail to verify.
- **Suggested fix:** Keep the recorded existentials in step with the
  simplified relation. After substitution, drop the fresh variables that no
  longer occur in `NormalCompletion` or `NormalRelation` (collect the
  relation's variables and intersect), either in `Instantiate` before
  returning `FreshVariables` or in the builder before
  `_existentials.AddRange`. Alternatively, relax
  `HasValidSummaryFreeVariableRoles` to require only that recorded
  existentials are not captured elsewhere, rather than that they all appear.
  Add a collector regression test with the source above and a non-constant
  control.

## P2 - Medium

### Assigning a conditional expression to an existing local leaves its old value in managed flow, and SP0027 reports correct calls

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs` (`Transfer`, the
  `IFlowCaptureOperation` and `ISimpleAssignmentOperation` cases, and
  `TryStorage`), `SharpProof.Effects/CoalesceAssignmentFlowCaptures.cs` (the
  only target-capture resolution, limited to `??=`), and the consumer
  `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`
  (`TryEvaluateAtOrigin`/`TryEvaluateArgumentSnapshot` read the stale fact).
- **Confidence:** Confirmed. A differential SP0027 fuzzer (constant locals,
  every call executed concretely with a probe that records whether the
  compiled-away precondition held) found a call that SP0027 reported although
  the precondition held when it ran. Minimized with
  `SharpProofFeatures=contracts`, where `T` has `Contract.Requires(1 <= a)`
  and `B` has `Contract.Requires(b)`, each of these reported SP0027 on correct
  code:

  ```csharp
  long x = 0; x = true ? 1 : x;  return T(x);   // x == 1
  long x = 0; x = r ? 1 : 2;     return T(x);   // x is 1 or 2
  long x = 0; x = r ? 1 : 2; x = x + 0; return T(x);
  long x = 0; x = (r ? 1 : 2) + 0; return T(x);  // conditional nested in the RHS
  bool b = false; b = r || true; return B(b);   // b == true
  bool b = false; b = r ? true : true; return B(b);
  ```

  `x = 1; return T(x);`, the equivalent `if`/`else` assignment, a
  declaration `long z = r ? 1 : 2;`, and compound assignments such as
  `x += r ? 1 : 2;` (whose target is set to top) were all silent, as
  expected. With the
  second call repeated (`T(x); return T(x);`) both calls were reported.
  Roslyn's CFG for `x = c ? 1 : 5;` (printed with a scratch probe) is:
  `capture#0 = x` before the branch, `capture#1 = 1` / `capture#1 = 5` in the
  arms, then `capture#0 := capture#1` in the join block.
- **What is wrong:** When the right-hand side of a simple assignment
  contains control flow, Roslyn captures the assignment *target* (an lvalue)
  in a flow capture before branching and later assigns through the capture
  reference. `ManagedAbstractFlow.Transfer` treats `capture#0 = x` as an
  rvalue capture (it records x's current value in the capture), and the
  final `capture#0 := capture#1` goes through `TryStorage`, which maps a
  capture reference to the capture id. So the new value is written to the
  capture, and `x` keeps its value from before the assignment. The flow then
  reports that stale value at every later use. `ResolveCoalesceAssignmentTarget`
  performs exactly the missing capture-to-storage mapping, but
  `CoalesceAssignmentFlowCaptures.IsRelevant` only admits captures under a
  `??=` expression. The call-site analyzer's comment in
  `RequiresCallSiteTreeAnalyzer` ("a multi-block RHS (e.g. a ternary) can
  lower into a flow-capture that happens to share the target's syntax span")
  shows the shape was known on that side. The effect pipeline happens to be
  safe: every effect claim on a method with such an assignment became
  `Unknown` (`UnsupportedOperation`) in the probes, including a method that
  only returned the assigned value.
- **Failure scenario:** SP0027 is a Warning by default and is documented as a
  concrete, replayed precondition violation. `x = cond ? a : b;` and
  `ok = ok || check;` are extremely common, so correct code that later
  passes the local to a method with a `Requires` gets a definite-violation
  warning, which fails the build under `TreatWarningsAsErrors`. The user
  cannot fix the code, because it is correct, so they suppress SP0027 and
  lose the real reports. Any other consumer of `ManagedFlowResult` facts
  (for example, establishing a callee's `Requires` before importing its
  effect summary) sees the same stale value; today the effect scanner stops
  first, but nothing in `ManagedAbstractFlow` itself prevents a stale fact
  from discharging an obligation.
- **Suggested fix:** Recognize target captures generally: a flow capture
  whose value is a local, parameter or field reference and whose capture
  references are used as simple-assignment targets (Roslyn marks these by
  the capture's syntax being the assignment's left operand) should be
  resolved to the captured storage, the way `??=` targets already are.
  Generalize `CoalesceAssignmentFlowCaptures.IsRelevant` to any
  `AssignmentExpressionSyntax` whose `Left` contains the capture's syntax,
  or conservatively set the captured storage to `TopForType` whenever a
  capture reference is assigned. Add SP0027 regression tests for the six
  callers above (and keep `x += cond ? 1 : 2` silent), and a unit test that the
  managed flow value of `x` after `x = r ? 1 : 2` is `[1, 2]`.

### SP0027 never checks members set in object initializers, collection initializers or `with`, so `init` accessor preconditions are never checked anywhere

- **File:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs` (the
  initializer branch of the call-site descent, and
  `HasReplayableCallEvaluation`, which decides whether a discovered call can
  be replayed), with the replayable shapes listed in
  `docs/diagnostic-examples.md` (SP0027).
- **Confidence:** Confirmed with `SharpProofFeatures=contracts`. For the
  code below, only `A7` and `A8` (plain assignment statements) were reported;
  `A1` to `A6` and `B1` were silent, although every argument is a literal
  that violates the precondition. The constructor calls in `new Ctor(0)` and
  `new Ctor(0) { P = 1 }`, and an explicit `l.Add(0)`, were reported. On the
  effect side the same calls were safe: a `[DoesNotThrow]` caller of
  `new Div { Init = 0 }` got SP0047 `CallPreconditionNotProven`, so only the
  diagnostic is missing.
- **What is wrong:** Call-site discovery descends into
  `IObjectOrCollectionInitializerOperation` items, but the setter, `init`
  accessor, indexer setter and `Add` calls it finds there are never treated
  as replayable, presumably because their receiver is the implicit
  initializer receiver rather than an expression the replay can
  substitute. The documented replayable shapes ("direct top-level
  expression statements, returns, throws, single local initializers,
  simple assignments with definitely non-throwing targets, ...") do not
  mention initializer members, so the limitation is at best implicit. It
  matters most for `init` accessors: outside the type's own constructors
  and `init` accessors, C# only allows calling them from an object
  initializer or a `with` expression, so a `Contract.Requires` in an `init`
  accessor is never checked at any external call site.
- **Failure scenario:**

  ```csharp
  public sealed class Cfg
  {
      private int _v;
      public int Setter { get => _v; set { Contract.Requires(value > 0); _v = value; } }
      public int Init { get => _v; init { Contract.Requires(value > 0); _v = value; } }
      public int this[int i] { get => _v; set { Contract.Requires(i >= 0); _v = value; } }
      public Cfg? Child { get; set; }
  }
  public sealed record RecCfg
  {
      private readonly int _v;
      public int Init { get => _v; init { Contract.Requires(value > 0); _v = value; } }
  }
  public sealed class PosList : IEnumerable<int>
  {
      public void Add(int x) { Contract.Requires(x > 0); }
      // GetEnumerator members omitted
  }

  public static Cfg A1() => new Cfg { Setter = 0 };                          // silent
  public static Cfg A2() => new Cfg { Init = -1 };                           // silent
  public static Cfg A3() => new Cfg { [-1] = 5 };                            // silent
  public static Cfg A4() => new() { Init = 0 };                              // silent
  public static RecCfg A5(RecCfg r) => r with { Init = 0 };                  // silent
  public static Cfg A6() => new Cfg { Child = new Cfg { Setter = -3 } };     // silent
  public static void A7(Cfg c) { c.Setter = 0; }                             // SP0027
  public static void A8(Cfg c) { c[-1] = 5; }                                // SP0027
  public static PosList B1() => new PosList { 0 };                           // silent
  ```

  Object initializers are the usual way to configure options and DTO
  objects, and validated `init` properties are the natural place to put a
  precondition. A user who adds one gets no warning for literal violations
  anywhere, while the equivalent setter assignment statement is reported.
- **Suggested fix:** Treat each initializer member as a call site whose
  receiver is the object under construction (fresh and non-null), after the
  constructor and the preceding initializer members have completed, and
  replay it like the corresponding simple assignment or `Add` call. Handle
  the initializer of a `with` expression the same way, with the clone as
  the receiver. If that is not wanted, document in the SP0027 section that
  initializer members and `with` are never checked, and consider reporting
  a `Requires` inside an `init` accessor as unenforceable. Add regressions
  for each form above.

### Effect attributes on local functions are silently ignored by the analyzer

- **File:** `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs` (the
  effect syntax action is registered for `SimpleLambdaExpression` and
  `ParenthesizedLambdaExpression` only) and
  `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`
  (`AnalyzeLambdaEffects`; `AnalyzeOperationBlock` and
  `AnalyzeUnselectedOperationBlock` skip nested callables through
  `IsNestedCallable`).
- **Confidence:** Confirmed with `SharpProofFeatures=effects`. Five local
  functions, each annotated and each clearly violating its attribute
  (`[EnforcePure] static int L() => LLog8.Count++;`, the same without
  `static` and with a block body, `[DoesNotThrow] static int L(int x) => 10 / x;`,
  and `[ZeroAllocations] static int L() => new object().GetHashCode();`),
  produced no diagnostic at all. Two lambdas with the same attributes in
  the same file were reported (SP0002 and SP0046), although both messages
  name the method as `''` ("Method '' is marked [EnforcePure] ..."). The
  collector did create claims for all seven nested callables, and the
  worker reported each as `Unknown` (`UnsupportedCallable`).
- **What is wrong:** Roslyn does not give a local function its own
  operation block, so the operation-block path never sees it, and the
  syntax-node path that handles annotated nested callables is registered
  only for lambda syntax. `ValidateNestedCallableDeclaration` is registered
  for `LocalFunctionStatement`, but it validates attribute arguments, not
  effects. The result is that an explicitly selected local function gets
  neither an effect diagnostic nor the SP0047 that
  `docs/diagnostic-examples.md` promises for selected callables outside the
  supported subset, while the verifier treats the same annotation as an
  unsupported claim. For lambdas, the diagnostic text uses the compiler's
  empty lambda name instead of something the user can find.
- **Failure scenario:** A developer marks a local helper
  `[DoesNotThrow]` or `[EnforcePure]` and sees no warning in the IDE or an
  advisory build, so they assume it was checked. In a strict verification
  build the same annotation turns into an `Unknown` claim with
  `UnsupportedCallable`, which the analyzer never hinted at.
- **Suggested fix:** Register the nested-callable effect action for
  `LocalFunctionStatement` as well (the local function's body is available
  from the containing method's operation tree), or report SP0047
  `UnsupportedCallable` for annotated local functions so the analyzer and
  the worker agree. Use a readable name in diagnostics for anonymous
  functions (for example "lambda at line N in 'Containing'"). Add analyzer
  tests for annotated static and non-static local functions.

### The 16 MiB manifest cap stops verification of any project with a few thousand annotated methods

- **File:** `SharpProof.CompilerArtifact/CompilerManifestArtifact.cs`
  (`MaximumBytes = WorkerProtocolJson.MaximumJsonBytes`, and the
  "exceeds the worker input byte limit" check) with the limit defined in
  `SharpProof.Worker.Protocol/ProtocolJson.cs` (`MaximumJsonBytes = 16 * 1024 * 1024`).
- **Confidence:** Confirmed with the unmodified collector. For a class of
  `N` methods of the form
  `[EnforcePure] public static long Mi(long a) { if (a > i) { return a - 1; } return a + i; }`,
  the manifest was 609,501 bytes for `N = 100`, 2,181,498 for `N = 400` and
  8,493,631 for `N = 1,600` (about 5.3 KB per claim: roughly 3 KB of lowered
  callable, 1.3 KB of location authorities and 0.9 KB of claim manifest).
  At `N = 3,200` the collector reported
  `SP0049: SharpProof could not emit the final compiler manifest: JsonException: The compiler manifest exceeds the worker input byte limit.`
  and wrote no manifest.
- **What is wrong:** The whole project's claims, lowered callables, source
  location authorities and compilation evidence go into one JSON artifact,
  and that artifact is subject to the same 16 MiB cap as every worker
  protocol file. The cap therefore bounds the number of annotated methods
  per project at a few thousand even for tiny bodies (fewer for larger ones),
  and exceeding it is not a per-claim `Unknown` but an infrastructure
  failure that loses every claim. `docs/analysis-limits.md` documents the
  16 MiB cap for "worker request, result, and cache envelope JSON files",
  but not that it limits the size of a verifiable project.
- **Failure scenario:** A team adopts `[EnforcePure]` and `[DoesNotThrow]`
  across a large library. Once the count passes about 3,000 methods, every
  verification build fails with SP0049, and removing annotations from
  unrelated methods is the only workaround.
- **Suggested fix:** Give the compiler manifest its own, much larger limit
  (it is produced by the trusted collector, not read from an untrusted
  source), or shard it per syntax tree or per namespace with a small index,
  and reduce per-claim size (for example, share location authorities per
  file). At minimum, when the cap is hit, report how many claims and bytes
  were involved and document the practical limit next to the 16 MiB rule.

### Reading an auto-implemented property makes every effect claim unprovable

- **File:** `SharpProof.Effects/EffectMethodNodeBuilder.cs` (`Build`:
  when `GetOperationRoot` returns `null`, as it does for a semicolon-bodied
  accessor, the callee summary is
  `EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnsupportedOperation)`),
  with the selected-method side in
  `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs`
  (`IsConcreteSemicolonAccessor`).
- **Confidence:** Confirmed with `SharpProofFeatures=effects` and the
  collector and worker. `[EnforcePure] int A1(Person p) => p.Age;` where
  `Age { get; set; }` was reported (SP0002) and was `Unknown`
  (`EffectSummaryIncomplete`) in the worker, and so were a get-only
  auto-property on a `readonly struct`, an `init` auto-property, and the
  same read under `[DoesNotThrow]` (`ExceptionSetUnknown: DirectCall, UnsupportedOperation`)
  and `[ZeroAllocations]`. Reading a public field, an expression-bodied
  property over a field, or a record's positional property (whose accessor
  is compiler-synthesized) was `Proven`. Setters are affected the same way:
  `[EnforcePure] Dto A1() => new Dto { Age = 5 };` (an object initializer
  on a freshly created object) and an `init` initializer were reported,
  while `new Dto { Field = 5 }` was accepted.
- **What is wrong:** An auto-property accessor is a single read or write of
  its compiler-generated backing field, but because its declaration has a
  semicolon instead of a body, the analyzer has no operation tree for it and
  summarizes every call to it as unsupported. The record case shows that
  compiler-synthesized accessors are already handled; source
  `{ get; set; }` accessors, the most common kind of property in C#, are
  not. This is not listed among the documented subset limits.
- **Failure scenario:** Almost any method that touches a DTO, options
  object or entity (`order.Total`, `point.X`) cannot be proven pure,
  non-throwing or allocation-free, so users see SP0002/SP0046/SP0045 on
  code that obviously satisfies the contract and conclude the analyzer is
  unusable, or rewrite properties as fields to satisfy it.
- **Suggested fix:** Summarize a semicolon accessor of an auto-property as
  a read (getter) or write (setter or `init`) of the property's backing
  field (`IPropertySymbol` has an associated field; for `init` accessors
  and struct receivers use the same receiver region rules as field access),
  with no allocation and no exception beyond the receiver null check. Add
  analyzer and worker tests for class, struct, `init` and static
  auto-properties.

### `foreach` over an array or a string is analyzed as interface enumeration, so it can never be proven

- **File:** `SharpProof.Effects/EffectMethodNodeBuilder.cs` and
  `SharpProof.Effects/OperationEffectScanner.cs` (the CFG walk scans the
  implicit `GetEnumerator`/`MoveNext`/`Current`/`Dispose` operations that
  Roslyn's control-flow graph uses for every `foreach`), and
  `SharpProof.Effects/ExceptionHandlerReachability.cs` (`GetForEachStatementInfo`
  for enumerator members).
- **Confidence:** Confirmed. A helper
  `static int Sum(int[] a) { var s = 0; foreach (var x in a) { s = unchecked(s + x); } return s; }`
  made `[EnforcePure]`, `[ZeroAllocations]` and `[DoesNotThrow]` callers
  fail (`ExceptionSetUnknown: DirectCall, Dispatch`), and the worker
  reported them `Unknown` (`EffectSummaryIncomplete`); the same helper
  written with a `for` loop over `a.Length` was `Proven` for all three. A
  `foreach` over a `string` failed the same way. Roslyn's
  `GetForEachStatementInfo` reports `IEnumerable.GetEnumerator()`,
  `IEnumerator.MoveNext()`, `IEnumerator.Current` and `IDisposable.Dispose()`
  for an `int[]`, and `string.GetEnumerator()` (a `CharEnumerator`) for a
  `string`, and the control-flow graph Roslyn builds for the loop contains
  those calls plus an explicit `object`-to-`int` conversion of `Current`.
- **What is wrong:** For arrays and strings the C# compiler does not use
  the enumerator pattern at all; it emits an index loop over `Length` with
  no allocation, no interface call, no unboxing and no `Dispose`. The
  control-flow graph describes the language-level pattern instead, and the
  effect scanner analyzes it literally, so every array or string `foreach`
  looks like interface dispatch (unknown effects), an unboxing conversion
  and, for strings, an enumerator allocation.
- **Failure scenario:** `foreach` over an array is one of the most common
  loops in C#. Any helper that uses it makes every effect claim that
  reaches it unprovable, which pushes users to rewrite idiomatic loops as
  `for` loops just to satisfy the analyzer.
- **Suggested fix:** When the `foreach` collection's type is a
  single-dimensional array or `string` (and, if desired, `Span<T>` or
  `ReadOnlySpan<T>`), replace the enumerator operations with the index-loop
  semantics the compiler actually emits: a `NullReferenceException` check
  on the collection, bounds-safe element reads, no allocation and no
  `Dispose`. Keep the pattern-based model for every other collection type.
  Add tests that the `foreach` and `for` versions of the same helper get
  the same summary.

### The parameterless `InvalidOperationException` constructor spec omits resource lookup effects

- **File:** `SharpProof.Specs/DefaultApiSpecCatalog.json` (row
  `bcl.invalid-operation-exception.ctor`, profile
  `ObservedExceptionConstruction`) and the generated
  `SharpProof.Specs/DefaultApiSpecCatalog.generated.cs`
- **Confidence:** Medium
- **What is wrong:** The profile declares `WritesReceiverState` only,
  `Allocation: None`, `DoesNotThrow` and `Terminates`. That fits
  `Exception()`, `Exception(string)` and `InvalidOperationException(string)`,
  which only store fields. The parameterless `InvalidOperationException()`
  instead chains to `base(SR.Arg_InvalidOperationException)`. In CoreLib,
  `SR.GetResourceString` reads the `UseSystemResourceKeys` AppContext switch.
  It then enters a `Monitor` on a static lock, may allocate its
  recursion-tracking list and the resource string, and resolves the string
  through `ResourceManager` using `CultureInfo.CurrentUICulture` (ambient
  thread state). On .NET Framework the equivalent path is
  `Environment.GetResourceString`, which has the same properties.
- **Failure scenario:**
  `[EnforcePure] static int Check(int x) => x >= 0 ? x : throw new InvalidOperationException();`
  imports the spec row. The summary shows no ambient read, no synchronization
  capability and no allocation beyond the fresh exception, so purity (and any
  "no synchronization" or allocation contract) is proven. The exception's
  `Message` actually depends on the caller's UI culture, and the constructor
  takes a process-wide lock.
- **Suggested fix:** Give `M:System.InvalidOperationException.#ctor` its own
  profile that reads ambient state, allocates managed memory, and has the
  synchronization capability, or remove the row so that callers fall back to
  unknown effects. Audit other parameterless exception constructors before
  adding them, because most of them load a default message the same way.

### Generated-code detection deviates from Roslyn's header heuristic

- **File:** `SharpProof.Analyzer.Core/AnalyzerGeneratedCodePolicy.cs`
  (`HasGeneratedHeader`, `IsExactGeneratedHeader`)
- **Confidence:** Confirmed with `SharpProofFeatures=contracts`. A file
  whose header was the three-line `// <auto-generated>` ...
  `// </auto-generated>` comment reported SP0027 for a call that violates a
  precondition. The same file with `// <auto-generated/>` reported nothing.
- **What is wrong:** Only the first comment in the leading trivia is
  inspected, and it must be exactly `<auto-generated/>` or
  `<autogenerated/>` (with optional space). Roslyn's own heuristic
  (`GeneratedCodeUtilities`) accepts any leading comment that *starts with*
  `<auto-generated` or `<autogenerated`, including the common
  `// <auto-generated>` ... `// </auto-generated>` block, and scans past a
  preceding license header.
- **Failure scenario:** A resource or gRPC generated file beginning with
  `// <auto-generated>` (the form emitted by many generators, including this
  repository's own generator scripts) is treated as hand-written when no
  `generated_code` editorconfig value is set. SharpProof then reports
  selection, SP0047 and precondition diagnostics inside generated code that
  users cannot edit.
- **Suggested fix:** Mirror Roslyn's rule: scan all leading single-line and
  multi-line comments before the first token and accept a comment whose
  trimmed text starts with `<autogenerated` or `<auto-generated`
  (case-insensitive).

### Contract payload verification lets `BadImageFormatException` escape

- **File:** `SharpProof.Frontend/ContractApiIdentityResolver.cs`
  (`HasExpectedPayloadHash`)
- **Confidence:** Confirmed. In a scratch harness, the compilation referenced
  `SharpProof.Attributes` through `MetadataReference.CreateFromImage` with
  valid bytes, and its `filePath` pointed to a six-byte file starting with `MZ`.
  Running the analyzer with `SharpProofFeatures=contracts` raised an analyzer
  exception. The stack ran from `HasExpectedPayloadHash` (`PEReader.HasMetadata`)
  through `ContractApiIdentityResolver..ctor` and
  `ContractSelectionInventory` to `SharpProofAnalyzerEngine.ValidateContractForCompanions`.
  No SP0050 was reported, and the precondition violation in the same file was
  not reported either.
- **What is wrong:** The method opens the referenced
  `SharpProof.Attributes` file with `PEReader`, reads `HasMetadata`, and calls
  `GetMetadataReader()`. Both throw `BadImageFormatException` for a truncated
  or corrupted image, but the exception filter only catches
  `ArgumentException`, `IOException`, `NotSupportedException`,
  `UnauthorizedAccessException`, `SecurityException` and
  `CryptographicException`. The sibling `HasExpectedModuleVersionId` does
  catch `BadImageFormatException`.
- **Failure scenario:** In an IDE session the reference path on disk is
  replaced mid-restore by a partially written file while Roslyn still holds
  the old metadata. The constructor of `ContractApiIdentityResolver` (run via
  `ForCompilation`) throws, the analyzer fails with AD0001 for the whole
  compilation, and SP0050 ("contract API payload could not be read") is never
  reported, which is exactly the case that diagnostic exists for.
- **Suggested fix:** Add `BadImageFormatException` (and
  `InvalidOperationException`, which `PEReader` can also throw on some
  malformed headers) to the catch filter so the failure becomes an
  `unreadableReason`.

### Analyzer-configuration profile keys conflict with the package's MSBuild defaults

- **File:** `SharpProof.Analyzer.Core/Configuration/AnalyzerConfiguration.cs`
  (`ReadOptionAliases`, `GetInvalidConfigurationValues`) together with
  `SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props` and
  `SharpProof.Package/buildTransitive/SharpProof.props`
- **Confidence:** Confirmed. The scratch harness supplied the global options
  `build_property.SharpProofProfile = advisory` (what the package targets
  always emit) and `sharpproof_profile = strict`. The analyzer reported
  `SP0025 ... invalid value 'strict / advisory': configuration aliases disagree`
  and nothing else, so an `[EnforcePure]` method with a static write was not
  analyzed. With `sharpproof_profile = advisory`, SP0002 was reported for
  the same method.
- **What is wrong:** `SharpProof.ConsumerContract.props` always assigns
  `SharpProofProfile=advisory` and `SharpProofFeatures=all` when the project
  leaves them empty, and `SharpProof.props` makes both compiler-visible. As a
  result, every compilation's global options contain nonblank
  `build_property.SharpProofProfile` and `build_property.SharpProofFeatures`
  values. `ReadOptionAliases` treats `sharpproof_profile`,
  `build_property.sharpproof_profile` and `build_property.SharpProofProfile` as
  aliases and reports a conflict whenever two nonblank aliases differ. The
  per-tree check reports "option is compilation-global" whenever an
  `.editorconfig` value differs from the global alias value. The README and
  getting-started guide say that "the same choices can be made through
  analyzer configuration" with `sharpproof_profile`/`sharpproof_features`, but
  only the default values can be set that way.
- **Failure scenario:** A consumer follows the README and writes
  `sharpproof_profile = strict` in a `.globalconfig` (or under `[*.cs]` in
  `.editorconfig`) without touching MSBuild. The build reports
  "configuration aliases disagree" (or "option is compilation-global"), and
  `FromOptions` returns `SharpProofProfile.Off`, so the analysis the user tried
  to make stricter is turned off.
- **Suggested fix:** Distinguish a package default from an explicit value (for
  example, emit a separate `_SharpProofProfileIsDefault` compiler-visible
  property, or stop defaulting the property in MSBuild and apply the default
  in `FromOptions`). Alternatively, document that the analyzer-configuration
  keys are only for hosts that do not import the package targets.

### Same-named `file` types give colliding callable IDs and fail the manifest

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/SemanticClaimIdentity.cs`
  (`CreateCallableId`, and `CreateContainerId` for nested callables),
  `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`
  (`CreateCallableIds`)
- **Confidence:** Confirmed. Two syntax trees were run through the unmodified
  collector analyzers:

  ```csharp
  // A.cs
  file static class Helper
  {
      public static long Check(long p)
      {
          Contract.Ensures(Contract.Result<long>() == p);
          return p;
      }
  }

  // B.cs
  file static class Helper
  {
      public static long Check(long p)
      {
          Contract.Ensures(Contract.Result<long>() == p);
          return 7;
      }
  }
  ```

  The compilation is valid C# 11, but the collector reported
  `SP0049: SharpProof could not emit the final compiler manifest: JsonException: The compiler manifest artifact is invalid.`
  A first-chance trace showed the failure in
  `CompilerManifestArtifactJson.Validate`. A Roslyn probe showed that both
  methods have the documentation comment ID `M:Helper.Check(System.Int64)`,
  while their metadata type names differ
  (`<A>F49B...__Helper` and `<B>F1B6...__Helper`). Calls to two same-named
  `file` helpers from ordinary callables were handled correctly, because
  summaries are keyed by symbol.
- **What is wrong:** `CreateCallableId` returns
  `DocumentationCommentId.CreateDeclarationId(method)` whenever it is
  non-empty, and uses the `spm1:` structural hash only when there is no
  documentation ID. Documentation IDs do not encode file-local scope, so
  two `file` types with the same namespace-qualified name (and nested types or
  lambdas inside them, through `CreateContainerId`) produce identical callable
  IDs. The manifest validator then rejects the duplicates, and the whole
  compilation's manifest is lost.
- **Failure scenario:** Source generators and style guides use `file` types
  for private helpers with common names (`Helper`, `Extensions`, `Cache`).
  Once two files in a verified project each declare such a type with
  contract-bearing methods of the same signature, every verification build
  fails with SP0049, and the error does not mention the collision.
- **Suggested fix:** For symbols inside a file-local type (check
  `INamedTypeSymbol.IsFileLocal` up the containing-type chain), build the
  callable ID from the structural `spm1:` hash, or append a stable qualifier
  derived from the metadata name (which already includes a hash of the
  file path) or from the syntax tree's canonical path. Keep documentation IDs for everything
  else. Apply the same rule to container IDs and any other identity derived
  from `GetDocumentationCommentId` that must be unique within a compilation.
  Add a collector test with the two files above.

### Same-named members in different `extension` blocks, and function-pointer overloads, get one callable ID and fail the manifest

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/SemanticClaimIdentity.cs`
  (`CreateCallableId`, which prefers the documentation comment ID),
  checked by `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`
  and the manifest validator.
- **Confidence:** Confirmed with the minimum supported compiler (Roslyn 4.14)
  and `LangVersion=preview`. For a single extension block the callable IDs
  printed by the worker were `M:Ext..Twice~System.Int64`,
  `M:Ext..Flip~System.Boolean` and `M:Ext..Make~System.Int32`: the
  extension container contributes an empty name and the receiver does not
  appear at all. The compilation below, which is valid C#, made the
  collector report
  `SP0049: SharpProof could not emit the final compiler manifest: JsonException: The compiler manifest artifact is invalid.`
  Renaming one member (`Thrice`), or moving one of them to an ordinary
  static method, produced a manifest and two `Proven` claims. Overloads
  that differ only in a function-pointer parameter fail the same way:
  `M(delegate* managed<int> f)` and `M(delegate* unmanaged[Cdecl]<int> f)`,
  `unmanaged[Cdecl]` and `unmanaged[Stdcall]`, or `delegate*<int>` and
  `delegate*<long>` each gave SP0049 (with `AllowUnsafeBlocks`). Roslyn
  4.14 prints the documentation ID `M:Fp.M()~System.Int32` for all of these;
  the function-pointer parameter is dropped entirely.
- **What is wrong:** This is the same defect as the `file` type entry above,
  reached through a different construct. `CreateCallableId` uses the
  documentation comment ID when there is one, and for an extension-block
  member that ID names the enclosing static class, an empty container
  segment, the member name and (for methods) its return type, but not the
  receiver type or the block. Two blocks that extend different types with
  a member of the same name and parameter list therefore produce identical
  IDs, the manifest validator rejects the duplicates, and the whole
  compilation's manifest is lost. Extension blocks are the C# 14 way to
  write extension members, and overloading one member name across several
  receiver types (`int`, `long`, `double`) is their usual shape. Function
  pointer parameters are omitted from the documentation ID, so any two
  overloads that differ only there collide as well; these callables are
  outside the supported subset (`UnsupportedCallable`), but an annotation
  on them still takes down the manifest for the whole project.
- **Failure scenario:**

  ```csharp
  public static class Ext
  {
      extension(int x)
      {
          [EnforcePure] public long Twice() => 2L * x;
      }
      extension(long x)
      {
          [EnforcePure] public long Twice() => 2 * x;
      }
  }
  ```

  Every verification build of a project containing this fails with SP0049,
  and the message does not say which symbols collided. The same happens for
  properties (`public long Bump { [EnforcePure] get => ...; }` in both
  blocks). Static members with the same name and parameters cannot collide
  this way, because the compiler already rejects them (CS0111).
- **Suggested fix:** Do not use documentation comment IDs as callable IDs
  for extension-block members (detect them through the containing type's
  extension marker), or append the receiver parameter's type and a stable
  block ordinal. More robustly, check the documentation ID for uniqueness
  within the compilation and fall back to the structural `spm1:` ID for
  every colliding symbol, which would also cover the `file` type case and
  any future construct with the same weakness. Add a collector regression
  with the two blocks above.

### Effect-annotated lambdas and local functions without `Contract.` text share one callable ID

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/ClaimManifestBuilder.cs`
  (`CreateCallableIds`), `SharpProof.CompilerCollector/CompilerArtifact/SemanticClaimIdentity.cs`
  (`CreateNestedCallableId`)
- **Confidence:** Confirmed with the unmodified collector analyzers. Each of
  these compilations reported
  `SP0049: SharpProof could not emit the final compiler manifest: JsonException: The compiler manifest artifact is invalid.`

  ```csharp
  public static int TwoLambdas()
  {
      Func<int, int> a = [DoesNotThrow] static (int x) => x;
      Func<int, int> b = [DoesNotThrow] static (int x) => 10 / x;
      return a(1) + b(1);
  }

  public static int SiblingLocals(bool flag)
  {
      if (flag)
      {
          [DoesNotThrow] static int L(int x) => x;
          return L(1);
      }
      else
      {
          [DoesNotThrow] static int L(int x) => 10 / x;
          return L(0);
      }
  }
  ```

  Controls emitted a manifest: the same two lambdas with different parameter
  types (`int` and `long`), and the same two lambdas with a
  `Contract.Requires` in each body.
- **What is wrong:** `CreateCallableIds` assigns sibling ordinals to nested
  callables only when `seed.Declaration.ToString()` contains the text
  `"Contract."` ("callables without contract clauses do not participate").
  Effect-only claims (`[DoesNotThrow]`, `[EnforcePure]`, `[ZeroAllocations]`
  and so on) do not need that text, so both nested callables fall back to the
  "neutral" ordinal 0. `CreateNestedCallableId` hashes the parent ID, the
  ordinal and the method shape, which are identical for same-signature
  lambdas or same-named local functions, so both claims get the same `spm1:`
  ID and manifest validation rejects the whole artifact. The text check is
  also fragile in the other direction: `Contract.` inside a comment or
  string literal gives a callable an ordinal and renumbers its siblings.
- **Failure scenario:** A method builds two `Func<int, int>` validators with
  `[DoesNotThrow]` lambda attributes (C# 10), or has two same-named local
  helpers in different branches. Every verification build of the project
  fails with SP0049 until one of them is renamed, retyped, or given a dummy
  contract.
- **Suggested fix:** Assign sibling ordinals to every nested callable that
  owns any claim (contract clauses or effect attributes), using the semantic
  clause inventory rather than a text search. Alternatively, include the
  declaration's syntax-tree ordinal and span start in the nested identity
  when the shape hash is otherwise ambiguous. Add collector tests for the two
  shapes above.

### A nondeterministic effect contract silently demands the `Randomness` capability

- **File:** `SharpProof.Effects/ExternalEffectResolver.cs` (the
  `|| !deterministic` branch that sets `EffectCapabilityKind.Randomness`, and
  the `SpecEffect.Nondeterminism` branch), with the
  `UsesNondeterminism` to `Randomness` row in
  `SharpProof.Effects/EffectContractMappings.generated.cs`
- **Confidence:** Confirmed, including against the repository's own sample.
  Running the unmodified analyzer over `samples/TrustedBoundary/TrustedBoundaryExamples.cs`
  (with `SharpProofFeatures=all` and `Nullable=enable`, as that project
  builds) reports
  `SP0016 ... could not be capability-verified: may-effect summary includes disallowed capabilities: Randomness`,
  although the boundary declares `Capabilities = SharpProofCapability.NativeInterop`
  and the caller allows exactly that. Scratch probes isolated the cause:
  the same declaration with `IsDeterministic = true` is silent, and the
  original declaration is silent once the caller also allows
  `SharpProofCapability.Randomness`. A boundary declaring
  `Capabilities = SharpProofCapability.Clock` whose caller allows `Clock` is
  reported the same way. Worse, `[EnforcePure]` on a caller of an `extern`
  boundary declared `[EffectContract(SharpProofEffect.None, Complete = true)]`
  reports SP0002, while the identical declaration with
  `IsDeterministic = true` is accepted.
- **What is wrong:** `ExternalEffectResolver` adds
  `EffectCapabilityKind.Randomness` whenever the declared summary is not
  deterministic: `if ((effects & EffectContractKind.UsesNondeterminism) != 0 || !deterministic)`.
  Nondeterminism and the randomness capability are different things, and the
  capability enum already distinguishes `Clock`, `Environment`, `Process`,
  `NativeInterop` and `Randomness`. Because `IsDeterministic` defaults to
  `false` (documented in `docs/public-api.md` as one of the conservative
  attribute defaults), every declaration that does not explicitly opt into
  determinism acquires a capability its author never wrote, and no
  `[AllowedCapabilities]` list can discharge it unless it names `Randomness`.
  A capability allowlist is a review artifact, so this also weakens its
  meaning: a reviewer sees `Randomness` granted on a method that only reads
  the clock or a process id.
- **Failure scenario:** The shipped `TrustedBoundary` sample is the scenario.
  Its `getpid` boundary is correctly declared nondeterministic with
  `Capabilities = NativeInterop`, and its caller allows `NativeInterop`, yet
  the analyzer reports SP0016 naming `Randomness`. The project still builds
  because SP0002 and SP0016 are `Info`, so the README's "Build succeeds"
  holds, but the feature the sample exists to demonstrate does not verify,
  and a consumer following it cannot discharge the capability contract
  without granting a capability the boundary does not use. Under a profile
  that raises these diagnostics, the same code stops building.
- **Suggested fix:** Do not derive a capability from the determinism flag.
  Map only the explicit `SharpProofEffect.UsesNondeterminism` flag, and give
  it its own `EffectCapabilityKind` (or treat nondeterminism as a summary
  property that capability contracts ignore) rather than reusing
  `Randomness`. Keep `IsDeterministic` for the determinism facet that
  postcondition and caching reasoning needs. Add analyzer tests for a
  nondeterministic `NativeInterop` boundary and a `Clock` boundary whose
  callers allow exactly the declared capability, and re-check the
  `TrustedBoundary` sample so it verifies cleanly.

### A refuted contract fails the build with an unlocated, uncoded error

- **File:** `SharpProof.Worker.Launcher/Program.cs` (the per-claim
  `Console.WriteLine("SharpProof " + result.Outcome + ...)` loop and the
  `refuted ? 5 : ...` exit code), `SharpProof.Host/VerifierDiagnosticTransport.cs`
  (`Validate`, which only admits SP0047 and SP0048),
  `SharpProof.BuildTasks/RunVerifier.cs` (`LogStandardError`, and stdout
  logged with `Log.LogMessage`) and
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets` (the
  generic `<Error Text="SharpProof verifier failed with exit code ..."/>`)
- **Confidence:** High (from the code; not run through MSBuild in a
  container).
- **What is wrong:** When a claim is `Refuted`, the launcher writes one plain
  stdout line of the form `SharpProof Refuted <callable id> Postcondition claim <claim id>`
  and exits with code 5. `RunVerifier` logs stdout lines as
  high-importance *messages*, not as errors or warnings. The structured
  diagnostic transport, which is what produces located MSBuild errors with a
  code, a file and a line, accepts only SP0047 (incomplete coverage) and
  SP0048 (assumptions), so nothing located is ever emitted for a refutation.
  Because no structured error was logged, the targets fall back to
  `SharpProof verifier failed with exit code 5.`, which has no code, file or
  line. The counterexample model is not printed at all. The published SARIF
  does contain a located `error` result for the claim, with the model in its
  property bag, and so does the JSON result, but MSBuild and the IDE error
  list read neither.
- **Failure scenario:** A developer's `Contract.Ensures(Contract.Result<long>() >= 0)`
  is refuted with the model `p = -1`. The IDE error list and the MSBuild
  error summary show only "SharpProof verifier failed with exit code 5."
  Double-clicking does nothing, the message does not name the method, the
  clause or the failing input, and the error cannot be filtered or
  suppressed by a diagnostic ID. The informational outcomes (incomplete
  coverage and declared assumptions) are the only verifier results that get
  a clickable, coded diagnostic, so the most important result is the least
  actionable.
- **Suggested fix:** Add a dedicated verifier diagnostic code for refuted
  claims (and, for effect claims, for the replayed violation witness), admit
  it in `VerifierDiagnosticTransport.Validate`, and have the launcher emit one
  structured diagnostic per refuted claim at the claim's manifest location,
  with a message that names the clause and prints the replayed model (for
  example `p = -1`). Keep exit code 5, but let the targets treat it as a
  structured semantic failure (as they already do for exit code 6) so the
  generic "failed with exit code" error is not added on top. Document the
  code in `docs/diagnostic-examples.md`.

## P3 - Low

### `new Nullable<T>()` is classified as definitely non-null

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs`
  (`EvaluateBounded`, `DefiniteOperationFacts.IsDefinitelyNonNull`)
- **Confidence:** Low. Not reproduced. A callee
  `static int Callee() => new int?() ?? Compute();` already makes a selected
  `[EnforcePure]` caller report SP0002, because the `Nullable<int>` creation is
  not modeled. A selected method that uses `new int?()` is rejected by the
  subset gate (SP0047). The misclassification is real in the code but is
  currently masked.
- **What is wrong:** Both helpers classify every `IObjectCreationOperation`
  as non-null. `new int?()` (or `new Nullable<T>()`) produces a nullable
  value without a value; boxing it yields `null`, and `?? fallback` evaluates
  the fallback.
- **Failure scenario:** `object boxed = new int?(); _ = boxed.GetHashCode();`
  is treated as a proven non-null receiver, so the `NullReferenceException` is
  omitted, and `var y = new int?() ?? Compute();` skips `Compute()`'s effects
  in `ScanCoalesce` because the value is "proven non-null".
- **Suggested fix:** Exclude creations whose type is `System.Nullable<T>`
  with no constructor arguments (return `Null`), or treat all nullable-typed
  creations as `MaybeNull` unless an argument is supplied.

### Companion discovery enumerates every type in every referenced assembly per compilation

- **File:** `SharpProof.Contracts/ContractForSymbolMatcher.cs`
  (`DiscoverCompanionRelationships`), `SharpProof.Frontend/ReferencedTypeSymbols.cs`
- **Confidence:** High (performance, not correctness)
- **What is wrong:** When the attributes package is referenced,
  `EffectiveContractSourceResolver.ForCompilation` calls
  `DiscoverCompanions`, which walks `ReferencedTypeSymbols.GetAllCached` (all
  named types in the source assembly and in every referenced assembly,
  including the full framework reference pack) and decodes custom attributes
  on each one. The cache is a `ConditionalWeakTable` keyed by `Compilation`,
  so the IDE repeats the whole walk after every edit because each edit
  produces a new `Compilation`.
- **Failure scenario:** A solution referencing the framework plus a few large
  packages has well over 50,000 metadata types. Each keystroke that triggers
  analyzer execution re-decodes attributes on all of them before any contract
  can be bound, which shows up as analyzer latency and memory churn in Visual
  Studio.
- **Suggested fix:** Limit the scan to assemblies that reference
  `SharpProof.Attributes` (a type can only carry `[ContractFor]` if its
  assembly references the attributes assembly), and cache per-assembly results
  in a `ConditionalWeakTable<IAssemblySymbol, ...>` so metadata assemblies
  shared across compilations are scanned once.

### Disposing the SMT backend can fault a racing check with `ObjectDisposedException`

- **File:** `SharpProof.Smt/IrSmtBackend.cs` (`Dispose`, `CheckSerializedAsync`)
- **Confidence:** Low. The stronger hang first suspected here did not
  reproduce. In a scratch probe on .NET 9, a check was running,
  a second check was queued on `WaitAsync`, and `Dispose` then started. The
  queued check acquired the gate as soon as the first one finished, ran to
  completion, and only then did `Dispose` proceed. The narrow race below remains.
- **What is wrong:** `CheckAsync` tests `_disposeStarted` and then calls
  `CheckSerializedAsync`, which awaits `_queryGate.WaitAsync`. `Dispose`
  sets `_disposeStarted`, takes the gate with a synchronous `Wait()`, never
  releases it, and finally disposes the semaphore. A caller that passes the
  `_disposeStarted` check just before `Dispose` runs, but reaches `WaitAsync`
  after `_queryGate.Dispose()`, gets `ObjectDisposedException` instead of
  the documented `Unknown(Unavailable)` result. `CallableVerifier` does not
  catch that exception type around the kernel call.
- **Failure scenario:** Shutdown or lane renewal disposes a backend while
  another thread is issuing its first query on the same instance. The
  query faults instead of abstaining, and the callable is reported as an
  infrastructure failure rather than an unavailable backend.
- **Suggested fix:** Use a disposal `CancellationTokenSource` linked into
  every `WaitAsync`, cancel it in `Dispose` before waiting, and translate that
  cancellation into `Unknown(Unavailable)`. Alternatively release the gate after
  marking `_disposed = true` and never dispose the semaphore while waiters can
  exist.

### A duplicated implementation reference turns an IL summary into SP0049

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerManifestArtifactProducer.cs`
  (`BuildSummaryEvidence`, the `ImplementationIl` branch),
  `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
  (`MetadataResolutionContext.GetReferenceModules`) and
  `SharpProof.CompilerCollector/CompilerArtifact/CompilerCompilationCapture.cs`
  (`CaptureReferences`)
- **Confidence:** Confirmed through `CompilationWithAnalyzers`, not through
  MSBuild. The compilation referenced the .NET 9 reference pack plus a
  separately compiled `Lib.dll` containing
  `public static int Max(int a, int b) => a >= b ? a : b;`. With one `Lib.dll`
  reference, the collector emitted a manifest, and the worker proved
  `Contract.Ensures(Contract.Result<int>() >= a && Contract.Result<int>() >= b); return Lib.Max(a, b);`
  with the core `il-summary:M:Lib.Max(System.Int32,System.Int32)`. When the same
  `Lib.dll` was referenced twice (the same path twice, or the original plus a
  byte-identical copy at another path), the compilation had no errors but the
  collector reported only
  `SP0049: SharpProof could not emit the final compiler manifest: InvalidOperationException: An IL summary authority is not bound to one captured module.`
  The same failure happened for `Math.Max(int, int)` when
  `System.Private.CoreLib.dll` itself was listed twice.
  Whether MSBuild's reference resolution and package conflict handling can
  still pass such a duplicate to `Csc` was not tested, so real-build
  reachability is uncertain.
- **What is wrong:** The lowering and publication sides use different ideas of
  "one module". `GetReferenceModules` only counts references for which
  `Compilation.GetAssemblyOrModuleSymbol` returns an assembly. Roslyn merges
  duplicate references and returns a symbol for only one of them, so the
  lowerer sees one match and builds the IL summary. `CaptureReferences`
  records every entry of `compilation.References`, including duplicates, and
  `BuildSummaryEvidence` groups the captured modules by `(Name, Sha256)` and
  throws unless exactly one module matches. The summary was admitted, but its
  evidence can never be published, and the exception fails the whole
  manifest instead of only that summary.
- **Failure scenario:** A build host (a custom build, a test harness, or an
  MSBuild customization that appends `ReferencePath` items) passes the same
  implementation assembly twice. Any verified method that calls a
  summarizable method from that assembly makes the collector report SP0049,
  and no claim in the project is verified. A project that never calls into
  that assembly builds normally, which makes the cause hard to find.
- **Suggested fix:** Match modules the same way on both sides. Either skip
  captured references that Roslyn merged away (no assembly symbol) when
  building `modulesByIdentity`, or accept several captured modules with the
  same name and SHA-256, since their bytes are identical, and pick one
  deterministically (for example the lowest ordinal path). Alternatively,
  make `GetReferenceModules` count raw references as the producer does, so the
  lowerer abstains with `ReferenceUnavailable` instead of emitting evidence
  that cannot be bound. Add a collector test with a duplicated library
  reference.

### Implementation-IL proofs silently assume the compile-time library binary is the one that runs

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerImplementationIlSummaryLowerer.cs`
  (`TryBuild`), `SEMANTICS.md` (implementation-IL admissibility), and the
  proof-core and SARIF projection that shows `il-summary:` cores
- **Confidence:** Medium (trust-boundary gap rather than a coding error). The
  collector does what `SEMANTICS.md` describes. A scratch run proved
  `Contract.Ensures(Contract.Result<int>() >= a && Contract.Result<int>() >= b); return Lib.Max(a, b);`
  with the core `il-summary:M:Lib.Max(System.Int32,System.Int32)` from the
  referenced `Lib.dll`. Nothing in the result, the documentation, or the build
  checks the binary that the application loads at run time.
- **What is wrong:** An implementation-IL summary uses the method body of the
  file Roslyn compiled against as proof authority. For NuGet packages and
  `netstandard2.0` project references, that file is often not the one
  deployed. NuGet version unification can pick a newer transitive version,
  binding redirects or `runtimeconfig` roll-forward can substitute another
  build, a plugin host can load its own copy, and a package can ship
  different `lib/` assets per target framework than the asset used for
  compilation. `SEMANTICS.md` describes the module hash only as provenance
  ("not a runtime compatibility gate") and never states that `Proven` results
  depending on `il-summary:` evidence hold only for that exact binary. A proof
  about the caller is presented like one whose dependencies are all in the
  current compilation.
- **Failure scenario:** A project references `Lib` 1.0.0, where
  `Lib.Clamp(int)` returns a value in `[0, 100]`, and SharpProof proves a
  caller's postcondition through the IL summary. Another dependency needs
  `Lib` 1.1.0, whose `Clamp` returns `-1` for invalid input, so NuGet
  unifies to 1.1.0 at restore and publish. The deployed application violates
  the "proven" postcondition. The build output has no hint beyond the
  `il-summary:` core ID.
- **Suggested fix:** Document the assumption explicitly in `SEMANTICS.md` and
  in the diagnostics or SARIF for claims whose core includes `il-summary:`
  (for example as a visible trust assumption similar to SP0048). Optionally,
  admit IL summaries only for assemblies that are copied to the output from
  the same path and hash (compare against `ReferenceCopyLocalPaths` or the
  `deps.json` runtime assets at publish time), or add a policy switch that
  treats implementation-IL evidence as an assumption that must be allowed.

### Atomic file publication is neither race-free nor durable

- **File:** `SharpProof.Ir/AtomicFile.cs` (`PublishStaged`, `WriteUtf8`,
  `WriteBytesAsync`)
- **Confidence:** Medium
- **What is wrong:** `PublishStaged` checks `File.Exists(destination)` and then
  calls either `File.Replace` or `File.Move`. If another process creates the
  destination between the check and the move, `File.Move` throws
  `IOException`; if it deletes the destination between the check and the
  replace, `File.Replace` throws `FileNotFoundException`. `WriteUtf8` and
  `WriteBytesAsync` never flush the staged file to disk (`Flush(true)`) before
  the rename, unlike `WriteStagedBytes`, so a crash after the rename can leave
  a zero-length or truncated destination on file systems with delayed
  allocation.
- **Failure scenario:** The launcher writes a response file with
  `WriteUtf8Async` while a concurrent retry of the same build removes the
  stale response; the publish throws and the build reports an infrastructure
  failure. Separately, a container killed right after publication leaves an
  empty worker result file that later fails validation as malformed instead of
  being absent.
- **Suggested fix:** `SharpProof.Ir` targets netstandard2.0, so
  `File.Move(..., overwrite: true)` is not available there. Try `File.Replace`
  first and fall back to `File.Move` on `FileNotFoundException`, and retry
  `File.Replace` when `File.Move` fails because the destination appeared (or
  provide a net9-only overload for the worker and launcher that uses the
  atomic overwrite move). Flush the staged stream with `Flush(true)` in every
  writer before publishing.

### Effect claims on callables that failed lowering assume a feasible entry

- **File:** `SharpProof.Worker/CallableVerificationPolicy.cs`
  (`FailedLowering`)
- **Confidence:** Low. The code path is as described, but it was not
  reachable in end-to-end probes (collector, worker and Z3 on C# source).
  `[ZeroAllocations] static Thing2 M(long v) => new Thing2();` was `Refuted`.
  Adding contradictory closed preconditions
  (`[Positive][InRange(-5, -1)] long v`) made the compiler effect summary
  incomplete, so the claim was `Unknown(EffectSummaryIncomplete)` whether or
  not the body lowered. `Contract.Requires` statements rule out the
  single-expression shape that produces replayable witnesses. Treat this as
  a latent inconsistency that becomes live if either restriction is relaxed.
- **What is wrong:** `CompilerManifestArtifactProducer.AttachEffectEvidence`
  attaches effect-claim evidence to every callable, including those whose
  lowering failed, and `CompilerLoweredArtifact` decodes it for failed
  callables too. `FailedLowering` then assembles each effect claim with
  `CallableEntryFeasibility.Feasible`, although no precondition was
  evaluated. For a callable that lowered successfully, the same evidence goes
  through `CallableEntryFeasibilityEvaluator`: contradictory preconditions
  produce a vacuous `Proven` (`VacuousEntry`), and unsupported preconditions
  produce `Unknown`. A body that fails to lower therefore gets a stronger
  verdict than one that lowers.
- **Failure scenario:** A method with an effect contract forbidding
  allocation, a `Contract.Requires` that cannot be lowered (or is
  contradictory), and a body construct the collector cannot lower, performs an
  unconditional `new object()`. The worker reports the effect claim as
  `Refuted` with a replayed witness. Changing only the unrelated body construct
  so that lowering succeeds turns the verdict into `Unknown` (unsupported
  precondition) or a vacuous `Proven` (contradictory precondition).
- **Suggested fix:** In `FailedLowering`, pass
  `CallableEntryFeasibility.Unknown(target.FailureReason)` when the callable
  declares any `Requires` (or unconditionally, when clauses are unavailable),
  or keep only `Proven` effect verdicts and downgrade `Refuted` ones to
  `Unknown` until entry feasibility can be established.

### The worker start-gate timeout cannot fire

- **File:** `SharpProof.Worker/Program.cs` (`WaitForStartAsync`)
- **Confidence:** Medium
- **What is wrong:** The worker waits for `LinuxWorkerProcess.StartMessage`
  with `Console.In.ReadLineAsync(timeoutBoundary.Token)` and races it against a
  30-second `Task.Delay`. `Console.In` is a synchronized `SyncTextReader`, whose
  `ReadLineAsync(CancellationToken)` checks the token once and then calls the
  blocking `ReadLine()` synchronously on the calling thread. The `ReadLineAsync`
  call does not return until a line or end of file arrives, so the delay task is
  only created after the read has already finished, and the timeout never
  applies.
- **Failure scenario:** A parent starts the worker with redirected standard
  input and then stalls (for example, it is suspended or deadlocked) without
  writing the start line or closing the pipe. The worker blocks in `Main`
  forever instead of exiting with code 125 after 30 seconds. The parent-death
  signal only helps if the parent actually exits.
- **Suggested fix:** Read from `Console.OpenStandardInput()` with a
  `StreamReader` and `ReadLineAsync(token)` (a stream-backed reader honors
  cancellation), or run the blocking read on a dedicated thread and race that
  task against the delay.

### Mount lookup picks the shadowed mount when several mounts share a mount point

- **File:** `SharpProof.Host/LinuxPathIdentity.cs`
  (`MountInfoSnapshot.FindFileSystemType`)
- **Confidence:** Medium
- **What is wrong:** The longest matching mount point wins, but ties are
  resolved with `mount.Path.Length <= bestMount.Length` → `continue`, which
  keeps the *first* entry in `/proc/self/mountinfo`. When several file systems
  are mounted on the same directory, the kernel lists them in mount order and
  only the last one is visible, so the check inspects the hidden file system.
- **Failure scenario:** A CI container bind-mounts an NFS or 9p/virtiofs share
  over an existing ext4 or overlay directory used for publication or the cache.
  `RequireLocalPath` reports the underlying `ext4`/`overlay` type and accepts a
  file system whose `flock`, atomic rename and directory `fsync` guarantees
  are exactly what `SupportedLocalFileSystems` is meant to exclude. The
  opposite stacking rejects a genuinely local tmpfs mounted over an NFS
  directory.
- **Suggested fix:** Use `<` instead of `<=` so later entries with the same
  mount point replace earlier ones, or, more robustly, compare the path's
  `st_dev` with the device numbers in field 3 of each mountinfo entry.

### Advisory profile activates full analysis for any attribute

- **File:** `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`
  (`GetAdvisoryActivation`)
- **Confidence:** High
- **What is wrong:** The advisory activation scan returns
  `AdvisoryActivation.Full` as soon as it sees any `AttributeSyntax` that is not
  an assembly or module attribute. It does not check that the attribute name
  could bind to a SharpProof control attribute. `MayContainAdvisoryActivationSyntax`
  also pre-selects every tree containing `[`, which includes array types,
  indexers and collection expressions.
- **Failure scenario:** Almost every real project contains `[Serializable]`,
  `[Fact]`, `[HttpGet]`, `[JsonPropertyName]` or similar attributes, so the
  advisory profile runs full symbol and operation analysis for the whole
  compilation even when SharpProof is never referenced in source. The
  `Lightweight` and `None` activations are effectively unreachable, and IDE
  performance for projects that only install the package is the same as full
  enforcement.
- **Suggested fix:** Only activate for attributes whose simple name (after
  removing the `Attribute` suffix and decoding Unicode escapes) matches a name
  in the SharpProof attribute catalog, or resolve the attribute type through
  the semantic model before choosing `Full`.

### Reference-family classification uses unanchored path substrings

- **File:** `SharpProof.Effects/ApiSpecResolution.cs`
  (`ClassifyReferenceFamily`) and
  `SharpProof.Effects/EffectContractMappings.generated.cs`
  (`ReferenceFamilyMarkers`)
- **Confidence:** Low
- **What is wrong:** A spec row whose approved identity carries a reference
  family is only admitted when the metadata reference path contains a family
  marker anywhere, compared case-insensitively. Three markers are not
  terminated with `/` (`/REFERENCEPACKS/NETSTANDARD`,
  `/PACKAGES/MICROSOFT.NETFRAMEWORK.REFERENCEASSEMBLIES`,
  `/REFERENCEPACKS/NET47`), so they also match unrelated directories such as
  `/packages/microsoft.netframework.referenceassemblies.fork/` or
  `/referencepacks/net472-custom/`. None of the markers is anchored to a
  package root, and neither the package version nor the assembly version is
  checked. The identity check it relies on compares only name and public key
  token, which Roslyn does not verify cryptographically.
- **Failure scenario:** A referenced `System.Runtime.dll` built with the
  Microsoft public key token and `ReferenceAssemblyAttribute`, placed under any
  directory whose path contains one of the markers, is classified as the
  trusted reference family. Spec rows describing the real framework behavior are
  then applied to calls into that assembly.
- **Suggested fix:** Terminate every marker with `/`, and match it against the
  resolved NuGet package root or SDK packs directory rather than any
  substring. Record and check the expected package id and version
  range for each family.

### SARIF results pair non-`fail` kinds with warning or error levels

- **File:** `SharpProof.Worker.Launcher/SarifProjection.cs` (`ClaimResult`,
  `IncompleteResult`)
- **Confidence:** Medium
- **What is wrong:** Unknown claims and incomplete callables are emitted with
  `kind: "review"` and `level` from `LauncherPresentation.Level`, which is
  `note`, `warning` (warn-on-unknown) or `error` (require-proven). SARIF 2.1.0
  (`result.kind`/`result.level`, sections 3.27.9 and 3.27.10) requires `level`
  to be absent or `"none"` whenever `kind` is anything other than `"fail"`. The
  published SARIF therefore violates the schema's normative rules exactly in
  the policies where the result matters.
- **Failure scenario:** A project uses `SharpProofVerifyPolicy=require-proven`
  and uploads the published SARIF to a code-scanning service or validates it
  with the SARIF multitool. Unknown claims are either rejected as invalid
  results or shown inconsistently: some consumers ignore `kind` and show
  errors, while others honor `kind: "review"` and drop the level.
- **Suggested fix:** Use `kind: "fail"` with the policy-derived level when the
  policy makes unknowns warnings or errors, and keep `kind: "review"` only with
  `level: "none"` (or omit `level`) for the advisory policy. Add a schema-level
  test that checks the `kind`/`level` invariant for each policy.

### A project timeout fails the build even under the `advisory` verify policy

- **File:** `SharpProof.Worker.Launcher/Program.cs` (the
  `if (response.RunStatus != WorkerRunStatus.Complete) return LauncherPresentation.ExitCode(...)`
  branch), `SharpProof.Worker.Launcher/LauncherProjections.generated.cs`
  (`ExitCode`, which maps `TimedOut` to 124) and
  `SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets` (the
  error raised for any nonzero exit code without a structured error)
- **Confidence:** Medium (from the code and documentation; not run through
  MSBuild in a container). The fuzz harnesses did hit `run=TimedOut` for
  40-method projects with nonlinear arithmetic under the default 300-second
  project budget, so the status is reachable in ordinary use.
- **What is wrong:** `docs/analysis-limits.md` describes
  `SharpProofVerifyPolicy` as the policy for *incomplete* selected analysis
  (`advisory`, `warn-on-unknown`, `require-proven`), and says that only
  malformed output, backend or replay failure, containment failure and
  infrastructure failure "make the run `Failed` and fail the build under
  every policy". A project timeout is listed separately as run status
  `TimedOut`. The launcher nevertheless returns exit code 124 for every
  run that is not `Complete`, before it looks at the policy, and the targets
  turn any nonzero exit code that is not accompanied by a structured error
  into `SharpProof verifier failed with exit code 124.` So a verification
  run that simply ran out of wall time fails the build under `advisory`
  exactly as an infrastructure failure would, and the error does not say
  that the cause was the time budget.
- **Failure scenario:** A team enables `SharpProofVerify=true` with the
  default `advisory` policy to get informational proofs. As the codebase
  grows past what the worker can finish in 300 seconds, every build starts
  failing with "SharpProof verifier failed with exit code 124", although no
  contract was refuted and the policy they chose treats incomplete analysis
  as information.
- **Suggested fix:** Decide the intended behavior and make code and
  documentation agree. If a timeout is an incomplete analysis, have the
  launcher report the remaining claims as `Unknown(ProjectTimeout)` with the
  policy's severity (SP0047) and exit 0, 5 or 6 as for a completed run. If it
  is meant to be fatal, say so next to the list of fatal statuses in
  `docs/analysis-limits.md`, and emit a structured, coded diagnostic that
  names the project time budget and the setting that raises it
  (`SharpProofVerifyProjectWallTimeMilliseconds`).

### Proofs made vacuous by `Contract.Assume` report `vacuity=None`

- **File:** `SharpProof.Worker/CallableVerifier.cs` (vacuity selection after
  `_kernel.VerifyAsync`), `SharpProof.Worker/CallableClaimResultAssembler.cs`
  (`Contradictory`, `FromOutcome`)
- **Confidence:** Confirmed. The unmodified collector and worker (bundled Z3)
  were run on the methods below. The contradictory-precondition and
  always-overflowing controls were marked correctly:
  `Contract.Requires(p > 0 && p < 0)` gave `Proven` with
  `vacuity=ContradictoryPreconditions`, and `return checked(long.MaxValue + p)`
  under `Requires(p > 0)` gave `vacuity=NoModeledNormalReturn`. The
  assumption-based cases did not:

  ```csharp
  public static long AssumeFalse(long p)
  {
      Contract.Ensures(Contract.Result<long>() == 42);
      Contract.Assume(false);
      return p;
  }

  public static long AssumeContradictsRequires(long p)
  {
      Contract.Requires(p > 0);
      Contract.Ensures(Contract.Result<long>() == 42);
      Contract.Assume(p < 0);
      return p;
  }
  ```

  Both were `Proven` with `vacuity=None` (cores `[assume:0]` and
  `[assume:1, requires:0]`).
- **What is wrong:** Vacuity evidence is computed only for contradictory
  preconditions and for a normal-completion predicate that is unsatisfiable
  under non-user assumptions. A user assumption that is unsatisfiable, alone
  or together with the preconditions, removes every modeled normal return,
  but the claim result is indistinguishable from an ordinary proof apart from
  the assumption ID in its core. The documentation says the vacuity field
  exists to make partial-correctness vacuity visible "rather than silently
  presenting the result as an ordinary proof", and consumers that key on
  `Vacuity` (SARIF readers, dashboards, the launcher summary) see a normal
  `Proven`.
- **Failure scenario:** A developer adds `Contract.Assume(count >= 0)` to quiet
  a solver timeout, but `count` is a negative constant on the only path, or a
  refactoring flips a `Requires`. Every postcondition of the method becomes
  `Proven` with `vacuity=None`. Under the default strict assumption policy
  SP0048 still fails the build, but under `SharpProofAssumptionPolicy=allow`
  or `warn` the only sign is an informational or warning SP0048 that looks the
  same as for a harmless assumption.
- **Suggested fix:** After a `Proven` outcome whose core contains a user
  assumption, check satisfiability of the entry and body assumptions including
  user assumptions but excluding the goal. If they are unsatisfiable, report a
  distinct vacuity kind (for example `ContradictoryAssumptions`), or reuse
  `NoModeledNormalReturn` and document that user assumptions count. Add worker
  tests for `Assume(false)` and for an assumption that contradicts a
  precondition.

### Return nullability treats `yield return null` as a null-returning method

- **File:** `SharpProof.Effects/ExceptionHandlerReachability.cs`
  (`ComputeReturnNullability`, used by `GetForEachExceptions` and the await
  branch; also consumed by `OperationEffectScanner.ScanAwait`)
- **Confidence:** Low. Not reproduced. A selected method containing `foreach`
  is rejected by the subset gate (SP0047, which only admits `for` and `while`
  loops). When the loop is in a callee, the call to the iterator's
  `IEnumerator.MoveNext` is unmodeled, so the caller already reports SP0002.
  The misclassification is in the code but is currently masked.
- **What is wrong:** The method collects every `IReturnOperation` under the
  declaration. Roslyn represents `yield return value` as an
  `IReturnOperation` with `OperationKind.YieldReturn`, so for an iterator the
  collected "returned values" are the yielded elements, not the enumerator
  object the method really returns. An iterator whose yields are all `null`
  (or `default`) is classified `ReturnNullability.Null`, although an iterator
  method never returns null.
- **Failure scenario:**
  `class Bag { public IEnumerator GetEnumerator() { yield return null; } }` and
  `try { foreach (var item in new Bag()) { Work(); } } catch (InvalidOperationException) { s_state++; }`.
  `GetForEachExceptions` sees `ReturnNullability.Null`, adds only a
  `NullReferenceException` and returns with `reachesBody = false`, so the
  loop body is never pushed and `Work()`'s exceptions are ignored. If `Work`
  throws `InvalidOperationException`, the catch is classified unreachable, its
  static write is dropped from the enclosing method's effect summary, and a
  write-free or pure contract can be accepted. `ScanAwait` has the same
  exposure for an awaiter factory that is an iterator.
- **Suggested fix:** Return `ReturnNullability.NonNull` for iterator methods
  (detected with `IMethodSymbol.IsIterator` on Roslyn versions that expose it,
  or by the presence of any `YieldReturn`/`YieldBreak` operation) and for
  async methods (the returned task or value task is never null), and ignore
  `IReturnOperation`s whose `Kind` is `YieldReturn` or `YieldBreak` when
  collecting returned values.

### SP0048 is reported once at the first callable instead of where assumptions are declared

- **File:** `SharpProof.Worker.Launcher/Program.cs` (`ReportAssumptions`,
  and the incomplete-coverage report above it),
  `SharpProof.Worker.Launcher/SarifProjection.cs` (assumption notification)
- **Confidence:** High (from the code).
- **What is wrong:** `ReportAssumptions` emits a single SP0048 at
  `response.Manifest.Callables[0].Location`, whose message has only counts
  ("total=N, user=U, trusted=T"). The SARIF projection adds the same text as a
  run-level notification with no location. Each `WorkerCallableResult`
  already carries its `Assumptions` and a `CallableId` that maps to a
  manifest location, so the launcher has what it needs to point at each
  method that declares `Contract.Assume` or `[SharpProofTrusted]`. The
  incomplete-coverage SP0047 has the same shape: it is reported once at the
  first incomplete callable, with a count.
- **Failure scenario:** With the strict profile (`SharpProofAssumptionPolicy`
  defaults to `error`), a project with 200 verified methods, one of which has
  a `Contract.Assume`, fails the build with an SP0048 error on whichever
  callable sorts first by ID, possibly in an unrelated file. The message does
  not name the method that holds the assumption, and SARIF viewers show no
  location, so the developer has to search the codebase or read the raw JSON
  result.
- **Suggested fix:** Emit one SP0048 per callable whose result lists user or
  trusted assumptions, at that callable's location, naming the assumption
  kinds and IDs. Put the same results in SARIF `results` with locations
  instead of a run-level notification. Do the same for SP0047 incomplete
  coverage (one result per incomplete callable, as the SARIF path already
  does for `IncompleteResult`).

### SP0027 prints the folded condition instead of the violated precondition

- **File:** `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`
  (`CompleteEvaluation` and the loop that builds `ClauseEvaluation`)
- **Confidence:** Confirmed. For
  `static int RequirePositive(int value) { Contract.Requires(value > 0); return value; }`
  called as `RequirePositive(-1)`, the reported message was
  `Call to 'RequirePositive' violates precondition 'false'`. When the argument
  is a local (`int x = -1; return Positive(x);`), the message was
  `Call to 'Positive' violates precondition '(v21 > 0)'`, which exposes an
  internal IR variable name instead of `value`.
- **What is wrong:** The analyzer substitutes the call arguments into
  `clause.Condition` and stores the substituted term in `ClauseEvaluation`.
  `IrFactory` folds constants while building terms, so with literal or
  otherwise known arguments the substituted condition is simply `false`. That
  folded term is what `IrPrinter` renders into the `{1}` placeholder of the
  message format, so the diagnostic never names the precondition that failed.
- **Failure scenario:** A method with several `Contract.Requires` clauses is
  called with arguments that violate one of them. Every SP0027 message reads
  `violates precondition 'false'`, so the developer cannot tell which clause
  failed. The documentation's own `Positive(0)` example produces this message.
  The `{0}` placeholder uses `TargetMethod.Name`, so a violating constructor
  call such as `new Guarded(0)` is reported as `Call to '.ctor' violates
  precondition 'false'` (confirmed), which names neither the type nor the
  condition.
- **Suggested fix:** Keep the unsubstituted `clause.Condition` (or the source
  text of the `Contract.Requires` argument) in `ClauseEvaluation` and print
  that. Optionally, append the concrete argument values that made it false.
  Format constructors with their containing type name (for example with
  `ToDisplayString` on the method symbol) instead of `Name`.

### ContractFor validation is documented as a generator but runs as an untagged compilation-end analyzer action

- **File:** `SharpProof.ContractForGenerator/ContractForValidatorGenerator.cs`
  (empty `Initialize`), `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`
  (`ValidateContractForCompanions` registered with
  `RegisterCompilationEndAction`),
  `eng/diagnostics/diagnostic-descriptors.v1.json` and the generated
  `ContractForDiagnosticDescriptors.generated.cs` /
  `GeneratedDiagnosticDescriptors.generated.cs` (`customTags: []` on every
  descriptor), and the docs that describe the generator:
  `docs/diagnostic-examples.md` ("ContractFor generator diagnostics"),
  `docs/coverage-and-limits.md` (the `ContractFor` validation row and the
  paragraph after the closed-attribute table), `docs/architecture.md`
  ("validated by an incremental, no-source generator") and
  `docs/public-api.md` ("The generator validates the association").
- **Confidence:** High for the mismatch (read from the code; the generator
  registers nothing and the SPCF rules are reported by the analyzer's
  compilation-end action, which runs for every non-`off` profile). Medium for
  the IDE consequence, which depends on Roslyn's handling of compilation-end
  diagnostics and was not observed in an IDE.
- **What is wrong:** The docs say an incremental generator validates
  `[ContractFor]` companions, that "the SPCF rules are errors once the
  generator is loaded", and that the row is enforced by the "incremental
  generator loaded with any non-`off` profile". The generator's `Initialize`
  is now empty (its comment says companions are reconciled by the analyzer),
  and all ten SPCF rules come from `SharpProofAnalyzerEngine`, in a
  compilation-end action. The same is true of SP0025 configuration errors and
  SP0050 (unverifiable contract API), which are also only reported from
  compilation-end actions. None of these descriptors carries
  `WellKnownDiagnosticTags.CompilationEnd`. Roslyn uses that tag to know a
  diagnostic can only come from whole-compilation analysis (it is what
  analyzer rule RS1037 asks for). RS1037 cannot flag it here because the
  diagnostics are created in helper classes and returned to the action.
  Two more IDs are mixed: SP0047 is reported from a compilation-end action
  for selected auto-property accessors (`ReconcileSelectedSemicolonAccessors`)
  and SP0024 for assembly-level control attributes (`ValidateDeclaredScope`
  on the assembly), while the same IDs are reported from ordinary symbol
  and operation actions everywhere else, so those descriptors cannot simply
  be tagged without splitting the compilation-end cases into their own IDs
  or moving them into symbol actions.
- **Failure scenario:** In an IDE with the default (document-scoped) live
  analysis, compilation-end actions do not run, so a malformed companion
  (SPCF0005 signature mismatch, SPCF0002 duplicate, SPCF0010 cycle) or a
  broken `SharpProofFeatures` value (SP0025) produces no squiggle while the
  user edits. Because the descriptors are not tagged as compilation-end, the
  IDE can also treat the same errors from the last build as live-analyzable
  and drop them from the Error List once live analysis refreshes the file.
  A user who reads the docs and disables or removes the generator
  reference expecting to lose only SPCF validation (or who keeps only the
  generator) gets the opposite of what the docs describe.
- **Suggested fix:** Either move the validation back into the generator (it
  can report diagnostics live) or update the docs to say the analyzer
  reports SPCF rules at compilation end and drop the generator from the
  package. Add `WellKnownDiagnosticTags.CompilationEnd` to every descriptor
  that is only reported from a compilation-end action (SPCF0001-SPCF0010,
  SP0025, SP0050) in `diagnostic-descriptors.v1.json`, and add a test that
  compares each descriptor's tags with the actions that report it.

### Verifier locations keep raw `#line` paths, so launcher and SARIF results point at the wrong file

- **File:** `SharpProof.CompilerCollector/CompilerArtifact/CompilerSourceLocationProjection.cs`
  (`Create` stores `GetMappedLineSpan().Path` verbatim),
  `SharpProof.Worker.Launcher/SarifProjection.cs` (`ArtifactLocation`,
  `EscapePath`), and `SharpProof.Worker.Launcher/Program.cs`
  (`ReportDiagnostic` prints `path(line,col)`).
- **Confidence:** Confirmed for the manifest contents; High for the
  downstream effect (read from the code, not run through the launcher). A
  source file in a subdirectory containing `#line 40 "..\Views\Page.cshtml"` before a
  method with a refutable `Ensures` produced manifest locations with
  `"path":"..\Views\Page.cshtml"` and the mapped line numbers.
- **What is wrong:** Roslyn returns the path from a `#line` directive exactly
  as written, and the compiler resolves a relative one against the directory
  of the file that contains the directive when it prints diagnostics
  (`SourceFileResolver.NormalizePath` with the tree's path as the base). The
  collector copies the unresolved text into the manifest. The launcher then
  prints it as an MSBuild-style `..\Views\Page.cshtml(42,9): error ...` line,
  which MSBuild and IDEs resolve against the project directory instead.
  `SarifProjection.ArtifactLocation` treats every non-rooted path as relative
  to `%SRCROOT%` (the project directory), and `EscapePath` splits only on
  `/`, so each backslash becomes `%5C` inside one segment:
  `..%5CViews%5CPage.cshtml`. That is a single oddly named file, not a
  relative path, on every platform.
- **Failure scenario:** Razor, T4 and other generators emit `#line`
  directives, often relative and with Windows separators. A refuted or
  incomplete claim in such code is reported against a file that does not
  exist (or the wrong file with the same relative name), so "go to error" in
  the IDE and SARIF viewers in CI do nothing. The analyzer's own diagnostics
  for the same code point to the right place, so the two disagree.
- **Suggested fix:** When `FileLinePositionSpan.HasMappedPath` is true and
  the path is relative, resolve it against the directory of
  `location.SourceTree.FilePath` (what the compiler does) before storing it.
  In `SarifProjection`, normalize `\` to `/` for relative paths before
  escaping, and make paths that fall outside the project directory absolute
  instead of `%SRCROOT%`-relative. Add a collector test with a relative
  `#line` path in a subdirectory and a SARIF snapshot test with a
  backslash path.

### Assigning a conditional, `??`, `?.`, `&&` or `||` expression to an existing local makes every effect claim in the method `Unknown`

- **File:** `SharpProof.Effects/OperationEffectScanner.Assignments.cs` (the
  target switch in the write-location scan falls through to
  `EffectSummaryOperations.Unsupported()` for an `IFlowCaptureReferenceOperation`
  target) and `SharpProof.Effects/CoalesceAssignmentFlowCaptures.cs` (the
  only target-capture resolution). The root cause is shared with the SP0027
  conditional-assignment entry in P2.
- **Confidence:** Confirmed. Each of these was reported with
  `ExceptionSetUnknown: UnsupportedOperation` even though none of them can
  throw:

  ```csharp
  [DoesNotThrow] public static int NoDivide(int p) { int d = 1; d = p > 0 ? 2 : 3; return d; }
  [DoesNotThrow] public static int SafeDivide(int p) { int d = 1; d = p > 0 ? 2 : 3; return 10 / d; }
  ```

  The same happened for `ok = r && false;`, `ok = r || true;` and
  `s = t ?? null;`, while the same right-hand sides written as declarations
  of fresh locals (`int v = p > 0 ? 2 : 3;`) were analyzed normally. The
  limitation is not listed in `docs/analysis-limits.md` or
  `docs/unknown-reasons.md`.
- **What is wrong:** When the right-hand side of an assignment contains
  control flow, Roslyn's CFG captures the *target* before branching and
  assigns through the capture reference. The effect scanner only knows how
  to write locals, parameters, fields, array elements and properties, so the
  capture-reference target is classified as an unsupported write. The
  conservative outcome is sound, and it is currently what keeps the stale
  managed-flow value from the P2 entry out of effect proofs, but it makes
  ordinary code unprovable.
- **Failure scenario:** `result = x > 0 ? x : -x;`, `name = input ?? "";`,
  `found = found || Check(i);` and `len = s?.Length ?? 0;` are among the most
  common statements in C#. Any one of them turns `[DoesNotThrow]`,
  `[EnforcePure]` and `[ZeroAllocations]` into `Unknown` with a generic
  reason, and users cannot tell why an obviously pure method fails.
- **Suggested fix:** Resolve target captures to the captured storage (the
  same fix as the P2 entry), then scan the write as a write to that local,
  parameter or field. Until then, name the construct in the SP0045/SP0046
  reason and list it in `docs/analysis-limits.md`.

### A valid `ContractFor` companion for an interface member reports SP0047 on the bodiless target

- **File:** `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs` (selected
  callables without an operation root are reported as
  `MissingOperationRoot`) together with the companion selection in
  `SharpProof.Contracts/EffectiveContractSourceResolver.cs`.
- **Confidence:** Confirmed. The shipped `samples/ContractFor` source,
  compiled with nullable enabled and `SharpProofFeatures=all`, reported
  `SP0047 ... could not completely analyze selected method 'Find': MissingOperationRoot`
  on `IService.Find`. A minimal valid companion for `IGate.Open` did the
  same, while a companion for a class method with a body did not, and the
  companion's `Requires` were still enforced at call sites (SP0027 for
  `new Calc2().Next(500)` against `Requires(x < 100)`).
- **What is wrong:** A companion makes its target a contract-bearing
  ("selected") callable. For an interface or abstract member there is no
  body to analyze, which is expected, but the pipeline reports it the same
  way as an explicitly annotated method it failed to analyze.
  `docs/diagnostic-examples.md` does say SP0047 "includes selected
  abstract, interface, and `extern` declarations that have no operation
  body", so the report is documented, but for a companion the user did not
  annotate the declaration at all and has no way to resolve the
  diagnostic other than suppressing it.
- **Failure scenario:** Every interface companion, which is the main use of
  `ContractFor` and what the sample demonstrates, adds an SP0047 that users
  cannot resolve. Teams that raise SP0047 to a warning to catch real
  analysis gaps (the diagnostics sample does exactly this kind of
  configuration) get a permanent false alarm per companion member and learn
  to ignore the rule.
- **Suggested fix:** Do not report SP0047 for abstract, interface, extern or
  partial-definition targets whose only contract source is a companion (or
  report a separate, clearly informational ID). Add the `samples/ContractFor`
  expectation "no SP0047" to the sample test.

### Preconditions added on overrides and interface implementations are assumed but cannot be checked at virtual call sites

- **File:** `SharpProof.Contracts/ContractClauseInventoryBuilder.cs` and
  `SharpProof.Contracts/ContractBinder.cs` (clauses are accepted on any
  ordinary method, including overrides and interface implementations), with
  the call-site check in
  `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs` (which only sees the
  statically bound target).
- **Confidence:** Confirmed. `StrictScaler.Scale`, an `override` of
  `BaseScaler.Scale` that also implements `IScaler.Scale`, declared
  `Contract.Requires(x > 0)` and `Contract.Ensures(Contract.Result<long>() > 0)`.
  The worker published the postcondition as `Proven` with proof core
  `requires:0`. The analyzer reported SP0027 for `new StrictScaler().Scale(-5)`
  but nothing for `baseScaler.Scale(-5)` or `scaler.Scale(-5)` through the
  interface, and nothing about the strengthened precondition itself. A
  `[Positive]` parameter on an interface implementation behaved the same way.
  The limitation is not described in `SEMANTICS.md` or
  `docs/coverage-and-limits.md`.
- **What is wrong:** A precondition is an obligation on callers. Callers
  that bind to the base method or the interface member cannot see a
  precondition declared only on one override, so it is never checked for
  them, yet the override's own proofs assume it. This is the precondition
  strengthening that the .NET Code Contracts tools rejected; SharpProof
  accepts it silently.
- **Failure scenario:** A team adds `Contract.Requires(x > 0)` to one
  implementation to get its postcondition proven. Every caller uses the
  interface, passes `-5`, and receives a negative result from a method whose
  "result is positive" claim is shown as proven in the verification report
  and in SARIF.
- **Suggested fix:** Report SP0024 for `Requires` clauses and closed
  parameter preconditions on overrides, explicit or implicit interface
  implementations, unless an identical precondition is declared on the
  overridden or implemented member (or on its `[ContractFor]` companion).
  Alternatively, mark such proofs with a distinct vacuity or assumption kind
  so that they are not presented as unconditional. Document the rule.

### SP0027 only checks calls in top-level statements, so a violation inside any block is silent

- **File:** `SharpProof.Analyzer.Core/RequiresCallSiteDiscovery.cs`
  (`HasReplayableCallEvaluation` and the shape rules it applies), with the
  shape list in `docs/diagnostic-examples.md` (SP0027).
- **Confidence:** Confirmed with `SharpProofFeatures=contracts`. With
  `PosV(int x)` requiring `x > 0`, `PosV(0);` as a statement directly in the
  method body was reported, but none of these was:
  `{ PosV(0); }`, `if (true) { PosV(0); }`, `do { PosV(0); } while (false);`,
  `checked { PosV(0); }`, `label: PosV(0);`, `try { PosV(0); } catch (Exception) { }`,
  `try { PosV(0); } finally { }`, `try { } finally { PosV(0); }`,
  `lock (new object()) { PosV(0); }`, and a `using` block body. Every one of
  those calls runs unconditionally. Nested expressions were also silent
  (`Outer(Pos(0))`, `var x = Pos(0) + 1;`, `$"{Pos(0)}"`,
  `if (Pos(0) > 0)`, `a[0] = Pos(0);`, `for (var i = Pos(0); ...)`,
  `Pos(-5) * 2`). The two `using` forms also disagree:
  `using var r = new Res(0);` was reported, but `using (new Res(0)) { }` and
  `using (var r = new Res(0)) { }` were not.
- **What is wrong:** The documentation describes SP0027 as reporting "an
  exact ordinary invocation or object creation" whose precondition is false,
  and lists the replayable shapes as "direct top-level expression
  statements, returns, throws, single local initializers, simple
  assignments ...". In practice "top-level" means a statement directly in
  the method body: a call inside any nested statement, even a bare block,
  is never replayed, and neither is a call nested inside a larger
  expression. The discovery and flow analysis already know when a call is
  definitely executed (they correctly stay silent for ternary arms,
  short-circuit operands and `catch` bodies), so the restriction is a
  shape limit, not a soundness requirement.
- **Failure scenario:** Almost all real calls sit inside an `if`, loop,
  `try` or `using` body, or inside an argument list. A user reading the
  SP0027 description expects `try { Connect(port: 0); } finally { ... }` to
  be reported the same way as a bare `Connect(port: 0);`, and gets
  silence. The `using` inconsistency means rewriting
  `using var r = new Res(0);` as a `using` statement silently removes the
  warning.
- **Suggested fix:** Replay calls in nested statements whenever the flow
  analysis proves them definitely executed from method entry (a bare
  block, `checked`/`unchecked`, a labeled statement, the first statements
  of a `try` or `lock` body, `if (true)`), and replay nested call
  expressions whose operands before the call are definitely non-throwing,
  as is already done for top-level shapes. Handle the `using` statement's
  resource expression like a `using` declaration. At minimum, state
  explicitly in the SP0027 section that only statements directly in the
  method body are checked.

### Effect attributes on abstract and interface members always fail and are never checked on implementations

- **File:** `SharpProof.Analyzer.Core/AnalyzerFeaturePipeline.cs` (selected
  callables without an operation root become `MissingOperationRoot`), the
  effect contract evaluation that then reports SP0002/SP0045/SP0046, and
  the attribute declarations in `SharpProof.Attributes`
  (`[AttributeUsage(..., Inherited = false)]` on `EnforcePureAttribute` and
  its siblings).
- **Confidence:** Confirmed. With `SharpProofFeatures=effects`,
  `[EnforcePure] public abstract int Area();` reported both
  `SP0047 ... 'Area': MissingOperationRoot` and
  `SP0002: Method 'Area' is marked [EnforcePure], but its effects do not prove observable purity`,
  and `[EnforcePure] int Get();` on an interface did the same. The sealed
  override `Square7.Area()`, which increments a static counter, and the
  implementation `Impl7.Get()`, which does the same, were not reported,
  and neither was an override of a `[DoesNotThrow] virtual` method that
  always throws. A `partial` method with the attribute on the defining
  declaration was checked against its implementation, which is correct.
- **What is wrong:** An effect attribute on a member with no body can only
  mean "every implementation must satisfy this". SharpProof does not
  implement that meaning (the attributes are not inherited and overrides
  are not checked), but it does not reject the placement either. Instead it
  treats the abstract member as a selected callable, fails to analyze it,
  and reports a violation of the contract itself, which reads as if the
  (nonexistent) body were impure. The SP0047 half is documented
  (`docs/diagnostic-examples.md` lists "selected abstract, interface, and
  `extern` declarations"); the SP0002/SP0046 half and the fact that
  implementations are never checked are not. The user gets two diagnostics they
  cannot fix on the declaration, and no diagnostic on the implementation
  that actually breaks the stated contract.
- **Failure scenario:** A library author annotates an interface
  (`[EnforcePure] int Get();`) to document and enforce purity for all
  implementations. Every build shows SP0002 and SP0047 on the interface,
  so the author suppresses them, and implementations that write global
  state are never reported. Callers cannot rely on the annotation either,
  since contracts are never used to discharge virtual dispatch.
- **Suggested fix:** Either report a dedicated diagnostic for effect
  attributes on abstract, interface and extern members ("not enforced;
  annotate implementations instead") and skip the SP0002/SP0045/SP0046 and
  SP0047 reports for them, or implement the obligation: treat an effect
  attribute on an abstract or interface member as applying to every
  override and implementation in the compilation and check each of them.
  Document the chosen behavior next to the attribute descriptions.

### SP0046 messages list exception types by full assembly identity

- **File:** `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs`
  (`FormatDiagnosticTypes`), which uses
  `SharpProof.Frontend/CompilerIdentityBridge.cs` (`CreateTypeDisplay`,
  `AssemblyIdentity(...) + "::" + TypeReference(...)`).
- **Confidence:** Confirmed. A `[DoesNotThrow]` method whose helper may
  throw `NullReferenceException` and a user exception `Fail` was reported as
  `... may-effect summary includes disallowed exceptions: Probe, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null::Fail, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e::System.NullReferenceException`.
- **What is wrong:** The diagnostic text reuses the canonical identity
  string meant for manifests and evidence, which prefixes every type with
  its assembly's display name, version, culture and public key token, and
  sorts by that prefix. The message becomes long enough that the actual
  exception names are hard to find, the order is by assembly rather than by
  type, and the text changes with the reference pack version (a project
  that multi-targets `net8.0` and `net9.0` gets different messages for the
  same method, and so does an SDK update).
- **Failure scenario:** Teams that review SP0046 in the IDE error list or
  keep baselines of analyzer output see unreadable messages and churn on
  every framework update, even though the analyzed code did not change.
- **Suggested fix:** Use `ToDisplayString()` (optionally with the assembly
  name only when two reported types share a full name) in user-facing
  messages, and keep `CreateTypeDisplay` for manifests, evidence and
  properties on the diagnostic. Sort by the displayed type name.

### Nullable value types and other everyday pure BCL members have no effect specifications, so ordinary code is unprovable

- **File:** `SharpProof.Specs/DefaultApiSpecCatalog.json` (no entries for
  `System.Nullable<T>` members) with the rule in `SEMANTICS.md` that a
  closed constructed generic API call is accepted only when a
  specification resolves for it.
- **Confidence:** Confirmed. Helpers `v.HasValue`, `v.GetValueOrDefault()`,
  `v ?? 0` and `v.HasValue ? v.Value : 0` over an `int?` parameter each made
  an `[EnforcePure]` caller report SP0002. So did `Math.Max(a, b)`,
  `Math.Min(a, b)`, `Math.Clamp(a, 0, 10)`, `string.IsNullOrEmpty(s)` and
  the string indexer `s[0]` directly in `[EnforcePure]` methods, while
  `Math.Abs(int)` and `string.Length`, which have catalog entries, were
  accepted. `Math.Max(int, int)` does have a relational specification in
  the `dotnet.scalar` pack, so with that pack enabled a postcondition can
  use its result while an effect claim on the same call still fails.
- **What is wrong:** `Nullable<T>.HasValue`, `GetValueOrDefault()` and
  `Value` are pure field reads (`Value` throws `InvalidOperationException`
  when empty, which the analysis already models for explicit unwraps), and
  the compiler lowers `??`, lifted operators and `is` checks on nullable
  value types into exactly these calls. Without specifications they are
  unmodeled external calls, so every method that touches a nullable value
  type is unknown for purity, allocation and exceptions.
- **Failure scenario:** Optional numeric parameters and fields (`int?`,
  `DateTime?`) are common in exactly the kind of small helper that effect
  contracts target; users see SP0002/SP0045/SP0046 on code that is plainly
  pure.
- **Suggested fix:** Add specifications for `Nullable<T>.HasValue`,
  `GetValueOrDefault()`, `GetValueOrDefault(T)` and `Value` (reads of the
  receiver, no allocation, `Value` throwing `InvalidOperationException`),
  instantiable for any `T`, and for the common pure numeric and string
  members (`Math.Min`, `Math.Max`, `Math.Clamp` with its
  `ArgumentException`, `string.IsNullOrEmpty`, the string indexer with its
  `IndexOutOfRangeException`). Add a test that `v ?? 0` over `int?` is
  provably pure.

### SP0027 checks another project's `Contract.Requires` only when that project is a source reference, so the IDE and the build disagree

- **File:** `SharpProof.Analyzer.Core/SharpProofAnalyzerEngine.cs`
  (`MayContainExternalClosedPreconditions` looks at `Contract.Requires`
  clauses through `CompilationReference` and only at closed parameter
  attributes through `PortableExecutableReference`) and the call-site
  contract lookup used by `SharpProof.Analyzer.Core/RequiresCallSiteAnalyzer.cs`.
- **Confidence:** Confirmed. A library method
  `public static long Pos(long x) { Contract.Requires(x > 0); return x; }`
  and a `[Positive]` sibling were called with `0` from a second
  compilation. With the library passed as a compilation reference (what
  Visual Studio and other IDE hosts use for project-to-project references),
  both calls reported SP0027. With the library passed as its compiled DLL
  (what `dotnet build` and CI use for the same project reference), only the
  `[Positive]` call was reported.
- **What is wrong:** `Contract.Requires` is `[Conditional]`, so it is not
  present in the referenced assembly's IL, and the analyzer has no other
  metadata record of it. When the referenced project is available as
  source, the analyzer reads the clause from its syntax. The same code
  therefore gets a precondition warning in the editor that the command-line
  build never produces (or, for a team that builds with
  `TreatWarningsAsErrors`, an IDE error that CI does not reproduce).
  `SEMANTICS.md` says the call-site screen "checks source and metadata
  targets, including closed parameter annotations", which does not say that
  `Requires` clauses are invisible across a compiled project boundary.
- **Failure scenario:** A shared library annotates its API with
  `Contract.Requires`. Developers see SP0027 in the IDE when they call it
  wrongly from an application project, but the CI build of the same commit
  is clean, so the warning is ignored, or a build that is green in CI shows
  errors locally.
- **Suggested fix:** Pick one behavior. Either persist `Requires` clauses
  in metadata (for example an assembly-level contract table written by the
  analyzer or compiler collector) so compiled references are checked too,
  or ignore source-only clauses of referenced projects so the IDE matches
  the build. Document the choice, and recommend closed parameter attributes
  for preconditions that must be checked across assemblies.

### Repeated `[AllowedExceptions]` attributes are silently unioned, and each is reported as its own proven claim

- **File:** `SharpProof.Attributes/AllowedExceptionsAttribute.cs`
  (`AllowMultiple = true`), the combination of repeated attributes in
  `SharpProof.Analyzer.Core/EffectContractDiagnostics.cs`, and the
  "Repeated effect attributes receive distinct manifest claims while
  sharing the effective combined constraint" rule in `SEMANTICS.md`.
- **Confidence:** Confirmed. A method carrying
  `[AllowedExceptions(typeof(InvalidOperationException))]` and, separately,
  `[AllowedExceptions(typeof(FormatException))]` that throws
  `FormatException` got no analyzer diagnostic, and the worker published
  two `Proven` claims for it, one located at each attribute. The same
  method with only the first attribute was reported (SP0046) and was
  `Unknown`.
- **What is wrong:** The allowed sets of repeated attributes are combined
  by union, which matches how a single attribute with both types behaves,
  but nothing documents that repeating the attribute widens the allowance,
  and the per-attribute claims in results and SARIF each say `Proven` at a
  location whose own attribute is violated. A reader who treats each
  annotation as an independent statement ("this member may throw only
  `InvalidOperationException`"), for example after a merge added a second
  attribute, gets a proof of something the code does not satisfy.
- **Failure scenario:** Two developers each add an `[AllowedExceptions]`
  to the same member for different reasons; the method now satisfies
  neither individual intent, but both claims show as proven in the
  verification report.
- **Suggested fix:** Either document that repeated `[AllowedExceptions]`
  attributes form one union constraint and report a single combined claim
  (located at the member) instead of one claim per attribute, or treat
  each attribute as its own constraint. Consider an informational
  diagnostic suggesting a single attribute when a member has several.

### `Contract.Assume` is used for postconditions but ignored by effect claims, while `Contract.Requires` is used by both

- **File:** `SharpProof.Effects/ManagedAbstractFlow.cs` (`Transfer`, the
  `IInvocationOperation` case:
  `IsRequires(invocation) ? Assume(state, invocation.Arguments[0].Value, true) : HavocCall(...)`,
  so a `Contract.Assume` call is treated like any other call), with the
  user-facing description in `docs/coverage-and-limits.md` ("Explicit user
  evidence").
- **Confidence:** Confirmed with `SharpProofFeatures=effects`.
  `[DoesNotThrow] int F(int x) { Contract.Requires(x != 0); return 10 / x; }`
  was accepted, but the same method with `Contract.Assume(x != 0)` reported
  SP0046 (`DivideByZeroException`), and so did
  `Contract.Assume(a != null && i >= 0 && i < a.Length); return a[i];`.
  For postconditions the worker does use `Assume` (it appears in proof cores
  as a user assumption).
- **What is wrong:** The documentation presents `Contract.Assume` as
  explicit user evidence without saying that it only feeds postcondition
  proofs. A user who adds an assumption to state an invariant the analyzer
  cannot infer (the usual reason to write one) finds that it has no effect
  on `[DoesNotThrow]`, `[AllowedExceptions]` or `[ZeroAllocations]`, even
  though the equivalent `Requires` does. Not trusting assumptions for
  effect claims is a defensible choice, since effect proofs have no place
  to record the assumption, but the asymmetry is surprising and
  undocumented.
- **Failure scenario:** A developer writes `Contract.Assume(divisor != 0)`
  before a division in a `[DoesNotThrow]` method, keeps getting SP0046, and
  either converts it into a `Requires` (which changes the method's contract
  and triggers SP0027 at callers) or suppresses the diagnostic.
- **Suggested fix:** Either apply `Assume` conditions to the managed flow
  for effect claims and record them as SP0048-visible assumptions on those
  claims, or document in `docs/coverage-and-limits.md` and `SEMANTICS.md`
  that assumptions are postcondition-only evidence and never discharge
  effect obligations.

## Areas checked without findings

These were exercised with the same scratch harnesses and produced no defect.
They are listed so the work is not repeated, and because each one is evidence
about where the implementation is solid.

- **Worker postcondition soundness (differential).** A C# fuzzer generated
  contract-bearing methods over `long`/`bool` with branches, reassignment,
  early returns and calls to generated helpers, ran the real collector and
  worker, and executed every `Proven` method over a grid of boundary inputs
  (including `long.MinValue`, `-1`, `0`, `long.MaxValue`). Roughly 870
  postconditions were published `Proven` across 180 seeds (40 methods each),
  including 308 with nested helper summaries, and none was violated by a
  concrete run. A variant with `int` parameters widened into `long`
  arithmetic, which exercises the parameter source-interval assumptions,
  published 240 more `Proven` claims across 100 seeds, again with no
  concrete violation. An earlier worker-level IR fuzzer covered 10,000 programs with
  no false proof. The reverse direction was checked too: across 250 more
  seeds, every one of the 1,154 `Refuted` postconditions was replayed by
  calling the compiled method with the reported model, and in every case the
  precondition held, the method returned normally, and the postcondition
  was false. There was no false refutation.
- **Source relational summaries through branching helpers.** With
  project-wide `checked` arithmetic, postconditions over helpers with
  early returns, a constant-case `switch`, a conditional expression and a
  checked multiplication were `Proven` through `source-summary:` evidence
  when true, and false ones were `Unknown` (`CounterexampleNotReplayable`)
  rather than proven. A helper whose `Requires` or `[Positive]` parameter
  the caller does not establish was not used to prove the caller.
- **Implementation-IL summaries (differential).** `int32` helpers with
  unchecked wraparound, `checked` arithmetic, division, remainder, negation
  and nested calls were compiled into a separate library and called from
  contract-bearing methods. 118 claims were proven through `il-summary:`
  evidence with no false proof.
- **`[DoesNotThrow]` and `[EnforcePure]` (differential).** Random methods over
  `int`, `string`, `int[]` and a mutable class, with guards, try/catch/finally,
  helper calls and static writes, were run through the analyzer; every method
  the analyzer accepted silently was then executed over an input grid. 289
  accepted `[DoesNotThrow]` methods threw nothing, and 245 accepted
  `[EnforcePure]` methods mutated no static field, array or object. A later
  campaign that also generated `switch` statements and `goto` out of nested
  `try`/`finally` regions did produce violations, and all of them were the
  nested-`finally` defect in the P0 section rather than anything new. When
  the generator was then restricted so that protected regions never nest, and
  given `using` (with both a no-op and a state-writing `Dispose`), `lock`,
  `checked` blocks and single-level `try`/`catch`/`finally` instead, 600
  batches produced 202 accepted `[EnforcePure]` and 222 accepted
  `[DoesNotThrow]` methods and no violation at all. A
  `[ZeroAllocations]` campaign that measured each accepted method's managed
  allocation with `GC.GetAllocatedBytesForCurrentThread` accepted 1,379
  methods across 300 batches; the only 3 that allocated were the same
  nested-`finally` defect. Direct probes also confirmed that boxing through
  `GetHashCode`, `Equals` and `ToString` on structs that do not override them,
  `Enum.HasFlag`, `Enum.ToString` and `int.ToString` are all reported.
- **SP0027 call-site checking (differential).** 3,082 call sites with literal
  arguments and randomly generated preconditions were compared against
  concrete evaluation of the same condition: 566 diagnostics, no false
  positive. Manual probes also found no false positive for truncating
  division, negative remainder, unsigned and narrowing casts, unchecked
  wraparound, shifts, UTF-16 lengths or floating point, and no report for
  call sites that are not definitely executed (short-circuit operands,
  ternary arms, `catch` blocks, switch cases). A second campaign generated
  callers whose locals start from literals and then pass through guards,
  early returns, `switch` statements, bounded loops and increments before the
  call, and recorded at run time whether the precondition held. Of its 94
  SP0027 reports, one was the conditional-assignment false positive listed
  above, one was a call with literal arguments behind a guard that always
  returns first (the call is dead, but it would violate the precondition
  whenever it ran), and the other 92 were real violations. The only analyzer
  exception was the monotonicity crash listed above. Named arguments out of
  order, default parameter values, an assignment or increment inside an
  earlier argument, implicit numeric conversions and extension-method
  receivers were all mapped correctly, as were narrowing and wrapping casts
  in arguments (`unchecked((byte)300)`, `(long)3.9`, `(short)40000`), `char`
  arguments, and caller-info parameters (`[CallerLineNumber]`,
  `[CallerMemberName]`, `[CallerFilePath]`, `[CallerArgumentExpression]`,
  whose compiler-supplied values were used instead of the declared
  defaults). Neither SP0027 nor the effect analysis trusts a callee's
  postcondition: a helper with a false `Ensures(Result == 0)` did not make
  `Pos(Helper())` a reported violation, and a false `Ensures(Result != 0)`,
  `Ensures(Result != null)` or a `Contract.Assume` in a helper did not
  discharge a caller's division or dereference. A local set to `0` and then
  changed before the call (through a `ref` or `out` argument, a `ref`
  local, a lambda, a local function, a helper writing an object field,
  deconstruction, `+=`, `++`, a loop, `try`/`finally`, `try`/`catch`, a
  constant `switch` or a `goto`) never produced a stale SP0027, and
  relational, `not`, `or` and type-pattern guards before a call produced
  no false positive either. A violating call inside an expression-tree
  lambda (`Expression<Action> e = () => Pos(0);`) was, correctly, not
  reported, since quoted code does not run.
- **Broken code.** Methods with syntax errors, unresolved names, malformed
  contract calls, out-of-range `[EffectContract]` flags, a string
  `[InRange]` bound, non-exception `[AllowedExceptions]` types, a companion
  for an unresolved type, and missing `goto` labels produced SP0024, SP0047
  or SPCF0001 (or nothing where the compiler already errors), and never an
  analyzer exception.
- **Fail-closed behavior of the worker.** A battery of twenty methods whose
  postconditions are false for some input (goto, switch, try/finally, checked
  blocks, compound assignment, increment, casts, shifts, bitwise operations,
  arrays, `lock`, local functions, tuples, `out` parameters, string length)
  produced six `Refuted` and fourteen `Unknown` results, and no `Proven`.
  Reference-typed contracts, heap state, patterns and loops abstain rather
  than guess.
- **Effect analysis coverage.** Correct diagnostics were produced for hidden
  allocations (closures, interface boxing, generic boxing, iterators,
  collection expressions, `string.Format`, delegate combination), hidden
  exception sources (throwing type initializers, array covariance, negative
  array sizes, unboxing, `lock(null)`, null delegate invocation, checked
  conversions, `throw null`), handler semantics (filters, rethrow, throwing
  `finally`, catch-type hierarchy), compound division and remainder overflow,
  project-wide `CheckForOverflowUnderflow`, virtual/abstract/interface
  dispatch, and code synthesized by the compiler that runs user code (record
  `ToString`/`GetHashCode`, object and collection initializers, struct
  parameterless constructors). `[Conditional]` and unimplemented `partial`
  call elision were handled correctly, and `[DoesNotReturn]` is not trusted.
- **Contract binding and placement.** Contracts in branches, blocks,
  switches, local functions, or after other statements fail closed, as do
  mismatched `Contract.Result<T>()` type arguments, `Contract.Old` of a
  result, and closed attributes on `ref`, `in` and `out` parameters.
  Vacuity evidence is correctly reported for contradictory preconditions and
  for a body with no modeled normal return.
- **Real-world code at scale.** The 100 pinned open-source files in
  `SharpProof.Gates/Corpus/oss-methods.json` (aalhour/C-Sharp-Algorithms) were
  extracted and every one of their 870 methods with a body was given an effect
  contract by syntax rewriting. The analyzer ran over the whole compilation
  three times, once each for `[DoesNotThrow]`, `[EnforcePure]` and
  `[ZeroAllocations]`, with no analyzer exception. The same annotated sources
  went through the collector and worker: the manifest was emitted with no
  SP0049, the run completed, and the single `Proven` effect claim was an
  empty `Dispose()`. It is worth noting how narrow the analyzable subset is on
  real code: 840 of the 870 methods were `SP0047`/`UnsupportedCallable`, which
  matches the documented bounded-subset limits but means these contracts are
  mostly unavailable to ordinary library code.
- **Shipped samples.** `Effects`, `Preconditions`, `Outcomes` and `ContractFor`
  produce no unexpected diagnostics when compiled the way their projects do
  (`SharpProofFeatures=all`, `Nullable=enable`), and all five `Library` claims
  are `Proven` end to end, as `samples/README.md` promises. The one sample
  that does not behave as intended is `TrustedBoundary` (see the
  `Randomness` capability entry).
- **Contract binding details.** `Contract.Old` is correct for expressions over
  several reassigned parameters, for arithmetic combined with
  `Contract.Result`, inside a conditional postcondition, and for a parameter
  that is never reassigned; `Old` of a helper call abstains. Invalid shapes
  (`Contract.Result` or `Contract.Old` inside `Requires` or `Assume`) both
  fail closed in the worker and are reported as SP0024 with a placement
  message. With several postconditions on one method, outcomes land on the
  right clause: the refuted clause was reported in its own position for a
  first-false and a middle-false method, with the others proven.
- **Indirect and unsafe writes.** Pointer writes, `fixed` blocks,
  `stackalloc`, `Span` indexers, `Interlocked`/`Volatile` on a static through
  `ref`, and writes through a span view all abstain or are reported; direct
  array-element writes and `Array.Clear` on a static array are reported.
  Contracts are never used to discharge virtual dispatch, so a proven
  `[EnforcePure]` on a virtual base method does not silence a call that an
  override could serve.
- **Trusted boundaries.** The documented metadata rule works end to end: a
  referenced assembly's method carrying `[SharpProofTrusted]` and
  `[EffectContract(..., Complete = true, PreconditionFree = true)]` lets a
  caller be proven `[EnforcePure]`, while the same declaration without
  `PreconditionFree` and an undeclared method both stay `Unknown`. In the
  analyzer, trust never sharpens anything on its own: a trusted method with
  no declared summary, and a declared summary that does not cover its own
  body, are both reported. `base.M()` resolves to the base implementation
  rather than the override, in both directions. A referenced library that
  defines its own look-alike `SharpProof.Attributes.SharpProofTrustedAttribute`
  and `EffectContractAttribute` (including an assembly-level "trusted"
  attribute) could not spoof trust: a caller of its impure method stayed
  `Unknown`.
- **Manifest determinism.** The collector produced a byte-identical manifest
  (same SHA-256) for the 100-file open-source corpus across two concurrent
  runs and one sequential run, so manifest emission does not depend on
  analysis order. Analyzer diagnostics were deterministic too: 40 fuzzer
  batches, each analyzed three times with concurrent execution enabled,
  produced identical diagnostic sets.
- **Source generators.** A real incremental generator run in process emitted
  a contract-bearing method into a `partial` class alongside a hand-written
  one. With both an absolute generated-file path (under `obj`, as MSBuild
  produces) and a relative one, the collector emitted a manifest and both
  postconditions were `Proven`.
- **Helper callees with a richer statement set (differential).** A second
  effect fuzzer generated unannotated helpers with nullable values
  (`??`, `HasValue`, `GetValueOrDefault`, unguarded casts), strings,
  `is`/`as` on `object`, field reads and writes, bounded `for` and array
  `foreach` loops, `switch` statements, exhaustive switch expressions,
  `lock`, `using`, `checked` blocks, single-level `try`/`catch`/`finally`
  and a user exception type, called from annotated methods. Shapes that
  reproduce the entries above were excluded. Over 1,000 batches, 1,732
  accepted `[EnforcePure]` methods mutated nothing and 668 accepted
  `[DoesNotThrow]` methods threw nothing when run over an input grid. The
  only analyzer exception was the monotonicity crash. For 300 of those
  batches the same sources also went through the collector and worker: all
  2,008 published effect claims matched the analyzer exactly (every method
  the analyzer accepted was `Proven`, and no method it reported was
  `Proven`).
- **Helpers with patterns, properties and operators (differential).** A
  third effect fuzzer generated helpers with `is` and switch-statement
  patterns (type, property, relational, `and`/`or`/`not`, list and tuple
  patterns, `when` guards), switch expressions, property getters that write
  static state or throw, a property setter, a user struct with `+`, `/`,
  unary `-`, `==` and conversions, null-conditional chains, interpolation
  and concatenation, and `try`/`catch` with filters. Over 1,000 batches in
  each mode, 1,959 accepted `[DoesNotThrow]` methods threw nothing and
  2,501 accepted `[EnforcePure]` methods changed no static field, array or
  object when run over an input grid. Of 2,692 accepted
  `[ZeroAllocations]` methods, the only two that allocated did so through a
  caught runtime exception (see the P0 entry). The only analyzer exception
  was the monotonicity crash. For 60 of the `[DoesNotThrow]` batches, the
  collector and worker agreed with the analyzer on all 480 effect claims.
  A fourth variant added `using` over a struct resource whose `Dispose`
  writes or throws, `foreach` over a custom struct enumerator whose
  `MoveNext`, `Current` and `Dispose` write or throw, `box?.Bump()`,
  backward `goto` loops, `do`/`while (false)`, throw expressions, string
  `switch` statements, `checked` increments and a user-defined compound
  `+=` (keeping `using` out of handlers and after `try` statements, where
  the P0 entries apply). Over 800 batches per mode it accepted 1,578
  `[DoesNotThrow]`, 1,772 `[EnforcePure]` and 1,897 `[ZeroAllocations]`
  methods; the only violation was one more caught `DivideByZeroException`
  allocation. The collector and worker agreed with the analyzer on all 400
  effect claims of 50 `[EnforcePure]` batches and all 320 of 40
  `[ZeroAllocations]` batches.
- **Implicit user code and callee bodies.** Because several false proofs
  above come from code the compiler runs implicitly, the other implicit
  call shapes were checked both in the selected method and behind an
  unannotated helper: positional, list and property patterns
  (`Deconstruct`, `Length`, indexers), implicit `Index`/`Range` indexers,
  custom event `add` accessors, `foreach` with user-defined element
  conversions or an extension `GetEnumerator`, tuple equality with a
  user-defined `==`, deconstruction, `with` on record classes and record
  structs (copy constructors and `init` accessors), object initializers with
  side-effecting `init` accessors, `AppendLiteral` in a custom interpolated
  string handler, `using` with an explicit `IDisposable.Dispose` next to a
  different public `Dispose()` (and a re-implemented interface), explicit
  static constructors triggered by method calls, instantiation or static
  properties, and pointer, `ref`-local, `Span`, `MemoryMarshal`,
  `Unsafe.As`, `Interlocked` and `ref`/`out` writes inside helpers. Every one
  was reported or failed closed. The same held for 23 runtime exception
  sources (nullable unwrapping, string, span, multi-dimensional and jagged
  indexers, checked float and sign conversions, `Math.Abs`, unboxing,
  division and remainder overflow, array covariance, negative array sizes)
  in the selected method and in helpers, for `checked` compound assignments
  and increments on `byte`, `sbyte`, `short`, `ushort` and `char` (where the
  narrowing back-conversion is what overflows, even from known constants),
  for checked `uint`/`ushort`/`byte` arithmetic, and for checked enum
  increments and enum arithmetic. Implicit and explicit `base(...)` calls
  that throw or write state were reported for constructors and for
  `new` of a type whose implicit constructor chains to them.
- **Handler reachability and completion.** Apart from the nullable unwrap
  entry above, a `catch` whose only possible source was a type initializer,
  a throwing property getter or `Dispose`, unboxing, an explicit reference
  or interface cast, array covariance, a negative array size, division or
  remainder overflow, a constant zero divisor, `Math.Abs`, checked
  negation, increments and lifted arithmetic, a `long` array index, `lock`
  on null, string indexing, a throwing `ToString` in concatenation,
  `int.Parse`, a null dereference or an exception filter was correctly
  treated as reachable. Code after a call to a virtual, abstract or default
  interface method whose base body never returns was kept, and callee loops
  that exit through `goto`, `break` inside `try`/`finally`, `using` or
  `lock`, a `catch`, a `return` inside `switch`, or an exception caught
  outside the loop were all treated as able to return. Guards on locals
  assigned through `??`, `?.`, `&&`, `||` and `?:` did not prune live
  branches in the effect analysis. Apart from `using` (see the P0 entry),
  every other specially modeled construct placed inside `catch` or
  `finally` was reported: string concatenation with a side-effecting
  `ToString`, `lock`, type initialization, a `foreach` enumerator in a
  helper's `finally`, nested `try`/`catch`, explicit throws, divisions, and
  writes through locals that alias caller state. Facts that hold only on
  the normal path out of a `try` (a divisor, a null check, an index or an
  overflow bound assigned at the end of the `try` body) were not used to
  discharge obligations after a `catch` that skips the assignment.
- **Ownership, aliasing and other scopes.** Writes through locals that
  start fresh and are then pointed at caller state (reassignment, a branch,
  `??`, `?:`, a returned argument, an object or array or struct holding the
  argument, a static field, swaps) were all reported, while writes that stay
  inside fresh objects and arrays were accepted. A `params` parameter
  written by the callee was treated as caller-visible when the caller
  passes its own array. Side-effecting and throwing exception filters, a
  `ref struct` pattern `Dispose` in a helper, `extern`/`DllImport` methods
  and function pointers were reported or failed closed. The worker never
  refuted an effect claim whose `throw` is caught locally or whose
  allocation is unreachable. SP0027 handled constructor calls,
  `this(...)`/`base(...)` initializers and target-typed `new` correctly.
  `[SharpProofSuppress]` on a class hid its method-level diagnostics but
  not SP0024 contract-placement errors or SPCF companion errors.
- **Completion, operators and summary import.** Helpers whose `throw` is
  caught internally (by the exact type, a base type, a filter, a bare
  `catch`, after a nested `finally`, or after a rethrow) were treated as
  returning, so writes after the call were reported. `using` in loop
  bodies, `switch` sections, `lock` bodies, after a `goto` label, in
  `do`/`while (false)` and in `else` branches of helpers was reported.
  User-defined `operator true`/`false`, `&`/`|` under `&&`/`||`, implicit
  and explicit conversions and `++` failed closed in the selected method
  and were reported through helpers. A callee's effect summary was imported
  only when its `Requires` or `[Positive]` precondition was established at
  the call site (a violated literal, an unknown argument, a guard on another
  variable and a weaker guard all gave SP0047 `CallPreconditionNotProven`).
- **Other specially modeled constructs.** `??=` into a static field, an
  array element, an object field or with a side-effecting right-hand side
  was reported, while `??=` on locals and by-value parameters was accepted.
  Interpolated strings converted to `FormattableString` or `IFormattable`
  were reported as allocating (and, correctly, not as running `ToString`).
  `volatile` reads, `lock`, `Interlocked` and `Thread.MemoryBarrier` were
  reported under `[AllowedCapabilities(None)]`. Lifted `checked`
  increments, compound assignments, negation and multiplication on
  nullable integers were reported (only lifted *conversions* are affected by
  the checked-conversion entry above). Newer language features were handled
  conservatively: accessors that write through the `field` keyword
  (including a lazy `field ??= new()` getter) and C# 14 `extension` block
  properties and methods with side effects were reported or failed
  closed. Members that *always* throw were reported when reached through a
  helper, whether a property getter, indexer, setter, user-defined `+`,
  explicit or implicit conversion, `operator true`, custom event `add`,
  constructor, record copy constructor (`with`), or a `Length` or property
  used by a list or property pattern; deconstruction was the only
  construct where an always-throwing member was lost (see the P0 entry),
  and even there a `catch` around it was correctly treated as reachable.
  Synchronous callers of `async Task`, `async ValueTask` and `async void`
  helpers were charged with the helpers' synchronous writes, exceptions and
  allocations. Exception escape through handlers was modeled precisely: a
  catch of a subtype or sibling, a filter that may be false (including a
  call that always returns `false`), `throw;`, a new throw in a catch, a
  throw in `finally` and a rethrow into an outer unrelated catch all kept
  the exception escaping, while exact, base-type and `when (true)` catches
  removed it.
- **Newer language features and other compiler-inserted calls.** Writes
  through `ref` fields, inline-array element access, collection expressions
  (including `[CollectionBuilder]` builders, collection-initializer `Add`
  methods and spreads), `params` collections and `fixed` over a
  `GetPinnableReference` all failed closed. C# 14 null-conditional
  assignment (`t?.F = 1`, `t?.F += 1`), partial property bodies, and
  C# 11 `checked` user-defined operators and conversions (the checked
  variant was the one charged in a `checked` context) were reported
  directly or through a helper. Apart from `++`/`--` and `ITuple` (see the
  P0 entries), user-defined conversions and overrides that the compiler
  calls implicitly were all reported through helpers: in compound
  assignment (including `int += wrapper`), unary `-` and `~`, relational
  operators, array indexes, conditional-expression arms, `??`, `??=`
  (including `Wrapper? w; w ??= 5`), tuple conversions, tuple deconstruction (from a
  tuple or a `Deconstruct` with convertible `out` types), tuple equality,
  `if` and `switch` governing expressions, compound string concatenation
  with a side-effecting or throwing `ToString`, record `==`, `Equals` and
  `GetHashCode` over a field with side-effecting overrides, anonymous-type
  `Equals`, `GetHashCode` and `ToString`, `ValueTuple.Equals`, `foreach`
  element casts and unboxing (including in a `catch` around the loop),
  and list-pattern `Slice` and range indexers on user types, strings and
  lists. Instance calls on a struct with an explicit static constructor
  were charged with its type initialization, which is what the runtime
  does (a class instance was, correctly, not). The effect analysis does no
  exact-type devirtualization, so stale receiver types after a reassignment
  cannot hide an override. `new T()` through a generic helper was charged
  with the instantiated constructor's writes and exceptions, and a
  `System.Threading.Lock` in `lock` failed closed. Synthesized record
  `Deconstruct` and `ToString` members that call user-written property
  getters were charged with the getters' effects. C# 14 partial
  constructors and partial events were charged with their implementation
  bodies, and a C# 13 `^` index in a nested object initializer
  (`new Holder { Items = { [^1] = 3 } }`) was charged with the write into the
  array its getter returns. (User-defined instance compound-assignment and
  increment operators could not be tested: Roslyn 4.14 rejects them.)
- **Aliases, struct receivers and recursion.** Writes through pattern
  variables (`is`, `switch` case labels, switch-expression arms, property,
  list and `not` patterns), deconstruction variables, `out var` results,
  tuple element copies and `using var` resources that alias a caller's
  object were all reported. So were struct instance methods (including
  `this = ...`) called on an element of a caller's array, on a struct
  field of a caller's object, on a nested struct field, and through a
  `ref` local, while the same call on a by-value struct copy was accepted.
  Mutually recursive helpers with a static write or `throw` anywhere in the
  cycle were reported in either declaration order, and a helper whose
  only normal return goes through a mutually recursive call was still
  treated as able to return, so writes and handlers after it were kept.
- **Flow facts through aliases, dispatch and imported preconditions.** A
  local changed through a `ref` local, a conditional `ref`, a `ref` or
  `out` argument, a lambda, a local function, a `Span` over it, a
  deconstruction or compound assignment through a `ref`, or a `ref`
  reassignment was never assumed to keep its old value: divisions, null
  dereferences and indexing after each change were reported. Writes
  through `ref`-returning properties, indexers and methods were reported.
  Calls on a receiver whose static type is sealed were resolved to the
  override the runtime calls, including one inherited from a non-sealed
  intermediate class. A callee's effect summary was not imported when its
  `Requires` was violated or unproven at a property setter, `init`
  accessor in an object initializer, indexer getter, constructor,
  instance or extension method, compound assignment or increment call site
  (SP0047 `CallPreconditionNotProven` in every case). `ToString` on
  `Nullable<T>` of a user struct, through `+`, `+=`, interpolation and a
  direct call, was charged with the struct's override.
- **Patterns, operators and records.** Property patterns (including
  extended `{ Inner.Value: 1 }` patterns, `or`/`not` combinations, switch
  statement case labels, switch-expression arms, list-pattern elements and
  `var` subpatterns) were charged with the getters they call, including
  interface getters and a getter that always throws. `checked` user-defined
  `++` and `--` operators were selected in a `checked` context and charged.
  A user-defined `==` or `!=` that does not behave like a null check was not
  trusted as one, while `is null` was. `with` on a non-sealed record class
  was treated as a virtual clone. Facts about struct fields, object fields,
  static fields and array elements were invalidated by `ref` calls,
  mutating struct methods, calls on the object, writes through a possible
  alias and static mutators.
- **More handler reachability.** A `catch` reached only through a struct
  resource's throwing `Dispose` (a `using` statement or declaration), a
  custom enumerator's throwing `MoveNext`, `Current` or `Dispose`, a
  throwing `ToString` in `+`, `+=`, `string.Concat` or interpolation (with
  and without alignment, for classes and structs), a throwing property in a
  property pattern, switch-expression arm or `when` guard, a throwing
  `Length` in a list pattern, or a throwing `Deconstruct` in a positional
  pattern was treated as reachable, so its writes were reported. Native
  integer division was charged with both `DivideByZeroException` and the
  `MinValue / -1` `OverflowException` (conservatively, since guards on
  `nint`/`nuint` divisors are not used). Contract expressions that can be
  undefined (division or remainder by a parameter, `checked` overflow,
  `MinValue / -1`) gave `PostconditionMayBeUndefined` unless a
  short-circuit guard made them defined, and SP0027 stayed silent for calls
  after an always-throwing helper, after a folded early `return`, and in
  branches that cannot run. Exception filters that throw were modeled the
  way the runtime behaves: the filter's own exception was swallowed and the
  original exception kept escaping, and a `catch` whose filter always
  throws was treated as never entered. Because the CLR evaluates an outer
  filter before an inner `finally` runs (two-pass handling), a filter such
  as `when (d == 0 && Note())` after `try { ... } finally { d = 1; }` does
  run `Note()`; the analysis did not use the post-`finally` value of `d` to
  prune the filter, so the write and the escaping exception were both
  kept.
- **Documented SP0024 triggers and source effect declarations.** Every
  SP0024 case listed in `docs/diagnostic-examples.md` (blank
  `[SharpProofTrusted]` and `[SharpProofSuppress]` reasons, `[NotNull]` on
  a value type, `[Positive]` and `[InRange]` on unsupported types, unordered
  `[InRange]` bounds, conditional and late `Requires`, non-exception
  `[AllowedExceptions]` types and undefined capability flags) was reported
  with a specific message, on parameters and on return values. A source
  method whose `[EffectContract]` does not cover its own body was reported
  (SP0047 `EffectContractDoesNotCoverBodySummary`), and its callers were
  judged by the body rather than the declaration, so they were not proven.
- **Effect refutations.** No false `Refuted` effect claim was found. Claims
  that are true at run time only because a `finally` or `Dispose` replaces
  the thrown exception, because a `[Conditional]` call and its arguments
  are elided, because the allocation or `throw` follows a call that never
  returns, or because a `throw` is caught locally were all `Proven` or
  `Unknown`.
- **Contracts over small and unsigned integers.** Postconditions over
  `uint`, `ushort`, `byte`, `sbyte`, `short` and `char` arithmetic,
  wrapping casts, shifts and bitwise operators were all `Unknown`
  (`UnsupportedBody` or `UnsupportedExpression`) rather than guessed, and
  the two that only widened or compared values (`long r = a;` for a `uint`
  `a`, and a mixed `uint`/`int` comparison) were correctly `Proven`.
- **Resource bounds.** Apart from the stack-overflow and exponential-time
  entries above, pathological shapes stayed within budget: 40 nested
  `try`/`catch` blocks and 25 nested `try`/`finally` blocks were analyzed
  in about two seconds, and an 800-case `switch`, 200 sequential
  `try`/`catch` statements, 1,000 to 2,000 sequential `if` statements and
  300- and 400-deep nested `if` statements were cut off with SP0047
  `BlockBudgetExceeded`. Sixty sequential `if`/`else` diamonds (2^60 paths)
  were analyzed in a second. Relational summaries over doubling helper
  chains stopped growing and fell back to `Unknown` beyond ten levels, so
  manifests stayed under a megabyte. Nesting 500 `if` statements overflows
  inside Roslyn's own `CSharpOperationFactory`, which is outside SharpProof.
  Analyzer time grew linearly with the number of annotated methods (500 to
  4,000 `[DoesNotThrow]` methods, each calling two helpers, took 1.4 to 5.9
  seconds), and 800 annotated methods over one 800-deep chain of
  expression-bodied helpers took about a second, so shared summaries are
  reused across selected methods. A single method with 2,000 violating
  top-level calls produced all 2,000 SP0027 diagnostics in under five
  seconds.
- **Closed attributes and integer domains.** `[InRange]`, `[Positive]` and
  `[NotNull]` on parameters and return values produced correct `Proven` and
  `Refuted` results, and an inverted `[InRange(10, 0)]` failed closed
  (`UnsupportedContract`). Source intervals for `sbyte`, `byte`, `short`,
  `ushort`, `char`, `int` and `uint` parameters were exact at both ends: each
  false bound was refuted at the boundary value and each true bound was
  proven with a `domain:` core. `[ContractFor]` companion clauses are
  checked against the target's own body (a false companion `Ensures` was
  refuted) and are not trusted by callers. A `Requires` in an interface
  companion was not assumed when verifying a class that implements the
  interface (its postcondition was refuted with an input the companion
  excludes).
- **Reference-pack identities.** Compiled against the `netstandard2.1`
  reference pack instead of the runtime, the analyzer produced the same
  results as on .NET 9: runtime exceptions resolved to the `netstandard`
  types, `[AllowedExceptions(typeof(DivideByZeroException))]` matched them,
  the `Math.Abs(int)` specification (including its `OverflowException`)
  was found through the `netstandard` facade, and SP0027 reported a
  `[Positive]` violation.
- **Identity and configuration.** Summaries are resolved per symbol, so two
  same-named `file`-local helpers do not share evidence. `#line` directives
  (including `hidden` and span forms) do not disturb manifest emission.
  Invalid `SharpProofFeatures` values are reported as SP0025 rather than
  silently disabling analysis, and `[SharpProofSuppress]` removes reporting
  without producing a proof.
