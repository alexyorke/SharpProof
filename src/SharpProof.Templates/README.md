# SharpProof.Templates

Starter projects for [SharpProof](https://github.com/alexyorke/SharpProof), which
checks C# contracts and effects at build time.

```sh
dotnet new install SharpProof.Templates
dotnet new sharpproof -o MyProject
```

Each generated project has one SharpProof package reference, a pinned local
tool, a small contract example, and a README that explains how to build it.
Run `dotnet new sharpproof --help` for the available variants and options.

The projects need the .NET 10 SDK. See
[getting started](https://github.com/alexyorke/SharpProof/blob/master/docs/getting-started.md)
and the [support matrix](https://github.com/alexyorke/SharpProof/blob/master/docs/preview-support.md).
