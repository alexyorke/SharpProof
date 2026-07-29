namespace SharpProof.Specs;

public sealed partial class ApiSpecTable {
    private const SpecEffect DefinedEffects =
        SpecEffect.Unknown |
        SpecEffect.ReadsReceiverState |
        SpecEffect.ReadsArgumentState |
        SpecEffect.WritesReceiverState |
        SpecEffect.WritesArgumentState |
        SpecEffect.ReadsAmbientState |
        SpecEffect.WritesAmbientState |
        SpecEffect.InputOutput |
        SpecEffect.Synchronization |
        SpecEffect.NativeCode |
        SpecEffect.Reflection |
        SpecEffect.Nondeterminism;
    private static long s_nextScope;
    private readonly ImmutableDictionary<SpecId, ApiSpecTemplate> _byId;
    private readonly ImmutableDictionary<string, ApiSpecTemplate> _byWitness;
    private readonly long _scope;

    private ApiSpecTable(long scope, ImmutableArray<ApiSpecTemplate> templates) {
        (_scope, Templates) = (scope, templates);
        _byId = templates.ToImmutableDictionary(static template => template.Id);
        _byWitness = templates.ToImmutableDictionary(
            static template => template.Target.WitnessIdentifier,
            StringComparer.Ordinal);
        ContentSha256 = ApiSpecContentDigest.Compute(templates);
    }

    public static ApiSpecTable Default { get; } = Create(CreateDefaultDeclarations());

    public ImmutableArray<ApiSpecTemplate> Templates { get; }
    public string ContentSha256 { get; }

    public static ApiSpecTable Create(IEnumerable<ApiSpecDeclaration> declarations) {
        if (declarations == null) throw new ArgumentNullException(nameof(declarations));
        var ordered = declarations
            .Select(declaration => declaration ??
                                   throw new ArgumentException(
                                       "Spec declarations cannot contain null.",
                                       nameof(declarations)))
            .OrderBy(static declaration => declaration.Target?.WitnessIdentifier, StringComparer.Ordinal)
            .ToImmutableArray();
        if (ordered.IsDefaultOrEmpty)
            throw new ArgumentException("At least one spec declaration is required.", nameof(declarations));
        var duplicate = ordered
            .GroupBy(static declaration => declaration.Target?.WitnessIdentifier, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicate != null)
            throw new ArgumentException(
                "Spec witness identifiers must be unique: " + duplicate.Key + ".",
                nameof(declarations));
        var scope = Interlocked.Increment(ref s_nextScope);
        var templates = ImmutableArray.CreateBuilder<ApiSpecTemplate>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
            templates.Add(CompileTemplate(new SpecId(scope, index), ordered[index]));
        return new ApiSpecTable(scope, templates.MoveToImmutable());
    }

    public ApiSpecTemplate Get(SpecId id) {
        EnsureScope(id);
        if (!_byId.TryGetValue(id, out var template))
            throw new ArgumentOutOfRangeException(nameof(id));
        return template;
    }

    public bool TryGetByWitnessIdentifier(
        string witnessIdentifier,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ApiSpecTemplate? template) {
        if (witnessIdentifier == null) throw new ArgumentNullException(nameof(witnessIdentifier));
        return _byWitness.TryGetValue(witnessIdentifier, out template);
    }

    private static ApiSpecTemplate CompileTemplate(SpecId id, ApiSpecDeclaration declaration) {
        ValidateDeclaration(declaration);
        var variables = ImmutableArray.CreateBuilder<SpecVariableInfo>();
        var receiver = AddOptionalVariable(
            id, variables, SpecVariableRole.Receiver,
            declaration.Target.ReceiverType);
        var parameters = ImmutableArray.CreateBuilder<SpecVarId>(declaration.Target.ParameterTypes.Length);
        for (var ordinal = 0; ordinal < declaration.Target.ParameterTypes.Length; ordinal++)
            parameters.Add(AddVariable(
                id, variables, SpecVariableRole.Parameter, ordinal,
                declaration.Target.ParameterTypes[ordinal]));
        var result = AddOptionalVariable(
            id, variables, SpecVariableRole.Result,
            declaration.Target.ResultType);
        var variableArray = variables.ToImmutable();
        var bySlot = variableArray.ToImmutableDictionary(
            static variable => (variable.Role, variable.Ordinal));
        var facets = NormalizeFacets(declaration.Facets);
        var postconditions = declaration.Postconditions.Select(postcondition => {
            if (postcondition == null)
                throw new ArgumentException("Postconditions cannot contain null.", nameof(declaration));
            ValidateEvidence(postcondition.Evidence, nameof(declaration));
            var condition = ValidateTerm(postcondition.Condition, bySlot, facets);
            if (condition.Type != SpecValueType.Boolean)
                throw new ArgumentException("Postconditions must be boolean.", nameof(declaration));
            if (!condition.IsTotal)
                throw new ArgumentException(
                    "Trusted postconditions must be total under normal-return preconditions.",
                    nameof(declaration));
            return new SpecPostcondition(postcondition.Condition, postcondition.Evidence);
        }).ToImmutableArray();
        return new ApiSpecTemplate(
            id, declaration.Target, facets,
            variableArray, receiver, parameters.MoveToImmutable(), result,
            postconditions);
    }

