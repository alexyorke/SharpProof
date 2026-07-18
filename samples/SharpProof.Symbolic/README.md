# SharpProof.Symbolic package sample

This console sample queries the invariant at a source location through the
supported `SharpProofAnalysisSession` API.

The preview package is not published to NuGet.org yet. From the repository root,
build the local feed, restore this sample from that feed, and run it:

```powershell
.\build-nuget.ps1 -Configuration Release
dotnet restore .\samples\SharpProof.Symbolic\SharpProof.Symbolic.Sample.csproj --source .\artifacts\nuget
dotnet run --project .\samples\SharpProof.Symbolic\SharpProof.Symbolic.Sample.csproj --no-restore
```

The query analyzes the supplied C# text without executing it and prints the
number of matching program points and their merged invariant.
