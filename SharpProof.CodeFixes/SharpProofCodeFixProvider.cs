using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer;

namespace SharpProof
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SharpProofCodeFixProvider)), Shared]
    public sealed class SharpProofCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
            SharpProofDiagnostics.PurityNotVerifiedId,
            SharpProofDiagnostics.MisplacedAttributeId,
            SharpProofDiagnostics.MissingEnforcePureAttributeId,
            SharpProofDiagnostics.ConflictingPurityAttributesId,
            SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId,
            SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId,
            SharpProofDiagnostics.RedundantAllowSynchronizationId);

        public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics[0];
            var document = context.Document;
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;

            switch (diagnostic.Id)
            {
                case SharpProofDiagnostics.PurityNotVerifiedId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declImpure))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [EnforcePure] and [Pure] attributes",
                                c => RemoveAttributesMatchingAsync(document, root, declImpure, IsEnforcePureOrPureAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0002"),
                            diagnostic);
                    }
                    break;

                case SharpProofDiagnostics.MisplacedAttributeId:
                    if (TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedPurity))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove misplaced purity attribute",
                                c => RemoveMisplacedAttributeAsync(document, root, misplacedPurity, c),
                                nameof(RemoveMisplacedAttributeAsync)),
                            diagnostic);
                    }
                    break;

                case SharpProofDiagnostics.MissingEnforcePureAttributeId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declMissing))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Add [EnforcePure] attribute",
                                c => AddEnforcePureAttributeAsync(document, root, declMissing, c),
                                nameof(AddEnforcePureAttributeAsync)),
                            diagnostic);
                    }
                    break;

                case SharpProofDiagnostics.ConflictingPurityAttributesId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declConflict))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove conflicting purity boundary attributes",
                                c => RemoveAttributesMatchingAsync(document, root, declConflict, IsConflictingPurityBoundaryAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0005"),
                            diagnostic);
                    }
                    break;

                case SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declAllow))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Add [EnforcePure] attribute",
                                c => AddEnforcePureAttributeAsync(document, root, declAllow, c),
                                nameof(AddEnforcePureAttributeAsync) + "SP0006a"),
                            diagnostic);
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [AllowSynchronization] attribute",
                                c => RemoveAttributesMatchingAsync(document, root, declAllow, IsAllowSynchronizationAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0006b"),
                            diagnostic);
                    }
                    break;

                case SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId:
                    if (TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedAllow))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove misplaced [AllowSynchronization] attribute",
                                c => RemoveMisplacedAttributeAsync(document, root, misplacedAllow, c),
                                nameof(RemoveMisplacedAttributeAsync) + "SP0007"),
                            diagnostic);
                    }
                    break;

                case SharpProofDiagnostics.RedundantAllowSynchronizationId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declRedundant))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [AllowSynchronization] attribute",
                                c => RemoveAttributesMatchingAsync(document, root, declRedundant, IsAllowSynchronizationAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0008"),
                            diagnostic);
                    }
                    break;
            }
        }

        private static bool TryFindPurityTargetDeclaration(SyntaxNode root, int position, out SyntaxNode declaration)
        {
            declaration = null!;
            for (var node = root.FindToken(position).Parent; node != null; node = node.Parent)
            {
                switch (node)
                {
                    case MethodDeclarationSyntax:
                    case ConstructorDeclarationSyntax:
                    case OperatorDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case LocalFunctionStatementSyntax:
                        declaration = node;
                        return true;
                }
            }
            return false;
        }

        private static bool TryFindAttributeSyntax(SyntaxNode root, Microsoft.CodeAnalysis.Text.TextSpan span, out AttributeSyntax attribute)
        {
            attribute = null!;
            var node = root.FindNode(span, findInsideTrivia: false, getInnermostNodeForTie: true);
            attribute = node.FirstAncestorOrSelf<AttributeSyntax>() ?? (node as AttributeSyntax)!;
            return attribute != null;
        }

        private static SyntaxNode? GetHostForAttribute(AttributeSyntax attr)
        {
            if (attr.Parent is not AttributeListSyntax list)
                return null;
            return list.Parent;
        }

        private static SyntaxNode RemoveAttributeFromHost(SyntaxNode host, AttributeSyntax attrToRemove)
        {
            var newLists = RemoveFromAttributeLists(GetAttributeLists(host), attrToRemove);
            return WithAttributeLists(host, newLists);
        }

        private static SyntaxList<AttributeListSyntax> GetAttributeLists(SyntaxNode host)
        {
            return host switch
            {
                MemberDeclarationSyntax m => m.AttributeLists,
                ParameterSyntax p => p.AttributeLists,
                CompilationUnitSyntax u => u.AttributeLists,
                LocalFunctionStatementSyntax l => l.AttributeLists,
                _ => default
            };
        }

        private static SyntaxNode WithAttributeLists(SyntaxNode host, SyntaxList<AttributeListSyntax> lists)
        {
            return host switch
            {
                MethodDeclarationSyntax m => m.WithAttributeLists(lists),
                ConstructorDeclarationSyntax c => c.WithAttributeLists(lists),
                OperatorDeclarationSyntax o => o.WithAttributeLists(lists),
                AccessorDeclarationSyntax a => a.WithAttributeLists(lists),
                LocalFunctionStatementSyntax l => l.WithAttributeLists(lists),
                ClassDeclarationSyntax c => c.WithAttributeLists(lists),
                StructDeclarationSyntax s => s.WithAttributeLists(lists),
                InterfaceDeclarationSyntax i => i.WithAttributeLists(lists),
                RecordDeclarationSyntax r => r.WithAttributeLists(lists),
                EnumDeclarationSyntax e => e.WithAttributeLists(lists),
                DelegateDeclarationSyntax d => d.WithAttributeLists(lists),
                PropertyDeclarationSyntax p => p.WithAttributeLists(lists),
                EventDeclarationSyntax ev => ev.WithAttributeLists(lists),
                FieldDeclarationSyntax f => f.WithAttributeLists(lists),
                ParameterSyntax p => p.WithAttributeLists(lists),
                CompilationUnitSyntax u => u.WithAttributeLists(lists),
                _ => host
            };
        }

        private static SyntaxList<AttributeListSyntax> RemoveFromAttributeLists(SyntaxList<AttributeListSyntax> lists, AttributeSyntax remove)
        {
            var newLists = new List<AttributeListSyntax>();
            foreach (var list in lists)
            {
                var kept = list.Attributes.Where(a => !a.Span.Equals(remove.Span)).ToList();
                if (kept.Count == 0)
                    continue;
                if (kept.Count == list.Attributes.Count)
                    newLists.Add(list);
                else
                    newLists.Add(list.WithAttributes(SyntaxFactory.SeparatedList(kept)));
            }
            return SyntaxFactory.List(newLists);
        }

        private static SyntaxList<AttributeListSyntax> FilterAttributeLists(
            SyntaxList<AttributeListSyntax> lists,
            SemanticModel model,
            System.Func<INamedTypeSymbol?, bool> shouldRemoveType)
        {
            var newLists = new List<AttributeListSyntax>();
            foreach (var list in lists)
            {
                var kept = list.Attributes.Where(a => !shouldRemoveType(GetAttributeClass(model, a))).ToList();
                if (kept.Count == 0)
                    continue;
                if (kept.Count == list.Attributes.Count)
                    newLists.Add(list);
                else
                    newLists.Add(list.WithAttributes(SyntaxFactory.SeparatedList(kept)));
            }
            return SyntaxFactory.List(newLists);
        }

        private static INamedTypeSymbol? GetAttributeClass(SemanticModel model, AttributeSyntax attributeSyntax)
        {
            var sym = model.GetSymbolInfo(attributeSyntax).Symbol;
            if (sym is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor)
                return ctor.ContainingType;
            if (sym is INamedTypeSymbol nt)
                return nt;
            return null;
        }

        private static bool IsEnforcePureOrPureAttribute(INamedTypeSymbol? t)
        {
            if (t == null) return false;
            return t.Name is "EnforcePureAttribute" or "PureAttribute"
                   && t.ContainingNamespace?.ToDisplayString() == "SharpProof.Attributes";
        }

        private static bool IsConflictingPurityBoundaryAttribute(INamedTypeSymbol? t) =>
            t != null &&
            t.Name is "PureAttribute" or "PureExternalAttribute" or "ImpureAttribute" &&
            t.ContainingNamespace?.ToDisplayString() == "SharpProof.Attributes";

        private static bool IsAllowSynchronizationAttribute(INamedTypeSymbol? t) =>
            t != null && t.Name == "AllowSynchronizationAttribute" && t.ContainingNamespace?.ToDisplayString() == "SharpProof.Attributes";

        private async Task<Document> RemoveMisplacedAttributeAsync(Document document, SyntaxNode root, AttributeSyntax attr, CancellationToken cancellationToken)
        {
            var host = GetHostForAttribute(attr);
            if (host == null)
                return document;
            var newHost = RemoveAttributeFromHost(host, attr);
            if (ReferenceEquals(host, newHost))
                return document;
            var newRoot = root.ReplaceNode(host, newHost);
            return document.WithSyntaxRoot(newRoot);
        }

        private async Task<Document> RemoveAttributesMatchingAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            System.Func<INamedTypeSymbol?, bool> shouldRemoveType,
            CancellationToken cancellationToken)
        {
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model == null)
                return document;
            var lists = GetAttributeLists(declaration);
            if (!FilterAttributeListsRemovesAny(lists, model, shouldRemoveType))
                return document;
            var newLists = FilterAttributeLists(lists, model, shouldRemoveType);
            var newDecl = WithAttributeLists(declaration, newLists);
            var newRoot = root.ReplaceNode(declaration, newDecl);
            return document.WithSyntaxRoot(newRoot);
        }

        private static bool FilterAttributeListsRemovesAny(
            SyntaxList<AttributeListSyntax> lists,
            SemanticModel model,
            System.Func<INamedTypeSymbol?, bool> shouldRemoveType)
        {
            foreach (var list in lists)
            {
                foreach (var attr in list.Attributes)
                {
                    if (shouldRemoveType(GetAttributeClass(model, attr)))
                        return true;
                }
            }
            return false;
        }

        private async Task<Document> AddEnforcePureAttributeAsync(Document document, SyntaxNode root, SyntaxNode declaration, CancellationToken cancellationToken)
        {
            const string ns = "SharpProof.Attributes";
            var lists = GetAttributeLists(declaration);
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model != null)
            {
                foreach (var list in lists)
                {
                    foreach (var attr in list.Attributes)
                    {
                        var c = GetAttributeClass(model, attr);
                        if (c?.Name == "EnforcePureAttribute" && c.ContainingNamespace?.ToDisplayString() == ns)
                            return document;
                    }
                }
            }

            var compilationUnit = declaration.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();
            bool useShortName = compilationUnit != null &&
                compilationUnit.Usings.Any(u => string.Equals(u.Name?.ToString(), "SharpProof.Attributes", System.StringComparison.Ordinal));
            var attributeName = useShortName
                ? "EnforcePure"
                : "global::SharpProof.Attributes.EnforcePure";
            var newAttrList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeName))))
                .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

            var newDecl = WithAttributeLists(declaration, lists.Insert(0, newAttrList));
            var newRoot = root.ReplaceNode(declaration, newDecl);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}
