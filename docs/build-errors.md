# Build errors

SharpProof's MSBuild targets report setup problems with `SPB` codes. They are
separate from the `SP` diagnostics the analyzer and verifier report about your
code; see [diagnostic examples](diagnostic-examples.md) for those.

## Configuration

| Code | Meaning | What to do |
| --- | --- | --- |
| SPB001 | The project sets a property SharpProof does not support, such as `SharpProofMode` | Remove the property. Use `SharpProofProfile` and `SharpProofFeatures`; package paths are not configurable |
| SPB002 | `SharpProofProfile` is not `advisory`, `strict` or `off` | Fix the value shown in the message |
| SPB003 | `SharpProofFeatures` is not `effects`, `contracts` or `all` | Fix the value shown in the message |
| SPB004 | `SharpProofVerify` is not `true` or `false` | Fix the value shown in the message |
| SPB005 | `SharpProofProfile=strict` with `SharpProofVerify=false` | Enable verification, or use the `advisory` profile |
| SPB006 | `SHARPPROOF_CONTRACTS` is defined | Remove it from `DefineConstants`; SharpProof has no runtime checking mode |
| SPB022 | `SharpProofVerifyPolicy` is not `advisory`, `warn-on-unknown` or `require-proven` | Fix the value shown in the message |
| SPB023 | `SharpProofAssumptionPolicy` is not `allow`, `warn` or `error` | Fix the value shown in the message |

See the [configuration reference](configuration.md) for each property.

## Build host

| Code | Meaning | What to do |
| --- | --- | --- |
| SPB010 | The .NET SDK version could not be determined | Build with the .NET SDK, or set `SharpProofProfile=off` |
| SPB011 | The .NET SDK is older than 9.0.300 | Install a newer SDK, or set `SharpProofProfile=off` |
| SPB012 | Verification is enabled but the `SharpProof.Verifier` package is missing | Reference `SharpProof`, which brings the verifier, or disable verification |
| SPB020 | Verification is not supported on this build host. The message lists the detected SDK, MSBuild, and OS and process architectures | Use .NET SDK 10.0.100 or newer on a host in the [support matrix](preview-support.md). An x64 `dotnet` running under emulation on an ARM64 machine is rejected: install the native SDK |
| SPB021 | The build host is musl Linux, such as Alpine | Build in a glibc-based image such as the default `mcr.microsoft.com/dotnet/sdk` tags |

On an unsupported host you can still run the analyzers without proofs:

```xml
<PropertyGroup>
  <SharpProofProfile>advisory</SharpProofProfile>
  <SharpProofVerify>false</SharpProofVerify>
</PropertyGroup>
```

## Verifier package and run

These usually mean a damaged or partly restored package. Delete the package from
the NuGet cache and restore again before investigating further.

| Code | Meaning |
| --- | --- |
| SPB024 | The verifier launcher is missing from the package |
| SPB025 | The verifier worker is missing from the package |
| SPB026 | A verifier path does not point into the restored package |
| SPB027 | The solver library for this OS and architecture is missing from the package |
| SPB028 | The internal run identifier is malformed |
| SPB029 | The internal run directory is outside the expected location |
| SPB030 | The verifier exited with an error and reported no structured diagnostic. The message gives the exit code |
| SPB031 | The compiler did not produce the data the verifier needs for this compilation. Check that analyzers run during the build (`RunAnalyzers` and `RunAnalyzersDuringBuild` are not `false`) |

Verifier exit codes reported by SPB030:

| Exit code | Meaning |
| --- | --- |
| 2 | Invalid arguments or input files |
| 3 | The worker produced no result, a malformed result, or one that could not be published |
| 124 | The run exceeded its time limit |

Any other code is an unexpected verifier failure; please
[report it](support.md) with the build output.
