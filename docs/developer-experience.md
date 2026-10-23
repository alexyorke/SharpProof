# Developer tools

SharpProof's optional developer packages are versioned with the three product packages.
`SharpProof.Tool` supplies the `sharpproof` .NET tool, `SharpProof.Templates` supplies
`dotnet new sharpproof`, and `SharpProof.CodeFixes` supplies optional editor actions.
These additions do not change existing verification or assumption policies. Package
availability on a public feed is not implied; use the matching packages from your
configured feed or this checkout's qualified package artifacts.

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

Use `--verification container` to generate strict verification, retained evidence,
and a CI workflow. Run `docker compose pull verify`, then
`docker compose run --rm verify build MyLibrary.csproj`. The consumer image is pinned
by digest and derives from the canonical Linux amd64 toolchain. No SharpProof source
checkout is required. Private-feed authentication belongs in your normal NuGet secret
mechanism, never in a committed generated project.

The CLI requires the .NET 10 runtime. In the consumer directory, create a manifest
with `dotnet new tool-manifest` if one does not already exist, then run
`dotnet tool install SharpProof.Tool --version <version>`. Use the same version as
the consumer packages. Teammates use `dotnet tool restore`.

## Inspect setup and evidence

`dotnet sharpproof doctor --project MyLibrary.csproj --verification portable` checks
SDK, evaluated package references, restored versions, and contract configuration.
The container mode also inspects Docker and the configured local consumer image.
Doctor does not restore, build, install, pull images, or execute application code.
Project evaluation reads MSBuild imports without invoking build targets.
Run it only against a trusted project. A passing setup check does not execute a proof
or establish that restored files match today's edited source.

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

For a GitHub Actions Bash step, the following example consumes the starter workflow's
`sharpproof-evidence` artifact. Supply the reviewed `BASELINE_RUN_ID`, expected
`BASE_BRANCH` and `BASE_SHA`, and a read-only `GH_TOKEN` from CI secrets. Restore the
matching local tool manifest first. Set `SHARPPROOF_REGRESSION_GATE=true` to enable gating.

```bash
set -euo pipefail
: "${BASELINE_RUN_ID:?Select a reviewed successful base-branch run}"
: "${BASE_BRANCH:?Supply the expected base branch}"
: "${BASE_SHA:?Supply the expected base commit}"
baseline_metadata=$(gh run view "$BASELINE_RUN_ID" --repo "$GITHUB_REPOSITORY" \
  --json conclusion,headBranch,headSha)
test "$(jq -r .conclusion <<< "$baseline_metadata")" = success
test "$(jq -r .headBranch <<< "$baseline_metadata")" = "$BASE_BRANCH"
test "$(jq -r .headSha <<< "$baseline_metadata")" = "$BASE_SHA"
gh run download "$BASELINE_RUN_ID" --repo "$GITHUB_REPOSITORY" \
  --name sharpproof-evidence --dir baseline
dotnet tool restore
gate=()
if [[ "${SHARPPROOF_REGRESSION_GATE:-false}" = true ]]; then
  gate+=(--fail-on-regression)
fi
dotnet sharpproof compare --baseline baseline/result.json \
  --current artifacts/sharpproof/result.json "${gate[@]}" \
  | tee artifacts/sharpproof/comparison.txt
```

The upload step retains the comparison alongside the current evidence even when the
gate fails. A missing baseline is an explicit setup failure; this example never creates
one from the current run.

## Editor assistance

Reference `SharpProof.CodeFixes` privately from a C# project to enable editor actions.
Use the package version matching the consumer packages, for example
`dotnet add package SharpProof.CodeFixes --version <version>`, then set
`PrivateAssets="all"` on that reference.
Select a supported top-level interface/class and choose **Generate SharpProof contract companion**.
The preview adds ordinary editable source with explicit receivers, generic constraints,
matching parameter types, and placeholder bodies. No contracts are guessed.
SPCF0004 offers missing-member scaffolding when the target and missing names are unambiguous.
Ambiguous overload repairs and Fix All are intentionally unavailable.

## Export an NUnit regression

Run `dotnet sharpproof export-test --project MyLibrary.csproj --result artifacts/sharpproof/result.json --claim <id> --output ../MyLibrary.Regressions`.
Use the matching single-target project. The output directory must not already exist,
and its parent directory must exist. Export writes source without executing consumer code.
Keep the generated project outside the consumer's source tree so its test source is not
included by the consumer's default compile glob. Run
`dotnet test ../MyLibrary.Regressions/SharpProof.Regression.csproj` in a canonical
container workspace that contains both projects. The starter's project-only Compose
mount needs to be extended to expose this sibling directory before running that test.

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
Timings are observations, not a claim of improvement over an unmeasured baseline.
`-Fast` permits development feedback without repository build analyzers; it does not
qualify a release and is never used by the release workflow.

`tooling developer-packages` requires clean exact-commit source and exports the same six
packages under `artifacts/developer-experience/<run-id>/packages` without running the
consumer demonstration. The release process records the new template/editor packages
separately under `artifacts/developer-packages`; the existing optional-tool publisher
handles `SharpProof.Tool`. The original `tooling pack` keeps its three-package graph.
Template packaging refuses an absent or mutable image reference.
