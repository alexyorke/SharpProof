# CI Exit-Code Gates

Symbolic queries normally exit with code 0 when the query ran successfully,
even when the result is conservative or reports a finding. CI gates turn
selected typed outcomes into a failing process status:

- `0`: the query ran and every configured gate passed
- `1`: the query ran and at least one configured gate failed
- `64`: the request or option combination was invalid

Gate failures are written to stderr as `CI gate failed [code]: ...`. JSON
results remain on stdout and stay parseable even when the process exits with
code 1. Multiple failing gates are all reported. Gates apply to the final typed
result after query filters, but aggregate thresholds use untruncated totals.

CI gates require a focused query mode and cannot be combined with `explain`.
They are successful query outcomes, not request/process errors; those use the
[typed query error model](error-model.md).

## Domain Gates

| Gate | Query mode | Failure condition |
| --- | --- | --- |
| `--fail-on-hazard` | `--runtime-hazards` | The final filtered result contains at least one hazard. |
| `--fail-on-unproven-implies` | invariant query with `--implies` | No proof result was produced, or any requested proof outcome is not `ProvenTrue`. |
| `--fail-on-capability-violation` | `--capabilities` | An observed capability is outside the repeated `--allowed-capability` allowlist. An empty allowlist means no capability is allowed. |
| `--fail-on-capability-unknown` | `--capabilities` | The result contains an unknown reason or unknown site. |
| `--fail-on-complexity-exceeded <bound>` | `--complexity` | The inferred complexity is provably above the bound. |
| `--fail-on-complexity-unknown` | `--complexity` | Complexity is unknown, recursive-unknown, has unknown reasons, or is incomparable with the configured bound. |
| `--max-conservative-unknowns <n>` | invariant query | The merged invariant's distinct conservative unknown-fact count exceeds `n`. |

Capability names are the `SharpProofCapability` values, such as `Console`,
`FileRead`, `Network`, and `Clock`. File, network, console, and registry
capabilities imply `IO`; the allowlist is normalized the same way, so allowing
`Console` also allows its implied `IO` bit.

Complexity bounds are `Constant`, `Logarithmic`, `Linear`, `Linearithmic`,
`Quadratic`, `Product`, and `Max`. Constant through Quadratic form an ordered
chain. Product and Max are comparable only to themselves and Constant. An
incomparable pairing is unknown, not exceeded, so combine both complexity
gates when CI must reject either outcome:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Worker.cs --line 40 --complexity `
  --fail-on-complexity-exceeded Linear `
  --fail-on-complexity-unknown `
  --compact-json
```

## Compact Output Gates

`--fail-on-compact-truncation` fails when `--max-lines`, `--max-points`,
`--max-hazards`, `--max-facts`, `--max-conditions`, or `--max-proofs` truncates
the selected `--compact-json` projection.

Repeated `--fail-on-compact-threshold <metric=max>` gates raw aggregate counts.
The process fails when `actual > max`; output truncation never lowers the
value used by the gate.

| Query mode | Supported metrics |
| --- | --- |
| Invariants | `program-points`, `conservative-unknowns`, `proof-unknowns`, `reachability-unknowns` |
| Runtime hazards | `hazards` |
| Capabilities | `capability-sites`, `capability-unknowns` |
| Complexity | `complexity-drivers`, `complexity-unknowns` |

For example, this keeps the JSON payload bounded while independently requiring
the full result to contain no conservative unknowns and no more than 100
program points:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Worker.cs --all-lines --compact-json `
  --max-points 20 `
  --fail-on-compact-threshold conservative-unknowns=0 `
  --fail-on-compact-threshold program-points=100
```

Add `--fail-on-compact-truncation` when exceeding the output limit itself must
also fail.

## JSON Requests

The schema-version 1 request envelope exposes the same settings under
`gates`:

```json
{
  "schemaVersion": 1,
  "mode": "capabilities",
  "source": {
    "text": "using System; class C { void M() => Console.WriteLine(); }",
    "filePath": "virtual/Capabilities.cs"
  },
  "target": { "kind": "line", "line": 1 },
  "output": { "format": "compactJson" },
  "gates": {
    "allowedCapabilities": ["Console"],
    "failOnCapabilityViolation": true,
    "failOnCapabilityUnknown": true,
    "failOnCompactTruncation": false,
    "compactThresholds": {
      "capability-sites": 10,
      "capability-unknowns": 0
    }
  }
}
```

Compact-threshold dictionary keys accept the same kebab-case metric names as
the CLI. JSON property names remain lower camel case. Other gate properties are
`failOnHazard`, `failOnUnprovenImplies`, `maximumComplexity`,
`failOnComplexityUnknown`, and `maxConservativeUnknowns`.
