using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof
{
    internal interface ICodeFixHandler
    {
        void Register(
            SharpProofCodeFixProvider provider,
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CodeFixHandlerRegistration registration);
    }

    internal static class CodeFixHandlers
    {
        private static readonly ICodeFixHandler SimpleRemoval = new SimpleRemovalCodeFixHandler();
        private static readonly ICodeFixHandler AddPurity = new AddPurityCodeFixHandler();
        private static readonly ICodeFixHandler Synchronization = new SynchronizationCodeFixHandler();
        private static readonly ICodeFixHandler MisplacedRequires = new MisplacedRequiresCodeFixHandler();
        private static readonly ICodeFixHandler InferredContract = new InferredContractCodeFixHandler();
        private static readonly ICodeFixHandler NullForgiving = new NullForgivingCodeFixHandler();

        internal static ICodeFixHandler Get(CodeFixHandlerFamily family)
        {
            return family switch
            {
                CodeFixHandlerFamily.SimpleRemoval => SimpleRemoval,
                CodeFixHandlerFamily.AddPurity => AddPurity,
                CodeFixHandlerFamily.AddPurityOrRemoveSynchronization => Synchronization,
                CodeFixHandlerFamily.MisplacedRequires => MisplacedRequires,
                CodeFixHandlerFamily.InferredContract => InferredContract,
                CodeFixHandlerFamily.NullForgivingRemoval => NullForgiving,
                _ => throw new System.ArgumentOutOfRangeException(nameof(family))
            };
        }
    }

    internal sealed class SimpleRemovalCodeFixHandler : ICodeFixHandler
    {
        public void Register(
            SharpProofCodeFixProvider provider,
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CodeFixHandlerRegistration registration)
        {
            provider.RegisterSimpleRemovalCodeFix(
                context,
                document,
                root,
                diagnostic,
                attributePolicy,
                registration.SimpleRemoval!);
        }
    }

    internal sealed class AddPurityCodeFixHandler : ICodeFixHandler
    {
        public void Register(
            SharpProofCodeFixProvider provider,
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CodeFixHandlerRegistration registration)
        {
            if (!SharpProofCodeFixProvider.TryFindPurityTargetDeclaration(
                    root, diagnostic.Location.SourceSpan.Start, out var declaration) ||
                declaration is PropertyDeclarationSyntax or IndexerDeclarationSyntax)
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add [EnforcePure] attribute",
                    cancellationToken => provider.AddEnforcePureAttributeAsync(
                        document, root, declaration, attributePolicy, cancellationToken),
                    "AddEnforcePureAttributeAsync"),
                diagnostic);
        }
    }

    internal sealed class SynchronizationCodeFixHandler : ICodeFixHandler
    {
        public void Register(
            SharpProofCodeFixProvider provider,
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CodeFixHandlerRegistration registration)
        {
            if (!SharpProofCodeFixProvider.TryFindPurityTargetDeclaration(
                    root, diagnostic.Location.SourceSpan.Start, out var declaration))
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add [EnforcePure] attribute",
                    cancellationToken => provider.AddEnforcePureAttributeAsync(
                        document, root, declaration, attributePolicy, cancellationToken),
                    "AddEnforcePureAttributeAsyncSP0006a"),
                diagnostic);
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove [AllowSynchronization] attribute",
                    cancellationToken => provider.RemoveAttributesMatchingAsync(
                        document,
                        root,
                        declaration,
                        SharpProofCodeFixProvider.AcceptedAttribute(
                            attributePolicy, "AllowSynchronizationAttribute"),
                        cancellationToken),
                    "RemoveAttributesMatchingAsyncSP0006b"),
                diagnostic);
        }
    }

    internal sealed class MisplacedRequiresCodeFixHandler : ICodeFixHandler
    {
        public void Register(
            SharpProofCodeFixProvider provider,
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CodeFixHandlerRegistration registration)
        {
            if (!SharpProofCodeFixProvider.TryFindAttributeSyntax(
                    root, diagnostic.Location.SourceSpan, out var attribute))
                return;

            if (SharpProofCodeFixProvider.CanMoveAttributeToGetter(attribute))
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Move [Requires] attribute to getter",
                        cancellationToken => SharpProofCodeFixProvider.MoveAttributeToGetterAsync(
                            document, root, attribute, cancellationToken),
                        "MoveAttributeToGetterAsyncSP0029"),
                    diagnostic);

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove misplaced [Requires] attribute",
                    cancellationToken => SharpProofCodeFixProvider.RemoveMisplacedAttributeAsync(
                        document, root, attribute, cancellationToken),
                    "RemoveMisplacedAttributeAsyncSP0029"),
                diagnostic);
        }
    }

    internal sealed class InferredContractCodeFixHandler : ICodeFixHandler
    {
        public void Register(
            SharpProofCodeFixProvider provider,
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CodeFixHandlerRegistration registration)
        {
            provider.RegisterInferredContractCodeFix(context, document, root, diagnostic);
        }
    }

    internal sealed class NullForgivingCodeFixHandler : ICodeFixHandler
    {
        public void Register(
            SharpProofCodeFixProvider provider,
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CodeFixHandlerRegistration registration)
        {
            if (!SharpProofCodeFixProvider.TryFindNullForgivingExpression(
                    root, diagnostic.Location.SourceSpan, out var suppression))
                return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove unnecessary null-forgiving operator",
                    _ => Task.FromResult(document.WithSyntaxRoot(
                        root.ReplaceNode(
                            suppression,
                            SharpProofCodeFixProvider.RemoveNullForgivingOperator(suppression)))),
                    "RemoveUnnecessaryNullForgivingOperator"),
                diagnostic);
        }
    }
}
