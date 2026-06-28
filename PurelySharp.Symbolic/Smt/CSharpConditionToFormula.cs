using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Smt
{
    public static class CSharpConditionToFormula
    {
        private const int MaxSourcePredicateInlineDepth = 4;

        public static bool TryTranslate(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                formula = new SmtBooleanConstant(booleanValue);
                return true;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var directValue, getSymbolVersion, inlineDepth) &&
                directValue is { Kind: SmtValueKind.Bool })
            {
                formula = directValue;
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryTranslate(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                operand != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslateValue(conditionalExpression, semanticModel, cancellationToken, out var conditionalValue, getSymbolVersion, inlineDepth) &&
                conditionalValue is { Kind: SmtValueKind.Bool })
            {
                formula = conditionalValue;
                return true;
            }

            if (expression is InvocationExpressionSyntax invocationExpression &&
                TryTranslateSourceBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out var invocationFormula, getSymbolVersion, inlineDepth) &&
                invocationFormula != null)
            {
                formula = invocationFormula;
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.IsExpression) &&
                    binaryExpression.Right is TypeSyntax &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var typeTestValue, getSymbolVersion, inlineDepth) &&
                    typeTestValue is { Kind: SmtValueKind.Reference })
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, typeTestValue, new SmtNullConstant());
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd, getSymbolVersion, inlineDepth) &&
                    leftAnd != null &&
                    rightAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr, getSymbolVersion, inlineDepth) &&
                    leftOr != null &&
                    rightOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (TryTranslateUnsignedCastBoundsComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var unsignedBoundsFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    unsignedBoundsFormula != null)
                {
                    formula = unsignedBoundsFormula;
                    return true;
                }

                if (TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth) &&
                    leftValue != null &&
                    rightValue != null &&
                    TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
                {
                    formula = comparison;
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression &&
                TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out var patternFormula, getSymbolVersion, inlineDepth))
            {
                formula = patternFormula;
                return true;
            }

            formula = null;
            return false;
        }

        private static bool TryTranslateUnsignedCastBoundsComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.LessThanExpression) &&
                !binaryExpression.IsKind(SyntaxKind.GreaterThanOrEqualExpression))
            {
                return false;
            }

            if (!TryCreateUnsignedCastBoundsInRangeFormula(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = binaryExpression.IsKind(SyntaxKind.LessThanExpression)
                ? inRangeFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula);
            return true;
        }

        private static bool TryCreateUnsignedCastBoundsInRangeFormula(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (!TryGetUnsignedCastOperand(leftExpression, semanticModel, cancellationToken, out var indexExpression, out var leftUnsignedType) ||
                !TryGetUnsignedCastOperand(rightExpression, semanticModel, cancellationToken, out var lengthExpression, out var rightUnsignedType) ||
                leftUnsignedType != rightUnsignedType ||
                !IsKnownNonNegativeIntegralExpression(lengthExpression, semanticModel, cancellationToken) ||
                !TryTranslateValue(indexExpression, semanticModel, cancellationToken, out var indexFormula, getSymbolVersion, inlineDepth) ||
                indexFormula is not { Kind: SmtValueKind.Int } ||
                !TryTranslateValue(lengthExpression, semanticModel, cancellationToken, out var lengthFormula, getSymbolVersion, inlineDepth) ||
                lengthFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            return true;
        }

        private static bool TryGetUnsignedCastOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax operand,
            out SpecialType unsignedType)
        {
            expression = UnwrapExpression(expression);
            if (expression is not CastExpressionSyntax castExpression)
            {
                operand = null!;
                unsignedType = SpecialType.None;
                return false;
            }

            var castType = semanticModel.GetTypeInfo(castExpression.Type, cancellationToken).Type;
            if (castType?.SpecialType is not SpecialType.System_UInt32 and not SpecialType.System_UInt64)
            {
                operand = null!;
                unsignedType = SpecialType.None;
                return false;
            }

            operand = castExpression.Expression;
            unsignedType = castType.SpecialType;
            return true;
        }

        private static bool IsKnownNonNegativeIntegralExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue &&
                TryGetIntegralConstant(constantValue.Value!, out var integralValue))
            {
                return integralValue >= 0;
            }

            return expression is MemberAccessExpressionSyntax memberAccess &&
                IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken);
        }

        private static bool TryTranslateSourceBooleanInvocation(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (inlineDepth >= MaxSourcePredicateInlineDepth ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                !CanInlineSourceBooleanPredicate(invocationOperation.TargetMethod) ||
                !TryGetReturnedBooleanExpression(
                    invocationOperation.TargetMethod,
                    semanticModel.Compilation,
                    cancellationToken,
                    out var returnedExpression,
                    out var returnedSemanticModel) ||
                !TryTranslate(returnedExpression, returnedSemanticModel, cancellationToken, out var returnedFormula, getSymbolVersion: null, inlineDepth + 1) ||
                returnedFormula is not { Kind: SmtValueKind.Bool } ||
                !TryCreateSourcePredicateSubstitutions(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    inlineDepth,
                    out var substitutions))
            {
                return false;
            }

            formula = SubstituteVariables(returnedFormula, substitutions);
            return true;
        }

        private static bool CanInlineSourceBooleanPredicate(IMethodSymbol methodSymbol)
        {
            return methodSymbol is
            {
                ReturnsVoid: false,
                ReturnsByRef: false,
                ReturnsByRefReadonly: false,
                ReturnType.SpecialType: SpecialType.System_Boolean,
                DeclaringSyntaxReferences.Length: > 0
            } &&
                methodSymbol.Parameters.All(static parameter => parameter.RefKind == RefKind.None);
        }

        private static bool TryGetReturnedBooleanExpression(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            CancellationToken cancellationToken,
            out ExpressionSyntax returnedExpression,
            out SemanticModel returnedSemanticModel)
        {
            returnedExpression = null!;
            returnedSemanticModel = null!;

            var callableSyntax = methodSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .FirstOrDefault();
            if (callableSyntax == null ||
                !TryGetSingleReturnedExpressionSyntax(callableSyntax, out returnedExpression))
            {
                return false;
            }

            returnedSemanticModel = compilation.GetSemanticModel(returnedExpression.SyntaxTree);
            return true;
        }

        private static bool TryGetSingleReturnedExpressionSyntax(
            SyntaxNode callableSyntax,
            out ExpressionSyntax returnedExpression)
        {
            switch (callableSyntax)
            {
                case MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.ExpressionBody?.Expression != null:
                    returnedExpression = methodDeclarationSyntax.ExpressionBody.Expression;
                    return true;
                case MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.Body != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(methodDeclarationSyntax.Body, out returnedExpression);
                case LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.ExpressionBody?.Expression != null:
                    returnedExpression = localFunctionStatementSyntax.ExpressionBody.Expression;
                    return true;
                case LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.Body != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(localFunctionStatementSyntax.Body, out returnedExpression);
                default:
                    returnedExpression = null!;
                    return false;
            }
        }

        private static bool TryGetSingleReturnedExpressionSyntaxFromBody(
            BlockSyntax bodySyntax,
            out ExpressionSyntax returnedExpression)
        {
            if (bodySyntax.Statements.Count != 1 ||
                bodySyntax.Statements[0] is not ReturnStatementSyntax returnStatement ||
                returnStatement.Expression == null)
            {
                returnedExpression = null!;
                return false;
            }

            returnedExpression = returnStatement.Expression!;
            return true;
        }

        private static bool TryCreateSourcePredicateSubstitutions(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            out IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            var builder = new List<SmtVariableSubstitution>(invocationOperation.Arguments.Length);
            foreach (var argument in invocationOperation.Arguments)
            {
                var parameter = argument.Parameter;
                if (parameter == null ||
                    !TryGetValueKind(parameter.Type, out var parameterKind) ||
                    argument.Value.Syntax is not ExpressionSyntax argumentExpression)
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                if (!TryTranslateValue(argumentExpression, semanticModel, cancellationToken, out var argumentFormula, getSymbolVersion, inlineDepth) ||
                    argumentFormula == null ||
                    argumentFormula.Kind != parameterKind)
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                var formalVariable = new SmtVariable(GetVariableName(parameter, getSymbolVersion: null), parameterKind);
                builder.Add(new SmtVariableSubstitution(
                    formalVariable.Name,
                    formalVariable.Name + ".",
                    formalVariable + ".",
                    argumentFormula));
            }

            substitutions = builder;
            return true;
        }

        private static SmtFormula SubstituteVariables(
            SmtFormula formula,
            IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            switch (formula)
            {
                case SmtVariable variable:
                    return SubstituteVariable(variable, substitutions);
                case SmtUnaryFormula unary:
                    return new SmtUnaryFormula(unary.Operator, SubstituteVariables(unary.Operand, substitutions));
                case SmtBinaryFormula binary:
                    return new SmtBinaryFormula(
                        binary.Operator,
                        SubstituteVariables(binary.Left, substitutions),
                        SubstituteVariables(binary.Right, substitutions));
                case SmtIntegerUnaryTerm unary:
                    return new SmtIntegerUnaryTerm(unary.Operator, SubstituteVariables(unary.Operand, substitutions));
                case SmtIntegerBinaryTerm binary:
                    return new SmtIntegerBinaryTerm(
                        binary.Operator,
                        SubstituteVariables(binary.Left, substitutions),
                        SubstituteVariables(binary.Right, substitutions));
                case SmtConditionalFormula conditional:
                    return new SmtConditionalFormula(
                        SubstituteVariables(conditional.Condition, substitutions),
                        SubstituteVariables(conditional.WhenTrue, substitutions),
                        SubstituteVariables(conditional.WhenFalse, substitutions),
                        conditional.ResultKind);
                default:
                    return formula;
            }
        }

        private static SmtFormula SubstituteVariable(
            SmtVariable variable,
            IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            foreach (var substitution in substitutions)
            {
                if (string.Equals(variable.Name, substitution.ExactName, StringComparison.Ordinal))
                {
                    return substitution.Replacement;
                }

                if (TrySubstituteMemberVariable(variable, substitution.SimpleMemberPrefix, substitution.Replacement, out var simpleMemberReplacement) ||
                    TrySubstituteMemberVariable(variable, substitution.FormulaMemberPrefix, substitution.Replacement, out simpleMemberReplacement))
                {
                    return simpleMemberReplacement;
                }
            }

            return variable;
        }

        private static bool TrySubstituteMemberVariable(
            SmtVariable variable,
            string memberPrefix,
            SmtFormula replacement,
            out SmtFormula substituted)
        {
            if (!variable.Name.StartsWith(memberPrefix, StringComparison.Ordinal))
            {
                substituted = null!;
                return false;
            }

            var suffix = variable.Name.Substring(memberPrefix.Length - 1);
            substituted = new SmtVariable(replacement + suffix, variable.Kind);
            return true;
        }

        private sealed class SmtVariableSubstitution
        {
            public SmtVariableSubstitution(
                string exactName,
                string simpleMemberPrefix,
                string formulaMemberPrefix,
                SmtFormula replacement)
            {
                ExactName = exactName;
                SimpleMemberPrefix = simpleMemberPrefix;
                FormulaMemberPrefix = formulaMemberPrefix;
                Replacement = replacement;
            }

            public string ExactName { get; }

            public string SimpleMemberPrefix { get; }

            public string FormulaMemberPrefix { get; }

            public SmtFormula Replacement { get; }
        }

        public static bool TryGetKnownStringLength(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out int length)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: string stringValue })
            {
                length = stringValue.Length;
                return true;
            }

            if (IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken))
            {
                length = 0;
                return true;
            }

            length = default;
            return false;
        }

        private static bool IsStringEmptyMemberAccess(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "Empty" &&
                semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IFieldSymbol
                {
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_String
                };
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas)
        {
            return TryCollectBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion: null);
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var originalCount = formulas.Count;
            TryCollectDomainFacts(expression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            AddBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            return formulas.Count > originalCount;
        }

        public static bool TryCollectPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            var originalCount = formulas.Count;
            AddPatternBindingFacts(
                matchedValue,
                matchedValueType,
                pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
            return formulas.Count > originalCount;
        }

        public static bool TryTranslateBuiltInElementAccessInRange(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            formula = null!;
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (receiverType is not IArrayTypeSymbol { Rank: 1 } &&
                receiverType?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            if (!TryCreateBuiltInElementAccessLengthFormula(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryCreateEffectiveBuiltInIndexFormula(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            return true;
        }

        public static bool TryTranslateBuiltInLengthValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryCreateBuiltInElementAccessLengthFormula(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        public static bool TryCollectDomainFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            var originalCount = formulas.Count;
            expression = UnwrapExpression(expression);

            foreach (var memberAccess in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
            {
                if (!IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken) ||
                    !TryTranslateValue(memberAccess, semanticModel, cancellationToken, out var lengthFormula, getSymbolVersion) ||
                    lengthFormula is not { Kind: SmtValueKind.Int })
                {
                    continue;
                }

                formulas.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(0)));
            }

            return formulas.Count > originalCount;
        }

        private static void AddBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                AddBranchAssumptions(prefixUnary.Operand, !branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
                return;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (branchWhenTrue &&
                    binaryExpression.IsKind(SyntaxKind.BitwiseAndExpression) &&
                    HasSupportedBooleanType(binaryExpression, semanticModel, cancellationToken))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (!branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (!branchWhenTrue &&
                    binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression) &&
                    HasSupportedBooleanType(binaryExpression, semanticModel, cancellationToken))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }
            }

            if (branchWhenTrue)
            {
                AddPatternBindingFacts(expression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }

            if (!TryTranslate(expression, semanticModel, cancellationToken, out var formula, getSymbolVersion) ||
                formula == null)
            {
                return;
            }

            formulas.Add(branchWhenTrue
                ? formula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, formula));
        }

        private static void AddPatternBindingFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is not IsPatternExpressionSyntax isPatternExpression ||
                !TryTranslateValue(isPatternExpression.Expression, semanticModel, cancellationToken, out var matchedValue, getSymbolVersion) ||
                matchedValue == null)
            {
                return;
            }

            var valueType = semanticModel.GetTypeInfo(isPatternExpression.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(isPatternExpression.Expression, cancellationToken).Type;
            AddPatternBindingFacts(
                matchedValue,
                valueType,
                isPatternExpression.Pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        private static void AddPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                AddPatternBindingFacts(
                    matchedValue,
                    matchedValueType,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                return;
            }

            switch (pattern)
            {
                case VarPatternSyntax varPattern:
                    AddDesignationBindingFact(matchedValue, varPattern.Designation, semanticModel, formulas, getSymbolVersion);
                    return;
                case DeclarationPatternSyntax declarationPattern:
                    AddDesignationBindingFact(matchedValue, declarationPattern.Designation, semanticModel, formulas, getSymbolVersion);
                    return;
                case RecursivePatternSyntax recursivePattern:
                    AddDesignationBindingFact(matchedValue, recursivePattern.Designation, semanticModel, formulas, getSymbolVersion);
                    AddRecursivePropertyPatternBindingFacts(
                        matchedValue,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
                case BinaryPatternSyntax binaryPattern when binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword):
                    AddPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        binaryPattern.Left,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    AddPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        binaryPattern.Right,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
                case ListPatternSyntax listPattern:
                    AddListPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        listPattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
            }
        }

        private static void AddListPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryGetBuiltInListPatternElementType(matchedValueType, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return;
            }

            for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
            {
                var subpattern = listPattern.Patterns[patternIndex];
                if (subpattern is SlicePatternSyntax)
                {
                    continue;
                }

                if (!TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex, out var fromEnd))
                {
                    continue;
                }

                var elementValue = CreateListPatternElementFormula(matchedValue, elementIndex, fromEnd, elementKind);
                AddPatternBindingFacts(
                    elementValue,
                    elementType,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddRecursivePropertyPatternBindingFacts(
            SmtFormula matchedValue,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var subpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (subpatterns == null)
            {
                return;
            }

            foreach (var subpattern in subpatterns.Value)
            {
                if (subpattern.NameColon?.Name == null)
                {
                    continue;
                }

                var memberSymbol = semanticModel.GetSymbolInfo(subpattern.NameColon.Name, cancellationToken).Symbol;
                if (!TryGetMemberType(memberSymbol, out var memberType) ||
                    !TryCreateMemberFormula(matchedValue, memberSymbol!.Name, memberType, out var memberValue) ||
                    memberValue == null)
                {
                    continue;
                }

                AddPatternBindingFacts(
                    memberValue,
                    memberType,
                    subpattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddDesignationBindingFact(
            SmtFormula matchedValue,
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                singleVariableDesignation.Identifier.ValueText == "_" ||
                semanticModel.GetDeclaredSymbol(singleVariableDesignation) is not ILocalSymbol localSymbol ||
                !TryCreateSymbolFormula(localSymbol, getSymbolVersion, out var localValue) ||
                !AreComparable(localValue, matchedValue))
            {
                return;
            }

            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, localValue, matchedValue));
        }

        private static bool TryTranslatePatternExpression(
            IsPatternExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateValue(expression.Expression, semanticModel, cancellationToken, out var value, getSymbolVersion, inlineDepth) ||
                value == null)
            {
                return false;
            }

            var valueType = semanticModel.GetTypeInfo(expression.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(expression.Expression, cancellationToken).Type;
            return TryTranslatePattern(value, expression.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, valueType, inlineDepth);
        }

        public static bool TryTranslatePattern(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            ITypeSymbol? valueType = null,
            int inlineDepth = 0)
        {
            formula = null;

            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return TryTranslatePattern(value, parenthesizedPattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, valueType, inlineDepth);
            }

            if (pattern is DiscardPatternSyntax or VarPatternSyntax)
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion, inlineDepth) &&
                constantValue != null &&
                AreComparable(value, constantValue))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, constantValue);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                TryTranslatePattern(value, unaryPattern.Pattern, semanticModel, cancellationToken, out var negatedPattern, getSymbolVersion, valueType, inlineDepth) &&
                negatedPattern != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                TryTranslatePattern(value, binaryPattern.Left, semanticModel, cancellationToken, out var leftPattern, getSymbolVersion, valueType, inlineDepth) &&
                TryTranslatePattern(value, binaryPattern.Right, semanticModel, cancellationToken, out var rightPattern, getSymbolVersion, valueType, inlineDepth) &&
                leftPattern != null &&
                rightPattern != null)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftPattern, rightPattern);
                    return true;
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftPattern, rightPattern);
                    return true;
                }
            }

            if (pattern is RelationalPatternSyntax relationalPattern &&
                value.Kind == SmtValueKind.Int &&
                TryTranslateValue(relationalPattern.Expression, semanticModel, cancellationToken, out var relationalValue, getSymbolVersion, inlineDepth) &&
                relationalValue is { Kind: SmtValueKind.Int })
            {
                switch (relationalPattern.OperatorToken.Kind())
                {
                    case SyntaxKind.GreaterThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, value, relationalValue);
                        return true;
                    case SyntaxKind.GreaterThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThan, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, value, relationalValue);
                        return true;
                }
            }

            if (pattern is RecursivePatternSyntax recursivePattern)
            {
                return TryTranslateRecursivePattern(value, recursivePattern, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            if (pattern is ListPatternSyntax listPattern)
            {
                return TryTranslateListPattern(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (pattern is DeclarationPatternSyntax or TypePatternSyntax)
            {
                if (value.Kind != SmtValueKind.Reference)
                {
                    return false;
                }

                formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant());
                return true;
            }

            return false;
        }

        private static bool TryTranslateRecursivePattern(
            SmtFormula value,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            SmtFormula? current = value.Kind == SmtValueKind.Reference
                ? new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant())
                : null;

            var subpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (subpatterns == null || subpatterns.Value.Count == 0)
            {
                formula = current;
                return formula != null;
            }

            foreach (var subpattern in subpatterns.Value)
            {
                if (!TryTranslatePropertySubpattern(value, subpattern, semanticModel, cancellationToken, out var subpatternFormula, getSymbolVersion, inlineDepth) ||
                    subpatternFormula == null)
                {
                    return false;
                }

                current = current == null
                    ? subpatternFormula
                    : new SmtBinaryFormula(SmtBinaryOperator.And, current, subpatternFormula);
            }

            formula = current;
            return formula != null;
        }

        private static bool TryTranslatePropertySubpattern(
            SmtFormula receiver,
            SubpatternSyntax subpattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (subpattern.NameColon?.Name == null)
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(subpattern.NameColon.Name, cancellationToken).Symbol;
            if (!TryGetMemberType(memberSymbol, out var memberType) ||
                !TryCreateMemberFormula(receiver, memberSymbol!.Name, memberType, out var memberValue) ||
                memberValue == null)
            {
                return false;
            }

            return TryTranslatePattern(memberValue, subpattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, memberType, inlineDepth);
        }

        private static bool TryTranslateListPattern(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (value.Kind != SmtValueKind.Reference ||
                !IsSupportedBuiltInListPatternReceiver(valueType))
            {
                return false;
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (!TryCreateMemberFormula(value, "Length", intType, out var lengthFormula) ||
                lengthFormula == null)
            {
                return false;
            }

            var hasSlice = false;
            var minimumLength = 0;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        minimumLength += GetListPatternMinimumLength(nestedListPattern);
                    }

                    hasSlice = true;
                    continue;
                }

                minimumLength++;
            }

            var nonNullFormula = new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                value,
                new SmtNullConstant());
            var lengthFormulaCondition = hasSlice
                ? new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength))
                : new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength));

            formula = new SmtBinaryFormula(SmtBinaryOperator.And, nonNullFormula, lengthFormulaCondition);
            AddListPatternElementConditions(
                value,
                valueType,
                listPattern,
                semanticModel,
                cancellationToken,
                ref formula,
                getSymbolVersion,
                inlineDepth);
            return true;
        }

        private static void AddListPatternElementConditions(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ref SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (formula == null ||
                !TryGetBuiltInListPatternElementType(valueType, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return;
            }

            for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
            {
                var subpattern = listPattern.Patterns[patternIndex];
                if (subpattern is SlicePatternSyntax)
                {
                    continue;
                }

                if (!TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex, out var fromEnd))
                {
                    continue;
                }

                var elementValue = CreateListPatternElementFormula(value, elementIndex, fromEnd, elementKind);
                if (TryTranslatePattern(
                        elementValue,
                        subpattern,
                        semanticModel,
                        cancellationToken,
                        out var elementCondition,
                        getSymbolVersion,
                        elementType,
                        inlineDepth) &&
                    elementCondition != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, formula, elementCondition);
                }
            }
        }

        private static bool TryGetListPatternElementPosition(
            ListPatternSyntax listPattern,
            int patternIndex,
            out int elementIndex,
            out bool fromEnd)
        {
            elementIndex = 0;
            fromEnd = false;

            if (listPattern.Patterns[patternIndex] is SlicePatternSyntax)
            {
                return false;
            }

            var sliceIndex = -1;
            for (var index = 0; index < listPattern.Patterns.Count; index++)
            {
                if (listPattern.Patterns[index] is SlicePatternSyntax)
                {
                    sliceIndex = index;
                    break;
                }
            }

            if (sliceIndex < 0 || patternIndex < sliceIndex)
            {
                elementIndex = patternIndex;
                return true;
            }

            elementIndex = listPattern.Patterns.Count - patternIndex;
            fromEnd = true;
            return true;
        }

        private static SmtFormula CreateListPatternElementFormula(
            SmtFormula receiver,
            int elementIndex,
            bool fromEnd,
            SmtValueKind elementKind)
        {
            var indexText = fromEnd
                ? "^" + elementIndex.ToString(CultureInfo.InvariantCulture)
                : elementIndex.ToString(CultureInfo.InvariantCulture);
            return new SmtVariable(receiver + "[" + indexText + "]", elementKind);
        }

        private static int GetListPatternMinimumLength(ListPatternSyntax listPattern)
        {
            var minimumLength = 0;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        minimumLength += GetListPatternMinimumLength(nestedListPattern);
                    }

                    continue;
                }

                minimumLength++;
            }

            return minimumLength;
        }

        private static bool TryGetNestedListPattern(PatternSyntax? pattern, out ListPatternSyntax listPattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            if (pattern is ListPatternSyntax candidate)
            {
                listPattern = candidate;
                return true;
            }

            listPattern = null!;
            return false;
        }

        private static bool IsSupportedBuiltInListPatternReceiver(ITypeSymbol? valueType)
        {
            return valueType is IArrayTypeSymbol { Rank: 1 } ||
                valueType?.SpecialType == SpecialType.System_String;
        }

        private static bool TryGetBuiltInListPatternElementType(ITypeSymbol? valueType, out ITypeSymbol elementType)
        {
            if (valueType is IArrayTypeSymbol { Rank: 1 } arrayType)
            {
                elementType = arrayType.ElementType;
                return true;
            }

            elementType = null!;
            return false;
        }

        private static bool TryCreateBuiltInElementAccessLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            receiverExpression = UnwrapExpression(receiverExpression);
            var receiverTypeInfo = semanticModel.GetTypeInfo(receiverExpression, cancellationToken);
            if ((receiverTypeInfo.Type is IArrayTypeSymbol { Rank: 1 } ||
                 receiverTypeInfo.ConvertedType is IArrayTypeSymbol { Rank: 1 }) &&
                TryCreateArrayLengthFormula(receiverExpression, semanticModel, cancellationToken, out lengthFormula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (TryGetKnownStringLength(receiverExpression, semanticModel, cancellationToken, out var knownStringLength))
            {
                lengthFormula = new SmtIntegerConstant(knownStringLength);
                return true;
            }

            if (!TryTranslateValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference })
            {
                lengthFormula = null!;
                return false;
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (!TryCreateMemberFormula(receiverFormula, "Length", intType, out var candidate) ||
                candidate is not { Kind: SmtValueKind.Int })
            {
                lengthFormula = null!;
                return false;
            }

            lengthFormula = candidate;
            return true;
        }

        private static bool TryCreateArrayLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (receiverExpression is ArrayCreationExpressionSyntax arrayCreation)
            {
                if (arrayCreation.Type.RankSpecifiers.Count == 1 &&
                    arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
                    !arrayCreation.Type.RankSpecifiers[0].Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                    TryTranslateValue(
                        arrayCreation.Type.RankSpecifiers[0].Sizes[0],
                        semanticModel,
                        cancellationToken,
                        out var sizeFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    sizeFormula is { Kind: SmtValueKind.Int })
                {
                    lengthFormula = sizeFormula;
                    return true;
                }

                if (arrayCreation.Initializer != null)
                {
                    lengthFormula = new SmtIntegerConstant(arrayCreation.Initializer.Expressions.Count);
                    return true;
                }
            }

            if (receiverExpression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
            {
                lengthFormula = new SmtIntegerConstant(implicitArrayCreation.Initializer.Expressions.Count);
                return true;
            }

            if (TryCreateCollectionExpressionLengthFormula(receiverExpression, out lengthFormula))
            {
                return true;
            }

            if (IsArrayEmptyInvocation(receiverExpression, semanticModel, cancellationToken))
            {
                lengthFormula = new SmtIntegerConstant(0);
                return true;
            }

            lengthFormula = null!;
            return false;
        }

        private static bool TryCreateCollectionExpressionLengthFormula(
            ExpressionSyntax receiverExpression,
            out SmtFormula lengthFormula)
        {
            if (receiverExpression is not CollectionExpressionSyntax collectionExpression ||
                collectionExpression.Elements.Any(static element => element is not ExpressionElementSyntax))
            {
                lengthFormula = null!;
                return false;
            }

            lengthFormula = new SmtIntegerConstant(collectionExpression.Elements.Count);
            return true;
        }

        private static bool IsArrayEmptyInvocation(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return receiverExpression is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol
                {
                    Name: "Empty",
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_Array
                };
        }

        private static bool TryCreateEffectiveBuiltInIndexFormula(
            ExpressionSyntax indexExpression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula indexFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            indexExpression = UnwrapElementAccessIndexExpression(indexExpression);
            if (indexExpression is PrefixUnaryExpressionSyntax fromEndIndex &&
                fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
            {
                if (!TryTranslateValue(
                        fromEndIndex.Operand,
                        semanticModel,
                        cancellationToken,
                        out var fromEndOffset,
                        getSymbolVersion,
                        inlineDepth) ||
                    fromEndOffset is not { Kind: SmtValueKind.Int })
                {
                    indexFormula = null!;
                    return false;
                }

                indexFormula = new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Subtract,
                    lengthFormula,
                    fromEndOffset);
                return true;
            }

            if (!TryTranslateValue(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var ordinaryIndex,
                    getSymbolVersion,
                    inlineDepth) ||
                ordinaryIndex is not { Kind: SmtValueKind.Int })
            {
                indexFormula = null!;
                return false;
            }

            indexFormula = ordinaryIndex;
            return true;
        }

        private static ExpressionSyntax UnwrapElementAccessIndexExpression(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression;
        }

        private static bool IsBuiltInNonNegativeLengthAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (memberAccess.Name.Identifier.ValueText != "Length")
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
            if (memberSymbol is not IPropertySymbol and not IFieldSymbol)
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            return receiverType is IArrayTypeSymbol ||
                receiverType?.SpecialType == SpecialType.System_String;
        }

        private static bool TryTranslateComparison(
            SyntaxKind kind,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            switch (kind)
            {
                case SyntaxKind.EqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.NotEqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.LessThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThan, left, right, out formula);
                case SyntaxKind.LessThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThanOrEqual, left, right, out formula);
                case SyntaxKind.GreaterThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThan, left, right, out formula);
                case SyntaxKind.GreaterThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThanOrEqual, left, right, out formula);
                default:
                    return false;
            }
        }

        private static bool TryCreateIntegralComparison(
            SmtBinaryOperator comparison,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            if (left.Kind != SmtValueKind.Int || right.Kind != SmtValueKind.Int)
            {
                return false;
            }

            formula = new SmtBinaryFormula(comparison, left, right);
            return true;
        }

        private static bool AreComparable(SmtFormula left, SmtFormula right)
        {
            if (left.Kind == right.Kind)
            {
                return true;
            }

            return (left is SmtNullConstant && right.Kind == SmtValueKind.Reference) ||
                (right is SmtNullConstant && left.Kind == SmtValueKind.Reference);
        }

        public static bool TryTranslateValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is bool booleanValue)
                {
                    formula = new SmtBooleanConstant(booleanValue);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    formula = new SmtNullConstant();
                    return true;
                }

                if (TryGetIntegralConstant(constantValue.Value, out var integralValue))
                {
                    formula = new SmtIntegerConstant(integralValue);
                    return true;
                }
            }

            if (TryTranslateDefaultValue(expression, semanticModel, cancellationToken, out formula))
            {
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryTranslateValue(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrueFormula, getSymbolVersion, inlineDepth) &&
                whenTrueFormula != null &&
                TryTranslateValue(conditionalExpression.WhenFalse, semanticModel, cancellationToken, out var whenFalseFormula, getSymbolVersion, inlineDepth) &&
                whenFalseFormula != null &&
                whenTrueFormula.Kind == whenFalseFormula.Kind)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula, whenTrueFormula.Kind);
                return true;
            }

            if (expression is SwitchExpressionSyntax switchExpression &&
                TryTranslateSwitchExpressionValue(switchExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeft, getSymbolVersion, inlineDepth) &&
                coalesceLeft is { Kind: SmtValueKind.Reference } &&
                TryTranslateValue(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRight, getSymbolVersion, inlineDepth) &&
                coalesceRight is { Kind: SmtValueKind.Reference })
            {
                formula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, coalesceLeft, new SmtNullConstant()),
                    coalesceLeft,
                    coalesceRight,
                    SmtValueKind.Reference);
                return true;
            }

            if (TryTranslateBooleanTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (TryTranslateIntegralTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is not ILocalSymbol && symbol is not IParameterSymbol)
            {
                return TryTranslateMemberValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private static bool TryTranslateDefaultValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                expression is not DefaultExpressionSyntax)
            {
                return false;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtBooleanConstant(false);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtIntegerConstant(0);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtNullConstant();
                return true;
            }

            return false;
        }

        private static bool TryTranslateSwitchExpressionValue(
            SwitchExpressionSyntax switchExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (switchExpression.Arms.Count < 2 ||
                !HasUnguardedDiscardFallback(switchExpression.Arms[switchExpression.Arms.Count - 1]))
            {
                return false;
            }

            var armConditions = new List<SmtFormula>();
            var armValues = new List<SmtFormula>();
            foreach (var arm in switchExpression.Arms)
            {
                if (!TryTranslateValue(arm.Expression, semanticModel, cancellationToken, out var armValue, getSymbolVersion, inlineDepth) ||
                    armValue == null ||
                    !SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                        switchExpression.GoverningExpression,
                        arm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition,
                        getSymbolVersion))
                {
                    formula = null;
                    return false;
                }

                if (armValues.Count > 0 &&
                    armValues[0].Kind != armValue.Kind)
                {
                    formula = null;
                    return false;
                }

                armConditions.Add(armCondition);
                armValues.Add(armValue);
            }

            var result = armValues[armValues.Count - 1];
            for (var index = armValues.Count - 2; index >= 0; index--)
            {
                result = new SmtConditionalFormula(
                    armConditions[index],
                    armValues[index],
                    result,
                    result.Kind);
            }

            formula = result;
            return true;
        }

        private static bool HasUnguardedDiscardFallback(SwitchExpressionArmSyntax arm)
        {
            return arm.WhenClause == null &&
                arm.Pattern is DiscardPatternSyntax;
        }

        private static bool TryTranslateBooleanTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!HasSupportedBooleanType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryTranslate(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                operand != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd, getSymbolVersion, inlineDepth) &&
                    leftAnd != null &&
                    rightAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.BitwiseAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseAnd, getSymbolVersion, inlineDepth) &&
                    leftBitwiseAnd != null &&
                    rightBitwiseAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftBitwiseAnd, rightBitwiseAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr, getSymbolVersion, inlineDepth) &&
                    leftOr != null &&
                    rightOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseOr, getSymbolVersion, inlineDepth) &&
                    leftBitwiseOr != null &&
                    rightBitwiseOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftBitwiseOr, rightBitwiseOr);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.ExclusiveOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftExclusiveOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightExclusiveOr, getSymbolVersion, inlineDepth) &&
                    leftExclusiveOr is { Kind: SmtValueKind.Bool } &&
                    rightExclusiveOr is { Kind: SmtValueKind.Bool })
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, leftExclusiveOr, rightExclusiveOr);
                    return true;
                }

                if (TryTranslateUnsignedCastBoundsComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var unsignedBoundsFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    unsignedBoundsFormula != null)
                {
                    formula = unsignedBoundsFormula;
                    return true;
                }

                if (TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth) &&
                    leftValue != null &&
                    rightValue != null &&
                    TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
                {
                    formula = comparison;
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                return TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            if (expression is InvocationExpressionSyntax invocationExpression)
            {
                return TryTranslateSourceBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            return false;
        }

        private static bool TryTranslateIntegralTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!HasSupportedIntegralType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary)
            {
                if (prefixUnary.IsKind(SyntaxKind.UnaryPlusExpression))
                {
                    return TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth) &&
                        formula is { Kind: SmtValueKind.Int };
                }

                if (prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                    operand is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operand);
                    return true;
                }
            }

            if (expression is CastExpressionSyntax castExpression &&
                IsRepresentationPreservingIntegralCast(castExpression, semanticModel, cancellationToken) &&
                TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var castOperand, getSymbolVersion, inlineDepth) &&
                castOperand is { Kind: SmtValueKind.Int })
            {
                formula = castOperand;
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var addLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var addRight, getSymbolVersion, inlineDepth) &&
                    addLeft is { Kind: SmtValueKind.Int } &&
                    addRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, addLeft, addRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var subtractLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var subtractRight, getSymbolVersion, inlineDepth) &&
                    subtractLeft is { Kind: SmtValueKind.Int } &&
                    subtractRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, subtractLeft, subtractRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.MultiplyExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var multiplyLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var multiplyRight, getSymbolVersion, inlineDepth) &&
                    multiplyLeft is { Kind: SmtValueKind.Int } &&
                    multiplyRight is { Kind: SmtValueKind.Int } &&
                    (multiplyLeft is SmtIntegerConstant || multiplyRight is SmtIntegerConstant))
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, multiplyLeft, multiplyRight);
                    return true;
                }
            }

            return false;
        }

        private static bool IsRepresentationPreservingIntegralCast(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var sourceType = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken).Type;
            var targetType = semanticModel.GetTypeInfo(castExpression, cancellationToken).Type;
            if (sourceType == null ||
                targetType == null ||
                !IsIntegralOrEnumType(sourceType) ||
                !IsIntegralOrEnumType(targetType))
            {
                return false;
            }

            return TryGetIntegralSpecialType(sourceType, out var sourceSpecialType) &&
                TryGetIntegralSpecialType(targetType, out var targetSpecialType) &&
                IsSameOrWideningIntegralConversion(sourceSpecialType, targetSpecialType);
        }

        private static bool IsSameOrWideningIntegralConversion(
            SpecialType sourceType,
            SpecialType targetType)
        {
            if (sourceType == targetType)
            {
                return true;
            }

            return sourceType switch
            {
                SpecialType.System_SByte => targetType is
                    SpecialType.System_Int16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_Int64,
                SpecialType.System_Byte => targetType is
                    SpecialType.System_Int16 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                SpecialType.System_Int16 => targetType is
                    SpecialType.System_Int32 or
                    SpecialType.System_Int64,
                SpecialType.System_UInt16 => targetType is
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                SpecialType.System_Int32 => targetType == SpecialType.System_Int64,
                SpecialType.System_UInt32 => targetType is
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                _ => false
            };
        }

        private static bool TryTranslateMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
            if (memberSymbol is not IPropertySymbol and not IFieldSymbol)
            {
                return false;
            }

            if (memberSymbol.Name == "Length" &&
                TryGetKnownStringLength(memberAccess.Expression, semanticModel, cancellationToken, out var stringLength))
            {
                formula = new SmtIntegerConstant(stringLength);
                return true;
            }

            if (memberSymbol is IFieldSymbol { HasConstantValue: true } constantField &&
                constantField.ConstantValue != null &&
                TryGetIntegralConstant(constantField.ConstantValue, out var integralConstant))
            {
                formula = new SmtIntegerConstant(integralConstant);
                return true;
            }

            if (TryTranslateTupleElementValue(memberAccess, memberSymbol, semanticModel, cancellationToken, out formula, getSymbolVersion))
            {
                return true;
            }

            if (!TryTranslateValue(memberAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion, inlineDepth) ||
                receiver == null)
            {
                return false;
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            return TryCreateMemberFormula(receiver, memberSymbol.Name, type, out formula);
        }

        private static bool TryTranslateTupleElementValue(
            MemberAccessExpressionSyntax memberAccess,
            ISymbol memberSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;
            if (memberSymbol is not IFieldSymbol fieldSymbol ||
                !TryGetTupleElementStorageName(fieldSymbol, out var storageName) ||
                semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not { } receiverSymbol ||
                receiverSymbol is not ILocalSymbol and not IParameterSymbol ||
                !TryGetValueKind(fieldSymbol.Type, out var kind))
            {
                return false;
            }

            formula = new SmtVariable(GetVariableName(receiverSymbol.OriginalDefinition, getSymbolVersion) + "." + storageName, kind);
            return true;
        }

        private static bool TryGetTupleElementStorageName(IFieldSymbol fieldSymbol, out string storageName)
        {
            var tupleField = fieldSymbol.CorrespondingTupleField ?? fieldSymbol;
            if (tupleField.Name.Length > 4 &&
                tupleField.Name.StartsWith("Item", StringComparison.Ordinal) &&
                tupleField.Name.Skip(4).All(char.IsDigit))
            {
                storageName = tupleField.Name;
                return true;
            }

            storageName = string.Empty;
            return false;
        }

        private static bool TryCreateMemberFormula(
            SmtFormula receiver,
            string memberName,
            ITypeSymbol type,
            out SmtFormula? formula)
        {
            formula = null;
            var variableName = receiver + "." + memberName;
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private static bool TryCreateSymbolFormula(
            ISymbol symbol,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type == null ||
                !TryGetValueKind(type, out var kind))
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), kind);
            return true;
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (type.IsReferenceType)
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryGetMemberType(ISymbol? memberSymbol, out ITypeSymbol type)
        {
            switch (memberSymbol)
            {
                case IPropertySymbol propertySymbol:
                    type = propertySymbol.Type;
                    return true;
                case IFieldSymbol fieldSymbol:
                    type = fieldSymbol.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static string GetVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion)
        {
            var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
            var name = symbol.Name + "#" + start.ToString(CultureInfo.InvariantCulture);
            var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        private static bool IsIntegralType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        }

        private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
        {
            return IsIntegralType(typeSymbol) ||
                typeSymbol.TypeKind == TypeKind.Enum;
        }

        private static bool TryGetIntegralSpecialType(ITypeSymbol typeSymbol, out SpecialType specialType)
        {
            if (IsIntegralType(typeSymbol))
            {
                specialType = typeSymbol.SpecialType;
                return true;
            }

            if (typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType } &&
                IsIntegralType(underlyingType))
            {
                specialType = underlyingType.SpecialType;
                return true;
            }

            specialType = SpecialType.None;
            return false;
        }

        private static bool TryGetIntegralConstant(object value, out long integralValue)
        {
            if (value is Enum enumValue)
            {
                value = Convert.ChangeType(enumValue, enumValue.GetTypeCode(), CultureInfo.InvariantCulture);
            }

            switch (value)
            {
                case sbyte signedByte:
                    integralValue = signedByte;
                    return true;
                case byte unsignedByte:
                    integralValue = unsignedByte;
                    return true;
                case short signedShort:
                    integralValue = signedShort;
                    return true;
                case ushort unsignedShort:
                    integralValue = unsignedShort;
                    return true;
                case int signedInt:
                    integralValue = signedInt;
                    return true;
                case uint unsignedInt:
                    integralValue = unsignedInt;
                    return true;
                case long signedLong:
                    integralValue = signedLong;
                    return true;
                case ulong unsignedLong when unsignedLong <= long.MaxValue:
                    integralValue = (long)unsignedLong;
                    return true;
                default:
                    integralValue = default;
                    return false;
            }
        }

        private static bool HasSupportedIntegralType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            return type != null && IsIntegralOrEnumType(type);
        }

        private static bool HasSupportedBooleanType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            return type?.SpecialType == SpecialType.System_Boolean;
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
                {
                    expression = parenthesizedExpression.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                return expression;
            }
        }
    }
}
