# Unknown evidence

SharpProof uses `Unknown` when available evidence cannot prove or disprove a
derived property. Unknown is not a contract violation fact, although a contract
such as `[EnforcePure]` still fails verification unless its verdict is Proven.

`SharpProofUnknownReason` carries a stable code, category, message, retryability,
and configuration classification. Common categories cover unresolved dispatch,
unsupported operations, missing or malformed metadata, recursive cycles,
cancellation, and exhausted analysis budgets. Effect sites retain the operation,
origin, transitive source, and proof status that produced the reason.

Consumers should branch on the result status and verdict enum. Messages are for
humans and may improve without changing semantics. Budget truncations are also
available through `SharpProofAnalysisResult.Budget`.
