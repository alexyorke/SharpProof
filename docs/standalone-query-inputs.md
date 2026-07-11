# Standalone Query Inputs

Use standalone inputs for editor buffers, generated snippets, and automation
that does not need an MSBuild project. These modes create the same synthetic
compilation used by `--file`; no temporary source file is required.

## Source Text Transports

Pass source directly on the command line:

```powershell
SharpProof.SymbolicCli `
  --source-text 'class C { static int M(int value) => value; }' `
  --source-file-name virtual/Buffer.cs `
  --line 1 `
  --compact-json
```

Or read only the C# source from standard input:

```powershell
Get-Content .\Buffer.cs -Raw | SharpProof.SymbolicCli `
  --stdin `
  --source-file-name virtual/Buffer.cs `
  --line 12
```

`--file`, `--stdin`, and `--source-text` are mutually exclusive.
`--source-file-name` is a virtual Roslyn syntax-tree path and is reported in
query results; it is not opened from disk. Standalone compiler switches such
as `--reference`, `--language-version`, `--define`, and `--nullable` work with
all three transports.

For a snippet extracted from a larger document, retain its origin metadata:

```powershell
SharpProof.SymbolicCli explain `
  --source-text $snippet `
  --source-file-name generated/Selection.cs `
  --source-map-uri file:///workspace/Original.cs `
  --source-map-original-line 80 `
  --source-map-original-column 5 `
  --line 1
```

The map says that snippet line 1, column 1 begins at the supplied original
location. Analysis targets and result coordinates remain snippet-local. An
editor adapter can use the retained URI and origin to translate them back to
the containing document. `explain` prints this metadata when present.

## JSON Request Envelope

Automation can provide a strict, versioned request inline with
`--request-json <json>` or stream it through `--request-json-stdin`. A request
selector is the only CLI argument because every query setting belongs in the
envelope.

```json
{
  "schemaVersion": 1,
  "mode": "query",
  "source": {
    "text": "#if FEATURE\nclass C { int M(int value) => value; }\n#endif",
    "filePath": "virtual/Buffer.cs",
    "sourceMap": {
      "sourceUri": "file:///workspace/Original.cs",
      "originalStartLine": 80,
      "originalStartColumn": 5
    }
  },
  "target": {
    "kind": "point",
    "line": 2,
    "column": 11
  },
  "references": ["C:/reference-assemblies/System.Runtime.dll"],
  "parseOptions": {
    "languageVersion": "preview",
    "preprocessorSymbols": ["FEATURE"],
    "nullable": "enable",
    "allowUnsafe": false,
    "documentationMode": "parse",
    "platform": "AnyCpu",
    "optimization": "Debug",
    "assemblyName": "Editor.Buffer"
  },
  "impliedConditions": ["value >= 0"],
  "smt": {
    "mode": "bounded",
    "timeoutMs": 300,
    "methodBudgetMs": 2000,
    "maxPathConditions": 32,
    "maxExpressionNodes": 300,
    "transientRetries": 1,
    "recycleContextOnTransientFailure": true,
    "disposeContextOnExit": false
  },
  "analysisLimits": {
    "merged-if-else-facts": 24
  },
  "query": {
    "checkReachability": true,
    "includeExpressionProgramPoints": false,
    "includeCurrentStatementCompletionFacts": false,
    "invariantTargets": ["value"]
  },
  "output": {
    "format": "compactJson",
    "maxLines": 10,
    "maxPoints": 50,
    "maxFacts": 20,
    "maxConditions": 20,
    "maxProofs": 20
  }
}
```

Supported `mode` values are `query`, `explain`, `runtimeHazards`,
`complexity`, and `capabilities`. Target kinds are `point`, `line`,
`position`, `span`, `lineSpan`, and `allLines`; each kind requires its matching
location fields. Output formats are `text`, `json`, `compactJson`, and
`invariantJson`. Runtime-hazard requests can also set
`query.includeUnprovenHazards`, `query.failOnHazard`, and repeated
`query.hazardKinds`; compact output accepts `maxHazards`.

Schema version 1 rejects unknown properties, unsupported values, missing
required target fields, invalid budgets, and incompatible query combinations
as usage errors. This makes misspelled automation settings visible instead of
silently accepting a different query.

## .NET API

The public API accepts the same in-memory source and compiler profile directly:

```csharp
var profile = new SymbolicSourceCompilationProfile(
    preprocessorSymbols: new[] { "FEATURE" },
    nullableContext: NullableContextOptions.Enable);
var input = SymbolicSourceInput
    .FromTextWithProfile(sourceText, profile, "virtual/Buffer.cs")
    .WithSourceMap(new SymbolicSourceMap(
        "file:///workspace/Original.cs",
        originalStartLine: 80,
        originalStartColumn: 5));

var options = new SymbolicQueryOptions(
    references: references,
    smtAnalysis: smt,
    impliedConditions: new[] { "value >= 0" });
var result = new SymbolicQueryService().Query(
    new SymbolicQueryRequest(
        input,
        SymbolicQueryTarget.Point(line: 2, column: 11),
        options));
```

`SymbolicSourceInput.SourceMap` retains the immutable origin metadata for the
host. As with CLI requests, it does not mutate Roslyn spans or result
coordinates.
