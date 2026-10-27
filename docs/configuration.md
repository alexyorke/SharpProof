# Consumer configuration

Maintainers: see [configuration ownership](configuration-ownership.md) for runtime and automation setting authorities.

The standard SDK-style consumer needs one `SharpProof` reference and no SharpProof properties. It brings the application contract API and analyzer transitively and enables strict verification automatically. Keep that reference without `PrivateAssets="all"` so contract application dependencies propagate. The internal package graph remains exact-version matched. The settings below are optional overrides, set in the project or `Directory.Build.props`; [getting started](getting-started.md) shows the minimal setup.

The implementation authority is [the consumer contract](../src/SharpProof.Package/buildTransitive/SharpProof.ConsumerContract.props), [portable targets](../src/SharpProof.Package/buildTransitive/SharpProof.targets), [verifier targets](../src/SharpProof.Verifier/buildTransitive/SharpProof.Verifier.targets), and [verifier defaults](../src/SharpProof.Verifier/buildTransitive/SharpProof.Verifier.defaults.props). Properties beginning with `_SharpProof` are implementation details.

## Activation and claim selection

| Property | Default | Accepted values and effect |
| --- | --- | --- |
| `SharpProofProfile` | `strict` | `advisory`, `strict`, `off`; advisory explicitly opts into portable defaults; off disables analysis and verification |
| `SharpProofFeatures` | `all` | `effects`, `contracts`, `all`; selects the analyzer features and compiler-artifact feature set |
| `SharpProofVerify` | `true` for strict; `false` otherwise | `true`, `false`; enables full verification when the profile is active |
| `SharpProofVerifyPolicy` | `require-proven` for strict; `advisory` otherwise | `advisory`, `warn-on-unknown`, `require-proven`; controls incomplete selected coverage |
| `SharpProofAssumptionPolicy` | `error` for strict; `allow` otherwise | `allow`, `warn`, `error`; governs declared user assumptions and trusted boundaries |
| `SharpProofSpecificationPacks` | Unset | Semicolon-delimited embedded pack IDs; currently `dotnet.scalar` |

`strict` requires `SharpProofVerify=true`; explicitly setting it false is a configuration error. Strict defaults can be overridden for verifier and assumption policies. `off` omits the portable analyzer and disables verification. An enabled worker requires the matching Verifier package, SDK 10.0.100+, Core MSBuild or the supported Windows MSBuild 18+ task-host path, and a host/filesystem in the [support matrix](preview-support.md): the canonical Linux amd64 container or native Windows, macOS, or glibc Linux on x64/ARM64. Design-time builds do not run the worker.

Feature selection does not mean that every method receives every possible proof. Effect annotations select effect obligations; postconditions and return contracts select normal-return obligations. A precondition describes an entry/call obligation and does not by itself assert a return relationship. Selected unsupported callables remain accountable through incomplete coverage and typed Unknown results. See [coverage and limits](coverage-and-limits.md) and [public API](public-api.md).

`advisory` reports incomplete selected coverage as information; `warn-on-unknown` reports warnings; `require-proven` rejects it. A replayed Refuted claim is an error under every policy. Infrastructure failures remain failures. Assumption policy checks declarations even when a particular claim does not mark them used. Preconditions and resolved API specifications remain recorded evidence and are outside that user-declaration rejection rule.

Specification packs add reviewed relational semantics for exact supported members. `dotnet.scalar` currently supplies the Int32 `Math.Max` relation. Unknown IDs, duplicate IDs, and empty entries within a nonempty list are rejected. Selecting a pack changes semantic evidence and cache identity; it does not increase a timeout or grant blanket framework support. See the [API catalog](api-spec-catalog.md).

## A CI consumer example

The single standard SharpProof reference already enables strict CI policy. To customize report locations, optionally apply these properties and build on a supported host:

```xml
<PropertyGroup>
  <SharpProofVerifyResultFile>$(MSBuildProjectDirectory)/artifacts/sharpproof/result.json</SharpProofVerifyResultFile>
  <SharpProofVerifySarifFile>$(MSBuildProjectDirectory)/artifacts/sharpproof/result.sarif</SharpProofVerifySarifFile>
</PropertyGroup>
```

Archive the structured result alongside the build. Inspect claim outcomes, callable coverage, assumptions, and vacuity; the absence of diagnostics alone is insufficient evidence. For portable-only feedback, use `advisory` with verification false.

## Budgets

| Property | Default | Accepted range or relationship |
| --- | ---: | --- |
| `SharpProofVerifyQueryRlimit` | 3000000 | Positive unsigned resource units, no larger than method rlimit |
| `SharpProofVerifyMethodRlimit` | 20000000 | Positive unsigned accumulated method resource units |
| `SharpProofVerifyMethodWallTimeMilliseconds` | 10000 | Positive milliseconds, no larger than project time |
| `SharpProofVerifyProjectWallTimeMilliseconds` | 300000 | Positive milliseconds |
| `SharpProofVerifyMaxParallelism` | 4 | 1 through 4 concurrent method lanes |
| `SharpProofVerifyMaximumExpressionDepth` | 64 | 1 through 256 |
| `SharpProofVerifyTerminationGraceMilliseconds` | 1000 | 1 through 300000 milliseconds |

