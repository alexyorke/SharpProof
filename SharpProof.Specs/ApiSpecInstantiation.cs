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
        var variables = template.Variables.ToImmutableDictionary(static item => item.Id);
        foreach (var substitution in substitutions) {
            if (!variables.TryGetValue(substitution.Key, out var variable))
                return Failed(
                    SpecInstantiationFailureKind.ForeignVariable,
                    substitution.Key,
                    "The substitution variable does not belong to this template.");
            if (substitution.Value == null || !BelongsToFactory(factory, substitution.Value))
                return Failed(
                    SpecInstantiationFailureKind.ForeignIrTerm,
                    substitution.Key,
                    "The substitution term does not belong to the destination IR factory.");
            if (!MatchesType(factory, substitution.Value.Type, variable.Type))
                return Failed(
                    SpecInstantiationFailureKind.TypeMismatch,
                    substitution.Key,
                    "The substitution term type does not match the spec variable type.");
        }
        var instantiation = new Instantiation(factory, substitutions,
            template.Variables.ToImmutableDictionary(
                static item => (item.Role, item.Ordinal)));
        var postconditions = ImmutableArray.CreateBuilder<IrTerm>(template.Postconditions.Length);
        foreach (var postcondition in template.Postconditions) {
            var result = instantiation.Term(postcondition.Condition);
            if (result.Failure != null) return SpecInstantiationResult.Failed(result.Failure);
            postconditions.Add(result.Term!);
        }
        return SpecInstantiationResult.Succeeded(postconditions.MoveToImmutable());
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

    private static SpecInstantiationResult Failed(
        SpecInstantiationFailureKind kind, SpecVarId variable, string detail) =>
        SpecInstantiationResult.Failed(new SpecInstantiationFailure(kind, variable, detail));

    private sealed class Instantiation(
        IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables) {
        internal TermResult Term(SpecTermDeclaration term) {
            try {
                return term switch {
                    SpecVariableDeclaration variable => Variable(variable),
                    SpecBooleanDeclaration value => new(factory.Boolean(value.Value), null),
                    SpecIntegerDeclaration value => new(factory.Integer(value.Value), null),
                    SpecStringDeclaration value => new(factory.String(value.Value), null),
                    SpecNullDeclaration value => Null(value),
                    SpecUnaryDeclaration value => Unary(value),
                    SpecBinaryDeclaration value => Binary(value),
                    SpecConditionalDeclaration value => Conditional(value),
                    SpecLengthDeclaration value => Child(value.Value,
                        child => factory.Length(child)),
                    _ => Failure(SpecInstantiationFailureKind.InvalidExpression, null,
                        "Unknown spec term type.")
                };
            }
            catch (ArgumentException exception) {
                return Failure(
                    SpecInstantiationFailureKind.InvalidExpression, null, exception.Message);
            }
        }

        private TermResult Variable(SpecVariableDeclaration variable) =>
            variables.TryGetValue((variable.Role, variable.Ordinal), out var information) &&
            substitutions.TryGetValue(information.Id, out var replacement)
                ? new(replacement, null)
                : Failure(SpecInstantiationFailureKind.MissingSubstitution, information?.Id,
                    "No IR term was supplied for a referenced spec variable.");

        private TermResult Null(SpecNullDeclaration value) {
            var type = value.Type switch {
                SpecValueType.String => factory.StringType,
                SpecValueType.Reference => factory.ObjectType,
                _ => default
            };
            return type.IsDefault
                ? Failure(SpecInstantiationFailureKind.UnsupportedValueType, null,
                    "A factory-independent sequence null needs a concrete sequence type substitution.")
                : new(factory.Null(type), null);
        }

        private TermResult Unary(SpecUnaryDeclaration unary) =>
            Child(unary.Operand, operand => factory.Unary(unary.Operator switch {
                SpecUnaryOperator.Not => IrUnaryOperator.Not,
                SpecUnaryOperator.Negate => IrUnaryOperator.Negate,
                _ => throw new ArgumentOutOfRangeException(nameof(unary))
            }, operand));

        private TermResult Binary(SpecBinaryDeclaration binary) {
            var left = Term(binary.Left);
            if (left.Failure != null) return left;
            var right = Term(binary.Right);
            if (right.Failure != null) return right;
            var @operator = binary.Operator switch {
                SpecBinaryOperator.Add => IrBinaryOperator.Add,
                SpecBinaryOperator.Subtract => IrBinaryOperator.Subtract,
                SpecBinaryOperator.Multiply => IrBinaryOperator.Multiply,
                SpecBinaryOperator.Divide => IrBinaryOperator.Divide,
                SpecBinaryOperator.Remainder => IrBinaryOperator.Remainder,
                SpecBinaryOperator.AndAlso => IrBinaryOperator.AndAlso,
                SpecBinaryOperator.OrElse => IrBinaryOperator.OrElse,
                SpecBinaryOperator.Equal => IrBinaryOperator.Equal,
                SpecBinaryOperator.NotEqual => IrBinaryOperator.NotEqual,
                SpecBinaryOperator.LessThan => IrBinaryOperator.LessThan,
                SpecBinaryOperator.LessThanOrEqual => IrBinaryOperator.LessThanOrEqual,
                SpecBinaryOperator.GreaterThan => IrBinaryOperator.GreaterThan,
                SpecBinaryOperator.GreaterThanOrEqual => IrBinaryOperator.GreaterThanOrEqual,
                SpecBinaryOperator.StringConcat => IrBinaryOperator.StringConcat,
                _ => throw new ArgumentOutOfRangeException(nameof(binary))
            };
            return new(factory.Binary(@operator, left.Term!, right.Term!), null);
        }

        private TermResult Conditional(SpecConditionalDeclaration conditional) {
            var condition = Term(conditional.Condition);
            if (condition.Failure != null) return condition;
            var whenTrue = Term(conditional.WhenTrue);
            if (whenTrue.Failure != null) return whenTrue;
            var whenFalse = Term(conditional.WhenFalse);
            return whenFalse.Failure != null
                ? whenFalse
                : new(factory.Conditional(
                    condition.Term!, whenTrue.Term!, whenFalse.Term!), null);
        }

        private TermResult Child(
            SpecTermDeclaration source, Func<IrTerm, IrTerm> create) {
            var child = Term(source);
            return child.Failure == null
                ? new(create(child.Term!), null)
                : child;
        }

        private static TermResult Failure(
            SpecInstantiationFailureKind kind, SpecVarId? variable, string detail) =>
            new(null, new SpecInstantiationFailure(kind, variable, detail));
    }

    private readonly record struct TermResult(
        IrTerm? Term, SpecInstantiationFailure? Failure);
}
