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

            if (previous.Filter != null)
            {
                continue;
            }

            if (previous.Declaration?.Type == null)
            {
                return true;
            }

            var previousType = context.SemanticModel.GetTypeInfo(
                previous.Declaration.Type, context.CancellationToken).Type;
            if (IsOrDerivesFrom(cancellationType, previousType))
            {
                return true;
            }
        }

        return false;
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

        if (clause.Declaration?.Identifier.ValueText is not { Length: > 0 } identifier ||
            filter is not IsPatternExpressionSyntax
            {
                Expression: IdentifierNameSyntax tested
            } ||
            tested.Identifier.ValueText != identifier ||
            context.SemanticModel.GetOperation(
                filter, context.CancellationToken) is not IIsPatternOperation
                patternTest)
        {
            return false;
        }

        return PatternExcludesCancellation(
            patternTest.Pattern, caughtType, cancellationType);
    }

    private static bool PatternExcludesCancellation(
        IPatternOperation pattern,
        ITypeSymbol? caughtType,
        INamedTypeSymbol? cancellationType)
    {
        switch (pattern)
        {
            case ITypePatternOperation typePattern:
                return !IsOrDerivesFrom(
                           cancellationType, typePattern.MatchedType) &&
                       !IsOrDerivesFrom(
                           typePattern.MatchedType, cancellationType);
            case INegatedPatternOperation
            {
                Pattern: ITypePatternOperation excludedPattern
            }:
                return IsOrDerivesFrom(
                           cancellationType, excludedPattern.MatchedType) ||
                       IsOrDerivesFrom(
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

    private static bool RethrowsCancellationImmediately(CatchClauseSyntax clause)
    {
        if (clause.Block.Statements.FirstOrDefault() is
            ThrowStatementSyntax { Expression: null })
        {
            return true;
        }

        if (clause.Block.Statements.LastOrDefault() is not
            ThrowStatementSyntax { Expression: null })
        {
            return false;
        }

        return !clause.Block.Statements
            .Take(clause.Block.Statements.Count - 1)
            .SelectMany(static statement => statement.DescendantNodesAndSelf())
            .Any(static syntax => syntax is
                ReturnStatementSyntax or
                GotoStatementSyntax or
                YieldStatementSyntax or
                BreakStatementSyntax or
                ContinueStatementSyntax or
                ThrowStatementSyntax);
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
                symbols.TaskOfInt32) ||
            IsAuditedWorkerMain(
                method,
                symbols[SharpProofSoundnessAnalyzer.KnownType.WorkerLauncherProgram],
                symbols.TaskOfInt32) ||
            SymbolEqualityComparer.Default.Equals(
                method,
                symbols.WorkerVerifyAsync))
        {
            return true;
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
