namespace SharpProof.Specs;

public enum SpecInstantiationStatus
{
    Succeeded,
    Failed
}

public enum SpecInstantiationFailureKind
{
    MissingSubstitution,
    ForeignVariable,
    ForeignIrTerm,
    TypeMismatch,
    UnsupportedValueType,
    InvalidExpression
}

public sealed partial class SpecInstantiationResult
{
    internal static SpecInstantiationResult Succeeded(ImmutableArray<IrTerm> postconditions)
    {
        return new(SpecInstantiationStatus.Succeeded, postconditions, null);
    }

    internal static SpecInstantiationResult Failed(SpecInstantiationFailure failure)
    {
        return new(SpecInstantiationStatus.Failed, [], failure);
    }
}

public static partial class ApiSpecInstantiator
{
    public static SpecInstantiationResult InstantiatePostconditions(
        ApiSpecTemplate template, IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions)
    {
        template = ArgumentNullGuard.NotNull(template, nameof(template));
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        substitutions = ArgumentNullGuard.NotNull(
            substitutions, nameof(substitutions));

        var variables = template.Variables.ToImmutableDictionary(static item => item.Id);
        foreach (var substitution in substitutions)
        {
            if (!variables.TryGetValue(substitution.Key, out var variable))
            {
                return Failed(
                    SpecInstantiationFailureKind.ForeignVariable,
                    substitution.Key,
                    "The substitution variable does not belong to this template.");
            }

            if (substitution.Value == null || !BelongsToFactory(factory, substitution.Value))
            {
                return Failed(
                    SpecInstantiationFailureKind.ForeignIrTerm,
                    substitution.Key,
                    "The substitution term does not belong to the destination IR factory.");
            }

            if (!MatchesType(factory, substitution.Value.Type, variable.Type))
            {
                return Failed(
                    SpecInstantiationFailureKind.TypeMismatch,
                    substitution.Key,
                    "The substitution term type does not match the spec variable type.");
            }
        }
        var instantiation = new Instantiation(factory, substitutions,
            template.Variables.ToImmutableDictionary(
                static item => (item.Role, item.Ordinal)));
        var postconditions = ImmutableArray.CreateBuilder<IrTerm>(template.Postconditions.Length);
        foreach (var postcondition in template.Postconditions)
        {
            var result = instantiation.Term(postcondition.Condition);
            if (result.Failure != null)
            {
                return SpecInstantiationResult.Failed(result.Failure);
            }

            postconditions.Add(result.Term!);
        }
        return SpecInstantiationResult.Succeeded(postconditions.MoveToImmutable());
    }

