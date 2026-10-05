# Golden tests

Each case is a C# source file paired with a `.expected` file in `lowering`,
`worker`, or `analyzer`. The stage runner formats stable results and compares
them byte for byte. Files use UTF-8 without BOM and LF line endings. Discovery
rejects orphan expectations; normal runs reject missing expectations.

Run a stage with the canonical container:

```powershell
$env:COMPOSE_PROJECT_NAME = 'sharpproof-phase2-core-ir'
docker compose run --rm tooling test -Target SharpProof.Frontend.Test/SharpProof.Frontend.Test.csproj -TestFilter 'FullyQualifiedName~GoldenLoweringTests'
docker compose run --rm tooling test -Target SharpProof.Worker.Test/SharpProof.Worker.Test.csproj -TestFilter 'FullyQualifiedName~GoldenWorkerTests'
docker compose run --rm tooling test -Target SharpProof.Analyzer.Test/SharpProof.Analyzer.Test.csproj -TestFilter 'FullyQualifiedName~GoldenAnalyzerTests'
```

## Updating expectations

Pass `-e SHARPPROOF_UPDATE_GOLDEN=1` explicitly before `tooling` in the same
command. Other values do not enable updates. Review the resulting diff.

The container entrypoint captures the original repository before making its
private test copy. Updates compare the source and previous expectation with
that original, then atomically replace only the paired original `.expected`
file. A missing expectation can be created in update mode. Changed inputs,
symbolic links and paths outside the source root fail. The loop service mounts
its original source read-only, so an update that needs a write fails clearly.
Rebuild the tooling image after changing the entrypoint.

## Stage outputs

Lowering cases show whole-body IR: mode, classification, source variables,
types, blocks, instructions, terminators and abstentions. Legacy lowering stays
authoritative while the typed candidate pipeline is under development; an
unsupported `ulong` case therefore records that abstention.

Worker cases show request binding, run and claim outcomes, reasons, cache
eligibility and selected launcher results. The first-line `golden-scenario`
directive selects a small test driver. Replay scenarios use the actual kernel
boundary with explicit Total IR until candidate source lowering can express
that boundary. They preserve separate cases for contract reads of approximation
values, unbound spec results and entry-bound input replay. The native cancellation
case verifies that completed solver work remains charged after cancellation,
without fixing a runtime-dependent resource counter in the expectation.

Analyzer cases show diagnostic code, severity, mapped location and message.
Stage formatters omit timings, absolute temporary paths and arbitrary solver
models. New bug fixes add a case in the relevant stage. The later migration of
the 72 Effects regression fixtures must preserve their metadata references,
options, selection and internal assertions before retiring the original tests.
