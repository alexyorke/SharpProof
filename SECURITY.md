# Security policy

## Report privately

Use [GitHub Security Advisories](https://github.com/alexyorke/SharpProof/security/advisories/new) for suspected vulnerabilities. Do not disclose them in a public issue.

Include the affected version/commit, a minimal reproduction, impact, and any known workaround. Omit secrets and unrelated personal data. Disclosure and remediation timing depend on severity, reproducibility, and release risk; no fixed response deadline is promised.

## Supported versions

Security fixes target the current development line and the most recent published preview, release candidate, or stable release. Older previews are unsupported unless an advisory states otherwise. The checked-in package version does not by itself establish publication status.

## Scope

Reports about analyzer isolation, native payload loading, verifier containment, malformed protocol handling, cache/artifact integrity, and soundness failures producing false Proven or false definite Refuted results are in scope.

Unsupported constructs that correctly produce visible Unknown/incomplete evidence are normally correctness reports. Report a claimed violation with its actual artifact, budgets, structured result, and reproduction when possible.

Full native verification is qualified only in the [canonical container and filesystem boundary](docs/preview-support.md). The trusted host/workspace assumptions there define the preview threat model.

## Repository security checks

`docker compose run --rm tooling security` runs the dependency audit and full Release build with banned-API enforcement. These checks are separate from ordinary PR tests; passing them is not a blanket security guarantee.