Rlimits are Z3 resource units. Wall-clock limits and Docker CPU/memory isolation are separate controls. Raising a budget cannot supply missing language semantics. Fixed frontend/VC construction bounds also remain in force. [Analysis limits](analysis-limits.md) explains the distinction and protocol input bounds.

## Cache and published evidence

Let the intermediate verification directory mean `$(IntermediateOutputPath)SharpProof` beneath the project directory, normally `obj/<Configuration>/<TargetFramework>/SharpProof`.

| Property | Default | Purpose |
| --- | --- | --- |
| `SharpProofVerifyCacheEnabled` | `true` | `true` or `false`; enables response caching |
| `SharpProofVerifyCacheMaximumBytes` | 536870912 | Positive byte count up to 536870912 (512 MiB) |
| `SharpProofVerifyCacheDirectory` | `cache` beneath the intermediate verification directory | Dedicated local cache storage |
| `SharpProofVerifyRequestFile` | `request.json` beneath that directory | Published worker request |
| `SharpProofVerifyResultFile` | `result.json` beneath that directory | Published structured worker response |
| `SharpProofCompilerManifestFile` | `compiler-manifest.json` beneath that directory | Published final-compilation artifact |
| `SharpProofVerifySarifFile` | Unset | Opt into a SARIF projection at this path |

For multitarget projects using `TargetFrameworks`, configured request, result, manifest, and SARIF paths gain a target-framework directory before the filename. For example, `artifacts/sharpproof/result.json` becomes `artifacts/sharpproof/net8.0/result.json` for a net8.0 inner build. Default intermediate paths already include the framework.

Each active invocation uses a private `runs/<invocation-id>` directory before publication. Local publication sets must be disjoint or exactly equal; partial overlap is rejected. Use dedicated local paths and read the [preview filesystem boundary](preview-support.md). Concurrent sharing of arbitrary build outputs remains unsupported.

Cache hits require validated eligible complete responses. Stable semantic Unknown may be cached; timeout, cancellation, backend/infrastructure failure, malformed evidence, and incomplete runs do not become complete cached evidence. Corrupt or unavailable cache storage becomes a miss. Changes to semantic artifact inputs participate in cache identity. See [SMT lifecycle](smt-lifecycle.md).

## AnalyzerConfig and IDE behavior

The analyzer recognizes compilation-global `sharpproof_profile` and `sharpproof_features`, their `build_property.sharpproof_profile` / `build_property.sharpproof_features` aliases, and the package-supplied `build_property.SharpProofProfile` / `build_property.SharpProofFeatures`. Values are trimmed and compared without case sensitivity. Prefer the MSBuild properties so package activation, collector selection, and IDE analysis share one configuration.

A global AnalyzerConfig can select features:

```ini
is_global = true
sharpproof_features = contracts
```

An explicit analyzer feature value can replace the package's defaulted `all`. Conflicting explicit aliases are invalid. An analyzer profile must agree with the effective MSBuild profile, including its default, because MSBuild controls package activation and verification. A per-file option that changes compilation-global selection is invalid; a redundant matching value is accepted. Invalid global analyzer configuration reports SP0025 and disables semantic analysis. These rules are exercised by [configuration regression tests](../src/SharpProof.Analyzer.Tests/ConfigurationDiagnosticsRegressionTests.cs), [package-default tests](../src/SharpProof.Analyzer.Tests/AnalyzerConfigurationPackageDefaultTests.cs), and [unit tests](../src/SharpProof.Analyzer.Tests/AnalyzerConfigurationUnitTests.cs).

Normal Roslyn severity configuration belongs in `.editorconfig`, for example:

```ini
[*.cs]
dotnet_diagnostic.SP0027.severity = warning
dotnet_diagnostic.SP0047.severity = warning
```

Severity changes affect diagnostic reporting, while worker verification and assumption policies decide their respective build gates. `SharpProofSuppress` also changes reporting only. Portable generated-code filtering uses compiler generated-code classification, known generated filenames/headers, and supported generated-code attributes; portable silence does not establish worker coverage. Companion validation runs at compilation end after generators have added syntax. See [diagnostics](diagnostic-examples.md).

Explicit advisory-only package analysis requires .NET SDK 9.0.300 or newer (Roslyn 4.14 or newer), independently of the application's target framework. The standard strict configuration requires SDK 10.0.100+ and the .NET 10 runtime. Full verification is unavailable in design-time builds and unsupported hosts; an IDE's portable diagnostics are not a native proof report.

## Reserved and removed settings

`SHARPPROOF_CONTRACTS` is reserved and unsupported in every profile, including `off`. It causes compiler-elided declarations to emit calls whose bodies do not enforce conditions; direct `Result`/`Old` execution throws. Remove it from project constants and source-local definitions.

`SharpProofMode` / `sharpproof_mode` were removed. The package also rejects the former `SharpProofAnalyzerDirectory`, `SharpProofCollectorDirectory`, `SharpProofCompilerCollectorPath`, `SharpProofToolsDirectory`, and `SharpProofPortableAnalyzerPath` overrides. Runtime tools are package-owned: `SharpProofWorkerPath` and `SharpProofLauncherPath` are validated against the exact package runtime closure and cannot select an arbitrary replacement worker. Internal `_SharpProof*` and `_SharpProofTest*` hooks are outside the consumer configuration surface.
