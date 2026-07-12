using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static class InferredContractSuggestionAnalyzer
{
    private const string AttributeNamespace = "global::SharpProof.Attributes.";

    private const SymbolicCapability AllKnownCapabilities =
        SymbolicCapability.IO |
        SymbolicCapability.FileRead |
        SymbolicCapability.FileWrite |
        SymbolicCapability.Network |
        SymbolicCapability.Console |
        SymbolicCapability.Process |
        SymbolicCapability.Environment |
        SymbolicCapability.Registry |
        SymbolicCapability.Clock |
        SymbolicCapability.Randomness |
        SymbolicCapability.Reflection |
        SymbolicCapability.Synchronization |
        SymbolicCapability.NativeInterop;

    private static readonly SymbolicCapability[] OrderedCapabilities =
    {
        SymbolicCapability.IO,
        SymbolicCapability.FileRead,
        SymbolicCapability.FileWrite,
        SymbolicCapability.Network,
        SymbolicCapability.Console,
        SymbolicCapability.Process,
        SymbolicCapability.Environment,
        SymbolicCapability.Registry,
        SymbolicCapability.Clock,
        SymbolicCapability.Randomness,
        SymbolicCapability.Reflection,
        SymbolicCapability.Synchronization,
        SymbolicCapability.NativeInterop
    };

    internal static void Analyze(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        var options = AnalyzerConfiguration.GetInferredContractSuggestionOptions(
            context.Options,
            context.Node.SyntaxTree,
            session.Configuration.InferredContractSuggestions);
        if (!options.IsEnabled ||
            context.State.RootOperation == null ||
            !IsSupportedDeclaration(context.Node) ||
            !MatchesScope(context.MethodSymbol, options.Scope))
            return;

        SuggestZeroAllocations(context, session, options);
        SuggestAllowedCapabilities(context, session, options);
        SuggestExpectedComplexity(context, session, options);
        SuggestExceptionContract(context, session, options);
        SuggestEnsures(context, session, options);
        SuggestRequires(context, session, options);
        SuggestNullableContracts(context, session, options);
    }

    private static void SuggestNullableContracts(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        InferredContractSuggestionOptions options)
    {
        const string optionKind = "nullability";
        const InferredContractConfidence confidence = InferredContractConfidence.High;
        if (!options.Includes(optionKind, confidence)) return;

        var method = context.MethodSymbol;
        if (!method.ReturnsVoid &&
            method.ReturnType.IsReferenceType &&
            method.ReturnNullableAnnotation == NullableAnnotation.Annotated &&
            NullableFlowFacts.GetMethodReturnState(method) != NullableFlowFactState.NotNull)
        {
            var returns = GetReturnExpressions(context.Node).ToArray();
            if (returns.Length != 0 &&
                returns.All(expression => NullableFlowFacts.GetExpressionState(
                    expression,
                    context.SemanticModel,
                    context.CancellationToken) == NullableFlowFactState.NotNull))
                Report(
                    context,
                    session,
                    SharpProofDiagnostics.SuggestNullableContractRule,
                    "nullable-return",
                    "global::System.Diagnostics.CodeAnalysis.NotNull",
                    "[return: NotNull]",
                    "every reachable return expression is proven non-null",
                    confidence);
        }

        if (!TryGetLeadingThrowGuard(context.Node, out var guard) ||
            !TryGetNullGuardParameter(context, guard.Condition, out var parameter) ||
            parameter.NullableAnnotation != NullableAnnotation.Annotated ||
            !SymbolicTypeFacts.IsReferenceLikeType(parameter.Type) ||
            NullableFlowFacts.HasNotNullPostcondition(parameter))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.SuggestNullableContractRule,
            "nullable-parameter:" + parameter.Name,
            "global::System.Diagnostics.CodeAnalysis.NotNull",
            "[NotNull] on parameter '" + parameter.Name + "'",
            "a null guard throws and every normal continuation has '" + parameter.Name + "' non-null",
            confidence);
    }

    private static bool TryGetNullGuardParameter(
        MethodBodyAnalysisContext context,
        ExpressionSyntax condition,
        out IParameterSymbol parameter)
    {
        condition = StripParentheses(condition);
        ExpressionSyntax? candidate = condition switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) &&
                                               binary.Left.IsKind(SyntaxKind.NullLiteralExpression) => binary.Right,
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) &&
                                               binary.Right.IsKind(SyntaxKind.NullLiteralExpression) => binary.Left,
            IsPatternExpressionSyntax isPattern when
                TryGetNullPatternPolarity(isPattern.Pattern, out var matchesNull) && matchesNull =>
                isPattern.Expression,
            _ => null
        };
        if (candidate != null &&
            context.SemanticModel.GetSymbolInfo(candidate, context.CancellationToken).Symbol is
                IParameterSymbol found)
        {
            parameter = found;
            return true;
        }

        parameter = null!;
        return false;
    }

    private static void SuggestZeroAllocations(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        InferredContractSuggestionOptions options)
    {
        const string kind = "zero-allocations";
        const InferredContractConfidence confidence = InferredContractConfidence.High;
        if (!options.Includes(kind, confidence) ||
            session.AttributePolicy.HasAttribute(context.MethodSymbol, "ZeroAllocationsAttribute") ||
            MethodAllocationAnalyzer.HasVisibleAllocationSites(context.State))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.SuggestZeroAllocationsRule,
            kind,
            AttributeNamespace + "ZeroAllocations",
            "[ZeroAllocations]",
            "no source-visible allocation sites",
            confidence);
    }

    private static void SuggestAllowedCapabilities(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        InferredContractSuggestionOptions options)
    {
        const string kind = "capabilities";
        const InferredContractConfidence confidence = InferredContractConfidence.High;
        if (!options.Includes(kind, confidence) ||
            session.AttributePolicy.HasAttribute(context.MethodSymbol, "AllowedCapabilitiesAttribute"))
            return;

        var result = context.State.GetCapabilityResult(context.CancellationToken);
        var capabilities = NormalizeCapabilities(result.Capabilities);
        if (result.HasUnknowns || (capabilities & ~AllKnownCapabilities) != 0) return;

        var flagExpressions = OrderedCapabilities
            .Where(capability => capability != SymbolicCapability.None && capabilities.HasFlag(capability))
            .Select(capability => AttributeNamespace + "SharpProofCapability." + capability)
            .ToArray();
        var argument = flagExpressions.Length == 0
            ? AttributeNamespace + "SharpProofCapability.None"
            : string.Join(" | ", flagExpressions);
        var displayArgument = capabilities == SymbolicCapability.None
            ? "SharpProofCapability.None"
            : string.Join(
                " | ",
                OrderedCapabilities
                    .Where(capability => capabilities.HasFlag(capability))
                    .Select(capability => "SharpProofCapability." + capability));
        var displaySet = capabilities == SymbolicCapability.None
            ? "no capabilities"
            : "the exact capability set " + string.Join(
                ", ",
                OrderedCapabilities.Where(capability => capabilities.HasFlag(capability)));

        Report(
            context,
            session,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesRule,
            kind,
            AttributeNamespace + "AllowedCapabilities(" + argument + ")",
            "[AllowedCapabilities(" + displayArgument + ")]",
            displaySet + " and no unknown capability sites",
            confidence);
    }

    private static void SuggestExpectedComplexity(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        InferredContractSuggestionOptions options)
    {
        const string kind = "complexity";
        const InferredContractConfidence confidence = InferredContractConfidence.High;
        if (!options.Includes(kind, confidence) ||
            session.AttributePolicy.HasAttribute(context.MethodSymbol, "ExpectedComplexityAttribute"))
            return;

        var result = context.State.GetComplexityResult(context.CancellationToken);
        if (result.Complexity.IsUnknown ||
            result.Complexity.IsRecursiveUnknown ||
            result.Complexity.IsConservative ||
            result.UnknownReasons.Count != 0 ||
            !TryGetComplexityKindName(result.Complexity.Kind, out var complexityKind))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.SuggestExpectedComplexityRule,
            kind,
            AttributeNamespace + "ExpectedComplexity(" + AttributeNamespace + "ComplexityKind." +
            complexityKind + ")",
            "[ExpectedComplexity(ComplexityKind." + complexityKind + ")]",
            "bounded symbolic complexity " + result.Complexity.Text + " with no unknown drivers",
            confidence);
    }

    private static void SuggestExceptionContract(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        InferredContractSuggestionOptions options)
    {
        const string kind = "exceptions";
        if (!options.IsEnabled ||
            !options.Kinds.Contains(kind) ||
            session.AttributePolicy.HasAttribute(context.MethodSymbol, "DoesNotThrowAttribute") ||
            session.AttributePolicy.HasAttribute(context.MethodSymbol, "AllowedExceptionsAttribute"))
            return;

        ExceptionFlowQuery.MethodExceptionQueryResult result;
        using (ExceptionFlowAnalyzer.UseAttributePolicy(session.AttributePolicy))
        {
            result = context.State.GetOrCreateSymbolicQueryResult(
                "exception-flow",
                () => ExceptionFlowQuery.AnalyzeMethod(
                    context.Node,
                    context.SemanticModel,
                    context.CancellationToken,
                    context.MethodSymbol,
                    session.ExceptionSummaryCatalog,
                    session.PurityService.SmtAnalysis,
                    session.AttributePolicy));
        }

        if (result.ExceptionEvidence.Count == 0)
        {
            const InferredContractConfidence confidence = InferredContractConfidence.High;
            if (!options.Includes(kind, confidence) || !IsTriviallyNonThrowingBody(context)) return;

            Report(
                context,
                session,
                SharpProofDiagnostics.SuggestExceptionContractRule,
                kind,
                AttributeNamespace + "DoesNotThrow",
                "[DoesNotThrow]",
                "a trivial closed body with no exception evidence",
                confidence);
            return;
        }

        const InferredContractConfidence allowedConfidence = InferredContractConfidence.Medium;
        if (!options.Includes(kind, allowedConfidence) ||
            !TryGetFiniteExceptionTypes(result, out var exceptionTypes, out var displayTypes))
            return;

        var arguments = string.Join(", ", exceptionTypes.Select(type => "typeof(" + type + ")"));
        Report(
            context,
            session,
            SharpProofDiagnostics.SuggestExceptionContractRule,
            kind,
            AttributeNamespace + "AllowedExceptions(" + arguments + ")",
            "[AllowedExceptions(" + string.Join(", ", displayTypes.Select(type => "typeof(" + type + ")")) + ")]",
            "a finite bounded exception set: " + string.Join(", ", displayTypes),
            allowedConfidence);
    }

    private static void SuggestEnsures(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        InferredContractSuggestionOptions options)
    {
        const string kind = "ensures";
        const InferredContractConfidence confidence = InferredContractConfidence.High;
        var method = context.MethodSymbol;
        if (!options.Includes(kind, confidence) ||
            session.AttributePolicy.HasAttribute(method, "EnsuresAttribute") ||
            method.ReturnsVoid ||
            method.ReturnsByRef ||
            method.ReturnsByRefReadonly ||
            method.IsAsync)
            return;

        var returns = GetReturnExpressions(context.Node).ToArray();
        if (returns.Length == 0) return;

        string? condition = null;
        foreach (var returnExpression in returns)
        {
            if (!TryInferEnsuresCondition(context, returnExpression, out var inferredCondition)) return;

            if (condition == null)
                condition = inferredCondition;
            else if (!string.Equals(condition, inferredCondition, StringComparison.Ordinal))
                return;
        }

        if (condition == null) return;

        Report(
            context,
            session,
            SharpProofDiagnostics.SuggestEnsuresRule,
            kind,
            AttributeNamespace + "Ensures(" + QuoteString(condition) + ")",
            "[Ensures(" + QuoteString(condition) + ")]",
            "a postcondition proved by every visible return: " + condition,
            confidence);
    }

    private static void SuggestRequires(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        InferredContractSuggestionOptions options)
    {
        const string kind = "requires";
        const InferredContractConfidence confidence = InferredContractConfidence.High;
        if (!options.Includes(kind, confidence) ||
            session.AttributePolicy.HasAttribute(context.MethodSymbol, "RequiresAttribute") ||
            !TryGetLeadingThrowGuard(context.Node, out var ifStatement) ||
            !TryNegateParameterGuard(context, ifStatement.Condition, out var condition))
            return;

        var position = RequiresContractHelpers.GetMethodEntrySpeculativePosition(context.Node);
        if (!RequiresContractHelpers.TryCreateCondition(
                context.SemanticModel,
                position,
                condition,
                context.CancellationToken,
                out _,
                out _,
                out _,
                out _))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.SuggestRequiresRule,
            kind,
            AttributeNamespace + "Requires(" + QuoteString(condition) + ")",
            "[Requires(" + QuoteString(condition) + ")]",
            "a leading throw guard whose normal-entry condition is " + condition,
            confidence);
    }

    private static void Report(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        DiagnosticDescriptor descriptor,
        string kind,
        string attributeExpression,
        string displayAttribute,
        string evidence,
        InferredContractConfidence confidence)
    {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
        var confidenceText = confidence.ToString().ToLowerInvariant();
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.SuggestedContractKindProperty, kind)
            .Add(SharpProofDiagnostics.SuggestedContractAttributeProperty, attributeExpression)
            .Add(SharpProofDiagnostics.SuggestedContractConfidenceProperty, confidenceText)
            .Add(SharpProofDiagnostics.SuggestedContractEvidenceProperty, evidence);
        properties = BaselineDiagnosticProperties.Add(
            properties,
            context.MethodSymbol,
            context.Node.SyntaxTree,
            "InferredContract",
            displayAttribute,
            kind + "|" + attributeExpression);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            displayAttribute,
            "suggested",
            confidenceText);
        var diagnostic = Diagnostic.Create(
            descriptor,
            location,
            null,
            properties,
            new object[]
            {
                context.MethodSymbol.Name,
                evidence,
                displayAttribute,
                confidenceText
            });
        if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static bool IsSupportedDeclaration(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            LocalFunctionStatementSyntax;
    }

    private static bool MatchesScope(IMethodSymbol methodSymbol, MissingPuritySuggestionScope scope)
    {
        return scope switch
        {
            MissingPuritySuggestionScope.All => true,
            MissingPuritySuggestionScope.Public =>
                methodSymbol.DeclaredAccessibility is Accessibility.Public or
                    Accessibility.Protected or
                    Accessibility.ProtectedOrInternal,
            MissingPuritySuggestionScope.Internal =>
                methodSymbol.DeclaredAccessibility is Accessibility.Internal or
                    Accessibility.ProtectedAndInternal or
                    Accessibility.ProtectedOrInternal,
            _ => false
        };
    }

    private static SymbolicCapability NormalizeCapabilities(SymbolicCapability capabilities)
    {
        if ((capabilities & (SymbolicCapability.FileRead |
                             SymbolicCapability.FileWrite |
                             SymbolicCapability.Network |
                             SymbolicCapability.Console |
                             SymbolicCapability.Registry)) != 0)
            capabilities |= SymbolicCapability.IO;

        return capabilities;
    }

    private static bool TryGetComplexityKindName(SymbolicComplexityKind kind, out string name)
    {
        name = kind switch
        {
            SymbolicComplexityKind.Constant => "Constant",
            SymbolicComplexityKind.Linear => "Linear",
            SymbolicComplexityKind.Product => "Product",
            SymbolicComplexityKind.Quadratic => "Quadratic",
            SymbolicComplexityKind.Max => "Max",
            _ => string.Empty
        };
        return name.Length != 0;
    }

    private static bool TryGetFiniteExceptionTypes(
        ExceptionFlowQuery.MethodExceptionQueryResult result,
        out string[] exceptionTypes,
        out string[] displayTypes)
    {
        var symbols = result.SiteEntries
            .Select(entry => entry.Exception.Type as INamedTypeSymbol)
            .ToArray();
        if (symbols.Length == 0 || symbols.Any(static symbol => symbol == null))
        {
            exceptionTypes = Array.Empty<string>();
            displayTypes = Array.Empty<string>();
            return false;
        }

        var distinctSymbols = symbols
            .Select(static symbol => symbol!)
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .OrderBy(static symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
            .ToArray();
        if (distinctSymbols.Length is < 1 or > 4)
        {
            exceptionTypes = Array.Empty<string>();
            displayTypes = Array.Empty<string>();
            return false;
        }

        exceptionTypes = distinctSymbols
            .Select(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToArray();
        displayTypes = distinctSymbols
            .Select(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            .ToArray();
        return true;
    }

    private static bool IsTriviallyNonThrowingBody(MethodBodyAnalysisContext context)
    {
        var expressionBody = context.Node switch
        {
            MethodDeclarationSyntax method => method.ExpressionBody?.Expression,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.ExpressionBody?.Expression,
            ConversionOperatorDeclarationSyntax conversion => conversion.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction => localFunction.ExpressionBody?.Expression,
            _ => null
        };
        if (expressionBody != null) return IsTriviallyNonThrowingExpression(context, expressionBody);

        var body = GetBody(context.Node);
        if (body == null || body.Statements.Count == 0) return body != null;

        return body.Statements.Count == 1 &&
               body.Statements[0] is ReturnStatementSyntax { Expression: { } returnExpression } &&
               IsTriviallyNonThrowingExpression(context, returnExpression);
    }

    private static bool IsTriviallyNonThrowingExpression(
        MethodBodyAnalysisContext context,
        ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);
        if (expression is LiteralExpressionSyntax or DefaultExpressionSyntax or TypeOfExpressionSyntax) return true;

        if (expression is InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            })
            return true;

        return expression is IdentifierNameSyntax identifier &&
               context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is IParameterSymbol;
    }

    private static IEnumerable<ExpressionSyntax> GetReturnExpressions(SyntaxNode node)
    {
        var expressionBody = node switch
        {
            MethodDeclarationSyntax method => method.ExpressionBody?.Expression,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.ExpressionBody?.Expression,
            ConversionOperatorDeclarationSyntax conversion => conversion.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction => localFunction.ExpressionBody?.Expression,
            _ => null
        };
        if (expressionBody != null)
        {
            yield return expressionBody;
            yield break;
        }

        var body = GetBody(node);
        if (body == null) yield break;

        foreach (var returnStatement in body
                     .DescendantNodes(static candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate))
                     .OfType<ReturnStatementSyntax>())
            if (returnStatement.Expression != null)
                yield return returnStatement.Expression;
    }

    private static bool TryInferEnsuresCondition(
        MethodBodyAnalysisContext context,
        ExpressionSyntax expression,
        out string condition)
    {
        expression = StripParentheses(expression);
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant.HasValue &&
            TryFormatContractConstant(constant.Value, context.MethodSymbol.ReturnType, out var literal))
        {
            condition = "result == " + literal;
            return true;
        }

        if (expression is IdentifierNameSyntax identifier &&
            context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is IParameterSymbol)
        {
            condition = "result == " + identifier.Identifier.Text;
            return true;
        }

        if (context.MethodSymbol.ReturnType.IsReferenceType &&
            expression is ObjectCreationExpressionSyntax or
                ImplicitObjectCreationExpressionSyntax or
                ArrayCreationExpressionSyntax or
                ImplicitArrayCreationExpressionSyntax or
                AnonymousObjectCreationExpressionSyntax or
                InterpolatedStringExpressionSyntax or
                TypeOfExpressionSyntax)
        {
            condition = "result != null";
            return true;
        }

        condition = string.Empty;
        return false;
    }

    private static bool TryFormatContractConstant(
        object? value,
        ITypeSymbol returnType,
        out string literal)
    {
        if (value == null)
        {
            literal = "null";
            return returnType.IsReferenceType || returnType.NullableAnnotation == NullableAnnotation.Annotated;
        }

        if (returnType.TypeKind == TypeKind.Enum)
        {
            literal = string.Empty;
            return false;
        }

        switch (value)
        {
            case bool boolean:
                literal = boolean ? "true" : "false";
                return true;
            case sbyte or byte or short or ushort or int:
                literal = Convert.ToString(value, CultureInfo.InvariantCulture)!;
                return true;
            case uint unsignedInteger:
                literal = unsignedInteger.ToString(CultureInfo.InvariantCulture) + "U";
                return true;
            case long longInteger:
                literal = longInteger.ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            case ulong unsignedLongInteger:
                literal = unsignedLongInteger.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            case char character:
                literal = SyntaxFactory.LiteralExpression(
                        SyntaxKind.CharacterLiteralExpression,
                        SyntaxFactory.Literal(character))
                    .ToString();
                return true;
            case string text:
                literal = QuoteString(text);
                return true;
            default:
                literal = string.Empty;
                return false;
        }
    }

    private static bool TryGetLeadingThrowGuard(SyntaxNode node, out IfStatementSyntax ifStatement)
    {
        var body = GetBody(node);
        if (body?.Statements.FirstOrDefault() is IfStatementSyntax candidate &&
            candidate.Else == null &&
            IsThrowOnly(candidate.Statement))
        {
            ifStatement = candidate;
            return true;
        }

        ifStatement = null!;
        return false;
    }

    private static bool IsThrowOnly(StatementSyntax statement)
    {
        return statement is ThrowStatementSyntax ||
               statement is BlockSyntax { Statements.Count: 1 } block &&
               block.Statements[0] is ThrowStatementSyntax;
    }

    private static bool TryNegateParameterGuard(
        MethodBodyAnalysisContext context,
        ExpressionSyntax expression,
        out string condition)
    {
        expression = StripParentheses(expression);
        if (expression is BinaryExpressionSyntax binary &&
            TryGetNegatedOperator(binary.Kind(), out var negatedOperator) &&
            HasOneParameterAndOneConstant(context, binary.Left, binary.Right))
        {
            condition = binary.Left.WithoutTrivia() + " " + negatedOperator + " " +
                        binary.Right.WithoutTrivia();
            return true;
        }

        if (expression is IsPatternExpressionSyntax isPattern &&
            IsParameterReference(context, isPattern.Expression) &&
            TryGetNullPatternPolarity(isPattern.Pattern, out var matchesNull))
        {
            condition = isPattern.Expression.WithoutTrivia() + (matchesNull ? " != null" : " == null");
            return true;
        }

        condition = string.Empty;
        return false;
    }

    private static bool HasOneParameterAndOneConstant(
        MethodBodyAnalysisContext context,
        ExpressionSyntax left,
        ExpressionSyntax right)
    {
        var leftIsParameter = IsParameterReference(context, left);
        var rightIsParameter = IsParameterReference(context, right);
        return leftIsParameter != rightIsParameter &&
               (leftIsParameter
                   ? context.SemanticModel.GetConstantValue(right, context.CancellationToken).HasValue
                   : context.SemanticModel.GetConstantValue(left, context.CancellationToken).HasValue);
    }

    private static bool IsParameterReference(MethodBodyAnalysisContext context, ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);
        return expression is IdentifierNameSyntax identifier &&
               context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is IParameterSymbol;
    }

    private static bool TryGetNullPatternPolarity(PatternSyntax pattern, out bool matchesNull)
    {
        if (pattern is ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression })
        {
            matchesNull = true;
            return true;
        }

        if (pattern is UnaryPatternSyntax
            {
                RawKind: (int)SyntaxKind.NotPattern,
                Pattern: ConstantPatternSyntax
                {
                    Expression.RawKind: (int)SyntaxKind.NullLiteralExpression
                }
            })
        {
            matchesNull = false;
            return true;
        }

        matchesNull = false;
        return false;
    }

    private static bool TryGetNegatedOperator(SyntaxKind kind, out string negatedOperator)
    {
        negatedOperator = kind switch
        {
            SyntaxKind.EqualsExpression => "!=",
            SyntaxKind.NotEqualsExpression => "==",
            SyntaxKind.LessThanExpression => ">=",
            SyntaxKind.LessThanOrEqualExpression => ">",
            SyntaxKind.GreaterThanExpression => "<=",
            SyntaxKind.GreaterThanOrEqualExpression => "<",
            _ => string.Empty
        };
        return negatedOperator.Length != 0;
    }

    private static BlockSyntax? GetBody(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Body,
            ConstructorDeclarationSyntax constructor => constructor.Body,
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.Body,
            ConversionOperatorDeclarationSyntax conversion => conversion.Body,
            LocalFunctionStatementSyntax localFunction => localFunction.Body,
            _ => null
        };
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    private static string QuoteString(string value)
    {
        return SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(value))
            .ToString();
    }
}
