namespace SharpProof.Ir;

internal interface IIrTermVisitor<TState, TResult>
{
    TResult Visit(IrBooleanTerm term, TState state);
    TResult Visit(IrIntegerTerm term, TState state);
    TResult Visit(IrStringTerm term, TState state);
    TResult Visit(IrNullTerm term, TState state);
    TResult Visit(IrEmptyArrayTerm term, TState state);
    TResult Visit(IrVariableTerm term, TState state);
    TResult Visit(IrOpaqueTerm term, TState state);
    TResult Visit(IrUnaryTerm term, TState state);
    TResult Visit(IrBinaryTerm term, TState state);
    TResult Visit(IrConditionalTerm term, TState state);
    TResult Visit(IrCastTerm term, TState state);
    TResult Visit(IrLengthTerm term, TState state);
    TResult Visit(IrSequenceAccessTerm term, TState state);
}
