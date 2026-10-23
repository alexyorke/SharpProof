# Developer tools

SharpProof's optional developer packages are versioned with the three product packages.
`SharpProof.Tool` supplies the `sharpproof` .NET tool, `SharpProof.Templates` supplies
`dotnet new sharpproof`, and `SharpProof.CodeFixes` supplies optional editor actions.
These additions do not change existing verification or assumption policies. Package
availability on a public feed is not implied; use the matching packages from your
configured feed or this checkout's qualified package artifacts.
The [delivery checklist](developer-experience-plan.md) distinguishes implementation,
workflow evidence, and the remaining CI and release decisions.
Official `1.0.0-preview.1` binaries, symbols, image, and detailed release evidence use
the private `alexyorke/SharpProof-preview` path; source remains public. Public CI uses
distinct validation versions. The [private release receipt](private-preview-release-receipt.md)
keeps actual publication and access evidence pending until verified.

## Install and start

Configure the feed containing the matching preview packages, then install the template
with `dotnet new install SharpProof.Templates::<version>`. Create and enter a project:

```sh
dotnet new sharpproof -n MyLibrary -o MyLibrary --verification portable
cd MyLibrary
dotnet build
```

The generated project targets net10.0 and pins the compiler SDK, C# 13, and package
versions. The portable variant explicitly selects advisory analysis without native
verification. Its demonstration caller deliberately emits SP0027; change -1 to 0 to
remove it. The [checkout project templates](../templates/README.md) remain available
through their existing installation workflow.

For a layout that keeps tools, CI, the application, and regressions together, use the
workspace template instead:

```sh
dotnet new sharpproof-workspace -n MyLibrary -o MyLibrary --verification portable
cd MyLibrary
dotnet tool restore
dotnet build src/app/MyLibrary.csproj
```

It generates `MyLibrary.slnx`, `src/app/MyLibrary.csproj`, `tests/`, the pinned SDK/local
tool manifest, and a credential-free private-feed configuration at the root. C# stays
pinned to 13 for export. Both templates include the matching private editor package
by default; `--editor false` omits it. The core application reference remains the
public `SharpProof` facade. Private-feed authentication is needed before installing
the template package as well as when restoring the generated workspace.

Choose `--verification container` when creating the project to generate strict
verification, retained evidence, and a CI workflow. Run `docker compose pull verify`,
`docker compose run --rm verify restore MyLibrary.csproj`, then
`docker compose run --rm verify build MyLibrary.csproj --no-restore`. The consumer image is pinned
by digest and derives from the canonical Linux amd64 toolchain. No SharpProof source
checkout is required. Private-feed authentication belongs in your normal NuGet secret
mechanism, never in a committed generated project.
The portable variant produces analyzer diagnostics without worker evidence; the
evidence commands below use the container variant's retained paths. They require
access to its matching packages and qualified image; public publication is a separate
release step.

The CLI executes on the .NET 10 runtime; installing or restoring a local tool also
requires a .NET SDK. The template's pinned SDK and the consumer image provide both.
Both templates already pin the matching local tool, so use `dotnet tool restore`.
For an existing project without that entry, create a manifest
with `dotnet new tool-manifest` if one does not already exist, then run
`dotnet tool install SharpProof.Tool --version <version>`. Use the same version as
the consumer packages. Teammates use `dotnet tool restore`.

The container does not inherit the host's user-level NuGet configuration or package
cache. Put source URLs in a project `NuGet.config`, use container-visible paths for
local feeds, and supply private-feed credentials through your usual secret mechanism.
A local feed outside the project needs an explicit read-only bind mount. The generated
CI workflow likewise needs registry/feed access configured before its restore/build
steps. Keep credentials out of the committed configuration.
Generated configuration names the private source `SharpProofPreview`; Compose forwards
the process variable `NuGetPackageSourceCredentials_SharpProofPreview` without storing
its value. Authenticate private GHCR reads through your Docker credential store.

For the single-project container starter, restore the local tool and inspect setup through the same
service so evaluation uses its container-owned restore assets:

```sh
docker compose run --rm verify tool restore
docker compose run --rm verify sharpproof doctor --project MyLibrary.csproj --verification container
```

These commands assume the matching tool is already recorded in the local manifest.
Host explanation and comparison can read the retained reports; exporting a test can
also require source and reference files that remain inside the build container.

