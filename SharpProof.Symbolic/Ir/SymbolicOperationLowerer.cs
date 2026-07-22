namespace SharpProof.Symbolic.Ir;

internal enum SymbolicAssignmentPostconditionProfile {
    Analyzer,
    Symbolic
}
internal static partial class SymbolicOperationLowerer {
    internal static bool TryLowerDivideByZeroHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        var (site, divisor, isRemainder) = operation switch {
            IBinaryOperation binary when binary.OperatorKind is BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder =>
                (binary.Syntax, binary.RightOperand.Syntax as ExpressionSyntax,
                    binary.OperatorKind == BinaryOperatorKind.Remainder),
            ICompoundAssignmentOperation assignment when assignment.OperatorKind is
                BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder =>
                (assignment.Syntax, assignment.Value.Syntax as ExpressionSyntax,
                    assignment.OperatorKind == BinaryOperatorKind.Remainder),
            _ => (null, null, false)
        };
        if (site == null || divisor == null ||
            !SymbolicTypeFacts.IsThrowingDivideByZeroType(
                CSharpSyntaxFacts.GetExpressionType(divisor, context.SemanticModel, context.CancellationToken))) {
            hazard = null!;
            return false;
        }
        const string provenance = "ir.runtime-hazard.divide-by-zero";
        var zero = SymbolicSemanticPipeline.LowerNumericZeroCondition(divisor, context);
        SymbolicTerm? subject = null;
        SymbolicCondition? trigger = null;
        if (zero is { IsExact: true, Value: { } zeroCondition }) {
            trigger = zeroCondition;
            subject = zeroCondition is SymbolicFactCondition {
                Fact.Atom: SymbolicRelationAtom { Left: var left }
            }
                ? left
                : null;
        }
        hazard = CreateHazard(
            site,
            SymbolicRuntimeHazardKind.DivideByZero,
            SymbolicExceptionPreconditionKind.DivideByZero,
            subject,
            trigger,
            ExceptionTypes.DivideByZeroException,
            isRemainder ? ExceptionCategories.DefiniteModuloByZero : ExceptionCategories.DefiniteDivideByZero,
            provenance);
        return true;
    }
    internal static bool TryLowerCheckedOverflowHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) => operation switch {
            IBinaryOperation binary => TryLowerCheckedBinaryOverflow(binary, context, out hazard),
            IUnaryOperation unary => TryLowerCheckedUnaryOverflow(unary, context, out hazard),
            IIncrementOrDecrementOperation update => TryLowerCheckedUpdateOverflow(update, context, out hazard),
            ICompoundAssignmentOperation assignment =>
                TryLowerCheckedCompoundOverflow(assignment, context, out hazard),
            IConversionOperation conversion => TryLowerCheckedConversionOverflow(conversion, context, out hazard),
            _ => NoHazard(out hazard)
        };

    internal static bool TryLowerReferenceNullHazard(
        ExpressionSyntax subjectExpression,
        SymbolicRuntimeHazardKind hazardKind,
        SymbolicExceptionPreconditionKind preconditionKind,
        string exceptionType,
        string category,
        string provenance,
        SymbolicLoweringContext context,
        bool suppressDefinitelyNotNull,
        out SymbolicHazardOperation hazard) {
        if (suppressDefinitelyNotNull &&
            NullableFlowFacts.IsDefinitelyNotNullReferenceValue(subjectExpression, context.SemanticModel, context.CancellationToken))
            return NoHazard(out hazard);

        var lowering = SymbolicSemanticPipeline.LowerTerm(subjectExpression, context);
        var subject = lowering is {
            IsExact: true,
            Value: { } value
        } && (value.Kind == SmtValueKind.Reference || value is SymbolicNullTerm)
                ? value
                : null;
        var trigger = subject == null
            ? null
            : SymbolicIrLowerer.CreateReferenceNullCondition(subject, true, subjectExpression, provenance + ".trigger");
        hazard = CreateHazard(subjectExpression, hazardKind, preconditionKind, subject, trigger, exceptionType, category, provenance);
        return true;
    }
    internal static bool TryLowerNullableValueHazard(
        ExpressionSyntax nullableExpression,
        string exceptionType,
        string category,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        const string provenance = "ir.runtime-hazard.nullable-value.without-value";
        var lowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(nullableExpression, context);
        SymbolicTerm? subject = null;
        SymbolicCondition? trigger = null;
        if (lowering is { IsExact: true, Value: SymbolicNullableHasValueTerm hasValue }) {
            subject = new SymbolicVariableTerm(hasValue.NullableName, SmtValueKind.Reference);
            trigger = new SymbolicNotCondition(new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicTruthAtom(hasValue),
                nullableExpression,
                "ir.runtime-hazard.nullable-value.has-value")));
        }
        hazard = CreateHazard(
            nullableExpression,
            SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
            subject,
            trigger,
            exceptionType,
            category,
            provenance);
        return true;
    }
    internal static bool TryLowerNullableValueAccessHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not MemberAccessExpressionSyntax memberAccess ||
            !SymbolicTypeFacts.IsNullableValueAccess(memberAccess, context.SemanticModel, context.CancellationToken))
            return NoHazard(out hazard);

        if (SymbolicRuntimeHazardSyntaxFacts.HasLaterLoopAssignmentOfMissingNullableValue(
                memberAccess.Expression,
                memberAccess,
                context.SemanticModel,
                context.CancellationToken)) {
            hazard = LowerLoopCarriedNullableValueHazard(memberAccess);
            return true;
        }
        return TryLowerNullableValueHazard(
            memberAccess.Expression,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue,
            context,
            out hazard);
    }
    internal static bool TryLowerDynamicNullBindingHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (!SymbolicDynamicNullBindingFacts.TryGetDynamicNullBindingShape(
                operation.Syntax,
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression,
                out _,
                out var receiver,
                out var category,
                out _) ||
            !SymbolicRuntimeHazardSyntaxFacts.IsDynamicExpression(receiver, context.SemanticModel, context.CancellationToken))
            return NoHazard(out hazard);

        return TryLowerReferenceNullHazard(
            receiver,
            SymbolicRuntimeHazardKind.DynamicNullBinding,
            SymbolicExceptionPreconditionKind.DynamicNullBinding,
            SymbolicDynamicNullBindingFacts.RuntimeBinderExceptionType,
            category,
            "ir.runtime-hazard.dynamic-null-binding",
            context,
            suppressDefinitelyNotNull: false,
            out hazard);
    }
    internal static bool TryLowerNullDereferenceHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) => TryLowerNullDereferenceHazardCore(operation.Syntax, context, out hazard);

    internal static bool TryLowerMemberAccessNullDereferenceHazard(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) => TryLowerNullDereferenceHazardCore(memberAccess, context, out hazard);

    private static bool TryLowerNullDereferenceHazardCore(
        SyntaxNode site,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        ExpressionSyntax? receiver = null;
        var category = ExceptionCategories.DefiniteNullDereference;
        switch (site) {
            case MemberAccessExpressionSyntax memberAccess:
                receiver = memberAccess.Expression;
                break;
            case ElementAccessExpressionSyntax elementAccess:
                receiver = elementAccess.Expression;
                break;
            case ForEachStatementSyntax forEachStatement:
                receiver = forEachStatement.Expression;
                break;
            case ForEachVariableStatementSyntax forEachVariableStatement:
                receiver = forEachVariableStatement.Expression;
                break;
            case AwaitExpressionSyntax awaitExpression:
                receiver = awaitExpression.Expression;
                category = ExceptionCategories.DefiniteAwaitNull;
                break;
            case WithExpressionSyntax withExpression:
                receiver = withExpression.Expression;
                category = ExceptionCategories.DefiniteWithNull;
                break;
            case InvocationExpressionSyntax { Expression: not MemberAccessExpressionSyntax } invocation:
                receiver = invocation.Expression;
                break;
            case AssignmentExpressionSyntax assignment
                when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                     CSharpSyntaxFacts.UnwrapExpression(assignment.Left, ExpressionCastUnwrapPolicy.All) is
                         TupleExpressionSyntax or DeclarationExpressionSyntax &&
                     context.SemanticModel.GetDeconstructionInfo(assignment).Method is
                         IMethodSymbol { IsStatic: false }:
                receiver = assignment.Right;
                category = ExceptionCategories.DefiniteDeconstructionNull;
                break;
        }
        if (receiver == null ||
            SymbolicRuntimeHazardSyntaxFacts.IsDynamicExpression(receiver, context.SemanticModel, context.CancellationToken) ||
            !SymbolicTypeFacts.IsReferenceType(CSharpSyntaxFacts.GetExpressionType(
                receiver,
                context.SemanticModel,
                context.CancellationToken)))
            return NoHazard(out hazard);

        return TryLowerReferenceNullHazard(
            receiver,
            SymbolicRuntimeHazardKind.NullDereference,
            SymbolicExceptionPreconditionKind.NullDereference,
            ExceptionTypes.NullReferenceException,
            category,
            "ir.runtime-hazard.null-dereference",
            context,
            suppressDefinitelyNotNull: true,
            out hazard);
    }
    internal static bool TryLowerArgumentNullHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        ExpressionSyntax? argument = null;
        var category = string.Empty;
        switch (operation) {
            case ILockOperation { Syntax: LockStatementSyntax lockStatement }:
                argument = lockStatement.Expression;
                category = ExceptionCategories.DefiniteLockNull;
                break;
            case IInvocationOperation invocation
                when TryGetRegexRequiredInputExpression(invocation, out var regexInput):
                argument = regexInput;
                category = ExceptionCategories.DefiniteRegexNullInput;
                break;
        }
        if (argument == null ||
            SymbolicRuntimeHazardSyntaxFacts.IsDynamicExpression(argument, context.SemanticModel, context.CancellationToken) ||
            !SymbolicTypeFacts.IsReferenceType(CSharpSyntaxFacts.GetExpressionType(
                argument,
                context.SemanticModel,
                context.CancellationToken)))
            return NoHazard(out hazard);

        return TryLowerReferenceNullHazard(
            argument,
            SymbolicRuntimeHazardKind.ArgumentNull,
            SymbolicExceptionPreconditionKind.ArgumentNull,
            ExceptionTypes.ArgumentNullException,
            category,
            "ir.runtime-hazard.argument-null",
            context,
            suppressDefinitelyNotNull: false,
            out hazard);
    }
    private static bool TryGetRegexRequiredInputExpression(IInvocationOperation operation, out ExpressionSyntax inputExpression) {
        inputExpression = null!;
        if (operation.TargetMethod.Name is not ("IsMatch" or "Match" or "Matches") ||
            !string.Equals(
                SymbolicTypeFacts.GetFullMetadataName(operation.TargetMethod.ContainingType),
                "System.Text.RegularExpressions.Regex",
                StringComparison.Ordinal))
            return false;

        for (var index = 0; index < operation.TargetMethod.Parameters.Length; index++)
            if (string.Equals(operation.TargetMethod.Parameters[index].Name, "input", StringComparison.Ordinal) &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, index, out inputExpression))
                return true;

        return false;
    }
    internal static SymbolicHazardOperation LowerLoopCarriedNullableValueHazard(SyntaxNode site) => CreateHazard(
            site,
            SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
            null,
            new SymbolicConstantCondition(true),
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue,
            "ir.runtime-hazard.nullable-value.loop-carried");

    internal static bool TryLowerNegativeLengthHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        var shape = operation.Syntax switch {
            ArrayCreationExpressionSyntax array => (
                Lengths: CSharpSyntaxFacts.GetExplicitArraySizeExpressions(array),
                Precondition: SymbolicExceptionPreconditionKind.NegativeLength,
                Kind: SymbolicRuntimeHazardKind.NegativeArrayLength,
                Provenance: "ir.runtime-hazard.array.negative-length",
                Category: ExceptionCategories.DefiniteNegativeArrayLength),
            StackAllocArrayCreationExpressionSyntax stackAlloc => (
                Lengths: SymbolicRuntimeHazardSyntaxFacts.GetStackAllocLengthExpressions(stackAlloc),
                Precondition: SymbolicExceptionPreconditionKind.NegativeStackAllocLength,
                Kind: SymbolicRuntimeHazardKind.NegativeStackAllocLength,
                Provenance: "ir.runtime-hazard.stackalloc.negative-length",
                Category: ExceptionCategories.DefiniteNegativeStackAllocLength),
            _ => default
        };
        if (shape.Lengths == null) return NoHazard(out hazard);

        return TryLowerNegativeLengthHazardCore(
            operation.Syntax,
            shape.Lengths,
            shape.Precondition,
            shape.Kind,
            shape.Provenance,
            shape.Category,
            context,
            out hazard);
    }
    private static bool TryLowerNegativeLengthHazardCore(
        SyntaxNode site,
        IEnumerable<ExpressionSyntax> lengthExpressions,
        SymbolicExceptionPreconditionKind preconditionKind,
        SymbolicRuntimeHazardKind hazardKind,
        string provenance,
        string category,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        SymbolicTerm? subject = null;
        SymbolicCondition? trigger = null;
        var hasExpression = false;
        var allExact = true;
        foreach (var expression in lengthExpressions) {
            hasExpression = true;
            var length = LowerIntegerTerm(expression, context);
            if (length == null) {
                allExact = false;
                continue;
            }
            subject ??= length;
            var negative = SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.LessThan,
                length,
                new SymbolicIntegerConstantTerm(0),
                expression,
                provenance + ".trigger");
            trigger = trigger == null
                ? negative
                : new SymbolicBinaryCondition(SymbolicConditionOperator.Or, trigger, negative);
        }
        if (!hasExpression) return NoHazard(out hazard);
        hazard = CreateHazard(
            site,
            hazardKind,
            preconditionKind,
            subject,
            allExact ? trigger : null,
            ExceptionTypes.OverflowException,
            category,
            provenance + ".aggregate",
            preserveUnsupportedSubject: true);
        return true;
    }
    internal static bool TryLowerInvalidCollectionCardinalityHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation is not IInvocationOperation invocationOperation ||
            operation.Syntax is not InvocationExpressionSyntax invocation ||
            invocationOperation.TargetMethod is not {
                IsStatic: false,
                Parameters.Length: 0,
                Name: "Dequeue" or "Peek" or "Pop"
            } method ||
            !IsKnownCardinalityCheckedCollection(method.ContainingType) ||
            invocation.Expression is not MemberAccessExpressionSyntax instanceMember)
            return NoHazard(out hazard);

        var receiver = instanceMember.Expression;
        var lowering = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(receiver, context);
        if (lowering is not { IsExact: true, Value: { Kind: SmtValueKind.Int } count })
            return NoHazard(out hazard);

        const string provenance = "ir.runtime-hazard.collection-cardinality";
        hazard = CreateHazard(
            receiver,
            SymbolicRuntimeHazardKind.InvalidCollectionCardinality,
            SymbolicExceptionPreconditionKind.InvalidCollectionCardinality,
            count,
            SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.Equal,
                count,
                new SymbolicIntegerConstantTerm(0),
                receiver,
                provenance + ".trigger"),
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteInvalidCollectionCardinality,
            provenance);
        return true;
    }
    private static bool IsKnownCardinalityCheckedCollection(INamedTypeSymbol type)
        => type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
               type.OriginalDefinition.MetadataName is "Queue`1" or "Stack`1" or "PriorityQueue`2";

    internal static bool TryLowerElementAccessBoundsHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not ElementAccessExpressionSyntax elementAccess ||
            !SymbolicRuntimeHazardSyntaxFacts.TryGetIndexOrRangeHazardMetadata(
                elementAccess,
                context.SemanticModel,
                context.CancellationToken,
                out var hazardKind,
                out var exceptionType,
                out var category))
            return NoHazard(out hazard);

        SymbolicTerm? subject = null;
        SymbolicCondition? trigger = null;
        var provenance = "ir.runtime-hazard.index.out-of-range";
        if (elementAccess.ArgumentList.Arguments.Count == 1) {
            var indexExpression = CSharpSyntaxFacts.UnwrapExpression(
                elementAccess.ArgumentList.Arguments[0].Expression,
                ExpressionCastUnwrapPolicy.All);
            if (indexExpression is InvocationExpressionSyntax absInvocation &&
                CSharpMathPatternRecognizer.TryGetMathAbsRemainderOperands(
                    absInvocation,
                    context.SemanticModel,
                    context.CancellationToken,
                    out _,
                    out var divisorExpression)) {
                var sourceLength = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(elementAccess.Expression, context);
                var divisorLength = SymbolicSemanticPipeline.LowerTerm(divisorExpression, context);
                if (sourceLength is { IsExact: true, Value: { Kind: SmtValueKind.Int } source } &&
                    divisorLength is { IsExact: true, Value: { Kind: SmtValueKind.Int } divisor } &&
                    Equals(source, divisor)) {
                    trigger = new SymbolicConstantCondition(false);
                    provenance = "ir.runtime-hazard.index.abs-modulo.same-length-unreachable";
                }
            }
        }
        if (trigger == null &&
            CSharpSyntaxFacts.GetExpressionType(elementAccess.Expression, context.SemanticModel, context.CancellationToken) is
                IArrayTypeSymbol { Rank: > 1 } arrayType &&
            elementAccess.ArgumentList.Arguments.Count == arrayType.Rank) {
            var receiver = SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context);
            var bounds = SymbolicSemanticPipeline.LowerArrayElementBoundsCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments.Select(static argument => argument.Expression).ToArray(),
                elementAccess,
                context);
            if (bounds is { IsExact: true, Value: { } inRange }) {
                if (receiver is { IsExact: true, Value: { Kind: SmtValueKind.Reference } receiverTerm })
                    subject = receiverTerm;
                trigger = new SymbolicNotCondition(inRange);
                provenance = "ir.runtime-hazard.index.multidimensional-out-of-range";
            }
        }
        else if (trigger == null && elementAccess.ArgumentList.Arguments.Count == 1) {
            var bounds = SymbolicSemanticPipeline.LowerBuiltInElementAccessOutOfRangeCondition(elementAccess, context);
            if (bounds is { IsExact: true, Value: { } outOfRange }) {
                trigger = outOfRange;
                var index = SymbolicSemanticPipeline.LowerTerm(elementAccess.ArgumentList.Arguments[0].Expression, context);
                if (index is { IsExact: true, Value: { } indexTerm })
                    subject = indexTerm;
                else if (SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context) is {
                    IsExact: true,
                    Value: { Kind: SmtValueKind.Reference } receiver
                })
                    subject = receiver;
            }
        }
        hazard = CreateHazard(
            elementAccess,
            hazardKind,
            GetIndexPreconditionKind(hazardKind),
            subject,
            trigger,
            exceptionType,
            category,
            provenance);
        return true;
    }
    internal static bool TryLowerArrayGetValueBoundsHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation is not IInvocationOperation invocationOperation ||
            operation.Syntax is not InvocationExpressionSyntax invocation ||
            !SymbolicRuntimeHazardSyntaxFacts.IsArrayGetValueInvocation(invocationOperation.TargetMethod) ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
            invocationOperation.Instance.Type is not IArrayTypeSymbol arrayType ||
            invocationOperation.Arguments.Length != arrayType.Rank)
            return NoHazard(out hazard);

        SymbolicTerm? subject = null;
        SymbolicCondition? trigger = null;
        var provenance = "ir.runtime-hazard.array-get-value.index-out-of-range";
        if (arrayType.Rank > 0 &&
            invocationOperation.Arguments.Length == arrayType.Rank &&
            SymbolicValueFacts.TryGetInvocationArgumentExpressionsByOrdinal(invocationOperation, arrayType.Rank,
                out var indexExpressions) &&
            indexExpressions.All(expression =>
                CSharpSyntaxFacts.GetExpressionType(expression, context.SemanticModel, context.CancellationToken)?.SpecialType ==
                SpecialType.System_Int32)) {
            var receiver = SymbolicSemanticPipeline.LowerTerm(receiverExpression, context);
            var bounds = SymbolicSemanticPipeline.LowerArrayElementBoundsCondition(
                receiverExpression, indexExpressions, invocation, context);
            if (receiver is { IsExact: true, Value: { Kind: SmtValueKind.Reference } receiverTerm } &&
                bounds is { IsExact: true, Value: { } inRange }) {
                subject = receiverTerm;
                trigger = new SymbolicNotCondition(inRange);
                if (arrayType.Rank > 1)
                    provenance = "ir.runtime-hazard.array-get-value.multidimensional-index-out-of-range";
            }
        }
        hazard = CreateHazard(
            invocation,
            SymbolicRuntimeHazardKind.IndexOutOfRange,
            SymbolicExceptionPreconditionKind.IndexOutOfRange,
            subject,
            trigger,
            ExceptionTypes.IndexOutOfRangeException,
            ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
            provenance);
        return true;
    }
    internal static bool TryLowerIndexConstructionBoundsHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not ExpressionSyntax expression)
            return NoHazard(out hazard);

        var lowering = SymbolicSemanticPipeline.LowerIndexConstructionArgumentOutOfRangeCondition(expression, context);
        if (lowering is not { IsExact: true, Value: { } trigger })
            return NoHazard(out hazard);

        hazard = CreateHazard(
            expression,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
            null,
            trigger,
            ExceptionTypes.ArgumentOutOfRangeException,
            ExceptionCategories.DefiniteIndexConstructionArgumentOutOfRange,
            "ir.runtime-hazard.index.constructor-argument-out-of-range");
        return true;
    }
    internal static bool TryLowerSlicingBoundsHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation is not IInvocationOperation invocationOperation ||
            operation.Syntax is not InvocationExpressionSyntax invocation ||
            !SymbolicRuntimeHazardSyntaxFacts.TryGetSlicingInvocationShape(
                invocationOperation,
                out var sourceExpression,
                out var startExpression,
                out var countExpression,
                out var oneArgumentUpperBoundIsInclusive,
                out var category))
            return NoHazard(out hazard);

        var inRange = SymbolicSemanticPipeline.LowerSubsequenceInRangeCondition(
            sourceExpression,
            startExpression,
            countExpression,
            invocation,
            context,
            oneArgumentUpperBoundIsInclusive);
        hazard = CreateHazard(
            invocation,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
            null,
            inRange is { IsExact: true, Value: { } condition } ? new SymbolicNotCondition(condition) : null,
            ExceptionTypes.ArgumentOutOfRangeException,
            category,
            "ir.runtime-hazard.slicing.argument-out-of-range");
        return true;
    }
    internal static bool TryLowerNullableValueCastHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (!TryGetBuiltInNonIdentityCast(operation, out var castExpression, out var targetType) ||
            !SymbolicRuntimeHazardSyntaxFacts.IsNullableValueCastShape(
                castExpression,
                targetType,
                context.SemanticModel,
                context.CancellationToken))
            return NoHazard(out hazard);

        return TryLowerNullableValueHazard(
            castExpression.Expression,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue,
            context,
            out hazard);
    }
    internal static bool TryLowerUnboxNullCastHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation is not IConversionOperation { Conversion.IsUserDefined: false } conversion ||
            operation.Syntax is not CastExpressionSyntax castExpression ||
            !SymbolicRuntimeHazardSyntaxFacts.IsUnboxingCastShape(
                castExpression,
                conversion.Type,
                context.SemanticModel,
                context.CancellationToken))
            return NoHazard(out hazard);

        return TryLowerReferenceNullHazard(
            castExpression.Expression,
            SymbolicRuntimeHazardKind.UnboxNull,
            SymbolicExceptionPreconditionKind.UnboxNull,
            ExceptionTypes.NullReferenceException,
            ExceptionCategories.DefiniteUnboxNull,
            "ir.runtime-hazard.unbox-null",
            context,
            suppressDefinitelyNotNull: false,
            out hazard);
    }
    internal static bool TryLowerInvalidCastHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (!TryGetBuiltInNonIdentityCast(operation, out var castExpression, out var targetType))
            return NoHazard(out hazard);

        var isUnboxing = SymbolicRuntimeHazardSyntaxFacts.IsUnboxingCastShape(
            castExpression,
            targetType,
            context.SemanticModel,
            context.CancellationToken);
        if (!isUnboxing) {
            var operandType = CSharpSyntaxFacts.GetExpressionType(
                castExpression.Expression,
                context.SemanticModel,
                context.CancellationToken);
            if (!SymbolicTypeFacts.IsReferenceType(targetType) ||
                !SymbolicTypeFacts.IsReferenceType(operandType))
                return NoHazard(out hazard);
        }
        var operand = castExpression.Expression;
        var operandLowering = SymbolicSemanticPipeline.LowerTerm(operand, context);
        if (operandLowering is { IsExact: true, Value: SymbolicNullTerm })
            return NoHazard(out hazard);

        SymbolicTerm? subject = operandLowering is { IsExact: true, Value: { Kind: SmtValueKind.Reference } reference }
            ? reference
            : null;
        SymbolicCondition? trigger = null;
        var provenance = "ir.runtime-hazard.invalid-cast.unsupported";
        if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                operand,
                castExpression,
                context.SemanticModel,
                context.CancellationToken,
                out var exactRuntimeType)) {
            var valid = isUnboxing
                ? SymbolicRuntimeTypeFacts.CanUnboxExactRuntimeTypeToValueType(exactRuntimeType, targetType)
                : SymbolicRuntimeTypeFacts.CanCastExactRuntimeTypeToReferenceType(
                    exactRuntimeType, targetType, context.SemanticModel.Compilation);
            if (valid) return NoHazard(out hazard);

            if (subject != null) {
                trigger = SymbolicIrLowerer.CreateReferenceNullCondition(
                    subject, false, operand, "ir.runtime-hazard.reference.non-null.guard");
                provenance = "ir.runtime-hazard.invalid-cast.exact-mismatch";
            }
            else {
                provenance = "ir.runtime-hazard.invalid-cast.exact-mismatch.unsupported";
            }
        }
        else if (!isUnboxing &&
                 subject != null &&
                 SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(targetType, out var typeKey)) {
            var nonNull = SymbolicIrLowerer.CreateReferenceNullCondition(subject, false, operand,
                "ir.runtime-hazard.invalid-cast.non-null");
            var isTargetType = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicTypeTestAtom(subject, typeKey),
                operand,
                "ir.runtime-hazard.invalid-cast.type-test"));
            trigger = new SymbolicBinaryCondition(SymbolicConditionOperator.And, nonNull, new SymbolicNotCondition(isTargetType));
            provenance = "ir.runtime-hazard.invalid-cast.mismatch";
        }
        hazard = CreateHazard(
            operand,
            SymbolicRuntimeHazardKind.InvalidCast,
            SymbolicExceptionPreconditionKind.InvalidCast,
            subject,
            trigger,
            ExceptionTypes.InvalidCastException,
            ExceptionCategories.DefiniteInvalidCast,
            provenance.EndsWith(".unsupported", StringComparison.Ordinal)
                ? provenance.Substring(0, provenance.Length - ".unsupported".Length)
                : provenance,
            preserveUnsupportedSubject: provenance.StartsWith("ir.runtime-hazard.invalid-cast.exact-mismatch", StringComparison.Ordinal));
        return true;
    }
    private static bool TryGetBuiltInNonIdentityCast(
        IOperation operation,
        out CastExpressionSyntax castExpression,
        out ITypeSymbol targetType) {
        castExpression = null!;
        targetType = null!;
        if (operation is not IConversionOperation conversion ||
            operation.Syntax is not CastExpressionSyntax resolvedCast ||
            conversion.Conversion.IsUserDefined ||
            conversion.Conversion.IsIdentity ||
            conversion.Type is not { TypeKind: not TypeKind.Dynamic } resolvedTargetType)
            return false;

        castExpression = resolvedCast;
        targetType = resolvedTargetType;
        return true;
    }
    internal static bool TryLowerArrayStoreMismatchHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            CSharpSyntaxFacts.UnwrapExpression(assignment.Left, ExpressionCastUnwrapPolicy.All) is not
                ElementAccessExpressionSyntax elementAccess ||
            !SymbolicRuntimeHazardSyntaxFacts.TryGetArrayElementStoreType(
                elementAccess,
                context.SemanticModel,
                context.CancellationToken,
                out var declaredArrayType) ||
            !SymbolicTypeFacts.IsReferenceType(declaredArrayType.ElementType))
            return NoHazard(out hazard);

        var receiver = SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context);
        var subject = receiver is { IsExact: true, Value: { Kind: SmtValueKind.Reference } value }
            ? value
            : null;
        SymbolicCondition? trigger = null;
        if (declaredArrayType.Rank == 1 &&
            elementAccess.ArgumentList.Arguments.Count == 1 &&
            subject != null &&
            SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(elementAccess, context) is {
                IsExact: true,
                Value: { } inRange
            }) {
            SymbolicCondition? mismatch = null;
            if (SymbolicSemanticPipeline.LowerTerm(assignment.Right, context) is { IsExact: true, Value: SymbolicNullTerm }) {
                mismatch = new SymbolicConstantCondition(false);
            }
            else if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                         elementAccess.Expression,
                         assignment,
                         context.SemanticModel,
                         context.CancellationToken,
                         out var exactRuntimeArrayType) &&
                     exactRuntimeArrayType is IArrayTypeSymbol { Rank: 1 } exactArrayType &&
                     SymbolicTypeFacts.IsReferenceType(exactArrayType.ElementType) &&
                     SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                         assignment.Right,
                         assignment,
                         context.SemanticModel,
                         context.CancellationToken,
                         out var exactAssignedType)) {
                mismatch = new SymbolicConstantCondition(
                    !SymbolicRuntimeTypeFacts.CanStoreExactRuntimeTypeInArrayElement(
                        exactAssignedType,
                        exactArrayType.ElementType,
                        context.SemanticModel.Compilation));
            }
            if (mismatch != null) {
                var receiverNotNull = SymbolicIrLowerer.CreateReferenceNullCondition(
                    subject,
                    false,
                    elementAccess.Expression,
                    "ir.runtime-hazard.array-type-mismatch.receiver-not-null");
                trigger = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    receiverNotNull,
                    new SymbolicBinaryCondition(SymbolicConditionOperator.And, inRange, mismatch));
            }
        }
        hazard = CreateHazard(
            assignment,
            SymbolicRuntimeHazardKind.ArrayTypeMismatch,
            SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
            subject,
            trigger,
            ExceptionTypes.ArrayTypeMismatchException,
            ExceptionCategories.DefiniteArrayTypeMismatch,
            "ir.runtime-hazard.array-type-mismatch",
            preserveUnsupportedSubject: true);
        return true;
    }
    internal static bool TryLowerSwitchNoMatchHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not SwitchExpressionSyntax switchExpression)
            return NoHazard(out hazard);

        SymbolicCondition? selected = null;
        foreach (var arm in switchExpression.Arms) {
            if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var armCondition)) {
                hazard = CreateHazard(
                    switchExpression,
                    SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
                    SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch,
                    null,
                    null,
                    ExceptionTypes.SwitchExpressionException,
                    ExceptionCategories.DefiniteSwitchExpressionNoMatch,
                    "ir.runtime-hazard.switch-expression.no-match");
                return true;
            }
            selected = selected == null
                ? armCondition
                : new SymbolicBinaryCondition(SymbolicConditionOperator.Or, selected, armCondition);
        }
        if (selected == null) return NoHazard(out hazard);
        hazard = CreateHazard(
            switchExpression,
            SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
            SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch,
            null,
            new SymbolicNotCondition(selected),
            ExceptionTypes.SwitchExpressionException,
            ExceptionCategories.DefiniteSwitchExpressionNoMatch,
            "ir.runtime-hazard.switch-expression.no-match");
        return true;
    }
    internal static ImmutableArray<SymbolicHazardOperation> LowerThrowHazards(
        SyntaxNode throwNode,
        bool isRethrow,
        string exceptionType,
        SymbolicLoweringContext context) {
        var hazards = ImmutableArray.CreateBuilder<SymbolicHazardOperation>(2);
        SymbolicTerm? subject = null;
        SymbolicCondition? nullCondition = null;
        if (!isRethrow &&
            SymbolicRuntimeExceptionFacts.TryGetThrowExpression(throwNode, out var expression)) {
            var operand = SymbolicSemanticPipeline.LowerTerm(expression, context);
            if (operand is { IsExact: true, Value: SymbolicNullTerm })
                nullCondition = new SymbolicConstantCondition(true);
            else if (operand is { IsExact: true, Value: { Kind: SmtValueKind.Reference } reference }) {
                subject = reference;
                nullCondition = SymbolicIrLowerer.CreateReferenceNullCondition(
                    reference, true, expression, "ir.runtime-hazard.throw-null.trigger");
            }
        }
        SymbolicCondition directTrigger = new SymbolicConstantCondition(true);
        if (nullCondition != null) {
            hazards.Add(CreateHazard(
                throwNode,
                SymbolicRuntimeHazardKind.DirectThrow,
                SymbolicExceptionPreconditionKind.NullDereference,
                subject,
                nullCondition,
                ExceptionTypes.NullReferenceException,
                ExceptionCategories.DefiniteThrowNull,
                "ir.runtime-hazard.throw-null"));
            directTrigger = new SymbolicNotCondition(nullCondition);
        }
        hazards.Add(CreateHazard(
            throwNode,
            isRethrow ? SymbolicRuntimeHazardKind.Rethrow : SymbolicRuntimeHazardKind.DirectThrow,
            SymbolicExceptionPreconditionKind.DirectThrow,
            subject,
            directTrigger,
            exceptionType,
            isRethrow ? ExceptionCategories.Rethrow : ExceptionCategories.DirectThrow,
            nullCondition == null
                ? "ir.runtime-hazard.direct-throw"
                : "ir.runtime-hazard.direct-throw.non-null"));
        return hazards.ToImmutable();
    }
    internal static bool TryLowerMathAbsOverflowHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation is not IInvocationOperation invocationOperation ||
            operation.Syntax is not InvocationExpressionSyntax invocation ||
            !invocationOperation.TargetMethod.IsStatic ||
            !SymbolicKnownApiLowerer.IsMathAbs(invocationOperation.TargetMethod) ||
            invocationOperation.TargetMethod.Parameters.Length != 1 ||
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(invocationOperation.TargetMethod.ReturnType, out var overflowingValue, out _) ||
            overflowingValue >= 0 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var operand))
            return NoHazard(out hazard);

        var value = LowerIntegerTerm(operand, context);
        if (value == null) return NoHazard(out hazard);

        const string provenance = "ir.runtime-hazard.math.abs-overflow";
        hazard = CreateHazard(
            invocation,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            value,
            CreateIntegerEquality(value, overflowingValue, operand, provenance + ".operand"),
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteCheckedIntegralOverflow,
            provenance);
        return true;
    }
    internal static bool TryLowerMathClampBoundsHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation is not IInvocationOperation invocationOperation ||
            operation.Syntax is not InvocationExpressionSyntax invocation ||
            !invocationOperation.TargetMethod.IsStatic ||
            !SymbolicKnownApiLowerer.IsMathClamp(invocationOperation.TargetMethod) ||
            invocationOperation.TargetMethod.Parameters.Length != 3 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 1, out var minExpression) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 2, out var maxExpression))
            return NoHazard(out hazard);

        var min = LowerIntegerTerm(minExpression, context);
        var max = LowerIntegerTerm(maxExpression, context);
        if (min == null || max == null) return NoHazard(out hazard);

        const string provenance = "ir.runtime-hazard.math.clamp.invalid-bounds";
        hazard = CreateHazard(
            invocation,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
            null,
            SymbolicIrLowerer.CreateRelationCondition(SymbolicRelationOperator.GreaterThan, min, max, invocation, provenance),
            ExceptionTypes.ArgumentException,
            ExceptionCategories.DefiniteInvalidClampBounds,
            provenance);
        return true;
    }
    internal static bool TryLowerKnownArgumentGuardHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not InvocationExpressionSyntax invocation ||
            !SymbolicKnownGuardFacts.TryCreateArgumentOutOfRangeGuardConditions(
                invocation,
                context.SemanticModel,
                context.CancellationToken,
                out var subject,
                out var trigger,
                out _,
                out var guardKey))
            return NoHazard(out hazard);

        hazard = CreateHazard(
            invocation,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
            subject,
            trigger,
            ExceptionTypes.ArgumentOutOfRangeException,
            ExceptionCategories.DefiniteArgumentOutOfRangeGuard,
            "ir.runtime-hazard.argument-out-of-range.guard." + guardKey);
        return true;
    }
    private static bool TryLowerCheckedBinaryOverflow(
        IBinaryOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not BinaryExpressionSyntax expression ||
            operation.OperatorMethod != null ||
            !TryGetCheckedIntegralRange(expression, context, out var minValue, out var maxValue) ||
            !TryGetOverflowOperator(expression.Kind(), operation.IsChecked, minValue, out var smtOperator))
            return NoHazard(out hazard);

        if (smtOperator is SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder) {
            const string provenance = "ir.runtime-hazard.checked-integral.signed-division-overflow";
            var left = LowerIntegerTerm(expression.Left, context);
            var right = LowerIntegerTerm(expression.Right, context);
            var trigger = left != null && right != null
                ? SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(left, right, minValue, expression, provenance)
                : null;
            hazard = CreateCheckedOverflowHazard(expression, left, trigger, provenance,
                ExceptionCategories.DefiniteCheckedIntegralOverflow);
            return true;
        }
        const string binaryProvenance = "ir.runtime-hazard.checked-integral.binary-overflow";
        var inRange = SymbolicSemanticPipeline.LowerIntegerBinaryInRangeCondition(
            expression.Left,
            expression.Right,
            smtOperator,
            minValue,
            maxValue,
            expression,
            context);
        hazard = CreateCheckedOverflowHazard(
            expression,
            null,
            inRange is { IsExact: true, Value: { } condition } ? new SymbolicNotCondition(condition) : null,
            binaryProvenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }
    private static bool TryLowerCheckedUnaryOverflow(
        IUnaryOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not PrefixUnaryExpressionSyntax expression ||
            operation.OperatorKind != UnaryOperatorKind.Minus ||
            operation.OperatorMethod != null ||
            !operation.IsChecked ||
            !TryGetCheckedIntegralRange(expression, context, out var minValue, out _))
            return NoHazard(out hazard);

        const string provenance = "ir.runtime-hazard.checked-integral.unary-minus-overflow";
        var value = LowerIntegerTerm(expression.Operand, context);
        hazard = CreateCheckedOverflowHazard(
            expression,
            value,
            value == null ? null : CreateIntegerEquality(value, minValue, expression.Operand, provenance + ".operand"),
            provenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }
    private static bool TryLowerCheckedUpdateOverflow(
        IIncrementOrDecrementOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not ExpressionSyntax expression ||
            operation.Target.Syntax is not ExpressionSyntax operand ||
            operation.OperatorMethod != null ||
            !operation.IsChecked ||
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(operation.Target.Type, out var minValue, out var maxValue))
            return NoHazard(out hazard);

        var increment = operation.Kind == OperationKind.Increment;
        var provenance = increment
            ? "ir.runtime-hazard.checked-integral.increment-overflow"
            : "ir.runtime-hazard.checked-integral.decrement-overflow";
        var value = LowerIntegerTerm(operand, context);
        hazard = CreateCheckedOverflowHazard(
            expression,
            value,
            value == null
                ? null
                : CreateIntegerEquality(value, increment ? maxValue : minValue, operand, provenance + ".operand"),
            provenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }
    private static bool TryLowerCheckedCompoundOverflow(
        ICompoundAssignmentOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not AssignmentExpressionSyntax assignment ||
            operation.Target.Syntax is not ExpressionSyntax leftExpression ||
            operation.Value.Syntax is not ExpressionSyntax rightExpression ||
            operation.OperatorMethod != null ||
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(operation.Target.Type, out var minValue, out var maxValue) ||
            !CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignment.Kind(), out var binaryKind) ||
            !TryGetOverflowOperator(binaryKind, operation.IsChecked, minValue, out var smtOperator))
            return NoHazard(out hazard);

        var left = LowerIntegerTerm(leftExpression, context);
        if (smtOperator is SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder) {
            const string provenance = "ir.runtime-hazard.checked-integral.compound-signed-division-overflow";
            var right = LowerIntegerTerm(rightExpression, context);
            var trigger = left != null && right != null
                ? SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(left, right, minValue, assignment, provenance)
                : null;
            hazard = CreateCheckedOverflowHazard(assignment, left, trigger, provenance,
                ExceptionCategories.DefiniteCheckedIntegralOverflow);
            return true;
        }
        const string compoundProvenance = "ir.runtime-hazard.checked-integral.compound-assignment-overflow";
        var inRange = SymbolicSemanticPipeline.LowerIntegerBinaryInRangeCondition(
            leftExpression,
            rightExpression,
            smtOperator,
            minValue,
            maxValue,
            assignment,
            context);
        hazard = CreateCheckedOverflowHazard(
            assignment,
            left,
            inRange is { IsExact: true, Value: { } condition } ? new SymbolicNotCondition(condition) : null,
            compoundProvenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }
    private static bool TryLowerCheckedConversionOverflow(
        IConversionOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard) {
        if (operation.Syntax is not CastExpressionSyntax cast ||
            operation.Operand.Syntax is not ExpressionSyntax operand ||
            !operation.IsChecked ||
            operation.Conversion is not {
                Exists: true,
                IsIdentity: false,
                IsImplicit: false,
                IsNumeric: true,
                IsUserDefined: false,
                MethodSymbol: null
            } ||
            !SymbolicTypeFacts.TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(cast, context.SemanticModel, context.CancellationToken),
                out var minValue,
                out var maxValue))
            return NoHazard(out hazard);

        if (SymbolicTypeFacts.TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(operand, context.SemanticModel, context.CancellationToken),
                out var sourceMinValue,
                out var sourceMaxValue) &&
            sourceMinValue >= minValue &&
            sourceMaxValue <= maxValue)
            return NoHazard(out hazard);

        const string provenance = "ir.runtime-hazard.checked-conversion.overflow";
        var value = LowerIntegerTerm(operand, context);
        var trigger = value == null
            ? null
            : new SymbolicNotCondition(SymbolicIrLowerer.CreateIntegerInRangeCondition(value, minValue, maxValue, operand, provenance));
        hazard = CreateCheckedOverflowHazard(cast, value, trigger, provenance,
            ExceptionCategories.DefiniteCheckedNumericConversionOverflow);
        return true;
    }
    private static SymbolicHazardOperation CreateCheckedOverflowHazard(
        SyntaxNode site,
        SymbolicTerm? subject,
        SymbolicCondition? trigger,
        string provenance,
        string category)
        => CreateHazard(
            site,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            subject,
            trigger,
            ExceptionTypes.OverflowException,
            category,
            provenance);

    private static SymbolicHazardOperation CreateHazard(
        SyntaxNode site,
        SymbolicRuntimeHazardKind hazardKind,
        SymbolicExceptionPreconditionKind preconditionKind,
        SymbolicTerm? subject,
        SymbolicCondition? trigger,
        string exceptionType,
        string category,
        string provenance,
        bool preserveUnsupportedSubject = false) {
        var confidence = trigger == null ? SymbolicFactConfidence.Unsupported : SymbolicFactConfidence.Exact;
        if (trigger == null) {
            provenance += ".unsupported";
            if (!preserveUnsupportedSubject) subject = null;
            trigger = CreateUnsupportedHazardCondition(site, provenance);
        }
        return new SymbolicHazardOperation(
            hazardKind,
            preconditionKind,
            subject,
            trigger,
            confidence,
            exceptionType,
            category,
            new SymbolicOperationOrigin(site.Span, 0, provenance));
    }
    private static SymbolicCondition CreateIntegerEquality(SymbolicTerm value, long constant, SyntaxNode source, string provenance) =>
        SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            value,
            new SymbolicIntegerConstantTerm(constant),
            source,
            provenance);

    private static SymbolicTerm? LowerIntegerTerm(ExpressionSyntax expression, SymbolicLoweringContext context) {
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        return lowering is { IsExact: true, Value: { Kind: SmtValueKind.Int } value } ? value : null;
    }
    private static SymbolicExceptionPreconditionKind GetIndexPreconditionKind(SymbolicRuntimeHazardKind hazardKind) =>
        hazardKind == SymbolicRuntimeHazardKind.ArgumentOutOfRange
            ? SymbolicExceptionPreconditionKind.ArgumentOutOfRange
            : SymbolicExceptionPreconditionKind.IndexOutOfRange;

    private static bool TryGetCheckedIntegralRange(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out long minValue,
        out long maxValue) {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return SymbolicTypeFacts.TryGetCheckedIntegralRange(typeInfo.ConvertedType ?? typeInfo.Type, out minValue, out maxValue);
    }
    private static bool TryGetOverflowOperator(
        SyntaxKind syntaxKind,
        bool isChecked,
        long minimum,
        out SmtIntegerBinaryOperator smtOperator) {
        smtOperator = default;
        if (!SymbolicOperatorLowerer.TryGetBinaryTermOperator(syntaxKind, out var binaryOperator) ||
            (binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract or
                SymbolicBinaryTermOperator.Multiply) && !isChecked ||
            (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder) &&
            minimum >= 0)
            return false;

        smtOperator = SymbolicOperatorLowerer.GetSmtIntegerBinaryOperator(binaryOperator);
        return true;
    }
    private static bool NoHazard(out SymbolicHazardOperation hazard) {
        hazard = null!;
        return false;
    }
    private static SymbolicCondition CreateUnsupportedHazardCondition(SyntaxNode site, string provenance) {
        var name = "unsupported_typed_projection#" + site.SpanStart.ToString(CultureInfo.InvariantCulture) +
                   "_" + site.Span.End.ToString(CultureInfo.InvariantCulture);
        return new SymbolicFactCondition(new SymbolicFact(
            new SymbolicTruthAtom(new SymbolicVariableTerm(name, SmtValueKind.Bool)),
            true,
            SymbolicFactConfidence.Exact,
            provenance + ".trigger",
            site.Span,
            null,
            provenance + ".trigger"));
    }
}
