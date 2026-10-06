# SharpProof diagnostics

Portable diagnostic descriptors are owned by [GeneratedDiagnosticDescriptors.generated.cs](../SharpProof.Analyzer.Core/GeneratedDiagnosticDescriptors.generated.cs) and [ContractForDiagnosticDescriptors.generated.cs](../SharpProof.Analyzer.Core/ContractForValidation/ContractForDiagnosticDescriptors.generated.cs). The files are hand-maintained.

The portable default is `SharpProofProfile=advisory` with `SharpProofFeatures=all`. Profiles are advisory, strict, or off; features are effects, contracts, or all. Normal Roslyn configuration can change effective severity.

```ini
dotnet_diagnostic.SP0002.severity = suggestion
dotnet_diagnostic.SP0027.severity = warning
dotnet_diagnostic.SP0052.severity = warning
```

These settings change reporting, not contract evidence. Diagnostic silence does not mean Proven.

## Main analyzer summary

| ID | Default severity | Meaning |
| --- | --- | --- |
| SP0002 | Info | Observable purity was not proven |
| SP0013 | Info | Reserved allocation-violation descriptor; not currently emitted |
| SP0015 | Info | Reserved capability-violation descriptor; not currently emitted |
| SP0016 | Info | Capability contract could not be established |
| SP0024 | Error | Invalid contract/control argument or clause usage |
| SP0025 | Error | Invalid analyzer configuration |
| SP0027 | Warning | Compiler-bound precondition concretely replayed false |
| SP0030 | Info | Reserved exception-violation descriptor; not currently emitted |
| SP0045 | Info | Zero-allocation contract could not be established |
| SP0046 | Info | Exception contract could not be established |
| SP0047 | Info | Selected analysis is incomplete |
| SP0049 | Error | Final compiler manifest emission failed |
| SP0050 | Error | Referenced contract API payload could not be verified |
| SP0052 | Warning | Complete body summary exceeds declared effect contract |

## SP0002

An `[EnforcePure]` method with observable state mutation or incomplete effects cannot obtain an analyzer purity success. Inspect the actual write/ambient-read/call boundary. Purity reporting is separate from a native worker claim verdict.

## SP0013

Reserved for a known allocation in a zero-allocation method. Current portable reporting uses SP0045 when allocation freedom is not established. Do not assume the reserved descriptor identifies every allocation site.

## SP0015

Reserved for disallowed capability use. Current portable incomplete capability reporting uses SP0016.

## SP0016

A method declares `[AllowedCapabilities]`, but analysis cannot establish that all effects stay within the declared capability set. Unknown external calls can cause this result. A trusted annotation without an accepted complete summary is insufficient.

## SP0024

Examples include unknown flag bits, invalid exception types, blank trust/suppression reasons, invalid closed attribute targets, misplaced contract clauses, and malformed intrinsic use. Fix the declaration or clause position; invalid data must not create proof facts.

## SP0025

Invalid profile/feature options, conflicting supported configuration, removed aliases, and the reserved `SHARPPROOF_CONTRACTS` symbol are configuration errors. Use the supported MSBuild properties and keep compiler-visible configuration consistent.

## SP0027

A modeled call violates a bound `Requires` predicate after concrete replay. For example, a literal negative argument can violate a positive-entry requirement. Merely lacking knowledge of an argument, or a predicate evaluation that may throw, does not justify this diagnostic.

The message identifies the callee and precondition. Constructor and reduced extension-method binding must preserve actual argument/receiver ordinals.

## SP0030

Reserved for an exception-violation descriptor. The portable analyzer currently reports unestablished exception contracts through SP0046. Native worker exception claims have separate structured results.

## SP0045

A `[ZeroAllocations]` contract could not be established. Inspect managed creation, boxing, exception creation, string operations, and opaque calls within the admitted model. Unknown is not a confirmed allocation witness.

## SP0046

A `[DoesNotThrow]` or `[AllowedExceptions]` contract could not be established. Arithmetic, null receivers, array access, calls, and explicit throws can contribute exception behavior. Catch/filter order and runtime exception identity matter.

