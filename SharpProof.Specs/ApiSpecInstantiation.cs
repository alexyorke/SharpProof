namespace SharpProof.Specs;

public enum SpecInstantiationStatus {
    Succeeded,
    Failed
}

public enum SpecInstantiationFailureKind {
    MissingSubstitution,
    ForeignVariable,
    ForeignIrTerm,
    TypeMismatch,
    UnsupportedValueType,
    InvalidExpression
}

public sealed record SpecInstantiationFailure(
    SpecInstantiationFailureKind Kind, SpecVarId? Variable, string Detail);

public sealed class SpecInstantiationResult {
    private SpecInstantiationResult(
        SpecInstantiationStatus status, ImmutableArray<IrTerm> postconditions,
        SpecInstantiationFailure? failure) =>
        (Status, Postconditions, Failure) = (status, postconditions, failure);

    public SpecInstantiationStatus Status { get; }
    public ImmutableArray<IrTerm> Postconditions { get; }
    public SpecInstantiationFailure? Failure { get; }

    internal static SpecInstantiationResult Succeeded(ImmutableArray<IrTerm> postconditions) =>
        new(SpecInstantiationStatus.Succeeded, postconditions, null);

    internal static SpecInstantiationResult Failed(SpecInstantiationFailure failure) =>
        new(SpecInstantiationStatus.Failed, [], failure);
}

public static class ApiSpecInstantiator {
    public static SpecInstantiationResult InstantiatePostconditions(
        ApiSpecTemplate template, IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions) {
        if (template == null) throw new ArgumentNullException(nameof(template));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (substitutions == null) throw new ArgumentNullException(nameof(substitutions));
        var variables = template.Variables.ToImmutableDictionary(static variable => variable.Id);
        foreach (var substitution in substitutions) {
            if (!variables.TryGetValue(substitution.Key, out var variable))
                return Failure(
                    SpecInstantiationFailureKind.ForeignVariable,
                    substitution.Key,
                    "The substitution variable does not belong to this template.");
            if (substitution.Value == null || !BelongsToFactory(factory, substitution.Value))
                return Failure(
                    SpecInstantiationFailureKind.ForeignIrTerm,
                    substitution.Key,
                    "The substitution term does not belong to the destination IR factory.");
            if (!MatchesType(factory, substitution.Value.Type, variable.Type))
                return Failure(
                    SpecInstantiationFailureKind.TypeMismatch,
                    substitution.Key,
                    "The substitution term type does not match the spec variable type.");
        }
        var postconditions = ImmutableArray.CreateBuilder<IrTerm>(template.Postconditions.Length);
        foreach (var postcondition in template.Postconditions) {
            var result = InstantiateTerm(postcondition.Condition, factory, substitutions);
            if (result.Failure != null) return SpecInstantiationResult.Failed(result.Failure);
            postconditions.Add(result.Term!);
        }
        return SpecInstantiationResult.Succeeded(postconditions.MoveToImmutable());
    }

    private static (IrTerm? Term, SpecInstantiationFailure? Failure) InstantiateTerm(
        SpecTerm term, IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions) {
        try {
            switch (term) {
                case SpecVariableTerm variable:
                    return substitutions.TryGetValue(variable.Variable, out var replacement)
                        ? (replacement, null)
                        : (null, new SpecInstantiationFailure(
                            SpecInstantiationFailureKind.MissingSubstitution,
                            variable.Variable,
                            "No IR term was supplied for a referenced spec variable."));
                case SpecBooleanTerm boolean:
                    return (factory.Boolean(boolean.Value), null);
                case SpecIntegerTerm integer:
                    return (factory.Integer(integer.Value), null);
                case SpecStringTerm text:
                    return (factory.String(text.Value), null);
                case SpecNullTerm nullValue:
                    return InstantiateNull(nullValue, factory);
                case SpecUnaryTerm unary:
                    return InstantiateUnary(unary, factory, substitutions);
                case SpecBinaryTerm binary:
                    return InstantiateBinary(binary, factory, substitutions);
                case SpecConditionalTerm conditional:
                    return InstantiateConditional(conditional, factory, substitutions);
                case SpecLengthTerm length:
                    var value = InstantiateTerm(length.Value, factory, substitutions);
                    return value.Failure == null
                        ? (factory.Length(value.Term!), null)
                        : value;
                default:
                    return (null, new SpecInstantiationFailure(
                        SpecInstantiationFailureKind.InvalidExpression,
                        null,
                        "Unknown spec term type."));
            }
        }
        catch (ArgumentException exception) {
            return (null, new SpecInstantiationFailure(
                SpecInstantiationFailureKind.InvalidExpression,
                null,
                exception.Message));
        }
    }

