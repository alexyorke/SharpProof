using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt
{
    internal static partial class CSharpConditionToFormula
    {
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
            var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (receiverType is IArrayTypeSymbol { Rank: > 1 } multidimensionalArrayType &&
                elementAccess.ArgumentList.Arguments.Count == multidimensionalArrayType.Rank)
            {
                return TryTranslateMultidimensionalArrayElementAccessInRange(
                    elementAccess,
                    multidimensionalArrayType,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            if (!IsSupportedBuiltInElementAccessReceiver(receiverType))
            {
                return false;
            }

            if (!TryCreateBuiltInElementAccessLengthFormula(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            var indexArgumentExpression = elementAccess.ArgumentList.Arguments[0].Expression;
            if (TryCreateBuiltInRangeAccessInRangeFormula(
                    indexArgumentExpression,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (TryCreateAbsRemainderIndexAccessInRangeFormula(
                    indexArgumentExpression,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (!TryResolveBuiltInIndexAccessIndexShape(
                    indexArgumentExpression,
                    semanticModel,
                    cancellationToken,
                    out var indexShape) ||
                !TryCreateEffectiveBuiltInIndexFormula(
                    indexShape,
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
            if (!TryCreateIndexShapeWellFormedFormula(
                    indexShape,
                    semanticModel,
                    cancellationToken,
                    out var indexWellFormed,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = ApplyWellFormedPrecondition(indexWellFormed, formula);
            return true;
        }

        private static bool TryCreateAbsRemainderIndexAccessInRangeFormula(
            ExpressionSyntax indexExpression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (indexExpression is not InvocationExpressionSyntax invocationExpression ||
                !TryGetMathAbsRemainderOperands(
                    invocationExpression,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out var divisorExpression) ||
                !TryTranslateValue(
                    divisorExpression,
                    semanticModel,
                    cancellationToken,
                    out var divisorFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                divisorFormula is not { Kind: SmtValueKind.Int } ||
                !Equals(divisorFormula, lengthFormula))
            {
                return false;
            }

            formula = new SmtBooleanConstant(true);
            return true;
        }

        private static bool TryTranslateMultidimensionalArrayElementAccessInRange(
            ElementAccessExpressionSyntax elementAccess,
            IArrayTypeSymbol arrayType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (arrayType.Rank <= 1 ||
                elementAccess.ArgumentList.Arguments.Count != arrayType.Rank)
            {
                return false;
            }

            SmtFormula? combined = null;
            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (!TryTranslateValue(
                        elementAccess.ArgumentList.Arguments[dimension].Expression,
                        semanticModel,
                        cancellationToken,
                        out var indexFormula,
                        getSymbolVersion,
                        inlineDepth) ||
                    indexFormula is not { Kind: SmtValueKind.Int } ||
                    !TryCreateArrayDimensionLengthFormula(
                        elementAccess.Expression,
                        dimension,
                        semanticModel,
                        cancellationToken,
                        out var lengthFormula,
                        getSymbolVersion,
                        inlineDepth) ||
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
                var dimensionInRange = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
                combined = combined == null
                    ? dimensionInRange
                    : new SmtBinaryFormula(SmtBinaryOperator.And, combined, dimensionInRange);
            }

            if (combined == null)
            {
                return false;
            }

            formula = combined;
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

        public static bool TryTranslateArrayDimensionLengthValue(
            ExpressionSyntax expression,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryCreateArrayDimensionLengthFormula(
                expression,
                dimension,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        public static bool TryTranslateNullableHasValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            if (TryTranslateNullableValueParts(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var parts,
                    getSymbolVersion,
                    inlineDepth))
            {
                formula = parts.HasValue;
                return true;
            }

            formula = null!;
            return false;
        }

        public static bool TryTranslateNullableValueParts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out NullableSmtValueParts parts,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            if (TryTranslateNullableValueParts(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var valueFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                parts = new NullableSmtValueParts(hasValueFormula, valueFormula);
                return true;
            }

            parts = default;
            return false;
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
                if (!IsKnownNonNegativeIntegralMemberAccess(memberAccess, semanticModel, cancellationToken) ||
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

            foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation)
                {
                    AddKnownStringInvocationDomainFacts(invocationOperation, semanticModel, cancellationToken, formulas, getSymbolVersion);
                }
            }

            return formulas.Count > originalCount;
        }

        private static void AddKnownStringInvocationDomainFacts(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var method = invocationOperation.TargetMethod;
            if (method.Name == "IsMatch" &&
                IsRegexType(method.ContainingType) &&
                TryGetRegexInputExpression(invocationOperation, out var regexInputExpression))
            {
                AddStringNonNullDomainFact(regexInputExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
                return;
            }

            if (method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.IsStatic ||
                method.Name is not "Contains" and not "StartsWith" and not "EndsWith")
            {
                return;
            }

            if (invocationOperation.Instance?.Syntax is ExpressionSyntax receiverExpression)
            {
                AddStringNonNullDomainFact(receiverExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }

            if (method.Parameters.Length >= 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                invocationOperation.Arguments.Length >= 1 &&
                invocationOperation.Arguments[0].Value.Syntax is ExpressionSyntax searchExpression)
            {
                AddStringNonNullDomainFact(searchExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }
        }

        private static bool TryGetRegexInputExpression(
            IInvocationOperation invocationOperation,
            out ExpressionSyntax inputExpression)
        {
            inputExpression = null!;
            var method = invocationOperation.TargetMethod;
            if (method.Name != "IsMatch" ||
                !IsRegexType(method.ContainingType))
            {
                return false;
            }

            if (method.IsStatic)
            {
                if (method.Parameters.Length < 1 ||
                    method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                    invocationOperation.Arguments.Length < 1 ||
                    invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax staticInputExpression)
                {
                    return false;
                }

                inputExpression = staticInputExpression;
                return true;
            }

            if (method.Parameters.Length < 1 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax instanceInputExpression)
            {
                return false;
            }

            inputExpression = instanceInputExpression;
            return true;
        }

        private static void AddStringNonNullDomainFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (TryCreateStringNonNullFormula(expression, semanticModel, cancellationToken, out var nonNullFormula, getSymbolVersion) &&
                nonNullFormula != null)
            {
                formulas.Add(nonNullFormula);
            }
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

            if (branchWhenTrue &&
                TryCreateTypeTestNonNullBranchFact(expression, semanticModel, cancellationToken, out var typeTestNonNull, getSymbolVersion))
            {
                formulas.Add(typeTestNonNull);
            }

            AddNullComparisonOperandImplications(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            AddConditionalAccessStringEqualityBranchFacts(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            AddNotNullWhenBranchFacts(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            AddMemberNotNullWhenBranchFacts(expression, branchWhenTrue, semanticModel, cancellationToken, formulas);
            if (TryAddInlineAssignmentBranchFacts(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion))
            {
                return;
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

        private static void AddNotNullWhenBranchFacts(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if ((binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
                     binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) &&
                    TryGetBoolLiteral(binaryExpression.Left, out var leftValue))
                {
                    AddNotNullWhenBranchFacts(
                        binaryExpression.Right,
                        GetComparedBranchValue(leftValue, binaryExpression, branchWhenTrue),
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
                }

                if ((binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
                     binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) &&
                    TryGetBoolLiteral(binaryExpression.Right, out var rightValue))
                {
                    AddNotNullWhenBranchFacts(
                        binaryExpression.Left,
                        GetComparedBranchValue(rightValue, binaryExpression, branchWhenTrue),
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
                }
            }

            if (expression is InvocationExpressionSyntax invocation)
            {
                AddNotNullWhenInvocationBranchFacts(
                    invocation,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddMemberNotNullWhenBranchFacts(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas)
        {
            expression = UnwrapExpression(expression);
            if (expression is PrefixUnaryExpressionSyntax negation &&
                negation.IsKind(SyntaxKind.LogicalNotExpression))
            {
                AddMemberNotNullWhenBranchFacts(
                    negation.Operand,
                    !branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas);
                return;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if ((binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
                     binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) &&
                    TryGetBoolLiteral(binaryExpression.Left, out var leftValue))
                {
                    AddMemberNotNullWhenBranchFacts(
                        binaryExpression.Right,
                        GetComparedBranchValue(leftValue, binaryExpression, branchWhenTrue),
                        semanticModel,
                        cancellationToken,
                        formulas);
                    return;
                }

                if ((binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
                     binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) &&
                    TryGetBoolLiteral(binaryExpression.Right, out var rightValue))
                {
                    AddMemberNotNullWhenBranchFacts(
                        binaryExpression.Left,
                        GetComparedBranchValue(rightValue, binaryExpression, branchWhenTrue),
                        semanticModel,
                        cancellationToken,
                        formulas);
                    return;
                }
            }

            if (expression is InvocationExpressionSyntax invocation)
            {
                AddMemberNotNullWhenInvocationBranchFacts(
                    invocation,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas);
            }
        }

        private static void AddMemberNotNullWhenInvocationBranchFacts(
            InvocationExpressionSyntax invocation,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas)
        {
            if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean ||
                invocationOperation.TargetMethod.IsStatic ||
                !IsCurrentInstanceInvocation(invocation))
            {
                return;
            }

            foreach (var memberTarget in GetMemberNotNullWhenTargets(invocationOperation.TargetMethod, branchWhenTrue))
            {
                if (!TryResolveMemberNotNullWhenTarget(invocationOperation.TargetMethod.ContainingType, memberTarget, out var member, out var memberType) ||
                    !TryCreateMemberFormula(new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference), member.Name, memberType, out var memberFormula) ||
                    memberFormula is not { Kind: SmtValueKind.Reference })
                {
                    continue;
                }

                formulas.Add(CreateNonNullFormula(memberFormula));
            }
        }

        private static bool IsCurrentInstanceInvocation(InvocationExpressionSyntax invocation)
        {
            var invokedExpression = UnwrapExpression(invocation.Expression);
            return invokedExpression is IdentifierNameSyntax ||
                invokedExpression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
        }

        private static IEnumerable<string> GetMemberNotNullWhenTargets(IMethodSymbol method, bool branchWhenTrue)
        {
            var targets = new List<string>();
            AddMemberNotNullWhenTargets(method, branchWhenTrue, targets);
            if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
            {
                AddMemberNotNullWhenTargets(method.OriginalDefinition, branchWhenTrue, targets);
            }

            return targets.Distinct(StringComparer.Ordinal);
        }

        private static void AddMemberNotNullWhenTargets(
            IMethodSymbol method,
            bool branchWhenTrue,
            ICollection<string> targets)
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (!string.Equals(
                        SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                        MemberNotNullWhenAttributeMetadataName,
                        StringComparison.Ordinal) ||
                    attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not bool attributeBranch ||
                    attributeBranch != branchWhenTrue)
                {
                    continue;
                }

                for (var index = 1; index < attribute.ConstructorArguments.Length; index++)
                {
                    AddMemberNotNullWhenTarget(attribute.ConstructorArguments[index], targets);
                }
            }
        }

        private static void AddMemberNotNullWhenTarget(TypedConstant argument, ICollection<string> targets)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var item in argument.Values)
                {
                    AddMemberNotNullWhenTarget(item, targets);
                }

                return;
            }

            if (argument.Value is string target &&
                !string.IsNullOrWhiteSpace(target))
            {
                targets.Add(target);
            }
        }

        private static bool TryResolveMemberNotNullWhenTarget(
            INamedTypeSymbol containingType,
            string target,
            out ISymbol member,
            out ITypeSymbol memberType)
        {
            var memberName = NormalizeMemberNotNullWhenTarget(target);
            if (memberName == null)
            {
                member = null!;
                memberType = null!;
                return false;
            }

            var candidates = containingType.GetMembers(memberName)
                .Where(candidate =>
                    candidate is IFieldSymbol or IPropertySymbol &&
                    !candidate.IsStatic &&
                    TryGetMemberNotNullWhenTargetType(candidate, out var type) &&
                    IsReferenceLikeType(type))
                .ToArray();
            if (candidates.Length != 1 ||
                !TryGetMemberNotNullWhenTargetType(candidates[0], out memberType))
            {
                member = null!;
                memberType = null!;
                return false;
            }

            member = candidates[0].OriginalDefinition;
            return true;
        }

        private static string? NormalizeMemberNotNullWhenTarget(string target)
        {
            target = target.Trim();
            if (target.StartsWith("this.", StringComparison.Ordinal))
            {
                target = target.Substring("this.".Length);
            }

            return target.Length != 0 && !target.Contains(".", StringComparison.Ordinal)
                ? target
                : null;
        }

        private static bool TryGetMemberNotNullWhenTargetType(ISymbol member, out ITypeSymbol type)
        {
            switch (member)
            {
                case IFieldSymbol fieldSymbol:
                    type = fieldSymbol.Type;
                    return true;
                case IPropertySymbol propertySymbol:
                    type = propertySymbol.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static bool TryGetBoolLiteral(ExpressionSyntax expression, out bool value)
        {
            expression = UnwrapExpression(expression);
            if (expression.IsKind(SyntaxKind.TrueLiteralExpression))
            {
                value = true;
                return true;
            }

            if (expression.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        private static bool GetComparedBranchValue(
            bool literalValue,
            BinaryExpressionSyntax comparison,
            bool branchWhenTrue)
        {
            return comparison.IsKind(SyntaxKind.EqualsExpression)
                ? literalValue == branchWhenTrue
                : literalValue != branchWhenTrue;
        }

        private static void AddNotNullWhenInvocationBranchFacts(
            InvocationExpressionSyntax invocation,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                return;
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.ArgumentKind != ArgumentKind.Explicit ||
                    argument.Parameter is not { IsParams: false } parameter ||
                    !IsSupportedNotNullWhenArgument(parameter, argument.Syntax as ArgumentSyntax) ||
                    !TryGetNotNullWhenValue(parameter, out var notNullWhenValue) ||
                    notNullWhenValue != branchWhenTrue ||
                    argument.Syntax is not ArgumentSyntax argumentSyntax ||
                    !TryCreateNotNullWhenArgumentFormula(
                        argumentSyntax.Expression,
                        semanticModel,
                        cancellationToken,
                        getSymbolVersion,
                        out var argumentFormula))
                {
                    continue;
                }

                formulas.Add(CreateNonNullFormula(argumentFormula));
            }
        }

        private static bool IsSupportedNotNullWhenArgument(
            IParameterSymbol parameter,
            ArgumentSyntax? argument)
        {
            if (argument == null)
            {
                return false;
            }

            return parameter.RefKind switch
            {
                RefKind.None => argument.RefKindKeyword.IsKind(SyntaxKind.None),
                RefKind.Out => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
                _ => false,
            };
        }

        private static bool TryGetNotNullWhenValue(IParameterSymbol parameter, out bool value)
        {
            if (TryGetSymbolNotNullWhenValue(parameter, out value))
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(parameter, parameter.OriginalDefinition) &&
                TryGetSymbolNotNullWhenValue(parameter.OriginalDefinition, out value))
            {
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryGetSymbolNotNullWhenValue(IParameterSymbol parameter, out bool value)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (!string.Equals(
                        SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                        NotNullWhenAttributeMetadataName,
                        StringComparison.Ordinal) ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not bool attributeValue)
                {
                    continue;
                }

                value = attributeValue;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryCreateNotNullWhenArgumentFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            if (TryGetLocalOrParameterArgumentSymbol(expression, semanticModel, cancellationToken, out var symbol) &&
                TryCreateSymbolFormula(symbol, getSymbolVersion, out formula) &&
                formula.Kind == SmtValueKind.Reference)
            {
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryGetLocalOrParameterArgumentSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol symbol)
        {
            expression = UnwrapExpression(expression);
            if (expression is DeclarationExpressionSyntax
                {
                    Designation: SingleVariableDesignationSyntax singleVariableDesignation
                } &&
                semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is ILocalSymbol declaredLocal)
            {
                symbol = declaredLocal.OriginalDefinition;
                return true;
            }

            var candidate = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
            if (candidate is ILocalSymbol or IParameterSymbol)
            {
                symbol = candidate;
                return true;
            }

            symbol = null!;
            return false;
        }

        private static bool TryAddInlineAssignmentBranchFacts(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is AssignmentExpressionSyntax directAssignment)
            {
                return TryAddDirectBooleanAssignmentBranchFacts(
                    directAssignment,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }

            if (expression is not BinaryExpressionSyntax binaryExpression ||
                !IsSupportedInlineAssignmentComparison(binaryExpression.Kind()))
            {
                return false;
            }

            if (TryAddInlineAssignmentComparisonBranchFacts(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    binaryExpression.Kind(),
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion,
                    rejectOtherReferencesAssignedSymbol: false))
            {
                return true;
            }

            return TryAddInlineAssignmentComparisonBranchFacts(
                binaryExpression.Right,
                binaryExpression.Left,
                ReverseComparisonKind(binaryExpression.Kind()),
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion,
                rejectOtherReferencesAssignedSymbol: true);
        }

        private static bool TryAddDirectBooleanAssignmentBranchFacts(
            AssignmentExpressionSyntax assignment,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var candidateFormulas = formulas.ToList();
            if (!TryCreateSimpleInlineAssignmentFact(
                    assignment,
                    semanticModel,
                    cancellationToken,
                    candidateFormulas,
                    getSymbolVersion,
                    out var targetFormula,
                    out _) ||
                targetFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            candidateFormulas.Add(branchWhenTrue
                ? targetFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, targetFormula));
            ReplaceFormulas(formulas, candidateFormulas);
            return true;
        }

        private static bool TryAddInlineAssignmentComparisonBranchFacts(
            ExpressionSyntax assignmentCandidate,
            ExpressionSyntax otherExpression,
            SyntaxKind comparisonKind,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion,
            bool rejectOtherReferencesAssignedSymbol)
        {
            assignmentCandidate = UnwrapExpression(assignmentCandidate);
            otherExpression = UnwrapExpression(otherExpression);
            var candidateFormulas = formulas.ToList();
            if (assignmentCandidate is not AssignmentExpressionSyntax assignment ||
                UnwrapExpression(otherExpression) is AssignmentExpressionSyntax ||
                !TryCreateSimpleInlineAssignmentFact(
                    assignment,
                    semanticModel,
                    cancellationToken,
                    candidateFormulas,
                    getSymbolVersion,
                    out var targetFormula,
                    out var assignedSymbol) ||
                (rejectOtherReferencesAssignedSymbol &&
                 ExpressionReferencesSymbol(otherExpression, assignedSymbol, semanticModel, cancellationToken)) ||
                !TryTranslateValue(otherExpression, semanticModel, cancellationToken, out var otherFormula, getSymbolVersion) ||
                otherFormula == null ||
                !TryTranslateComparison(comparisonKind, targetFormula, otherFormula, out var comparisonFormula) ||
                comparisonFormula == null)
            {
                return false;
            }

            candidateFormulas.Add(branchWhenTrue
                ? comparisonFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, comparisonFormula));
            ReplaceFormulas(formulas, candidateFormulas);
            return true;
        }

        private static bool TryCreateSimpleInlineAssignmentFact(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula targetFormula,
            out ISymbol assignedSymbol)
        {
            targetFormula = null!;
            assignedSymbol = null!;
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                ContainsNestedAssignment(assignment.Right) ||
                semanticModel.GetSymbolInfo(UnwrapExpression(assignment.Left), cancellationToken).Symbol is not ISymbol assignmentTarget ||
                assignmentTarget is not ILocalSymbol and not IParameterSymbol ||
                ExpressionReferencesSymbol(assignment.Right, assignmentTarget.OriginalDefinition, semanticModel, cancellationToken) ||
                !TryCreateSymbolFormula(assignmentTarget.OriginalDefinition, getSymbolVersion, out targetFormula))
            {
                targetFormula = null!;
                return false;
            }

            assignedSymbol = assignmentTarget.OriginalDefinition;
            RemoveFactsReferencingSymbol(formulas, assignedSymbol, getSymbolVersion);

            if (targetFormula is { Kind: SmtValueKind.Reference } &&
                TryCreateAsExpressionAssignmentFacts(
                    assignment.Right,
                    targetFormula,
                    semanticModel,
                    cancellationToken,
                    out var asFacts,
                    getSymbolVersion))
            {
                foreach (var fact in asFacts)
                {
                    formulas.Add(fact);
                }

                return true;
            }

            if (!TryTranslateValue(assignment.Right, semanticModel, cancellationToken, out var assignedValue, getSymbolVersion) ||
                assignedValue == null ||
                !SymbolicFactFactory.CanCompareSmtValues(targetFormula, assignedValue))
            {
                targetFormula = null!;
                return false;
            }

            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, assignedValue));
            return true;
        }

        private static bool IsSupportedInlineAssignmentComparison(SyntaxKind kind)
        {
            return kind is
                SyntaxKind.EqualsExpression or
                SyntaxKind.NotEqualsExpression or
                SyntaxKind.LessThanExpression or
                SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.GreaterThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression;
        }

        private static SyntaxKind ReverseComparisonKind(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
                SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
                SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
                SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
                _ => kind,
            };
        }

        private static void ReplaceFormulas(ICollection<SmtFormula> formulas, IEnumerable<SmtFormula> replacement)
        {
            formulas.Clear();
            foreach (var formula in replacement)
            {
                formulas.Add(formula);
            }
        }

        private static bool ContainsNestedAssignment(SyntaxNode node)
        {
            return node.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().Any();
        }

        private static bool ExpressionReferencesSymbol(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var expression in node.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            {
                var expressionSymbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol;
                if (expressionSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveFactsReferencingSymbol(
            ICollection<SmtFormula> formulas,
            ISymbol symbol,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var variableName = GetVariableName(symbol.OriginalDefinition, getSymbolVersion);
            SmtFormulaReferenceScanner.RemoveFormulasReferencingVariable(formulas, variableName);
        }

        private static void AddConditionalAccessStringEqualityBranchFacts(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (expression is not BinaryExpressionSyntax binaryExpression ||
                (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                 !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) ||
                branchWhenTrue != binaryExpression.IsKind(SyntaxKind.EqualsExpression))
            {
                return;
            }

            if (TryAddConditionalAccessStringEqualityBranchFacts(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion))
            {
                return;
            }

            TryAddConditionalAccessStringEqualityBranchFacts(
                binaryExpression.Right,
                binaryExpression.Left,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        private static bool TryAddConditionalAccessStringEqualityBranchFacts(
            ExpressionSyntax conditionalCandidate,
            ExpressionSyntax otherCandidate,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            conditionalCandidate = UnwrapExpression(conditionalCandidate);
            otherCandidate = UnwrapExpression(otherCandidate);
            if (conditionalCandidate is not ConditionalAccessExpressionSyntax conditionalAccess ||
                !TryCreateStringNonNullFormula(otherCandidate, semanticModel, cancellationToken, out var otherNonNull, getSymbolVersion) ||
                otherNonNull is not SmtBooleanConstant { Value: true } ||
                !TryTranslateStringValue(otherCandidate, semanticModel, cancellationToken, out var otherString, getSymbolVersion) ||
                otherString == null ||
                !TryTranslateValue(conditionalAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion) ||
                receiver is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            var resultTypeInfo = semanticModel.GetTypeInfo(conditionalAccess, cancellationToken);
            var resultType = resultTypeInfo.ConvertedType ?? resultTypeInfo.Type;
            if (resultType?.SpecialType != SpecialType.System_String ||
                !TryCreateConditionalAccessWhenNotNullValueFormula(
                    conditionalAccess,
                    receiver,
                    resultType,
                    semanticModel,
                    cancellationToken,
                    out var whenNotNullReference,
                    getSymbolVersion,
                    inlineDepth: 0) ||
                whenNotNullReference is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formulas.Add(CreateNonNullFormula(receiver));
            formulas.Add(CreateNonNullFormula(whenNotNullReference));
            formulas.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                CreateStringValueFormulaForReference(whenNotNullReference),
                otherString));
            return true;
        }

        private static bool TryCreateTypeTestNonNullBranchFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null!;
            expression = UnwrapExpression(expression);

            ExpressionSyntax? testedExpression = null;
            if (expression is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.IsExpression) &&
                binaryExpression.Right is TypeSyntax)
            {
                testedExpression = binaryExpression.Left;
            }
            else if (expression is IsPatternExpressionSyntax isPatternExpression &&
                PatternMatchImpliesReferenceNonNull(isPatternExpression.Pattern))
            {
                testedExpression = isPatternExpression.Expression;
            }

            if (testedExpression == null ||
                !TryTranslateValue(testedExpression, semanticModel, cancellationToken, out var testedValue, getSymbolVersion) ||
                testedValue is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = CreateNonNullFormula(testedValue);
            return true;
        }

        private static bool PatternMatchImpliesReferenceNonNull(PatternSyntax pattern)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return PatternMatchImpliesReferenceNonNull(parenthesizedPattern.Pattern);
            }

            if (pattern is DeclarationPatternSyntax or TypePatternSyntax or RecursivePatternSyntax)
            {
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    return PatternMatchImpliesReferenceNonNull(binaryPattern.Left) ||
                        PatternMatchImpliesReferenceNonNull(binaryPattern.Right);
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    return PatternMatchImpliesReferenceNonNull(binaryPattern.Left) &&
                        PatternMatchImpliesReferenceNonNull(binaryPattern.Right);
                }
            }

            return false;
        }

        private static void AddNullComparisonOperandImplications(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression ||
                !ComparisonBranchImpliesComparedValueNonNull(binaryExpression.Kind(), branchWhenTrue))
            {
                return;
            }

            if (IsNullLikeReferenceComparisonOperand(binaryExpression.Left, semanticModel, cancellationToken) &&
                TryCreateOperandNonNullImplication(binaryExpression.Right, semanticModel, cancellationToken, out var rightImplication, getSymbolVersion))
            {
                formulas.Add(rightImplication);
                return;
            }

            if (IsNullLikeReferenceComparisonOperand(binaryExpression.Right, semanticModel, cancellationToken) &&
                TryCreateOperandNonNullImplication(binaryExpression.Left, semanticModel, cancellationToken, out var leftImplication, getSymbolVersion))
            {
                formulas.Add(leftImplication);
            }
        }

        private static bool ComparisonBranchImpliesComparedValueNonNull(SyntaxKind comparisonKind, bool branchWhenTrue)
        {
            return branchWhenTrue
                ? comparisonKind == SyntaxKind.NotEqualsExpression
                : comparisonKind == SyntaxKind.EqualsExpression;
        }

        private static bool IsNullLikeReferenceComparisonOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true, Value: null })
            {
                return true;
            }

            if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                expression is not DefaultExpressionSyntax)
            {
                return false;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            return type?.IsReferenceType == true;
        }

        private static bool TryCreateOperandNonNullImplication(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null!;
            expression = UnwrapExpression(expression);
            ExpressionSyntax? sourceExpression = null;

            if (expression is BinaryExpressionSyntax asExpression &&
                asExpression.IsKind(SyntaxKind.AsExpression))
            {
                sourceExpression = asExpression.Left;
            }
            else if (expression is CastExpressionSyntax castExpression &&
                IsIdentityPreservingReferenceCast(castExpression, semanticModel, cancellationToken))
            {
                sourceExpression = castExpression.Expression;
            }
            else if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                sourceExpression = conditionalAccess.Expression;
            }

            if (sourceExpression == null ||
                !TryTranslateValue(sourceExpression, semanticModel, cancellationToken, out var sourceFormula, getSymbolVersion) ||
                sourceFormula is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = CreateNonNullFormula(sourceFormula);
            return true;
        }

        private static void AddPatternBindingFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is not IsPatternExpressionSyntax isPatternExpression)
            {
                return;
            }

            if (TryAddNullablePatternBindingFacts(isPatternExpression, semanticModel, cancellationToken, formulas, getSymbolVersion))
            {
                return;
            }

            if (!TryTranslatePatternInputValue(
                    isPatternExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var matchedValue,
                    out var valueType,
                    getSymbolVersion,
                    inlineDepth: 0) ||
                matchedValue == null)
            {
                return;
            }

            AddPatternBindingFacts(
                matchedValue,
                valueType,
                isPatternExpression.Pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        private static bool TryAddNullablePatternBindingFacts(
            IsPatternExpressionSyntax isPatternExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryTranslateNullableValueParts(
                    isPatternExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out var nullableValue,
                    getSymbolVersion,
                    inlineDepth: 0) ||
                nullableValue == null)
            {
                return false;
            }

            AddNullablePatternBindingFacts(
                nullableValue,
                isPatternExpression.Pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
            return true;
        }

        private static void AddNullablePatternBindingFacts(
            SmtFormula nullableValue,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                AddNullablePatternBindingFacts(
                    nullableValue,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                return;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern)
            {
                AddDesignationBindingFact(
                    nullableValue,
                    declarationPattern.Designation,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion,
                    out _);
                return;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
            {
                AddNullablePatternBindingFacts(
                    nullableValue,
                    binaryPattern.Left,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                AddNullablePatternBindingFacts(
                    nullableValue,
                    binaryPattern.Right,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
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
                    AddDesignationBindingFact(matchedValue, varPattern.Designation, semanticModel, cancellationToken, formulas, getSymbolVersion, out _);
                    return;
                case DeclarationPatternSyntax declarationPattern:
                    AddDesignationBindingFact(matchedValue, declarationPattern.Designation, semanticModel, cancellationToken, formulas, getSymbolVersion, out _);
                    AddDesignationNonNullFact(declarationPattern.Designation, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                case RecursivePatternSyntax recursivePattern:
                    AddDesignationBindingFact(
                        matchedValue,
                        recursivePattern.Designation,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion,
                        out var designationValue);
                    AddDesignationNonNullFact(recursivePattern.Designation, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddRecursivePropertyPatternBindingFacts(
                        matchedValue,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    AddRecursiveTuplePositionalPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    if (designationValue != null &&
                        !Equals(designationValue, matchedValue))
                    {
                        AddSubstitutedPatternFactsForDesignationReceiver(
                            matchedValue,
                            designationValue,
                            matchedValueType,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                        AddRecursivePropertyPatternBindingFacts(
                            designationValue,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                        AddRecursiveTuplePositionalPatternBindingFacts(
                            designationValue,
                            matchedValueType,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                    }

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
                    AddListPatternLengthFacts(
                        matchedValue,
                        matchedValueType,
                        listPattern,
                        semanticModel,
                        formulas);
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

        private static void AddSubstitutedPatternFactsForDesignationReceiver(
            SmtFormula matchedValue,
            SmtFormula designationValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (matchedValue is not SmtVariable matchedVariable ||
                !TryTranslatePattern(
                    matchedValue,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var patternFormula,
                    getSymbolVersion,
                    matchedValueType,
                    inlineDepth: 0) ||
                patternFormula == null)
            {
                return;
            }

            var substitutions = new[]
            {
                new SmtVariableSubstitution(
                    matchedVariable.Name,
                    matchedVariable.Name + ".",
                    GetFormulaMemberName(matchedVariable) + ".",
                    designationValue)
            };
            formulas.Add(SubstituteVariables(patternFormula, substitutions));
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
            if (!TryGetBuiltInElementAccessElementType(matchedValueType, semanticModel.Compilation, out var elementType) ||
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
                if (!TryResolvePropertySubpatternValue(
                        matchedValue,
                        subpattern,
                        semanticModel,
                        cancellationToken,
                        out var memberValue,
                        out var memberType,
                        out var pathCondition) ||
                    memberValue == null ||
                    memberType == null)
                {
                    continue;
                }

                if (pathCondition != null)
                {
                    formulas.Add(pathCondition);
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

        private static void AddRecursiveTuplePositionalPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var subpatterns = recursivePattern.PositionalPatternClause?.Subpatterns;
            if (subpatterns == null)
            {
                return;
            }

            for (var position = 0; position < subpatterns.Value.Count; position++)
            {
                if (!TryResolveTuplePositionalSubpatternValue(
                        matchedValue,
                        matchedValueType,
                        position,
                        out var memberValue,
                        out var memberType) ||
                    memberValue == null ||
                    memberType == null)
                {
                    continue;
                }

                AddPatternBindingFacts(
                    memberValue,
                    memberType,
                    subpatterns.Value[position].Pattern,
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
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula? localValue)
        {
            localValue = null;
            if (!TryCreateDesignationFormula(designation, semanticModel, cancellationToken, getSymbolVersion, out var designationValue) ||
                designationValue == null ||
                !SymbolicFactFactory.CanCompareSmtValues(designationValue, matchedValue))
            {
                return;
            }

            localValue = designationValue;
            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, localValue, matchedValue));

            if (TryCreateDesignationStringFormula(designation, semanticModel, cancellationToken, getSymbolVersion, out var designationString) &&
                TryCreateStringContentFormula(matchedValue, out var matchedString))
            {
                formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, designationString, matchedString));
            }
        }

        private static void AddDesignationNonNullFact(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryCreateDesignationFormula(designation, semanticModel, cancellationToken, getSymbolVersion, out var designationValue) ||
                designationValue is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            formulas.Add(CreateNonNullFormula(designationValue));
        }

        private static bool TryCreateDesignationStringFormula(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            formula = null!;
            if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                singleVariableDesignation.Identifier.ValueText == "_" ||
                semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is not ILocalSymbol localSymbol ||
                localSymbol.Type.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            formula = new SmtVariable(GetVariableName(localSymbol, getSymbolVersion) + ".String", SmtValueKind.String);
            return true;
        }

        private static bool TryCreateStringContentFormula(SmtFormula referenceFormula, out SmtFormula formula)
        {
            formula = null!;
            if (referenceFormula.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var receiverName = referenceFormula is SmtVariable variable
                ? variable.Name
                : referenceFormula.ToString();
            if (string.IsNullOrEmpty(receiverName))
            {
                return false;
            }

            formula = new SmtVariable(receiverName + ".String", SmtValueKind.String);
            return true;
        }

        private static bool TryCreateDesignationFormula(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            formula = null!;
            if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                singleVariableDesignation.Identifier.ValueText == "_" ||
                semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is not ILocalSymbol localSymbol)
            {
                return false;
            }

            return TryCreateSymbolFormula(localSymbol, getSymbolVersion, out formula);
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
            if (TryTranslateNullablePatternExpression(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (!TryTranslatePatternInputValue(
                    expression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var value,
                    out var valueType,
                    getSymbolVersion,
                    inlineDepth) ||
                value == null)
            {
                return false;
            }

            return TryTranslatePattern(value, expression.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, valueType, inlineDepth);
        }

        private static bool TryTranslatePatternInputValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? value,
            out ITypeSymbol? valueType,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            expression = UnwrapExpression(expression);
            var valueTypeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            valueType = valueTypeInfo.ConvertedType ?? valueTypeInfo.Type;

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out value, getSymbolVersion, inlineDepth) &&
                value != null)
            {
                return true;
            }

            if (IsBuiltInSpanType(valueType) &&
                TryCreateBuiltInElementAccessReceiverFormula(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var spanValue,
                    getSymbolVersion,
                    inlineDepth) &&
                spanValue is { Kind: SmtValueKind.Reference })
            {
                value = spanValue;
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryTranslateNullablePatternExpression(
            IsPatternExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateNullableValueParts(
                    expression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var valueFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                valueFormula == null ||
                !TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(expression.Expression, cancellationToken).Type,
                    out var underlyingType))
            {
                return false;
            }

            return TryTranslateNullablePattern(
                hasValueFormula,
                valueFormula,
                underlyingType,
                expression.Pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        private static bool TryTranslateNullablePattern(
            SmtFormula hasValueFormula,
            SmtFormula valueFormula,
            ITypeSymbol underlyingType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (pattern is DiscardPatternSyntax or VarPatternSyntax)
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern &&
                PatternTypeMatchesUnderlyingType(declarationPattern.Type, underlyingType, semanticModel, cancellationToken))
            {
                formula = hasValueFormula;
                return true;
            }

            if (pattern is TypePatternSyntax typePattern &&
                PatternTypeMatchesUnderlyingType(typePattern.Type, underlyingType, semanticModel, cancellationToken))
            {
                formula = hasValueFormula;
                return true;
            }

            if (pattern is ConstantPatternSyntax nullConstantPattern &&
                IsNullLikeNullableComparisonOperand(nullConstantPattern.Expression, semanticModel, cancellationToken))
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, hasValueFormula);
                return true;
            }

            if (pattern is RecursivePatternSyntax recursivePattern)
            {
                if (IsEmptyRecursivePattern(recursivePattern))
                {
                    formula = hasValueFormula;
                    return true;
                }

                if (TryTranslateRecursivePattern(
                        valueFormula,
                        underlyingType,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        out var recursiveFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    recursiveFormula != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, hasValueFormula, recursiveFormula);
                    return true;
                }
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion, inlineDepth) &&
                constantValue != null &&
                SymbolicFactFactory.CanCompareSmtValues(valueFormula, constantValue))
            {
                formula = new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    hasValueFormula,
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, valueFormula, constantValue));
                return true;
            }

            if (pattern is RelationalPatternSyntax relationalPattern &&
                valueFormula.Kind == SmtValueKind.Int &&
                TryTranslateValue(relationalPattern.Expression, semanticModel, cancellationToken, out var relationalValue, getSymbolVersion, inlineDepth) &&
                relationalValue is { Kind: SmtValueKind.Int } &&
                TryTranslateRelationalPatternComparison(relationalPattern.OperatorToken.Kind(), valueFormula, relationalValue, out var comparison))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, hasValueFormula, comparison);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    unaryPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    out var negatedPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                negatedPattern != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    binaryPattern.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                leftPattern != null &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    binaryPattern.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightPattern,
                    getSymbolVersion,
                    inlineDepth) &&
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

            return false;
        }

        private static bool IsEmptyRecursivePattern(RecursivePatternSyntax recursivePattern)
        {
            return recursivePattern.PropertyPatternClause is not { Subpatterns.Count: > 0 } &&
                recursivePattern.PositionalPatternClause is not { Subpatterns.Count: > 0 };
        }

        private static bool TryTranslateRelationalPatternComparison(
            SyntaxKind operatorKind,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula formula)
        {
            formula = null!;
            switch (operatorKind)
            {
                case SyntaxKind.GreaterThanToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, left, right);
                    return true;
                case SyntaxKind.GreaterThanEqualsToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, left, right);
                    return true;
                case SyntaxKind.LessThanToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.LessThan, left, right);
                    return true;
                case SyntaxKind.LessThanEqualsToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, left, right);
                    return true;
                default:
                    return false;
            }
        }

        private static bool PatternTypeMatchesUnderlyingType(
            TypeSyntax patternTypeSyntax,
            ITypeSymbol underlyingType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var patternType = semanticModel.GetTypeInfo(patternTypeSyntax, cancellationToken).Type;
            return patternType != null &&
                SymbolEqualityComparer.Default.Equals(patternType, underlyingType);
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

            if (TryDistributePatternOverConditionalValue(
                    value,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    valueType,
                    inlineDepth))
            {
                return true;
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion, inlineDepth) &&
                constantValue != null &&
                SymbolicFactFactory.CanCompareSmtValues(value, constantValue))
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
                return TryTranslateRecursivePattern(value, valueType, recursivePattern, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
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

            if (pattern is DeclarationPatternSyntax declarationPattern)
            {
                if (!TryTranslateReferenceTypePattern(
                        value,
                        valueType,
                        declarationPattern.Type,
                        semanticModel,
                        cancellationToken,
                        out formula))
                {
                    return false;
                }

                return true;
            }

            if (pattern is TypePatternSyntax typePattern)
            {
                if (!TryTranslateReferenceTypePattern(
                        value,
                        valueType,
                        typePattern.Type,
                        semanticModel,
                        cancellationToken,
                        out formula))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static bool TryDistributePatternOverConditionalValue(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            ITypeSymbol? valueType,
            int inlineDepth)
        {
            formula = null;
            if (inlineDepth >= MaxConditionalPatternDistributionDepth ||
                value is not SmtConditionalFormula conditionalValue ||
                conditionalValue.Condition.Kind != SmtValueKind.Bool ||
                conditionalValue.WhenTrue.Kind != conditionalValue.ResultKind ||
                conditionalValue.WhenFalse.Kind != conditionalValue.ResultKind ||
                !TryTranslatePattern(
                    conditionalValue.WhenTrue,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var whenTruePattern,
                    getSymbolVersion,
                    valueType,
                    inlineDepth + 1) ||
                whenTruePattern is not { Kind: SmtValueKind.Bool } ||
                !TryTranslatePattern(
                    conditionalValue.WhenFalse,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var whenFalsePattern,
                    getSymbolVersion,
                    valueType,
                    inlineDepth + 1) ||
                whenFalsePattern is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            formula = whenTruePattern.Equals(whenFalsePattern)
                ? whenTruePattern
                : new SmtConditionalFormula(
                    conditionalValue.Condition,
                    whenTruePattern,
                    whenFalsePattern,
                    SmtValueKind.Bool);
            return true;
        }

        private static bool TryTranslateReferenceTypePattern(
            SmtFormula value,
            ITypeSymbol? valueType,
            TypeSyntax patternTypeSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var patternType = semanticModel.GetTypeInfo(patternTypeSyntax, cancellationToken).Type;
            if (IsTypeKnownAssignableTo(valueType, patternType))
            {
                formula = CreateNonNullFormula(value);
                return true;
            }

            if (!TryCreateRuntimeTypeTestFormula(value, patternType, out var runtimeTypeTest))
            {
                return false;
            }

            formula = Conjoin(CreateNonNullFormula(value), runtimeTypeTest);
            return true;
        }

        private static bool TryTranslateRecursivePattern(
            SmtFormula value,
            ITypeSymbol? valueType,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            SmtFormula? current = ShouldRequireRecursivePatternNonNull(value, valueType)
                ? CreateNonNullFormula(value)
                : null;

            var positionalSubpatterns = recursivePattern.PositionalPatternClause?.Subpatterns;
            if (positionalSubpatterns != null)
            {
                for (var position = 0; position < positionalSubpatterns.Value.Count; position++)
                {
                    if (!TryTranslateTuplePositionalSubpattern(
                            value,
                            valueType,
                            positionalSubpatterns.Value[position],
                            position,
                            semanticModel,
                            cancellationToken,
                            out var positionalFormula,
                            getSymbolVersion,
                            inlineDepth) ||
                        positionalFormula == null)
                    {
                        return false;
                    }

                    current = Conjoin(current, positionalFormula);
                }
            }

            var propertySubpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (propertySubpatterns != null)
            {
                foreach (var subpattern in propertySubpatterns.Value)
                {
                    if (!TryTranslatePropertySubpattern(value, subpattern, semanticModel, cancellationToken, out var subpatternFormula, getSymbolVersion, inlineDepth) ||
                        subpatternFormula == null)
                    {
                        return false;
                    }

                    current = Conjoin(current, subpatternFormula);
                }
            }

            formula = current;
            return formula != null;
        }

        private static bool ShouldRequireRecursivePatternNonNull(SmtFormula value, ITypeSymbol? valueType)
        {
            return value.Kind == SmtValueKind.Reference &&
                (valueType == null || valueType.IsReferenceType);
        }

        private static SmtFormula Conjoin(SmtFormula? left, SmtFormula right)
        {
            return left == null
                ? right
                : new SmtBinaryFormula(SmtBinaryOperator.And, left, right);
        }

        private static bool TryTranslateTuplePositionalSubpattern(
            SmtFormula receiver,
            ITypeSymbol? receiverType,
            SubpatternSyntax subpattern,
            int position,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryResolveTuplePositionalSubpatternValue(
                    receiver,
                    receiverType,
                    position,
                    out var memberValue,
                    out var memberType) ||
                memberValue == null ||
                memberType == null)
            {
                return false;
            }

            return TryTranslatePattern(
                memberValue,
                subpattern.Pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                memberType,
                inlineDepth) &&
                formula != null;
        }

        private static bool TryResolveTuplePositionalSubpatternValue(
            SmtFormula receiver,
            ITypeSymbol? receiverType,
            int position,
            out SmtFormula? memberValue,
            out ITypeSymbol? memberType)
        {
            memberValue = null;
            memberType = null;
            if (!TryGetTuplePositionalField(receiverType, position, out var fieldSymbol) ||
                !TryGetTupleElementStorageName(fieldSymbol, out var storageName) ||
                !TryGetValueKind(fieldSymbol.Type, out var kind))
            {
                return false;
            }

            memberType = fieldSymbol.Type;
            memberValue = new SmtVariable(GetFormulaVariableName(receiver) + "." + storageName, kind);
            return true;
        }

        private static string GetFormulaVariableName(SmtFormula formula)
        {
            return formula is SmtVariable variable
                ? variable.Name
                : formula.ToString() ?? string.Empty;
        }

        private static bool TryGetTuplePositionalField(
            ITypeSymbol? receiverType,
            int position,
            out IFieldSymbol fieldSymbol)
        {
            fieldSymbol = null!;
            if (receiverType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            if (namedType.IsTupleType)
            {
                if (position < 0 || position >= namedType.TupleElements.Length)
                {
                    return false;
                }

                fieldSymbol = namedType.TupleElements[position];
                return true;
            }

            var storageName = "Item" + (position + 1).ToString(CultureInfo.InvariantCulture);
            fieldSymbol = namedType
                .GetMembers(storageName)
                .OfType<IFieldSymbol>()
                .FirstOrDefault(static field => !field.IsStatic)!;
            return fieldSymbol != null;
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
            if (!TryResolvePropertySubpatternValue(
                    receiver,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    out var memberValue,
                    out var memberType,
                    out var pathCondition) ||
                memberValue == null ||
                memberType == null)
            {
                return false;
            }

            if (!TryTranslatePattern(memberValue, subpattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, memberType, inlineDepth) ||
                formula == null)
            {
                return false;
            }

            if (pathCondition != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, pathCondition, formula);
            }

            return true;
        }

        private static bool TryResolvePropertySubpatternValue(
            SmtFormula receiver,
            SubpatternSyntax subpattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? memberValue,
            out ITypeSymbol? memberType,
            out SmtFormula? pathCondition)
        {
            memberValue = null;
            memberType = null;
            pathCondition = null;

            var propertyPath = (SyntaxNode?)subpattern.NameColon?.Name ?? subpattern.ExpressionColon?.Expression;
            if (propertyPath == null ||
                !TryGetPropertySubpatternMemberNames(propertyPath, out var memberNames))
            {
                return false;
            }

            var currentValue = receiver;
            for (var index = 0; index < memberNames.Length; index++)
            {
                var memberName = memberNames[index];
                var memberSymbol = semanticModel.GetSymbolInfo(memberName, cancellationToken).Symbol;
                if (!TryGetMemberType(memberSymbol, out memberType))
                {
                    return false;
                }

                SmtFormula? nextValue;
                if (memberSymbol?.Name == "Length" &&
                    memberSymbol.ContainingType?.SpecialType == SpecialType.System_String &&
                    TryCreateStringLengthFormula(currentValue, out var stringLengthFormula))
                {
                    memberType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                    nextValue = stringLengthFormula;
                }
                else if (!TryCreateMemberFormula(currentValue, memberSymbol!.Name, memberType, out nextValue) ||
                         nextValue == null)
                {
                    return false;
                }

                currentValue = nextValue;
                if (index < memberNames.Length - 1 &&
                    memberType.IsReferenceType)
                {
                    var nonNull = CreateNonNullFormula(currentValue);
                    pathCondition = pathCondition == null
                        ? nonNull
                        : new SmtBinaryFormula(SmtBinaryOperator.And, pathCondition, nonNull);
                }
            }

            memberValue = currentValue;
            return memberType != null;
        }

        private static bool TryGetPropertySubpatternMemberNames(
            SyntaxNode propertyPath,
            out ImmutableArray<SimpleNameSyntax> memberNames)
        {
            var builder = ImmutableArray.CreateBuilder<SimpleNameSyntax>();
            if (!AddPropertySubpatternMemberNames(propertyPath, builder) ||
                builder.Count == 0)
            {
                memberNames = ImmutableArray<SimpleNameSyntax>.Empty;
                return false;
            }

            memberNames = builder.ToImmutable();
            return true;
        }

        private static bool AddPropertySubpatternMemberNames(
            SyntaxNode propertyPath,
            ImmutableArray<SimpleNameSyntax>.Builder memberNames)
        {
            switch (propertyPath)
            {
                case SimpleNameSyntax simpleName:
                    memberNames.Add(simpleName);
                    return true;
                case QualifiedNameSyntax qualifiedName:
                    return AddPropertySubpatternMemberNames(qualifiedName.Left, memberNames) &&
                        AddPropertySubpatternMemberNames(qualifiedName.Right, memberNames);
                case MemberAccessExpressionSyntax memberAccess:
                    return AddPropertySubpatternMemberNames(memberAccess.Expression, memberNames) &&
                        AddPropertySubpatternMemberNames(memberAccess.Name, memberNames);
                default:
                    return false;
            }
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
            if (value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var canTranslateElementConditions = IsSupportedBuiltInElementAccessReceiver(valueType);
            if (!canTranslateElementConditions &&
                !ListPatternHasOnlySelectionNeutralElements(listPattern))
            {
                return false;
            }

            if (!TryCreateListPatternLengthCondition(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    out var lengthFormulaCondition) ||
                lengthFormulaCondition == null)
            {
                return false;
            }

            var nonNullFormula = CreateNonNullFormula(value);
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, nonNullFormula, lengthFormulaCondition);
            if (canTranslateElementConditions)
            {
                AddListPatternElementConditions(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    cancellationToken,
                    ref formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            return true;
        }

        private static void AddListPatternLengthFacts(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            ICollection<SmtFormula> formulas)
        {
            if (value.Kind != SmtValueKind.Reference ||
                !TryCreateListPatternLengthCondition(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    out var lengthFormulaCondition) ||
                lengthFormulaCondition == null)
            {
                return;
            }

            formulas.Add(CreateNonNullFormula(value));
            formulas.Add(lengthFormulaCondition);
        }

        private static bool TryCreateListPatternLengthCondition(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            out SmtFormula? lengthFormulaCondition)
        {
            lengthFormulaCondition = null;
            if (!TryCreateListPatternLengthFormula(value, valueType, semanticModel, out var lengthFormula) ||
                lengthFormula == null)
            {
                return false;
            }

            GetListPatternLengthShape(listPattern, out var minimumLength, out var exactLength);
            lengthFormulaCondition = exactLength
                ? new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength))
                : new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength));
            return true;
        }

        private static bool TryCreateListPatternLengthFormula(
            SmtFormula value,
            ITypeSymbol? valueType,
            SemanticModel semanticModel,
            out SmtFormula? lengthFormula)
        {
            lengthFormula = null;
            if (valueType?.SpecialType == SpecialType.System_String)
            {
                return TryCreateStringLengthFormula(value, out lengthFormula);
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (valueType is IArrayTypeSymbol { Rank: 1 } ||
                valueType?.SpecialType == SpecialType.System_String ||
                IsBuiltInSpanType(valueType) ||
                IsBuiltInMemoryType(valueType))
            {
                return TryCreateMemberFormula(value, "Length", intType, out lengthFormula) &&
                    lengthFormula != null;
            }

            if (!TryGetListPatternLengthMemberName(valueType, out var memberName))
            {
                return false;
            }

            return TryCreateMemberFormula(value, memberName, intType, out lengthFormula) &&
                lengthFormula != null;
        }

        private static bool TryGetListPatternLengthMemberName(ITypeSymbol? valueType, out string memberName)
        {
            if (SymbolicTypeFacts.HasInstanceInt32Member(valueType, "Length"))
            {
                memberName = "Length";
                return true;
            }

            if (SymbolicTypeFacts.HasInstanceInt32Member(valueType, "Count"))
            {
                memberName = "Count";
                return true;
            }

            memberName = string.Empty;
            return false;
        }

        private static bool TryCreateStringLengthFormula(SmtFormula receiver, out SmtFormula formula)
        {
            formula = null!;
            if (receiver.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var receiverName = receiver is SmtVariable variable
                ? variable.Name
                : receiver.ToString();
            if (string.IsNullOrEmpty(receiverName))
            {
                return false;
            }

            formula = new SmtStringLengthTerm(new SmtVariable(receiverName + ".String", SmtValueKind.String));
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
                !TryGetBuiltInElementAccessElementType(valueType, semanticModel.Compilation, out var elementType) ||
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

        private static void GetListPatternLengthShape(
            ListPatternSyntax listPattern,
            out int minimumLength,
            out bool exactLength)
        {
            minimumLength = 0;
            exactLength = true;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        GetListPatternLengthShape(nestedListPattern, out var nestedMinimumLength, out var nestedExactLength);
                        minimumLength += nestedMinimumLength;
                        exactLength &= nestedExactLength;
                    }
                    else
                    {
                        exactLength = false;
                    }

                    continue;
                }

                minimumLength++;
            }
        }

        private static bool ListPatternHasOnlySelectionNeutralElements(ListPatternSyntax listPattern)
        {
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (IsSelectionNeutralSlicePattern(slicePattern.Pattern))
                    {
                        continue;
                    }

                    if (!TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern) ||
                        !ListPatternHasOnlySelectionNeutralElements(nestedListPattern))
                    {
                        return false;
                    }

                    continue;
                }

                if (!IsSelectionNeutralPattern(subpattern))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSelectionNeutralSlicePattern(PatternSyntax? pattern)
        {
            return pattern == null ||
                IsSelectionNeutralPattern(pattern);
        }

        private static bool IsSelectionNeutralPattern(PatternSyntax pattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            return pattern is DiscardPatternSyntax or VarPatternSyntax;
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

    }
}
