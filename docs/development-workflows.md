# Development workflows and qualification evidence

Use the canonical Linux amd64 container for repository execution. Host commands below start at the repository root. Commands beginning with `sp`, `pwsh`, or `dotnet` run inside the persistent canonical development container; [container development](container-development.md) explains its workspace and the disposable finite task workspace.

## Choose a workflow

| Need | Entry point | Evidence and limits |
| --- | --- | --- |
| One regression | `tooling test -Target <project.csproj> -TestFilter <filter>` | Restores/builds the project, then runs its assembly with the VSTest filter |
| Changed project feedback | `tooling test-changed` or `tooling quick` | Selects impacted tests from Git changes; `quick` selects Debug and `-Fast` |
| Full development check | `tooling check` | Restore/build, semantic tests, package tests, and timing profile |
| Pull request qualification | `tooling pr` | Release contract/build and semantic/package phases with the canonical category filter |
| Portable/worker/package suite | `tooling portable-tests`, `worker-tests`, or `package-tests` | Selects the corresponding suite; package tests use their scheduler |
| Repository analysis and samples | `tooling self-apply` | Builds tracked source projects with advisory portable analysis, then runs the package sample matrix |
| Corpus qualification | `tooling corpus -Configuration Release` | Snapshot, metamorphic invariance, support classifications, Unknown ratchets, and replay |
| Coverage qualification | `tooling coverage` with a comparison ref | Release collection, source/PDB authority, coverage floors and changed trusted-code coverage |
| Clean package graph | `tooling pack -Configuration Release` | Exact-commit packages and package layout validation |
| Packed consumer behavior | `tooling package-consumers -PackageSource <feed>` | Real analyzer/verifier and minimum-SDK framework consumers |
| Retained and rotating fuzz | `tooling fuzz-nightly -Configuration Release` | Bounded clean-source campaign with validated per-run results |
| Scheduled nightly profile | `tooling nightly` | Runs the sequential nightly profile, including corpus and bounded fuzz; CI uploads all evidence |
| Exploratory generative campaign | `tooling test ... -TestFilter TestCategory=LongFuzz` | Explicit test harnesses; findings are recorded for review and CI checks error severity afterward |

The dispatcher [Invoke-SharpProofContainer.ps1](../scripts/Invoke-SharpProofContainer.ps1) owns accepted commands and parameters. [contract.json](../eng/acceptance/contract.json) owns trusted pipeline classifications and acceptance wall-time budgets; [baseline.json](../eng/coverage/baseline.json) owns numeric coverage floors. A successful selected test or feedback profile establishes only its selected scope.

## Parameters and test selection

The dispatcher defaults to `-Configuration Debug`, `-Target SharpProof.slnx`, no filter, and no supplied package feed. `pr` selects Release internally. `pr-gates` and `fuzz-nightly` require Release; coverage and packing build Release internally. `-Target` selects input for restore/build/test, rather than overriding specialized suites.

`-NoBuild` and `-Fast` are supported for `test`, `test-changed`, `semantic-tests`, `portable-tests`, `worker-tests`, and `package-tests`. `-NoBuild` requires existing outputs in the same workspace. `-Fast` disables build analyzers for development feedback; it does not remove generation or tests. `-ReuseTestHarness` is specific to `package-tests`. See [Invoke-SharpProofPackageTests.ps1](../scripts/Invoke-SharpProofPackageTests.ps1) for package harness preparation and scheduling.

Change selection includes tracked differences and nonignored untracked paths. The comparison order is `-ComparisonRef`, `SHARPPROOF_CHANGED_BASE_REF`, `HEAD` for a dirty checkout, then `HEAD^` for a clean checkout. Selection follows project references, imports, linked inputs, and shared build files; uncertain ownership expands to the full graph. A first commit without `HEAD^` needs an explicit comparison. The dispatcher exposes the environment variable; script-only options are used in the persistent container:

```text
pwsh -NoLogo -NoProfile -File scripts/Invoke-SharpProofChangedTests.ps1 -ComparisonRef origin/master -PlanOnly
pwsh -NoLogo -NoProfile -File scripts/Invoke-SharpProofDevCheck.ps1 -Configuration Release -PlanOnly
```

`-PlanOnly` prints selection or the developer command plan without running the selected builds/tests. These script parameters are not dispatcher parameters. Sources: [changed-test selection](../scripts/Invoke-SharpProofChangedTests.ps1), [developer check](../scripts/Invoke-SharpProofDevCheck.ps1), and [command plan](../scripts/Get-SharpProofDevCheckPlan.ps1).

## Canonical PR cohorts

The canonical filter is `TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus`. [ci.yml](../.github/workflows/ci.yml) splits this unchanged universe across two jobs:

