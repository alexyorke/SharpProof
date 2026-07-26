namespace SharpProof.Migration;

internal static class LegacyContractMigration {
    private const string RequiresMetadataName =
        "SharpProof.Attributes.RequiresAttribute";
    private const string EnsuresMetadataName =
        "SharpProof.Attributes.EnsuresAttribute";
    private const string ContractMetadataName =
        "SharpProof.Attributes.Contract";

    internal static async Task<Document?> TryMigrateAsync(
        Document document,
        AttributeSyntax selectedAttribute,
        CancellationToken cancellationToken) {
        var model = await document.GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        if (model == null || root == null) return null;
        var declaration = selectedAttribute.FirstAncestorOrSelf<
            BaseMethodDeclarationSyntax>();
        if (declaration is not (
                MethodDeclarationSyntax or ConstructorDeclarationSyntax))
            return null;
        if (!HasMigratableBody(declaration) ||
            declaration.Modifiers.Any(static modifier =>
                modifier.IsKind(SyntaxKind.AsyncKeyword) ||
                modifier.IsKind(SyntaxKind.ExternKeyword) ||
                modifier.IsKind(SyntaxKind.AbstractKeyword)))
            return null;

        var method = model.GetDeclaredSymbol(declaration, cancellationToken)
            as IMethodSymbol;
        if (method == null ||
            method.MethodKind is not (
                MethodKind.Ordinary or MethodKind.Constructor) ||
            method.RefKind != RefKind.None)
            return null;
        var compilation = model.Compilation;
        var requiresType = compilation.GetTypeByMetadataName(
            RequiresMetadataName);
        var ensuresType = compilation.GetTypeByMetadataName(
            EnsuresMetadataName);
        var contractType = compilation.GetTypeByMetadataName(
            ContractMetadataName);
        if (requiresType == null || ensuresType == null || contractType == null)
            return null;
        if (!TryGetLegacyKind(
                selectedAttribute,
                model,
                requiresType,
                ensuresType,
                cancellationToken,
                out _))
            return null;

        var legacyAttributes = declaration.AttributeLists
            .SelectMany(static list => list.Attributes)
            .Select(attribute => new {
                Attribute = attribute,
                Kind = TryGetLegacyKind(
                    attribute,
                    model,
                    requiresType,
                    ensuresType,
                    cancellationToken,
                    out var kind)
                    ? kind
                    : (LegacyContractKind?)null
            })
            .Where(static item => item.Kind.HasValue)
            .OrderBy(static item => item.Attribute.SpanStart)
            .ToImmutableArray();
        if (legacyAttributes.IsDefaultOrEmpty) return null;

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>(
            legacyAttributes.Length);
        foreach (var item in legacyAttributes) {
            if (!TryGetConditionText(
                    item.Attribute,
                    model,
                    cancellationToken,
                    out var text) ||
                !LegacyContractExpressionRewriter.TryRewrite(
                    text,
                    item.Kind!.Value,
                    method,
                    declaration,
                    out var condition))
                return null;
            var contractCall = CreateContractCall(item.Kind.Value, condition);
            statements.Add(
                SyntaxFactory.ExpressionStatement(contractCall)
                    .WithAdditionalAnnotations(
                        Formatter.Annotation,
                        MigrationAnnotations.InsertedContract));
        }

        var stripped = declaration.RemoveNodes(
            legacyAttributes.Select(static item => item.Attribute),
            SyntaxRemoveOptions.KeepExteriorTrivia);
        if (stripped is not BaseMethodDeclarationSyntax withoutAttributes)
            return null;
        var emptyAttributeLists = withoutAttributes.AttributeLists
            .Where(static list => list.Attributes.Count == 0)
            .ToImmutableArray();
        if (!emptyAttributeLists.IsDefaultOrEmpty) {
            stripped = withoutAttributes.RemoveNodes(
                emptyAttributeLists,
                SyntaxRemoveOptions.KeepExteriorTrivia);
            if (stripped is not BaseMethodDeclarationSyntax cleanedDeclaration)
                return null;
            withoutAttributes = cleanedDeclaration;
        }
        var rewritten = AddStatements(
            withoutAttributes,
            statements.ToImmutable(),
            method);
        if (rewritten == null) return null;
        rewritten = rewritten.WithAdditionalAnnotations(
            MigrationAnnotations.MigratedDeclaration,
            Formatter.Annotation);
        var changedRoot = root.ReplaceNode(declaration, rewritten);
        var changedDocument = document.WithSyntaxRoot(changedRoot);
        return await ValidateChangedDocumentAsync(
            changedDocument,
            contractType,
            statements.Count,
            cancellationToken).ConfigureAwait(false)
            ? changedDocument
            : null;
    }

