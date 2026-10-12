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
    internal static SpecInstantiationResult Succeeded(
        ImmutableArray<IrTerm> postconditions,
        IrTerm? normalCompletionCondition)
    {
        return new(
            SpecInstantiationStatus.Succeeded,
            postconditions,
            normalCompletionCondition,
            null);
    }

    internal static SpecInstantiationResult Failed(SpecInstantiationFailure failure)
    {
        return new(SpecInstantiationStatus.Failed, [], null, failure);
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
        substitutions = substitutions.ToImmutableDictionary();

        var variables = template.VariablesById;
        foreach (var substitution in substitutions)
        {
            if (!variables.TryGetValue(substitution.Key, out var variable))
            {
                return Failed(
                    SpecInstantiationFailureKind.ForeignVariable,
                    substitution.Key,
                    "The substitution variable does not belong to this template.");
            }

            var failure = ValidateSubstitution(factory, substitution.Value, variable.Type);
            if (failure == SpecInstantiationFailureKind.ForeignIrTerm)
            {
                return Failed(
                    SpecInstantiationFailureKind.ForeignIrTerm,
                    substitution.Key,
                    "The substitution term does not belong to the destination IR factory.");
            }

            if (failure == SpecInstantiationFailureKind.TypeMismatch)
            {
                return Failed(
                    SpecInstantiationFailureKind.TypeMismatch,
                    substitution.Key,
                    "The substitution term type does not match the spec variable type.");
            }
        }
        var instantiation = new Instantiation(factory, substitutions,
            template.VariablesBySlot, template.ValidatedTerms);
        IrTerm? normalCompletionCondition = null;
        if (template.Facets.Throws.NormalCompletion is { } declaredCompletion)
        {
            var completion = instantiation.Term(declaredCompletion);
            if (completion.Failure != null)
            {
                return SpecInstantiationResult.Failed(completion.Failure);
            }

            normalCompletionCondition = completion.Term;
        }

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
        return SpecInstantiationResult.Succeeded(
            postconditions.MoveToImmutable(),
            normalCompletionCondition);
    }

    private static SpecInstantiationFailureKind? ValidateSubstitution(
        IrFactory factory, IrTerm? term, IrTypeKind expected)
    {
        var lookupFailure = SpecInstantiationFailureKind.ForeignIrTerm;
        try
        {
            if (term == null ||
                !ReferenceEquals(factory.GetTerm(term.Id), term))
            {
                return SpecInstantiationFailureKind.ForeignIrTerm;
            }

            lookupFailure = SpecInstantiationFailureKind.TypeMismatch;
            var info = factory.GetTypeInfo(term.Type);
            return ApiSpecTable.IsSupportedSpecType(expected) && info.Kind == expected
                ? null
                : SpecInstantiationFailureKind.TypeMismatch;
        }
        catch (ArgumentException)
        {
            return lookupFailure;
        }
    }

    private static SpecInstantiationResult Failed(
        SpecInstantiationFailureKind kind, SpecVarId variable, string detail)
    {
        return SpecInstantiationResult.Failed(new SpecInstantiationFailure(kind, variable, detail));
    }

    private sealed partial class Instantiation(
        IrFactory factory,
        IReadOnlyDictionary<SpecVarId, IrTerm> substitutions,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        IReadOnlyDictionary<SpecTermDeclaration, ValidatedSpecTerm> validatedTerms)
        : IValidatedSpecTermVisitor<Instantiation.TermResult>
    {
        internal TermResult Term(SpecTermDeclaration term)
        {
            try
            {
                return validatedTerms[term].Accept(this);
            }
            catch (ArgumentException exception)
            {
                return Failure(
                    SpecInstantiationFailureKind.InvalidExpression, null, exception.Message);
            }
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Variable(SpecVariableDeclaration declaration)
        {
            return Variable(declaration);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Boolean(SpecBooleanDeclaration declaration)
        {
            return new(factory.Boolean(declaration.Value), null);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Integer(SpecIntegerDeclaration declaration)
        {
            return new(factory.Integer(declaration.Value), null);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.String(SpecStringDeclaration declaration)
        {
            return new(factory.String(declaration.Value), null);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Null(SpecNullDeclaration declaration)
        {
            return Null(declaration);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Unary(SpecUnaryDeclaration declaration)
        {
            return Unary(declaration);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Binary(SpecBinaryDeclaration declaration)
        {
            return Binary(declaration);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Conditional(SpecConditionalDeclaration declaration)
        {
            return Conditional(declaration);
        }

        TermResult IValidatedSpecTermVisitor<TermResult>.Length(SpecLengthDeclaration declaration)
        {
            return Child(declaration.Value, child => factory.Length(child));
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
            var failure = ResolvePair(
                binary.Left,
                binary.Right,
                isEquality,
                out var left,
                out var right);
            if (failure is { } result)
            {
                return result;
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

        private TermResult Null(SpecNullDeclaration value, IrTerm peer)
        {
            var peerType = factory.GetTypeInfo(peer.Type);
            return peerType.Kind == value.Type &&
                   value.Type is
                       IrTypeKind.String or
                       IrTypeKind.Reference or
                       IrTypeKind.Sequence
                ? new(factory.Null(peer.Type), null)
                : Failure(SpecInstantiationFailureKind.TypeMismatch, null,
                    "The exact instantiated null operand type does not match its peer.");
        }

        private static bool IsNullOnly(SpecTermDeclaration declaration)
        {
            return declaration is SpecNullDeclaration ||
                   declaration is SpecConditionalDeclaration conditional &&
                   IsNullOnly(conditional.WhenTrue) &&
                   IsNullOnly(conditional.WhenFalse);
        }

        private TermResult NullOnly(SpecTermDeclaration declaration, IrTerm peer)
        {
            if (declaration is SpecNullDeclaration literal)
            {
                return Null(literal, peer);
            }

            var conditional = (SpecConditionalDeclaration)declaration;
            var condition = Term(conditional.Condition);
            if (condition.Failure != null)
            {
                return condition;
            }

            var whenTrue = NullOnly(conditional.WhenTrue, peer);
            if (whenTrue.Failure != null)
            {
                return whenTrue;
            }

            var whenFalse = NullOnly(conditional.WhenFalse, peer);
            return whenFalse.Failure != null
                ? whenFalse
                : new(factory.Conditional(
                    condition.Term!, whenTrue.Term!, whenFalse.Term!), null);
        }

        private TermResult Conditional(SpecConditionalDeclaration conditional)
        {
            var condition = Term(conditional.Condition);
            if (condition.Failure != null)
            {
                return condition;
            }

            var failure = ResolvePair(
                conditional.WhenTrue,
                conditional.WhenFalse,
                inferNulls: true,
                out var whenTrue,
                out var whenFalse);
            return failure is { } result
                ? result
                : new(factory.Conditional(
                    condition.Term!, whenTrue.Term!, whenFalse.Term!), null);
        }

        private TermResult? ResolvePair(
            SpecTermDeclaration leftDeclaration,
            SpecTermDeclaration rightDeclaration,
            bool inferNulls,
            out TermResult left,
            out TermResult right)
        {
            if (inferNulls &&
                IsNullOnly(leftDeclaration) &&
                !IsNullOnly(rightDeclaration))
            {
                right = Term(rightDeclaration);
                if (right.Failure != null)
                {
                    left = default;
                    return right;
                }

                left = NullOnly(leftDeclaration, right.Term!);
            }
            else
            {
                left = Term(leftDeclaration);
                right = default;
            }

            if (left.Failure != null)
            {
                return left;
            }

            if (right.Term == null)
            {
                right = inferNulls &&
                        IsNullOnly(rightDeclaration) &&
                        !IsNullOnly(leftDeclaration)
                    ? NullOnly(rightDeclaration, left.Term!)
                    : Term(rightDeclaration);
            }

            return right.Failure != null ? right : null;
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
