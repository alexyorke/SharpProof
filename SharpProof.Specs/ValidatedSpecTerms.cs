namespace SharpProof.Specs;

// Consumers traverse only nodes produced after expression validation. The
// public declaration hierarchy stays extensible; unknown declarations are
// rejected by the validator before they can enter this closed representation.
internal interface IValidatedSpecTermVisitor<TResult>
{
    TResult Variable(SpecVariableDeclaration declaration);
    TResult Boolean(SpecBooleanDeclaration declaration);
    TResult Integer(SpecIntegerDeclaration declaration);
    TResult String(SpecStringDeclaration declaration);
    TResult Null(SpecNullDeclaration declaration);
    TResult Unary(SpecUnaryDeclaration declaration);
    TResult Binary(SpecBinaryDeclaration declaration);
    TResult Conditional(SpecConditionalDeclaration declaration);
    TResult Length(SpecLengthDeclaration declaration);
}

internal abstract class ValidatedSpecTerm
{
    private protected ValidatedSpecTerm()
    {
    }

    internal abstract TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor);
}

internal static partial class ApiSpecTermValidator
{
    // This method is private to the validator, and called only after
    // ValidateCore has accepted this declaration and every child. Its final
    // case is therefore the ninth known kind, not an unknown-kind fallback.
    private static ValidatedSpecTerm CompileValidatedTerm(SpecTermDeclaration declaration)
    {
        return declaration switch
        {
            SpecVariableDeclaration variable => new VariableTerm(variable),
            SpecBooleanDeclaration boolean => new BooleanTerm(boolean),
            SpecIntegerDeclaration integer => new IntegerTerm(integer),
            SpecStringDeclaration text => new StringTerm(text),
            SpecNullDeclaration nullValue => new NullTerm(nullValue),
            SpecUnaryDeclaration unary => new UnaryTerm(unary),
            SpecBinaryDeclaration binary => new BinaryTerm(binary),
            SpecConditionalDeclaration conditional => new ConditionalTerm(conditional),
            _ => new LengthTerm((SpecLengthDeclaration)declaration)
        };
    }

    private sealed class VariableTerm(SpecVariableDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Variable(declaration);
    }

    private sealed class BooleanTerm(SpecBooleanDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Boolean(declaration);
    }

    private sealed class IntegerTerm(SpecIntegerDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Integer(declaration);
    }

    private sealed class StringTerm(SpecStringDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.String(declaration);
    }

    private sealed class NullTerm(SpecNullDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Null(declaration);
    }

    private sealed class UnaryTerm(SpecUnaryDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Unary(declaration);
    }

    private sealed class BinaryTerm(SpecBinaryDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Binary(declaration);
    }

    private sealed class ConditionalTerm(SpecConditionalDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Conditional(declaration);
    }

    private sealed class LengthTerm(SpecLengthDeclaration declaration) : ValidatedSpecTerm
    {
        internal override TResult Accept<TResult>(IValidatedSpecTermVisitor<TResult> visitor)
            => visitor.Length(declaration);
    }
}
