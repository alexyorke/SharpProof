# Bounded analysis limits

SharpProof bounds fact collection, state merging, and solver work so analysis
cannot grow without limit. Exceeded bounds record stable truncation evidence and
produce `Unknown`; retained facts are never upgraded into an optimistic proof.

`SharpProofAnalysisBudget` controls CFG/state limits such as merged branch facts,
finite-foreach facts, structural null depth, path conditions, and fact-choice
combinations. All values must be positive. Analyzer builds expose matching
compilation-global `sharpproof_analysis_*` options; invalid values report
`SP0025` while the default remains active.

```csharp
var budget = new SharpProofAnalysisBudget(
    MaxFiniteForeachElementFacts: 16,
    MaxMergedPathConditions: 64);

using var session = SharpProofAnalysisSession.FromText(
    sourceText,
    "Example.cs",
    new SharpProofAnalysisOptions(budget));
var result = session.Analyze(new SharpProofAnalysisRequest(
    target,
    SharpProofAnalysisFacet.ProofFacts));

foreach (var item in result.Truncations)
    Console.WriteLine($"{item.Code}: {item.Observed} > {item.Limit}");
```

Each truncation includes its stable code, retained limit, observed count,
provenance, and optional source span. Z3 timeout, expression-node,
path-condition, and method budgets remain separate solver limits and surface as
unified unknown reasons.
