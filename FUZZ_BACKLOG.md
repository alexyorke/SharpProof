# Fuzz backlog

## Next high-value items

- Add negative exception families where `PS0010` must not emit:
  - caught internal throw
  - dead-branch throw
  - guarded divide-by-zero excluded by path
  - guarded null dereference excluded by path
- Add positive exception families beyond direct throw:
  - definite divide-by-zero
  - definite null dereference
  - `using` / `Dispose()` exception propagation
  - nested local-function and lambda throw propagation
- Add direct-shape generators only for shapes that prove they emit in Roslyn:
  - `InlineArrayAccess`
- Keep initializer-shape work scoped to cases that can prove a direct emit before promotion:
  - `MemberInitializer`
  - `PropertyInitializer`
- Keep reclassifying lowered/internal operation kinds out of the "worth generating" bucket:
  - `FlowCapture`
  - `FlowCaptureReference`
  - `FlowAnonymousFunction`
  - `MethodBody`
  - `ConstructorBody`
  - `Branch`
  - `Loop`
  - `Labeled`
- Add a small aggregation tool for overnight runs:
  - merge multiple phase `summary.json` / `coverage.json`
  - report total cases, findings, throughput, and remaining unobserved operation kinds
- Rerun a full 4-phase overnight fuzz pass after the next generator tranche lands.
