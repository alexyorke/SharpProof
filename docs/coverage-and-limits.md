# Coverage and limits

SharpProof 0.2 is intentionally narrower than the 0.1 preview.

The supported analyzer surface is the effect cluster plus concretely replayed
call-site `Contract.Requires(...)` checks. Deep postcondition proving runs only
in the opt-in worker. Complexity analysis, regex-to-SMT translation, metadata
IL effect inference, standalone runtime-hazard queries, and nullable-contract
diagnostics are not active product features.

The language subset is enumerated in `LanguageSubsetGate`. Async, iterators,
closures, local functions, dynamic binding, unsafe code, ref-like and pointer
shapes, unsupported generics, and unknown future Roslyn operation kinds abstain
silently. Adding a construct requires lowering, interpreter, oracle, and
metamorphic coverage before it enters the allowlist.

External calls require either a resolved `ApiSpec` row or both an explicit
complete `EffectContract` and a non-blank `SharpProofTrusted` justification.
Missing, untrusted, or ambiguous models are unknown. Regex predicates are
opaque. Effects include implicit exceptions for supported operations, but do
not claim whole-runtime coverage. Path-insensitive may effects can prove that a
contract permits all possible effects, but a possible violation remains
`Unknown` until a concrete trace can be replayed.

The SMT backend is used only by `SharpProof.Worker`. It has deterministic Z3
resource limits and an outer process wall-time boundary. A timeout, native
failure, unsupported encoding, malformed model, failed replay, cancellation, or
exhausted budget cannot produce a proof and is never cached as a semantic
answer.

See `SEMANTICS.md` for the normative soundness rules.
