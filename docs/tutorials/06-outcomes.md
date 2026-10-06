# 6. Read failures and Unknown

A useful verifier makes failures accountable. This consumer intentionally contains one claim of each outcome. The consumer build must fail because a contract is Refuted, even though verifier policy is advisory.

## Compare the claims

```csharp
using SharpProof.Attributes;
public static class Outcomes
{
    public static long Proven(long value)
    {
        Contract.Ensures(Contract.Result<long>() == value);
        return value;
    }
    public static long Refuted(long value)
    {
        Contract.Ensures(Contract.Result<long>() > value);
        return value;
    }
    public static volatile int Shared;
    [DoesNotThrow]
    public static int Unknown() => Shared;
}
```

`Proven` returns its input and promises exactly that.

`Refuted` also returns its input but promises a strictly greater value. A replayable violating input disproves that relationship. The launcher reports SP0051 as an error; advisory policy does not silence a real refutation.

`Unknown` reads a volatile field under `[DoesNotThrow]`. Volatile reads currently sit outside the supported body model, so the worker abstains with a typed reason. Unknown describes a limit of this analysis; it does not establish that this method throws. The preceding lesson shows supported ordinary field operations and loops.

## Inspect structured evidence

Open `06-outcomes-result.json` in the run's evidence directory. Join `claimResults[].claimId` to `manifest.claims[].claimId` to find the claim's callable, kind, and source location.

Read these fields together:

| Field | Why it matters |
| --- | --- |
| `runStatus`, `failureReason` | Did verification itself complete? |
| `callableResults[].coverage` and `reason` | Was each selected callable fully covered? |
| `claimResults[].outcome` and `reason` | What was established or why did checking abstain? |
| `assumptions` and `Used` evidence | What conditions/evidence does the result depend on? |
| `vacuity` | Was entry contradictory or normal return absent? |
| `model` or `effectWitness` | What concrete violating evidence is available? |

The JSON uses its serialized field casing, including lowercase `used`; the description above names the evidence concept. Read actual records rather than parsing console text.

A Complete run can contain Unknown. The run status accounts for executing the bounded analysis, while outcomes account for individual obligations.

## Decide what to do

Fix the false postcondition or the body that violates it. For Unknown, first read the typed reason. Simplify unsupported code, strengthen the specification with justified input conditions, or choose a policy that accepts visible incomplete coverage.

Do not treat a larger timeout as a semantic repair. Do not add an unjustified Assume to force a result. The worker requires replay before a candidate becomes Refuted.

The top-level tutorial runner succeeds only after checking that this consumer failed as expected and published the three expected outcomes. This is a tested failure demonstration, not a green build for the incorrect consumer.

## Captured output

Verbatim excerpt from the successful series run. These are the runner's projected claim records; full build logs and JSON are saved in the evidence directory.

```text
[06-outcomes] build exit: 1
  run: Complete; failure: None
  M:Outcomes.Proven(System.Int64)~System.Int64 | Postcondition/Unspecified | Proven | None | Unspecified | None
  M:Outcomes.Refuted(System.Int64)~System.Int64 | Postcondition/Unspecified | Refuted | None | Unspecified | None
  M:Outcomes.Unknown~System.Int32 | Effect/DoesNotThrow | Unknown | UnsupportedBody | Unavailable | None
```

Verbatim diagnostic message excerpts, with the file/project path and help-link suffix omitted:

```text
error SP0051: Refuted Postcondition claim spc1:4c76d18973b56f1f4922190406d233fa7007743d4180980fbade8c57972ce193 for M:Outcomes.Refuted(System.Int64)~System.Int64. Counterexample: parameter:0 = 0.
```

The replayed input is 0: the method returns 0 and its false contract demands a value greater than 0. The volatile-read claim has `reason=UnsupportedBody`; its callable separately has `coverage=Incomplete` and `reason=SemanticUnknown`. Both records are useful.

## Continue

[Previous: loops and state](05-loops-and-state.md) | [Next: companion contracts](07-companion.md). Reference: [Unknown reason catalog](../unknown-reasons.md).