    private static SpecVarId AddVariable(
        SpecId id, ImmutableArray<SpecVariableInfo>.Builder variables,
        SpecVariableRole role, int ordinal, SpecValueType type) {
        var variable = new SpecVarId(id, variables.Count);
        variables.Add(new SpecVariableInfo(variable, role, ordinal, type));
        return variable;
    }

    private static SpecVarId? AddOptionalVariable(
        SpecId id, ImmutableArray<SpecVariableInfo>.Builder variables,
        SpecVariableRole role, SpecValueType? type) =>
        type.HasValue
            ? AddVariable(id, variables, role, -1, type.Value)
            : null;

    private static TermFacts ValidateTerm(
        SpecTermDeclaration declaration,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets) {
        if (declaration == null)
            throw new ArgumentException("Spec expressions cannot contain null.", nameof(declaration));
        switch (declaration) {
            case SpecVariableDeclaration variable:
                if (!variables.TryGetValue((variable.Role, variable.Ordinal), out var info))
                    throw new ArgumentException(
                        "The spec expression references an unavailable variable slot.",
                        nameof(declaration));
                if (info.Type != variable.Type)
                    throw new ArgumentException(
                        "The spec variable declaration has the wrong type.",
                        nameof(declaration));
                var nonNull = info.Role == SpecVariableRole.Receiver ||
                    info.Role == SpecVariableRole.Result &&
                    (facets.Nullness.Result == SpecNullness.NonNull ||
                     facets.Cardinality.Result is SpecCardinality.Empty or
                         SpecCardinality.NonEmpty or SpecCardinality.Exact);
                return new(info.Type, true, nonNull, null);
            case SpecBooleanDeclaration boolean:
                return new(boolean.Type, true, false, null);
            case SpecIntegerDeclaration integer:
                return new(integer.Type, true, false, integer.Value);
            case SpecStringDeclaration text:
                if (text.Value == null)
                    throw new ArgumentException("String constants cannot be null.", nameof(declaration));
                return new(text.Type, true, true, null);
            case SpecNullDeclaration nullValue:
                if (nullValue.Type is not (SpecValueType.String or SpecValueType.Reference or SpecValueType.Sequence))
                    throw new ArgumentException("Null requires a nullable spec type.", nameof(declaration));
                return new(nullValue.Type, true, false, null);
            case SpecUnaryDeclaration unary:
                return ValidateUnary(unary, variables, facets);
            case SpecBinaryDeclaration binary:
                return ValidateBinary(binary, variables, facets);
            case SpecConditionalDeclaration conditional:
                return ValidateConditional(conditional, variables, facets);
            case SpecLengthDeclaration length:
                var value = ValidateTerm(length.Value, variables, facets);
                if (value.Type is not (SpecValueType.String or SpecValueType.Sequence))
                    throw new ArgumentException("Length requires a string or sequence.", nameof(declaration));
                return new(length.Type, value.IsTotal && value.IsNonNull, false, null);
            default:
                throw new ArgumentException("Unsupported spec expression declaration.", nameof(declaration));
        }
    }

    private static TermFacts ValidateUnary(
        SpecUnaryDeclaration unary,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets) {
        var operand = ValidateTerm(unary.Operand, variables, facets);
        var expected = unary.Operator switch {
            SpecUnaryOperator.Not => SpecValueType.Boolean,
            SpecUnaryOperator.Negate => SpecValueType.Integer,
            _ => throw new ArgumentOutOfRangeException(nameof(unary))
        };
        if (operand.Type != expected || unary.Type != expected)
            throw new ArgumentException("Invalid unary spec expression types.", nameof(unary));
        long? integer = null;
        if (unary.Operator == SpecUnaryOperator.Negate &&
            operand.Integer is { } value && TryNegate(value, out var negated))
            integer = negated;
        return new(expected,
            unary.Operator == SpecUnaryOperator.Not ? operand.IsTotal : integer.HasValue,
            false, integer);
    }

