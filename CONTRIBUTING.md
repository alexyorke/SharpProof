# Contributing

Use the canonical Linux amd64 container for repository .NET, PowerShell, test, packaging, and acceptance work. Read [AGENTS.md](AGENTS.md) and [container development](docs/container-development.md) before running commands.

## Start a change

Check the branch, HEAD, upstream, worktrees, and dirty state. Preserve unrelated edits. Work on a branch from the intended base; fetch before merging or rebasing.

For a bug, reproduce the failure or trace the incorrect behavior before editing. Add a focused regression that exercises the actual failing boundary. A passing test must establish the required behavior, not merely mirror the implementation.

## Validate

```text
docker compose run --rm tooling test -Target SharpProof.Worker.Test/SharpProof.Worker.Test.csproj -TestFilter FullyQualifiedName~NameOfRegression
docker compose run --rm tooling pr
```

Replace the project and filter with the relevant test. `pr` runs the Release PR gates, excluding tests categorized Performance, Coverage, or Corpus. Run additional relevant gates:

```text
docker compose run --rm -e SHARPPROOF_COVERAGE_COMPARISON_REF=origin/master tooling coverage -Configuration Release
docker compose run --rm tooling corpus -Configuration Release
docker compose run --rm tooling security
docker compose run --rm tooling samples -Configuration Release
```

[Container development](docs/container-development.md) explains each supported command and artifact location. Select the relevant qualification gates explicitly; `quick` and `-Fast` are development feedback.

Do not run concurrent builds against the same bind-mounted outputs. Use distinct Compose projects for independent worktrees with the same directory basename.

## Soundness and evidence

Preserve typed outcomes and reasons. Unsupported operations, incomplete summaries, malformed artifacts, and unreplayable counterexamples must not become `Proven` or definite violations. Do not weaken frozen expectations, proof rules, coverage floors, timeouts, or support classifications to make a regression pass.

A semantic change must account for compiler lowering, VC construction, replay, protocol validation, caching, and diagnostic projection where affected. Review the [semantics](SEMANTICS.md), [architecture](docs/architecture.md), and [golden tests](tests/golden/README.md).

Update versioned artifacts when their meaning or shape changes. Declarative files with `.generated.cs` names are hand-maintained where their header says so; a retired generator is not an update workflow.

## Documentation and review

Keep commands runnable from their stated working directory. Use links to current source and avoid duplicating volatile counters. C# fences in README, docs, and samples are compiled by `DocumentationSnippetTests`; each fence must be a complete compilation unit.

Describe the user-visible before/after behavior, relevant validation, and material limits in the pull request. Review the resulting diff and stage explicit paths.
