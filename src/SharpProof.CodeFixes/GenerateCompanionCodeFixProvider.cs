using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GenerateCompanionCodeFixProvider)), Shared]
public sealed class GenerateCompanionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [CompanionSuggestionContract.DiagnosticId];

    public override FixAllProvider? GetFixAllProvider()
    {
        return null;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(context, nameof(context));
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in context.Diagnostics)
        {
            if (root == null || diagnostic.Id != CompanionSuggestionContract.DiagnosticId ||
                !diagnostic.Properties.TryGetValue(CompanionSuggestionContract.FixKindProperty, out var fixKind) ||
                fixKind != CompanionSuggestionContract.FixKind || diagnostic.Location.SourceTree != root.SyntaxTree ||
                root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } declaration ||
                declaration.Identifier.Span != diagnostic.Location.SourceSpan)
            { continue; }
            await new CompanionRefactoringProvider().ComputeRefactoringsAsync(new CodeRefactoringContext(
                context.Document, diagnostic.Location.SourceSpan,
                action => context.RegisterCodeFix(action ?? throw new ArgumentNullException(nameof(action)), diagnostic),
                context.CancellationToken)).ConfigureAwait(false);
        }
    }
}
