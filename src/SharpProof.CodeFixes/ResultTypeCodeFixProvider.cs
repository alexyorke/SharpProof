using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;

namespace SharpProof.CodeFixes;

/// <summary>Corrects the type argument of an incorrectly typed result intrinsic.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ResultTypeCodeFixProvider)), Shared]
public sealed class ResultTypeCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "SharpProof.ResultReturnType";

    public override ImmutableArray<string> FixableDiagnosticIds => ["SP0024"];

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document ?? throw new ArgumentException("A code-fix document is required.", nameof(context));
        // This provider is exported only for C# source documents, whose language
        // services supply both a syntax tree and a compilation semantic model.
        var root = (await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false))!;
        var model = (await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false))!;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue("SharpProof.FixKind", out var fixKind) || fixKind != "ResultReturnType")
            {
                continue;
            }
            var invocation = root.FindNode(diagnostic.Location.SourceSpan)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation == null || invocation.Span != diagnostic.Location.SourceSpan ||
                model.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol intrinsic ||
                !IsContractMethod(intrinsic, "Result", model.Compilation) || intrinsic.TypeArguments.Length != 1 ||
                invocation.ArgumentList.Arguments.Count != 0 ||
                model.GetEnclosingSymbol(invocation.SpanStart, context.CancellationToken) is not IMethodSymbol callable ||
                callable.ReturnsVoid || callable.IsAsync ||
                callable.ReturnType.TypeKind is TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer ||
                callable.ReturnType.IsRefLikeType || callable.ReturnType is ITypeParameterSymbol { AllowsRefLikeType: true } ||
                SymbolEqualityComparer.IncludeNullability.Equals(intrinsic.TypeArguments[0], callable.ReturnType))
            {
                continue;
            }

            // Do not repair unrelated placement/nesting errors or move any contract clause.
            var enclosingInvocation = invocation.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (enclosingInvocation == null ||
                model.GetSymbolInfo(enclosingInvocation, context.CancellationToken).Symbol is not IMethodSymbol clause ||
                !IsContractMethod(clause, "Ensures", model.Compilation))
            {
                continue;
            }

            // Result has no value arguments from which to infer T. A bound
            // invocation therefore spells its generic name directly or as a member.
            var name = invocation.Expression is GenericNameSyntax direct
                ? direct
                : (GenericNameSyntax)((MemberAccessExpressionSyntax)invocation.Expression).Name;
            var argumentType = name.TypeArgumentList.Arguments[0];
            // Directives inside a compound type can pair with trivia outside it.
            // Exterior trivia alone cannot safely preserve that conditional text.
            if (argumentType.ContainsDirectives)
            { continue; }
            var replacement = SyntaxGenerator.GetGenerator(context.Document).TypeExpression(callable.ReturnType)
                .WithTriviaFrom(argumentType)
                .WithAdditionalAnnotations(Simplifier.Annotation);
            var changedDocument = document.WithSyntaxRoot(root.ReplaceNode(argumentType, replacement));
            if (await CodeFixCompilationValidation.HasNewErrorsAsync(document.Project, changedDocument.Project, context.CancellationToken).ConfigureAwait(false))
            { continue; }
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Match Contract.Result type to the callable return type",
                    _ => Task.FromResult(changedDocument),
                    EquivalenceKey),
                diagnostic);
        }
    }

    private static bool IsContractMethod(IMethodSymbol method, string name, Compilation compilation)
    {
        method = method ?? throw new ArgumentNullException(nameof(method));
        name = name ?? throw new ArgumentNullException(nameof(name));
        compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        return method.Name == name && method.IsStatic &&
            SymbolEqualityComparer.Default.Equals(method.ContainingType, compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")) &&
            method.ContainingAssembly.Identity.Name == "SharpProof.Attributes";
    }
}
