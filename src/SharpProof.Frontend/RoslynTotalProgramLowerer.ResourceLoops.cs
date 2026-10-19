using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private OperationId ResourceLoopSite(IOperation condition, OperationId fallback)
    {
        condition = ArgumentNullGuard.NotNull(condition, nameof(condition));
        RepositoryParameterGuard.ThrowIfInvalid(fallback.IsDefault, nameof(fallback));
        if (_calls?.CaptureResourceLoopEntries != true || condition.Language != LanguageNames.CSharp ||
            condition is not IBinaryOperation
            {
                OperatorMethod: null, LeftOperand: ILocalReferenceOperation,
                OperatorKind: BinaryOperatorKind.LessThan or BinaryOperatorKind.GreaterThan
            })
        { return fallback; }
        var syntax = condition.Syntax;
        while (syntax.Parent is ParenthesizedExpressionSyntax parent)
        { syntax = parent; }
        var sourceGuard = syntax.Parent is ForStatementSyntax increasing && increasing.Condition?.Span == syntax.Span ||
            syntax.Parent is WhileStatementSyntax decreasing && decreasing.Condition.Span == syntax.Span;
        if (!sourceGuard)
        { return fallback; }
        return _context.Factory.CreateOperation(IrSourceLoopEntry.GuardMarker, _context.Factory.GetOperationInfo(fallback).SourceSpan);
    }
}