For the container workspace variant, run from its root with the nested app path:

```sh
docker compose pull verify
docker compose run --rm verify tool restore
docker compose run --rm verify restore src/app/MyLibrary.csproj
docker compose run --rm verify build src/app/MyLibrary.csproj --no-restore
docker compose run --rm verify tool run dotnet-sharpproof -- doctor --project src/app/MyLibrary.csproj --verification container
docker compose run --rm verify tool run dotnet-sharpproof -- explain --result artifacts/sharpproof/result.json
```

## Initialize an existing project

Use the matching local tool to preview configuration for a trusted existing C# project:

```sh
dotnet sharpproof init --project MyLibrary.csproj --verification portable
```

`init` defaults to portable mode and preview only. Review the proposed file plan, then
repeat with `--apply` to write the supported configuration. Initialization does not
establish a proof, install an SDK, authenticate a feed, or replace review of existing
project policy. Keep secret values in your normal secret mechanism.

The supported plan pins the facade, private CodeFixes reference, and local tool to the
installed tool's version, selects the requested profile, and adds the credential-free
`SharpProofPreview` source/mapping. It can reuse ancestor NuGet/tool/central-version
configuration, so inspect every listed path. Container mode also plans Compose,
isolated compiler outputs, and retained evidence paths. Conflicting versions/settings,
ambiguous imports/configuration, inline preview credentials, and linked targets are
rejected rather than silently replaced. No restore, build, or project-code execution
is part of preview or apply.

Initialization covers a simple existing project, not a new multi-project workspace.
Its container Compose/output configuration is scoped to the selected project directory.
Use the workspace template for an app-under-`src/` and regressions-under-`tests/`
topology. A flat initialized project can use standalone regression export to a sibling;
it cannot place a workspace export inside its own source root. Existing qualified
workspaces can use `export-test --workspace` independently. Initializing a nested
workspace project may decline inherited output properties or report-path conflicts
that require manual integration.

Container initialization uses `--verification container` and requires
`--consumer-image ghcr.io/alexyorke/sharpproof-preview@sha256:<qualified-digest>` unless
a compatible existing pinned Compose configuration supplies that image. Use the
qualified immutable digest from the private release receipt; a version tag or an
invented digest is not image authority. Preview the container plan before applying it.

## Select a report format

Named-option `doctor --project`, `explain --result`, `compare`, `export-test`, and
`init` accept `--format text|json|markdown`; text is the default. For example:

```sh
dotnet sharpproof doctor --project src/MyLibrary/MyLibrary.csproj --format json
dotnet sharpproof explain --result artifacts/sharpproof/result.json --format markdown
dotnet sharpproof compare --baseline baseline/result.json --current artifacts/sharpproof/result.json --format json
```

JSON/Markdown reports are one document on stdout, including setup/regression status 1.
Invalid arguments or evidence return 2, leave stdout empty, and write the error to
stderr; cancellation returns 130. Exit 0 means command processing completed, not that
every selected contract is Proven. Legacy `doctor [--json]` and positional
`explain <result.json> ... [--json]` keep their existing syntax and stored-evidence scope.

## Inspect setup and evidence

`dotnet sharpproof doctor --project MyLibrary.csproj --verification portable` checks
SDK, evaluated package references, restored versions, and contract configuration.
Outside the canonical container, explicit container mode also inspects Docker and
the configured local consumer image. Inside that container, the build still validates
the container contract; doctor does not qualify it.
Doctor does not restore, build, install, pull images, or execute application code.
Project evaluation reads MSBuild imports without invoking build targets.
Run it only against a trusted project. Restore after dependency changes before using
its package checks. A passing setup check does not execute a proof or establish source
freshness. Advisory project checks require SDK 9.0.300 or newer; evaluated active
verification requires SDK 10.0.100 or newer. The tool itself requires .NET 10.

The existing `dotnet sharpproof doctor [--json]` command remains SDK discovery only.
The positional `dotnet sharpproof explain <result.json>` form still reads a standalone
stored result and supports `--file`, `--line`, and `--json`; it does not require the
request or compiler manifest. See the [tool reference](../src/SharpProof.Tool/README.md)
for those options.

