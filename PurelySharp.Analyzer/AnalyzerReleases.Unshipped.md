## Unshipped Release

### New Rules

| Rule ID | Category | Severity | Notes |
| ------- | -------- | -------- | ----- |
| PS0009 | Purity | Info | Optional purity diagnostic explanation emitted when `purelysharp_emit_explanations` is enabled. |
| PS0010 | ExceptionFlow | Info | Optional thrown-exception summary emitted when `purelysharp_report_exceptions` is enabled. |
| PS0011 | ExceptionFlow | Warning | Optional uncaught call-site warning emitted when `purelysharp_checked_exceptions` is enabled. |
| PS0012 | Purity | Info | Optional non-authoritative BCL purity fallback guess emitted when `purelysharp_emit_explanations` is enabled. |

### Enhancements

- `PS0010` can consume generated `PurelySharp.EffectSummary.json` additional files and propagate summarized metadata/library exception types through source callers.
- `PS0010` infers typed `throw;` rethrows from enclosing catch clauses and still suppresses them when an outer catch handles the same exception type.
- `PS0010` reports definite integer/decimal divide-by-zero and modulo-by-zero expressions with compile-time constant zero divisors, excluding floating-point division.
- `PS0010` reports definite null dereferences on literal/default-null receivers and suppresses them when caught.
- `PS0010` emits structured exception evidence properties for categories and sources.
- `PS0011` pinpoints uncaught call sites that can propagate exceptions discovered from source analysis or generated `PurelySharp.EffectSummary.json` summaries.
- `PS0011` also reports uncaught direct throws plus analyzer-proven divide-by-zero and null-dereference sites, and preserves multi-hop source-callee evidence chains.
- Corpus reports aggregate `PS0010` and `PS0011` counts plus exception categories and sources.
- Effect summary IL exception extraction recognizes constructed exception objects stored to and reloaded from locals before `throw`.
- Effect summary output includes assembly SHA-256, per-method IL SHA-256, and method cache keys for self-validating generated indexes.
- `PS0010` uses bounded branch path facts for simple local/parameter zero-divisor and null-receiver exception cases, including short-circuit conditions, `is not` else branches, and guard `if` false paths.
- `PS0002` evidence now includes low-confidence BCL fallback guess properties for otherwise unknown metadata BCL members, and `PS0012` can surface the same guess when explanation diagnostics are enabled.
