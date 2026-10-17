# Security policy

## Report privately

Use [GitHub Security Advisories](https://github.com/alexyorke/SharpProof/security/advisories/new) for suspected vulnerabilities. Do not disclose them in a public issue.

Include the affected version/commit, a minimal reproduction, impact, and any known workaround. Omit secrets and unrelated personal data. Disclosure and remediation timing depend on severity, reproducibility, and release risk; no fixed response deadline is promised.

## Supported versions

Security fixes target the current development line and the most recent published preview, release candidate, or stable release. Older previews are unsupported unless an advisory states otherwise. The checked-in package version does not by itself establish publication status.

## Scope

Reports about analyzer isolation, native payload loading, verifier containment, malformed protocol handling, cache/artifact integrity, and soundness failures producing false Proven or false definite Refuted results are in scope.

Unsupported constructs that correctly produce visible Unknown/incomplete evidence are normally correctness reports. Report a claimed violation with its actual artifact, budgets, structured result, and reproduction when possible.

Full verification supports the canonical Linux amd64 container and six native combinations: Windows, macOS, and glibc Linux on x64/ARM64, within the [host and filesystem boundary](docs/preview-support.md). Exact-artifact native consumer CI checks the single-package setup, proof/refutation evidence, and downstream contract dependencies. Repository build/test/pack workflows remain container-only. The trusted host/workspace assumptions there define the preview threat model; native execution does not inherit Docker resource isolation.

The single standard `SharpProof` reference enables strict verification automatically and restores its exact-version API/analyzer dependencies; Verifier remains a compatibility entrypoint. This does not install a compiler SDK, change the trusted host boundary, or qualify an unsupported OS/architecture. Full verification requires Core MSBuild with SDK 10.0.100+ and the .NET 10 runtime. Native Linux requires the distribution/runtime dependencies and architecture-specific glibc baseline documented in the support matrix. Windows solver payloads link the C++ runtime statically and require no separate Visual C++ redistributable installation. musl Linux and other OS/CPU combinations remain unsupported.

## Repository security checks

`docker compose run --rm tooling security` runs the dependency audit and full Release build with banned-API enforcement. These checks are separate from ordinary PR tests; passing them is not a blanket security guarantee.
