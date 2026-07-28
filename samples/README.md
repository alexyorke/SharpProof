# Package-backed samples

These projects are executable package-consumer specifications. Every
`SharpProof` reference resolves from packed NuGet artifacts; no sample has a
project reference into the repository.

| Project | Demonstrates | Expected result |
| --- | --- | --- |
| `Effects` | Purity, allocation, exception, and capability contracts | Build succeeds |
| `Preconditions` | A direct `Requires` prologue and a known-good call | Build succeeds |
| `ContractFor` | A compiler-symbol-bound interface companion | Build succeeds |
| `TrustedBoundary` | A reviewed external boundary with a complete effect summary | Build succeeds |
| `Library` | Advisory local use and strict Windows x64 CI verification | Its single claim is `Proven` |
| `Outcomes` | One each of `Proven`, `Refuted`, and `Unknown` | The assertion runner validates all three records |
| `Diagnostics` | SP0027, SP0045, and SP0047 without failing the build | Expected warnings are present |
| `MalformedContract` | A late contract clause | Build fails with SP0024 |

Run the complete matrix from the repository root:

```powershell
./scripts/Test-SharpProofSamples.ps1
```

The runner packs the current product when no feed is supplied, restores every
sample from that isolated feed, redirects all `obj`, `bin`, and package-cache
state to a temporary directory, and asserts exit codes, diagnostics, and worker
records. On unsupported hosts it instead asserts the explicit Windows x64
verifier rejection.

To test release-candidate bytes without repacking:

```powershell
./scripts/Test-SharpProofSamples.ps1 -PackageSource ./nupkgs
```

Applications normally reference `SharpProof.Attributes` and keep `SharpProof`
private. A strict Windows x64 CI job also references
`SharpProof.Verifier.Win-x64` privately. The `Library` project shows that
package shape directly.
