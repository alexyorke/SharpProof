using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Gates.Corpus;

internal static class OpenSourceCorpusRunner {
    private const string AnnotationKind = "SharpProofOssCorpusMethod";

    internal static async Task<ImmutableArray<CorpusObservation>> ObserveAsync(
        OpenSourceCorpusDocument document,
        CancellationToken cancellationToken) {
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(
            document.Files.Length + 1);
        trees.Add(CSharpSyntaxTree.ParseText(
            """
            global using System;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Linq;
            global using System.Net.Http;
            global using System.Threading;
            global using System.Threading.Tasks;
            """,
            AnalyzerGateHost.ParseOptions,
            "__SharpProofOssCorpusGlobalUsings.cs",
            Encoding.UTF8));

        var methodsByFile = document.Methods
            .GroupBy(
                static method => $"{method.SourceId}|{method.Path}",
                StringComparer.Ordinal)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                StringComparer.Ordinal);
        var targets = ImmutableDictionary.CreateBuilder<
            TargetKey,
            TargetInfo>();
        foreach (var file in document.Files) {
            cancellationToken.ThrowIfCancellationRequested();
            var root = CSharpSyntaxTree.ParseText(
                    OpenSourceCorpusCatalog.NormalizeLineEndings(file.Content),
                    AnalyzerGateHost.ParseOptions,
                    file.Path,
                    Encoding.UTF8)
                .GetCompilationUnitRoot(cancellationToken);
            var key = $"{file.SourceId}|{file.Path}";
            if (methodsByFile.TryGetValue(key, out var methods)) {
                var selected = methods.ToImmutableDictionary(
                    method => OpenSourceCorpusCatalog.FindDeclaration(root, method),
                    static method => method);
                root = root.ReplaceNodes(
                    selected.Keys,
                    (original, rewritten) => Instrument(
                        rewritten,
                        selected[original].Id));
            }
            var tree = CSharpSyntaxTree.Create(
                root,
                AnalyzerGateHost.ParseOptions,
                file.Path,
                Encoding.UTF8);
            trees.Add(tree);
            foreach (var declaration in tree.GetCompilationUnitRoot(
                         cancellationToken)
                     .GetAnnotatedNodes(AnnotationKind)
                     .OfType<MethodDeclarationSyntax>()) {
                var annotation = declaration.GetAnnotations(AnnotationKind)
                    .Single();
                var id = annotation.Data ??
                    throw new InvalidDataException(
                        $"Instrumented method in {file.Path} has no corpus ID.");
                var method = document.Methods.Single(candidate =>
                    string.Equals(candidate.Id, id, StringComparison.Ordinal));
                targets.Add(
                    new TargetKey(tree, declaration.SpanStart),
                    new TargetInfo(method, tree, declaration.FullSpan));
            }
        }
        if (targets.Count != document.Methods.Length)
            throw new InvalidDataException(
                $"Instrumented {targets.Count} OSS methods, expected " +
                $"{document.Methods.Length}.");

        var template = AnalyzerGateHost.CreateCompilation(
            string.Empty,
            "SharpProofOssCorpus");
        var compilation = template
            .RemoveSyntaxTrees(template.SyntaxTrees)
            .AddSyntaxTrees(trees);
        var compilerErrors = compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(25)
            .ToImmutableArray();
        if (!compilerErrors.IsDefaultOrEmpty)
            throw new InvalidDataException(
                "The pinned OSS corpus did not compile:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    compilerErrors.Select(static diagnostic =>
                        diagnostic.ToString())));

        var factory = new RecordingSessionFactory(targets.ToImmutable());
        var diagnostics = await AnalyzerGateHost.AnalyzeAsync(
                compilation,
                new SharpProofAnalyzer(factory),
                "effects",
                concurrentAnalysis: true,
                cancellationToken)
            .ConfigureAwait(false);
        var outcomes = factory.GetOutcomes();
        var observations = ImmutableArray.CreateBuilder<CorpusObservation>(
            document.Methods.Length);
        foreach (var method in document.Methods) {
            if (!outcomes.TryGetValue(method.Id, out var semanticOutcome))
                throw new InvalidDataException(
                    $"Analyzer did not record an outcome for OSS method {method.Id}.");
            var target = targets.Values.Single(info =>
                string.Equals(
                    info.Method.Id,
                    method.Id,
                    StringComparison.Ordinal));
            var canonicalDiagnostics = diagnostics
                .Where(diagnostic =>
                    ReferenceEquals(diagnostic.Location.SourceTree, target.Tree) &&
                    target.Span.Contains(diagnostic.Location.SourceSpan))
                .Select(diagnostic => CorpusGate.CanonicalizeDiagnostic(
                    diagnostic,
                    compilation.Options))
                .OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal)
                .ToImmutableArray();
            observations.Add(
                new CorpusObservation(
                    $"{method.Id}.baseline",
                    CorpusGate.ToVerdict(
                        semanticOutcome,
                        canonicalDiagnostics.IsDefaultOrEmpty),
                    semanticOutcome,
                    canonicalDiagnostics));
        }
        return observations.ToImmutable();
    }

    private static MethodDeclarationSyntax Instrument(
        MethodDeclarationSyntax declaration,
        string id) =>
        declaration
            .WithAttributeLists(
                declaration.AttributeLists.Insert(
                    0,
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Attribute(
                                SyntaxFactory.ParseName(
                                    "global::SharpProof.Attributes.EnforcePure"))))))
            .WithAdditionalAnnotations(
                new SyntaxAnnotation(AnnotationKind, id));

    private readonly record struct TargetKey(
        SyntaxTree Tree,
        int SourceStart);

    private sealed record TargetInfo(
        OpenSourceCorpusMethod Method,
        SyntaxTree Tree,
        TextSpan Span);

    private sealed class RecordingSessionFactory(
        ImmutableDictionary<TargetKey, TargetInfo> targets)
        : IAnalyzerSessionFactory {
        private readonly ConcurrentDictionary<
            string,
            AnalyzerSemanticOutcome> _outcomes =
            new(StringComparer.Ordinal);

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken) =>
            new(
                compilation,
                configuration,
                cancellationToken,
                Record);

        internal ImmutableDictionary<string, AnalyzerSemanticOutcome>
            GetOutcomes() =>
            _outcomes.ToImmutableDictionary(StringComparer.Ordinal);

        private void Record(
            IMethodSymbol method,
            AnalyzerSemanticOutcome outcome) {
            foreach (var reference in method.DeclaringSyntaxReferences) {
                if (!targets.TryGetValue(
                        new TargetKey(
                            reference.SyntaxTree,
                            reference.Span.Start),
                        out var target))
                    continue;
                _outcomes.AddOrUpdate(
                    target.Method.Id,
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(current, outcome));
            }
        }
    }
}
