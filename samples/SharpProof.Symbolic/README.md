# SharpProof.Symbolic package sample

This console sample queries the invariant at a source location through the
supported `SymbolicQueryService` API.

After restoring `SharpProof.Symbolic` from NuGet, run:

```powershell
dotnet run
```

The query analyzes the supplied C# text without executing it and prints the
number of matching program points and their merged invariant.
