# Project templates

`sp-verified` is the standard setup: a class library with one `SharpProof.Verifier` reference, no SharpProof properties, and a simple normal-return proof. The package enables strict verification automatically and supplies its API/analyzer dependencies transitively. The reference permits application contract dependencies to propagate. `sp-portable` is an explicit advanced alternative for compiler diagnostics without the native worker. Both target net8.0; this does not change the compiler-host or worker runtime requirements. Use a [qualified host](../docs/preview-support.md).

The installer reads [eng/Versions.props](../eng/Versions.props), stages templates in a unique temporary directory, and adds the exact package version as their default. Source templates deliberately have no independent release-version default. Neither installation nor generation obtains packages or promises public feed availability.

Inside a persistent canonical development container, run:

```text
pwsh -NoLogo -NoProfile -File eng/scripts/Install-SharpProofTemplates.ps1
```

The script prints a private template-hive path and copyable `dotnet new` commands. Run one of those commands with the printed path, outside the SharpProof repository so its build properties do not leak into the consumer. A custom hive keeps normal user templates untouched. To use a different matching package graph, pass `--packageVersion <version>` when generating a project.

Configure your consumer's local/private NuGet feed, restore, and build in Release. In the portable project change `Positive(1)` to `Positive(0)` to see SP0027. In the verified project inspect the published worker evidence and expected Proven claim; see [first proof](../docs/getting-started.md#your-first-proof). Do not define `SHARPPROOF_CONTRACTS` or replace runtime guards with static contracts.

## Maintainer validation

After producing exact packages, run in the canonical container with a feed path visible inside that container:

```text
pwsh -NoLogo -NoProfile -File eng/scripts/Install-SharpProofTemplates.ps1 -Verify -PackageSource artifacts/container-packages
```

The check validates package/symbol identities against the checkout version, installs into an isolated hive, generates both consumers outside repository build inheritance, restores the matching packages into a private cache, builds portable and real strict verification consumers, and requires the portable bad-call negative control to fail with SP0027. Source mapping restricts SharpProof packages to the exact supplied feed; nuget.org provides external dependencies. The verifier must return exactly one nonvacuous Proven claim with complete coverage and no run failure. Scratch is local to the container; no logs or source are uploaded. Strict build failure propagates, including Unknown/incomplete selected proof obligations.

Temporary templates/hive and consumer files remain under the printed unique `sharpproof-templates-<id>` directory for inspection. They disappear when a disposable container ends. In a persistent container, remove that exact printed temporary directory when finished, after checking it is the generated scratch directory; normal user template registration is unaffected.

For existing projects, merge only the needed references/properties rather than replacing the project file. Follow [incremental adoption](../docs/adoption.md). The [tutorial series](../docs/tutorials/README.md) includes external contract companions and trusted-boundary examples for advanced scenarios.