    private static TermFacts ValidateBinary(
        SpecBinaryDeclaration binary,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets) {
        var left = ValidateTerm(binary.Left, variables, facets);
        var right = ValidateTerm(binary.Right, variables, facets);
        var resultType = binary.Operator switch {
            SpecBinaryOperator.Add or
                SpecBinaryOperator.Subtract or
                SpecBinaryOperator.Multiply or
                SpecBinaryOperator.Divide or
                SpecBinaryOperator.Remainder
                when left.Type == SpecValueType.Integer &&
                     right.Type == SpecValueType.Integer =>
                SpecValueType.Integer,
            SpecBinaryOperator.AndAlso or SpecBinaryOperator.OrElse
                when left.Type == SpecValueType.Boolean &&
                     right.Type == SpecValueType.Boolean =>
                SpecValueType.Boolean,
            SpecBinaryOperator.Equal or SpecBinaryOperator.NotEqual
                when left.Type == right.Type =>
                SpecValueType.Boolean,
            SpecBinaryOperator.LessThan or
                SpecBinaryOperator.LessThanOrEqual or
                SpecBinaryOperator.GreaterThan or
                SpecBinaryOperator.GreaterThanOrEqual
                when left.Type == SpecValueType.Integer &&
                     right.Type == SpecValueType.Integer =>
                SpecValueType.Boolean,
            SpecBinaryOperator.StringConcat
                when left.Type == SpecValueType.String &&
                     right.Type == SpecValueType.String =>
                SpecValueType.String,
            _ => throw new ArgumentException("Invalid binary spec expression types.", nameof(binary))
        };
        if (binary.Type != resultType)
            throw new ArgumentException("The binary spec result type is incorrect.", nameof(binary));
        var arithmetic = binary.Operator is SpecBinaryOperator.Add or
            SpecBinaryOperator.Subtract or SpecBinaryOperator.Multiply or
            SpecBinaryOperator.Divide or SpecBinaryOperator.Remainder;
        long? integer = null;
        if (arithmetic && left.Integer is { } leftValue && right.Integer is { } rightValue &&
            TryArithmetic(binary.Operator, leftValue, rightValue, out var result))
            integer = result;
        return new(resultType, arithmetic ? integer.HasValue : left.IsTotal && right.IsTotal,
            binary.Operator == SpecBinaryOperator.StringConcat, integer);
    }

    private static TermFacts ValidateConditional(
        SpecConditionalDeclaration conditional,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables,
        ApiSpecFacets facets) {
        var condition = ValidateTerm(conditional.Condition, variables, facets);
        var whenTrue = ValidateTerm(conditional.WhenTrue, variables, facets);
        var whenFalse = ValidateTerm(conditional.WhenFalse, variables, facets);
        if (condition.Type != SpecValueType.Boolean ||
            whenTrue.Type != whenFalse.Type ||
            conditional.Type != whenTrue.Type)
            throw new ArgumentException("Invalid conditional spec expression types.", nameof(conditional));
        return new(conditional.Type, condition.IsTotal && whenTrue.IsTotal && whenFalse.IsTotal,
            whenTrue.IsNonNull && whenFalse.IsNonNull, null);
    }

    private static bool TryNegate(long value, out long result) {
        try {
            result = checked(-value);
            return true;
        }
        catch (OverflowException) {
            result = 0;
            return false;
        }
    }

    private static bool TryArithmetic(
        SpecBinaryOperator @operator, long left, long right, out long result) {
        try {
            result = @operator switch {
                SpecBinaryOperator.Add => checked(left + right),
                SpecBinaryOperator.Subtract => checked(left - right),
                SpecBinaryOperator.Multiply => checked(left * right),
                SpecBinaryOperator.Divide => left / right,
                SpecBinaryOperator.Remainder => left % right,
                _ => throw new ArgumentOutOfRangeException(nameof(@operator))
            };
            return true;
        }
        catch (ArithmeticException) {
            result = 0;
            return false;
        }
    }

    private readonly record struct TermFacts(
        SpecValueType Type, bool IsTotal, bool IsNonNull, long? Integer);

