namespace SharpProof;

    public sealed partial class SharpProofCodeFixProvider
    {
        private void RegisterSimpleRemovalCodeFix(
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            SimpleRemovalRegistration registration)
        {
            if (registration.Operation == SimpleRemovalOperation.MisplacedAttribute)
            {
                if (!TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedAttribute)) return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        registration.Title,
                        c => RemoveMisplacedAttributeAsync(document, root, misplacedAttribute, c),
                        registration.EquivalenceKey),
                    diagnostic);
                return;
            }

            if (!TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration)) return;

            var shouldRemoveType = AcceptedAttribute(attributePolicy, registration.AttributeTypeNames);
            Func<CancellationToken, Task<Document>> apply = registration.Operation ==
                SimpleRemovalOperation.DeclarationAndAccessors
                ? c => RemoveAttributesMatchingAsync(document, root, declaration, shouldRemoveType, c)
                : c => RemoveContractAttributeAsync(
                    document, root, diagnostic, declaration, shouldRemoveType, c);
            context.RegisterCodeFix(
                CodeAction.Create(registration.Title, apply, registration.EquivalenceKey),
                diagnostic);
        }

        internal static bool TryFindPurityTargetDeclaration(SyntaxNode root, int position, out SyntaxNode declaration)
        {
            declaration = null!;
            for (var node = root.FindToken(position).Parent; node != null; node = node.Parent)
                if (node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or OperatorDeclarationSyntax or
                    ConversionOperatorDeclarationSyntax or IndexerDeclarationSyntax or PropertyDeclarationSyntax or
                    AccessorDeclarationSyntax or LocalFunctionStatementSyntax)
                {
                    declaration = node;
                    return true;
                }

            return false;
        }

        internal static bool TryFindAttributeSyntax(SyntaxNode root, TextSpan span, out AttributeSyntax attribute)
        {
            var node = root.FindNode(span, false, true);
            attribute = node.FirstAncestorOrSelf<AttributeSyntax>() ?? (node as AttributeSyntax)!;
            return attribute != null;
        }

        private static SyntaxNode? GetHostForAttribute(AttributeSyntax attr) =>
            (attr.Parent as AttributeListSyntax)?.Parent;

        internal static bool CanMoveAttributeToGetter(AttributeSyntax attribute) =>
            AttributeTargetSyntaxFacts.IsGetterAliasTarget(GetHostForAttribute(attribute));

        private static SyntaxNode RemoveAttributeFromHost(
            SyntaxNode host,
            AttributeSyntax attrToRemove,
            bool preserveLeadingTrivia = true)
        {
            if (attrToRemove.Parent is not AttributeListSyntax list) return host;

            var nodeToRemove = list.Attributes.Count == 1
                ? (SyntaxNode)list
                : attrToRemove;
            var options = preserveLeadingTrivia && HasSignificantTrivia(nodeToRemove.GetLeadingTrivia())
                ? SyntaxRemoveOptions.KeepLeadingTrivia
                : SyntaxRemoveOptions.KeepNoTrivia;
            return host.RemoveNode(nodeToRemove, options) ?? host;
        }

        private static bool HasSignificantTrivia(SyntaxTriviaList trivia)
        {
            return trivia.Any(static item =>
                !item.IsKind(SyntaxKind.WhitespaceTrivia) &&
                !item.IsKind(SyntaxKind.EndOfLineTrivia));
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

        private static SyntaxNode RemoveAttributesMatchingFromDeclarationAndAccessors(
            SyntaxNode declaration,
            SemanticModel model,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            out bool removedAny)
        {
            var nodesToRemove = new List<SyntaxNode>();
            var hosts = new List<SyntaxNode> { declaration };
            if (declaration is BasePropertyDeclarationSyntax { AccessorList: { } accessorList })
                hosts.AddRange(accessorList.Accessors);

            foreach (var host in hosts)
            foreach (var list in GetAttributeLists(host))
            {
                var matching = list.Attributes
                    .Where(attribute => shouldRemoveType(GetAttributeClass(model, attribute)))
                    .ToArray();
                if (matching.Length == 0) continue;

                if (matching.Length == list.Attributes.Count)
                    nodesToRemove.Add(list);
                else
                    nodesToRemove.AddRange(matching);
            }

            removedAny = nodesToRemove.Count != 0;
            if (!removedAny) return declaration;

            var trackedDeclaration = declaration.TrackNodes(nodesToRemove);
            foreach (var original in nodesToRemove)
            {
                var current = trackedDeclaration.GetCurrentNode(original);
                if (current == null) continue;

                var options = HasSignificantTrivia(current.GetLeadingTrivia())
                    ? SyntaxRemoveOptions.KeepLeadingTrivia
                    : SyntaxRemoveOptions.KeepNoTrivia;
                trackedDeclaration = trackedDeclaration.RemoveNode(current, options) ?? trackedDeclaration;
            }

            return trackedDeclaration;
        }

        private static INamedTypeSymbol? GetAttributeClass(SemanticModel model, AttributeSyntax attributeSyntax) =>
            model.GetSymbolInfo(attributeSyntax).Symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType,
                INamedTypeSymbol type => type,
                _ => null
            };

        internal static Func<INamedTypeSymbol?, bool> AcceptedAttribute(
            SharpProofAttributeIdentityPolicy policy,
            params string[] attributeTypeNames) =>
            type => attributeTypeNames.Any(name => policy.IsAccepted(type, name));

        internal static Task<Document> RemoveMisplacedAttributeAsync(Document document, SyntaxNode root, AttributeSyntax attr,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var host = GetHostForAttribute(attr);
            if (host == null)
                return Task.FromResult(document);
            var newHost = RemoveAttributeFromHost(host, attr);
            if (ReferenceEquals(host, newHost))
                return Task.FromResult(document);
            var newRoot = root.ReplaceNode(host, newHost);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }

        internal static async Task<Document> MoveAttributeToGetterAsync(
            Document document,
            SyntaxNode root,
            AttributeSyntax attribute,
            CancellationToken cancellationToken)
        {
            var host = GetHostForAttribute(attribute);
            if (host is not PropertyDeclarationSyntax && host is not IndexerDeclarationSyntax) return document;

            var hostWithoutAttribute = RemoveAttributeFromHost(host, attribute, preserveLeadingTrivia: false);
            var sourceAttributeList = (AttributeListSyntax)attribute.Parent!;
            var attributeList = sourceAttributeList.Attributes.Count == 1
                ? sourceAttributeList.WithAttributes(SyntaxFactory.SingletonSeparatedList(attribute))
                : SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
            attributeList = attributeList
                .WithTarget(null)
                .WithAdditionalAnnotations(Formatter.Annotation);
            var updatedHost = AddAttributeToGetter(hostWithoutAttribute, attributeList);
            if (updatedHost == null) return document;

            return await Formatter.FormatAsync(
                    document.WithSyntaxRoot(root.ReplaceNode(host, updatedHost)),
                    Formatter.Annotation,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static SyntaxNode? AddAttributeToGetter(
            SyntaxNode host,
            AttributeListSyntax attributeList)
        {
            return host switch
            {
                PropertyDeclarationSyntax property => AddAttributeToGetter(
                    property,
                    property.AccessorList,
                    property.ExpressionBody,
                    property.SemicolonToken,
                    attributeList,
                    static (declaration, accessorList) => declaration.WithAccessorList(accessorList),
                    static declaration => declaration
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)),
                IndexerDeclarationSyntax indexer => AddAttributeToGetter(
                    indexer,
                    indexer.AccessorList,
                    indexer.ExpressionBody,
                    indexer.SemicolonToken,
                    attributeList,
                    static (declaration, accessorList) => declaration.WithAccessorList(accessorList),
                    static declaration => declaration
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)),
                _ => null
            };
        }

        private static TDeclaration? AddAttributeToGetter<TDeclaration>(
            TDeclaration declaration,
            AccessorListSyntax? accessorList,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken,
            AttributeListSyntax attributeList,
            Func<TDeclaration, AccessorListSyntax, TDeclaration> withAccessorList,
            Func<TDeclaration, TDeclaration> withoutExpressionBody)
            where TDeclaration : SyntaxNode
        {
            if (accessorList != null)
            {
                var getter = accessorList.Accessors.FirstOrDefault(static accessor =>
                    accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (getter == null) return null;

                var updatedGetter = getter.WithAttributeLists(
                        getter.AttributeLists.Insert(0, attributeList))
                    .WithAdditionalAnnotations(Formatter.Annotation);
                return withAccessorList(
                    declaration,
                    accessorList.WithAccessors(accessorList.Accessors.Replace(getter, updatedGetter)))
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }

            if (expressionBody == null) return null;

            var expressionGetter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithAttributeLists(SyntaxFactory.SingletonList(attributeList))
                .WithExpressionBody(expressionBody)
                .WithSemicolonToken(semicolonToken)
                .WithAdditionalAnnotations(Formatter.Annotation);
            var updatedDeclaration = withoutExpressionBody(declaration)
                .WithAdditionalAnnotations(Formatter.Annotation);
            return withAccessorList(
                updatedDeclaration,
                SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(expressionGetter))
                    .WithOpenBraceToken(
                        SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
                    .WithAdditionalAnnotations(Formatter.Annotation));
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
                foreach (var location in new[] { diagnostic.Location }.Concat(diagnostic.AdditionalLocations))
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

        internal async Task<Document> RemoveAttributesMatchingAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            CancellationToken cancellationToken)
        {
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model == null)
                return document;
            var newDecl = RemoveAttributesMatchingFromDeclarationAndAccessors(
                declaration,
                model,
                shouldRemoveType,
                out var removedAny);
            if (!removedAny) return document;

            var newRoot = root.ReplaceNode(declaration, newDecl);
            return document.WithSyntaxRoot(newRoot);
        }

        internal async Task<Document> AddEnforcePureAttributeAsync(
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

            var officialAttribute = model?.Compilation.GetTypeByMetadataName(
                "SharpProof.Attributes.EnforcePureAttribute");
            var useShortName = model != null &&
                               officialAttribute != null &&
                               HasUnaliasedSharpProofAttributesUsing(declaration) &&
                               IsUnambiguousAttributeName(
                                   model,
                                   declaration.SpanStart,
                                   "EnforcePure",
                                   officialAttribute);
            var attributeName = useShortName
                ? "EnforcePure"
                : "global::SharpProof.Attributes.EnforcePure";
            var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
            var newAttrList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeName))));

            return InsertFormattedAttributeList(document, root, declaration, newAttrList, lineEnding);
        }

}
