# Measure consumer feedback

Measure the complete consumer workflow against the same source and environment without SharpProof analysis. More workers or a faster solver phase alone does not establish a better experience. Keep selected coverage and proof quality alongside timings.

The repository provides [Measure-SharpProofConsumerPerformance.ps1](../eng/scripts/Measure-SharpProofConsumerPerformance.ps1). Inside the canonical container after producing matching packages:

```text
pwsh -NoLogo -NoProfile -File eng/scripts/Measure-SharpProofConsumerPerformance.ps1 -PackageSource artifacts/container-packages -Iterations 3 -OutputDirectory artifacts/consumer-performance
```

The synthetic consumer compares Attributes-only, advisory portable analysis, and strict verification on the same eight-method workload. It records first build, no-op, warm rebuild, and small-edit timings, keeping restore separate and rotating variant order. The JSON records package hashes, SDK/platform, and selected proof quality. Treat synthetic results as a reproducible signal, not a guarantee for every application's size or language features.

Report schema version 3 records the benchmark checkout commit, whether tracked files were modified, the harness SHA-256, package version, and repository commits embedded in the packages. The benchmark checkout and package commits are separate identities: measuring previously packed artifacts does not imply they were built from the current checkout. Keep the harness hash when comparing reports from modified checkouts; the dirty flag alone does not identify those edits. Console differences use signed milliseconds relative to the Attributes-only baseline, so a negative difference means the measured variant was faster in that run.

`-MaximumBuildMilliseconds` opts into an explicit absolute gate. Set a threshold only after measuring an appropriate environment; no universal threshold is implied. Keep local logs private and review them before sharing. For a meaningful regression report retain iteration counts, source/package identity, machine/container limits, cache state, command, timing units and claim outcomes.

Also validate representative real applications and interactive cancellation/editor responsiveness. The harness measures build feedback; it does not certify IDE latency or cancellation behavior. Retest changed or failing paths rather than repeatedly running an unchanged broad suite. Use the [performance issue form](../.github/ISSUE_TEMPLATE/performance.yml) for a sanitized, comparable reproduction.

## Scaled resource measurements

The resource workload builds and runs the same eight allocation kernels with the
Attributes-only baseline, advisory analyzer and strict verifier. Every verified
build must preserve eight nonvacuous resource proofs with complete coverage.
Input sizes are distinct integers from 0 through 1000; zero measures fixed entry
allocation. The small edit doubles each loop allocation's element count, and the
runtime samples execute that final source in every variant.

```text
pwsh -NoLogo -NoProfile -File eng/scripts/Measure-SharpProofConsumerPerformance.ps1 -PackageSource artifacts/container-packages -Workload resources -InputSizes "0,16,128,1000" -RuntimeRepetitions 100 -Iterations 3 -OutputDirectory artifacts/resource-performance
```

Each process performs ten warmups per input, then repeats all eight kernels the
requested number of times. The report records raw stopwatch ticks and frequency,
cumulative bytes allocated by the current thread, repetition count, input size
and an independently checked output checksum. Kernel intervals exclude report
construction and serialization. Process milliseconds include startup, warmups and
report writing. Allocations describe total bytes over the measured repetitions;
divide by repetitions for a per-entry observation. This is neither peak live nor
retained memory.

Runtime and subject source hashes, actual framework/architecture, OS, GC mode and
the tiered compilation environment setting accompany the samples. Each variant
must match its baseline source, environment and output. Per-input comparisons
retain absolute values and signed millisecond/byte differences from that
iteration's baseline. Restore and the four build scenarios remain separate.
Each run uses its own compiler server identity, shared by its variants, so
concurrent runs cannot reuse analyzer loading state from a deleted package cache.
Variant order rotates between iterations. Keep raw samples and use repeated runs
when interpreting small timing differences; no timing threshold is implicit.

These are measurements of admitted synchronous kernels. They do not prove Big-O,
tightness, average-case or amortized bounds, and do not validate concurrency,
cancellation or exceptional completion. Such proof obligations remain separate
from matching the kernel's observed result and selected resource claims.
