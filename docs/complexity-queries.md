# Complexity Queries

SharpProof exposes a conservative symbolic complexity surface for asking:
"What is the best proven asymptotic cost of this method-like body?"

The current result model is intentionally bounded. It does not attempt to infer
real wall-clock performance, allocation cost, cache effects, or JIT behavior.

## Result Shape

`SymbolicQueryService.QueryComplexity(...)` returns a
`SymbolicComplexityResult` with:

- a normalized `Complexity` summary such as `O(1)`, `O(n)`, `O(n * m)`,
  `O(n^2)`, `O(Unknown)`, or `O(RecursiveUnknown)`
- structural `Drivers`
- `UnknownReasons`
- `CalleeSummaries`
- containing method/span metadata

Current complexity kinds:

- `Constant`
- `Linear`
- `Product`
- `Quadratic`
- `Max`
- `Unknown`
- `RecursiveUnknown`

## Supported Reasoning

The first tranche is deliberately conservative and focuses on shapes the repo
can already justify:

- straight-line code as `O(1)`
- bounded `for` loops with recognized induction and upper bounds
- supported `foreach` over arrays, spans, strings, and count-backed containers
- nested loops as multiplicative or quadratic when the bounds are recoverable
- conditionals as worst-case maximum
- some monotone `while` loops when the bound is provable
- bounded callee composition when the callee summary is known

Unsupported loops, recursion cycles, unknown callees, or unsupported
operations do not get guessed. They stay conservative.

## Library API

Use `SymbolicQueryService.QueryComplexity(...)` with:

- `SymbolicSourceInput.FromFile(...)`
- `SymbolicSourceInput.FromText(...)`
- `SymbolicSourceInput.FromSyntaxTree(...)`
- `SymbolicSourceInput.FromNode(...)`

Supported target shapes:

- `SymbolicQueryTarget.Point(...)`
- `SymbolicQueryTarget.Position(...)`
- `SymbolicQueryTarget.Line(...)`
- `SymbolicQueryTarget.Node(...)`

Complexity queries resolve the containing method-like body. Invalid
source/target combinations are API misuse and currently throw
`NotSupportedException`.

## CLI

The symbolic CLI exposes complexity queries through `--complexity`:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --complexity
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --position 128 --complexity --json
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --complexity --compact-json
```

Current CLI target support is intentionally narrow:

- `--line`
- `--line` with `--column`
- `--position`

Invalid combinations, such as `--complexity --all-lines`, fail with an
argument error rather than returning a lossy aggregate.

## Limitations

- Complexity queries are method-level only in the current tranche.
- Unknown external calls can force `Unknown`.
- Recursive cycles currently return `RecursiveUnknown`.
- The current engine does not model floating-point, allocation complexity, or
  richer amortized/container-specific costs.
