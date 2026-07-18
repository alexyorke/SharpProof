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
    new SharpProofAnalysisOptions(enableSmt: true));

var invariant = session.Analyze(
    SharpProofQuery.Invariant(SharpProofTarget.Point(42, 1)));
var proof = session.Analyze(
    SharpProofQuery.Condition(SharpProofTarget.Point(42, 1), "value >= 0"));
var hazards = session.Analyze(
    SharpProofQuery.RuntimeHazards(SharpProofTarget.LineNumber(42)));
var capabilities = session.Analyze(
    SharpProofQuery.Capabilities(SharpProofTarget.LineNumber(42)));
var complexity = session.Analyze(
    SharpProofQuery.Complexity(SharpProofTarget.LineNumber(42)));
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