    private static bool HasMigratableBody(
        BaseMethodDeclarationSyntax declaration) =>
        declaration.Body != null || declaration.ExpressionBody != null;

    private static bool TryGetLegacyKind(
        AttributeSyntax attribute,
        SemanticModel model,
        INamedTypeSymbol requiresType,
        INamedTypeSymbol ensuresType,
        CancellationToken cancellationToken,
        out LegacyContractKind kind) {
        var constructor = model.GetSymbolInfo(
                attribute,
                cancellationToken)
            .Symbol as IMethodSymbol;
        var attributeType = constructor?.ContainingType;
        if (SymbolEqualityComparer.Default.Equals(
                attributeType?.OriginalDefinition,
                requiresType)) {
            kind = LegacyContractKind.Requires;
            return true;
        }
        if (SymbolEqualityComparer.Default.Equals(
                attributeType?.OriginalDefinition,
                ensuresType)) {
            kind = LegacyContractKind.Ensures;
            return true;
        }
        kind = default;
        return false;
    }

    private static bool TryGetConditionText(
        AttributeSyntax attribute,
        SemanticModel model,
        CancellationToken cancellationToken,
        out string text) {
        text = string.Empty;
        if (attribute.ArgumentList?.Arguments.Count != 1)
            return false;
        var constant = model.GetConstantValue(
            attribute.ArgumentList.Arguments[0].Expression,
            cancellationToken);
        if (!constant.HasValue || constant.Value is not string value ||
            string.IsNullOrWhiteSpace(value))
            return false;
        text = value;
        return true;
    }

