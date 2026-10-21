# Development workflows and qualification evidence

Use the canonical Linux amd64 container for repository execution. Host commands below start at the repository root. Commands beginning with `sp`, `pwsh`, or `dotnet` run inside the persistent canonical development container; [container development](container-development.md) explains its workspace and the disposable finite task workspace.

## Choose a workflow

| Need | Entry point | Evidence and limits |
| --- | --- | --- |
| One regression | `tooling test -Target <project.csproj> -TestFilter <filter>` | Restores/builds the project, then runs its assembly with the VSTest filter |
| Changed project feedback | `tooling test-changed` or `tooling quick` | Selects impacted tests from Git changes; `quick` selects Debug and `-Fast` |
| Full development check | `tooling check` | Restore/build, semantic tests, package tests, and timing profile |
| Pull request qualification | `tooling pr` | Documentation checks, Release contract/build, and semantic/package phases with the canonical category filter |
| Documentation maintenance | `tooling docs`; `tooling docs-update` for regeneration | Source-reference drift, local links/anchors, and automation regressions; update exports a file for explicit copy-back |
| Portable/worker/package suite | `tooling portable-tests`, `worker-tests`, or `package-tests` | Selects the corresponding suite; package tests use their scheduler |
| Repository analysis and samples | `tooling self-apply -Configuration Release` | Builds every tracked source project with advisory portable analysis, then runs the complete package sample matrix; required by PR CI |
| Corpus qualification | `tooling corpus -Configuration Release` | Snapshot, metamorphic invariance, support classifications, Unknown ratchets, and replay |
| Coverage qualification | `tooling coverage` with a comparison ref | Release collection, source/PDB authority, coverage floors and changed trusted-code coverage |
| Clean package graph | `tooling pack -Configuration Release` | Exact-commit packages and package layout validation |
| Packed consumer behavior | `tooling package-consumers -PackageSource <feed>` | Real analyzer/verifier and minimum-SDK framework consumers |
| Retained and rotating fuzz | `tooling fuzz-nightly -Configuration Release` | Bounded clean-source campaign with validated per-run results |
| Scheduled nightly profile | `tooling nightly` | Runs the sequential nightly profile, including corpus and bounded fuzz; CI uploads all evidence |
| Exploratory generative campaign | `tooling test ... -TestFilter TestCategory=LongFuzz` | Explicit test harnesses; findings are recorded for review and CI checks error severity afterward |

The dispatcher [Invoke-SharpProofContainer.ps1](../eng/scripts/Invoke-SharpProofContainer.ps1) owns accepted commands and parameters. [contract.json](../eng/acceptance/contract.json) owns trusted pipeline classifications and acceptance wall-time budgets; [baseline.json](../eng/coverage/baseline.json) owns numeric coverage floors. A successful selected test or feedback profile establishes only its selected scope.

[Documentation maintenance](documentation-maintenance.md) explains generation and update PRs. The [generated reference](generated/source-reference.md) preserves literal declarations; the authored guides explain their behavior and limitations.

## Parameters and test selection

The dispatcher defaults to `-Configuration Debug`, `-Target SharpProof.slnx`, no filter, and no supplied package feed. `pr` selects Release internally. `pr-gates` and `fuzz-nightly` require Release; coverage and packing build Release internally. `-Target` selects input for restore/build/test, rather than overriding specialized suites.

`-NoBuild` and `-Fast` are supported for `test`, `test-changed`, `semantic-tests`, `portable-tests`, `worker-tests`, and `package-tests`. `-NoBuild` requires existing outputs in the same workspace. `-Fast` disables build analyzers for development feedback; it does not remove generation or tests. `-ReuseTestHarness` is specific to `package-tests`. See [Invoke-SharpProofPackageTests.ps1](../eng/scripts/Invoke-SharpProofPackageTests.ps1) for package harness preparation and scheduling.

Change selection includes tracked differences and nonignored untracked paths. The comparison order is `-ComparisonRef`, `SHARPPROOF_CHANGED_BASE_REF`, `HEAD` for a dirty checkout, then `HEAD^` for a clean checkout. Selection follows project references, imports, linked inputs, and shared build files; uncertain ownership expands to the full graph. A first commit without `HEAD^` needs an explicit comparison. The dispatcher exposes the environment variable; script-only options are used in the persistent container:

```text
pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProofChangedTests.ps1 -ComparisonRef origin/master -PlanOnly
pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProofDevCheck.ps1 -Configuration Release -PlanOnly
```

`-PlanOnly` prints selection or the developer command plan without running the selected builds/tests. These script parameters are not dispatcher parameters. Sources: [changed-test selection](../eng/scripts/Invoke-SharpProofChangedTests.ps1), [developer check](../eng/scripts/Invoke-SharpProofDevCheck.ps1), and [command plan](../eng/scripts/Get-SharpProofDevCheckPlan.ps1).

