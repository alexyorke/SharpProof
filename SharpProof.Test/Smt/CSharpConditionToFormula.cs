using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test.Smt;

internal static class CSharpConditionToFormula {
    public static bool TryTranslate(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out SmtFormula? formula) {
        var lowering = SymbolicSemanticPipeline.LowerCondition(expression, new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is { IsExact: true, Value: { } condition } &&
            SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded)) {
            formula = encoded;
            return true;
        }
        formula = null;
        return false;
    }
}