    private static bool BelongsToFactory(IrFactory factory, IrTerm term)
    {
        try
        {
            return ReferenceEquals(factory.GetTerm(term.Id), term);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool MatchesType(IrFactory factory, IrTypeId type, IrTypeKind expected)
    {
        IrTypeInfo info;
        try
        {
            info = factory.GetTypeInfo(type);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return IsSupportedSpecType(expected) &&
               info.Kind == expected;
    }

    private static bool IsSupportedSpecType(IrTypeKind type)
    {
        return type is
            IrTypeKind.Boolean or
            IrTypeKind.Integer or
            IrTypeKind.String or
            IrTypeKind.Reference or
            IrTypeKind.Sequence;
    }

    private static SpecInstantiationResult Failed(
        SpecInstantiationFailureKind kind, SpecVarId variable, string detail)
    {
        return SpecInstantiationResult.Failed(new SpecInstantiationFailure(kind, variable, detail));
    }

    private sealed partial class Instantiation(
        IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables)
    {
        internal TermResult Term(SpecTermDeclaration term)
        {
            try
            {
                return term switch
                {
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
            catch (ArgumentException exception)
            {
                return Failure(
                    SpecInstantiationFailureKind.InvalidExpression, null, exception.Message);
            }
        }

        private TermResult Variable(SpecVariableDeclaration variable)
        {
            return variables.TryGetValue((variable.Role, variable.Ordinal), out var information) &&
            substitutions.TryGetValue(information.Id, out var replacement)
                ? new(replacement, null)
                : Failure(SpecInstantiationFailureKind.MissingSubstitution, information?.Id,
                    "No IR term was supplied for a referenced spec variable.");
        }

        private TermResult Null(SpecNullDeclaration value)
        {
            var type = value.Type switch
            {
                IrTypeKind.String => factory.StringType,
                IrTypeKind.Reference => factory.ObjectType,
                _ => default
            };
            return type.IsDefault
                ? Failure(SpecInstantiationFailureKind.UnsupportedValueType, null,
                    "A factory-independent sequence null needs a concrete sequence type substitution.")
                : new(factory.Null(type), null);
        }

        private TermResult Unary(SpecUnaryDeclaration unary)
        {
            return Child(
                unary.Operand,
                operand => factory.Unary(unary.Operator, operand));
        }

        private TermResult Binary(SpecBinaryDeclaration binary)
        {
            var isEquality = binary.Operator is
                IrBinaryOperator.Equal or IrBinaryOperator.NotEqual;
            TermResult left;
            TermResult right;
            if (isEquality &&
                binary.Left is SpecNullDeclaration leftNull &&
                binary.Right is not SpecNullDeclaration)
            {
                right = Term(binary.Right);
                if (right.Failure != null)
                {
                    return right;
                }

                left = Null(leftNull, right);
            }
            else
            {
                left = Term(binary.Left);
                right = default;
            }

            if (left.Failure != null)
            {
                return left;
            }

            if (right.Term == null)
            {
                right = isEquality &&
                        binary.Right is SpecNullDeclaration rightNull &&
                        binary.Left is not SpecNullDeclaration
                    ? Null(rightNull, left)
                    : Term(binary.Right);
            }
            if (right.Failure != null)
            {
                return right;
            }

            if (isEquality && left.Term!.Type != right.Term!.Type)
            {
                return Failure(
                    SpecInstantiationFailureKind.TypeMismatch,
                    null,
                    "The exact instantiated equality operand types do not match.");
            }

            return new(
                factory.Binary(binary.Operator, left.Term!, right.Term!),
                null);
        }

        private TermResult Null(SpecNullDeclaration value, TermResult peer)
        {
            if (peer.Failure != null)
            {
                return peer;
            }

            var peerType = factory.GetTypeInfo(peer.Term!.Type);
            if (value.Type == IrTypeKind.Sequence)
            {
                return Failure(SpecInstantiationFailureKind.UnsupportedValueType, null,
                    "A factory-independent sequence null needs a concrete sequence type substitution.");
            }

            return peerType.Kind == value.Type &&
                   value.Type is IrTypeKind.String or IrTypeKind.Reference
                ? new(factory.Null(peer.Term.Type), null)
                : Failure(SpecInstantiationFailureKind.TypeMismatch, null,
                    "The exact instantiated null operand type does not match its peer.");
        }

        private TermResult Conditional(SpecConditionalDeclaration conditional)
        {
            var condition = Term(conditional.Condition);
            if (condition.Failure != null)
            {
                return condition;
            }

            var whenTrue = Term(conditional.WhenTrue);
            if (whenTrue.Failure != null)
            {
                return whenTrue;
            }

            var whenFalse = Term(conditional.WhenFalse);
            return whenFalse.Failure != null
                ? whenFalse
                : new(factory.Conditional(
                    condition.Term!, whenTrue.Term!, whenFalse.Term!), null);
        }

        private TermResult Child(
            SpecTermDeclaration source, Func<IrTerm, IrTerm> create)
        {
            var child = Term(source);
            return child.Failure == null
                ? new(create(child.Term!), null)
                : child;
        }

        private static TermResult Failure(
            SpecInstantiationFailureKind kind, SpecVarId? variable, string detail)
        {
            return new(null, new SpecInstantiationFailure(kind, variable, detail));
        }
    }

}
