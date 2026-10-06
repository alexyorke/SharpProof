# 8. Review a trusted boundary

Some calls cross into code SharpProof cannot verify as a source body. A complete effect declaration can document a reviewed boundary. This is an explicit assumption, and the worker records it differently from a checked body proof.

## Declare the external member

```csharp
using System.Runtime.InteropServices;
using SharpProof.Attributes;
public static class NativeBoundary
{
    [SharpProofTrusted("Reviewed libc getpid signature and declared effects.")]
    [EffectContract(
        SharpProofEffect.ReadsAmbientState | SharpProofEffect.UsesNativeCode,
        Capabilities = SharpProofCapability.NativeInterop,
        Complete = true,
        PreconditionFree = true,
        IsDeterministic = false)]
    [DllImport("libc", EntryPoint = "getpid")]
    public static extern int GetProcessId();
}
```

This declares libc getpid without executing it in the tutorial. The reason describes the review, the effect flags permit ambient reads and native code, and the capability permits native interop. The declaration does not assert determinism.

`Complete=true` asserts that the declaration covers the relevant effects. `PreconditionFree=true` explicitly certifies that callers do not need a source-only precondition envelope that disappears from emitted metadata. These are reviewed assertions, not automatically discovered facts.

`SharpProofTrusted` alone supplies no effect summary. An incomplete declaration leaves omissions unknown. Do not copy this shape to an unrelated external function without reviewing its actual effects and calling requirements.

## Choose assumption policy

The tutorial uses `SharpProofAssumptionPolicy=warn`, so declared trusted evidence remains visible without failing solely for that declaration. `error` rejects declared user assumptions/trusted boundaries; it is the strict profile's default.

This policy looks at declarations, including ones not marked Used for a particular proof. Explicit preconditions and resolved API specifications are different recorded evidence and are not all rejected by that declaration policy.

A `TrustedCompleteBoundary` result is a declared external boundary, not `CompleteMayEffectSummary` from verifying an owned source body. Always retain that distinction when reporting assurance.

## Extend the declaration carefully

Effect and capability flags are independent closed sets. Unknown bits are invalid. If the boundary can throw, declare the escaping exception types and appropriate effect; permitting Throws alone does not permit allocation.

Prefer checked source implementations when available. Use audited built-in specifications or opt-in relational packs only for their exact supported member identities and domains. An effect declaration alone does not establish a numerical result relation.

For example, `SharpProofSpecificationPacks=dotnet.scalar` selects the embedded reviewed relational pack containing the Int32 Math.Max result relation. See [the specification catalog](../api-spec-catalog.generated.md) and [coverage guide](../coverage-and-limits.md#api-specifications).

## Finish with a CI policy

Start with portable feedback, add worker verification in the canonical container, then choose require-proven and an explicit assumption policy for the claims you intend to gate. Keep Unknown, vacuity, and reviewed boundaries visible in the result records.

Run the complete tutorial suite after changing specifications. Its saved logs and JSON let you compare actual behavior. For repository changes, use the separate [qualification commands](../container-development.md); tutorial success does not replace PR, coverage, corpus, security, or release checks.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[08-trusted-boundary] build exit: 0
  run: Complete; failure: None
  M:NativeBoundary.GetProcessId~System.Int32 | Effect/EffectContract | Proven | None | TrustedCompleteBoundary | None
```

Verbatim diagnostic message excerpts, with the file/project path and help-link suffix omitted:

```text
warning SP0048: User assumption/trusted evidence declared for M:NativeBoundary.GetProcessId~System.Int32: total=1, user=0, trusted=1; user-ids=[], trusted-ids=[spa1:b4892ec90885e74c619cb9653c237d998d81e0774ec30bc388729a6dac263815].
```

SP0048 keeps the declared trust visible even though the selected effect claim is Proven. The certainty column identifies the source of that result.

## Continue

[Previous: companion contracts](07-companion.md) | [Back to the learning path](README.md).
