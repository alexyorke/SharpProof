using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Frontend.Host;

/// <summary>
/// Audited compiler-construction boundary for release evidence and fuzzing.
///
/// Production proof code must consume the compiler-bound compilation supplied
/// by the host. The release and fuzz harnesses are the explicit exception:
/// they construct disposable compilations to exercise the analyzer and
/// frontend. Keeping the Roslyn calls here makes that exception reviewable.
/// </summary>
internal static class CompilerConstructionBoundary
{
    internal static SyntaxTree ParseCSharpText(
        string text,
        CSharpParseOptions options,
        string? path = null,
        Encoding? encoding = null,
        CancellationToken cancellationToken = default)
    {
#pragma warning disable RS0030 // Audited release/fuzz source-synthesis boundary.
        return CSharpSyntaxTree.ParseText(
            text,
            options,
            path ?? string.Empty,
            encoding,
            cancellationToken: cancellationToken);
#pragma warning restore RS0030
    }

    internal static SyntaxTree CreateCSharpTree(
        CSharpSyntaxNode root,
        CSharpParseOptions options,
        string? path = null,
        Encoding? encoding = null)
    {
#pragma warning disable RS0030 // Audited release/fuzz source-synthesis boundary.
        return CSharpSyntaxTree.Create(root, options, path ?? string.Empty, encoding);
#pragma warning restore RS0030
    }

    internal static CSharpCompilation CreateCSharpCompilation(
        string assemblyName,
        IEnumerable<SyntaxTree>? syntaxTrees,
        IEnumerable<MetadataReference>? references,
        CSharpCompilationOptions? options)
    {
#pragma warning disable RS0030 // Audited release/fuzz source-synthesis boundary.
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            options);
#pragma warning restore RS0030
    }

    internal static ImmutableArray<Diagnostic> GetCompilationDiagnostics(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
#pragma warning disable RS0030 // Audited compile-diagnostic boundary for harnesses.
        return [.. compilation.GetDiagnostics(cancellationToken)];
#pragma warning restore RS0030
    }
}
