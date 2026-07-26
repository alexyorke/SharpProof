namespace SharpProof.Specs;

public sealed class ApiSpecTable {
    public const string DefaultTableIdentity = "SharpProof.ApiSpec.Default";
    public const string DefaultTableVersion = "1";

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
    }

    public static ApiSpecTable Default { get; } = Create(CreateDefaultDeclarations());

    public ImmutableArray<ApiSpecTemplate> Templates { get; }

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
        var postconditions = declaration.Postconditions.Select(postcondition => {
            if (postcondition == null)
                throw new ArgumentException("Postconditions cannot contain null.", nameof(declaration));
            ValidateEvidence(postcondition.Evidence, nameof(declaration));
            var condition = CompileTerm(postcondition.Condition, bySlot);
            if (condition.Type != SpecValueType.Boolean)
                throw new ArgumentException("Postconditions must be boolean.", nameof(declaration));
            return new SpecPostcondition(condition, postcondition.Evidence);
        }).ToImmutableArray();
        return new ApiSpecTemplate(
            id, declaration.Target, NormalizeFacets(declaration.Facets),
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

    private static SpecTerm CompileTerm(
        SpecTermDeclaration declaration,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables) {
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
                return new SpecVariableTerm(info.Id, info.Type);
            case SpecBooleanDeclaration boolean:
                return new SpecBooleanTerm(boolean.Value);
            case SpecIntegerDeclaration integer:
                return new SpecIntegerTerm(integer.Value);
            case SpecStringDeclaration text:
                if (text.Value == null)
                    throw new ArgumentException("String constants cannot be null.", nameof(declaration));
                return new SpecStringTerm(text.Value);
            case SpecNullDeclaration nullValue:
                if (nullValue.Type is not (SpecValueType.String or SpecValueType.Reference or SpecValueType.Sequence))
                    throw new ArgumentException("Null requires a nullable spec type.", nameof(declaration));
                return new SpecNullTerm(nullValue.Type);
            case SpecUnaryDeclaration unary:
                return CompileUnary(unary, variables);
            case SpecBinaryDeclaration binary:
                return CompileBinary(binary, variables);
            case SpecConditionalDeclaration conditional:
                return CompileConditional(conditional, variables);
            case SpecLengthDeclaration length:
                var value = CompileTerm(length.Value, variables);
                if (value.Type is not (SpecValueType.String or SpecValueType.Sequence))
                    throw new ArgumentException("Length requires a string or sequence.", nameof(declaration));
                return new SpecLengthTerm(value);
            default:
                throw new ArgumentException("Unsupported spec expression declaration.", nameof(declaration));
        }
    }

    private static SpecUnaryTerm CompileUnary(
        SpecUnaryDeclaration unary,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables) {
        var operand = CompileTerm(unary.Operand, variables);
        var expected = unary.Operator switch {
            SpecUnaryOperator.Not => SpecValueType.Boolean,
            SpecUnaryOperator.Negate => SpecValueType.Integer,
            _ => throw new ArgumentOutOfRangeException(nameof(unary))
        };
        if (operand.Type != expected || unary.Type != expected)
            throw new ArgumentException("Invalid unary spec expression types.", nameof(unary));
        return new SpecUnaryTerm(unary.Operator, operand, expected);
    }

    private static SpecBinaryTerm CompileBinary(
        SpecBinaryDeclaration binary,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables) {
        var left = CompileTerm(binary.Left, variables);
        var right = CompileTerm(binary.Right, variables);
        var resultType = binary.Operator switch {
            SpecBinaryOperator.Add or
                SpecBinaryOperator.Subtract or
                SpecBinaryOperator.Multiply or
                SpecBinaryOperator.Divide or
                SpecBinaryOperator.Remainder
                when left.Type == SpecValueType.Integer && right.Type == SpecValueType.Integer =>
                SpecValueType.Integer,
            SpecBinaryOperator.AndAlso or SpecBinaryOperator.OrElse
                when left.Type == SpecValueType.Boolean && right.Type == SpecValueType.Boolean =>
                SpecValueType.Boolean,
            SpecBinaryOperator.Equal or SpecBinaryOperator.NotEqual when left.Type == right.Type =>
                SpecValueType.Boolean,
            SpecBinaryOperator.LessThan or
                SpecBinaryOperator.LessThanOrEqual or
                SpecBinaryOperator.GreaterThan or
                SpecBinaryOperator.GreaterThanOrEqual
                when left.Type == SpecValueType.Integer && right.Type == SpecValueType.Integer =>
                SpecValueType.Boolean,
            SpecBinaryOperator.StringConcat
                when left.Type == SpecValueType.String && right.Type == SpecValueType.String =>
                SpecValueType.String,
            _ => throw new ArgumentException("Invalid binary spec expression types.", nameof(binary))
        };
        if (binary.Type != resultType)
            throw new ArgumentException("The binary spec result type is incorrect.", nameof(binary));
        return new SpecBinaryTerm(binary.Operator, left, right, resultType);
    }

    private static SpecConditionalTerm CompileConditional(
        SpecConditionalDeclaration conditional,
        IReadOnlyDictionary<(SpecVariableRole Role, int Ordinal), SpecVariableInfo> variables) {
        var condition = CompileTerm(conditional.Condition, variables);
        var whenTrue = CompileTerm(conditional.WhenTrue, variables);
        var whenFalse = CompileTerm(conditional.WhenFalse, variables);
        if (condition.Type != SpecValueType.Boolean ||
            whenTrue.Type != whenFalse.Type ||
            conditional.Type != whenTrue.Type)
            throw new ArgumentException("Invalid conditional spec expression types.", nameof(conditional));
        return new SpecConditionalTerm(condition, whenTrue, whenFalse, conditional.Type);
    }

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

    private static ImmutableArray<ApiSpecDeclaration> CreateDefaultDeclarations() {
        var documented = new SpecEvidence(SpecEvidenceKind.Documented, "dotnet-api-contract");
        var observed = new SpecEvidence(SpecEvidenceKind.Observed, "supported-runtime-observation");
        var typeInitialization = new SpecEvidence(
            SpecEvidenceKind.Documented, "dotnet-generic-cache-type-initialization-boundary");
        var contractSemantics = new SpecEvidence(
            SpecEvidenceKind.Documented, "sharpproof-compiler-bound-ghost-contract");
        return [
            Declaration(
                "bcl.array.empty", "M:System.Array.Empty``1",
                "System.Array", "Empty", null, [], SpecValueType.Sequence,
                Facets(
                    SpecEffect.Unknown, typeInitialization,
                    SpecAllocationBehavior.Unknown, observed,
                    SpecThrowBehavior.DoesNotThrow, [], documented,
                    SpecNullness.NonNull, documented,
                    SpecCardinality.Empty, documented),
                genericArity: 1),
            Declaration(
                "bcl.object.ctor", "M:System.Object.#ctor",
                "System.Object", ".ctor", SpecValueType.Reference, [], null,
                Facets(
                    SpecEffect.None, observed,
                    SpecAllocationBehavior.None, observed,
                    SpecThrowBehavior.DoesNotThrow, [], observed,
                    SpecNullness.NotApplicable, documented,
                    SpecCardinality.NotApplicable, documented),
                memberKind: SpecTargetMemberKind.Constructor),
            GhostContract(
                "contract.assume", "M:SharpProof.Attributes.Contract.Assume(System.Boolean)",
                "Assume", 0, [SpecValueType.Boolean], null, contractSemantics),
            GhostContract(
                "contract.ensures", "M:SharpProof.Attributes.Contract.Ensures(System.Boolean)",
                "Ensures", 0, [SpecValueType.Boolean], null, contractSemantics),
            GhostContract(
                "contract.old", "M:SharpProof.Attributes.Contract.Old``1(``0)",
                "Old", 1, [SpecValueType.Reference], SpecValueType.Reference, contractSemantics,
                throwsOnDirectInvocation: true),
            GhostContract(
                "contract.requires", "M:SharpProof.Attributes.Contract.Requires(System.Boolean)",
                "Requires", 0, [SpecValueType.Boolean], null, contractSemantics),
            GhostContract(
                "contract.result", "M:SharpProof.Attributes.Contract.Result``1",
                "Result", 1, [], SpecValueType.Reference, contractSemantics,
                throwsOnDirectInvocation: true),
            Declaration(
                "bcl.string.length", "P:System.String.Length",
                "System.String", "Length", SpecValueType.String, [], SpecValueType.Integer,
                Facets(
                    SpecEffect.ReadsReceiverState, documented,
                    SpecAllocationBehavior.None, observed,
                    SpecThrowBehavior.DoesNotThrow, [], observed,
                    SpecNullness.NotApplicable, documented,
                    SpecCardinality.NotApplicable, documented),
                [
                    Postcondition(
                        SpecBinaryOperator.Equal,
                        Variable(SpecVariableRole.Result, SpecValueType.Integer),
                        new SpecLengthDeclaration(
                            Variable(SpecVariableRole.Receiver, SpecValueType.String)),
                        documented)
                ],
                memberKind: SpecTargetMemberKind.PropertyGet),
            Declaration(
                "bcl.string.concat.string-string",
                "M:System.String.Concat(System.String,System.String)",
                "System.String", "Concat", null,
                [SpecValueType.String, SpecValueType.String], SpecValueType.String,
                Facets(
                    SpecEffect.None, observed,
                    SpecAllocationBehavior.MayAllocate, documented,
                    SpecThrowBehavior.DoesNotThrow, [], documented,
                    SpecNullness.NonNull, documented,
                    SpecCardinality.NotApplicable, documented)),
            Declaration(
                "bcl.list.add", "M:System.Collections.Generic.List`1.Add(`0)",
                "System.Collections.Generic.List`1", "Add", SpecValueType.Reference,
                [SpecValueType.Reference], null,
                Facets(
                    SpecEffect.WritesReceiverState, documented,
                    SpecAllocationBehavior.MayAllocate, observed,
                    SpecThrowBehavior.Unknown, [], documented,
                    SpecNullness.NotApplicable, documented,
                    SpecCardinality.NotApplicable, documented)),
            Declaration(
                "bcl.math.abs.int32", "M:System.Math.Abs(System.Int32)",
                "System.Math", "Abs", null, [SpecValueType.Integer], SpecValueType.Integer,
                Facets(
                    SpecEffect.None, observed,
                    SpecAllocationBehavior.None, observed,
                    SpecThrowBehavior.MayThrow, ["System.OverflowException"], documented,
                    SpecNullness.NotApplicable, documented,
                    SpecCardinality.NotApplicable, documented),
                [
                    Postcondition(
                        SpecBinaryOperator.GreaterThanOrEqual,
                        Variable(SpecVariableRole.Result, SpecValueType.Integer),
                        new SpecIntegerDeclaration(0),
                        documented)
                ]),
            Declaration(
                "bcl.enumerable.empty", "M:System.Linq.Enumerable.Empty``1",
                "System.Linq.Enumerable", "Empty", null, [], SpecValueType.Sequence,
                Facets(
                    SpecEffect.Unknown, typeInitialization,
                    SpecAllocationBehavior.Unknown, observed,
                    SpecThrowBehavior.DoesNotThrow, [], observed,
                    SpecNullness.NonNull, documented,
                    SpecCardinality.Empty, documented),
                genericArity: 1)
        ];
    }

    private static ApiSpecDeclaration Declaration(
        string witnessIdentifier, string documentationCommentId,
        string containingTypeMetadataName, string memberName,
        SpecValueType? receiverType, ImmutableArray<SpecValueType> parameterTypes,
        SpecValueType? resultType, ApiSpecFacets facets,
        ImmutableArray<SpecPostconditionDeclaration> postconditions = default,
        SpecTargetMemberKind memberKind = SpecTargetMemberKind.Method,
        int genericArity = 0) =>
        new(
            new ApiSpecTarget(
                witnessIdentifier, documentationCommentId, containingTypeMetadataName,
                memberKind, memberName, !receiverType.HasValue, genericArity,
                receiverType, parameterTypes, resultType),
            facets,
            postconditions.IsDefault ? [] : postconditions);

    private static SpecPostconditionDeclaration Postcondition(
        SpecBinaryOperator @operator, SpecTermDeclaration left,
        SpecTermDeclaration right, SpecEvidence evidence) =>
        new(
            new SpecBinaryDeclaration(
                @operator, left, right, SpecValueType.Boolean),
            evidence);

    private static SpecVariableDeclaration Variable(
        SpecVariableRole role, SpecValueType type) =>
        new(role, -1, type);

    private static ApiSpecDeclaration GhostContract(
        string witnessIdentifier, string documentationCommentId,
        string memberName, int genericArity,
        ImmutableArray<SpecValueType> parameterTypes, SpecValueType? resultType,
        SpecEvidence evidence,
        bool throwsOnDirectInvocation = false) =>
        Declaration(
            witnessIdentifier,
            documentationCommentId,
            "SharpProof.Attributes.Contract",
            memberName,
            null,
            parameterTypes,
            resultType,
            Facets(
                SpecEffect.None,
                throwsOnDirectInvocation
                    ? SpecAllocationBehavior.MayAllocate
                    : SpecAllocationBehavior.None,
                throwsOnDirectInvocation
                    ? SpecThrowBehavior.MayThrow
                    : SpecThrowBehavior.DoesNotThrow,
                throwsOnDirectInvocation
                    ? ["System.InvalidOperationException"]
                    : [],
                SpecNullness.NotApplicable,
                SpecCardinality.NotApplicable,
                evidence),
            genericArity: genericArity);

    private static ApiSpecFacets Facets(
        SpecEffect effects, SpecAllocationBehavior allocation,
        SpecThrowBehavior throws, ImmutableArray<string> exceptionNames,
        SpecNullness nullness, SpecCardinality cardinality, SpecEvidence evidence) =>
        Facets(
            effects, evidence,
            allocation, evidence,
            throws, exceptionNames, evidence,
            nullness, evidence,
            cardinality, evidence);

    private static ApiSpecFacets Facets(
        SpecEffect effects, SpecEvidence effectEvidence,
        SpecAllocationBehavior allocation, SpecEvidence allocationEvidence,
        SpecThrowBehavior throws, ImmutableArray<string> exceptionNames,
        SpecEvidence throwEvidence, SpecNullness nullness,
        SpecEvidence nullnessEvidence, SpecCardinality cardinality,
        SpecEvidence cardinalityEvidence) => new(
        new SpecEffectFacet(effects, effectEvidence),
        new SpecAllocationFacet(allocation, allocationEvidence),
        new SpecThrowFacet(throws, exceptionNames, throwEvidence),
        new SpecNullnessFacet(nullness, nullnessEvidence),
        new SpecCardinalityFacet(cardinality, null, cardinalityEvidence));
}