- `canonical-main` runs `pr -SemanticCohort Remaining -TestResultsDirectory artifacts/canonical-remaining`, including package phases.
- `canonical-gates` runs `semantic-tests -Configuration Release -SemanticCohort Gates -TestResultsDirectory artifacts/canonical-gates` with the canonical filter.
- `build-and-test` requires both successful jobs, downloads their evidence into `artifacts/canonical-cohorts/remaining` and `gates`, then invokes `canonical-cohort-gate`.

`All` is the default cohort. `Remaining` and `Gates` require retained test results and the exact canonical filter. `pr` retains package phases and rejects Gates-only selection. `-SemanticCohort` and `-TestResultsDirectory` apply only to `pr`, `pr-gates`, and `semantic-tests`.

Reconciliation checks actual NUnit discovery, TRX execution identities/outcomes, source identity, and the CI run/attempt evidence. The six existing Explicit LongFuzz skips have specific allowed identities and reasons; additional skipped tests fail reconciliation. Two independently successful job labels alone do not replace this gate. Sources: [semantic scheduler](../scripts/Invoke-SharpProofSemanticTests.ps1) and [cohort evidence validator](../scripts/SharpProof.CanonicalCohortEvidence.ps1).

## Coverage collection and acceptance

For a complete finite run:

```text
docker compose run --rm -e SHARPPROOF_COVERAGE_COMPARISON_REF=origin/master tooling coverage
```

Choose the actual change base. CI resolves the PR base SHA, the preceding push SHA, or the required manual `comparison_ref` input to an exact commit. The normal command builds Release and writes a unique `artifacts/coverage/container-<id>` directory.

[coverage.yml](../.github/workflows/coverage.yml) instead collects `coverage-shard -CoveragePhase semantic` and `coverage-shard -CoveragePhase package` on separate runners. The semantic half includes the isolated Attributes run. Each half emits normalized Cobertura reports, `coverage-authority.json`, and report-manifest evidence under `artifacts/coverage/<phase>`. CI downloads both successful shards, then runs `coverage-gate` with its comparison ref. It preserves each successful shard's artifact attempt when only failed jobs are rerun.

`-CoveragePhase` is valid only for `coverage-shard`, and must select `semantic` or `package`. `coverage-gate` requires both downloaded phases and rejects preexisting merged authority or reports outside those phases. It checks the shard evidence, rebuilds production source/PDB authority, merges line hits using maximum hits, and enforces the same coverage criteria. The shard collection alone does not enforce final acceptance. Sources: [collection](../scripts/Invoke-SharpProofCoverage.ps1), [shard evidence](../scripts/SharpProof.CoverageShardEvidence.ps1), and [coverage validation](../scripts/Test-SharpProofCoverage.ps1).

Inspect `SharpProof.coverage.json` with the reports and `artifacts/timings/*-coverage.json`. Preserve source commit, comparison SHA, configuration, binary/PDB identity, denominators, and individual shard evidence when comparing results. [Production inventory](../scripts/Get-SharpProofProductionInventory.ps1) derives the coverage universe from solution projects and evaluated `SharpProofProductionProject`/`Compile` items. The acceptance contract classifies trusted pipeline projects/paths; the coverage baseline defines line floors and declaration-only coverage classification, with its project set checked against evaluated inventory. Changing collection layout does not authorize changing these authorities.

## Corpus and native effect oracles

`corpus` prints a structured result with case counts, support counts, Unknown reasons, replay counts, and failures. [The corpus guide](../SharpProof.Gates/README.md) describes the case universe and independent ratchets. Use `sp corpus-update -Configuration Release` only for an intentional observation refresh in a persistent workspace, then review the source changes. A finite task copy discards ordinary source edits.

The [Gates executable](../SharpProof.Gates/Program.cs) also exposes five developer oracle modes: `exception-oracle`, `allocation-oracle`, `purity-oracle`, `capability-oracle`, and `effectcontract-oracle`. These are executable subcommands rather than `tooling` commands. Inside the canonical container:

```text
dotnet run --project SharpProof.Gates/SharpProof.Gates.csproj -c Release -- exception-oracle --limit 5
```

Omit `--limit` or use zero for the full pinned method universe. Positive limits select method IDs in ordinal order and cannot exceed the universe size. A partial run is explicitly nonexhaustive. Reports include the universe hash/count, checked count, native outcomes/reasons, runtime observations, and reachable-source summary counts. [NativeEffectOracleGate.cs](../SharpProof.Gates/Corpus/NativeEffectOracleGate.cs) consumes the compiler artifact and independently checks applicable IL/runtime witnesses. Some purity/capability/lock cases explicitly report an oracle as not run; a successful report is not a runtime witness for every effect verdict. These reports do not publish worker proof authority.

## Bounded fuzz campaigns