`dotnet sharpproof explain --result artifacts/sharpproof/result.json` reads the matching
evidence set and explains outcomes, assumptions, vacuity, and Unknown reasons.
Use `--claim <id>` to focus on one claim. `--request` and `--compiler-manifest` override
the default sibling filenames. Missing or inconsistent evidence is an error.
Validation establishes consistency with recorded inputs; an archived report does not
authenticate an arbitrary worker binary or prove that today's edited source was checked.

The export snapshot records compiler options, source trees, and reference metadata,
so compiler-option or unrelated-source edits can change the artifact digest and miss
the complete worker-response cache even when the lowered verification model is equivalent.
The [cache](../src/SharpProof.Worker/VerificationCache.cs) continues to bind the
[exact artifact bytes](../src/SharpProof.Worker/ArtifactValidator.cs) with its other
existing inputs, without changing callable/claim identities or proof semantics or
promising broader reuse or a performance improvement.

When lowering retains a source abstention, Unknown evidence includes its stage, typed
detail code, and source location. Some failures expose only the broader claim reason.
The explanation never invents an unsupported operation or recommends raising a timeout
to supply missing language semantics.
Reports also retain source method/parameter labels for readable counterexamples;
semantic claim IDs and positional model bindings remain unchanged.

Exit codes are 0 for a successful command, 1 for failed setup or an explicitly enabled
regression gate, and 2 for invalid arguments, unsupported export, or invalid evidence.
An explain command's exit code describes report processing; read the worker run status
and claim outcomes to determine verification success. Cancellation exits 130.

## Compare runs in CI

Run `dotnet sharpproof compare --baseline baseline/result.json --current artifacts/sharpproof/result.json`.
Add `--fail-on-regression` to reject lost proofs, new failures/incomplete coverage,
new vacuity, removed selected claims/callables, and new entry/trust assumptions.
Pass `--baseline-request` and `--baseline-compiler-manifest` when baseline companion
files do not use standard names. Both runs must identify the same assembly and framework.
Incompatible protocol/specification evidence is rejected.

Download baseline evidence from a successful base-branch CI run and archive the current
request, result, compiler manifest, and comparison output together. If no baseline exists,
establish one explicitly from the reviewed base branch; do not silently use the current run.
Contract changes appear as removals and additions. Replacing a baseline is an explicit
reviewed change. Existing Refuted build failures remain failures regardless of comparison.

The generated `.github/workflows/sharpproof.yml` caller uses the
[reusable consumer workflow](../.github/workflows/sharpproof-consumer.yml). Its `uses`
pin and `workflow-revision` are the same qualified public source SHA; do not substitute
a moving branch or the separate private release workflow SHA.

| Caller input | Meaning |
| --- | --- |
| `project` | Workspace-relative consumer project, such as `src/app/MyLibrary.csproj` |
| `image` | Exact private-preview SHA-256 image reference from the qualified receipt |
| `workflow-revision` | Qualified public source SHA, identical to the reusable workflow pin |
| `caller-workflow` | Exact caller path, generated as `.github/workflows/sharpproof.yml` |
| `registry-username` | Account authorized to read the private image/feed |
| `compose-file`, `service`, `evidence-directory` | Defaults: `compose.yaml`, `verify`, `artifacts/sharpproof` |
| `package-feed-url` | Defaults to the private GitHub NuGet feed |
| `mode` | `report` by default; `enforce` adds the comparison regression gate |
| `initialize-baseline` | Default false; explicit reviewed default-branch manual initialization only |

The required `container-read-token` and `package-read-token` secrets are explicit
read-only package credentials; the generated caller maps `SHARPPROOF_PREVIEW_TOKEN`
to them. Keep publisher/signing credentials out of consumer CI. Fork PRs and
`pull_request_target` are rejected before credential-bearing work. Reports go to
the Markdown job summary and retained evidence artifact, not PR comments.

For a PR, baseline lookup requires the exact PR base SHA, same repository/caller,
and a successful trusted base/default-branch run. It validates the numeric artifact
identity, API archive digest, complete file receipt, and actual evidence. A missing,
tampered, incompatible, or stale baseline fails; there is no fallback to the current
run. Establish the first baseline only through a reviewed `workflow_dispatch` on the
repository default branch with `initialize-baseline=true`.

