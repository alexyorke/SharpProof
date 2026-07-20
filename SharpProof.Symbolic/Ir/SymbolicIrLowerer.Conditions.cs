namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    internal static SymbolicCondition CreateFactCondition(SymbolicAtom atom, SyntaxNode node, string provenance) =>
        new SymbolicFactCondition(SymbolicFact.Exact(atom, node, provenance));

    internal static SymbolicCondition CreateRelationCondition(
        SymbolicRelationOperator op,
        SymbolicTerm left,
        SymbolicTerm right,
        SyntaxNode node,
        string provenance)
    {
        return CreateFactCondition(new SymbolicRelationAtom(op, left, right), node, provenance);
    }

    internal static SymbolicCondition CreateReferenceIsNullCondition(SymbolicTerm reference, SyntaxNode node)
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

    public static SymbolicCondition CreateReferenceNullCondition(
        SymbolicTerm value,
        bool equalToNull,
        SyntaxNode node,
        string provenance)
    {
        return CreateRelationCondition(
            equalToNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
            value,
            new SymbolicNullTerm(),
            node,
            provenance);
    }

    public static SymbolicCondition CreateIntegerZeroCondition(
        SymbolicTerm value,
        SyntaxNode node,
        string provenance)
    {
        return CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            value,
            new SymbolicIntegerConstantTerm(0),
            node,
            provenance);
    }

    public static SymbolicCondition CreateSignedDivisionOverflowCondition(
        SymbolicTerm left,
        SymbolicTerm right,
        long minValue,
        SyntaxNode node,
        string provenance)
    {
        return new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            CreateRelationCondition(
                SymbolicRelationOperator.Equal,
                left,
                new SymbolicIntegerConstantTerm(minValue),
                node,
                provenance + ".left-min"),
            CreateRelationCondition(
                SymbolicRelationOperator.Equal,
                right,
                new SymbolicIntegerConstantTerm(-1),
                node,
                provenance + ".right-minus-one"));
    }
}