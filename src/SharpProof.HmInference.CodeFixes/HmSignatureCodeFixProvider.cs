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

            if (Inference.Analyze(method, model, context.CancellationToken).Status != "Suggestion")
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

            return parameter.WithType(shape.ParameterList.Parameters[index].Type!.WithTriviaFrom(parameter.Type!));
        }), method.ParameterList.Parameters.GetSeparators()))).WithAdditionalAnnotations(Formatter.Annotation);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var changed = document.WithSyntaxRoot(root!.ReplaceNode(method, replacement));
        return await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
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

                var methods = diagnostics.Where(d =>
                {
                    if (d is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(d));
                    }

                    return d.Id == "HM0001" && d.Location.IsInSource && root.FullSpan.Contains(d.Location.SourceSpan);
                }).Select(d =>
                {
                    if (d is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(d));
                    }

                    var target = root.FindToken(d.Location.SourceSpan.Start).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                    return target?.Identifier.Span == d.Location.SourceSpan ? target : null;
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
