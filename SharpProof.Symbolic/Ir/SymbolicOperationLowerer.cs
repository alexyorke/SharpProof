using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicOperationLowerer
{
    internal static SymbolicLoweringResult<SymbolicOperationSequence> Lower(
        IOperation operation,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext,
        int sequence = 0)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (targetContext == null) throw new ArgumentNullException(nameof(targetContext));
        if (valueContext == null) throw new ArgumentNullException(nameof(valueContext));

        targetContext.CancellationToken.ThrowIfCancellationRequested();
        return operation switch
        {
            IExpressionStatementOperation expressionStatement =>
                Lower(expressionStatement.Operation, targetContext, valueContext, sequence),
            IVariableDeclaratorOperation { Initializer.Value: { } value } declarator =>
                LowerSimpleAssignment(
                    declarator.Symbol,
                    value,
                    declarator.Syntax,
                    targetContext,
                    valueContext,
                    sequence,
                    "operation-lowering.declaration"),
            ISimpleAssignmentOperation assignment when TryGetDirectTargetSymbol(assignment.Target, out var target) =>
                LowerSimpleAssignment(
                    target,
                    assignment.Value,
                    assignment.Syntax,
                    targetContext,
                    valueContext,
                    sequence,
                    "operation-lowering.assignment"),
            _ => Unsupported(operation, "operation-lowering.unsupported")
        };
    }

    internal static SymbolicLoweringResult<SymbolicOperationSequence> LowerSimpleAssignment(
        ISymbol targetSymbol,
        IOperation valueOperation,
        SyntaxNode source,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext,
        int sequence,
        string provenance)
    {
        if (!TryCreateSymbolTerm(targetSymbol, targetContext, out var target) ||
            valueOperation.Syntax is not ExpressionSyntax valueExpression)
            return Unsupported(source, provenance + ".target");

        var value = target.Kind switch
        {
            SmtValueKind.Bool => SymbolicSemanticPipeline.LowerBooleanValueTerm(valueExpression, valueContext),
            SmtValueKind.Reference => SymbolicSemanticPipeline.LowerReferenceTerm(valueExpression, valueContext),
            _ => SymbolicSemanticPipeline.LowerTerm(valueExpression, valueContext)
        };
        if (value is not { IsExact: true, Value: { } sourceTerm } ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(target, sourceTerm))
            return Unsupported(source, provenance + ".value");

        var operation = new SymbolicAssignmentOperation(
            System.Collections.Immutable.ImmutableArray.Create(
                new SymbolicAssignmentBinding(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition),
                    target,
                    sourceTerm)),
            SymbolicAssignmentOperationKind.Simple,
            IsChecked: false,
            new SymbolicOperationOrigin(source.Span, sequence, provenance));
        return SymbolicLoweringResult<SymbolicOperationSequence>.Exact(
            SymbolicOperationSequence.Single(operation),
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
    }

    private static bool TryGetDirectTargetSymbol(IOperation target, out ISymbol symbol)
    {
        switch (target)
        {
            case ILocalReferenceOperation local:
                symbol = local.Local.OriginalDefinition;
                return true;
            case IParameterReferenceOperation parameter:
                symbol = parameter.Parameter.OriginalDefinition;
                return true;
            default:
                symbol = null!;
                return false;
        }
    }

    private static bool TryCreateSymbolTerm(
        ISymbol symbol,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);
        if (type == null ||
            !SymbolicFactFactory.TryGetValueKind(
                type,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsReferenceType,
                out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
        return true;
    }

    private static SymbolicLoweringResult<SymbolicOperationSequence> Unsupported(
        IOperation operation,
        string provenance)
    {
        return Unsupported(operation.Syntax, provenance);
    }

    private static SymbolicLoweringResult<SymbolicOperationSequence> Unsupported(
        SyntaxNode source,
        string provenance)
    {
        return SymbolicLoweringResult<SymbolicOperationSequence>.Unsupported(
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
    }
}
