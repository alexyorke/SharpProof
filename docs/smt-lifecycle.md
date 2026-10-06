# SMT lifecycle

Native Z3 runs in the Linux amd64 verifier boundary. The portable analyzer does not load it. Pinned payload and loading rules are described in [native SMT packaging](native-smt-packaging.md).

## Construct an obligation

The worker validates the closed compiler artifact, prepares a typed Total program, and checks entry feasibility. Passive VC construction derives guarded scalar SSA, heap/store constraints, normal completion, exceptions, effects, and call obligations from the owned program.

Natural loops have separate proof and witness encodings. Invariant candidates need establishment and preservation checks. Opaque calls and approximated reads retain conservative values and effects.

Native effect claims use the same program through the exception/effect-site checks. A source-body proof is distinct from a trusted external boundary declaration.

## Run bounded native work

Worker budgets bound query resources, accumulated method resources, method wall time, project wall time, expression depth, and parallelism. The [analysis limits](analysis-limits.md) table lists current defaults.

A query result contains status and evidence rather than a boolean shortcut. Solver Unknown, cancellation, resource exhaustion, unavailable backends, and malformed results are typed failures or abstentions. Native context ownership and cancellation must not leave in-flight work unaccounted for.

Completed work remains charged even if cancellation arrives afterward. A model from an incomplete query cannot establish a definite violation.

## Interpret evidence

Unsatisfiability can establish a modeled obligation under its recorded assumptions. Satisfiability supplies a candidate. Replay checks entry bindings, paths, operations, postcondition definedness, and violation sites before Refuted is published.

Approximation values, unevaluable API results, or impossible heap aliases can make a candidate unreplayable. These are Unknown outcomes. A source-less metadata effect site cannot produce a definite emitted witness.

The worker assembles outcomes, reasons, assumptions, certainty, witness, and vacuity. Protocol validation checks their compatibility before consumers rely on them.

## Cancel and terminate

The worker accepts cancellation and applies semantic and wall-clock limits. The launcher separately bounds project execution and termination grace. Container CPU and memory limits are the outer resource boundary; SharpProof does not duplicate Docker's resource controller.

Timeout and backend failure are not proofs. They must remain visible in run or claim records and cannot become an empty successful response.

## Cache and publish

Only eligible validated complete responses enter the content-addressed cache. Input identity binds compiler artifact bytes, worker, specifications, protocol, and budgets. A cached response is revalidated against the expected manifest.

Transient failures are excluded. Storage failure or corruption becomes a miss. Publication validates and locks its output set under the [preview filesystem assumptions](preview-support.md).

## Implementation references

- [PassiveCallableVcBuilder.cs](../SharpProof.Worker/Vc/PassiveCallableVcBuilder.cs)
- [PassiveLoopCutter.cs](../SharpProof.Worker/Vc/PassiveLoopCutter.cs)
- [NativeEffectClaims.cs](../SharpProof.Worker/NativeEffectClaims.cs)
- [VerificationCache.cs](../SharpProof.Worker/VerificationCache.cs)
- [ProtocolJson.cs](../SharpProof.Worker.Protocol/ProtocolJson.cs)
