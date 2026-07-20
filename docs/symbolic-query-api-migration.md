# Symbolic query preview API migration

The preview .NET API now uses one session, one discriminated query model, and
one result envelope. `SymbolicQueryService`, `SymbolicQueryContext`, and the
old `Symbolic*` result DTO graph are no
longer public.

Create a session for the compilation or source lifetime, then analyze typed
queries:

```csharp
using var session = SharpProofAnalysisSession.FromText(
    sourceText,
    "Example.cs",
    new SharpProofAnalysisOptions(EnableSmt: true));

var invariant = session.Analyze(
    new SharpProofQuery(
        SharpProofQueryKind.Invariant,
        new SharpProofTarget(SharpProofTargetKind.Point, Line: 42, Column: 1)));
var proof = session.Analyze(
    new SharpProofQuery(
        SharpProofQueryKind.Condition,
        new SharpProofTarget(SharpProofTargetKind.Point, Line: 42, Column: 1),
        Condition: "value >= 0"));
var hazards = session.Analyze(
    new SharpProofQuery(
        SharpProofQueryKind.RuntimeHazards,
        new SharpProofTarget(SharpProofTargetKind.Line, Line: 42)));
var capabilities = session.Analyze(
    new SharpProofQuery(
        SharpProofQueryKind.Capabilities,
        new SharpProofTarget(SharpProofTargetKind.Line, Line: 42)));
var complexity = session.Analyze(
    new SharpProofQuery(
        SharpProofQueryKind.Complexity,
        new SharpProofTarget(SharpProofTargetKind.Line, Line: 42)));
```

`SharpProofQueryResult` supplies common status, source location, structured
unknown reasons, budget/truncation metadata, evidence, and a typed payload.
Use `SourceQueryPayload`, `ConditionQueryPayload`,
`RuntimeHazardQueryPayload`, `CapabilityQueryPayload`, or
`ComplexityQueryPayload` to access domain details. Failed and canceled queries
carry a stable `SharpProofError` and no payload.

Equivalent queries share a session-scoped, thread-safe result cache. Canceled
queries are not cached, and disposing the session releases its owned SMT
service and cached results.

The CLI now consumes this API internally while retaining its command names,
exit codes, text, JSON, SARIF, Markdown, and evidence schemas. The canonical
domain payload types remain available during the preview migration; raw Roslyn
source inputs and service-specific execution APIs are compatibility internals.
