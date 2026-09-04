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

internal static class OpenSourceCorpusRunner
{
    private const string AnnotationKind = "SharpProofOssCorpusMethod";

    internal static async Task<ImmutableArray<CorpusObservation>> ObserveAsync(
        OpenSourceCorpusDocument document,
        CancellationToken cancellationToken)
    {
        var parsedFiles = OpenSourceCorpusCatalog.GetParsedFiles(document);
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
            Encoding.UTF8,
            cancellationToken));

        var methodsByFile = document.Methods
            .GroupBy(
                static method => OpenSourceCorpusCatalog.GetSourceFileKey(
                    method.SourceId,
                    method.Path),
                StringComparer.Ordinal)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                StringComparer.Ordinal);
        var methodsById = document.Methods.ToImmutableDictionary(
            static method => method.Id,
            StringComparer.Ordinal);
        var targets = ImmutableDictionary.CreateBuilder<
            TargetKey,
            TargetInfo>();
        foreach (var file in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = OpenSourceCorpusCatalog.GetSourceFileKey(
                file.SourceId,
                file.Path);
            var root = parsedFiles is not null &&
                parsedFiles.TryGetValue(key, out var parsedRoot)
                ? parsedRoot
                : CSharpSyntaxTree.ParseText(
                        OpenSourceCorpusCatalog.NormalizeLineEndings(file.Content),
                        AnalyzerGateHost.ParseOptions,
                        file.Path,
                        Encoding.UTF8,
                        cancellationToken)
                    .GetCompilationUnitRoot(cancellationToken);
            if (methodsByFile.TryGetValue(key, out var methods))
            {
                var declarationIndex =
                    OpenSourceCorpusCatalog.BuildDeclarationIndex(root);
                var selected = methods.ToImmutableDictionary(
                    method => OpenSourceCorpusCatalog.FindDeclaration(
                        declarationIndex,
                        method),
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
                     .OfType<MethodDeclarationSyntax>())
            {
                var annotation = declaration.GetAnnotations(AnnotationKind)
                    .Single();
                var id = annotation.Data ??
                    throw new InvalidDataException(
                        $"Instrumented method in {file.Path} has no corpus ID.");
                if (!methodsById.TryGetValue(id, out var method))
                {
                    throw new InvalidDataException(
                        $"Instrumented method in {file.Path} refers to " +
                        $"unknown corpus ID {id}.");
                }
                targets.Add(
                    new TargetKey(tree, declaration.SpanStart),
                    new TargetInfo(method, tree, declaration.FullSpan));
            }
        }
        if (targets.Count != document.Methods.Length)
        {
            throw new InvalidDataException(
                $"Instrumented {targets.Count} OSS methods, expected " +
                $"{document.Methods.Length}.");
        }

        var template = AnalyzerGateHost.CreateCompilation(
            string.Empty,
            "SharpProofOssCorpus");
        var compilation = template
            .RemoveSyntaxTrees(template.SyntaxTrees)
            .AddSyntaxTrees(trees);
        AnalyzerGateHost.ThrowIfCompilationHasErrors(
            compilation,
            25,
            static errors => new InvalidDataException(
                "The pinned OSS corpus did not compile:" +
                Environment.NewLine + errors),
            cancellationToken);

        var targetMap = targets.ToImmutable();
        var targetsByMethodId = targetMap.Values.ToImmutableDictionary(
            static target => target.Method.Id,
            StringComparer.Ordinal);
        var factory = new RecordingSessionFactory(targetMap);
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
        var diagnosticAssignments = new int[diagnostics.Length];
        var targetsByTree = targetMap.Values
            .GroupBy(
                static target => target.Tree,
                (IEqualityComparer<SyntaxTree>)ReferenceEqualityComparer.Instance)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                (IEqualityComparer<SyntaxTree>)ReferenceEqualityComparer.Instance);
        var diagnosticsByMethod = targetMap.Values
            .Select(static target => target.Method.Id)
            .ToDictionary(
                static id => id,
                static _ => new List<string>(),
                StringComparer.Ordinal);
        for (var index = 0; index < diagnostics.Length; index++)
        {
            var diagnostic = diagnostics[index];
            if (diagnostic.Location.SourceTree is not { } tree ||
                !targetsByTree.TryGetValue(tree, out var treeTargets))
            {
                continue;
            }

            foreach (var target in treeTargets)
            {
                if (!target.Span.Contains(diagnostic.Location.SourceSpan))
                {
                    continue;
                }

                diagnosticAssignments[index]++;
                diagnosticsByMethod[target.Method.Id].Add(
                    CorpusGate.CanonicalizeDiagnostic(
                        diagnostic,
                        compilation.Options));
            }
        }
        foreach (var method in document.Methods)
        {
            if (!outcomes.TryGetValue(method.Id, out var semanticOutcome))
            {
                throw new InvalidDataException(
                    $"Analyzer did not record an outcome for OSS method {method.Id}.");
            }

            if (!targetsByMethodId.TryGetValue(method.Id, out var target))
            {
                throw new InvalidDataException(
                    $"Analyzer did not produce a target for OSS method " +
                    $"{method.Id}.");
            }
            var canonicalDiagnostics = diagnosticsByMethod[target.Method.Id]
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
        RequireCompleteDiagnosticAssignment(
            diagnostics,
            diagnosticAssignments);
        return observations.ToImmutable();
    }

    internal static void RequireCompleteDiagnosticAssignment(
        ImmutableArray<Diagnostic> diagnostics,
        int[] assignmentCounts)
    {
        if (assignmentCounts.Length != diagnostics.Length)
        {
            throw new ArgumentException(
                "Diagnostic assignment counts do not match the diagnostics.",
                nameof(assignmentCounts));
        }

        var invalid = diagnostics
            .Select((diagnostic, index) =>
                (Diagnostic: diagnostic, Count: assignmentCounts[index]))
            .Where(static item => item.Count != 1)
            .ToArray();
        if (invalid.Length == 0)
        {
            return;
        }

        throw new InvalidDataException(
            $"{invalid.Length} analyzer diagnostics were not assigned " +
            "to exactly one selected OSS method:" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                invalid.Take(25).Select(static item =>
                    $"{item.Diagnostic.Id} [{item.Count}] " +
                    item.Diagnostic.Location)));
    }

    private static MethodDeclarationSyntax Instrument(
        MethodDeclarationSyntax declaration,
        string id)
    {
        return declaration
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
    }

    private readonly record struct TargetKey(
        SyntaxTree Tree,
        int SourceStart);

    private sealed record TargetInfo(
        OpenSourceCorpusMethod Method,
        SyntaxTree Tree,
        TextSpan Span);

    private sealed class RecordingSessionFactory(
        ImmutableDictionary<TargetKey, TargetInfo> targets)
        : IAnalyzerSessionFactory
    {
        private readonly ConcurrentDictionary<
            string,
            AnalyzerSemanticOutcome> _outcomes =
            new(StringComparer.Ordinal);

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            return new(
                compilation,
                configuration,
                cancellationToken,
                Record);
        }

        internal ImmutableDictionary<string, AnalyzerSemanticOutcome>
            GetOutcomes()
        {
            return _outcomes.ToImmutableDictionary(StringComparer.Ordinal);
        }

        private void Record(
            IMethodSymbol method,
            AnalyzerSemanticOutcome outcome)
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (!targets.TryGetValue(
                        new TargetKey(
                            reference.SyntaxTree,
                            reference.Span.Start),
                        out var target))
                {
                    continue;
                }

                _outcomes.AddOrUpdate(
                    target.Method.Id,
                    outcome,
                    (_, current) =>
                        AnalyzerSemanticOutcomes.Combine(current, outcome));
            }
        }
    }
}
