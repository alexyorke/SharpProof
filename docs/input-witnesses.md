# Solver Witnesses And Input Domains

SharpProof query results can explain which inputs make a program point or
runtime hazard feasible. The public API exposes both a concrete solver model
and a conservative summary of the constraints around that model.

These are different claims:

- A satisfying assignment is one example accepted by the bounded solver. It is
  not the complete set of inputs.
- An input domain summarizes supported path constraints such as integer bounds,
  nullness, string predicates, collection lengths, and index relationships.
  It can be exact, approximate, or unsupported independently of the model.

## Query Surfaces

The full public result graph exposes witnesses at every requested scope:

- `SymbolicProgramPointResult.ReachabilityWitness` describes one input model
  that reaches a program point. `InputDomainSummary` is its domain view.
- `SymbolicQueryResult` exposes `ReachabilityWitnesses` plus a conservatively
  merged `InputDomainSummary` for point, line, span, and file scopes.
- `SymbolicConditionProofResult.Witness` demonstrates the reported implication
  outcome when a model is available. For an unknown implication,
  `CounterexampleWitness` exposes a model satisfying the path and the negated
  condition when the solver found one.
- `SymbolicRuntimeHazard.TriggerWitness` is computed from `path && trigger`, so
  its assignment reaches the candidate and satisfies the hazard condition.
  `SymbolicRuntimeHazardQueryResult` exposes all trigger witnesses and their
  merged domain summary.

The CLI's `--json` output serializes these same public DTOs, including model
assignments and input domains when available.

## Status And Precision

`SymbolicWitnessStatus` is present on witnesses, assignments, domains, and
individual domain predicates:

- `Exact` means the value or supported constraint is represented exactly by
  the current bounded model.
- `Approximate` means the result is useful but intentionally conservative. This
  includes opaque non-null reference identities, regex predicates whose .NET
  semantics may be approximated, disjunctive path summaries, and domains merged
  across alternative program points.
- `Unsupported` means SharpProof could not produce the requested model or
  domain shape. `Reason` identifies the boundary.
- `None` means no satisfying witness applies, normally because the path or
  trigger is unsatisfiable.

Consumers must not reinterpret `Approximate` or `Unsupported` as an exact input
contract. A witness attached to an `Unknown` proof is evidence of satisfiability
for the represented candidate constraints, not a proof that all source/runtime
semantics were modeled.

## Assignments

Each `SymbolicSatisfyingAssignment` includes:

- the internal `SymbolicName` and source-oriented `SourceName`
- `Role`: parameter, local, receiver, receiver state, derived, or unknown
- `ValueKind`: boolean, integer, reference, string, or unknown
- a stable display `Value` and the applicable typed property:
  `BooleanValue`, `IntegerValue`, `StringValue`, or `IsNull`
- its precision `Status` and `Reason`

Non-null reference values are opaque solver identities. Their `IsNull = false`
fact is useful, but their display value is marked approximate and must not be
treated as a constructible object graph.

## Domain Summaries

`SymbolicInputDomain` groups related solver variables under a source input. For
example, a string reference, its `.String` content variable, and a string-length
term are reported as one string domain. A collection reference and its
`.Length` or `.Count` variable are reported as one collection domain.

Supported fields include:

- `IntegerRange`, with inclusive/exclusive minimum and maximum bounds
- `Nullness`
- `ExactString` and `StringLengthRange`
- required prefixes, suffixes, and substrings
- regex patterns, explicitly marked approximate
- `CollectionLengthRange`
- `IsIndex` and `RelatedCollection`
- normalized `Predicates` with per-predicate precision and reasons

Point domains represent a conjunction of the supported path constraints.
Aggregate line/span/file domains represent alternative paths. Their range is a
conservative envelope, nullness or exact content is retained only when common,
and required string predicates are intersected. The aggregate is marked
`Approximate` with `AlternativeCount` so a consumer cannot mistake the union for
a single path contract.

## API Example

```csharp
using SharpProof.Symbolic;
using var session = SharpProofAnalysisSession.FromText(
    source,
    "Example.cs",
    new SharpProofAnalysisOptions(enableSmt: true));
var response = session.Analyze(
    SharpProofQuery.Reachability(SymbolicQueryTarget.Line(42)));
var result = ((SourceQueryPayload)response.Payload!).Value;

foreach (var witness in result.ReachabilityWitnesses)
{
    foreach (var assignment in witness.Assignments)
        Console.WriteLine($"{assignment.SourceName} = {assignment.Value}");
}

foreach (var domain in result.InputDomainSummary.Domains)
    Console.WriteLine($"{domain.Name}: {domain.Status} ({domain.Reason})");
```

For a divide-by-zero candidate, inspect
`hazard.TriggerWitness.Assignments` and select the `divisor` assignment. A
satisfying trigger witness will report integer value `0`; an unreachable
candidate reports `None` instead of inventing an input.
