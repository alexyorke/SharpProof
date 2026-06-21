# PurelySharp Agent Notes

- Run long-lived local .NET commands in this repo under a Windows Job Object.
- Preferred entrypoint: `.\scripts\Invoke-PurelySharpDotnet.ps1 ...`
- Use the wrapper for `dotnet test`, `dotnet build`, `dotnet pack`, `dotnet restore`, and `dotnet run`.
- Do not start multiple large `Tools/PurelySharp.EffectSummary` runs at the same time.
- If a runtime analysis needs extra headroom, pass an explicit `-MemoryLimitMb` value to the wrapper instead of running uncapped by accident.
- If a timed-out test leaves `testhost.exe` behind, stop the orphan before retrying.
- Treat `Tools/PurelySharp.EffectSummary/Program.cs` `AssemblyEffectSummarizer.VisitThrownExceptionEdges` as a known memory-risk path when investigating OOM behavior.