`report` records comparison regressions without failing solely for those regressions.
`enforce` enables `--fail-on-regression`. Both still fail invalid evidence and the
consumer's actual build/proof policy, including strict Refuted failures. Retain the
workflow's numeric `evidence-artifact-id` with the comparison and source identities.

## Editor assistance

Reference `SharpProof.CodeFixes` privately from a C# project to enable editor actions.
Use the package version matching the consumer packages, for example
`dotnet add package SharpProof.CodeFixes --version <version>`, then set
`PrivateAssets="all"` on that reference.
Select a supported top-level interface/class and choose **Generate SharpProof contract companion**.
The preview adds ordinary editable source with explicit receivers, generic constraints,
matching parameter types, and placeholder bodies. No contracts are guessed.
The optional package also emits informational SPED0001 for eligible targets, exposing
the same generation action through diagnostic UI when the editor loads it; the
refactoring remains available. Installing the optional package enables this suggestion,
and removing it removes that suggestion without changing verifier semantics.
The package keeps its compiler-only suggestion analyzer separate from its Workspaces
code-fix assembly. It supplies `SharpProofEditorSuggestions=true` and the corresponding
compiler-visible property; an explicit `SharpProofEditorSuggestions=false` silences
SPED0001.
The suggestion admits eligible source class/interface/record types with ordinary
methods and declines generated or cross-file partial candidates; the explicit
refactoring can still handle compatible cross-file partials.
SPCF0004 offers missing-member scaffolding when the target and missing names are unambiguous.
Ambiguous companion repairs are declined, and companion repair has no Fix All. The
existing Result-type correction retains its own Fix All support.
All real-editor qualification is pending for the three required Windows x64 hosts;
follow the [interactive acceptance runbook](editor-acceptance.md).

## Author a contract and inspect its result

