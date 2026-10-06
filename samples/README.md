# Package-backed samples

For explanations and a progressive learning path with captured output, start with the [tutorials](../docs/tutorials/README.md). packed NuGet artifacts rather than repository project references. The harness checks actual consumer builds, diagnostics, and worker results.

| Project | Demonstrates | Harness expectation |
| --- | --- | --- |
| `Effects` | Purity, allocation, exception, capability contracts | Build succeeds |
| `Preconditions` | Method/constructor preconditions and closed attributes | Build succeeds |
| `ContractFor` | Symbol-bound companion and consumer call | Build succeeds |
| `TrustedBoundary` | Reviewed complete external effect declaration | Build succeeds |
| `Library` | Branches, locals, multiple returns, entry values | Strict verification proves every selected claim |
| `Outcomes` | Proven, Refuted, Unknown | Result records contain each expected outcome |
| `Diagnostics` | SP0027, SP0045, SP0047 | Expected diagnostics appear without failing the build |
| `MalformedContract` | Late contract clause | Build fails with SP0024 |

From the repository root:

```text
docker compose run --rm tooling samples -Configuration Release
```

Without a supplied feed, the harness packs the current product into an isolated local feed. It redirects build and package-cache state into temporary roots and checks outputs. Portable-only host jobs do not execute the native verifier sample matrix.

To test already packed artifacts:

```text
docker compose run --rm tooling samples -Configuration Release -PackageSource artifacts/container-packages
```

Applications reference Attributes as a compile dependency and keep SharpProof private. Full strict container verification also references Verifier privately. See [getting started](../docs/getting-started.md) for package shape and [Test-SharpProofSamples.ps1](../scripts/Test-SharpProofSamples.ps1) for the executable assertions.