[Invoke-SharpProofFuzzCampaign.ps1](../scripts/Invoke-SharpProofFuzzCampaign.ps1) combines a UTC-day rotating seed with [retained seeds](../eng/fuzz/retained-seeds.json). The current contract requests 10,000 rotating cases; the retained manifest requests 1,000 per seed. The total campaign cap is 1,000,000. The runner is rebuilt from clean exact-commit source, and source identity is checked again after rebuilding and after the campaign.

`tooling fuzz-nightly -Configuration Release` writes `artifacts/fuzz/nightly`. The script's advanced options are `-OutputDirectory`, `-RotatingSeed`, `-RotatingCases`, and `-RetainedCases`; zero case overrides use the configured budgets. Run advanced invocations in the persistent container. Output ownership is leased so overlapping writers cannot replace the same campaign. Each rotating/retained run produces `.stdout.json` and `.stderr.txt`; validated results feed `campaign.json`, including requested/observed counts, seeds, commit, and pass status. Missing, malformed, incomplete, or mismatched runner evidence fails the campaign.

For a bounded individual replay, the fuzz executable accepts `--cases` (default 1,000, range 1..1,000,000), `--seed` (signed 32-bit integer, default `0x5A17`), and `--max-parallelism` (default 4, range 1..4):

```text
dotnet run --project Tools/SharpProof.Fuzz/SharpProof.Fuzz.csproj -c Release -- --cases 1000 --seed 23063 --max-parallelism 4
```

Sources: [FuzzOptions.cs](../Tools/SharpProof.Fuzz/FuzzOptions.cs), [FuzzRunner.cs](../Tools/SharpProof.Fuzz/FuzzRunner.cs), and [evidence lifecycle](../scripts/SharpProof.FuzzEvidenceLifecycle.ps1). A standalone replay does not produce a qualified clean-source campaign envelope. [nightly.yml](../.github/workflows/nightly.yml) schedules the full nightly profile and uploads its artifacts even on failure.

## LongFuzz exploration and finding review

The six Explicit harnesses and their feature families are described in [container development](container-development.md#long-running-fuzz-campaign). They share the process's wall-clock budget; remaining time is apportioned among harnesses. Environment defaults in [LongFuzzSession.cs](../SharpProof.Worker.Test/LongFuzz/LongFuzzSession.cs) are:

| Variable | Default | Meaning |
| --- | --- | --- |
| `SHARPPROOF_LONGFUZZ_MINUTES` | `5` | Whole selected run's time budget |
| `SHARPPROOF_LONGFUZZ_SEED_START` | `1` | First generated case seed |
| `SHARPPROOF_LONGFUZZ_SEED_END` | `int.MaxValue` | Exclusive seed limit |
| `SHARPPROOF_LONGFUZZ_OUTPUT` | `artifacts/long-fuzz` | Evidence directory relative to repository root, or absolute path |

Replay needs the harness identity and seed together. `hits.jsonl` records claim, worker outcome/reason, CLR observation, details, and full source; `hits/*.cs` preserves standalone sources with a header. `stats-<harness>.json` refreshes every 50 cases and at completion. `events.log` records live annotations. Sources can receive numeric suffixes when multiple findings share a harness/seed/kind.

The NUnit harnesses record findings and continue. [long-fuzz.yml](../.github/workflows/long-fuzz.yml) adds a separate error-severity check after artifact upload. It runs nightly or manually with six shards, disjoint 250,000-seed ranges, a default 300-minute budget per shard, manual range 1..320 minutes, and a 350-minute job timeout. It is advisory and does not run on PRs. Review errors and warnings separately; an unobserved refutation on a finite CLR input grid does not by itself establish an incorrect refutation. Turn a confirmed finding into a focused deterministic regression before editing expectations.

## Packed qualification and evidence inspection

`pack` exports the exact three-package graph to `artifacts/container-packages`; it requires clean exact-commit source. Run `package-consumers -Configuration Release -PackageSource artifacts/container-packages` and `samples` against those bytes. Consumer qualification also selects the pinned minimum SDK for framework consumers. The sample harness checks eight package-only projects, expected diagnostics, strict Proven claims, and mixed typed outcomes; see [samples](../samples/README.md).

[Release process](../eng/release/README.md) describes branch versus tag qualification, platform consumers, and feed routing. Keep the uploaded package artifact's source identity with consumer results. [Golden tests](../tests/golden/README.md) documents all four fixture stages and guarded original-workspace updates.

When inspecting CI, begin with the failing phase's summary, source SHA, requested scope, and artifact name. Then inspect bounded failures in the matching report and timings. Keep raw corpus observations, coverage counts, bounded fuzz campaign evidence, and exploratory findings distinct: they establish different claims and have different stopping conditions.
