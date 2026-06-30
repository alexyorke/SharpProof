# Symbolic Invariant Queries

`PurelySharp.Symbolic` exposes a Roslyn-based invariant query surface that can be used without the analyzer package.
The API accepts a `SyntaxTree` plus `Compilation`, or raw source/file helpers that create a compilation from trusted platform references.

The primary entrypoint is `SymbolicSourceQueryService`:

- `QuerySyntaxTree` reports the merged invariant at a specific line and column.
- `QuerySyntaxTreeAtPosition` reports the merged invariant at an absolute source position.
- `QuerySyntaxTreeLine` reports statement-level program points that intersect a source line. Pass `includeExpressionProgramPoints: true` to also include selected expression nodes such as calls, element access, member access, assignments, binary expressions, conditionals, and patterns.
- `QuerySyntaxTreeAllLines` reports every non-empty invariant line from one parse/compilation pass. It accepts the same expression-program-point option for callers that need finer line-level query results.
- `SymbolicSourceQueryResult.Invariant` exposes a typed program-point invariant descriptor. Its `Conditions` are the SMT-backed path conditions, `MergeKind` is `Conjunction`, and `MergedInvariantText` is the condition conjunction used for proof queries.
- `SymbolicSourceQueryResult.PathConditions` is a convenience view over the typed condition descriptors, including source-like condition text such as `value > 0`, formula kind, SMT value kind, merge target, whether the condition came from a real SMT formula, and whether it is a conservative unknown placeholder.
- `SymbolicSourceQueryResult.PathConditionCount` and `ProofOutcomes` summarize the current point without requiring callers to traverse `PathConditions` or `ConditionProofs`. `SymbolicConditionProofSummary.TotalCount` is included on aggregate proof summaries.
- `SymbolicSourceQueryResult` includes both the queried point (`Line`, `Column`, `Position`) and the selected node span (`NodeSpanStart`, `NodeSpanEnd`, `NodeSpanLength`, `NodeStartLine`, `NodeStartColumn`, `NodeEndLine`, `NodeEndColumn`) so callers can re-query either by line/column or absolute position.
- `SymbolicSourceQueryResult.MethodName` reports the containing method, local function, constructor, destructor, or operator name when the selected node is inside one.
- `SymbolicSourceQueryResult.ProgramPointKind` is `Statement`, `Expression`, or `Other`, which lets tools distinguish expression points returned by `includeExpressionProgramPoints` from statement-level points without relying on Roslyn node-kind suffixes.
- `SymbolicInvariantResult.ConservativeUnknownCount` and `HasConservativeUnknowns` expose conservative placeholders directly on invariant summaries.
- `SymbolicLineQueryResult` exposes both per-program-point invariants and line-level summaries. `Facts` and `ObservedInvariant` remain the distinct union of facts observed across retained program points. `MergedInvariant` is conservative and uses `MergeKind=ConservativeFactMerge`.
- `SymbolicMergedPathFacts` backs conservative line and file merges. It separates facts that hold at every retained reachable-or-unknown program point (`AlwaysFacts`), facts that were observed but do not hold everywhere (`MaybeFacts`), and conservative placeholders such as `unknown(value)` (`ConservativeUnknowns`).
- `SymbolicMergedPathFacts.ConservativeUnknownDiagnostics` explains each conservative placeholder with the target, placeholder text, reason, maybe-facts that caused it, and retained candidate/unreachable program-point counts.
- When reachability is checked, unreachable program points are excluded from the conservative merge. If all retained points are unreachable, the conservative merged invariant is `false`.
- `SymbolicFileQueryResult` exposes all line results plus `ObservedInvariant`, conservative `MergedInvariant`, `MergedPathFacts`, reachability summaries, and implication summaries. File observed invariants still use `MergeKind=DistinctFactUnion`.
- `SymbolicLineQueryResult.ProgramPointSummary` and `SymbolicFileQueryResult.ProgramPointSummary` expose aggregate point count, total and maximum path-condition counts, reachability counts, and proof-outcome counts. These summaries are derived from retained program points and are recomputed after `SymbolicSourceQueryFilter` is applied.
- `SymbolicSourceQueryFilter` can post-filter line or file results by exact node kind, program-point kind, exact source line, line range, containing method name, method-name substring, presence of facts, presence of path conditions, path-condition target, exact path-condition text, path-condition substring, reachability, presence of proofs, proof outcome, exact proof condition, or proof-condition substring; aggregate summaries are recomputed from the retained points.
- `ToCompactResult(...)` on point, line, and file results returns a stable machine-readable projection with `schemaVersion`, observed raw SMT facts, conservative source-like merged invariants, direct `mergedInvariantText`, condition targets, conservative-unknown counts, reachability counts, proof summaries, proof outcome counts, compact `analysisSummary` certainty metrics, SMT diagnostics, conservative-unknown diagnostics, and truncation flags.
- `analysisSummary` is the smallest status surface for tools that do not want nested payloads: it includes program-point count, invariant condition count, conservative unknown count, total/max path-condition counts, checked/known/unknown reachability counts, resolved/unknown proof counts, SMT enablement, executed query count, and `hasUnresolvedAnalysis`.
- `SymbolicCompactQueryOptions.SummaryOnly` is a preset for aggregate-only compact output. It keeps invariant, reachability, proof, and truncation metadata while omitting nested line and program-point arrays.
- Compact program points include file path, line, column, absolute position, node span start/end/length, node start/end line and column, containing method name, program-point kind, direct merged invariant text, reachability and reachability reason, proof outcomes, and bounded proof details. This metadata remains available at the top level for a single point result even when `--max-points 0` suppresses nested program point arrays.
- `QueryFileLine` and `QuerySourceLine` are convenience wrappers for standalone tools.
- `QueryFileAllLines` and `QuerySourceAllLines` provide the same all-lines summary for file or raw source input.

