# Upgrade and compatibility policy

SharpProof is a preview. A checked-in version is not evidence that packages are published. Update the single standard `SharpProof` reference from your chosen feed and read release notes before changing a consumer. Its exact-version API/analyzer dependencies restore automatically. The version authority is [SharpProof.Release.props](../SharpProof.Release.props); release notes live in [CHANGELOG.md](../CHANGELOG.md).

## Compatibility boundaries

| Surface | What to review on upgrade |
| --- | --- |
| Attributes API | Source/binary compatibility and XML documentation; [public API](public-api.md) |
| Analyzer | Compiler-host minimum, diagnostic IDs, severities, messages, configuration and safe code fixes |
| Verifier | Worker runtime, native payload/architecture, host/filesystem support and resource budgets |
| Semantics | Selection, supported constructs, Unknown classifications, trusted assumptions, vacuity and replay |
| Evidence | Versioned request/result/manifest protocols; consumers must validate versions and matching identities |
| Build integration | MSBuild defaults, report paths, caching, design-time behavior and transitive assets |

Preview releases can change these boundaries. Do not treat semantic-version appearance alone as a stability guarantee. Stable-release compatibility commitments must be published before relying on them. Do not mix package versions or manually copy a worker, solver, analyzer, or task from another release.

## Consumer checklist

The current development toolchain uses .NET 10. Full verification requires SDK 10.0.100+ and the .NET 10 runtime; portable analyzer consumers retain SDK 9.0.300+/Roslyn 4.14 support. Update the build host/runtime before enabling this verifier version, while retaining a compatible application target framework where appropriate.

Build-host choice is separate from the application target. Full verification uses Core MSBuild, or Windows x64 full-framework MSBuild 18+ with the out-of-process .NET task-host bridge and a compatible .NET 10 SDK/runtime. Review a successful completed exact-artifact qualification run for the source SHA and host version; job definitions alone do not establish a passed result. The ARM64 full-framework bridge is implemented but is not separately qualified by the x64 Visual Studio jobs, and native ARM64 Core MSBuild coverage does not qualify that full-host route. Do not infer interactive IDE qualification from MSBuild.exe tests. MSBuild 17.14 with SDK 9.0.300 is an explicit advisory-only path, not a default verifier host. See [legacy compatibility](legacy-compatibility.md) for the target matrix, classic-project exclusions, and old-OS deployment limits.

The main `SharpProof` package now enables strict full verification without activation properties; `SharpProof.Verifier` remains a compatibility entrypoint with the same defaults. Consumers that previously installed the main package without setting a profile must expect selected Unknown/incomplete claims and declared user/trusted assumptions to fail the default gate. SDK 9 hosts require an explicit portable-only advisory configuration; the default SDK 10 verifier cannot run there. Existing explicit policy overrides remain deliberate configuration and should be reviewed during migration.

For the standard setup, replace the old separate Attributes/analyzer/Verifier references with one `SharpProof` reference, remove redundant SharpProof activation properties, and remove `PrivateAssets="all"` from that standard reference so transitive application contracts propagate. Its exact-version graph is `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`. Inspect the resulting package dependency graph and packed-library consumer behavior; do not remove application runtime guards.

1. Save the current package versions, SDK version, source revision and representative results. Read the new release's migration notes and [support matrix](preview-support.md). Keep the application target framework separate from the compiler/worker runtime decision.
2. Update the standard SharpProof reference in a branch, restore normally, and review dependency/lock-file changes. If maintaining a portable-only setup, retain explicit advisory/verification-disabled settings and keep the transitive Attributes application dependency visible. Leave runtime guards intact.
3. Build representative portable and full-verification consumers. Compare diagnostic changes, selected callable/claim counts, Unknown reasons, assumptions, vacuity, and replayable violations. Investigate a change in outcomes rather than accepting a greener build as proof of improvement.
4. Regenerate current reports with the new package graph; never join new results to an old manifest. Let cache identity validation decide reuse and use a clean reproduction to investigate cache-specific failures.
5. Exercise a deliberately failing claim on a temporary change to ensure the CI gate still rejects it. Restore the correct input and run the selected qualification again.
6. Roll out gradually. To roll back, restore the complete previous package graph, SDK/configuration and reviewed source state, then rebuild. Preserve both runs for comparison without publishing private evidence.

## Maintainer release review

Attributes now carries a full strong-name signature for signed Framework consumers. Its deliberately public open-source identity key is stable and is not an authentication secret. Introducing that identity into the preview requires existing consumers to rebuild; changing the key again would break compiled references. Keep exact API/payload admission intact, and use the [signing policy](../eng/signing/README.md) when reviewing the identity.

Maintain public API declarations and package validation. Set `SharpProofPackageValidationBaselineVersion` to a reviewed, actually published compatible version to opt into a previous-package baseline; [package metadata](../SharpProof.PackageMetadata.props) forwards it to the SDK validation setting. The initial preview supplies no fabricated prior baseline. Do not disable validation for convenience. Document intentional breaks in release notes, including non-API changes that affect proof evidence.

Tooling now embeds portable PDBs and tracked/untracked source documents. Matching symbol packages and coverage authority use the PDB extracted from the same compiled assembly rather than a second build. Attributes retains portable symbols; main is an assembly-free facade. See [NuGet debugging](nuget-debugging.md) for the actual debugger and artifact checks.

The optional [SharpProof.Tool](../SharpProof.Tool/README.md) has its own versioned manifest installation. It is not required by the normal application package. Its `explain` command reports stored evidence and does not establish current-source freshness or perform a new proof; upgrade and restore it separately if your team uses it.

The [release process](../eng/release/README.md) owns exact-artifact qualification and publication. A development branch passing tests is not release qualification.
