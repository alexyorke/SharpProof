using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace SharpProof.HmInference.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HmSignatureCodeFixProvider)), Shared]
public sealed class HmSignatureCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["HM0001"];

    public override FixAllProvider GetFixAllProvider()
    {
        return new SequentialFixAll();
    }

    private static bool CanEditAllMethodParts(Document document, MethodDeclarationSyntax method, SemanticModel model, CancellationToken cancellationToken)
    {
        var symbol = model.GetDeclaredSymbol(method, cancellationToken);
        var partner = symbol?.PartialDefinitionPart ?? symbol?.PartialImplementationPart;
        if (partner is null)
        {
            return true;
        }

        foreach (var reference in partner.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partnerDocument = document.Project.GetDocument(reference.SyntaxTree);
            if (partnerDocument is null || !document.Project.DocumentIds.Contains(partnerDocument.Id))
            {
                return false;
            }
        }

        return true;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null || model.Compilation.GetDiagnostics(context.CancellationToken).Any(d =>
        {
            if (d is null)
            {
                throw new global::System.ArgumentNullException(nameof(d));
            }

            return Inference.IsCompilationError(d);
        }))
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree != root.SyntaxTree || !root.FullSpan.Contains(diagnostic.Location.SourceSpan))
            {
                continue;
            }

            var method = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method is null || method.Identifier.Span != diagnostic.Location.SourceSpan)
            {
                continue;
            }

            if (!CanEditAllMethodParts(context.Document, method, model, context.CancellationToken)
                || Inference.Analyze(method, model, context.CancellationToken).Status != "Suggestion")
            {
                continue;
            }

            var span = method.Identifier.Span;
            context.RegisterCodeFix(CodeAction.Create("Generalize method signature (API change)", cancellation => ApplyAtAsync(context.Document, span.Start, cancellation), "HM.GeneralizeSignature"), diagnostic);
        }
    }

    private static async Task<Document> ApplyAtAsync(Document document, int position, CancellationToken cancellationToken)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || position >= root.FullSpan.End)
        {
            return document;
        }

        var method = root?.FindToken(position).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        return method is null ? document : await ApplyAsync(document, method, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Document> ApplyAsync(Document document, MethodDeclarationSyntax method, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || model.Compilation.GetDiagnostics(cancellationToken).Any(d =>
        {
            if (d is null)
            {
                throw new global::System.ArgumentNullException(nameof(d));
            }

            return Inference.IsCompilationError(d);
        }))
        {
            return document;
        }

        if (!CanEditAllMethodParts(document, method, model, cancellationToken))
        {
            return document;
        }

        var result = Inference.Analyze(method, model, cancellationToken);
        if (result.Status != "Suggestion" || result.Signature is null)
        {
            return document;
        }

        var shape = SyntaxFactory.ParseMemberDeclaration("static " + result.Signature + ";") as MethodDeclarationSyntax;
        if (shape is null || shape.ContainsDiagnostics)
        {
            return document;
        }

        var typeParameters = method.TypeParameterList is null ? shape.TypeParameterList
            : method.TypeParameterList.WithParameters(method.TypeParameterList.Parameters.AddRange(shape.TypeParameterList!.Parameters.Skip(method.TypeParameterList.Parameters.Count)));
        var replacement = method.WithConstraintClauses(method.ConstraintClauses.AddRange(shape.ConstraintClauses.Skip(method.ConstraintClauses.Count))).WithReturnType(shape.ReturnType.WithTriviaFrom(method.ReturnType)).WithTypeParameterList(typeParameters).WithParameterList(method.ParameterList.WithParameters(SyntaxFactory.SeparatedList(method.ParameterList.Parameters.Select((parameter, index) =>
        {
            if (parameter is null)
            {
                throw new global::System.ArgumentNullException(nameof(parameter));
            }

            if (index < 0)
            {
                throw new global::System.ArgumentOutOfRangeException(nameof(index));
            }

            // Preserve user syntax and append only attributes inferred for this parameter.
            return parameter.WithType(shape.ParameterList.Parameters[index].Type!.WithTriviaFrom(parameter.Type!))
                .AddAttributeLists(shape.ParameterList.Parameters[index].AttributeLists.Skip(parameter.AttributeLists.Count).ToArray());
        }), method.ParameterList.Parameters.GetSeparators()))).WithAdditionalAnnotations(Formatter.Annotation);
        var roots = Inference.RewriteMethodParts(method, replacement, model, cancellationToken);
        var solution = document.Project.Solution;
        var originals = new Dictionary<DocumentId, (SyntaxNode Root, SemanticModel Model)>();
        foreach (var entry in roots)
        {
            var originalDocument = document.Project.GetDocument(entry.Key);
            if (originalDocument is null)
            { return document; }
            var originalRoot = (await originalDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var originalModel = (await originalDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false))!;
            originals.Add(originalDocument.Id, (originalRoot, originalModel));
            var root = entry.Value.ReplaceNodes(entry.Value.GetAnnotatedNodes("HM.PartialRewrite"), (before, rewritten) =>
            {
                if (before is null)
                { throw new ArgumentNullException(nameof(before)); }
                if (rewritten is null)
                { throw new ArgumentNullException(nameof(rewritten)); }
                return rewritten.WithoutAnnotations("HM.PartialRewrite").WithAdditionalAnnotations(Formatter.Annotation);
            });
            solution = solution.WithDocumentSyntaxRoot(originalDocument.Id, root);
        }
        foreach (var entry in originals)
        {
            var changed = solution.GetDocument(entry.Key)!;
            var changedRoot = (await changed.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var formatted = await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            var formattedRoot = (await formatted.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var formattedModel = (await formatted.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false))!;
            if (Inference.PreservesCallerInfoArguments(entry.Value.Root, entry.Value.Model, formattedRoot, formattedModel, cancellationToken))
            { solution = formatted.Project.Solution; continue; }
            var unformattedRoot = changedRoot.ReplaceNodes(changedRoot.GetAnnotatedNodes(Formatter.Annotation), (original, rewritten) =>
            {
                if (original is null)
                { throw new ArgumentNullException(nameof(original)); }
                if (rewritten is null)
                { throw new ArgumentNullException(nameof(rewritten)); }
                return rewritten.WithoutAnnotations(Formatter.Annotation);
            });
            solution = solution.WithDocumentSyntaxRoot(entry.Key, unformattedRoot);
        }
        solution = await UpdateLinkedDocumentsAsync(document.Project.Solution, solution, originals.Keys, document.Id, method.Identifier.SpanStart, result.Signature, cancellationToken).ConfigureAwait(false)
            ?? document.Project.Solution;
        return solution.GetDocument(document.Id)!;

    }

    private static async Task<Solution?> UpdateLinkedDocumentsAsync(Solution original, Solution changed,
        IEnumerable<DocumentId> changedIds, DocumentId targetId, int targetPosition, string expectedSignature, CancellationToken cancellationToken)
    {
        if (original is null)
        {
            throw new ArgumentNullException(nameof(original));
        }
        if (changed is null)
        {
            throw new ArgumentNullException(nameof(changed));
        }
        if (changedIds is null)
        {
            throw new ArgumentNullException(nameof(changedIds));
        }
        if (targetId is null)
        {
            throw new ArgumentNullException(nameof(targetId));
        }
        if (expectedSignature is null)
        {
            throw new ArgumentNullException(nameof(expectedSignature));
        }

        var changedDocumentIds = new HashSet<DocumentId>(changedIds);
        var linkedProjects = new HashSet<ProjectId>();
        var targetDocument = original.GetDocument(targetId)!;
        var targetText = await targetDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
        foreach (var changedId in changedDocumentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalDocument = original.GetDocument(changedId)!;
            if (originalDocument.FilePath is null)
            {
                continue;
            }

            var originalText = await originalDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var changedText = await changed.GetDocument(changedId)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            foreach (var linked in original.Projects.SelectMany(project =>
            {
                if (project is null)
                {
                    throw new ArgumentNullException(nameof(project));
                }
                return project.Documents;
            }))
            {
                if (linked.Id == changedId || linked.FilePath != originalDocument.FilePath
                    || changedDocumentIds.Contains(linked.Id))
                {
                    continue;
                }

                var linkedText = await linked.GetTextAsync(cancellationToken).ConfigureAwait(false);
                if (!linkedText.ContentEquals(originalText))
                {
                    continue;
                }

                changed = changed.WithDocumentText(linked.Id, changedText);
                linkedProjects.Add(linked.Project.Id);
            }
        }

        foreach (var projectId in linkedProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Document? linkedTarget = null;
            foreach (var candidate in original.GetProject(projectId)!.Documents)
            {
                if (candidate.FilePath != targetDocument.FilePath)
                {
                    continue;
                }

                var candidateText = await candidate.GetTextAsync(cancellationToken).ConfigureAwait(false);
                if (candidateText.ContentEquals(targetText))
                {
                    linkedTarget = candidate;
                    break;
                }
            }

            if (linkedTarget is null)
            {
                return null;
            }

            var linkedRoot = await linkedTarget.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (linkedRoot is null || !linkedRoot.FullSpan.Contains(targetPosition))
            {
                return null;
            }

            var linkedMethod = linkedRoot.FindToken(targetPosition).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (linkedMethod?.Identifier.SpanStart != targetPosition)
            {
                return null;
            }

            var linkedModel = await linkedTarget.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (linkedModel is null || linkedModel.Compilation.GetDiagnostics(cancellationToken).Any(Inference.IsCompilationError)
                || !CanEditAllMethodParts(linkedTarget, linkedMethod, linkedModel, cancellationToken))
            {
                return null;
            }

            var linkedResult = Inference.Analyze(linkedMethod, linkedModel, cancellationToken);
            if (linkedResult.Status != "Suggestion" || linkedResult.Signature != expectedSignature)
            {
                return null;
            }
        }

        var affectedProjects = new HashSet<ProjectId>(linkedProjects);
        foreach (var changedId in changedDocumentIds)
        {
            affectedProjects.Add(changedId.ProjectId);
        }

        var dependents = new Dictionary<ProjectId, List<ProjectId>>();
        foreach (var project in original.Projects)
        {
            foreach (var reference in project.ProjectReferences)
            {
                if (!dependents.TryGetValue(reference.ProjectId, out var referringProjects))
                {
                    referringProjects = new List<ProjectId>();
                    dependents.Add(reference.ProjectId, referringProjects);
                }

                referringProjects.Add(project.Id);
            }
        }

        var pending = new Queue<ProjectId>(affectedProjects);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectId = pending.Dequeue();
            if (!dependents.TryGetValue(projectId, out var referringProjects))
            {
                continue;
            }

            foreach (var referringId in referringProjects)
            {
                if (affectedProjects.Add(referringId))
                {
                    pending.Enqueue(referringId);
                }
            }
        }

        foreach (var projectId in affectedProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await HasNewCompilationErrorsAsync(original.GetProject(projectId)!, changed.GetProject(projectId)!,
                cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return changed;
    }

    private static async Task<bool> HasNewCompilationErrorsAsync(Project before, Project after, CancellationToken cancellationToken)
    {
        if (before is null)
        {
            throw new ArgumentNullException(nameof(before));
        }
        if (after is null)
        {
            throw new ArgumentNullException(nameof(after));
        }

        var beforeCompilation = await before.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var afterCompilation = await after.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (beforeCompilation is null || afterCompilation is null)
        {
            return true;
        }

        (string Id, DocumentId? DocumentId, string Path, int Start, int Length, string Message) ErrorKey(Diagnostic diagnostic, Project project)
        {
            if (diagnostic is null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }
            if (project is null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            var tree = diagnostic.Location.SourceTree;
            return (diagnostic.Id, tree is null ? null : project.GetDocument(tree)?.Id,
                tree?.FilePath ?? string.Empty, diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length,
                diagnostic.GetMessage(global::System.Globalization.CultureInfo.InvariantCulture));
        }

        var previousErrors = new Dictionary<(string Id, DocumentId? DocumentId, string Path, int Start, int Length, string Message), int>();
        foreach (var diagnostic in beforeCompilation.GetDiagnostics(cancellationToken))
        {
            if (!Inference.IsCompilationError(diagnostic))
            {
                continue;
            }

            var key = ErrorKey(diagnostic, before);
            previousErrors.TryGetValue(key, out var count);
            previousErrors[key] = count + 1;
        }

        foreach (var diagnostic in afterCompilation.GetDiagnostics(cancellationToken))
        {
            if (!Inference.IsCompilationError(diagnostic))
            {
                continue;
            }

            var key = ErrorKey(diagnostic, after);
            if (!previousErrors.TryGetValue(key, out var count) || count == 0)
            {
                return true;
            }

            previousErrors[key] = count - 1;
        }

        return false;
    }

    private sealed class SequentialFixAll : FixAllProvider
    {
        public override Task<CodeAction?> GetFixAsync(FixAllContext context)
        {
            if (context is null)
            {
                throw new global::System.ArgumentNullException(nameof(context));
            }

            return Task.FromResult<CodeAction?>(CodeAction.Create("Generalize eligible method signatures (API change)", cancellation => ApplyAllAsync(context, cancellation), "HM.GeneralizeSignature"));
        }

        private static async Task<Solution> ApplyAllAsync(FixAllContext context, CancellationToken cancellationToken)
        {
            if (context is null)
            {
                throw new global::System.ArgumentNullException(nameof(context));
            }

            IEnumerable<Document> documents = context.Scope switch
            {
                FixAllScope.Document when context.Document is not null => new[]
                {
                    context.Document
                },
                FixAllScope.Project => context.Project.Documents,
                FixAllScope.Solution => context.Project.Solution.Projects.SelectMany(project =>
                {
                    if (project is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(project));
                    }

                    return project.Documents;
                }),
                _ => Enumerable.Empty<Document>()
            };
            var solution = context.Project.Solution;
            foreach (var original in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = solution.GetDocument(original.Id)!;
                var diagnostics = await context.GetDocumentDiagnosticsAsync(original).ConfigureAwait(false);
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (root is null)
                {
                    continue;
                }

                var diagnosticRoot = (await original.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
                var originalMethods = diagnosticRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
                var currentMethods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
                var methods = diagnostics.Where(d =>
                {
                    if (d is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(d));
                    }

                    return d.Id == "HM0001" && d.Location.IsInSource && diagnosticRoot.FullSpan.Contains(d.Location.SourceSpan);
                }).Select(d =>
                {
                    if (d is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(d));
                    }

                    var target = diagnosticRoot.FindToken(d.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                    var ordinal = target is null ? -1 : Array.IndexOf(originalMethods, target);
                    return target?.Identifier.Span == d.Location.SourceSpan && ordinal >= 0 && ordinal < currentMethods.Length ? currentMethods[ordinal] : null;
                }).Where(method => method is not null).Cast<MethodDeclarationSyntax>().Distinct().ToArray();
                var annotations = methods.ToDictionary(method =>
                {
                    if (method is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(method));
                    }

                    return method;
                }, _ => new SyntaxAnnotation());
                document = document.WithSyntaxRoot(root.ReplaceNodes(methods, (before, rewritten) =>
                {
                    if (before is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(before));
                    }

                    if (rewritten is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(rewritten));
                    }

                    return rewritten.WithAdditionalAnnotations(annotations[before]);
                }));
                // Recompute inference against the updated solution for every method.
                // Batch application of independently checked signatures is unsafe.
                foreach (var annotation in annotations.Values)
                {
                    root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                    var method = root!.GetAnnotatedNodes(annotation).OfType<MethodDeclarationSyntax>().SingleOrDefault();
                    if (method is not null)
                    {
                        document = await ApplyAsync(document, method, cancellationToken).ConfigureAwait(false);
                    }
                }

                solution = document.Project.Solution;
            }

            return solution;
        }
    }
}
