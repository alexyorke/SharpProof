using Microsoft.CodeAnalysis;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static SymbolicCondition CreateFactCondition(SymbolicAtom atom, SyntaxNode node, string provenance)
        {
            return new SymbolicFactCondition(SymbolicFact.Exact(atom, node, provenance));
        }

        private static SymbolicCondition CreateRelationCondition(
            SymbolicRelationOperator op,
            SymbolicTerm left,
            SymbolicTerm right,
            SyntaxNode node,
            string provenance)
        {
            return CreateFactCondition(new SymbolicRelationAtom(op, left, right), node, provenance);
        }

        private static SymbolicCondition CreateReferenceIsNullCondition(SymbolicTerm reference, SyntaxNode node)
        {
            return CreateFactCondition(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    reference,
                    new SymbolicNullTerm()),
                node,
                "ir.string.concat.null-empty");
        }

        public static SymbolicCondition CreateIntegerInRangeCondition(
            SymbolicTerm value,
            long minValue,
            long maxValue,
            SyntaxNode node,
            string provenance)
        {
            return new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateRelationCondition(
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    value,
                    new SymbolicIntegerConstantTerm(minValue),
                    node,
                    provenance + ".lower-bound"),
                CreateRelationCondition(
                    SymbolicRelationOperator.LessThanOrEqual,
                    value,
                    new SymbolicIntegerConstantTerm(maxValue),
                    node,
                    provenance + ".upper-bound"));
        }
    }
}
