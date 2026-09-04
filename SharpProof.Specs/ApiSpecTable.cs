namespace SharpProof.Specs;

public sealed partial class ApiSpecTable
{
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
    private readonly ImmutableDictionary<string, ApiSpecTemplate> _byWitness;
    private readonly long _scope;

    private ApiSpecTable(long scope, ImmutableArray<ApiSpecTemplate> templates)
    {
        (_scope, Templates) = (scope, templates);
        _byWitness = templates.ToImmutableDictionary(
            static template => template.Target.WitnessIdentifier,
            StringComparer.Ordinal);
        ContentSha256 = ApiSpecContentDigest.Compute(templates);
    }

    public static ApiSpecTable Default { get; } = Create(CreateDefaultDeclarations());

    public ImmutableArray<ApiSpecTemplate> Templates
    {
        get;
    }
    public string ContentSha256
    {
        get;
    }

    public static ApiSpecTable Create(IEnumerable<ApiSpecDeclaration> declarations)
    {
        declarations = ArgumentNullGuard.NotNull(declarations, nameof(declarations));

        var ordered = declarations
            .Select(declaration => declaration ??
                                   throw new ArgumentException(
                                       "Spec declarations cannot contain null.",
                                       nameof(declarations)))
            .OrderBy(static declaration => declaration.Target?.WitnessIdentifier, StringComparer.Ordinal)
            .ToImmutableArray();
        if (ordered.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one spec declaration is required.", nameof(declarations));
        }

        ApiSpecDeclaration? duplicate = null;
        for (var index = 1; index < ordered.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(
                    ordered[index - 1].Target?.WitnessIdentifier,
                    ordered[index].Target?.WitnessIdentifier))
            {
                duplicate = ordered[index];
                break;
            }
        }
        if (duplicate != null)
        {
            throw new ArgumentException(
                "Spec witness identifiers must be unique: " +
                duplicate.Target?.WitnessIdentifier + ".",
                nameof(declarations));
        }

