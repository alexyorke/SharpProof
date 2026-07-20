# Symbolic Invariant Queries

Sibling symbolic query surfaces are documented separately:

- [Capability analysis](capability-analysis.md)
- [Complexity queries](complexity-queries.md)

SharpProof exposes an invariant query surface through the current
`SharpProof.Symbolic` assembly. It can be used without the
analyzer package.
The API accepts source text or a file and can reuse a project-loaded session.

The primary entrypoint is `SharpProofAnalysisSession`:

- `Analyze(new SharpProofQuery(SharpProofQueryKind.Invariant, target))` reports invariants for a file, text buffer, or loaded project.
- `FromFile` and `FromText` create compilation-scoped sessions.
- A `SharpProofTarget` record plus `SharpProofTargetKind` selects a program point or aggregate scope.
- `SharpProofAnalysisOptions` carries SMT enablement, implied conditions, and bounded-analysis limits.
- `SharpProofQueryKind.Condition` checks whether a source-level condition follows at a point.
- `SharpProofQueryKind.RuntimeHazards` queries proven or optionally unproven runtime-hazard candidates through the same session.
- Internal program-point state retains typed invariants, source spans, containing-method identity, point kind, path conditions, and proof outcomes. Public callers receive the focused `SourceQueryPayload`; the CLI compatibility projector consumes the richer internal snapshot.
- `SymbolicInvariantResult.ConservativeUnknownCount` and `HasConservativeUnknowns` expose conservative placeholders directly on invariant summaries.
- A `SourceQueryPayload` exposes the conservative merged invariant and bounded program-point count. Detailed legacy program-point state remains internal to the engine and CLI projection.
- `SymbolicMergedPathFacts` backs conservative line and file merges. It separates facts that hold at every retained reachable-or-unknown program point (`AlwaysFacts`), facts that were observed but do not hold everywhere (`MaybeFacts`), and conservative placeholders such as `unknown(value)` (`ConservativeUnknowns`).
- `SymbolicMergedPathFacts.ConservativeUnknownDiagnostics` explains each conservative placeholder with the target, placeholder text, reason, maybe-facts that caused it, and retained candidate/unreachable program-point counts.
- When reachability is checked, unreachable program points are excluded from the conservative merge. If all retained points are unreachable, the conservative merged invariant is `false`.
- File and line aggregation remains conservative. CLI filters are applied by the internal projection layer before external JSON/text rendering.
- The CLI serializes the canonical point, line, file, capability, complexity, and runtime-hazard results through one stable lower-camel `--json` policy.
- `analysisSummary` is the smallest status surface for tools that do not want nested payloads: it includes program-point count, invariant condition count, conservative unknown count, total/max path-condition counts, checked/known/unknown reachability counts, resolved/unknown proof counts, SMT enablement, executed query count, and `hasUnresolvedAnalysis`.
- `SharpProofQueryResult` is the unified public result for point, line, span, and all-lines queries. Typed payloads expose focused summaries; JSON projection belongs to the CLI adapter.
- `AnalysisTruncation` on query and runtime-hazard results reports stable `analysis_limit.*` events whenever a configured fact, branch, depth, or state-merge cap drops proof evidence. See [bounded analysis limits](analysis-limits.md).
- `SourceQueryPayload.Smt` includes the stable solver health state, last failure code, and executed-query count. See [SMT lifecycle and health](smt-lifecycle.md).
- Program points expose `ReachabilityWitness` and `InputDomainSummary`; line,
  span, file, and unified query results expose alternative
  `ReachabilityWitnesses` and a conservatively merged domain summary.
  Implication proofs and runtime hazards expose outcome/counterexample and
  `path && trigger` witnesses respectively. The detailed precision contract is
  documented in [solver witnesses and input domains](input-witnesses.md).

Pass a bounded `SmtAnalysisService` to classify reachability or prove `--implies` conditions.
If SMT is disabled, times out, exceeds budget, or cannot load its native solver, callers should treat the result as unknown rather than proven.

Length, count, and index facts include arrays, strings, `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ReadOnlyMemory<T>`, and supported count-backed collections when Roslyn syntax and semantic-model lowering can prove them. Direct local and parameter copies preserve known length facts, exact parameterless `List<T>` constructions preserve `Count` facts, and supported span/memory `Slice` results expose derived `Length` invariants.

Runtime type tests are represented as Z3-backed reference predicates. That lets `is`, declaration/type patterns, switch pattern exclusions, and guarded casts share the same path facts without hard-coded method or branch special cases.

