# Runtime Hazard Coverage Backlog

This file tracks bounded runtime-hazard gaps that need their own acceptance
criteria. A passing known-limitation test records current conservative behavior;
it does not mean the missing hazard is supported.

## RH-DYN-001: Non-null dynamic binder failures

Status: Open.

SharpProof models `RuntimeBinderException` when a dynamic receiver is null. It
does not yet model binder failures for a definitely non-null receiver, including
missing members, wrong argument shapes, invalid dynamic conversions, or invalid
indexing. The current null-binding candidate becomes unreachable, and there is
no separate candidate for the remaining binder failure.

Regression:
`SymbolicRuntimeHazardQueryTests.KnownLimitation_DynamicBinderMissingMemberOnNonNullReceiver_HasNoBinderHazard`.

Completion requires a distinct, stable binder-failure category and candidate
model. Definite cases should be proven, uncertain runtime shapes should remain
`Unknown`, and null-binding behavior must remain unchanged.

## RH-ARR-001: Covariant array stores after identity merges

Status: Open.

SharpProof proves a covariant store mismatch when a local has one traceable
exact runtime array type. When control flow merges multiple exact array
identities, that disjunction is currently discarded, so a store rejected by
every possible element type remains `Unknown`.

Regression:
`SymbolicRuntimeHazardQueryTests.KnownLimitation_ArrayCovarianceStoreAcrossMergedIdentities_RemainsUnknown`.

Completion requires bounded disjunctive runtime-type facts across branch and
state merges. A mismatch may be proven only when every feasible identity
rejects the stored value; mixed compatible/incompatible identities must remain
conditional or `Unknown`.

## RH-THROW-001: Throw expressions beyond `throw null`

Status: Verified as supported for direct source expressions.

The candidate and exception-flow pipelines already enumerate
`ThrowExpressionSyntax`, retain the static exception type, and handle normal
completion facts for coalesce and conditional expressions. This was a stale
limitation rather than an open gap.

Regression:
`SymbolicRuntimeHazardQueryTests.QuerySourceRuntimeHazardsLine_ReportsTypedCoalesceThrowExpression`.

Keep new throw-expression containers covered as C# syntax evolves. Transitive
delegate and unresolved-call propagation remain separate call-graph concerns,
not part of this item.
