# Nullable contract verification

SharpProof verifies nullable promises against reachable normal completions. This
is separate from Roslyn's annotation compatibility checks: a declaration can be
well annotated while its implementation still violates the promised result,
parameter, or member state.

The analyzer verifies non-nullable returns, `[return: NotNull]`,
`[NotNullIfNotNull]`, `[NotNull]`, `[NotNullWhen]`, `[MaybeNullWhen]`,
`[MemberNotNull]`, and `[MemberNotNullWhen]`. Exceptional-only exits do not
violate normal-return postconditions. Unsupported lowering, solver timeout, and
unstable property getters remain unknown rather than being accepted as proofs.

Null-forgiving operators are classified as unsafe when a null counterexample is
proved, unnecessary when the operand is proved non-null, or unverified when the
bounded analysis is inconclusive. SP0045 offers a code fix that removes only the
unnecessary `!`.

Source guard methods such as a leading null check followed by `throw` contribute
a non-null normal-completion fact to callers. The same proof can produce opt-in
SP0046 suggestions for `[NotNull]` and return `[NotNull]` annotations by enabling
`sharpproof_suggest_inferred_contracts` and including `nullability` in
`sharpproof_suggest_inferred_contracts_kinds`.

Member facts are invalidated across calls unless canonical method effects prove
the call cannot mutate them. Locals remain stable after capture. Repeated property reads
are not treated as identical unless stability can be established; current
verification therefore reports unstable getter contracts as inconclusive.

External behavior is supplied through exact effect contracts or established by
lazy metadata analysis. Unavailable metadata remains unknown.
