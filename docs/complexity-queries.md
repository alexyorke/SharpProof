# Complexity Queries

SharpProof exposes a conservative symbolic complexity surface for asking:
"What is the best proven asymptotic cost of this method-like body?"

It also exposes an opt-in analyzer contract:
`[ExpectedComplexity(ComplexityKind...)]`
for enforcing simple upper bounds such as `O(1)`, `O(n)`, and `O(n^2)`.

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

## Analyzer Contract

Declarable bounds (`ComplexityKind` values):

- `[ExpectedComplexity(ComplexityKind.Constant)]` — `O(1)`
- `[ExpectedComplexity(ComplexityKind.Logarithmic)]` — `O(log n)`
- `[ExpectedComplexity(ComplexityKind.Linear)]` — `O(n)`
- `[ExpectedComplexity(ComplexityKind.Linearithmic)]` — `O(n log n)`
- `[ExpectedComplexity(ComplexityKind.Quadratic)]` — `O(n^2)`
- `[ExpectedComplexity(ComplexityKind.Product)]` — `O(n*m)` over independent
  size parameters
- `[ExpectedComplexity(ComplexityKind.Max)]` — `O(max(n, m))` over independent
  size parameters

`Unknown` and `RecursiveUnknown` are reported inference states, not declarable
bounds.

Current diagnostics:

- `SP0021` when the inferred complexity definitely exceeds the declared bound
- `SP0022` when the declared bound could not be verified conservatively
- `SP0023` when `[ExpectedComplexity]` is applied to a non-method-like target

The comparison is a deliberately partial order. SharpProof only accepts bounds
it can justify conservatively:

- `Constant`, `Logarithmic`, `Linear`, `Linearithmic`, and `Quadratic` form a
  total chain, so a smaller inferred cost satisfies any larger declared bound
  (for example `O(n)` satisfies `Linear`, `Linearithmic`, and `Quadratic`).
- `Product` (`O(n*m)`) and `Max` (`O(max(n, m))`) involve independent size
  parameters, so they only satisfy their own declared kind and `O(1)` satisfies
  them. They are never coerced into or out of the single-variable chain.
- Any pairing the partial order cannot justify — such as `O(n * m)` against
  `Quadratic`, `O(max(...))` against `Product`, or an inferred `O(Unknown)` /
  `O(RecursiveUnknown)` — stays conservative and reports `SP0022`.

## CLI

The symbolic CLI exposes complexity queries through `--complexity`:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --line 42 --complexity
dotnet run --project Tools/SharpProof.SymbolicCli -- --file Example.cs --position 128 --complexity --json
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
