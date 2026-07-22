# SMT Lifecycle And Conservative Fallback

SharpProof creates a bounded SMT service for each `SharpProofAnalysisSession`.
The service reuses native solver sessions internally and caches reusable proof
results, but resource accounting is reset for each top-level analysis request.
One method or request therefore cannot consume the SMT budget of a later,
unrelated request.

The public entry point is `SharpProofAnalysisSession.Analyze`. Solver lifecycle
types are implementation details and are not a supported public API.

## Availability And Recovery

The NuGet packages bundle native Z3 for the runtime identifiers documented in
[native SMT packaging and platform support](native-smt-packaging.md). On an
unsupported platform, or when the native library cannot be loaded, SharpProof
returns a conservative `Unknown` result with an `smt_unavailable` reason. It
does not silently treat an unavailable solver as a successful proof.

Transient solver failures are retried once by default after recycling the
current native solver session. An exhausted transient failure returns
`Unknown` with `smt_transient_failure`. Timeouts, incomplete encodings, and
resource limits similarly return structured Unknown reasons. Proven cache
entries remain reusable after a solver session is recycled.

Disposing `SharpProofAnalysisSession` disposes the SMT service and all native
solver sessions owned by it. Callers should therefore use `using` or otherwise
dispose sessions deterministically.

## Public Result Surface

Solver outcomes are exposed through `SharpProofAnalysisResult`:

- `Status` is `Succeeded`, `Unknown`, `Failed`, or `Canceled`.
- `ProofFacts` contains the requested proof status and compact evidence.
- `UnknownReasons` contains stable reason codes such as `smt_unavailable`,
  `smt_timeout`, and `smt_method_budget_exceeded`.
- `Truncations` records bounded-analysis limits that were reached.
- `Error` contains structured input or internal failures.

Internal health counters and solver-context recycling methods are not emitted
by the public API or CLI. Consumers must gate on the requested proof fact, not
only on the overall query status.

## CLI

The CLI uses the same session lifecycle and accepts this shape:

```powershell
dotnet run --project Tools/SharpProof.SymbolicCli -- analyze `
  --file Example.cs `
  --target line:42 `
  --facets proofs,hazards `
  --condition "left < right" `
  --format json `
  --fail-on-unknown `
  --fail-on-disproven
```

Targets are `line:N[:column]`, `position:N`, `span:start:end`, and
`all-lines`. Facets are `effects`, `proofs`, `hazards`, and `complexity`.
The only proof gates are `--fail-on-unknown` and `--fail-on-disproven`.

Exit codes are 0 for an accepted result, 2 for CLI usage errors, 3 for analysis
failures, 4 when the Unknown gate fails, and 5 when a requested proof or effect
verdict is disproven.