The CLI mirrors the same API:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --line-invariants --check-reachability --implies "index >= 0"
```

Use `--position` to query the statement or expression at a 0-based absolute source position:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --position 128 --check-reachability
```

Use `--all-lines` to enumerate every source line with statement/expression program points from one parse/compilation pass:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --check-reachability --json
```

Use `--line-expressions` with `--line-invariants` or `--all-lines` when a caller needs expression-level points without computing absolute positions. This is useful for querying the invariant at a call, element access, member access, or arithmetic expression on a line:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --line-invariants --line-expressions --program-point-kind Expression --node-kind AddExpression --check-reachability --implies "index >= 0" --json
```

Use `--node-kind`, `--program-point-kind`, `--filter-line`, `--line-start`, `--line-end`, `--method`, `--method-contains`, `--with-facts`, `--with-conditions`, `--condition-target`, `--condition`, `--condition-contains`, or `--reachability` with `--line-invariants` or `--all-lines` to narrow aggregate output without changing symbolic inference:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --method TryParse --node-kind ReturnStatement --filter-line 42 --condition-target index --with-conditions --json
```

Use `--with-proofs`, `--proof-outcome`, `--proof-condition`, and `--proof-condition-contains` after `--implies` to keep only points with matching implication results:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --line-expressions --method-contains Parse --program-point-kind Expression --implies "index >= 0" --with-proofs --proof-outcome ProvenTrue --json
```

Use `--json` for the canonical lower-camel machine-readable result. See the
[proof/evidence compatibility policy](evidence-schema.md) before persisting or
strictly validating evidence fields.

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --check-reachability --implies "index >= 0" --json
```

## Runtime Hazard Queries

The same CLI can query runtime hazards instead of invariant program points. Runtime hazard queries support `--line`, `--span-start`/`--span-end`, or `--all-lines`; they do not use `--position`, invariant proof flags, or invariant program-point filters.

By default, `--runtime-hazards` returns only hazards with `Status = Proven`. Add `--include-unproven-hazards` when a tool wants to inspect `Unknown`, `Unreachable`, or `Unsupported` candidates.

Known remaining runtime-hazard gaps: failed `as` conversions do not yet become reusable negative type facts, dynamic binder modeling is limited to null receivers, array covariance stores can be missed through aliases or merged array identities, and richer throw-expression flow remains limited to currently proven `throw null` cases.

Query proven hazards on one line:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --runtime-hazards
```

Query all proven null-dereference hazards with JSON output:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --runtime-hazards --hazard-kind NullDereference --json
```

Inspect unknown candidates as a machine-readable result:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --runtime-hazards --include-unproven-hazards --hazard-status Unknown --json
```

Fail the process when the final filtered hazard output is non-empty:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --all-lines --runtime-hazards --hazard-kind DivideByZero --fail-on-hazard
```

Hazard filters include `--hazard-kind`, `--hazard-status`, `--hazard-exception-type`, and `--hazard-category`. JSON preserves typed hazard entries, counts, analysis truncation, and SMT diagnostics. Library callers consume the same `SymbolicRuntimeHazardQueryResult` graph serialized by the CLI.

All-lines `--json` emits the canonical lower-camel query graph with string enum values, program points, merged facts, summaries, diagnostics, and typed analysis-truncation events.

Text output includes total line count, lines with program points, total program points, program-point kind on points, program-point summary, conservative merged invariant text, invariant condition details, conservative unknown counts, observed distinct fact count, observed invariant metadata, and aggregate reachability or implication counts when requested. File-level observed facts and summaries are an overview, not a single invariant that holds at every program point; use each point result with `MergeKind=Conjunction` for actual path invariants.

For line and file aggregates, prefer `MergedInvariant` when a caller needs a conservative summary. Prefer `ObservedInvariant` or `Facts` when a caller needs every fact seen at any retained program point. `Facts` remains the raw SMT text from the symbolic engine; source-like condition text is exposed through typed path conditions and conservative invariant summaries. An `unknown(target)` entry means the query layer saw path facts about that target, but those facts were not common to every retained reachable-or-unknown point, so the aggregate cannot soundly claim one concrete fact.

Use `--json` for machine-readable output. The CLI does not hardcode BCL predicates; it only reports facts produced from the Roslyn syntax and semantic model.

Invalid source/target combinations are API misuse. The library currently throws
`NotSupportedException` for unsupported combinations, and the CLI exits with an
argument error for unsupported flag combinations.
