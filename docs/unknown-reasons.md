# Typed abstention reasons

SharpProof represents uncertainty with closed enums rather than reason strings.

Frontend lowering can abstain for unsupported operation kinds, types, member or
invocation shapes, control flow, statements, mutation, conversions,
user-defined or lifted operators, invalid/error operations, and future unknown
Roslyn kinds.

The analyzer language gate separately records closed reasons for unsupported
callables, missing operation roots, operation kinds, types, and operation
shapes. These abstentions remain intentionally silent.

Proof verification can abstain for unsupported operations or encoding,
approximations touching the goal, missing API specifications, resource limits,
timeouts, unavailable backends, infrastructure failure, malformed backend
results, or failed counterexample replay.

The worker protocol adds callable, contract, body, expression, deep-Ensures,
return-value, method-timeout, and project-timeout reasons. Worker responses
retain the typed reason for automation and debugging.

Display messages may contain strings. Semantic branching, cache identity, and
proof evidence must use the typed values.
