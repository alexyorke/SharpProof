# Package-backed samples

These eight projects consume packed NuGet artifacts rather than repository project references. The harness checks actual consumer builds, diagnostics, and worker results. For explanations and a progressive learning path with captured output, start with the [tutorials](../../docs/tutorials/README.md).

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

The standard consumer needs one `SharpProof` reference, no activation properties, and no `PrivateAssets="all"`; its exact-version dependencies supply the application Attributes API and full verifier automatically. These samples deliberately select advisory/verification policies where needed to demonstrate portable diagnostics, mixed outcomes, or reviewed trust; their explicit overrides are not required installation steps. The Library sample exercises strict verification. See [getting started](../../docs/getting-started.md) for the standard setup and [Test-SharpProofSamples.ps1](../../eng/scripts/Test-SharpProofSamples.ps1) for the executable assertions.
