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
        if (clause.Declaration?.Type == null)
        {
            return;
        }

        var caughtType = context.SemanticModel
            .GetTypeInfo(clause.Declaration.Type, context.CancellationToken)
            .Type;
        if (!IsSameType(
                caughtType,
                symbols[SharpProofSoundnessAnalyzer.KnownType.OperationCanceledException]) ||
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
