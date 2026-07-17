# Machine-Readable Explain Reports

`explain` can compose the proof surfaces for one source point into a bounded
JSON, SARIF 2.1.0, or Markdown report. These formats are intended for IDEs, CI
bots, and issue attachments that need more context than a focused query result.

```powershell
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42 --json
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42 --sarif
dotnet run --project .\Tools\SharpProof.SymbolicCli\SharpProof.SymbolicCli.csproj -- explain --file Example.cs --line 42 --markdown
```

The three output selectors are mutually exclusive. Without one, `explain`
keeps its concise text output.

## Composed JSON Schema

`explain --json` emits one lower-camel JSON document with `kind: "explain"`
and `schemaVersion: 1`. It also carries the shared
`evidenceSchemaVersion` and `evidenceSchemaCompatibility` fields.

The document contains:

- `source`: virtual or physical file identity, source-input kind, and optional
  snippet source-map metadata
- `target`: requested line/column or position plus the resolved program point,
  node span, method, and program-point kind
- `project`: project/solution identity, analyzer configs, AdditionalFiles,
  baseline/effect-summary counts, workspace diagnostics, and configuration
  issues when MSBuild context was loaded
- `invariant`: the canonical point-query view, including
  invariant status, reachability, proof outcomes, SMT diagnostics, and bounded
  facts and conditions
- `runtimeHazards`: a bounded runtime-hazard view for the
  resolved source line
- `capabilities`: the containing method's capability summary plus bounded
  unknown reasons and sites
- `complexity`: the containing method's complexity summary plus bounded
  reasons, drivers, and callee summaries
- `diagnostics`: relevant project analyzer diagnostics, with target-first
  ordering and evidence links
- `crossLinks`: JSON-pointer relationships among the target, diagnostics, and
  symbolic evidence sections
- `truncation`: one aggregate view of output bounds and analysis-limit events

Diagnostic cross-links are conservative mappings. Capability diagnostics such
as `SP0015`-`SP0017` link to `#/capabilities`, complexity diagnostics such as
`SP0021`-`SP0023` link to `#/complexity`, and runtime-hazard/exception
diagnostics link to `#/runtimeHazards`. Other diagnostics link to the invariant
section; `SP0002` links to both invariant and capability evidence.

Standalone inputs do not run the analyzer, so their `diagnostics` section has
zero counts. Add `--project` or `--solution` to include diagnostics after the
same analyzer configuration, baseline suppression, and AdditionalFiles used by
the build. For a position target, runtime hazards are queried on the resolved
program point's line.

## Output Bounds

Machine-readable reports default to 50 items per bounded collection:

| Option | Bounded content |
| --- | --- |
| `--report-max-diagnostics <n>` | Analyzer diagnostic items |
| `--report-max-hazards <n>` | Runtime-hazard items |
| `--report-max-items <n>` | Invariant facts/conditions/proofs, capability sites/reasons, complexity drivers/reasons/callees, and project metadata/messages |

All limits accept zero. Untruncated totals remain in their owning sections.
Each section reports its own truncation flags, while root `truncation` tells a
consumer whether any report projection or underlying bounded analysis was
truncated. Increase output limits only when the attachment or consumer can
accept the additional data. Analysis-limit truncation is separate and must be
addressed through the bounded-analysis settings described in
[bounded analysis limits](analysis-limits.md).

## SARIF 2.1.0

`explain --sarif` emits a SARIF 2.1.0 log with the standard schema URL and one
SharpProof run. Results include:

- relevant Roslyn analyzer diagnostics with their original IDs and severities
- bounded runtime hazards with stable `SPQ-HZ-*` rule IDs
- synthetic warnings when invariant, capability, or complexity evidence is
  unresolved
- `SPQ-REPORT-TRUNCATED` when report or analysis bounds affected the result

Locations use SARIF physical locations, and each result has
`properties.crossLinks` pointing to the corresponding section in the composed
JSON model. Run properties retain schema versions, report truncation, and the
complete bounded cross-link list. This lets a CI bot upload SARIF while keeping
enough identifiers to request or attach the richer JSON report.

## Markdown

`explain --markdown` renders the same composed result as an issue-ready report.
It includes summary metadata, invariant/reachability status, bounded hazard,
capability, complexity, and diagnostic tables, cross-links, and an explicit
truncation notice. Markdown is a presentation format; use JSON when a consumer
needs stable field names or exact counts.

## JSON Request Envelope

The strict schema-versioned request envelope supports all explain formats and
limits without a temporary source file:

```json
{
  "schemaVersion": 1,
  "mode": "explain",
  "source": {
    "text": "class C { static void M() { throw new System.Exception(); } }",
    "filePath": "virtual/Buffer.cs"
  },
  "target": {
    "kind": "point",
    "line": 1,
    "column": 35
  },
  "output": {
    "format": "sarif",
    "maxDiagnostics": 25,
    "maxHazards": 25,
    "maxItems": 25
  }
}
```

For explain reports, `maxHazards`, `maxDiagnostics`, and `maxItems` bound only
the composed report sections. Focused query JSON is not output-truncated.

Successful explain reports return exit code 0 even when they contain analyzer
diagnostics or hazards. CI exit gates require a focused query mode and cannot
be combined with `explain`. Failed JSON or SARIF-oriented requests emit the
typed `kind: "error"` JSON envelope instead of a partial report. See the
[typed query error model](error-model.md).
