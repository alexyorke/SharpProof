using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Worker.Test;

internal static class WorkerTestCompilation
{
    internal static CSharpCompilation Create(
        string assemblyName,
        params (string FileName, string Source)[] sources)
    {
        return Create(
            assemblyName,
            OutputKind.DynamicallyLinkedLibrary,
            sources);
    }

    internal static CSharpCompilation Create(
        string assemblyName,
        OutputKind outputKind,
        IEnumerable<(string FileName, string Source)> sources)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp12,
            preprocessorSymbols: [Contract.ConditionalSymbol]);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            sources.Select(source => CSharpSyntaxTree.ParseText(
                source.Source,
                parseOptions,
                source.FileName)),
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(
                outputKind,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static error => error.ToString())));
        return compilation;
    }
}
