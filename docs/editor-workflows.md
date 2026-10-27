# Editor feedback and debugging

SharpProof's portable analyzer provides editor diagnostics when the editor's Roslyn host meets the [support boundary](preview-support.md). Native verification runs at build time, not on every keystroke. Automated Roslyn workspace tests cover code-fix behavior; this is not certification of every IDE/version or extension combination.

Interactive Visual Studio, VS Code with the Microsoft C# extension, and Rider sessions
are not qualified; a command-line SDK does not qualify an IDE.

## Safe fixes

The packaged SP0024 code fix can correct the type argument of `Contract.Result<T>()` to the enclosing method's return type inside an `Ensures` clause, when it can identify the actual SharpProof contract API. Preview the diff and rebuild. It does not establish the postcondition's truth. Fix All uses the same eligible change across its selected scope. It must not add assumptions, weaken a predicate, or move contract clauses to manufacture success.

The code-fix assembly is a design-time payload so command-line compilation does not acquire Roslyn Workspaces dependencies. If an editor offers no fix, check the actual diagnostic and editor compiler-host compatibility. A supported command-line SDK alone does not establish the editor's Roslyn version.

The optional matching `SharpProof.CodeFixes` package also supplies the informational
SPED0001 suggestion, **Generate SharpProof contract companion**. It exposes the action
through diagnostic UI as well as the existing refactoring. Reference the package with
`PrivateAssets="all"`; removing it removes this optional suggestion. Generation and
missing-member repair have no Fix All, retain throwing placeholders, and decline
ambiguous/incompatible or source-generated edits. They do not infer valid contracts.
The diagnostic suggestion also declines cross-file partial types; the existing
explicit refactoring can handle compatible cross-file partials. Missing a suggestion
on an ineligible shape is different from an eligible action failing to load.

## Repository tasks

Open the repository in a compatible Dev Container client using [.devcontainer/devcontainer.json](../.devcontainer/devcontainer.json). Its container-owned persistent workspace is separate from the host checkout; choose and inspect the correct branch before editing. Use **SharpProof: targeted tests (Dev Container)** for focused tests inside that workspace. The host **targeted tests (Docker)** and **pack (Docker)** tasks run finite canonical tasks through the [host wrapper](../eng/scripts/Invoke-SharpProof.ps1), which supplies read-only metadata mounts for Git worktrees as needed.

Do not run concurrent builds against the same bind-mounted outputs. A targeted test is feedback for that selected case, not full qualification. See [contributing](../CONTRIBUTING.md) for the broader gates.

## Attach to managed processes

Inside the Dev Container, **SharpProof: attach to managed process (Dev Container)** uses the managed debugger's process picker. Choose the relevant worker, host or compiler process for the reproduction. Source matching is exact and Just My Code is disabled so package/tool frames can be inspected. Build and reproduce in the persistent workspace; finite task copies may use different source paths and disappear after execution.

Worker processes can be short-lived. Start with a reproducible focused test and a breakpoint in the owning managed stage. Analyzer code runs in the compiler/analysis host, collector code during final compilation, build tasks in Core MSBuild, and native verification in the out-of-process host/worker. Do not attach to an unrelated build or change proof budgets simply to hide a failure.

For consumer package debugging, use available Source Link/portable symbols and the exact package source revision. Inspect the package/symbol identity when a breakpoint does not bind. Local binlogs can help diagnose evaluated configuration, but review them for private properties and credentials before sharing; see [support](support.md).
