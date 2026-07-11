using System;
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
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer;

namespace SharpProof
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SharpProofCodeFixProvider))]
    [Shared]
    public sealed class SharpProofCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
            SharpProofDiagnostics.PurityNotVerifiedId,
            SharpProofDiagnostics.MisplacedAttributeId,
            SharpProofDiagnostics.MissingEnforcePureAttributeId,
            SharpProofDiagnostics.ConflictingPurityAttributesId,
            SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId,
            SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId,
            SharpProofDiagnostics.RedundantAllowSynchronizationId,
            SharpProofDiagnostics.AllocationInZeroAllocationMethodId,
            SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId,
            SharpProofDiagnostics.CapabilityViolationId,
            SharpProofDiagnostics.CapabilityUnknownId,
            SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId,
            SharpProofDiagnostics.EnsuresNotProvenId,
            SharpProofDiagnostics.EnsuresUnsupportedId,
            SharpProofDiagnostics.MisplacedEnsuresAttributeId,
            SharpProofDiagnostics.ComplexityExceededId,
            SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId,
            SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId,
            SharpProofDiagnostics.SuggestZeroAllocationsId,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesId,
            SharpProofDiagnostics.SuggestExpectedComplexityId,
            SharpProofDiagnostics.SuggestExceptionContractId,
            SharpProofDiagnostics.SuggestEnsuresId,
            SharpProofDiagnostics.SuggestRequiresId);

        public override FixAllProvider? GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

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
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [EnforcePure] and [Pure] attributes",
                                c => RemoveAttributesMatchingAsync(document, root, declImpure,
                                    IsEnforcePureOrPureAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0002"),
                            diagnostic);
                    break;

                case SharpProofDiagnostics.MisplacedAttributeId:
                    if (TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedPurity))
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove misplaced purity attribute",
                                c => RemoveMisplacedAttributeAsync(document, root, misplacedPurity, c),
                                nameof(RemoveMisplacedAttributeAsync)),
                            diagnostic);
                    break;

                case SharpProofDiagnostics.MissingEnforcePureAttributeId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declMissing))
                    {
                        if (declMissing is PropertyDeclarationSyntax or IndexerDeclarationSyntax) break;

                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Add [EnforcePure] attribute",
                                c => AddEnforcePureAttributeAsync(document, root, declMissing, c),
                                nameof(AddEnforcePureAttributeAsync)),
                            diagnostic);
                    }

                    break;

                case SharpProofDiagnostics.ConflictingPurityAttributesId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start,
                            out var declConflict))
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove conflicting purity boundary attributes",
                                c => RemoveAttributesMatchingAsync(document, root, declConflict,
                                    IsConflictingPurityBoundaryAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0005"),
                            diagnostic);
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
                                c => RemoveAttributesMatchingAsync(document, root, declAllow,
                                    IsAllowSynchronizationAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0006b"),
                            diagnostic);
                    }

                    break;

                case SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId:
                    if (TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedAllow))
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove misplaced [AllowSynchronization] attribute",
                                c => RemoveMisplacedAttributeAsync(document, root, misplacedAllow, c),
                                nameof(RemoveMisplacedAttributeAsync) + "SP0007"),
                            diagnostic);
                    break;

                case SharpProofDiagnostics.RedundantAllowSynchronizationId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start,
                            out var declRedundant))
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [AllowSynchronization] attribute",
                                c => RemoveAttributesMatchingAsync(document, root, declRedundant,
                                    IsAllowSynchronizationAttribute, c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0008"),
                            diagnostic);
                    break;

                case SharpProofDiagnostics.AllocationInZeroAllocationMethodId:
                    RegisterRemoveContractAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove [ZeroAllocations] attribute",
                        IsZeroAllocationsAttribute,
                        "SP0013");
                    break;

                case SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId:
                    RegisterRemoveMisplacedAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove misplaced [ZeroAllocations] attribute",
                        "SP0014");
                    break;

                case SharpProofDiagnostics.CapabilityViolationId:
                case SharpProofDiagnostics.CapabilityUnknownId:
                    RegisterRemoveContractAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove [AllowedCapabilities] attribute",
                        IsAllowedCapabilitiesAttribute,
                        diagnostic.Id);
                    break;

                case SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId:
                    RegisterRemoveMisplacedAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove misplaced [AllowedCapabilities] attribute",
                        "SP0017");
                    break;

                case SharpProofDiagnostics.EnsuresNotProvenId:
                case SharpProofDiagnostics.EnsuresUnsupportedId:
                    RegisterRemoveContractAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove [Ensures] attribute",
                        IsEnsuresAttribute,
                        diagnostic.Id);
                    break;

                case SharpProofDiagnostics.MisplacedEnsuresAttributeId:
                    RegisterRemoveMisplacedAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove misplaced [Ensures] attribute",
                        "SP0020");
                    break;

                case SharpProofDiagnostics.ComplexityExceededId:
                case SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId:
                    RegisterRemoveContractAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove [ExpectedComplexity] attribute",
                        IsExpectedComplexityAttribute,
                        diagnostic.Id);
                    break;

                case SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId:
                    RegisterRemoveMisplacedAttributeCodeFix(
                        context,
                        document,
                        root,
                        diagnostic,
                        "Remove misplaced [ExpectedComplexity] attribute",
                        "SP0023");
                    break;

                case SharpProofDiagnostics.SuggestZeroAllocationsId:
                case SharpProofDiagnostics.SuggestAllowedCapabilitiesId:
                case SharpProofDiagnostics.SuggestExpectedComplexityId:
                case SharpProofDiagnostics.SuggestExceptionContractId:
                case SharpProofDiagnostics.SuggestEnsuresId:
                case SharpProofDiagnostics.SuggestRequiresId:
                    RegisterInferredContractCodeFix(context, document, root, diagnostic);
                    break;
            }
        }

        private void RegisterInferredContractCodeFix(
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic)
        {
            if (!diagnostic.Properties.TryGetValue(
                    SharpProofDiagnostics.SuggestedContractAttributeProperty,
                    out var attributeExpression) ||
                attributeExpression == null ||
                string.IsNullOrWhiteSpace(attributeExpression) ||
                !attributeExpression.StartsWith("global::SharpProof.Attributes.", StringComparison.Ordinal) ||
                !TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration) ||
                declaration is PropertyDeclarationSyntax or IndexerDeclarationSyntax or AccessorDeclarationSyntax)
                return;

            diagnostic.Properties.TryGetValue(
                SharpProofDiagnostics.SuggestedContractKindProperty,
                out var contractKind);
            var title = "Add inferred " +
                        (string.IsNullOrWhiteSpace(contractKind) ? "SharpProof" : contractKind) +
                        " contract";
            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => AddInferredContractAttributeAsync(
                        document,
                        root,
                        declaration,
                        attributeExpression,
                        cancellationToken),
                    nameof(AddInferredContractAttributeAsync) + diagnostic.Id),
                diagnostic);
        }

        private void RegisterRemoveMisplacedAttributeCodeFix(
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            string title,
            string equivalenceKeySuffix)
        {
            if (TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedAttribute))
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title,
                        c => RemoveMisplacedAttributeAsync(document, root, misplacedAttribute, c),
                        nameof(RemoveMisplacedAttributeAsync) + equivalenceKeySuffix),
                    diagnostic);
        }

        private void RegisterRemoveContractAttributeCodeFix(
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            string title,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            string equivalenceKeySuffix)
        {
            if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration))
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title,
                        c => RemoveContractAttributeAsync(document, root, diagnostic, declaration, shouldRemoveType, c),
                        nameof(RemoveContractAttributeAsync) + equivalenceKeySuffix),
                    diagnostic);
        }

        private static bool TryFindPurityTargetDeclaration(SyntaxNode root, int position, out SyntaxNode declaration)
        {
            declaration = null!;
            for (var node = root.FindToken(position).Parent; node != null; node = node.Parent)
                switch (node)
                {
                    case MethodDeclarationSyntax:
                    case ConstructorDeclarationSyntax:
                    case OperatorDeclarationSyntax:
                    case ConversionOperatorDeclarationSyntax:
                    case IndexerDeclarationSyntax:
                    case PropertyDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case LocalFunctionStatementSyntax:
                        declaration = node;
                        return true;
                }

            return false;
        }

        private static bool TryFindAttributeSyntax(SyntaxNode root, TextSpan span, out AttributeSyntax attribute)
        {
            attribute = null!;
            var node = root.FindNode(span, false, true);
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
                MemberDeclarationSyntax m => m.WithAttributeLists(lists),
                LocalFunctionStatementSyntax l => l.WithAttributeLists(lists),
                ParameterSyntax p => p.WithAttributeLists(lists),
                CompilationUnitSyntax u => u.WithAttributeLists(lists),
                _ => host
            };
        }

        private static SyntaxList<AttributeListSyntax> RemoveFromAttributeLists(SyntaxList<AttributeListSyntax> lists,
            AttributeSyntax remove)
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
            Func<INamedTypeSymbol?, bool> shouldRemoveType)
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
            return t.Name is "EnforcePureAttribute" or "PureAttribute";
        }

        private static bool IsConflictingPurityBoundaryAttribute(INamedTypeSymbol? t)
        {
            return t != null &&
                   t.Name is "PureAttribute" or "PureExternalAttribute" or "ImpureAttribute" &&
                   t.ContainingNamespace?.ToDisplayString() == "SharpProof.Attributes";
        }

        private static bool IsAllowSynchronizationAttribute(INamedTypeSymbol? t)
        {
            return t != null && t.Name == "AllowSynchronizationAttribute" &&
                   t.ContainingNamespace?.ToDisplayString() == "SharpProof.Attributes";
        }

        private static bool IsZeroAllocationsAttribute(INamedTypeSymbol? t)
        {
            return IsAttributeNamed(t, "ZeroAllocationsAttribute");
        }

        private static bool IsAllowedCapabilitiesAttribute(INamedTypeSymbol? t)
        {
            return IsAttributeNamed(t, "AllowedCapabilitiesAttribute");
        }

        private static bool IsEnsuresAttribute(INamedTypeSymbol? t)
        {
            return IsAttributeNamed(t, "EnsuresAttribute");
        }

        private static bool IsExpectedComplexityAttribute(INamedTypeSymbol? t)
        {
            return IsAttributeNamed(t, "ExpectedComplexityAttribute");
        }

        private static bool IsAttributeNamed(INamedTypeSymbol? t, string attributeTypeName)
        {
            return t != null && t.Name == attributeTypeName;
        }

        private Task<Document> RemoveMisplacedAttributeAsync(Document document, SyntaxNode root, AttributeSyntax attr,
            CancellationToken cancellationToken)
        {
            var host = GetHostForAttribute(attr);
            if (host == null)
                return Task.FromResult(document);
            var newHost = RemoveAttributeFromHost(host, attr);
            if (ReferenceEquals(host, newHost))
                return Task.FromResult(document);
            var newRoot = root.ReplaceNode(host, newHost);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }

        private async Task<Document> RemoveContractAttributeAsync(
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SyntaxNode declaration,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            CancellationToken cancellationToken)
        {
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model != null)
                foreach (var location in GetDiagnosticLocations(diagnostic))
                {
                    if (!location.IsInSource) continue;

                    if (TryFindAttributeSyntax(root, location.SourceSpan, out var attribute) &&
                        shouldRemoveType(GetAttributeClass(model, attribute)))
                        return await RemoveMisplacedAttributeAsync(document, root, attribute, cancellationToken)
                            .ConfigureAwait(false);
                }

            return await RemoveAttributesMatchingAsync(document, root, declaration, shouldRemoveType, cancellationToken)
                .ConfigureAwait(false);
        }

        private static IEnumerable<Location> GetDiagnosticLocations(Diagnostic diagnostic)
        {
            yield return diagnostic.Location;
            foreach (var location in diagnostic.AdditionalLocations) yield return location;
        }

        private async Task<Document> RemoveAttributesMatchingAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
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
            Func<INamedTypeSymbol?, bool> shouldRemoveType)
        {
            foreach (var list in lists)
                foreach (var attr in list.Attributes)
                    if (shouldRemoveType(GetAttributeClass(model, attr)))
                        return true;

            return false;
        }

        private async Task<Document> AddEnforcePureAttributeAsync(Document document, SyntaxNode root,
            SyntaxNode declaration, CancellationToken cancellationToken)
        {
            const string ns = "SharpProof.Attributes";
            var lists = GetAttributeLists(declaration);
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model != null)
                foreach (var list in lists)
                    foreach (var attr in list.Attributes)
                    {
                        var c = GetAttributeClass(model, attr);
                        if (c?.Name == "EnforcePureAttribute" && c.ContainingNamespace?.ToDisplayString() == ns)
                            return document;
                    }

            var compilationUnit = declaration.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();
            var useShortName = compilationUnit != null &&
                               compilationUnit.Usings.Any(u => string.Equals(u.Name?.ToString(),
                                   "SharpProof.Attributes", StringComparison.Ordinal));
            var attributeName = useShortName
                ? "EnforcePure"
                : "global::SharpProof.Attributes.EnforcePure";
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var lineEnding = sourceText.ToString().IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var newAttrList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeName))))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(lineEnding));

            var newDecl = WithAttributeLists(declaration, lists.Insert(0, newAttrList));
            var newRoot = root.ReplaceNode(declaration, newDecl);
            var updatedDocument = document.WithSyntaxRoot(newRoot);
            var updatedText = await updatedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var normalizedText = NormalizeLineEndings(updatedText.ToString(), lineEnding);
            if (string.Equals(normalizedText, updatedText.ToString(), StringComparison.Ordinal)) return updatedDocument;

            return updatedDocument.WithText(SourceText.From(normalizedText, updatedText.Encoding));
        }

        private async Task<Document> AddInferredContractAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            string attributeExpression,
            CancellationToken cancellationToken)
        {
            const string attributeNamespace = "global::SharpProof.Attributes.";
            if (string.IsNullOrWhiteSpace(attributeExpression) ||
                !attributeExpression.StartsWith(attributeNamespace, StringComparison.Ordinal) ||
                attributeExpression.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return document;

            var compilationUnit = declaration.Ancestors().OfType<CompilationUnitSyntax>().FirstOrDefault();
            var useShortName = compilationUnit != null &&
                               compilationUnit.Usings.Any(u => string.Equals(u.Name?.ToString(),
                                   "SharpProof.Attributes", StringComparison.Ordinal));
            if (useShortName) attributeExpression = attributeExpression.Replace(attributeNamespace, string.Empty);

            var parsedUnit = SyntaxFactory.ParseCompilationUnit(
                "[" + attributeExpression + "] class __SharpProofAttributePlaceholder { }");
            var newAttributeList = parsedUnit.Members
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault()?
                .AttributeLists
                .FirstOrDefault();
            if (newAttributeList == null ||
                newAttributeList.Attributes.Count != 1 ||
                newAttributeList.ContainsDiagnostics)
                return document;

            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var lineEnding = sourceText.ToString().IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var indentation = SyntaxFactory.TriviaList(
                declaration.GetLeadingTrivia()
                    .Reverse()
                    .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    .Reverse());
            newAttributeList = newAttributeList
                .WithoutTrivia()
                .WithLeadingTrivia(indentation)
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(lineEnding));

            var lists = GetAttributeLists(declaration);
            var newDeclaration = WithAttributeLists(declaration, lists.Insert(0, newAttributeList));
            var newRoot = root.ReplaceNode(declaration, newDeclaration);
            var updatedDocument = document.WithSyntaxRoot(newRoot);
            var updatedText = await updatedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var normalizedText = NormalizeLineEndings(updatedText.ToString(), lineEnding);
            if (string.Equals(normalizedText, updatedText.ToString(), StringComparison.Ordinal)) return updatedDocument;

            return updatedDocument.WithText(SourceText.From(normalizedText, updatedText.Encoding));
        }

        private static string NormalizeLineEndings(string text, string lineEnding)
        {
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", lineEnding);
        }
    }
}
