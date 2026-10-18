# Measure consumer feedback

Measure the complete consumer workflow against the same source and environment without SharpProof analysis. More workers or a faster solver phase alone does not establish a better experience. Keep selected coverage and proof quality alongside timings.

The repository provides [Measure-SharpProofConsumerPerformance.ps1](../eng/scripts/Measure-SharpProofConsumerPerformance.ps1). Inside the canonical container after producing matching packages:

```text
pwsh -NoLogo -NoProfile -File eng/scripts/Measure-SharpProofConsumerPerformance.ps1 -PackageSource artifacts/container-packages -Iterations 3 -OutputDirectory artifacts/consumer-performance
```

The synthetic consumer compares Attributes-only, advisory portable analysis, and strict verification on the same eight-method workload. It records first build, no-op, warm rebuild, and small-edit timings, keeping restore separate and rotating variant order. The JSON records package hashes, SDK/platform, and selected proof quality. Treat synthetic results as a reproducible signal, not a guarantee for every application's size or language features.

Report schema version 2 also records the benchmark checkout commit, whether tracked files were modified, the harness SHA-256, package version, and repository commits embedded in the packages. The benchmark checkout and package commits are separate identities: measuring previously packed artifacts does not imply they were built from the current checkout. Keep the harness hash when comparing reports from modified checkouts; the dirty flag alone does not identify those edits. Console differences use signed milliseconds relative to the Attributes-only baseline, so a negative difference means the measured variant was faster in that run.

`-MaximumBuildMilliseconds` opts into an explicit absolute gate. Set a threshold only after measuring an appropriate environment; no universal threshold is implied. Keep local logs private and review them before sharing. For a meaningful regression report retain iteration counts, source/package identity, machine/container limits, cache state, command, timing units and claim outcomes.

Also validate representative real applications and interactive cancellation/editor responsiveness. The harness measures build feedback; it does not certify IDE latency or cancellation behavior. Retest changed or failing paths rather than repeatedly running an unchanged broad suite. Use the [performance issue form](../.github/ISSUE_TEMPLATE/performance.yml) for a sanitized, comparable reproduction.
