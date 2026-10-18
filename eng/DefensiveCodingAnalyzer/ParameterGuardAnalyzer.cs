using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.DefensiveCodingAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterGuardAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SPINT001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Validate every input parameter",
        "Parameter '{0}' has no explicit guard",
        "DefensiveCoding",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every input parameter of a concrete callable must occur in an explicit validation that rejects invalid input.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.OperatorDeclaration,
            SyntaxKind.ConversionOperatorDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.AnonymousMethodExpression,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (!TryGetCallable(context.Node, out var parameters, out var body))
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            // An out parameter is written by the callable and cannot be read
            // before it is assigned, so it is not an input to validate.
            if (parameter.Modifiers.Any(static token => token.IsKind(SyntaxKind.OutKeyword)) ||
                context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is not IParameterSymbol symbol)
            {
                continue;
            }

            // An unread underscore in an anonymous callable is a discarded input.
            // A named method or a lambda that reads it still has an input contract.
            if (context.Node is AnonymousFunctionExpressionSyntax &&
                parameter.Identifier.ValueText == "_" && !HasDeclaredInputConstraint(symbol) &&
                !Mentions(body, symbol, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }
            // Neither Boolean value nor a CancellationToken state is invalid
            // on its own. Rejecting one would change the callable's contract.
            if (AcceptsEveryNullableReferenceValue(body, symbol, context.SemanticModel, context.CancellationToken) ||
                AcceptsEveryNullableValue(body, symbol, context.SemanticModel, context.CancellationToken) ||
                symbol.Type.SpecialType == SpecialType.System_Boolean ||
                AcceptsEveryIntrinsicStructValue(symbol.Type) ||
                AcceptsEveryZeroStateValue(symbol.Type) ||
                AcceptsEveryUnusedValue(body, symbol, context.SemanticModel, context.CancellationToken) ||
                AcceptsEveryProjectionValue(body, symbol, context.SemanticModel, context.CancellationToken) ||
                SymbolEqualityComparer.Default.Equals(
                    symbol.Type,
                    context.Compilation.GetTypeByMetadataName("System.Threading.CancellationToken")) ||
                HasTotalDomainReason(parameter))
            {
                continue;
            }

            if (HasGuard(body, symbol, context.SemanticModel, context.CancellationToken) ||
                HasStateValidation(body, symbol, context.SemanticModel, context.CancellationToken) ||
                IsNullPredicateValidation(body, symbol, context.SemanticModel, context.CancellationToken) ||
                IsValuePredicateValidation(body, symbol, context.SemanticModel, context.CancellationToken) ||
                IsValueAssertionValidation(body, symbol, context.SemanticModel, context.Compilation, context.CancellationToken))
            {
                continue;
            }

            if (AcceptsEveryNumericValue(body, symbol, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                parameter.Identifier.GetLocation(),
                parameter.Identifier.ValueText));
        }
    }

    private static bool TryGetCallable(
        SyntaxNode node,
        out ImmutableArray<ParameterSyntax> parameters,
        out SyntaxNode body)
    {
        parameters = [];
        body = null!;
        switch (node)
        {
            case MethodDeclarationSyntax method when ((SyntaxNode?)method.Body ?? method.ExpressionBody) is { } methodBody:
                parameters = method.ParameterList.Parameters.ToImmutableArray();
                body = methodBody;
                return true;
            case ConstructorDeclarationSyntax constructor when ((SyntaxNode?)constructor.Body ?? constructor.ExpressionBody) is not null:
                parameters = constructor.ParameterList.Parameters.ToImmutableArray();
                body = constructor;
                return true;
            case OperatorDeclarationSyntax operation when ((SyntaxNode?)operation.Body ?? operation.ExpressionBody) is { } operationBody:
                parameters = operation.ParameterList.Parameters.ToImmutableArray();
                body = operationBody;
                return true;
            case ConversionOperatorDeclarationSyntax conversion
                when ((SyntaxNode?)conversion.Body ?? conversion.ExpressionBody) is { } conversionBody:
                parameters = conversion.ParameterList.Parameters.ToImmutableArray();
                body = conversionBody;
                return true;
            case LocalFunctionStatementSyntax local when ((SyntaxNode?)local.Body ?? local.ExpressionBody) is { } localBody:
                parameters = local.ParameterList.Parameters.ToImmutableArray();
                body = localBody;
                return true;
            case SimpleLambdaExpressionSyntax simpleLambda:
                parameters = [simpleLambda.Parameter];
                body = simpleLambda.Body;
                return true;
            case ParenthesizedLambdaExpressionSyntax lambda:
                parameters = lambda.ParameterList.Parameters.ToImmutableArray();
                body = lambda.Body;
                return true;
            case AnonymousMethodExpressionSyntax anonymous when anonymous.ParameterList is { } anonymousParameters:
                parameters = anonymousParameters.Parameters.ToImmutableArray();
                body = anonymous.Body;
                return true;
            case TypeDeclarationSyntax type when type.ParameterList is { } primary:
                parameters = primary.Parameters.ToImmutableArray();
                body = type;
                return true;
            default:
                return false;
        }
    }

    private static bool HasGuard(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var node in body.DescendantNodesAndSelf(node =>
            ReferenceEquals(node, body) || DescendIntoCallable(node)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is IfStatementSyntax condition &&
                Mentions(condition.Condition, parameter, model, cancellationToken) &&
                (Rejects(condition.Statement) ||
                 condition.Else is { Statement: { } alternative } && Rejects(alternative)))
            {
                return true;
            }

            if (node is InvocationExpressionSyntax invocation &&
                IsValidationCall(invocation.Expression) &&
                (invocation.ArgumentList.Arguments.Any(argument =>
                    Mentions(argument.Expression, parameter, model, cancellationToken)) ||
                 invocation.Expression is MemberAccessExpressionSyntax validation &&
                    Mentions(validation.Expression, parameter, model, cancellationToken)))
            {
                return true;
            }

            if (node is BinaryExpressionSyntax coalesce &&
                coalesce.IsKind(SyntaxKind.CoalesceExpression) &&
                coalesce.Right is ThrowExpressionSyntax &&
                Mentions(coalesce.Left, parameter, model, cancellationToken))
            {
                return true;
            }

            if (node is SwitchExpressionSyntax selection &&
                Mentions(selection.GoverningExpression, parameter, model, cancellationToken) &&
                selection.Arms.Any(static arm => RejectsExpression(arm.Expression)))
            {
                return true;
            }

            if (node is SwitchStatementSyntax switchStatement &&
                Mentions(switchStatement.Expression, parameter, model, cancellationToken) &&
                switchStatement.Sections.Any(static section =>
                    section.Statements.LastOrDefault() is { } last && Rejects(last)))
            {
                return true;
            }

            if (node is ConditionalExpressionSyntax conditional &&
                Mentions(conditional.Condition, parameter, model, cancellationToken) &&
                (RejectsExpression(conditional.WhenTrue) ||
                 RejectsExpression(conditional.WhenFalse)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStateValidation(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var nullable = parameter.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var array = parameter.Type is INamedTypeSymbol named &&
            named.Name == "ImmutableArray" &&
            HasNamespace(named.ContainingNamespace, "System", "Collections", "Immutable");
        if (!nullable && !array)
        {
            return false;
        }

        return body.DescendantNodesAndSelf(node => ReferenceEquals(node, body) || DescendIntoCallable(node))
            .Select(node => node switch
            {
                IfStatementSyntax condition => condition.Condition,
                ConditionalExpressionSyntax condition => condition.Condition,
                _ => null
            })
            .OfType<ExpressionSyntax>()
            .Any(condition => condition.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
                .Any(member => (nullable && member.Name.Identifier.ValueText == "HasValue" ||
                    array && member.Name.Identifier.ValueText is "IsDefault" or "IsDefaultOrEmpty") &&
                    Mentions(member.Expression, parameter, model, cancellationToken)));
    }
    private static bool RejectsExpression(ExpressionSyntax expression)
    {
        return expression is ThrowExpressionSyntax or DefaultExpressionSyntax ||
            expression.IsKind(SyntaxKind.NullLiteralExpression) ||
            expression.IsKind(SyntaxKind.FalseLiteralExpression) ||
            expression.IsKind(SyntaxKind.DefaultLiteralExpression);
    }
    private static bool IsValuePredicateValidation(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!parameter.Type.IsValueType)
        {
            return false;
        }

        return ReturnedBooleanExpressions(body, model, cancellationToken).Any(expression =>
            Mentions(expression, parameter, model, cancellationToken));
    }
    private static bool IsValueAssertionValidation(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (!parameter.Type.IsValueType ||
            compilation.AssemblyName?.EndsWith(".Test", StringComparison.Ordinal) != true)
        {
            return false;
        }

        return body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(invocation =>
            model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method &&
            method.ContainingType.Name == "Assert" && HasNamespace(method.ContainingType.ContainingNamespace, "NUnit", "Framework") &&
            method.Name is "That" or "AreEqual" or "AreNotEqual" or "Throws" or "ThrowsAsync" &&
            invocation.ArgumentList.Arguments.Any(argument =>
                Mentions(argument.Expression, parameter, model, cancellationToken)));
    }
    private static bool IsNullPredicateValidation(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        return ReturnedBooleanExpressions(body, model, cancellationToken).Any(expression =>
            expression.DescendantNodesAndSelf().Any(node =>
                node is IsPatternExpressionSyntax pattern &&
                    IsNullPattern(pattern.Pattern) &&
                    Mentions(pattern.Expression, parameter, model, cancellationToken) ||
                node is BinaryExpressionSyntax comparison &&
                    (comparison.IsKind(SyntaxKind.EqualsExpression) ||
                     comparison.IsKind(SyntaxKind.NotEqualsExpression)) &&
                    (comparison.Left.IsKind(SyntaxKind.NullLiteralExpression) &&
                        Mentions(comparison.Right, parameter, model, cancellationToken) ||
                     comparison.Right.IsKind(SyntaxKind.NullLiteralExpression) &&
                        Mentions(comparison.Left, parameter, model, cancellationToken))));
    }

    private static IEnumerable<ExpressionSyntax> ReturnedBooleanExpressions(
        SyntaxNode body,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        IEnumerable<ExpressionSyntax> expressions = body switch
        {
            ArrowExpressionClauseSyntax arrow => [arrow.Expression],
            ExpressionSyntax direct => [direct],
            BlockSyntax block => block.DescendantNodes(DescendIntoCallable)
                .OfType<ReturnStatementSyntax>()
                .Select(static statement => statement.Expression)
                .OfType<ExpressionSyntax>(),
            _ => []
        };

        return expressions.Where(expression =>
            model.GetTypeInfo(expression, cancellationToken).Type?.SpecialType ==
                SpecialType.System_Boolean);
    }
    private static bool IsNullPattern(PatternSyntax pattern)
    {
        return pattern is ConstantPatternSyntax
        {
            Expression: LiteralExpressionSyntax literal
        } &&
            literal.IsKind(SyntaxKind.NullLiteralExpression) ||
            pattern is UnaryPatternSyntax unary &&
            unary.IsKind(SyntaxKind.NotPattern) &&
            IsNullPattern(unary.Pattern);
    }
    private static bool DescendIntoCallable(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax
            and not MethodDeclarationSyntax and not ConstructorDeclarationSyntax
            and not OperatorDeclarationSyntax and not ConversionOperatorDeclarationSyntax;
    }

    private static bool HasDeclaredInputConstraint(IParameterSymbol parameter)
    {
        return parameter.GetAttributes().Any(attribute => attribute.AttributeClass is { } type &&
            (type.Name is "PositiveAttribute" or "InRangeAttribute" or "RangeAttribute" ||
             type.Name == "NotNullAttribute" && HasNamespace(type.ContainingNamespace, "SharpProof", "Attributes") ||
             type.Name == "DisallowNullAttribute" && HasNamespace(type.ContainingNamespace, "System", "Diagnostics", "CodeAnalysis")));
    }
    private static bool AcceptsEveryNullableReferenceValue(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if ((!parameter.Type.IsReferenceType && parameter.Type.TypeKind != TypeKind.TypeParameter) ||
            parameter.NullableAnnotation != NullableAnnotation.Annotated || HasDeclaredInputConstraint(parameter))
        {
            return false;
        }

        // A nullable reference explicitly admits null. The compiler checks
        // dereferences; a suppression operator opts out of that check and
        // therefore still needs a visible runtime guard.
        return !body.DescendantNodesAndSelf()
            .OfType<PostfixUnaryExpressionSyntax>()
            .Where(static expression =>
                expression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            .Any(expression => IsDereferenceReceiver(expression) &&
                MentionsWithoutNestedCallables(expression.Operand, parameter, model, cancellationToken));
    }
    private static bool IsDereferenceReceiver(ExpressionSyntax expression)
    {
        SyntaxNode receiver = expression;
        while (receiver.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            receiver = parentheses;
        }

        return receiver.Parent is MemberAccessExpressionSyntax member && ReferenceEquals(member.Expression, receiver) ||
            receiver.Parent is ElementAccessExpressionSyntax element && ReferenceEquals(element.Expression, receiver) ||
            receiver.Parent is InvocationExpressionSyntax invocation && ReferenceEquals(invocation.Expression, receiver);
    }
    private static bool AcceptsEveryNullableValue(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (parameter.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T || HasDeclaredInputConstraint(parameter))
        {
            return false;
        }

        // HasValue and GetValueOrDefault accept both states. Extracting Value
        // or casting to the underlying type requires an explicit prior check.
        return !body.DescendantNodesAndSelf().Any(node =>
            node is MemberAccessExpressionSyntax member &&
                member.Name.Identifier.ValueText == "Value" &&
                model.GetSymbolInfo(member, cancellationToken).Symbol is IPropertySymbol { ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } &&
                Mentions(member.Expression, parameter, model, cancellationToken) ||
            node is CastExpressionSyntax cast &&
                Mentions(cast.Expression, parameter, model, cancellationToken));
    }
    private static bool AcceptsEveryNumericValue(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!IsNumericPrimitive(parameter.Type) || parameter.GetAttributes().Length != 0)
        {
            return false;
        }

        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter))
            {
                continue;
            }

            for (var node = (SyntaxNode)identifier; ; node = node.Parent!)
            {
                if (node is ArrayRankSpecifierSyntax ||
                    IsCheckedNumericUse(node, model, cancellationToken) ||
                    node is SwitchExpressionSyntax selection && !selection.Arms.Any(static arm =>
                        arm.WhenClause is null && arm.Pattern is DiscardPatternSyntax or VarPatternSyntax) ||
                    node is ArgumentSyntax { Parent: BracketedArgumentListSyntax } ||
                    node is BinaryExpressionSyntax binary &&
                        (binary.IsKind(SyntaxKind.DivideExpression) || binary.IsKind(SyntaxKind.ModuloExpression)) &&
                        !HasSafeDivisor(binary, parameter, model, cancellationToken) ||
                    node is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Value")
                {
                    return false;
                }
                if (ReferenceEquals(node, body))
                {
                    break;
                }
            }
        }

        return true;
    }

    private static bool HasSafeDivisor(BinaryExpressionSyntax division, IParameterSymbol parameter, SemanticModel model, CancellationToken cancellationToken)
    {
        if (parameter.Type.Name == "BigInteger" && !Mentions(division.Right, parameter, model, cancellationToken))
        {
            return true;
        }

        var constant = model.GetConstantValue(division.Right, cancellationToken);
        if (!constant.HasValue || constant.Value is null)
        {
            return false;
        }

        return constant.Value switch
        {
            int value => value is not (0 or -1),
            long value => value is not (0 or -1),
            uint value => value != 0,
            ulong value => value != 0,
            _ => false
        };
    }
    private static bool IsCheckedNumericUse(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        if (node is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.AddExpression) || binary.IsKind(SyntaxKind.SubtractExpression) ||
             binary.IsKind(SyntaxKind.MultiplyExpression)) &&
            model.GetOperation(binary, cancellationToken) is IBinaryOperation operation)
        {
            return operation.IsChecked || operation.Type?.SpecialType == SpecialType.System_Decimal;
        }

        return node is PrefixUnaryExpressionSyntax unary &&
                model.GetOperation(unary, cancellationToken) is IUnaryOperation { IsChecked: true } ||
            node is CastExpressionSyntax cast &&
                model.GetOperation(cast, cancellationToken) is IConversionOperation { IsChecked: true } conversion &&
                !conversion.Conversion.IsIdentity;
    }
    private static bool HasTotalDomainReason(ParameterSyntax parameter)
    {
        const string marker = "/* SPINT001: all values valid - ";
        var source = parameter.ToFullString();
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var reasonStart = start + marker.Length;
        var end = source.IndexOf("*/", reasonStart, StringComparison.Ordinal);
        return end > reasonStart &&
            !string.IsNullOrWhiteSpace(source.Substring(reasonStart, end - reasonStart));
    }
    private static bool AcceptsEveryProjectionValue(
        SyntaxNode body,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (!parameter.Type.IsValueType || parameter.Type.Name == "ScopedIrId" || parameter.GetAttributes().Length != 0)
        {
            return false;
        }

        ExpressionSyntax? expression = body switch
        {
            ArrowExpressionClauseSyntax arrow => arrow.Expression,
            ExpressionSyntax direct => direct,
            BlockSyntax { Statements.Count: 1 } block when block.Statements[0] is ReturnStatementSyntax result => result.Expression,
            _ => null
        };
        while (expression is ParenthesizedExpressionSyntax parentheses)
        {
            expression = parentheses.Expression;
        }

        if (expression is IdentifierNameSyntax identity)
        {
            return SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(identity, cancellationToken).Symbol, parameter);
        }

        if (expression is not MemberAccessExpressionSyntax member ||
            member.Expression is not IdentifierNameSyntax receiver ||
            !SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(receiver, cancellationToken).Symbol, parameter))
        {
            return false;
        }

        var symbol = model.GetSymbolInfo(member, cancellationToken).Symbol;
        if (symbol is IFieldSymbol)
        {
            return true;
        }

        return symbol is IPropertySymbol property &&
            (property.GetMethod?.GetAttributes().Any(attribute =>
                attribute.AttributeClass is { Name: "CompilerGeneratedAttribute" } attributeType &&
                    HasNamespace(attributeType.ContainingNamespace, "System", "Runtime", "CompilerServices")) == true ||
             property.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is ParameterSyntax ||
            reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax declaration &&
                declaration.ExpressionBody is null &&
                declaration.AccessorList?.Accessors.Any(accessor =>
                    accessor.IsKind(SyntaxKind.GetAccessorDeclaration) &&
                    accessor.Body is null && accessor.ExpressionBody is null) == true));
    }
    private static bool AcceptsEveryUnusedValue(SyntaxNode body, IParameterSymbol parameter, SemanticModel model, CancellationToken cancellationToken)
    {
        return body is not TypeDeclarationSyntax && parameter.Type.IsValueType &&
            parameter.Type.Name is not ("ScopedIrId" or "SpecId" or "SpecVarId") && parameter.GetAttributes().Length == 0 &&
            !Mentions(body, parameter, model, cancellationToken);
    }
    private static bool AcceptsEveryZeroStateValue(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named &&
            named.DeclaringSyntaxReferences.Length != 0 &&
            !named.GetMembers().OfType<IFieldSymbol>().Any(static field => !field.IsStatic);
    }
    private static bool AcceptsEveryIntrinsicStructValue(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var ns = named.ContainingNamespace;
        return ((named.Name is "ValueTuple" or "Guid") && HasNamespace(ns, "System")) ||
            (named.Name == "KeyValuePair" && HasNamespace(ns, "System", "Collections", "Generic"));
    }
    private static bool HasNamespace(INamespaceSymbol? symbol, params string[] parts)
    {
        parts = RepositoryParameterGuard.NotNull(parts, nameof(parts));

        for (var index = parts.Length - 1; index >= 0; index--)
        {
            if (symbol is null || symbol.IsGlobalNamespace || symbol.Name != parts[index])
            {
                return false;
            }

            symbol = symbol.ContainingNamespace;
        }

        return symbol?.IsGlobalNamespace == true;
    }
    private static bool IsNumericPrimitive(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Enum ||
            (type.Name == "BigInteger" && HasNamespace(type.ContainingNamespace, "System", "Numerics")) ||
            type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr or
            SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Decimal or SpecialType.System_Char;
    }

    private static bool Rejects(StatementSyntax statement)
    {
        return statement is ThrowStatementSyntax or ReturnStatementSyntax ||
            statement is BlockSyntax block && block.Statements.LastOrDefault() is { } last && Rejects(last) ||
            statement is IfStatementSyntax conditional &&
                conditional.Else is { Statement: { } alternative } &&
                Rejects(conditional.Statement) && Rejects(alternative);
    }

    private static bool IsValidationCall(ExpressionSyntax expression)
    {
        var name = expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => string.Empty
        };
        return name.StartsWith("ThrowIf", StringComparison.Ordinal) ||
               name.StartsWith("Validate", StringComparison.Ordinal) ||
               name.StartsWith("Ensure", StringComparison.Ordinal) ||
               name.StartsWith("Check", StringComparison.Ordinal) ||
               name.StartsWith("Require", StringComparison.Ordinal) ||
               name is "Requires" or "NotNull";
    }

    private static bool MentionsWithoutNestedCallables(
        SyntaxNode expression,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        return expression.DescendantNodesAndSelf(node => ReferenceEquals(node, expression) || DescendIntoCallable(node))
            .OfType<IdentifierNameSyntax>().Any(identifier =>
                SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, parameter));
    }
    private static bool Mentions(
        SyntaxNode expression,
        IParameterSymbol parameter,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        return MentionsCore(expression, parameter, model, ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default), cancellationToken);
    }

    private static bool MentionsCore(
        SyntaxNode expression,
        IParameterSymbol parameter,
        SemanticModel model,
        ImmutableHashSet<ISymbol>.Builder visited,
        CancellationToken cancellationToken)
    {
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, parameter))
            {
                return true;
            }

            if (symbol is not ILocalSymbol local || !visited.Add(local))
            {
                continue;
            }

            foreach (var reference in local.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not VariableDeclaratorSyntax { Initializer.Value: { } initializer } declaration)
                {
                    continue;
                }

                var scope = declaration.Ancestors().FirstOrDefault(static node => node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
                if (scope is null || scope.DescendantNodes().OfType<IdentifierNameSyntax>().Any(use =>
                    (use.Parent is AssignmentExpressionSyntax assignment && ReferenceEquals(assignment.Left, use) ||
                     use.Parent is PrefixUnaryExpressionSyntax prefix && (prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression)) ||
                     use.Parent is PostfixUnaryExpressionSyntax postfix && (postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression)) ||
                     use.Parent is ArgumentSyntax argument && argument.RefKindKeyword.RawKind != 0) &&
                    SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(use, cancellationToken).Symbol, local)))
                {
                    continue;
                }

                if (MentionsCore(initializer, parameter, model, visited, cancellationToken))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
