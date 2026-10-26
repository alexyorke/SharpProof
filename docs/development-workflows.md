# Development workflows and qualification evidence

Use the canonical Linux amd64 container for repository execution. Host commands below start at the repository root. Commands beginning with `sp`, `pwsh`, or `dotnet` run inside the persistent canonical development container; [container development](container-development.md) explains its workspace and the disposable finite task workspace.

## Choose a workflow

| Need | Entry point | Evidence and limits |
| --- | --- | --- |
| One regression | `tooling test -Target <project.csproj> -TestFilter <filter>` | Restores/builds the project, then runs its assembly with the VSTest filter |
| Changed project feedback | `tooling test-changed` or `tooling quick` | Selects impacted tests from Git changes; `quick` selects Debug and `-Fast` |
| Full development check | `tooling check` | Restore/build, semantic tests, package tests, and timing profile |
| Pull request qualification | `tooling pr` | Documentation checks, Release contract/build, and semantic/package phases with the canonical category filter |
| Documentation links | `tooling docs` | Local Markdown links and anchors |
| Portable/worker/package suite | `tooling portable-tests`, `worker-tests`, or `package-tests` | Selects the corresponding suite; package tests use their scheduler |
| Repository analysis and samples | `tooling self-apply -Configuration Release` | Builds every tracked source project with advisory portable analysis, then runs the complete package sample matrix; required by PR CI |
| Corpus qualification | `tooling corpus -Configuration Release` | Snapshot, metamorphic invariance, support classifications, Unknown ratchets, and replay |
| Coverage | `tooling coverage` | Release Cobertura line coverage of the C# sources, merged into `artifacts/coverage/summary.md` |
| Clean package graph | `tooling pack -Configuration Release` | Exact-commit product packages; `tooling pack-tool` packs the optional developer packages |
| Retained and rotating fuzz | `tooling fuzz-nightly -Configuration Release` | Bounded clean-source campaign with validated per-run results |
| Scheduled nightly profile | `tooling nightly` | Runs the sequential nightly profile, including corpus and bounded fuzz; CI uploads all evidence |
| Exploratory generative campaign | `tooling test ... -TestFilter TestCategory=LongFuzz` | Explicit test harnesses; findings are recorded for review and CI checks error severity afterward |

The dispatcher [Invoke-SharpProofContainer.ps1](../eng/scripts/Invoke-SharpProofContainer.ps1) owns accepted commands and parameters. [contract.json](../eng/acceptance/contract.json) owns test parallelism and wall-time budgets. A successful selected test or feedback profile establishes only its selected scope.

The nightly fuzz runner rebuild uses the 600-second solution-build allowance. The
worker's default per-project wall-time budget remains 300 seconds; these measure
different boundaries. See [analysis limits](analysis-limits.md) before changing either
value.

## Parameters and test selection

The dispatcher defaults to `-Configuration Debug`, `-Target SharpProof.slnx`, no filter, and no supplied package feed. `pr` selects Release internally. `pr-gates` and `fuzz-nightly` require Release; coverage and packing build Release internally. `-Target` selects input for restore/build/test, rather than overriding specialized suites.

`-NoBuild` and `-Fast` are supported for `test`, `test-changed`, `semantic-tests`, `portable-tests`, `worker-tests`, and `package-tests`. `-NoBuild` requires existing outputs in the same workspace. `-Fast` disables build analyzers for development feedback; it does not remove generation or tests. `-ReuseTestHarness` is specific to `package-tests`. See [Invoke-SharpProofPackageTests.ps1](../eng/scripts/Invoke-SharpProofPackageTests.ps1) for package harness preparation and scheduling.

Change selection includes tracked differences and nonignored untracked paths. The comparison order is `-ComparisonRef`, `SHARPPROOF_CHANGED_BASE_REF`, `HEAD` for a dirty checkout, then `HEAD^` for a clean checkout. Selection follows project references, imports, linked inputs, and shared build files; uncertain ownership expands to the full graph. A first commit without `HEAD^` needs an explicit comparison. The dispatcher exposes the environment variable; script-only options are used in the persistent container:

```text
pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProofChangedTests.ps1 -ComparisonRef origin/master -PlanOnly
pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProofDevCheck.ps1 -Configuration Release -PlanOnly
```

`-PlanOnly` prints selection or the developer command plan without running the selected builds/tests. These script parameters are not dispatcher parameters. Sources: [changed-test selection](../eng/scripts/Invoke-SharpProofChangedTests.ps1), [developer check](../eng/scripts/Invoke-SharpProofDevCheck.ps1), and [command plan](../eng/scripts/Get-SharpProofDevCheckPlan.ps1).

## Canonical PR jobs

The canonical filter is `TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus`. [ci.yml](../.github/workflows/ci.yml) splits it across parallel jobs:

- `canonical-build` builds the complete Release solution with all analyzers.
- `canonical-main` runs `semantic-tests -SemanticCohort Remaining -TaskGroup <WorkerA|WorkerB|Projects>` on three runners.
- `canonical-package` runs `package-tests -PackagePartitionCount 4 -PackagePartition <0..3>` on four runners. Partitions hash the fully qualified method identity, so all parameter cases of a method stay together. Partition zero also runs `docs` and `layout`.
- `canonical-gates` runs `semantic-tests -SemanticCohort Gates` as the corpus part and the remainder shards.
- `formatting`, `self-application`, `self-application-samples`, and `api-coverage` run alongside.
- `build-and-test` is the single required check; it succeeds only when every job above succeeded.

`-Fast` skips analyzers only in the test jobs; `canonical-build` enforces them. `-SemanticCohort`, `-TaskGroup`, and `-TestResultsDirectory` apply only to `pr`, `pr-gates`, and `semantic-tests`. Source: [semantic scheduler](../eng/scripts/Invoke-SharpProofSemanticTests.ps1).

## Coverage

```text
docker compose run --rm tooling coverage
```

[Invoke-SharpProofCoverage.ps1](../eng/scripts/Invoke-SharpProofCoverage.ps1) builds Release, runs the semantic and package tests with the Microsoft collector (Cobertura), collects `SharpProof.Attributes` in its own test host, then merges every report by file and line: a line is covered when any report hit it. It writes per-project and total line coverage to `artifacts/coverage/summary.md`. `-MinimumLinePercent` optionally fails below an aggregate floor. [coverage.yml](../.github/workflows/coverage.yml) runs it on `master` pushes and on demand.

## CI runner setup and telemetry

Every container job builds the tooling image through [prepare-qualified-packages](../.github/actions/prepare-qualified-packages/action.yml) with the default docker driver. The `windows-native` stage, which compiles Z3 for win-x64 and win-arm64, takes about 14 of an uncached build's 15 minutes. The action caches that stage's 53 MB `/payload` output in the Actions cache, keyed on the stage text, its base image, the [cross-build recipe](../eng/container/windows-native/README.md), and the `z3` section of `toolchain.json` (the only part the stage reads), so unrelated catalog changes keep the cache. On a hit, `--build-context windows-native=<payload>` replaces the stage. `Prepare-NativePayload.ps1` still checks both DLLs against the pinned byte lengths, SHA-256 digests, and receipt provenance, so a cached payload must match what a fresh compile produces. On a miss, the stage builds as before and the job saves its verified output. Default-branch entries are readable by every pull request, and pull-request entries stay scoped to their own branch. Scheduled runs and release tags skip the cache and compile from source, so the recipe keeps proving it reproduces the pins and releases never ship a cached payload. The image pulls three .NET SDK base images: the build SDK, the minimum SDK, and the minimum-framework SDK, which also supplies the pinned test runtime. The serviced PowerShell archive and the API inventory reference pack are checksum-pinned downloads rather than images. Base-image pulls and the rest of the image take under a minute, so no buildx layer cache is used.

[quality.yml](../.github/workflows/quality.yml) builds the lint image on top of the prepared tooling image instead of running `docker compose build lint`, which would rebuild tooling through its `service:tooling` context.

The same action starts a temporary [runner telemetry](../.github/actions/runner-telemetry/action.yml) sampler. Each job's last step stops it and adds host CPU, I/O wait, memory, swap, pressure-stall, and per-container peaks plus a per-minute timeline to the job summary. The raw samples are uploaded as `runner-telemetry-<job>-<attempt>`. It only observes the runner from outside the container and changes no limits. Remove it once runner sizing is settled.

## Corpus and native effect oracles

`corpus` prints a structured result with case counts, support counts, Unknown reasons, replay counts, and failures. [The corpus guide](../src/SharpProof.Gates/README.md) describes the case universe and independent ratchets. Use `sp corpus-update -Configuration Release` only for an intentional observation refresh in a persistent workspace, then review the source changes. A finite task copy discards ordinary source edits.

The [Gates executable](../src/SharpProof.Gates/Program.cs) also exposes five developer oracle modes: `exception-oracle`, `allocation-oracle`, `purity-oracle`, `capability-oracle`, and `effectcontract-oracle`. These are executable subcommands rather than `tooling` commands. Inside the canonical container:

```text
dotnet run --project src/SharpProof.Gates/SharpProof.Gates.csproj -c Release -- exception-oracle --limit 5
```

Omit `--limit` or use zero for the full pinned method universe. Positive limits select method IDs in ordinal order and cannot exceed the universe size. A partial run is explicitly nonexhaustive. Reports include the universe hash/count, checked count, native outcomes/reasons, runtime observations, and reachable-source summary counts. [NativeEffectOracleGate.cs](../src/SharpProof.Gates/Corpus/NativeEffectOracleGate.cs) consumes the compiler artifact and independently checks applicable IL/runtime witnesses. Some purity/capability/lock cases explicitly report an oracle as not run; a successful report is not a runtime witness for every effect verdict. These reports do not publish worker proof authority.

## Bounded fuzz campaigns

