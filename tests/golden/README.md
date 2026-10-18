# Golden tests

Each fixture pairs a C# input with a stable `.expected` output in `lowering`, `worker`, `vc`, or `analyzer`. Discovery rejects orphan expectations; normal runs reject missing expectations. Files use UTF-8 without BOM and LF. [GoldenTest.cs](../../eng/testing/GoldenTest.cs) owns discovery, comparison, and guarded updates.

## Run a stage

From the repository root:

```text
docker compose run --rm tooling test -Target src/SharpProof.Frontend.Tests/SharpProof.Frontend.Tests.csproj -TestFilter FullyQualifiedName~GoldenLoweringTests
docker compose run --rm tooling test -Target src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj -TestFilter FullyQualifiedName~GoldenWorkerTests

docker compose run --rm tooling test -Target src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj -TestFilter FullyQualifiedName~GoldenVcRegressionTests
docker compose run --rm tooling test -Target src/SharpProof.Analyzer.Tests/SharpProof.Analyzer.Tests.csproj -TestFilter FullyQualifiedName~GoldenAnalyzerTests
```

Use a unique Compose project name when independent worktrees share a directory basename.

## Stage outputs

Lowering fixtures record typed program structure, mode, types, variables, blocks, instructions, terminators, source evidence, and abstentions. The current worker uses Total IR; fixtures can deliberately exercise narrower or unsupported lowering cases.

Worker fixtures record request binding, run/claim outcomes, reasons, cache behavior, and launcher boundaries. A `golden-scenario` directive selects a test driver where needed. Replay fixtures can construct explicit IR to exercise a boundary independently of source lowering.

Analyzer fixtures record code, effective severity, mapped location, and message. Formatters omit timings, absolute temporary paths, and arbitrary solver model choices. Assertions should preserve meaningful evidence rather than incidental output.

The `vc` fixtures preserve historical verification regressions through the current compiler artifact and worker pipeline. They record outcome, reason, vacuity, proof core, and assumptions. A leading `// vc-depth-limit: 1` or `// vc-depth-limit: 3` selects the corresponding expression-depth boundary; the test checks the required Unknown reason as well as the snapshot. See [GoldenVcRegressionTests.cs](../../src/SharpProof.Worker.Tests/GoldenVcRegressionTests.cs).

## Updating expectations

Only an intentional reviewed behavior change justifies a refresh. Do not change a frozen expectation merely because a new implementation fails it.

Pass `-e SHARPPROOF_UPDATE_GOLDEN=1` before `tooling` in a stage command:

```text
docker compose run --rm -e SHARPPROOF_UPDATE_GOLDEN=1 tooling test -Target src/SharpProof.Frontend.Tests/SharpProof.Frontend.Tests.csproj -TestFilter FullyQualifiedName~GoldenLoweringTests
```

Only the exact value 1 enables updates. The entrypoint captures the original source root before creating its private task copy. Update code checks the input and old expectation against that root and atomically writes only the paired original expectation. Changed inputs, symlinks, and out-of-root targets fail.

The loop service's original source is read-only, so writes there fail. Rebuild tooling after entrypoint changes. Review every expectation diff and rerun normally afterward.