## Canonical PR cohorts

The canonical filter is `TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus`. [ci.yml](../.github/workflows/ci.yml) splits this unchanged universe across parallel jobs:

- `canonical-main` runs `pr -SemanticCohort Remaining -TaskGroup <group> -TestResultsDirectory artifacts/canonical-remaining -SkipPackageTests -SkipRepositoryGates` as three parts on separate runners: `worker-a` (`-TaskGroup WorkerA`, remainder shards 1 and 4), `worker-b` (`-TaskGroup WorkerB`, remainder shards 2 and 3) and `projects` (`-TaskGroup Projects`, every other Remaining task). Each part's full Release solution build is an analyzer-enforcing build.
- `canonical-package-partition` runs `package-tests -Configuration Release -Fast -PackagePartitionCount 4 -PackagePartition <0..3>` with the canonical filter on four runners. Partition zero also runs the source-only `repository-gates`. The `canonical-package` check requires every partition and reconciles retained discovery and TRX evidence. `-Fast` skips analyzers only in those builds.
- `canonical-gates` runs `semantic-tests -Configuration Release -SemanticCohort Gates -TestResultsDirectory artifacts/canonical-gates` with the canonical filter. The Gates tests run as four tasks: `gates-corpus` holds the classes that run the whole reviewed corpus through the corpus verifier's worker pool and reserves three scheduler slots, and `gates-remainder-1` to `-3` split the other classes by class name prefix and use one slot each. The last remainder shard takes every class the listed prefixes do not match, so each class runs exactly once.
- `formatting` runs `format-check` while `package-reproducibility` runs the unchanged Full independent-path package reproduction.
- `self-application` runs `self-apply -SelfApplicationPhase Sources`, while `self-application-samples` runs `-SelfApplicationPhase Samples`. The default `All` retains both phases locally. `api-coverage` separately runs the existing inventory, runtime-oracle, and usage-ranking checks and publishes their reports.
- `build-and-test` requires all those jobs to succeed, downloads their evidence into `artifacts/canonical-cohorts/remaining` and `gates`, then runs [Invoke-SharpProofCanonicalCohortGate.ps1](../eng/scripts/Invoke-SharpProofCanonicalCohortGate.ps1), the script behind `canonical-cohort-gate`. The gate reads only evidence and Git identity, so this job runs it on the checksum-verified PowerShell archive from `toolchain.json` instead of building the tooling image.

The self-application phases, API checks, and formatting become ready after repository quality succeeds. Package partitions and independent reproduction start alongside quality so their final aggregation can finish earlier. This limits the first wave of runner requests so the longer semantic and coverage jobs can start promptly; all successful-PR checks and aggregate requirements remain in force.

`All` is the default cohort. `Remaining` and `Gates` require retained test results and the exact canonical filter. Without coverage, `-TaskGroup` accepts `Worker`, `WorkerA`, `WorkerB`, or `Projects` with the `Remaining` cohort. build-and-test moves all Remaining parts' task directories into one cohort directory and keeps each part's receipt as `canonical-cohort.<part>.json`; the gate requires the parts to share their run, source, and selection, and checks their merged tasks, discovery, and cases as one cohort. `pr` retains package phases unless `-SkipPackageTests` is given, retains repository gates unless `-SkipRepositoryGates` is given, and rejects Gates-only selection. `-SemanticCohort`, `-TaskGroup`, and `-TestResultsDirectory` apply only to `pr`, `pr-gates`, and `semantic-tests`.

Reconciliation checks actual NUnit discovery, TRX execution identities/outcomes, source identity, and the CI run/attempt evidence. The six existing Explicit LongFuzz skips have specific allowed identities and reasons; additional skipped tests fail reconciliation. Two independently successful job labels alone do not replace this gate. Sources: [semantic scheduler](../eng/scripts/Invoke-SharpProofSemanticTests.ps1) and [cohort evidence validator](../eng/scripts/SharpProof.CanonicalCohortEvidence.ps1).

Package partitions hash the UTF-8 fully qualified method identity with SHA-256 and take the unsigned digest modulo four. All parameter cases of a method stay together. Each partition retains the complete NUnit discovery dump, selected case names, source/run identity, and TRX receipts. The gate recomputes ownership, requires identical discovered universes, and checks that the four executed case lists cover that universe exactly once. Unexpected skips and unsuccessful results fail; the established unsupported-host test may retain its exact container-specific skip. Package coverage uses the same method partition logic and authenticates these receipts alongside its coverage reports. Successful partitions retain their artifact attempts across failed-only reruns. Canonical Worker, Projects, and Gates receipts also retain their successful producer attempts; the final gate requires an exact receipt-to-attempt map from the workflow outputs and rejects mismatched or future attempts.

## Coverage collection and acceptance

For a complete finite run:

