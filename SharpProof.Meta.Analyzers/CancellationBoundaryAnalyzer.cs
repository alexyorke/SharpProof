using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
        var filter = clause.Filter?.FilterExpression;
        if (filter == null)
        {
            return true;
        }
        if (context.SemanticModel.GetConstantValue(
                filter, context.CancellationToken) is
            { HasValue: true, Value: true })
        {
            return true;
        }
        if (clause.Declaration == null ||
            context.SemanticModel.GetDeclaredSymbol(
                clause.Declaration,
                context.CancellationToken) is not ILocalSymbol caughtLocal)
        {
            return false;
        }
        var operation = Unwrap(context.SemanticModel.GetOperation(
            filter, context.CancellationToken));
        if (operation is IIsTypeOperation typeTest &&
            Unwrap(typeTest.ValueOperand) is ILocalReferenceOperation typeTested)
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    typeTested.Local, caughtLocal))
            {
                return false;
            }
            return IsOrDerivesFrom(cancellationType, typeTest.TypeOperand);
        }
        if (operation is not IIsPatternOperation patternTest ||
            Unwrap(patternTest.Value) is not ILocalReferenceOperation tested ||
            !SymbolEqualityComparer.Default.Equals(tested.Local, caughtLocal))
        {
            return false;
        }
        return PatternIncludesAllCancellation(
            patternTest.Pattern, caughtType, cancellationType);
    }

    private static bool PatternIncludesAllCancellation(
        IPatternOperation pattern,
        ITypeSymbol? caughtType,
        INamedTypeSymbol cancellationType)
    {
        return pattern switch
        {
            ITypePatternOperation typePattern =>
                IsOrDerivesFrom(cancellationType, typePattern.MatchedType),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Or =>
                PatternIncludesAllCancellation(
                    binary.LeftPattern, caughtType, cancellationType) ||
                PatternIncludesAllCancellation(
                    binary.RightPattern, caughtType, cancellationType),
            IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.And =>
                PatternIncludesAllCancellation(
                    binary.LeftPattern, caughtType, cancellationType) &&
                PatternIncludesAllCancellation(
                    binary.RightPattern, caughtType, cancellationType),
            _ => false
        };
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
        if (clause.Filter?.FilterExpression is not { } filter)
        {
            return false;
        }

        var constant = context.SemanticModel.GetConstantValue(
            filter, context.CancellationToken);
        if (constant is { HasValue: true, Value: false })
        {
            return true;
        }

        ExpressionSyntax patternExpression = filter;
        while (patternExpression is ParenthesizedExpressionSyntax parenthesized)
        {
            patternExpression = parenthesized.Expression;
        }
        if (clause.Declaration == null ||
            patternExpression is not IsPatternExpressionSyntax ||
            context.SemanticModel.GetDeclaredSymbol(
                clause.Declaration,
                context.CancellationToken) is not ILocalSymbol caughtLocal ||
            Unwrap(context.SemanticModel.GetOperation(
                patternExpression, context.CancellationToken)) is not
                IIsPatternOperation patternTest ||
            Unwrap(patternTest.Value) is not ILocalReferenceOperation tested ||
            !SymbolEqualityComparer.Default.Equals(
                tested.Local, caughtLocal))
        {
            return false;
        }

        return PatternExcludesCancellation(
            patternTest.Pattern, caughtType, cancellationType);
    }

    private static IOperation? Unwrap(IOperation? operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static bool PatternExcludesCancellation(
        IPatternOperation pattern,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType)
    {
        switch (pattern)
        {
            case ITypePatternOperation typePattern:
                return typePattern.MatchedType.TypeKind == TypeKind.Class &&
                       !IsOrDerivesFrom(
                           cancellationType, typePattern.MatchedType) &&
                       !IsOrDerivesFrom(
                           typePattern.MatchedType, cancellationType);
            case INegatedPatternOperation
            {
                Pattern: ITypePatternOperation excludedPattern
            }:
                return IsOrDerivesFrom(
                           cancellationType, excludedPattern.MatchedType) ||
                       IsAssignableTo(
                           caughtType, excludedPattern.MatchedType);
            case IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Or:
                return PatternExcludesCancellation(
                           binary.LeftPattern, caughtType, cancellationType) &&
                       PatternExcludesCancellation(
                           binary.RightPattern, caughtType, cancellationType);
            case IBinaryPatternOperation binary
                when binary.OperatorKind == BinaryOperatorKind.And:
                return PatternExcludesCancellation(
                           binary.LeftPattern, caughtType, cancellationType) ||
                       PatternExcludesCancellation(
                           binary.RightPattern, caughtType, cancellationType);
            default:
                return false;
        }
    }

    private static bool IsOrDerivesFrom(
        ITypeSymbol? type,
        ITypeSymbol? possibleBase)
    {
        if (possibleBase == null)
        {
            return false;
        }

        for (var current = type as INamedTypeSymbol;
             current != null;
             current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    possibleBase.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
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
        return clause.Block.Statements.FirstOrDefault() is
            ThrowStatementSyntax { Expression: null };
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

        // MSBuild task implementations translate cancellation into the task
        // protocol (a Boolean result or a classified build failure). These
        // catches are not semantic proof answers, so they are audited at the
        // build-protocol boundary rather than reported as swallowed
        // cancellation.
        if (method.ContainingType?.AllInterfaces.Any(interfaceType =>
                IsSameType(
                    interfaceType,
                    symbols[SharpProofSoundnessAnalyzer.KnownType.MsBuildCancelableTask])) == true)
        {
            return true;
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

        return ThrowsIfCallerCancellationRequested(
                   clause,
                   context,
                   method,
                   symbols) ||
               ReifiesCallerCancellation(clause, context, method, symbols);
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

        var responseOperation = Unwrap(awaited.Operation);
        if (responseOperation is IInvocationOperation configureAwait &&
            configureAwait.TargetMethod is
            {
                Name: "ConfigureAwait",
                IsStatic: false,
                Parameters.Length: 1
            } &&
            SymbolEqualityComparer.Default.Equals(
                configureAwait.TargetMethod.ContainingType,
                symbols.TaskOfInt32) &&
            configureAwait.TargetMethod.Parameters[0].Type.SpecialType ==
                SpecialType.System_Boolean)
        {
            responseOperation = Unwrap(configureAwait.Instance);
        }

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

        var cancellationOperation = Unwrap(context.SemanticModel.GetOperation(
            cancellationExpression,
            context.CancellationToken));
        var directCancellation = cancellationOperation is
            IPropertyReferenceOperation cancellationRequested &&
            cancellationRequested.Property.Name == "IsCancellationRequested" &&
            IsSameType(
                cancellationRequested.Property.ContainingType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.CancellationToken]) &&
            ReferencesParameter(cancellationRequested.Instance, method.Parameters[1]);
        var latchedCancellation = cancellationOperation is IInvocationOperation helper &&
            helper.TargetMethod is
            {
                Name: "CallerCancellationWon",
                MethodKind: MethodKind.LocalFunction,
                Parameters.Length: 0,
                ReturnType.SpecialType: SpecialType.System_Boolean
            } &&
            SymbolEqualityComparer.Default.Equals(
                helper.TargetMethod.ContainingSymbol,
                method);
        if (!directCancellation && !latchedCancellation)
        {
            return false;
        }

        return IsCancellationProjection(
                   create,
                   "status",
                   canceled,
                   symbols[SharpProofSoundnessAnalyzer.KnownType.WorkerRunStatus],
                   "Canceled",
                   "TimedOut") &&
               IsCancellationProjection(
                   create,
                   "callableReason",
                   canceled,
                   symbols[
                       SharpProofSoundnessAnalyzer.KnownType.WorkerCallableCoverageReason],
                   "Canceled",
                   "ProjectTimeout") &&
               IsCancellationProjection(
                   create,
                   "claimReason",
                   canceled,
                   symbols[
                       SharpProofSoundnessAnalyzer.KnownType.WorkerClaimReason],
                   "Canceled",
                   "ProjectTimeout");
    }

    private static bool IsCancellationProjection(
        IInvocationOperation invocation,
        string parameterName,
        ILocalSymbol canceled,
        INamedTypeSymbol? expectedType,
        string canceledName,
        string timeoutName)
    {
        var argument = invocation.Arguments.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Parameter?.Name,
                parameterName,
                StringComparison.Ordinal));
        if (Unwrap(argument?.Value) is not IConditionalOperation conditional ||
            Unwrap(conditional.Condition) is not
                ILocalReferenceOperation condition ||
            !SymbolEqualityComparer.Default.Equals(
                condition.Local,
                canceled))
        {
            return false;
        }

        return IsNamedStaticField(
                   conditional.WhenTrue,
                   expectedType,
                   canceledName) &&
               IsNamedStaticField(
                   conditional.WhenFalse,
                   expectedType,
                   timeoutName);
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

    private static bool ThrowsIfCallerCancellationRequested(
        CatchClauseSyntax clause,
        SyntaxNodeAnalysisContext context,
        IMethodSymbol method,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        if (clause.Block.Statements.FirstOrDefault() is not
                ExpressionStatementSyntax expression ||
            context.SemanticModel.GetOperation(
                expression.Expression,
                context.CancellationToken) is not IInvocationOperation invocation ||
            invocation.TargetMethod.Name != "ThrowIfCancellationRequested" ||
            !IsSameType(
                invocation.TargetMethod.ContainingType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.CancellationToken]))
        {
            return false;
        }

        return ReferencesParameter(invocation.Instance, method.Parameters[6]);
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
        while (receiver is IConversionOperation conversion)
        {
            receiver = conversion.Operand;
        }

        return receiver is IParameterReferenceOperation reference &&
               SymbolEqualityComparer.Default.Equals(
                   reference.Parameter,
                   parameter);
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

    private static bool IsSameType(
        ITypeSymbol? actual,
        INamedTypeSymbol? expected)
    {
        return actual != null &&
        expected != null &&
        SymbolEqualityComparer.Default.Equals(
            actual.OriginalDefinition,
            expected.OriginalDefinition);
    }
}
