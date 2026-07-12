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
using SharpProof.Analyzer.Configuration;

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
            SharpProofDiagnostics.MisplacedRequiresAttributeId,
            SharpProofDiagnostics.SuggestZeroAllocationsId,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesId,
            SharpProofDiagnostics.SuggestExpectedComplexityId,
            SharpProofDiagnostics.SuggestExceptionContractId,
            SharpProofDiagnostics.SuggestEnsuresId,
            SharpProofDiagnostics.SuggestRequiresId,
            SharpProofDiagnostics.SuggestNullableContractId,
            SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId);

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
            var configuration = AnalyzerConfiguration.FromOptions(document.Project.AnalyzerOptions);
            var attributePolicy = SharpProofAttributeIdentityPolicy.Create(configuration.AttributeStubNamespaces);

            switch (diagnostic.Id)
            {
                case SharpProofDiagnostics.PurityNotVerifiedId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declImpure))
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [EnforcePure] and [Pure] attributes",
                                c => RemoveAttributesMatchingAsync(document, root, declImpure,
                                    type => IsEnforcePureOrPureAttribute(attributePolicy, type), c),
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
                                c => AddEnforcePureAttributeAsync(document, root, declMissing, attributePolicy, c),
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
                                    type => IsConflictingPurityBoundaryAttribute(attributePolicy, type), c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0005"),
                            diagnostic);
                    break;

                case SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declAllow))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Add [EnforcePure] attribute",
                                c => AddEnforcePureAttributeAsync(document, root, declAllow, attributePolicy, c),
                                nameof(AddEnforcePureAttributeAsync) + "SP0006a"),
                            diagnostic);
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [AllowSynchronization] attribute",
                                c => RemoveAttributesMatchingAsync(document, root, declAllow,
                                    type => IsAllowSynchronizationAttribute(attributePolicy, type), c),
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
                                    type => IsAllowSynchronizationAttribute(attributePolicy, type), c),
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
                        type => IsZeroAllocationsAttribute(attributePolicy, type),
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
                        type => IsAllowedCapabilitiesAttribute(attributePolicy, type),
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
                        type => IsEnsuresAttribute(attributePolicy, type),
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
                        type => IsExpectedComplexityAttribute(attributePolicy, type),
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

                case SharpProofDiagnostics.MisplacedRequiresAttributeId:
                    if (TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedRequires))
                    {
                        if (CanMoveAttributeToGetter(misplacedRequires))
                            context.RegisterCodeFix(
                                CodeAction.Create(
                                    "Move [Requires] attribute to getter",
                                    c => MoveAttributeToGetterAsync(document, root, misplacedRequires, c),
                                    nameof(MoveAttributeToGetterAsync) + "SP0029"),
                                diagnostic);

                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove misplaced [Requires] attribute",
                                c => RemoveMisplacedAttributeAsync(document, root, misplacedRequires, c),
                                nameof(RemoveMisplacedAttributeAsync) + "SP0029"),
                            diagnostic);
                    }

                    break;

                case SharpProofDiagnostics.SuggestZeroAllocationsId:
                case SharpProofDiagnostics.SuggestAllowedCapabilitiesId:
                case SharpProofDiagnostics.SuggestExpectedComplexityId:
                case SharpProofDiagnostics.SuggestExceptionContractId:
                case SharpProofDiagnostics.SuggestEnsuresId:
                case SharpProofDiagnostics.SuggestRequiresId:
                case SharpProofDiagnostics.SuggestNullableContractId:
                    RegisterInferredContractCodeFix(context, document, root, diagnostic);
                    break;

                case SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId:
                    if (TryFindNullForgivingExpression(root, diagnostic.Location.SourceSpan, out var suppression))
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove unnecessary null-forgiving operator",
                                _ => Task.FromResult(document.WithSyntaxRoot(
                                    root.ReplaceNode(
                                        suppression,
                                        suppression.Operand.WithTriviaFrom(suppression)))),
                                "RemoveUnnecessaryNullForgivingOperator"),
                            diagnostic);
                    break;
            }
        }

        private static bool TryFindNullForgivingExpression(
            SyntaxNode root,
            TextSpan span,
            out PostfixUnaryExpressionSyntax suppression)
        {
            for (var node = root.FindToken(span.Start).Parent; node != null; node = node.Parent)
                if (node is PostfixUnaryExpressionSyntax postfix &&
                    postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    suppression = postfix;
                    return true;
                }

            suppression = null!;
            return false;
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
                (!attributeExpression.StartsWith("global::SharpProof.Attributes.", StringComparison.Ordinal) &&
                 !attributeExpression.StartsWith(
                     "global::System.Diagnostics.CodeAnalysis.",
                     StringComparison.Ordinal)) ||
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
                    cancellationToken => contractKind != null &&
                                         contractKind.StartsWith("nullable-", StringComparison.Ordinal)
                        ? AddInferredNullableContractAttributeAsync(
                            document,
                            root,
                            declaration,
                            attributeExpression,
                            contractKind,
                            cancellationToken)
                        : AddInferredContractAttributeAsync(
                            document,
                            root,
                            declaration,
                            attributeExpression,
                            cancellationToken),
                    nameof(AddInferredContractAttributeAsync) + diagnostic.Id),
                diagnostic);
        }

        private static Task<Document> AddInferredNullableContractAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            string attributeExpression,
            string contractKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (contractKind == "nullable-return")
            {
                var parsed = SyntaxFactory.ParseCompilationUnit(
                    "class __SharpProofPlaceholder { [return: " + attributeExpression +
                    "] object M() => null; }");
                var list = parsed.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()?.AttributeLists.FirstOrDefault();
                if (list == null || list.ContainsDiagnostics) return Task.FromResult(document);

                var indentation = SyntaxFactory.TriviaList(
                    declaration.GetLeadingTrivia()
                        .Reverse()
                        .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                        .Reverse());
                list = list.WithoutTrivia()
                    .WithLeadingTrivia(indentation)
                    .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

                var existing = GetAttributeLists(declaration);
                var replacement = WithAttributeLists(
                    declaration,
                    existing.Insert(0, list));
                return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(declaration, replacement)));
            }

            const string parameterPrefix = "nullable-parameter:";
            if (!contractKind.StartsWith(parameterPrefix, StringComparison.Ordinal))
                return Task.FromResult(document);

            var parameterName = contractKind.Substring(parameterPrefix.Length);
            var parameter = declaration.DescendantNodes()
                .OfType<ParameterSyntax>()
                .FirstOrDefault(candidate => candidate.Identifier.ValueText == parameterName);
            if (parameter == null) return Task.FromResult(document);

            var parameterUnit = SyntaxFactory.ParseCompilationUnit(
                "class __SharpProofPlaceholder { void M([" + attributeExpression + "] object value) { } }");
            var attributeList = parameterUnit.DescendantNodes().OfType<ParameterSyntax>()
                .FirstOrDefault()?.AttributeLists.FirstOrDefault();
            if (attributeList == null || attributeList.ContainsDiagnostics) return Task.FromResult(document);

            var updatedParameter = parameter.AddAttributeLists(
                attributeList.WithoutTrivia().WithTrailingTrivia(SyntaxFactory.Space));
            return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(parameter, updatedParameter)));
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

        private static bool CanMoveAttributeToGetter(AttributeSyntax attribute)
        {
            return GetHostForAttribute(attribute) switch
            {
                PropertyDeclarationSyntax property =>
                    property.ExpressionBody != null ||
                    property.AccessorList?.Accessors.Any(static accessor =>
                        accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) == true,
                IndexerDeclarationSyntax indexer =>
                    indexer.ExpressionBody != null ||
                    indexer.AccessorList?.Accessors.Any(static accessor =>
                        accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) == true,
                _ => false
            };
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
                AccessorDeclarationSyntax a => a.AttributeLists,
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
                AccessorDeclarationSyntax a => a.WithAttributeLists(lists),
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

        private static bool IsEnforcePureOrPureAttribute(
            SharpProofAttributeIdentityPolicy policy,
            INamedTypeSymbol? type)
        {
            return policy.IsAccepted(type, "EnforcePureAttribute") ||
                   policy.IsAccepted(type, "PureAttribute");
        }

        private static bool IsConflictingPurityBoundaryAttribute(
            SharpProofAttributeIdentityPolicy policy,
            INamedTypeSymbol? type)
        {
            return policy.IsAccepted(type, "PureAttribute") ||
                   policy.IsAccepted(type, "PureExternalAttribute") ||
                   policy.IsAccepted(type, "ImpureAttribute");
        }

        private static bool IsAllowSynchronizationAttribute(
            SharpProofAttributeIdentityPolicy policy,
            INamedTypeSymbol? type)
        {
            return policy.IsAccepted(type, "AllowSynchronizationAttribute");
        }

        private static bool IsZeroAllocationsAttribute(
            SharpProofAttributeIdentityPolicy policy,
            INamedTypeSymbol? type)
        {
            return policy.IsAccepted(type, "ZeroAllocationsAttribute");
        }

        private static bool IsAllowedCapabilitiesAttribute(
            SharpProofAttributeIdentityPolicy policy,
            INamedTypeSymbol? type)
        {
            return policy.IsAccepted(type, "AllowedCapabilitiesAttribute");
        }

        private static bool IsEnsuresAttribute(
            SharpProofAttributeIdentityPolicy policy,
            INamedTypeSymbol? type)
        {
            return policy.IsAccepted(type, "EnsuresAttribute");
        }

        private static bool IsExpectedComplexityAttribute(
            SharpProofAttributeIdentityPolicy policy,
            INamedTypeSymbol? type)
        {
            return policy.IsAccepted(type, "ExpectedComplexityAttribute");
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

        private async Task<Document> MoveAttributeToGetterAsync(
            Document document,
            SyntaxNode root,
            AttributeSyntax attribute,
            CancellationToken cancellationToken)
        {
            var host = GetHostForAttribute(attribute);
            if (host is not PropertyDeclarationSyntax && host is not IndexerDeclarationSyntax) return document;

            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var lineEnding = sourceText.ToString().IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var hostWithoutAttribute = RemoveAttributeFromHost(host, attribute);
            var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(attribute.WithoutTrivia()));
            var updatedHost = AddAttributeToGetter(hostWithoutAttribute, attributeList, lineEnding);
            if (updatedHost == null) return document;

            var updatedDocument = document.WithSyntaxRoot(root.ReplaceNode(host, updatedHost));
            var updatedText = await updatedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var normalizedText = NormalizeLineEndings(updatedText.ToString(), lineEnding);
            return updatedDocument.WithText(SourceText.From(normalizedText, updatedText.Encoding));
        }

        private static SyntaxNode? AddAttributeToGetter(
            SyntaxNode host,
            AttributeListSyntax attributeList,
            string lineEnding)
        {
            switch (host)
            {
                case PropertyDeclarationSyntax property:
                {
                    if (property.AccessorList != null)
                    {
                        var getter = property.AccessorList.Accessors.FirstOrDefault(static accessor =>
                            accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                        if (getter == null) return null;

                        attributeList = FormatAttributeBeforeExistingGetter(attributeList, getter, lineEnding);
                        return property.ReplaceNode(
                            getter,
                            getter.WithAttributeLists(getter.AttributeLists.Insert(0, attributeList)));
                    }

                    if (property.ExpressionBody == null) return null;

                    var expressionGetter = CreateExpressionBodiedGetter(
                        property.ExpressionBody,
                        property.SemicolonToken,
                        attributeList,
                        GetIndentation(property),
                        lineEnding);
                    var propertyWithAccessor = property
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default);
                    propertyWithAccessor = RemoveTrailingTriviaFromLastToken(propertyWithAccessor);
                    return propertyWithAccessor.WithAccessorList(CreateAccessorList(expressionGetter));
                }
                case IndexerDeclarationSyntax indexer:
                {
                    if (indexer.AccessorList != null)
                    {
                        var getter = indexer.AccessorList.Accessors.FirstOrDefault(static accessor =>
                            accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                        if (getter == null) return null;

                        attributeList = FormatAttributeBeforeExistingGetter(attributeList, getter, lineEnding);
                        return indexer.ReplaceNode(
                            getter,
                            getter.WithAttributeLists(getter.AttributeLists.Insert(0, attributeList)));
                    }

                    if (indexer.ExpressionBody == null) return null;

                    var expressionGetter = CreateExpressionBodiedGetter(
                        indexer.ExpressionBody,
                        indexer.SemicolonToken,
                        attributeList,
                        GetIndentation(indexer),
                        lineEnding);
                    var indexerWithAccessor = indexer
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default);
                    indexerWithAccessor = RemoveTrailingTriviaFromLastToken(indexerWithAccessor);
                    return indexerWithAccessor.WithAccessorList(CreateAccessorList(expressionGetter));
                }
                default:
                    return null;
            }
        }

        private static AccessorDeclarationSyntax CreateExpressionBodiedGetter(
            ArrowExpressionClauseSyntax expressionBody,
            SyntaxToken semicolonToken,
            AttributeListSyntax attributeList,
            string hostIndentation,
            string lineEnding)
        {
            var accessorIndentation = hostIndentation + "    ";
            attributeList = attributeList.WithTrailingTrivia(
                LineBreakAndIndent(lineEnding, accessorIndentation));
            expressionBody = expressionBody.WithArrowToken(
                expressionBody.ArrowToken.WithLeadingTrivia(SyntaxFactory.Space));
            var semicolonTrailingTrivia = PreserveInlineTriviaBeforeLineBreak(
                semicolonToken.TrailingTrivia,
                lineEnding,
                hostIndentation);
            return SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithAttributeLists(SyntaxFactory.SingletonList(attributeList))
                .WithExpressionBody(expressionBody)
                .WithSemicolonToken(
                    semicolonToken
                        .WithLeadingTrivia(default(SyntaxTriviaList))
                        .WithTrailingTrivia(semicolonTrailingTrivia));
        }

        private static AccessorListSyntax CreateAccessorList(AccessorDeclarationSyntax getter)
        {
            var semicolonToken = getter.SemicolonToken;
            var trailingTrivia = semicolonToken.TrailingTrivia;
            var indentation = trailingTrivia.LastOrDefault(static trivia =>
                trivia.IsKind(SyntaxKind.WhitespaceTrivia)).ToFullString();
            var lineEnding = trailingTrivia.FirstOrDefault(static trivia =>
                trivia.IsKind(SyntaxKind.EndOfLineTrivia)).ToFullString();
            if (lineEnding.Length == 0) lineEnding = "\n";

            return SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(getter))
                .WithOpenBraceToken(
                    SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                        .WithLeadingTrivia(LineBreakAndIndent(lineEnding, indentation))
                        .WithTrailingTrivia(LineBreakAndIndent(lineEnding, indentation + "    ")))
                .WithCloseBraceToken(
                    SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                        .WithTrailingTrivia(SyntaxFactory.EndOfLine(lineEnding)));
        }

        private static TNode RemoveTrailingTriviaFromLastToken<TNode>(TNode node)
            where TNode : SyntaxNode
        {
            var lastToken = node.GetLastToken();
            return (TNode)node.ReplaceToken(lastToken, lastToken.WithTrailingTrivia(default(SyntaxTriviaList)));
        }

        private static AttributeListSyntax FormatAttributeBeforeExistingGetter(
            AttributeListSyntax attributeList,
            AccessorDeclarationSyntax getter,
            string lineEnding)
        {
            var indentation = SyntaxFactory.TriviaList(
                getter.GetLeadingTrivia()
                    .Reverse()
                    .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    .Reverse());
            return attributeList
                .WithLeadingTrivia(indentation)
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(lineEnding));
        }

        private static string GetIndentation(SyntaxNode node)
        {
            return string.Concat(
                node.GetLeadingTrivia()
                    .Reverse()
                    .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    .Reverse()
                    .Select(static trivia => trivia.ToFullString()));
        }

        private static SyntaxTriviaList LineBreakAndIndent(string lineEnding, string indentation)
        {
            var trivia = new List<SyntaxTrivia> { SyntaxFactory.EndOfLine(lineEnding) };
            if (indentation.Length != 0) trivia.Add(SyntaxFactory.Whitespace(indentation));
            return SyntaxFactory.TriviaList(trivia);
        }

        private static SyntaxTriviaList PreserveInlineTriviaBeforeLineBreak(
            SyntaxTriviaList originalTrivia,
            string lineEnding,
            string indentation)
        {
            var inlineTrivia = originalTrivia
                .TakeWhile(static trivia => !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                .ToList();
            if (!inlineTrivia.Any(static trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia)))
                inlineTrivia.Clear();

            inlineTrivia.AddRange(LineBreakAndIndent(lineEnding, indentation));
            return SyntaxFactory.TriviaList(inlineTrivia);
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
            var newDecl = FilterDeclarationAndAccessorAttributes(
                declaration,
                model,
                shouldRemoveType,
                out var removedAny);
            if (!removedAny) return document;

            var newRoot = root.ReplaceNode(declaration, newDecl);
            return document.WithSyntaxRoot(newRoot);
        }

        private static SyntaxNode FilterDeclarationAndAccessorAttributes(
            SyntaxNode declaration,
            SemanticModel model,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            out bool removedAny)
        {
            removedAny = false;
            var declarationLists = GetAttributeLists(declaration);
            if (FilterAttributeListsRemovesAny(declarationLists, model, shouldRemoveType))
            {
                declaration = WithAttributeLists(
                    declaration,
                    FilterAttributeLists(declarationLists, model, shouldRemoveType));
                removedAny = true;
            }

            if (declaration is not BasePropertyDeclarationSyntax property || property.AccessorList == null)
                return declaration;

            var accessors = new List<AccessorDeclarationSyntax>(property.AccessorList.Accessors.Count);
            foreach (var accessor in property.AccessorList.Accessors)
            {
                var lists = accessor.AttributeLists;
                if (!FilterAttributeListsRemovesAny(lists, model, shouldRemoveType))
                {
                    accessors.Add(accessor);
                    continue;
                }

                accessors.Add(accessor.WithAttributeLists(FilterAttributeLists(lists, model, shouldRemoveType)));
                removedAny = true;
            }

            if (!removedAny) return declaration;

            var accessorList = property.AccessorList.WithAccessors(SyntaxFactory.List(accessors));
            return declaration switch
            {
                PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.WithAccessorList(accessorList),
                IndexerDeclarationSyntax indexerDeclaration => indexerDeclaration.WithAccessorList(accessorList),
                _ => declaration
            };
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

        private async Task<Document> AddEnforcePureAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CancellationToken cancellationToken)
        {
            var lists = GetAttributeLists(declaration);
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model != null)
                foreach (var list in lists)
                    foreach (var attr in list.Attributes)
                    {
                        var c = GetAttributeClass(model, attr);
                        if (attributePolicy.IsAccepted(c, "EnforcePureAttribute"))
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