    private static (IrTerm? Term, SpecInstantiationFailure? Failure) InstantiateNull(
        SpecNullTerm nullValue, IrFactory factory) {
        var type = nullValue.Type switch {
            SpecValueType.String => factory.StringType,
            SpecValueType.Reference => factory.ObjectType,
            _ => default
        };
        return type.IsDefault
            ? (null, new SpecInstantiationFailure(
                SpecInstantiationFailureKind.UnsupportedValueType,
                null,
                "A factory-independent sequence null needs a concrete sequence type substitution."))
            : (factory.Null(type), null);
    }

    private static (IrTerm? Term, SpecInstantiationFailure? Failure) InstantiateUnary(
        SpecUnaryTerm unary, IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions) {
        var operand = InstantiateTerm(unary.Operand, factory, substitutions);
        if (operand.Failure != null) return operand;
        if (!Enum.IsDefined(typeof(SpecUnaryOperator), unary.Operator))
            throw new ArgumentOutOfRangeException(nameof(unary));
        var @operator = (IrUnaryOperator)(int)unary.Operator;
        return (factory.Unary(@operator, operand.Term!), null);
    }

    private static (IrTerm? Term, SpecInstantiationFailure? Failure) InstantiateBinary(
        SpecBinaryTerm binary, IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions) {
        var left = InstantiateTerm(binary.Left, factory, substitutions);
        if (left.Failure != null) return left;
        var right = InstantiateTerm(binary.Right, factory, substitutions);
        if (right.Failure != null) return right;
        if (!Enum.IsDefined(typeof(SpecBinaryOperator), binary.Operator))
            throw new ArgumentOutOfRangeException(nameof(binary));
        var @operator = (IrBinaryOperator)(int)binary.Operator;
        return (factory.Binary(@operator, left.Term!, right.Term!), null);
    }

    private static (IrTerm? Term, SpecInstantiationFailure? Failure) InstantiateConditional(
        SpecConditionalTerm conditional, IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions) {
        var condition = InstantiateTerm(conditional.Condition, factory, substitutions);
        if (condition.Failure != null) return condition;
        var whenTrue = InstantiateTerm(conditional.WhenTrue, factory, substitutions);
        if (whenTrue.Failure != null) return whenTrue;
        var whenFalse = InstantiateTerm(conditional.WhenFalse, factory, substitutions);
        if (whenFalse.Failure != null) return whenFalse;
        return (factory.Conditional(condition.Term!, whenTrue.Term!, whenFalse.Term!), null);
    }

    private static bool BelongsToFactory(IrFactory factory, IrTerm term) {
        try {
            return ReferenceEquals(factory.GetTerm(term.Id), term);
        }
        catch (ArgumentException) {
            return false;
        }
    }

    private static bool MatchesType(IrFactory factory, IrTypeId type, SpecValueType expected) {
        IrTypeInfo info;
        try {
            info = factory.GetTypeInfo(type);
        }
        catch (ArgumentException) {
            return false;
        }
        return expected switch {
            SpecValueType.Boolean => type == factory.BooleanType,
            SpecValueType.Integer => type == factory.IntegerType,
            SpecValueType.String => type == factory.StringType,
            SpecValueType.Reference => info.Kind == IrTypeKind.Reference,
            SpecValueType.Sequence => info.Kind == IrTypeKind.Sequence,
            _ => false
        };
    }

    private static SpecInstantiationResult Failure(
        SpecInstantiationFailureKind kind, SpecVarId variable, string detail) =>
        SpecInstantiationResult.Failed(new SpecInstantiationFailure(kind, variable, detail));
}
