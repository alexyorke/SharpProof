## Unshipped Release

### New Rules

| Rule ID | Category | Severity | Notes |
| ------- | -------- | -------- | ----- |
| SP0009 | Purity | Info | Optional purity diagnostic explanation emitted when `sharpproof_emit_explanations` is enabled. |
| SP0010 | ExceptionFlow | Info | Optional thrown-exception summary emitted when `sharpproof_report_exceptions` or `sharpproof_runtime_hazard_mode = summaries/all` is enabled. |
| SP0011 | ExceptionFlow | Warning | Optional uncaught call-site and runtime-hazard warning emitted when `sharpproof_checked_exceptions` or `sharpproof_runtime_hazard_mode = sites/all` is enabled. |
| SP0012 | Purity | Info | Optional non-authoritative BCL purity fallback guess emitted when `sharpproof_emit_explanations` or `sharpproof_report_bcl_fallback_guesses` is enabled. |
| SP0013 | Allocation | Warning | Reports direct source-visible allocation sites inside methods marked with `[ZeroAllocations]`. |
| SP0014 | Usage | Error | Reports `[ZeroAllocations]` when it is applied to a non-method declaration. |
| SP0015 | Capability | Warning | Reports source-visible operations or proven transitive callees that exceed a method's `[AllowedCapabilities]` contract. |
| SP0016 | Capability | Warning | Reports capability-contract operations whose required capabilities could not be fully verified conservatively. |
| SP0017 | Usage | Error | Reports `[AllowedCapabilities]` when it is applied to a non-method declaration. |

### Enhancements

- `SP0010` can consume generated `SharpProof.EffectSummary.json` additional files and propagate summarized metadata/library exception types through source callers.
- `sharpproof_runtime_hazard_mode` enables runtime-failure checks over ordinary methods without requiring purity attributes. `sites` emits `SP0011`; `summaries` emits `SP0010`; `all` emits both.
- `SP0010` infers typed `throw;` rethrows from enclosing catch clauses and still suppresses them when an outer catch handles the same exception type.
- `SP0010` reports definite integer/decimal divide-by-zero and modulo-by-zero expressions with compile-time constant zero divisors, excluding floating-point division.
- `SP0010` reports definite null dereferences on literal/default-null receivers and suppresses them when caught.
- `SP0010` emits structured exception evidence properties for categories and sources.
- `SP0011` pinpoints uncaught call sites that can propagate exceptions discovered from source analysis or generated `SharpProof.EffectSummary.json` summaries.
- `SP0011` also reports uncaught direct throws plus analyzer-proven divide-by-zero and null-dereference sites, and preserves multi-hop source-callee evidence chains.
- Corpus reports aggregate `SP0010` and `SP0011` counts plus exception categories and sources.
- Effect summary IL exception extraction recognizes constructed exception objects stored to and reloaded from locals before `throw`.
- Effect summary output includes assembly SHA-256, per-method IL SHA-256, and method cache keys for self-validating generated indexes.
- `SP0010` uses bounded branch path facts for simple local/parameter zero-divisor and null-receiver exception cases, including short-circuit conditions, `is not` else branches, and guard `if` false paths.
- `SP0002` evidence now includes low-confidence BCL fallback guess properties for otherwise unknown metadata BCL methods, constructors, properties, and fields; `SP0012` can surface the same guess when explanation diagnostics or dedicated BCL fallback reporting are enabled.
- `[ZeroAllocations]` adds direct allocation-site checks for explicit object, array, anonymous-object, collection-expression, delegate, boxing, and supported `with` expression allocations without requiring purity analysis to fail.
- `[AllowedCapabilities]` adds direct and transitive capability-contract checks plus conservative unknown-capability reporting for unsupported, dynamic, or insufficiently classified operations.