[Invoke-SharpProofFuzzCampaign.ps1](../eng/scripts/Invoke-SharpProofFuzzCampaign.ps1) combines a UTC-day rotating seed with [retained seeds](../eng/fuzz/retained-seeds.json). The current contract requests 10,000 rotating cases; the retained manifest requests 1,000 per seed. The total campaign cap is 1,000,000. The runner is rebuilt from clean exact-commit source, and source identity is checked again after rebuilding and after the campaign.

`tooling fuzz-nightly -Configuration Release` writes `artifacts/fuzz/nightly`. The script's advanced options are `-OutputDirectory`, `-RotatingSeed`, `-RotatingCases`, and `-RetainedCases`; zero case overrides use the configured budgets. Run advanced invocations in the persistent container. Output ownership is leased so overlapping writers cannot replace the same campaign. Each rotating/retained run produces `.stdout.json` and `.stderr.txt`; validated results feed `campaign.json`, including requested/observed counts, seeds, commit, and pass status. Missing, malformed, incomplete, or mismatched runner evidence fails the campaign.

Runner compilation retains `runner-build.stdout.txt` and `runner-build.stderr.txt`
under that output directory and uses the 600-second solution-build allowance. A failed
build prints bounded stream tails; exit 124 identifies the configured build timeout.
Retained invocations keep their 300-second limit and rotating invocations keep their
1200-second limit. The build fix does not change the campaign's seeds, case counts,
or worker proof budgets.

For a bounded individual replay, the fuzz executable accepts `--cases` (default 1,000, range 1..1,000,000), `--seed` (signed 32-bit integer, default `0x5A17`), and `--max-parallelism` (default 4, range 1..4):

```text
dotnet run --project src/SharpProof.Fuzz/SharpProof.Fuzz.csproj -c Release -- --cases 1000 --seed 23063 --max-parallelism 4
```

Sources: [FuzzOptions.cs](../src/SharpProof.Fuzz/FuzzOptions.cs), [FuzzRunner.cs](../src/SharpProof.Fuzz/FuzzRunner.cs), and [evidence lifecycle](../eng/scripts/SharpProof.FuzzEvidenceLifecycle.ps1). A standalone replay does not produce a qualified clean-source campaign envelope. [nightly.yml](../.github/workflows/nightly.yml) schedules the full nightly profile and uploads its artifacts even on failure.

## LongFuzz exploration and finding review

The six Explicit harnesses and their feature families are described in [container development](container-development.md#long-running-fuzz-campaign). They share the process's wall-clock budget; remaining time is apportioned among harnesses. Environment defaults in [LongFuzzSession.cs](../src/SharpProof.Worker.Tests/LongFuzz/LongFuzzSession.cs) are:

| Variable | Default | Meaning |
| --- | --- | --- |
| `SHARPPROOF_LONGFUZZ_MINUTES` | `5` | Whole selected run's time budget |
| `SHARPPROOF_LONGFUZZ_SEED_START` | `1` | First generated case seed |
| `SHARPPROOF_LONGFUZZ_SEED_END` | `int.MaxValue` | Exclusive seed limit |
| `SHARPPROOF_LONGFUZZ_OUTPUT` | `artifacts/long-fuzz` | Evidence directory relative to repository root, or absolute path |

Replay needs the harness identity and seed together. `hits.jsonl` records claim, worker outcome/reason, CLR observation, details, and full source; `hits/*.cs` preserves standalone sources with a header. `stats-<harness>.json` refreshes every 50 cases and at completion. `events.log` records live annotations. Sources can receive numeric suffixes when multiple findings share a harness/seed/kind.

The NUnit harnesses record findings and continue. [long-fuzz.yml](../.github/workflows/long-fuzz.yml) adds a separate error-severity check after artifact upload. It runs nightly or manually with six shards, disjoint 250,000-seed ranges, a default 300-minute budget per shard, manual range 1..320 minutes, and a 350-minute job timeout. It is advisory and does not run on PRs. Review errors and warnings separately; an unobserved refutation on a finite CLR input grid does not by itself establish an incorrect refutation. Turn a confirmed finding into a focused deterministic regression before editing expectations.

## Packed qualification and evidence inspection

`pack` exports the exact three-package graph to `artifacts/container-packages`; it requires clean exact-commit source. Run `package-tests` and `samples` against those bytes. The sample harness checks eight package-only projects, expected diagnostics, strict Proven claims, and mixed typed outcomes; see [samples](../src/samples/README.md).

[Release process](../eng/release/README.md) describes tag publication. Keep the uploaded package artifact's source identity with consumer results. [Golden tests](../tests/golden/README.md) documents all four fixture stages and guarded original-workspace updates.

When inspecting CI, begin with the failing phase's summary, source SHA, requested scope, and artifact name. Then inspect bounded failures in the matching report and timings. Keep raw corpus observations, coverage counts, bounded fuzz campaign evidence, and exploratory findings distinct: they establish different claims and have different stopping conditions.
