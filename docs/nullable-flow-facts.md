# Shared Nullable-Flow Facts

SharpProof uses one nullable-flow contract reader across symbolic program-point
analysis and analyzer evidence. It combines Roslyn null-state analysis with
`System.Diagnostics.CodeAnalysis` contracts, then distributes the resulting
facts to `[Ensures]`, runtime-hazard, reachability, exception-flow, and purity
queries.

## Fact Precedence

Nullable facts are classified in this order:

1. An explicit CodeAnalysis contract on the parameter, return, property, or
   field.
2. Roslyn's `NullableFlowState` for the expression at its actual source
   position.
3. Conservative syntax facts for values that are intrinsically non-null, such
   as object, array, collection, and string creation expressions.

An explicit maybe-null contract wins over a non-nullable type annotation.
For example, `[MaybeNull] string Read()` remains maybe-null. Likewise,
`[AllowNull] string value` does not create a non-null method-entry assumption.

Facts are attached to symbol identity and are removed when a later assignment,
`ref`/`out` call, increment, or other tracked mutation can invalidate them.

## Supported CodeAnalysis Contracts

| Contract | Shared interpretation |
| --- | --- |
| `[AllowNull]` | A non-nullable input may be null; no non-null entry fact is added. |
| `[DisallowNull]` | A nullable input is assumed non-null at method entry. |
| `[MaybeNull]` | A non-nullable return, property, field, `ref`, or `out` result remains maybe-null. |
| `[NotNull]` | A nullable return or normally completed argument is non-null. |
| `[MaybeNullWhen(value)]` | The matching Boolean-result branch remains maybe-null; the opposite branch falls back to its declared output contract. |
| `[NotNullWhen(value)]` | The matching Boolean-result branch receives a non-null argument fact. |
| `[NotNullIfNotNull(name)]` | A method/property result is non-null when the named source argument is non-null. |
| `[MemberNotNull(...)]` | Normal completion establishes non-null facts for the named current-instance members. |
| `[MemberNotNullWhen(value, ...)]` | The matching Boolean-result branch establishes the named member facts. |
| `[DoesNotReturn]` | A completed call path is unreachable. |
| `[DoesNotReturnIf(value)]` | Normal completion establishes the inverse condition for the annotated argument. |

Property-level `AllowNull`/`DisallowNull` contracts are also applied to the
compiler-generated setter value parameter. Generic constructed symbols use the
same contracts as their original definitions.

## Consumers

- Program-point proofs add non-null method-entry facts, assignment facts, call
  completion facts, and conditional output facts to both typed IR state and
  formula compatibility state.
- `[Ensures]` uses the same parameter entry state and simplifies `result` null
  tests from the containing method's return contract before replacing the
  result placeholder.
- Runtime-hazard discovery omits a null-dereference trigger when the receiver
  is non-null under the shared expression classification. Maybe-null contracts
  keep unproven candidates available when the query requests them.
- Reachability and purity condition checks use the same classification for
  built-in `== null`, `!= null`, `is null`, and `is not null` tests.
- Exception-flow reuses the program-point facts and the same `NotNull` and
  `DoesNotReturnIf` contract reader; it no longer parses those attributes
  independently.

## Trust And Limits

CodeAnalysis attributes and enabled nullable annotations are contracts. They
are assumptions supplied by the analyzed program or referenced metadata, not
proofs that the annotated implementation honors them. A contradictory body can
therefore make a caller proof unsound in the same way that an incorrect
`[Requires]` or a complete exact `[EffectContract]` can.

`MaybeNull` and `AllowNull` are conservative overrides; they never prove that a
value is null. Dynamic dispatch, ambiguous member names, unsupported expression
lowering, and mutations that cannot be bounded remain unknown. Null-forgiving
expressions follow Roslyn's flow-state contract and should be used only when the
source intentionally assumes non-null behavior.
