# SharpProof Agent Notes

- Run long-lived local .NET commands in this repo under a Windows Job Object.
- Preferred entrypoint: `.\scripts\Invoke-SharpProofDotnet.ps1 ...`
- Use the wrapper for `dotnet test`, `dotnet build`, `dotnet pack`, `dotnet restore`, and `dotnet run`.
- Do not start multiple large `Tools/SharpProof.EffectSummary` runs at the same time.
- If a runtime analysis needs extra headroom, pass an explicit `-MemoryLimitMb` value to the wrapper instead of running uncapped by accident.
- If a timed-out test leaves `testhost.exe` behind, stop the orphan before retrying.