        var scope = Interlocked.Increment(ref s_nextScope);
        var templates = ImmutableArray.CreateBuilder<ApiSpecTemplate>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            templates.Add(CompileTemplate(new SpecId(scope, index), ordered[index]));
        }

        return new ApiSpecTable(scope, templates.MoveToImmutable());
    }

    public ApiSpecTemplate Get(SpecId id)
    {
        EnsureScope(id);
        if ((uint)id.Value >= (uint)Templates.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        return Templates[id.Value];
    }

    public bool TryGetByWitnessIdentifier(
        string witnessIdentifier,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ApiSpecTemplate? template)
    {
        witnessIdentifier = ArgumentNullGuard.NotNull(
            witnessIdentifier, nameof(witnessIdentifier));

        return _byWitness.TryGetValue(witnessIdentifier, out template);
    }

    private static ApiSpecTemplate CompileTemplate(SpecId id, ApiSpecDeclaration declaration)
    {
        ValidateDeclaration(declaration);
        var variables = ImmutableArray.CreateBuilder<SpecVariableInfo>();
        var receiver = AddOptionalVariable(
            id, variables, SpecVariableRole.Receiver,
            declaration.Target.ReceiverType);
        var parameters = ImmutableArray.CreateBuilder<SpecVarId>(declaration.Target.ParameterTypes.Length);
        for (var ordinal = 0; ordinal < declaration.Target.ParameterTypes.Length; ordinal++)
        {
            parameters.Add(AddVariable(
                id, variables, SpecVariableRole.Parameter, ordinal,
                declaration.Target.ParameterTypes[ordinal]));
        }

        var result = AddOptionalVariable(
            id, variables, SpecVariableRole.Result,
            declaration.Target.ResultType);
        var variableArray = variables.ToImmutable();
        var bySlot = variableArray.ToImmutableDictionary(
            static variable => (variable.Role, variable.Ordinal));
        var facets = NormalizeFacets(
            declaration.Facets,
            declaration.Target);
        var postconditions = declaration.Postconditions.Select(postcondition =>
        {
            if (postcondition == null)
            {
                throw new ArgumentException("Postconditions cannot contain null.", nameof(declaration));
            }

            ValidateEvidence(postcondition.Evidence, nameof(declaration));
            var condition = ApiSpecTermValidator.Validate(
                postcondition.Condition,
                bySlot,
                facets);
            if (condition.Type != IrTypeKind.Boolean)
            {
                throw new ArgumentException("Postconditions must be boolean.", nameof(declaration));
            }

            if (!condition.IsTotal)
            {
                throw new ArgumentException(
                    "Trusted postconditions must be total under normal-return preconditions.",
                    nameof(declaration));
            }

            return new SpecPostcondition(postcondition.Condition, postcondition.Evidence);
        }).ToImmutableArray();
        return new ApiSpecTemplate(
            id, declaration.Target, facets,
            variableArray, receiver, parameters.MoveToImmutable(), result,
            postconditions);
    }

    private static SpecVarId AddVariable(
        SpecId id, ImmutableArray<SpecVariableInfo>.Builder variables,
        SpecVariableRole role, int ordinal, IrTypeKind type)
    {
        var variable = new SpecVarId(id, variables.Count);
        variables.Add(new SpecVariableInfo(variable, role, ordinal, type));
        return variable;
    }

    private static SpecVarId? AddOptionalVariable(
        SpecId id, ImmutableArray<SpecVariableInfo>.Builder variables,
        SpecVariableRole role, IrTypeKind? type)
    {
        return type.HasValue
            ? AddVariable(id, variables, role, -1, type.Value)
            : null;
    }

    private static void ValidateDeclaration(ApiSpecDeclaration declaration)
    {
        if (declaration.Target == null)
        {
            throw new ArgumentException("A spec target is required.", nameof(declaration));
        }

        var target = declaration.Target;
        ValidateText(target.WitnessIdentifier, nameof(target.WitnessIdentifier));
        ValidateText(target.DocumentationCommentId, nameof(target.DocumentationCommentId));
        ValidateText(target.ContainingTypeMetadataName, nameof(target.ContainingTypeMetadataName));
        ValidateText(target.MemberName, nameof(target.MemberName));
        ValidateDefined(target.MemberKind, nameof(target.MemberKind));
        _ = ArgumentNullGuard.RequireNonnegative(
            target.GenericArity, nameof(declaration));
        if (target.MemberKind == SpecTargetMemberKind.Constructor &&
            target.IsStatic)
        {
            throw new ArgumentException(
                "Spec constructors must be instance members.",
                nameof(declaration));
        }

        if (target.MemberKind == SpecTargetMemberKind.PropertyGet &&
            target.GenericArity != 0)
        {
            throw new ArgumentException(
                "Spec properties cannot declare generic arity.",
                nameof(declaration));
        }

        if (target.ParameterTypes.IsDefault)
        {
            throw new ArgumentException("Parameter types must be initialized.", nameof(declaration));
        }

        if (target.ApprovedAssemblies.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one approved assembly identity is required.", nameof(declaration));
        }

        foreach (var assembly in target.ApprovedAssemblies)
        {
            if (assembly == null || string.IsNullOrWhiteSpace(assembly.Name) ||
                assembly.PublicKeyToken == null ||
                assembly.PublicKeyToken.Length is not (0 or 16) ||
                assembly.PublicKeyToken.Any(static character => !Uri.IsHexDigit(character)) ||
                !Enum.IsDefined(
                    typeof(ApiSpecReferenceFamily),
                    assembly.ReferenceFamily))
            {
                throw new ArgumentException("Approved assembly identities are invalid.", nameof(declaration));
            }
        }
        if (target.ApprovedAssemblies
                .Select(static assembly =>
                    assembly.Name + "\u001f" +
                    assembly.PublicKeyToken.ToUpperInvariant() + "\u001f" +
                    (int)assembly.ReferenceFamily)
                .Distinct(StringComparer.Ordinal)
                .Count() != target.ApprovedAssemblies.Length)
        {
            throw new ArgumentException("Approved assembly identities must be unique.", nameof(declaration));
        }

        foreach (var parameterType in target.ParameterTypes)
        {
            ValidateSpecType(parameterType, nameof(target.ParameterTypes));
        }

        if (target.ReceiverType.HasValue)
        {
            ValidateSpecType(target.ReceiverType.Value, nameof(target.ReceiverType));
        }

        if (target.ResultType.HasValue)
        {
            ValidateSpecType(target.ResultType.Value, nameof(target.ResultType));
        }

        if (target.IsStatic && target.ReceiverType.HasValue ||
            !target.IsStatic && !target.ReceiverType.HasValue)
        {
            throw new ArgumentException(
                "Receiver type presence must agree with static member shape.",
                nameof(declaration));
        }

        if (declaration.Facets == null)
        {
            throw new ArgumentException("Spec facets are required.", nameof(declaration));
        }

        if (declaration.Postconditions.IsDefault)
        {
            throw new ArgumentException("Postconditions must be initialized.", nameof(declaration));
        }
    }

    private static ApiSpecFacets NormalizeFacets(
        ApiSpecFacets facets,
        ApiSpecTarget target)
    {
        ValidateEvidence(facets.Effects?.Evidence, nameof(facets));
        ValidateEvidence(facets.Allocation?.Evidence, nameof(facets));
        ValidateEvidence(facets.Throws?.Evidence, nameof(facets));
        ValidateEvidence(facets.Nullness?.Evidence, nameof(facets));
        ValidateEvidence(facets.Cardinality?.Evidence, nameof(facets));
        if (facets.Termination != null)
        {
            ValidateEvidence(facets.Termination.Evidence, nameof(facets));
            ValidateDefined(facets.Termination.Behavior, nameof(facets));
        }
        var (effects, allocation, throws, nullness, cardinality) =
            (facets.Effects!, facets.Allocation!, facets.Throws!, facets.Nullness!, facets.Cardinality!);
        if ((effects.Effects & ~DefinedEffects) != 0)
        {
            throw new ArgumentException("The effect facet contains undefined flags.", nameof(facets));
        }

        if ((effects.Effects & SpecEffect.Unknown) != 0 &&
            effects.Effects != SpecEffect.Unknown)
        {
            throw new ArgumentException("Unknown effects cannot be combined with known effects.", nameof(facets));
        }

        if (((effects.Effects & (
                 SpecEffect.ReadsReceiverState |
                 SpecEffect.WritesReceiverState)) != 0 &&
             target.IsStatic) ||
            ((effects.Effects & (
                 SpecEffect.ReadsArgumentState |
                 SpecEffect.WritesArgumentState)) != 0 &&
             target.ParameterTypes.IsDefaultOrEmpty))
        {
            throw new ArgumentException(
                "The effect facet does not apply to the declared target.",
                nameof(facets));
        }

        ValidateDefined(allocation.Behavior, nameof(facets));
        ValidateDefined(throws.Behavior, nameof(facets));
        ValidateDefined(nullness.Result, nameof(facets));
        ValidateDefined(cardinality.Result, nameof(facets));
        if (nullness.Result is not (
                SpecNullness.Unknown or
                SpecNullness.NotApplicable) &&
            (!target.ResultType.HasValue ||
             !IrOperatorCatalog.IsNullable(target.ResultType.Value)))
        {
            throw new ArgumentException(
                "The nullness facet does not apply to the declared result type.",
                nameof(facets));
        }

        if (cardinality.Result is not (
                SpecCardinality.Unknown or
                SpecCardinality.NotApplicable) &&
            target.ResultType != IrTypeKind.Sequence)
        {
            throw new ArgumentException(
                "The cardinality facet does not apply to the declared result type.",
                nameof(facets));
        }

        if (throws.ExceptionMetadataNames.IsDefault)
        {
            throw new ArgumentException("Throw exception names must be initialized.", nameof(facets));
        }

        if (throws.ExceptionMetadataNames.Any(static name => string.IsNullOrWhiteSpace(name)))
        {
            throw new ArgumentException("Throw exception names cannot be blank.", nameof(facets));
        }

        if (throws.Behavior != SpecThrowBehavior.MayThrow &&
            !throws.ExceptionMetadataNames.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Only MayThrow facets can list exception types.", nameof(facets));
        }

        if (cardinality.Result == SpecCardinality.Exact)
        {
            if (cardinality.ExactCount is < 0 or null)
            {
                throw new ArgumentException("Exact cardinality requires a non-negative count.", nameof(facets));
            }
        }
        else if (cardinality.ExactCount.HasValue)
        {
            throw new ArgumentException("Only exact cardinality can carry a count.", nameof(facets));
        }
        return facets;
    }

    private static void ValidateEvidence(SpecEvidence? evidence, string parameterName)
    {
        if (evidence == null || string.IsNullOrWhiteSpace(evidence.Source))
        {
            throw new ArgumentException("Every facet and postcondition requires evidence.", parameterName);
        }

        ValidateDefined(evidence.Kind, parameterName);
    }

    private static void ValidateText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
    }

    private static void ValidateDefined<T>(T value, string parameterName) where T : struct, Enum
    {
        _ = ArgumentNullGuard.RequireDefined(value, parameterName);
    }

    private static void ValidateSpecType(IrTypeKind value, string parameterName)
    {
        if (!IsSupportedSpecType(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    internal static bool IsSupportedSpecType(IrTypeKind value)
    {
        return value is
            IrTypeKind.Boolean or
            IrTypeKind.Integer or
            IrTypeKind.String or
            IrTypeKind.Reference or
            IrTypeKind.Sequence;
    }

    private void EnsureScope(SpecId id)
    {
        if (id.Scope != _scope)
        {
            throw new ArgumentException("The spec identifier belongs to a different table.", nameof(id));
        }
    }
}
