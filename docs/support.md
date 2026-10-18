# Troubleshoot and report a problem

For a suspected vulnerability or an incorrect definite proof/refutation with security implications, use the [private security channel](../SECURITY.md). Public issue forms are for information you have reviewed and intend to publish.

## First checks

Confirm the [host/compiler/filesystem support matrix](preview-support.md), matching package versions, configured feed, selected profile/features, and whether the worker actually ran. Design-time editor feedback does not run native verification. Read the diagnostic's help link or the [Unknown reason](unknown-reasons.md) before changing budgets.

From a trusted source checkout run `pwsh -File scripts/Invoke-SharpProof.ps1 doctor -Target path/to/Consumer.csproj -Format Json`. It evaluates the original mounted project's imports and existing restore assets without running targets, restoring or building. It reports SDK/configuration, direct package references and evidence locations, and explicitly identifies missing assets. It cannot certify a native payload loads or a claim is proven; finish with the [first-proof workflow](getting-started.md#your-first-proof). Project evaluation is for trusted projects, not arbitrary untrusted MSBuild inputs.

If the problem is specific to a repository command, give the exact container entrypoint and source revision. If it is a consumer issue, reproduce it in a small external project with explicit package references and the same compiler SDK. Keep the smallest source that still reproduces the problem.

## Prepare a local support bundle

Collect locally first; nothing should be uploaded automatically:

- Minimal project/source and relevant MSBuild/AnalyzerConfig settings.
- Exact SharpProof package versions, compiler SDK, OS/architecture and filesystem type.
- Command, exit code, expected behavior and observed behavior.
- For verification: the matching request, result and compiler manifest, selected claim identity and relevant reason. Include source revision and run identity so stale evidence can be excluded.
- For performance: comparable baseline and changed timings, inputs, cache state, selected coverage/outcomes and hardware/container limits.

Review every file before sharing. Compiler artifacts can contain source, full paths, symbol names and business logic. MSBuild binlogs can contain evaluated properties, command lines and environment-derived secrets. Replace private path/user/project names consistently, remove credentials/connection strings/tokens and unrelated data, and reproduce again with the redacted minimal project. Do not edit protocol identities inside authoritative evidence and then present it as an authentic original run; regenerate evidence from the sanitized reproduction instead.

Attach only the minimum reviewed files, using a private channel when necessary. Do not paste entire build environments, caches, home directories, proprietary source or raw binlogs into a public issue. If credentials were exposed, revoke/rotate them through the issuing service.

## Choose a report

Use the repository's correctness/Unknown, installation, performance or question form. Include whether the issue reproduces with an unchanged template or sample. Unknown is an accountable result; a useful report explains what construct or budget prevented progress. Do not add `Assume`, suppress diagnostics, or weaken expected outcomes merely to obtain a passing reproduction.
