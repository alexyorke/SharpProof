namespace SharpProof.CompilerArtifact;

internal static class CompilerTotalSpecTermInstantiator
{
    internal static IrTerm Instantiate(IrFactory factory, SpecTermDeclaration term, ImmutableArray<IrTerm> arguments, IrTypeId integerType, int depth)
    {
        if (depth >= 256)
        { throw new ArgumentException("A specification-pack term is too deep."); }
        IrTerm result = term switch
        {
            SpecVariableDeclaration { Role: SpecVariableRole.Parameter } parameter
                when parameter.Ordinal >= 0 && parameter.Ordinal < arguments.Length => arguments[parameter.Ordinal],
            SpecBooleanDeclaration value => factory.Boolean(value.Value),
            SpecIntegerDeclaration value when factory.GetTypeInfo(integerType).Width == 64 ||
                value.Value is >= int.MinValue and <= int.MaxValue => factory.Integer(integerType, value.Value),
            SpecUnaryDeclaration { Operator: IrUnaryOperator.Not } value =>
                factory.Unary(value.Operator, Instantiate(factory, value.Operand, arguments, integerType, depth + 1)),
            SpecBinaryDeclaration value when value.Operator is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual or
                IrBinaryOperator.LessThan or IrBinaryOperator.LessThanOrEqual or IrBinaryOperator.GreaterThan or
                IrBinaryOperator.GreaterThanOrEqual or IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse =>
                factory.Binary(value.Operator, Instantiate(factory, value.Left, arguments, integerType, depth + 1),
                    Instantiate(factory, value.Right, arguments, integerType, depth + 1)),
            SpecConditionalDeclaration value => factory.Conditional(
                Instantiate(factory, value.Condition, arguments, integerType, depth + 1),
                Instantiate(factory, value.WhenTrue, arguments, integerType, depth + 1),
                Instantiate(factory, value.WhenFalse, arguments, integerType, depth + 1)),
            _ => throw new ArgumentException("A specification-pack Total term is unsupported.")
        };
        var expectedType = term.Type switch
        {
            IrTypeKind.Boolean => factory.BooleanType,
            IrTypeKind.Integer => integerType,
            _ => default
        };
        if (result.Type != expectedType)
        { throw new ArgumentException("A specification-pack Total term has an invalid type."); }
        return result;
    }
}
