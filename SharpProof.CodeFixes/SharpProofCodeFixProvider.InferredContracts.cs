namespace SharpProof;

    public sealed partial class SharpProofCodeFixProvider {
        internal void RegisterInferredContractCodeFix(
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic) {
            if (!diagnostic.Properties.TryGetValue(
                    DiagnosticPropertyNames.SuggestedContractAttributeProperty,
                out var attributeExpression) ||
                attributeExpression == null ||
                string.IsNullOrWhiteSpace(attributeExpression) ||
                (!attributeExpression.StartsWith("global::SharpProof.Attributes.", StringComparison.Ordinal) &&
                 !attributeExpression.StartsWith(
                     "global::System.Diagnostics.CodeAnalysis.",
                     StringComparison.Ordinal)) ||
                !TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration) ||
                declaration is PropertyDeclarationSyntax or IndexerDeclarationSyntax)
                return;

            diagnostic.Properties.TryGetValue(
                DiagnosticPropertyNames.SuggestedContractKindProperty,
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

        private static async Task<Document> AddInferredNullableContractAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            string attributeExpression,
            string contractKind,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            if (contractKind == "nullable-return") {
                var parsed = SyntaxFactory.ParseCompilationUnit(
                    "class __SharpProofPlaceholder { [return: " + attributeExpression +
                    "] object M() => null; }");
                var list = parsed.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()?.AttributeLists.FirstOrDefault();
                if (list == null || list.ContainsDiagnostics) return document;

                var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
                return InsertFormattedAttributeList(
                    document,
                    root,
                    declaration,
                    list.WithoutTrivia(),
                    lineEnding);
            }

            const string parameterPrefix = "nullable-parameter:";
            if (!contractKind.StartsWith(parameterPrefix, StringComparison.Ordinal))
                return document;

            var parameterName = contractKind.Substring(parameterPrefix.Length);
            var parameter = declaration.DescendantNodes()
                .OfType<ParameterSyntax>()
                .FirstOrDefault(candidate => candidate.Identifier.ValueText == parameterName);
            if (parameter == null) {
                if (parameterName != "value" ||
                    declaration is not AccessorDeclarationSyntax accessor ||
                    !accessor.IsKind(SyntaxKind.SetAccessorDeclaration) &&
                    !accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
                    return document;

                var accessorUnit = SyntaxFactory.ParseCompilationUnit(
                    "class __SharpProofPlaceholder { object P { [param: " + attributeExpression +
                    "] set { } } }");
                var accessorAttributeList = accessorUnit.DescendantNodes()
                    .OfType<AccessorDeclarationSyntax>()
                    .FirstOrDefault()?.AttributeLists.FirstOrDefault();
                if (accessorAttributeList == null || accessorAttributeList.ContainsDiagnostics)
                    return document;

                var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
                return InsertFormattedAttributeList(
                    document,
                    root,
                    accessor,
                    accessorAttributeList.WithoutTrivia(),
                    lineEnding);
            }

            var parameterUnit = SyntaxFactory.ParseCompilationUnit(
                "class __SharpProofPlaceholder { void M([" + attributeExpression + "] object value) { } }");
            var attributeList = parameterUnit.DescendantNodes().OfType<ParameterSyntax>()
                .FirstOrDefault()?.AttributeLists.FirstOrDefault();
            if (attributeList == null || attributeList.ContainsDiagnostics) return document;

            var updatedParameter = parameter.AddAttributeLists(
                attributeList.WithoutTrivia().WithTrailingTrivia(SyntaxFactory.Space));
            return document.WithSyntaxRoot(root.ReplaceNode(parameter, updatedParameter));
        }

        private async Task<Document> AddInferredContractAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            string attributeExpression,
            CancellationToken cancellationToken) {
            const string attributeNamespace = "global::SharpProof.Attributes.";
            if (string.IsNullOrWhiteSpace(attributeExpression) ||
                !attributeExpression.StartsWith(attributeNamespace, StringComparison.Ordinal) ||
                attributeExpression.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return document;

            var useShortName = HasUnaliasedSharpProofAttributesUsing(declaration);

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

            if (useShortName)
                newAttributeList = ShortenSharpProofAttributeNames(newAttributeList, attributeNamespace);

            var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
            return InsertFormattedAttributeList(
                document,
                root,
                declaration,
                newAttributeList.WithoutTrivia(),
                lineEnding);
        }

        private static Document InsertFormattedAttributeList(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            AttributeListSyntax attributeList,
            string lineEnding) {
            var originalDeclaration = declaration;
            (declaration, attributeList) = FormatInsertedAttribute(declaration, attributeList, lineEnding);
            var updatedDeclaration = WithAttributeLists(
                declaration,
                GetAttributeLists(declaration).Insert(0, attributeList));
            return document.WithSyntaxRoot(root.ReplaceNode(originalDeclaration, updatedDeclaration));
        }

        private static async Task<string> GetLineEndingAsync(
            Document document,
            CancellationToken cancellationToken) {
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            return sourceText.ToString().IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        }

        private static (SyntaxNode Declaration, AttributeListSyntax AttributeList) FormatInsertedAttribute(
            SyntaxNode declaration,
            AttributeListSyntax attributeList,
            string lineEnding) {
            var leadingTrivia = declaration.GetLeadingTrivia();
            var indentation = SyntaxFactory.TriviaList(
                leadingTrivia
                    .Reverse()
                    .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    .Reverse());
            var trailingTrivia = SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(lineEnding)).AddRange(indentation);
            return (
                declaration.WithLeadingTrivia(default(SyntaxTriviaList)),
                attributeList
                    .WithLeadingTrivia(leadingTrivia)
                    .WithTrailingTrivia(trailingTrivia));
        }

        private static bool HasUnaliasedSharpProofAttributesUsing(SyntaxNode declaration) =>
            declaration.AncestorsAndSelf()
                .SelectMany(static ancestor => ancestor switch {
                    CompilationUnitSyntax compilationUnit => compilationUnit.Usings,
                    BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Usings,
                    _ => default
                })
                .Any(static directive =>
                    directive.Alias == null &&
                    string.Equals(directive.Name?.ToString(), "SharpProof.Attributes", StringComparison.Ordinal));

        private static AttributeListSyntax ShortenSharpProofAttributeNames(
            AttributeListSyntax attributeList,
            string attributeNamespace) {
            return (AttributeListSyntax)new SharpProofAttributeNameRewriter(attributeNamespace).Visit(attributeList)!;
        }

        sealed class SharpProofAttributeNameRewriter(string attributeNamespace) : CSharpSyntaxRewriter {
            public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node) =>
                Shorten(node) ?? base.VisitQualifiedName(node);

            public override SyntaxNode? VisitAliasQualifiedName(AliasQualifiedNameSyntax node) =>
                Shorten(node) ?? base.VisitAliasQualifiedName(node);

            public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node) {
                var expressionText = node.Expression.ToString();
                if (expressionText.StartsWith(attributeNamespace, StringComparison.Ordinal))
                    return node.WithExpression(SyntaxFactory.ParseExpression(
                            expressionText.Substring(attributeNamespace.Length))
                        .WithTriviaFrom(node.Expression));

                return base.VisitMemberAccessExpression(node);
            }

            private NameSyntax? Shorten(NameSyntax node) {
                var text = node.ToString();
                return text.StartsWith(attributeNamespace, StringComparison.Ordinal)
                    ? SyntaxFactory.ParseName(text.Substring(attributeNamespace.Length)).WithTriviaFrom(node)
                    : null;
            }
        }
}
