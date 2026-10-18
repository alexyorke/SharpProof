# Arcade adoption audit

The repository follows the [Arcade SDK requirements at the pinned upstream commit](https://github.com/dotnet/arcade/blob/ca4c91e56da9ae88fc62578058d8aee531679dba/Documentation/ArcadeSdk.md).
`global.json` pins .NET SDK 10.0.402 and Microsoft.DotNet.Arcade.Sdk
9.0.0-beta.26507.8. The SDK pin and all existing product identities are retained.

| Requirement | Implementation |
| --- | --- |
| Output layout | Every owned project evaluates binary, intermediate and package outputs under `artifacts`. Private consumer fixtures retain their isolated overrides. |
| Shared scripts | All 151 files under `eng/common` match the pinned upstream tree. `eng/arcade-source.json` records Git blobs, SHA256 hashes and executable modes. These files are immutable. |
| Engineering | `eng/Build.props` selects the source solution. Per-project restore supports `.slnx` with Arcade 9. Repository extensions live outside `eng/common`. |
| Versions and feeds | Product and package dependency versions live in `eng/Versions.props`. Public engineering packages have explicit NuGet source mappings. |
| Projects | All 57 owned C# projects are under `src`: 49 main-solution projects and eight package-backed samples. The additional upstream internal tools project stays in the required common tree. |
| Test filenames | Nineteen test projects use `.Tests` or `.IntegrationTests`. Explicit assembly names and namespaces preserve existing identities and friend-assembly contracts. |
| Build analyzers | Existing pre-SDK output isolation remains in the build-analyzer projects, with outputs redirected into Arcade's artifact tree. Existing guard and logging policies remain enabled. |
| Testing | A custom Arcade test-runner extension uses the existing centrally pinned NUnit adapter and test SDK. Semantic and package schedulers remain available. |
| Packaging | Package IDs, preview versions, portable symbols and Source Link remain unchanged. Arcade's explicit symbol-format validation opt-out retains NuGet.org `.snupkg` packages. |
| License | The original MIT copyright and grant remain, with the title and reservation line formatted for Arcade's license validator. |
| Container isolation | Finite tasks build in private workspaces and export evidence/packages even on failure. Compiler outputs are not shared through the host artifact mount. Worktree-aware launchers mount resolved Git metadata read-only. |
| Private restores | Explicit restore caches also supply Arcade's package-import root. Sample builds use private compiler processes to avoid retaining analyzers from deleted temporary feeds. |
| Optional features | Microsoft signing, Visual Studio insertion, localization, IBC training and Azure DevOps integration are not enabled. Existing GitHub CI remains authoritative. |

`docker compose run --rm tooling layout` inventories every tracked or
nonignored source addition, verifies upstream checksums, checks project and
solution/filter references, validates static MSBuild inputs and evaluates all
owned project output paths. PR gates invoke this check. Its machine-readable
report is `artifacts/layout-audit/report.json`.

Git administration, separate checkouts, ignored generated output and local
caches are outside the source inventory. Synthetic project files embedded in
test source remain fixtures rather than repository projects.

Validation logs for this migration are retained under
`artifacts/arcade-migration`. GitHub checks on the exact pushed commit are the
merge authority. See [repository layout](repository-layout.md) for commands.
