using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Roslyn;
using static SharpProof.Meta.Analyzers.SharpProofSoundnessAnalyzer;

namespace SharpProof.Meta.Analyzers;

/// <summary>
/// Audits cancellation catches at the small set of process and verification
/// boundaries that are allowed to translate cancellation.
/// </summary>
internal static class CancellationBoundaryAnalyzer
{
    internal static void AnalyzeCatchClause(
        SyntaxNodeAnalysisContext context,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        var clause = (CatchClauseSyntax)context.Node;
        var cancellationType =
            symbols[SharpProofSoundnessAnalyzer.KnownType.OperationCanceledException];
        var caughtType = clause.Declaration?.Type == null
            ? null
            : context.SemanticModel
                .GetTypeInfo(clause.Declaration.Type, context.CancellationToken)
                .Type;
        if (!CatchesCancellation(clause, caughtType, cancellationType) ||
            CancellationHandledEarlier(clause, cancellationType, context) ||
            FilterExcludesCancellation(
                clause, caughtType, cancellationType, context) ||
            RethrowsCancellationImmediately(clause) ||
            IsAuditedCancellationBoundary(
                clause,
                context,
                context.ContainingSymbol,
                symbols))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.SwallowedCancellation,
            clause.CatchKeyword.GetLocation()));
    }

    private static bool CancellationHandledEarlier(
        CatchClauseSyntax clause,
        INamedTypeSymbol? cancellationType,
        SyntaxNodeAnalysisContext context)
    {
        if (cancellationType == null || clause.Parent is not TryStatementSyntax statement)
        {
            return false;
        }

        foreach (var previous in statement.Catches)
        {
            if (ReferenceEquals(previous, clause))
            {
                return false;
            }

            if (previous.Declaration?.Type == null)
            {
                if (previous.Filter == null ||
                    FilterIncludesAllCancellation(
                        previous, null, cancellationType, context))
                {
                    return true;
                }
                continue;
            }

            var previousType = context.SemanticModel.GetTypeInfo(
                previous.Declaration.Type, context.CancellationToken).Type;
            if (IsOrDerivesFrom(cancellationType, previousType) &&
                (previous.Filter == null ||
                 FilterIncludesAllCancellation(
                     previous, previousType, cancellationType, context)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FilterIncludesAllCancellation(
        CatchClauseSyntax clause,
        ITypeSymbol? caughtType,
        INamedTypeSymbol cancellationType,
        SyntaxNodeAnalysisContext context)
    {
        var outcome = EvaluateFilter(
            clause,
            caughtType,
            cancellationType,
            context);
        return outcome == CancellationFilterOutcome.None ||
            outcome == CancellationFilterOutcome.ReturnsTrue;
    }

    private static bool CatchesCancellation(
        CatchClauseSyntax clause,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType)
    {
        return cancellationType != null &&
            (clause.Declaration == null ||
             IsOrDerivesFrom(caughtType, cancellationType) ||
             IsOrDerivesFrom(cancellationType, caughtType));
    }

    private static bool FilterExcludesCancellation(
        CatchClauseSyntax clause,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType,
        SyntaxNodeAnalysisContext context)
    {
        var outcome = EvaluateFilter(
            clause,
            caughtType,
            cancellationType,
            context);
        return outcome != CancellationFilterOutcome.None &&
            (outcome & CancellationFilterOutcome.ReturnsTrue) == 0;
    }

    private static CancellationFilterOutcome EvaluateFilter(
        CatchClauseSyntax clause,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType,
        SyntaxNodeAnalysisContext context)
    {
        if (clause.Filter?.FilterExpression is not { } filter)
        {
            return CancellationFilterOutcome.None;
        }

        if (context.SemanticModel.GetConstantValue(
                filter, context.CancellationToken) is
            { HasValue: true, Value: bool constant })
        {
            return constant
                ? CancellationFilterOutcome.ReturnsTrue
                : CancellationFilterOutcome.ReturnsFalse;
        }

        if (clause.Declaration == null ||
            context.SemanticModel.GetDeclaredSymbol(
                clause.Declaration,
                context.CancellationToken) is not ILocalSymbol caughtLocal)
        {
            return CancellationFilterOutcome.Unknown;
        }

        return EvaluateCancellationFilter(
            GetFilterOperation(filter, context),
            caughtLocal,
            caughtType,
            cancellationType);
    }

    private static IOperation? GetFilterOperation(
        ExpressionSyntax expression,
        SyntaxNodeAnalysisContext context)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return context.SemanticModel.GetOperation(
            expression, context.CancellationToken);
    }

    private static CancellationFilterOutcome EvaluateCancellationFilter(
        IOperation? operation,
        ILocalSymbol caughtLocal,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType)
    {
        operation = Unwrap(operation);
        if (operation?.ConstantValue is
            { HasValue: true, Value: bool constant })
        {
            return constant
                ? CancellationFilterOutcome.ReturnsTrue
                : CancellationFilterOutcome.ReturnsFalse;
        }

        return operation switch
        {
            IIsTypeOperation typeTest
                when ReferencesLocal(typeTest.ValueOperand, caughtLocal) =>
                EvaluateTypeTest(
                    typeTest.TypeOperand, caughtType, cancellationType),
            IIsPatternOperation patternTest
                when ReferencesLocal(patternTest.Value, caughtLocal) =>
                EvaluatePattern(
                    patternTest.Pattern, caughtType, cancellationType),
            IUnaryOperation
            {
                OperatorMethod: null,
                OperatorKind: UnaryOperatorKind.Not
            } unary =>
                Negate(EvaluateCancellationFilter(
                    unary.Operand,
                    caughtLocal,
                    caughtType,
                    cancellationType)),
            IBinaryOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.ConditionalOr
            } binary =>
                ConditionalOr(
                    EvaluateCancellationFilter(
                        binary.LeftOperand,
                        caughtLocal,
                        caughtType,
                        cancellationType),
                    EvaluateCancellationFilter(
                        binary.RightOperand,
                        caughtLocal,
                        caughtType,
                        cancellationType)),
            IBinaryOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.ConditionalAnd
            } binary =>
                ConditionalAnd(
                    EvaluateCancellationFilter(
                        binary.LeftOperand,
                        caughtLocal,
                        caughtType,
                        cancellationType),
                    EvaluateCancellationFilter(
                        binary.RightOperand,
                        caughtLocal,
                        caughtType,
                        cancellationType)),
            _ => CancellationFilterOutcome.Unknown
        };
    }

    private static CancellationFilterOutcome EvaluatePattern(
        IPatternOperation pattern,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType)
    {
        return pattern switch
        {
            ITypePatternOperation typePattern =>
                EvaluateTypeTest(
                    typePattern.MatchedType, caughtType, cancellationType),
            IConstantPatternOperation constantPattern
                when IsNullConstant(constantPattern.Value) =>
                CancellationFilterOutcome.ReturnsFalse,
            IConstantPatternOperation =>
                CancellationFilterOutcome.ReturnsFalse |
                CancellationFilterOutcome.ReturnsTrue,
            IDiscardPatternOperation => CancellationFilterOutcome.ReturnsTrue,
            INegatedPatternOperation negated =>
                Negate(EvaluatePattern(
                    negated.Pattern, caughtType, cancellationType)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Or =>
                ConditionalOr(
                    EvaluatePattern(
                        binary.LeftPattern, caughtType, cancellationType),
                    EvaluatePattern(
                        binary.RightPattern, caughtType, cancellationType)),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.And =>
                ConditionalAnd(
                    EvaluatePattern(
                        binary.LeftPattern, caughtType, cancellationType),
                    EvaluatePattern(
                        binary.RightPattern, caughtType, cancellationType)),
            _ => CancellationFilterOutcome.Unknown
        };
    }

    private static CancellationFilterOutcome EvaluateTypeTest(
        ITypeSymbol matchedType,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType)
    {
        if (IsAssignableTo(cancellationType, matchedType) ||
            IsAssignableTo(caughtType, matchedType))
        {
            return CancellationFilterOutcome.ReturnsTrue;
        }
        return TypePatternExcludesCancellation(matchedType, cancellationType)
            ? CancellationFilterOutcome.ReturnsFalse
            : CancellationFilterOutcome.ReturnsFalse |
              CancellationFilterOutcome.ReturnsTrue;
    }

    private static CancellationFilterOutcome Negate(
        CancellationFilterOutcome outcome)
    {
        var result = outcome & CancellationFilterOutcome.Throws;
        if ((outcome & CancellationFilterOutcome.ReturnsFalse) != 0)
        {
            result |= CancellationFilterOutcome.ReturnsTrue;
        }
        if ((outcome & CancellationFilterOutcome.ReturnsTrue) != 0)
        {
            result |= CancellationFilterOutcome.ReturnsFalse;
        }
        return result;
    }

    private static CancellationFilterOutcome ConditionalAnd(
        CancellationFilterOutcome left,
        CancellationFilterOutcome right)
    {
        var result = left & CancellationFilterOutcome.Throws;
        if ((left & CancellationFilterOutcome.ReturnsFalse) != 0)
        {
            result |= CancellationFilterOutcome.ReturnsFalse;
        }
        if ((left & CancellationFilterOutcome.ReturnsTrue) != 0)
        {
            result |= right;
        }
        return result;
    }

    private static CancellationFilterOutcome ConditionalOr(
        CancellationFilterOutcome left,
        CancellationFilterOutcome right)
    {
        var result = left & CancellationFilterOutcome.Throws;
        if ((left & CancellationFilterOutcome.ReturnsTrue) != 0)
        {
            result |= CancellationFilterOutcome.ReturnsTrue;
        }
        if ((left & CancellationFilterOutcome.ReturnsFalse) != 0)
        {
            result |= right;
        }
        return result;
    }

    private static bool ReferencesLocal(
        IOperation? operation,
        ILocalSymbol local)
    {
        return Unwrap(operation) is ILocalReferenceOperation reference &&
            SymbolEqualityComparer.Default.Equals(reference.Local, local);
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        return OperationUnwrapping.Unwrap(operation);
    }

    private static IOperation? UnwrapConfigureAwait(
        IOperation? operation,
        INamedTypeSymbol? awaitedType = null)
    {
        operation = Unwrap(operation);
        return operation is IInvocationOperation configureAwait &&
            configureAwait.TargetMethod is
            {
                Name: "ConfigureAwait",
                IsStatic: false,
                Parameters.Length: 1
            } method &&
            method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean &&
            (awaitedType == null || SymbolEqualityComparer.Default.Equals(
                method.ContainingType,
                awaitedType))
                ? Unwrap(configureAwait.Instance)
                : operation;
    }

    private static bool TypePatternExcludesCancellation(
        ITypeSymbol matchedType,
        INamedTypeSymbol? cancellationType)
    {
        return matchedType.TypeKind == TypeKind.Class &&
            !IsOrDerivesFrom(cancellationType, matchedType) &&
            !IsOrDerivesFrom(matchedType, cancellationType);
    }

    private static bool IsNullConstant(IOperation operation)
    {
        return Unwrap(operation)?.ConstantValue is { HasValue: true, Value: null };
    }

    [Flags]
    private enum CancellationFilterOutcome
    {
        None = 0,
        ReturnsFalse = 1,
        ReturnsTrue = 2,
        Throws = 4,
        Unknown = ReturnsFalse | ReturnsTrue | Throws
    }

    private static bool IsOrDerivesFrom(
        ITypeSymbol? type,
        ITypeSymbol? possibleBase)
    {
        return RoslynSymbolFacts.IsOrDerivesFrom(type, possibleBase);
    }

    private static bool IsAssignableTo(
        ITypeSymbol? type,
        ITypeSymbol? possibleBase)
    {
        if (IsOrDerivesFrom(type, possibleBase))
        {
            return true;
        }
        if (type is not INamedTypeSymbol namedType ||
            possibleBase?.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        return namedType.AllInterfaces.Any(implemented =>
            SymbolEqualityComparer.Default.Equals(
                implemented.OriginalDefinition,
                possibleBase.OriginalDefinition));
    }

    private static bool RethrowsCancellationImmediately(CatchClauseSyntax clause)
    {
        if (clause.Block.Statements.FirstOrDefault() is not
            ThrowStatementSyntax { } throwStatement)
        {
            return false;
        }

        if (throwStatement.Expression == null)
        {
            return true;
        }

        // `throw caught;` is equivalent to a bare rethrow when it is the
        // caught exception itself. Do not authorize arbitrary expressions
        // (including another exception or a method call) at this boundary.
        var expression = throwStatement.Expression;
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return clause.Declaration?.Identifier is { } identifier &&
            expression is IdentifierNameSyntax thrownIdentifier &&
            string.Equals(
                identifier.ValueText,
                thrownIdentifier.Identifier.ValueText,
                StringComparison.Ordinal);
    }

    private static bool IsAuditedCancellationBoundary(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        ISymbol? containingSymbol,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        if (containingSymbol is not IMethodSymbol method)
        {
            return false;
        }

        if (IsAuditedWorkerMain(
                method,
                symbols[SharpProofSoundnessAnalyzer.KnownType.WorkerProgram],
                symbols.TaskOfInt32))
        {
            return ReifiesWorkerProgramCancellation(
                clause,
                context,
                method,
                symbols);
        }

        if (SymbolEqualityComparer.Default.Equals(
                method,
                symbols.WorkerVerifyAsync))
        {
            return ReifiesWorkerVerificationCancellation(
                clause,
                context,
                method,
                symbols);
        }

        if (method is not
            {
                Name: "VerifyTargetAsync",
                IsStatic: true,
                Parameters.Length: 7
            } ||
            !IsSameType(
                method.ContainingType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.CallableVerificationPolicy]) ||
            !SymbolEqualityComparer.Default.Equals(
                method.ReturnType,
                symbols.VerifyTargetTask) ||
            method.Parameters[6].Name != "callerCancellation" ||
            !IsSameType(
                method.Parameters[6].Type,
                symbols[SharpProofSoundnessAnalyzer.KnownType.CancellationToken]))
        {
            return false;
        }

        return ReifiesCallerCancellation(clause, context, method, symbols);
    }

    private static bool ReifiesWorkerProgramCancellation(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        if (SoleReturn(clause.Block)?.Expression is not
                ExpressionSyntax returnExpression)
        {
            return false;
        }

        if (Unwrap(context.SemanticModel.GetOperation(
                returnExpression,
                context.CancellationToken)) is not IAwaitOperation awaited)
        {
            return false;
        }

        var responseOperation = UnwrapConfigureAwait(
            awaited.Operation,
            symbols.TaskOfInt32);

        if (responseOperation is not IInvocationOperation respond ||
            respond.TargetMethod is not
            {
                Name: "Respond",
                MethodKind: MethodKind.LocalFunction,
                Parameters.Length: 1
            } respondMethod ||
            !SymbolEqualityComparer.Default.Equals(
                respondMethod.ContainingSymbol,
                method) ||
            !SymbolEqualityComparer.Default.Equals(
                respondMethod.ReturnType,
                symbols.TaskOfInt32) ||
            !IsSameType(
                respondMethod.Parameters[0].Type,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.WorkerVerifyResponse]) ||
            !PublishesWorkerResponse(
                respondMethod,
                context,
                method,
                symbols) ||
            respond.Arguments.SingleOrDefault(candidate =>
                candidate.Parameter?.Ordinal == 0) is not { Value: { } response } ||
            Unwrap(response) is not IInvocationOperation create)
        {
            return false;
        }

        if (create.TargetMethod is not
            {
                Name: "Create",
                MethodKind: MethodKind.Ordinary,
                IsStatic: true
            } createMethod ||
            !IsSameType(
                createMethod.ContainingType,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.WorkerResultAssembler]) ||
            !IsSameType(
                createMethod.ReturnType,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.WorkerVerifyResponse]))
        {
            return false;
        }

        var runStatus = createMethod.Parameters.SingleOrDefault(candidate =>
            string.Equals(
                candidate.Name,
                "runStatus",
                StringComparison.Ordinal) &&
            IsSameType(
                candidate.Type,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.WorkerRunStatus]));
        var runStatusArgument = create.Arguments.SingleOrDefault(candidate =>
            SymbolEqualityComparer.Default.Equals(
                candidate.Parameter,
                runStatus));
        return runStatus != null &&
               IsNamedStaticField(
                   runStatusArgument?.Value,
                   symbols[SharpProofSoundnessAnalyzer.KnownType.WorkerRunStatus],
                   "Canceled");
    }

    private static bool PublishesWorkerResponse(
        IMethodSymbol respondMethod,
        SyntaxNodeAnalysisContext context,
        IMethodSymbol mainMethod,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        var localSyntax = respondMethod.DeclaringSyntaxReferences
            .SingleOrDefault()?
            .GetSyntax(context.CancellationToken) as
            LocalFunctionStatementSyntax;
        if (localSyntax?.Body is not { Statements.Count: 2 } body ||
            body.Statements[0] is not
                ExpressionStatementSyntax { Expression: { } publishExpression } ||
            body.Statements[1] is not
                ReturnStatementSyntax { Expression: { } resultExpression } ||
            context.SemanticModel.GetConstantValue(
                resultExpression,
                context.CancellationToken) is not
                { HasValue: true, Value: 0 })
        {
            return false;
        }

        if (Unwrap(context.SemanticModel.GetOperation(
                publishExpression,
                context.CancellationToken)) is not IAwaitOperation awaited)
        {
            return false;
        }

        var publication = UnwrapConfigureAwait(awaited.Operation);

        if (publication is not IInvocationOperation write ||
            write.TargetMethod is not
            {
                Name: "WriteResponseAtomicAsync",
                MethodKind: MethodKind.Ordinary,
                IsStatic: true,
                Arity: 0,
                Parameters.Length: 2
            } writeMethod ||
            !IsSameType(
                writeMethod.ContainingType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.WorkerProgram]) ||
            writeMethod.Parameters[0].Type.SpecialType !=
                SpecialType.System_String ||
            !IsSameType(
                writeMethod.Parameters[1].Type,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.WorkerVerifyResponse]))
        {
            return false;
        }

        var path = write.Arguments.SingleOrDefault(candidate =>
            candidate.Parameter?.Ordinal == 0);
        var response = write.Arguments.SingleOrDefault(candidate =>
            candidate.Parameter?.Ordinal == 1);
        return Unwrap(path?.Value) is ILocalReferenceOperation
        {
            Local.Name: "resultPath"
        } resultPath &&
            SymbolEqualityComparer.Default.Equals(
                resultPath.Local.ContainingSymbol,
                mainMethod) &&
            ReferencesParameter(
                response?.Value,
                respondMethod.Parameters[0]);
    }

    private static bool ReifiesWorkerVerificationCancellation(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        if (SoleReturn(clause.Block)?.Expression is not
                ExpressionSyntax returnExpression ||
            Unwrap(context.SemanticModel.GetOperation(
                returnExpression,
                context.CancellationToken)) is not
                IInvocationOperation interrupted ||
            interrupted.TargetMethod is not
            {
                Name: "Interrupted",
                MethodKind: MethodKind.LocalFunction,
                Parameters.Length: 1
            } localFunction ||
            !localFunction.Parameters[0].HasExplicitDefaultValue ||
            localFunction.Parameters[0].ExplicitDefaultValue != null ||
            !SymbolEqualityComparer.Default.Equals(
                localFunction.ContainingSymbol,
                method) ||
            !IsSameType(
                localFunction.ReturnType,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.WorkerVerifyResponse]))
        {
            return false;
        }

        var localSyntax = localFunction.DeclaringSyntaxReferences
            .SingleOrDefault()?
            .GetSyntax(context.CancellationToken) as
            LocalFunctionStatementSyntax;
        if (localSyntax?.Body is not { Statements.Count: 2 } body ||
            body.Statements[0] is not
                LocalDeclarationStatementSyntax declaration ||
            body.Statements[1] is not
                ReturnStatementSyntax { Expression: { } resultExpression } ||
            declaration.Declaration.Variables.Count != 1)
        {
            return false;
        }

        var cancellationVariable = declaration.Declaration.Variables[0];
        if (cancellationVariable.Identifier.ValueText != "canceled" ||
            cancellationVariable.Initializer?.Value is not { } cancellationExpression ||
            context.SemanticModel.GetDeclaredSymbol(
                cancellationVariable,
                context.CancellationToken) is not ILocalSymbol canceled ||
            Unwrap(context.SemanticModel.GetOperation(
                cancellationExpression,
                context.CancellationToken)) is not
                IPropertyReferenceOperation cancellationRequested ||
            cancellationRequested.Property.Name != "IsCancellationRequested" ||
            !IsSameType(
                cancellationRequested.Property.ContainingType,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.CancellationToken]) ||
            !ReferencesParameter(
                cancellationRequested.Instance,
                method.Parameters[1]) ||
            !PreservesIncomingParameterValue(
                context,
                method,
                method.Parameters[1]) ||
            Unwrap(context.SemanticModel.GetOperation(
                resultExpression,
                context.CancellationToken)) is not
                IInvocationOperation create ||
            create.TargetMethod.Name != "CreateIncomplete" ||
            !IsSameType(
                create.TargetMethod.ContainingType,
                symbols[
                    SharpProofSoundnessAnalyzer.KnownType.WorkerResultAssembler]))
        {
            return false;
        }

        var projections = new Dictionary<string, CancellationProjection>(
            StringComparer.Ordinal);
        foreach (var argument in create.Arguments)
        {
            var parameterName = argument.Parameter?.Name;
            if (parameterName is not
                    ("status" or "callableReason" or "claimReason") ||
                projections.ContainsKey(parameterName))
            {
                continue;
            }

            var conditional = Unwrap(argument.Value) as IConditionalOperation;
            var condition = conditional == null
                ? null
                : Unwrap(conditional.Condition) as ILocalReferenceOperation;
            projections.Add(
                parameterName,
                new CancellationProjection(
                    condition != null &&
                    SymbolEqualityComparer.Default.Equals(condition.Local, canceled),
                    conditional?.WhenTrue,
                    conditional?.WhenFalse));
        }

        return IsCancellationProjection(
                   projections,
                   "status",
                   symbols[SharpProofSoundnessAnalyzer.KnownType.WorkerRunStatus],
                   "Canceled",
                   "TimedOut") &&
               IsCancellationProjection(
                   projections,
                   "callableReason",
                   symbols[
                       SharpProofSoundnessAnalyzer.KnownType.WorkerCallableCoverageReason],
                   "Canceled",
                   "ProjectTimeout") &&
               IsCancellationProjection(
                   projections,
                   "claimReason",
                   symbols[
                       SharpProofSoundnessAnalyzer.KnownType.WorkerClaimReason],
                   "Canceled",
                   "ProjectTimeout");
    }

    private static bool IsCancellationProjection(
        IReadOnlyDictionary<string, CancellationProjection> projections,
        string parameterName,
        INamedTypeSymbol? expectedType,
        string canceledName,
        string timeoutName)
    {
        return projections.TryGetValue(parameterName, out var projection) &&
               projection.ConditionMatches &&
               IsNamedStaticField(
                   projection.WhenTrue,
                   expectedType,
                   canceledName) &&
               IsNamedStaticField(
                   projection.WhenFalse,
                   expectedType,
                   timeoutName);
    }

    private readonly struct CancellationProjection
    {
        internal CancellationProjection(
            bool conditionMatches,
            IOperation? whenTrue,
            IOperation? whenFalse)
        {
            ConditionMatches = conditionMatches;
            WhenTrue = whenTrue;
            WhenFalse = whenFalse;
        }

        internal bool ConditionMatches { get; }
        internal IOperation? WhenTrue { get; }
        internal IOperation? WhenFalse { get; }
    }

    private static bool IsNamedStaticField(
        IOperation? operation,
        INamedTypeSymbol? expectedType,
        string name)
    {
        return Unwrap(operation) is IFieldReferenceOperation field &&
               field.Field is { IsStatic: true } &&
               string.Equals(field.Field.Name, name, StringComparison.Ordinal) &&
               IsSameType(field.Field.ContainingType, expectedType);
    }

    private static bool ReifiesCallerCancellation(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        if (clause.Block.Statements.FirstOrDefault() is not
                IfStatementSyntax { Else: null } cancellationIf ||
            context.SemanticModel.GetOperation(
                cancellationIf.Condition,
                context.CancellationToken) is not
                IPropertyReferenceOperation cancellationRequested ||
            cancellationRequested.Property.Name != "IsCancellationRequested" ||
            !IsSameType(
                cancellationRequested.Property.ContainingType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.CancellationToken]) ||
            !ReferencesParameter(
                cancellationRequested.Instance,
                method.Parameters[6]) ||
            !PreservesIncomingParameterValue(
                context,
                method,
                method.Parameters[6]) ||
            SoleReturn(cancellationIf.Statement)?.Expression is not
                ExpressionSyntax returnExpression ||
            context.SemanticModel.GetOperation(
                returnExpression,
                context.CancellationToken) is not IInvocationOperation invocation ||
            invocation.TargetMethod is not
            { Name: "Unknown", IsStatic: true, Parameters.Length: 3 } ||
            !IsSameType(
                invocation.TargetMethod.ContainingType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.CallableVerificationPolicy]) ||
            !IsSameType(
                invocation.TargetMethod.ReturnType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.CallableVerificationResult]))
        {
            return false;
        }

        var target = invocation.Arguments.FirstOrDefault(
            candidate => candidate.Parameter?.Ordinal == 0);
        return ReferencesParameter(target?.Value, method.Parameters[1]) &&
               IsCanceledReasonArgument(
                   invocation,
                   1,
                   symbols[SharpProofSoundnessAnalyzer.KnownType.WorkerClaimReason]) &&
               IsCanceledReasonArgument(
                   invocation,
                   2,
                   symbols[
                       SharpProofSoundnessAnalyzer.KnownType.WorkerCallableCoverageReason]);
    }

    private static ReturnStatementSyntax? SoleReturn(StatementSyntax statement)
    {
        return statement switch
        {
            ReturnStatementSyntax direct => direct,
            BlockSyntax { Statements.Count: 1 } block =>
                block.Statements[0] as ReturnStatementSyntax,
            _ => null
        };
    }

    private static bool IsCanceledReasonArgument(
        IInvocationOperation invocation,
        int parameterOrdinal,
        INamedTypeSymbol? expectedType)
    {
        if (expectedType == null ||
            !IsSameType(
                invocation.TargetMethod.Parameters[parameterOrdinal].Type,
                expectedType))
        {
            return false;
        }

        var argument = invocation.Arguments.FirstOrDefault(
            candidate => candidate.Parameter?.Ordinal == parameterOrdinal);
        IOperation? value = argument?.Value;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value is IFieldReferenceOperation field &&
               field.Field is { Name: "Canceled", IsStatic: true } &&
               IsSameType(field.Field.ContainingType, expectedType);
    }

    private static bool ReferencesParameter(
        IOperation? receiver,
        IParameterSymbol parameter)
    {
        receiver = Unwrap(receiver);

        return receiver is IParameterReferenceOperation reference &&
               SymbolEqualityComparer.Default.Equals(
                   reference.Parameter,
                   parameter);
    }

    private static bool PreservesIncomingParameterValue(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        IParameterSymbol parameter)
    {
        var declaration = method.DeclaringSyntaxReferences.SingleOrDefault();
        if (declaration == null ||
            declaration.SyntaxTree != context.Node.SyntaxTree ||
            context.SemanticModel.GetOperation(
                declaration.GetSyntax(context.CancellationToken),
                context.CancellationToken) is not { } root)
        {
            return false;
        }

        return !root.DescendantsAndSelf().Any(operation =>
            WritesParameter(operation, parameter));
    }

    private static bool WritesParameter(
        IOperation operation,
        IParameterSymbol parameter)
    {
        return operation switch
        {
            IAssignmentOperation assignment =>
                TargetsParameter(assignment.Target, parameter),
            IIncrementOrDecrementOperation increment =>
                ReferencesParameter(increment.Target, parameter),
            IArgumentOperation argument
                when argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out =>
                ReferencesParameter(argument.Value, parameter),
            IInvocationOperation invocation
                when HasWritableReducedReceiver(invocation) =>
                ReferencesParameter(invocation.Instance, parameter),
            IVariableDeclaratorOperation declarator
                when declarator.Symbol.RefKind != RefKind.None =>
                ReferencesParameter(declarator.Initializer?.Value, parameter),
            _ => false
        };
    }

    private static bool HasWritableReducedReceiver(
        IInvocationOperation invocation)
    {
        var reduced = invocation.TargetMethod.ReducedFrom;
        return reduced != null &&
               reduced.Parameters.Length > 0 &&
               reduced.Parameters[0].RefKind is RefKind.Ref or RefKind.Out;
    }

    private static bool TargetsParameter(
        IOperation operation,
        IParameterSymbol parameter)
    {
        operation = Unwrap(operation) ?? operation;
        if (ReferencesParameter(operation, parameter))
        {
            return true;
        }

        return operation is ITupleOperation tuple &&
               tuple.Elements.Any(element =>
                   TargetsParameter(element, parameter));
    }

    private static bool IsAuditedWorkerMain(
        IMethodSymbol method,
        INamedTypeSymbol? program,
        INamedTypeSymbol? taskOfInt32)
    {
        return method is
        { Name: "Main", IsStatic: true, Parameters.Length: 1 } &&
        IsSameType(method.ContainingType, program) &&
        SymbolEqualityComparer.Default.Equals(method.ReturnType, taskOfInt32) &&
        method.Parameters[0].Type is IArrayTypeSymbol { Rank: 1 } arguments &&
        arguments.ElementType.SpecialType == SpecialType.System_String;
    }

}
