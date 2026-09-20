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

The list currently has 19 entries: 0 P0, 0 P1 and 19 P3. The final
section records the areas that were probed without finding a defect.

## P3 - Low



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
