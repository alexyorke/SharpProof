# Architecture

SharpProof has one direction of analysis:

```text
C# source or referenced IL
        |
        v
cached MethodEffects
   |      |      |      |
 purity allocation capability exceptions
        |
        v
unified SharpProofAnalysisResult
```

`SharpProof.Symbolic` owns the canonical effects and symbolic result model. `SharpProof.Analyzer` projects diagnostics and contracts. The CLI, NuGet package, and VSIX consume the same result. Referenced IL is resolved lazily and cached in memory; there is no generated summary tool or JSON artifact pipeline.
