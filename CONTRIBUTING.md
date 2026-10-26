# Contributing

Use the canonical Linux amd64 container for repository .NET, PowerShell, test, packaging, and acceptance work. Read [AGENTS.md](AGENTS.md) and [container development](docs/container-development.md) before running commands.

## Start a change

Check the branch, HEAD, upstream, worktrees, and dirty state. Preserve unrelated edits. Work on a branch from the intended base; fetch before merging or rebasing.

For a bug, reproduce the failure or trace the incorrect behavior before editing. Add a focused regression that exercises the actual failing boundary. A passing test must establish the required behavior, not merely mirror the implementation.

## Find the first useful check

| Change | Start here | First useful check |
| --- | --- | --- |
| Portable diagnostic | `SharpProof.Analyzer`, `SharpProof.Analyzer.Core`, analyzer tests | Focused analyzer regression and diagnostic help link |
| Contract API/docs | `SharpProof.Attributes`, Attributes tests | Public API/XML checks and documentation snippets |
| Language semantics | Collector, frontend, verifier and replay boundaries | A compiler-backed regression covering affected stages |
| Package/MSBuild integration | `SharpProof.Package`, `SharpProof.Verifier`, package tests | Package-backed external consumer reproducing the issue |
| Protocol/cache/publication | Worker protocol and validator tests | Matching-evidence, malformed-input and compatibility regressions |
| Guides/templates | `docs`, `templates` | Documentation checks, snippet tests and exact-feed template smoke check |

For a first contribution, reproduce one issue, locate its existing test cohort, add the smallest meaningful regression, make the fix, run that focused test, then the relevant broader gate below. The [architecture](docs/architecture.md) maps the pipeline; [development workflows](docs/development-workflows.md) explains feedback versus qualification. Hand-maintained files marked `.generated.cs` are edited according to their headers, not by an assumed generator.

## Run validation

Run every repository quality and link check with one command, including image setup:

```sh
docker compose run --rm --build lint
```

Reports are saved under `artifacts/lint`; findings return a failing exit code.
For host Git worktrees, use `pwsh -File eng/scripts/Invoke-SharpProof.ps1 lint`.
See [quality gates](eng/lint/README.md) for the check scope.

Apply the repository's `.editorconfig` formatting rules before submitting changes:

```text
docker compose run --rm tooling dev -lc 'pwsh -NoLogo -NoProfile -File eng/scripts/Format-CSharp.ps1'
docker compose run --rm tooling dev -lc 'pwsh -NoLogo -NoProfile -File eng/scripts/Format-Scripts.ps1'
docker compose run --rm tooling format-check
```

For a Git worktree, use `pwsh -File eng/scripts/Invoke-SharpProof.ps1` in place of `docker compose run --rm tooling` so the container can read its Git metadata. The apply command writes to the checkout; `format-check` verifies a disposable snapshot with `--verify-no-changes`. Whitespace formatting covers every tracked C# file, including samples, templates, golden fixtures, script helpers, and checked-in generated sources. The main solution also runs the full `dotnet format` command, including configured warning-level style and analyzer fixes. PR builds and packaging/release fail when formatting drifts.

`Format-Scripts.ps1` formats all tracked `.ps1`, `.psm1`, and `.psd1` files with PSScriptAnalyzer's formatter and checks parsing plus warning/error rules. It also runs ShellCheck and shfmt on every tracked `.sh`/`.bash` file and shell shebang script, including extensionless files. PowerShell uses four-space indentation; shell scripts use two spaces, indented case arms, and spaces after redirections. Check mode never rewrites sources. `format-check` includes these checks and regression tests proving that lint, syntax, and formatting failures are rejected.

Tool versions and download SHA256 values are pinned in [the container catalog](eng/container/toolchain.json). PowerShell [lint settings](eng/PSScriptAnalyzerSettings.psd1) retain default warning/error rules except host status output, plural helper nouns, and interactive `ShouldProcess` requirements: these are unattended build/test utilities with existing helper APIs, and status output must not pollute data pipelines. The [formatter settings](eng/PowerShellFormatting.psd1) are separate from lint policy. A narrowly scoped suppression documents the legacy `RepositoryRoot` compatibility parameter in the framework-feed helper; its targeting packs come from the selected SDK.

```text
docker compose run --rm tooling test -Target src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj -TestFilter FullyQualifiedName~NameOfRegression
docker compose run --rm tooling pr
```

Replace the project and filter with the relevant test. `pr` runs the Release PR gates, excluding tests categorized Performance, Coverage, or Corpus. Run additional relevant gates:

```text
docker compose run --rm tooling coverage -Configuration Release
docker compose run --rm tooling corpus -Configuration Release
docker compose run --rm tooling security
docker compose run --rm tooling samples -Configuration Release
```

[Container development](docs/container-development.md) explains container setup and supported commands. [Development workflows](docs/development-workflows.md) covers test selection, CI cohort reconciliation, coverage shards, acceptance evidence, fuzzing, and package qualification. Select the relevant qualification gates explicitly; `quick` and `-Fast` are development feedback.

Do not run concurrent builds against the same bind-mounted outputs. Use distinct Compose projects for independent worktrees with the same directory basename.

## Soundness and evidence

Preserve typed outcomes and reasons. Unsupported operations, incomplete summaries, malformed artifacts, and unreplayable counterexamples must not become `Proven` or definite violations. Do not weaken frozen expectations, proof rules, coverage floors, timeouts, or support classifications to make a regression pass.

A semantic change must account for compiler lowering, VC construction, replay, protocol validation, caching, and diagnostic projection where affected. Review the [semantics](SEMANTICS.md), [architecture](docs/architecture.md), and [golden tests](tests/golden/README.md).

Update versioned artifacts when their meaning or shape changes. Declarative files with `.generated.cs` names are hand-maintained where their header says so; a retired generator is not an update workflow.

## Documentation and review

Keep commands runnable from their stated working directory. Use links to current source and avoid duplicating volatile counters. C# fences in the root README, Markdown files under `docs/`, and `src/samples/README.md` are compiled by [DocumentationSnippetTests](src/SharpProof.Analyzer.Tests/DocumentationSnippetTests.cs); each fence must be a complete compilation unit.

Describe the user-visible before/after behavior, relevant validation, and material limits in the pull request. Review the resulting diff and stage explicit paths.