## SP0047

A selected callable is outside the portable analyzer's admitted subset or analysis is incomplete. Unannotated unsupported methods can remain quiet; silence must not be counted as proof. Worker callable coverage is a separate accountability record.

## SP0049

The collector failed to emit the final compiler manifest needed by verification. Treat this as an infrastructure failure. A missing artifact cannot be replaced by a successful ordinary build or guessed source reparse.

## SP0050

The referenced Attributes assembly could not be read/attested. Check exact package identity and payload compatibility. Source shadows and same-named types are not substitutes for the supported contract API.

## SP0052

The complete modeled body summary exceeds an `[EffectContract]` declaration. Effect flags are independent: allowing exceptions does not allow allocation. Partial declarations and trusted external boundaries must be interpreted according to their accepted completeness evidence.

## ContractFor diagnostics

All companion-validation descriptors default to Error. They validate compiler symbol relationships after generator output is available.

| ID | Condition |
| --- | --- |
| SPCF0001 | Invalid target or contract attribute identity |
| SPCF0002 | Duplicate companion |
| SPCF0003 | Invalid companion type |
| SPCF0004 | Missing target member |
| SPCF0005 | Member signature mismatch |
| SPCF0006 | Ambiguous member match |
| SPCF0007 | Required companion member body missing |
| SPCF0008 | Invalid clause placement |
| SPCF0009 | Companion targets itself |
| SPCF0010 | Cyclic companion relationship |

### SPCF0001

The attribute must identify one resolvable named target type using the supported contract API identity. Correct the `typeof` target or the referenced Attributes package; a same-named lookalike is not contract evidence.

### SPCF0002

Exactly one companion may map to the target. Remove or reconcile the duplicate declarations, including declarations contributed by generators.

### SPCF0003

The companion must be a static class with the target's generic arity and matching constraints. An instance class or mismatched generic companion is invalid.

### SPCF0004

The reported target method needs an exact ordinary companion member. Add that member with the matching name and signature.

### SPCF0005

The signature must match the target overload, including generic constraints, ref kinds, nullability, and return type. An instance target requires an explicit first receiver parameter of the target type; a static target does not. The receiver must not be ref, scoped, optional, or params.

### SPCF0006

The mapping has more than one possible member match. Make the companion signatures unambiguous rather than relying on textual overload-name matching.

### SPCF0007

The companion member needs a compiler-bound source body. A bodyless declaration cannot supply its prologue clauses.

### SPCF0008

Place companion clauses in the member's contiguous direct prologue and use expression intrinsics only in their allowed clause contexts. Moving a clause after executable code does not establish a contract.

### SPCF0009

A companion cannot target itself. Point it at the distinct interface or class whose contract it describes.

### SPCF0010

Companion relationships must be acyclic. Remove the reported cycle; otherwise the relationship cannot provide an effective contract source.

## Worker reporting

The launcher reports each claim's typed outcome, reason, and vacuity. The verifier policy decides whether incomplete selected analysis is allowed, warned, or rejected; assumption policy separately governs declared user assumptions and trusted boundaries. Infrastructure failures remain failures.

| Worker diagnostic | Meaning |
| --- | --- |
| SP0047 | Incomplete selected callable coverage, with policy-dependent level |
| SP0048 | Declared user assumption or trusted boundary, with assumption-policy-dependent level |
| SP0051 | Replayed refuted contract; reported as an error |

These codes are owned by [VerifierDiagnosticCodes.cs](../SharpProof.Host/VerifierDiagnosticCodes.cs). A Refuted claim remains an error even under advisory verifier policy.

Do not infer native effect refutations from a reserved portable diagnostic ID. Inspect structured worker results and optional SARIF, and use [Unknown reasons](unknown-reasons.md) to understand abstentions.

The [Diagnostics and Outcomes samples](../samples/README.md) exercise actual reporting and worker records. [Golden tests](../tests/golden/README.md) freeze stable analyzer output.