    private static void ValidateDeclaration(ApiSpecDeclaration declaration) {
        if (declaration.Target == null)
            throw new ArgumentException("A spec target is required.", nameof(declaration));
        var target = declaration.Target;
        ValidateText(target.WitnessIdentifier, nameof(target.WitnessIdentifier));
        ValidateText(target.DocumentationCommentId, nameof(target.DocumentationCommentId));
        ValidateText(target.ContainingTypeMetadataName, nameof(target.ContainingTypeMetadataName));
        ValidateText(target.MemberName, nameof(target.MemberName));
        ValidateDefined(target.MemberKind, nameof(target.MemberKind));
        if (target.GenericArity < 0)
            throw new ArgumentOutOfRangeException(nameof(declaration));
        if (target.ParameterTypes.IsDefault)
            throw new ArgumentException("Parameter types must be initialized.", nameof(declaration));
        if (target.ApprovedAssemblies.IsDefaultOrEmpty)
            throw new ArgumentException("At least one approved assembly identity is required.", nameof(declaration));
        foreach (var assembly in target.ApprovedAssemblies) {
            if (assembly == null || string.IsNullOrWhiteSpace(assembly.Name) ||
                assembly.PublicKeyToken == null ||
                assembly.PublicKeyToken.Length is not (0 or 16) ||
                assembly.PublicKeyToken.Any(static character => !Uri.IsHexDigit(character)) ||
                !Enum.IsDefined(
                    typeof(ApiSpecReferenceFamily),
                    assembly.ReferenceFamily))
                throw new ArgumentException("Approved assembly identities are invalid.", nameof(declaration));
        }
        if (target.ApprovedAssemblies.Distinct().Count() != target.ApprovedAssemblies.Length)
            throw new ArgumentException("Approved assembly identities must be unique.", nameof(declaration));
        foreach (var parameterType in target.ParameterTypes)
            ValidateDefined(parameterType, nameof(target.ParameterTypes));
        if (target.ReceiverType.HasValue)
            ValidateDefined(target.ReceiverType.Value, nameof(target.ReceiverType));
        if (target.ResultType.HasValue)
            ValidateDefined(target.ResultType.Value, nameof(target.ResultType));
        if (target.IsStatic && target.ReceiverType.HasValue ||
            !target.IsStatic && !target.ReceiverType.HasValue)
            throw new ArgumentException(
                "Receiver type presence must agree with static member shape.",
                nameof(declaration));
        if (declaration.Facets == null)
            throw new ArgumentException("Spec facets are required.", nameof(declaration));
        if (declaration.Postconditions.IsDefault)
            throw new ArgumentException("Postconditions must be initialized.", nameof(declaration));
    }

    private static ApiSpecFacets NormalizeFacets(ApiSpecFacets facets) {
        ValidateEvidence(facets.Effects?.Evidence, nameof(facets));
        ValidateEvidence(facets.Allocation?.Evidence, nameof(facets));
        ValidateEvidence(facets.Throws?.Evidence, nameof(facets));
        ValidateEvidence(facets.Nullness?.Evidence, nameof(facets));
        ValidateEvidence(facets.Cardinality?.Evidence, nameof(facets));
        var (effects, allocation, throws, nullness, cardinality) =
            (facets.Effects!, facets.Allocation!, facets.Throws!, facets.Nullness!, facets.Cardinality!);
        if ((effects.Effects & ~DefinedEffects) != 0)
            throw new ArgumentException("The effect facet contains undefined flags.", nameof(facets));
        if ((effects.Effects & SpecEffect.Unknown) != 0 &&
            effects.Effects != SpecEffect.Unknown)
            throw new ArgumentException("Unknown effects cannot be combined with known effects.", nameof(facets));
        ValidateDefined(allocation.Behavior, nameof(facets));
        ValidateDefined(throws.Behavior, nameof(facets));
        ValidateDefined(nullness.Result, nameof(facets));
        ValidateDefined(cardinality.Result, nameof(facets));
        if (throws.ExceptionMetadataNames.IsDefault)
            throw new ArgumentException("Throw exception names must be initialized.", nameof(facets));
        if (throws.ExceptionMetadataNames.Any(static name => string.IsNullOrWhiteSpace(name)))
            throw new ArgumentException("Throw exception names cannot be blank.", nameof(facets));
        if (throws.Behavior != SpecThrowBehavior.MayThrow &&
            !throws.ExceptionMetadataNames.IsDefaultOrEmpty)
            throw new ArgumentException("Only MayThrow facets can list exception types.", nameof(facets));
        if (cardinality.Result == SpecCardinality.Exact) {
            if (cardinality.ExactCount is < 0 or null)
                throw new ArgumentException("Exact cardinality requires a non-negative count.", nameof(facets));
        }
        else if (cardinality.ExactCount.HasValue) {
            throw new ArgumentException("Only exact cardinality can carry a count.", nameof(facets));
        }
        return facets;
    }

    private static void ValidateEvidence(SpecEvidence? evidence, string parameterName) {
        if (evidence == null || string.IsNullOrWhiteSpace(evidence.Source))
            throw new ArgumentException("Every facet and postcondition requires evidence.", parameterName);
        ValidateDefined(evidence.Kind, parameterName);
    }

    private static void ValidateText(string? value, string parameterName) {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A non-empty value is required.", parameterName);
    }

    private static void ValidateDefined<T>(T value, string parameterName) where T : struct, Enum {
        if (!Enum.IsDefined(typeof(T), value))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private void EnsureScope(SpecId id) {
        if (id.Scope != _scope)
            throw new ArgumentException("The spec identifier belongs to a different table.", nameof(id));
    }
}
