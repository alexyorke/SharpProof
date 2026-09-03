using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;

internal static class TestCompilation
{
    internal static CSharpCompilation Create(
        string assemblyPrefix,
        string source,
        bool allowUnsafe = false,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool includeSharpProofReference = true)
    {
        return CreateCore(
            assemblyPrefix,
            [(string.Empty, source)],
            LanguageVersion.CSharp12,
            allowUnsafe,
            outputKind,
            includeSharpProofReference,
            appendUniqueAssemblySuffix: true);
    }

    internal static CSharpCompilation Create(
        string assemblyPrefix,
        IEnumerable<(string FileName, string Source)> sources,
        LanguageVersion languageVersion = LanguageVersion.CSharp12,
        bool allowUnsafe = false,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool includeSharpProofReference = true)
    {
        return CreateCore(
            assemblyPrefix,
            sources,
            languageVersion,
            allowUnsafe,
            outputKind,
            includeSharpProofReference,
            appendUniqueAssemblySuffix: true);
    }

    internal static CSharpCompilation Create(
        string assemblyName,
        params (string FileName, string Source)[] sources)
    {
        return CreateCore(
            assemblyName,
            sources,
            LanguageVersion.CSharp12,
            allowUnsafe: false,
            OutputKind.DynamicallyLinkedLibrary,
            includeSharpProofReference: true,
            appendUniqueAssemblySuffix: false);
    }

    internal static CSharpCompilation Create(
        string assemblyName,
        OutputKind outputKind,
        IEnumerable<(string FileName, string Source)> sources)
    {
        return CreateCore(
            assemblyName,
            sources,
            LanguageVersion.CSharp12,
            allowUnsafe: false,
            outputKind,
            includeSharpProofReference: true,
            appendUniqueAssemblySuffix: false);
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

    private static CSharpCompilation CreateCore(
        string assemblyName,
        IEnumerable<(string FileName, string Source)> sources,
        LanguageVersion languageVersion,
        bool allowUnsafe,
        OutputKind outputKind,
        bool includeSharpProofReference,
        bool appendUniqueAssemblySuffix)
    {
        var parseOptions = new CSharpParseOptions(
            languageVersion,
            preprocessorSymbols: [Contract.ConditionalSymbol]);
        var compilation = CSharpCompilation.Create(
            appendUniqueAssemblySuffix
                ? assemblyName + "_" + Guid.NewGuid().ToString("N")
                : assemblyName,
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
}
