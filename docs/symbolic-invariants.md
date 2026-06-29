# Symbolic Invariant Queries

`PurelySharp.Symbolic` exposes a Roslyn-based invariant query surface that can be used without the analyzer package.
The API accepts a `SyntaxTree` plus `Compilation`, or raw source/file helpers that create a compilation from trusted platform references.

The primary entrypoint is `SymbolicSourceQueryService`:

- `QuerySyntaxTree` reports the merged invariant at a specific line and column.
- `QuerySyntaxTreeAtPosition` reports the merged invariant at an absolute source position.
- `QuerySyntaxTreeLine` reports every statement/expression program point that intersects a source line.
- `QuerySyntaxTreeAllLines` reports every non-empty invariant line from one parse/compilation pass.
- `SymbolicLineQueryResult` exposes both the per-program-point invariants and a line-level merged fact summary.
- `SymbolicFileQueryResult` exposes all line results plus observed fact, reachability, and implication summaries.
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

All-lines JSON is a single `SymbolicFileQueryResult` object. Text output includes total line count, lines with program points, total program points, observed distinct fact count, and aggregate reachability or implication counts when requested. File-level observed facts are an overview, not a single invariant that holds at every program point; use each line or point result for actual merged invariants.

Use `--json` for machine-readable output. The CLI does not hardcode BCL predicates; it only reports facts produced from the Roslyn syntax and semantic model.