```text
docker compose run --rm -e SHARPPROOF_COVERAGE_COMPARISON_REF=origin/master tooling coverage
```

Choose the actual change base. CI resolves the PR base SHA, the preceding push SHA, or the required manual `comparison_ref` input to an exact commit. The normal command builds Release and writes a unique `artifacts/coverage/container-<id>` directory.

[coverage.yml](../.github/workflows/coverage.yml) collects thirteen shards on separate runners. `semantic-gates-corpus-a` runs the snapshot workflow class; `semantic-gates-corpus-b` runs its complete complement within the original corpus selection, including Corpus-category tests. `semantic-gates-shards` retains the three Gates remainder tasks. `semantic-worker-a` retains the first Worker half; `semantic-worker-b-2` and `semantic-worker-b-3` give each task of the other half its own runner. `semantic-analyzer` runs Analyzer tests alongside the existing semantic Fuzz campaign; `semantic-projects-rest` runs all other original project tasks; `semantic-extras` runs architecture checks and the isolated Attributes and Fuzz console collectors. `package-0` through `package-3` retain the four package method partitions. Each shard emits normalized Cobertura reports, source/binary authority, TRX receipts, and a report manifest. Successful artifact attempts remain available for failed-only reruns.

`coverage-gate` requires every phase in the [semantic coverage plan](../eng/scripts/SharpProof.SemanticCoveragePlan.ps1). It authenticates manifests, checks each complete task partition, and verifies the corpus case union against actual full NUnit discovery, including parameter cases and category exclusions. The gate independently rebuilds production source/PDB authority, keeps maximum hits within each authenticated production module, and enforces the unchanged floors and changed-TCB criteria. Identical class records may reuse an earlier validation only after their module and source have been authenticated in the current report; any changed record receives full line/PDB validation. Reuse preserves zero hits, overlapping credits, and distinct ownership of linked sources. No authority build, comparison, or coverage obligation is removed.

Coverage shards derive their test build projects from the same task plan and add every evaluated production project. Canonical PR jobs still build the full solution with analyzers. The merge streams XML directly into XmlDocument rather than first copying each whole report into a PowerShell string. Source/module authority, range and hit validation, all overlapping credits, direct zero-hit evidence, and MAX merging are unchanged. Exact immutable artifact IDs allow concurrent downloads while retaining successful producer attempts.

`-CoveragePhase` is valid only for `coverage-shard`. The legacy `semantic-gates-corpus`, `semantic-projects`, and `semantic-worker-b` selections remain available for standalone probes; they do not replace the thirteen required CI phases. The gate rejects preexisting merged authority and reports outside its exact phase set. Shard collection alone does not enforce final acceptance. Sources: [collection](../eng/scripts/Invoke-SharpProofCoverage.ps1), [shard evidence](../eng/scripts/SharpProof.CoverageShardEvidence.ps1), and [coverage validation](../eng/scripts/Test-SharpProofCoverage.ps1).

Inspect `SharpProof.coverage.json` with the reports and `artifacts/timings/*-coverage.json`. Preserve source commit, comparison SHA, configuration, binary/PDB identity, denominators, and individual shard evidence when comparing results. [Production inventory](../eng/scripts/Get-SharpProofProductionInventory.ps1) derives the coverage universe from solution projects and evaluated `SharpProofProductionProject`/`Compile` items. The acceptance contract classifies trusted pipeline projects/paths; the coverage baseline defines line floors and declaration-only coverage classification, with its project set checked against evaluated inventory. Changing collection layout does not authorize changing these authorities.

The changed-TCB percentage measures changed lines with an authenticated PDB span or direct collector record, including generated and zero-hit spans. `changedTcb.unmappedLines` separately lists changed lines without either measurement; these receive no execution credit. This reporting requires valid syntax and matching source, module, PDB, and collector evidence from every owning compilation. Missing evidence still fails the gate. The 100% floor applies to measurable lines; it does not establish execution or semantic correctness for unmapped lines.

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

`pack` exports the exact three-package graph to `artifacts/container-packages`; it requires clean exact-commit source. Run `package-consumers -Configuration Release -PackageSource artifacts/container-packages` and `samples` against those bytes. Consumer qualification also selects the pinned minimum SDK for framework consumers. The sample harness checks eight package-only projects, expected diagnostics, strict Proven claims, and mixed typed outcomes; see [samples](../src/samples/README.md).

[Release process](../eng/release/README.md) describes branch versus tag qualification, platform consumers, and feed routing. Keep the uploaded package artifact's source identity with consumer results. [Golden tests](../tests/golden/README.md) documents all four fixture stages and guarded original-workspace updates.

When inspecting CI, begin with the failing phase's summary, source SHA, requested scope, and artifact name. Then inspect bounded failures in the matching report and timings. Keep raw corpus observations, coverage counts, bounded fuzz campaign evidence, and exploratory findings distinct: they establish different claims and have different stopping conditions.