Pass a bounded `SmtAnalysisService` to classify reachability or prove `--implies` conditions.
If SMT is disabled, times out, exceeds budget, or cannot load its native solver, callers should treat the result as unknown rather than proven.

The CLI mirrors the same API:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --line 42 --line-invariants --check-reachability --implies "index >= 0"
```

Use `--position` to query the statement or expression at a 0-based absolute source position:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --position 128 --check-reachability
```

Use `--all-lines` to enumerate every source line with statement/expression program points from one parse/compilation pass:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --all-lines --check-reachability --json
```

Use `--line-expressions` with `--line-invariants` or `--all-lines` when a caller needs expression-level points without computing absolute positions. This is useful for querying the invariant at a call, element access, member access, or arithmetic expression on a line:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --line 42 --line-invariants --line-expressions --program-point-kind Expression --node-kind AddExpression --check-reachability --implies "index >= 0" --compact-json
```

Use `--node-kind`, `--program-point-kind`, `--filter-line`, `--line-start`, `--line-end`, `--method`, `--method-contains`, `--with-facts`, `--with-conditions`, `--condition-target`, `--condition`, `--condition-contains`, or `--reachability` with `--line-invariants` or `--all-lines` to narrow aggregate output without changing symbolic inference:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --all-lines --method TryParse --node-kind ReturnStatement --filter-line 42 --condition-target index --with-conditions --json
```

Use `--with-proofs`, `--proof-outcome`, `--proof-condition`, and `--proof-condition-contains` after `--implies` to keep only points with matching implication results:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --all-lines --line-expressions --method-contains Parse --program-point-kind Expression --implies "index >= 0" --with-proofs --proof-outcome ProvenTrue --compact-json
```

Use `--compact-json` when a tool needs a smaller stable shape instead of the full public object graph:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --all-lines --check-reachability --implies "index >= 0" --compact-json --max-lines 25 --max-points 100 --max-facts 20 --max-conditions 20 --max-proofs 20
```

Use `--summary-only` to emit compact JSON with file or line aggregate metadata but no nested line or program-point arrays:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --all-lines --check-reachability --implies "index >= 0" --summary-only
```

All-lines `--json` is a single full `SymbolicFileQueryResult` object with string enum values. `--compact-json` emits lower-camel-case JSON with `schemaVersion`, `kind`, file/line/program-point counts, string enum values, `observedInvariant`, `conservativeInvariant`, direct `mergedInvariantText`, method names and program-point kinds on program points, invariant `targets`, conservative unknown counts, reachability counts, proof summaries, proof outcomes, compact `analysisSummary`, `smtDiagnostics`, bounded nested line/program-point arrays, conservative-unknown diagnostics, and `truncation` flags. `--max-lines`, `--max-points`, `--max-facts`, `--max-conditions`, and `--max-proofs` apply only to `--compact-json`; totals remain untruncated so callers can detect omitted details.

Text output includes total line count, lines with program points, total program points, program-point kind on points, program-point summary, conservative merged invariant text, invariant condition details, conservative unknown counts, observed distinct fact count, observed invariant metadata, and aggregate reachability or implication counts when requested. File-level observed facts and summaries are an overview, not a single invariant that holds at every program point; use each point result with `MergeKind=Conjunction` for actual path invariants.

For line and file aggregates, prefer `MergedInvariant` when a caller needs a conservative summary. Prefer `ObservedInvariant` or `Facts` when a caller needs every fact seen at any retained program point. `Facts` remains the raw SMT text from the symbolic engine; source-like condition text is exposed through typed path conditions and compact conservative invariant summaries. An `unknown(target)` entry means the query layer saw path facts about that target, but those facts were not common to every retained reachable-or-unknown point, so the aggregate cannot soundly claim one concrete fact.

Use `--json` or `--compact-json` for machine-readable output. The CLI does not hardcode BCL predicates; it only reports facts produced from the Roslyn syntax and semantic model.
