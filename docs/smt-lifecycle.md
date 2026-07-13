# SMT Lifecycle And Health

SharpProof reuses one Z3-backed proof-search context per managed thread. Reuse
avoids repeatedly loading native state in analyzer hosts, while the proof-result
cache remains separate from those contexts. Lifecycle controls make recovery
and cleanup explicit without discarding valid cached conclusions.

The NuGet packages bundle native Z3 only for Windows x64 and macOS x64. Linux,
arm64, and other unsupported package/RID combinations use the permanent
conservative fallback unless the host supplies a compatible native library.
See [native SMT packaging and platform support](native-smt-packaging.md).

## Default Recovery

`SmtSolverLifecycleOptions.Default` uses these settings:

| Setting | Default | Meaning |
| --- | ---: | --- |
| `MaxTransientRetries` | 1 | Retry a logical proof once after a transient Z3 failure. |
| `RecycleContextOnTransientFailure` | `true` | Dispose the failed current-thread context before retrying. |
| `DisposeCurrentThreadContextOnServiceDispose` | `false` | Preserve the shared thread context when a short-lived service is disposed. |

A Z3 exception reported by the solver is transient. SharpProof records
`smt_transient_failure`, recycles the context by default, and retries within the
configured count. A successful retry returns the proof and records recovery in
health telemetry. An exhausted transient failure stays unknown and is not put
in the local or shared result cache, so a later request can recover.

Native library absence, incompatible native binaries, unsupported platforms,
and context initialization failures are treated as permanent for that
`SmtAnalysisService`. Further requests return `smt_unavailable` without trying
to recreate the context. `IsPermanentlyUnavailable` and `Health` expose this
state directly.

Timeouts, resource budgets, and formula-encoding failures remain conservative
query outcomes. They do not mark the solver service permanently unavailable.

## Health Snapshot

`SmtAnalysisService.Health` returns an immutable `SmtAnalysisHealth` snapshot:

- `State`: `Disabled`, `Ready`, `Degraded`, `PermanentlyUnavailable`, or
  `Disposed`
- `LastFailureCode`
- consecutive transient failures
- transient retry and recovered-failure counts
- context recycle count and global context generation
- `IsAvailable` and `IsPermanentlyUnavailable` convenience flags

The same snapshot and the active `SmtSolverLifecycleOptions` are included in
`SymbolicSmtDiagnostics`, `SymbolicCompactSmtDiagnostics`, and compact
runtime-hazard SMT diagnostics. Full and compact JSON therefore expose health
without requiring direct access to the service instance.

## Explicit Recycling

Use the service methods when a host reaches an intentional maintenance point:

```csharp
using var smt = new SmtAnalysisService(
    SmtAnalysisOptions.Default.WithLifecycle(
        new SmtSolverLifecycleOptions(maxTransientRetries: 2)));

var current = smt.RecycleCurrentThreadSolverContext();
var global = smt.RequestGlobalSolverContextRecycle();
```

`RecycleCurrentThreadSolverContext()` immediately disposes a context on the
calling thread when one exists. `RequestGlobalSolverContextRecycle()` advances
a process-wide generation and disposes the calling thread's context. Contexts
on other threads are disposed lazily the next time those threads use the
solver; a thread-static object cannot safely be synchronously disposed from a
different thread.

Both methods return `SmtSolverContextRecycleResult`, including the scope,
whether the current context was disposed, the requested generation, and local
and shared cache counts. Neither operation clears proof-result caches. Cached
proven results remain reusable after context recycling.

Service disposal preserves the historical reuse behavior by default. Set
`DisposeCurrentThreadContextOnServiceDispose` when a host knows the service is
ending on the same thread that owns the context. For process-wide or worker-pool
maintenance, prefer the generation-based global recycle request.

## Analyzer Configuration

The analyzer accepts compilation-global lifecycle options:

```ini
is_global = true

sharpproof_smt_transient_retry_count = 2
sharpproof_smt_recycle_context_on_transient_failure = true
sharpproof_smt_dispose_thread_context_on_service_dispose = false
```

The retry count must be non-negative. Invalid values produce `SP0025` and leave
the documented default active.

## CLI

The symbolic CLI exposes matching controls:

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- --file Example.cs --line 42 --check-reachability `
  --smt-transient-retries 2 `
  --smt-dispose-context-on-exit `
  --compact-json
```

Use `--smt-keep-context-on-transient-failure` only for diagnostics or a host
that supplies its own context recovery; the default recycle-before-retry path
is safer. Text SMT diagnostics print health and lifecycle counters. JSON emits
nested `smtDiagnostics.health` and `smtDiagnostics.lifecycle` objects.
