using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Contracts.Test;

internal static class ContractTestCompilation
{
    internal static CSharpCompilation Create(
        string assemblyPrefix,
        string source,
        bool allowUnsafe = false,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool includeSharpProofReference = true)
    {
        return Create(
            assemblyPrefix,
            [(string.Empty, source)],
            LanguageVersion.CSharp12,
            allowUnsafe,
            outputKind,
            includeSharpProofReference);
    }

    internal static CSharpCompilation Create(
        string assemblyPrefix,
        IEnumerable<(string FileName, string Source)> sources,
        LanguageVersion languageVersion = LanguageVersion.CSharp12,
        bool allowUnsafe = false,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool includeSharpProofReference = true)
    {
        var parseOptions = new CSharpParseOptions(
            languageVersion,
            preprocessorSymbols: ["SHARPPROOF_CONTRACTS"]);
        var compilation = CSharpCompilation.Create(
            assemblyPrefix + "_" + Guid.NewGuid().ToString("N"),
            sources.Select(source => CSharpSyntaxTree.ParseText(
                source.Source,
                parseOptions,
                source.FileName)),
            includeSharpProofReference
                ? TestMetadataReferences.WithSharpProof
                : TestMetadataReferences.Platform,
            new CSharpCompilationOptions(
                outputKind,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: allowUnsafe));
        AssertNoErrors(compilation);
        return compilation;
    }

    internal static void AssertNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic.ToString())));
    }
}