Start with the [complete first-proof example](getting-started.md#first-use-in-three-steps).
Keep direct `Requires` and `Ensures` clauses in a contiguous method prologue; use `Old`
for parameter-entry state and `Result` for the normal return value. Scaffolding only
creates signatures and placeholders: replace those bodies with reviewed clauses using
the [public API](public-api.md) and [companion rules](diagnostic-examples.md#contractfor-diagnostics).

Build the consumer, then review the selected claims, completed run status, assumptions,
and vacuity in its [evidence](verification-results.md). Portable diagnostic silence
does not establish a proof, and Unknown does not establish a counterexample. For a
supported replay-confirmed Refuted postcondition, export the original obligation as a
runtime regression, observe its assertion fail, fix the implementation, then require
both the regression and a fresh strict verification build to pass. Keep ordinary runtime
input validation at application boundaries; static contracts do not insert guards.

## Export an NUnit regression

Exports use a relative `ProjectReference` when the paths share a filesystem root.
Standalone Windows exports across drive roots retain an absolute reference and do
not gain the same relocation behavior. Prefer a workspace with the library under
`src/` and regressions under `tests/`, with both inside the existing canonical Compose
mount. For example, from its workspace root:

```sh
docker compose run --rm verify tool run dotnet-sharpproof -- export-test --project src/app/MyLibrary.csproj --result artifacts/sharpproof/result.json --claim <id> --output tests/MyLibrary.Regression --workspace .
```

`--workspace` requires the consumer and new output to be inside that root and a
compatible existing pinned Linux amd64 `verify` service. The root must expose the
workspace at `/workspace/consumer` with isolated compiler outputs/volumes. Export emits
a regression README with commands using that existing service; it does not create
another image definition, Compose file, or credential copy. Create the `tests` parent
first if it does not exist, and keep the output child absent until export.
Workspace mode requires the finalized root Compose/output-isolation layout; linked
paths, out-of-root project/output/remapped source files, and cross-filesystem-root
references are rejected. Multiple regression exports keep separate compiler outputs.

Move the whole workspace together to preserve the relative reference. A workspace
test does not embed a host-specific absolute project path; source/reference hash
checks still apply while exporting. Rebinding captured inputs can still require the
original build container even though the resulting test project can move.

Run `dotnet sharpproof export-test --project MyLibrary.csproj --result artifacts/sharpproof/result.json --claim <id> --output ../MyLibrary.Regressions`.
Use the matching single-target project. The output directory must not already exist,
and its parent directory must exist. Export writes source without executing consumer code.
Keep the generated project outside the consumer's source tree so its test source is not
included by the consumer's default compile glob. Run
`dotnet test ../MyLibrary.Regressions/SharpProof.Regression.csproj` in a canonical
container workspace that contains both projects. The starter's project-only Compose
mount needs an extra writable parent mount for container export and test. Preserve
the relative directory relationship between consumer and test; use workspace mode
above for moving the complete layout between host paths.

For the generated starter, create a writable sibling `Regressions` directory, then add
this service override in `compose.override.yaml` (merge it with any existing override):

```yaml
services:
  verify:
    volumes:
      - ../Regressions:/workspace/regressions
```

With the local tool restored in that service and the original compiler inputs still
available, export into a new child directory and run the generated project:

```sh
docker compose run --rm verify sharpproof export-test --project MyLibrary.csproj --result artifacts/sharpproof/result.json --claim <id> --output /workspace/regressions/MyLibrary
docker compose run --rm verify test /workspace/regressions/MyLibrary/SharpProof.Regression.csproj
```

If your package sources are configured only in the consumer's `NuGet.config`, first
restore the generated test with
`docker compose run --rm verify restore /workspace/regressions/MyLibrary/SharpProof.Regression.csproj --configfile /workspace/consumer/NuGet.config`,
then add `--no-restore` to its test command. A sibling test project does not inherit
the consumer directory's configuration by its location.

The mounted parent exists; `/workspace/regressions/MyLibrary` must not exist before
export. Mounting the intended output directory itself would create it and make export
fail. The test is expected to fail its original postcondition before the implementation
is fixed; run it again after the fix, then require a fresh strict consumer build.

The initial exporter supports replayed direct `Refuted` postconditions on public,
synchronous static methods without type parameters, declared on public types without
type parameters, with Boolean/integral return values and parameters passed by value.
It checks source/reference hashes and preserves scalar arithmetic, conversions,
parameter-entry Old, and Result. Unsupported expressions, companions, object graphs,
receivers, effects, and Unknown claims produce an explanation without a test.
Use the original build workspace when recorded compiler inputs are unavailable elsewhere.

The exporter rebinds source with the packaged Roslyn compiler, which supports C# through
version 13. Templates explicitly pin C# 13. Source captured with a newer language version
is rejected even when it uses older constructs; use a supported language version when
verification evidence must support regression export.

NUnit projects target net8.0 for netstandard2.0/netstandard2.1 libraries and net8.0
consumers, net9.0 for net9.0 consumers, or net10.0 for net10.0 consumers. Other runtimes
are explicitly unsupported by the initial exporter; a netstandard library is never
emitted as a test host.

The generated project builds its consumer reference in advisory portable mode so a
Refuted verifier gate does not prevent the runtime regression from compiling.
The consumer's ordinary build policy remains unchanged. The assertion represents the
original postcondition: the generated test should fail on the defective implementation
and pass once it is fixed. It does not enable runtime contract checks.

## Qualify developer packages

Set `SHARPPROOF_CONSUMER_IMAGE` to the qualified immutable GHCR image reference, then run
`docker compose run --rm -e SHARPPROOF_CONSUMER_IMAGE tooling developer-experience`.
This builds the solution, packs all six packages, installs the tool/template in isolated
locations, and checks generated portable/native consumers, comparison, and regression export.
Evidence and phase timings are exported beneath `artifacts/developer-experience/<run-id>`.
`run.json` records scope, pass/fail status, analyzer mode, source identity, package
hashes, and consumer image reference. A complete local consumer run requires
`scope=developer-experience`, `qualificationMode=AnalyzerEnabled`, and `status=passed`;
a package-only or feedback-only pass does not establish that result.
Timings are observations, not a claim of improvement over an unmeasured baseline.
`-Fast` permits development feedback without repository build analyzers; it does not
qualify a release and is never used by the release workflow.

`tooling developer-packages` requires clean exact-commit source and exports the same six
packages under `artifacts/developer-experience/<run-id>/packages` without running the
consumer demonstration. The release process records the new template/editor packages
separately under `artifacts/developer-packages`; the existing optional-tool publisher
handles `SharpProof.Tool`. The original `tooling pack` keeps its three-package graph.
Template packaging refuses an absent or mutable image reference.
