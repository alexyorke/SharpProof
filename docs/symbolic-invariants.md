# Symbolic Invariant Queries

`PurelySharp.Symbolic` exposes a Roslyn-based invariant query surface that can be used without the analyzer package.
The API accepts a `SyntaxTree` plus `Compilation`, or raw source/file helpers that create a compilation from trusted platform references.

The primary entrypoint is `SymbolicSourceQueryService`:

- `QuerySyntaxTree` reports the merged invariant at a specific line and column.
- `QuerySyntaxTreeAtPosition` reports the merged invariant at an absolute source position.
- `QuerySyntaxTreeLine` reports every statement/expression program point that intersects a source line.
- `QuerySyntaxTreeAllLines` reports every non-empty invariant line from one parse/compilation pass.
- `SymbolicSourceQueryResult.Invariant` exposes a typed program-point invariant descriptor. Its `Conditions` are the SMT-backed path conditions, `MergeKind` is `Conjunction`, and `MergedInvariantText` is the condition conjunction used for proof queries.
- `SymbolicSourceQueryResult.PathConditions` is a convenience view over the typed condition descriptors, including condition text, formula kind, SMT value kind, and whether the condition came from a real SMT formula.
- `SymbolicLineQueryResult` exposes both the per-program-point invariants and a line-level merged fact summary through `MergedInvariant`. Line summaries use `MergeKind=DistinctFactUnion` because they aggregate facts observed across retained program points, not one path that must hold everywhere on the line.
- `SymbolicFileQueryResult` exposes all line results plus `ObservedInvariant`, reachability summaries, and implication summaries. File observed invariants also use `MergeKind=DistinctFactUnion`.
- `SymbolicSourceQueryFilter` can post-filter line or file results by exact node kind, presence of facts, or reachability; aggregate summaries are recomputed from the retained points.
- `QueryFileLine` and `QuerySourceLine` are convenience wrappers for standalone tools.
- `QueryFileAllLines` and `QuerySourceAllLines` provide the same all-lines summary for file or raw source input.

Pass a bounded `SmtAnalysisService` to classify reachability or prove `--implies` conditions.
If SMT is disabled, times out, exceeds budget, or cannot load its native solver, callers should treat the result as unknown rather than proven.

The CLI mirrors the same API:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --line 42 --line-invariants --check-reachability --implies "index >= 0"
```

Use `--all-lines` to enumerate every source line with statement/expression program points from one parse/compilation pass:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --all-lines --check-reachability --json
```

Use `--node-kind`, `--with-facts`, or `--reachability` with `--line-invariants` or `--all-lines` to narrow aggregate output without changing symbolic inference:

```powershell
dotnet run --project Tools/PurelySharp.SymbolicCli -- --file Example.cs --all-lines --node-kind ReturnStatement --with-facts --json
```

All-lines JSON is a single `SymbolicFileQueryResult` object. Text output includes total line count, lines with program points, total program points, observed distinct fact count, invariant merge kind, invariant condition count, and aggregate reachability or implication counts when requested. File-level observed facts are an overview, not a single invariant that holds at every program point; use each point result with `MergeKind=Conjunction` for actual path invariants.

Use `--json` for machine-readable output. The CLI does not hardcode BCL predicates; it only reports facts produced from the Roslyn syntax and semantic model.