    private static InvocationExpressionSyntax CreateContractCall(
        LegacyContractKind kind,
        ExpressionSyntax condition) {
        var member = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.ParseName(
                    "global::SharpProof.Attributes.Contract")
                .WithAdditionalAnnotations(Simplifier.Annotation),
            SyntaxFactory.IdentifierName(
                kind == LegacyContractKind.Requires
                    ? "Requires"
                    : "Ensures"));
        return SyntaxFactory.InvocationExpression(
            member,
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(condition))));
    }

    private static BaseMethodDeclarationSyntax? AddStatements(
        BaseMethodDeclarationSyntax declaration,
        ImmutableArray<StatementSyntax> contracts,
        IMethodSymbol method) {
        if (declaration.Body != null) {
            var body = declaration.Body.WithStatements(
                declaration.Body.Statements.InsertRange(0, contracts));
            return declaration.WithBody(body);
        }
        var expression = declaration.ExpressionBody?.Expression;
        if (expression == null) return null;
        var expressionBody = declaration.ExpressionBody!;
        var expressionLeadingTrivia = expressionBody.ArrowToken.LeadingTrivia
            .AddRange(expressionBody.ArrowToken.TrailingTrivia)
            .AddRange(expression.GetLeadingTrivia());
        expression = expression.WithLeadingTrivia(expressionLeadingTrivia);
        StatementSyntax terminal;
        if (declaration is ConstructorDeclarationSyntax ||
            method.ReturnsVoid) {
            terminal = SyntaxFactory.ExpressionStatement(expression);
        }
        else {
            terminal = SyntaxFactory.ReturnStatement(expression);
        }
        var terminalLastToken = terminal.GetLastToken();
        terminal = terminal.ReplaceToken(
            terminalLastToken,
            terminalLastToken.WithTrailingTrivia(
                declaration.SemicolonToken.TrailingTrivia));
        var block = SyntaxFactory.Block(
            contracts.Add(terminal));
        return declaration switch {
            MethodDeclarationSyntax methodDeclaration =>
                methodDeclaration
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(block),
            ConstructorDeclarationSyntax constructor =>
                constructor
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(block),
            _ => null
        };
    }

    private static async Task<bool> ValidateChangedDocumentAsync(
        Document document,
        INamedTypeSymbol originalContractType,
        int expectedStatementCount,
        CancellationToken cancellationToken) {
        var root = await document.GetSyntaxRootAsync(cancellationToken)
            .ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken)
            .ConfigureAwait(false);
        if (root == null || model == null) return false;
        var declaration = root.GetAnnotatedNodes(
                MigrationAnnotations.MigratedDeclaration)
            .OfType<BaseMethodDeclarationSyntax>()
            .SingleOrDefault();
        if (declaration == null) return false;
        var statements = declaration.GetAnnotatedNodes(
                MigrationAnnotations.InsertedContract)
            .OfType<ExpressionStatementSyntax>()
            .ToImmutableArray();
        if (statements.Length != expectedStatementCount) return false;
        var contractType = model.Compilation.GetTypeByMetadataName(
            ContractMetadataName);
        if (contractType == null ||
            !SymbolEqualityComparer.Default.Equals(
                contractType,
                originalContractType))
            return false;
        foreach (var statement in statements) {
            if (model.GetOperation(
                    statement.Expression,
                    cancellationToken) is not IInvocationOperation invocation ||
                !SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.ContainingType,
                    contractType) ||
                invocation.Arguments.Length != 1 ||
                invocation.Arguments[0].Value.Type?.SpecialType !=
                SpecialType.System_Boolean ||
                invocation.DescendantsAndSelf().Any(
                    static operation => operation is IInvalidOperation))
                return false;
        }
        return true;
    }

    private enum LegacyContractKind {
        Requires,
        Ensures
    }

    private static class MigrationAnnotations {
        internal static SyntaxAnnotation InsertedContract { get; } =
            new("SharpProofMigration.InsertedContract");
        internal static SyntaxAnnotation MigratedDeclaration { get; } =
            new("SharpProofMigration.Declaration");
    }

    private static class LegacyContractExpressionRewriter {
        internal static bool TryRewrite(
            string text,
            LegacyContractKind kind,
            IMethodSymbol method,
            BaseMethodDeclarationSyntax declaration,
            out ExpressionSyntax expression) {
            expression = SyntaxFactory.ParseExpression(
                text,
                options: new CSharpParseOptions(LanguageVersion.CSharp12));
            if (expression.ContainsDiagnostics ||
                expression.FullSpan.Length != text.Length)
                return false;
            var parameterSyntax = declaration.ParameterList.Parameters
                .ToDictionary(
                    static parameter => parameter.Identifier.ValueText,
                    static parameter => parameter.Identifier,
                    StringComparer.Ordinal);
            if (parameterSyntax.Count != method.Parameters.Length)
                return false;
            if (kind == LegacyContractKind.Ensures &&
                (parameterSyntax.ContainsKey("result") ||
                 method.ContainingType.GetMembers("result").Length != 0))
                return false;
            var oldIsAmbiguous =
                parameterSyntax.ContainsKey("old") ||
                method.ContainingType.GetMembers("old").Length != 0;
            var validator = new SupportedExpressionRewriter(
                kind,
                method,
                parameterSyntax,
                (declaration as MethodDeclarationSyntax)?.ReturnType,
                oldIsAmbiguous,
                declaration.Modifiers.Any(static modifier =>
                    modifier.IsKind(SyntaxKind.StaticKeyword)));
            var rewritten = validator.Visit(expression) as ExpressionSyntax;
            if (!validator.IsSupported || rewritten == null) return false;
            expression = rewritten;
            return true;
        }

        private sealed class SupportedExpressionRewriter(
            LegacyContractKind kind,
            IMethodSymbol method,
            IReadOnlyDictionary<string, SyntaxToken> parameters,
            TypeSyntax? returnType,
            bool oldIsAmbiguous,
            bool isStatic)
            : CSharpSyntaxRewriter {
            private readonly LegacyContractKind _kind = kind;
            private readonly IMethodSymbol _method = method;
            private readonly IReadOnlyDictionary<string, SyntaxToken> _parameters =
                parameters;
            private readonly bool _isStatic = isStatic;
            private readonly TypeSyntax? _returnType = returnType;
            private readonly bool _oldIsAmbiguous = oldIsAmbiguous;
            private bool _insideOld;

            internal bool IsSupported { get; private set; } = true;

            public override SyntaxNode? Visit(SyntaxNode? node) {
                if (node is ExpressionSyntax expression &&
                    !IsSupportedExpressionShape(expression)) {
                    IsSupported = false;
                    return node;
                }
                return base.Visit(node);
            }

            public override SyntaxNode? VisitIdentifierName(
                IdentifierNameSyntax node) {
                if (!IsSupported) return node;
                if (_kind == LegacyContractKind.Ensures &&
                    node.Identifier.ValueText == "result") {
                    if (_insideOld ||
                        _method.ReturnsVoid ||
                        _method.MethodKind == MethodKind.Constructor) {
                        IsSupported = false;
                        return node;
                    }
                    return CreateResultExpression(node);
                }
                if (!_parameters.TryGetValue(
                        node.Identifier.ValueText,
                        out var identifier)) {
                    IsSupported = false;
                    return node;
                }
                if (_insideOld &&
                    _method.Parameters.First(parameter =>
                        parameter.Name == node.Identifier.ValueText).RefKind ==
                    RefKind.Out) {
                    IsSupported = false;
                    return node;
                }
                return node.WithIdentifier(
                    identifier.WithTriviaFrom(node.Identifier));
            }

            public override SyntaxNode? VisitThisExpression(
                ThisExpressionSyntax node) {
                if (_isStatic) IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitInvocationExpression(
                InvocationExpressionSyntax node) {
                if (!IsSupported ||
                    node.Expression is not IdentifierNameSyntax identifier ||
                    identifier.Identifier.ValueText != "old" ||
                    _oldIsAmbiguous ||
                    _kind != LegacyContractKind.Ensures ||
                    _insideOld ||
                    node.ArgumentList.Arguments.Count != 1) {
                    IsSupported = false;
                    return node;
                }
                _insideOld = true;
                var argument = Visit(
                    node.ArgumentList.Arguments[0].Expression)
                    as ExpressionSyntax;
                _insideOld = false;
                if (!IsSupported || argument == null) return node;
                var oldMember = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseName(
                            "global::SharpProof.Attributes.Contract")
                        .WithAdditionalAnnotations(Simplifier.Annotation),
                    SyntaxFactory.IdentifierName("Old"));
                return SyntaxFactory.InvocationExpression(
                        oldMember,
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(argument))))
                    .WithTriviaFrom(node);
            }

            public override SyntaxNode? VisitMemberAccessExpression(
                MemberAccessExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitElementAccessExpression(
                ElementAccessExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitObjectCreationExpression(
                ObjectCreationExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitAssignmentExpression(
                AssignmentExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitAwaitExpression(
                AwaitExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitSimpleLambdaExpression(
                SimpleLambdaExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitParenthesizedLambdaExpression(
                ParenthesizedLambdaExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitAnonymousMethodExpression(
                AnonymousMethodExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitCastExpression(
                CastExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitTypeOfExpression(
                TypeOfExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitDefaultExpression(
                DefaultExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            public override SyntaxNode? VisitDeclarationExpression(
                DeclarationExpressionSyntax node) {
                IsSupported = false;
                return node;
            }

            private InvocationExpressionSyntax CreateResultExpression(
                IdentifierNameSyntax source) {
                var returnType = _returnType ??
                    throw new InvalidOperationException(
                        "A result placeholder requires a method return type.");
                var member = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParseName(
                            "global::SharpProof.Attributes.Contract")
                        .WithAdditionalAnnotations(Simplifier.Annotation),
                    SyntaxFactory.GenericName(
                        SyntaxFactory.Identifier("Result"),
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                returnType.WithoutTrivia()))));
                return SyntaxFactory.InvocationExpression(member)
                    .WithTriviaFrom(source);
            }

            private static bool IsSupportedExpressionShape(
                ExpressionSyntax expression) =>
                expression switch {
                    IdentifierNameSyntax => true,
                    ThisExpressionSyntax => true,
                    LiteralExpressionSyntax => true,
                    ParenthesizedExpressionSyntax => true,
                    InvocationExpressionSyntax => true,
                    ConditionalExpressionSyntax => true,
                    PrefixUnaryExpressionSyntax prefix =>
                        prefix.IsKind(SyntaxKind.LogicalNotExpression) ||
                        prefix.IsKind(SyntaxKind.UnaryMinusExpression) ||
                        prefix.IsKind(SyntaxKind.UnaryPlusExpression),
                    BinaryExpressionSyntax binary =>
                        binary.Kind() is
                            SyntaxKind.AddExpression or
                            SyntaxKind.SubtractExpression or
                            SyntaxKind.MultiplyExpression or
                            SyntaxKind.DivideExpression or
                            SyntaxKind.ModuloExpression or
                            SyntaxKind.LogicalAndExpression or
                            SyntaxKind.LogicalOrExpression or
                            SyntaxKind.EqualsExpression or
                            SyntaxKind.NotEqualsExpression or
                            SyntaxKind.LessThanExpression or
                            SyntaxKind.LessThanOrEqualExpression or
                            SyntaxKind.GreaterThanExpression or
                            SyntaxKind.GreaterThanOrEqualExpression or
                            SyntaxKind.CoalesceExpression,
                    _ => false
                };
        }
    }
}
