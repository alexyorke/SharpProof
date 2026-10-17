namespace SharpProof.CompilerArtifact;

// Binds audited method evidence first to compiler symbols, then to Total IR values.
internal sealed class CompilerSpecificationMethodBinding(
    IrFactory factory, CompilerSpecificationMethodEvidence definition,
    ImmutableArray<IrTypeId> parameters, IrTypeId integerType)
{
    private readonly IrFactory _factory = factory ?? throw new System.ArgumentNullException(nameof(factory));
    private readonly CompilerSpecificationMethodEvidence _definition = definition ?? throw new System.ArgumentNullException(nameof(definition));
    private readonly ImmutableArray<IrTypeId> _parameters = parameters.IsDefault ? throw new System.ArgumentException("Value must be initialized.", nameof(parameters)) : parameters;
    private readonly IrTypeId _integerType = integerType.IsDefault ? throw new System.ArgumentException("Value must be initialized.", nameof(integerType)) : integerType;

    internal static CompilerSpecificationMethodEvidence? Admit(IMethodSymbol method, CompilerSpecificationMethodEvidence? definition)
    {
        if (definition == null ||
            method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic ||
            method.TypeParameters.Length != 0 ||
            method.Parameters.Any(static parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind != RefKind.None)) ||
            method.Parameters.Length != definition.ParameterTypes.Length ||
            !MatchesAssembly(method.ContainingAssembly, definition.Assemblies) ||
            !CompilerSpecificationScalarDomain.MatchesType(method.ReturnType, definition.ResultType))
        { return null; }
        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (!CompilerSpecificationScalarDomain.MatchesType(
                    method.Parameters[index].Type, definition.ParameterTypes[index]))
            { return null; }
        }
        return definition;
    }

    internal static TotalScalarCallModel? CreateTotal(IrFactory factory, IMethodSymbol method,
        CompilerSpecificationMethodEvidence definition)
    {
        if (definition is null)
        {
            throw new System.ArgumentNullException(nameof(definition));
        }
        if (!CompilerSpecificationScalarDomain.TryMapSignature(
                factory, method, out var parameters, out _, out var integerType))
        { return null; }
        var binding = new CompilerSpecificationMethodBinding(factory, definition, parameters, integerType);
        return new(parameters.Length, binding.Apply);
    }

    private TotalScalarRule Apply(ImmutableArray<IrTerm> arguments)
    {
        try
        {
            if (arguments.Length != _parameters.Length || arguments.Where((argument, ordinal) => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.Type != _parameters[ordinal])).Any())
            { throw new ArgumentException("A specification-pack argument has an invalid type."); }
            var result = CompilerTotalSpecTermInstantiator.Instantiate(_factory, _definition.Result, arguments, _integerType, 0);
            return new TotalScalarRule(result, [], FrontendSubsetClassification.Exact);
        }
        catch (ArgumentException)
        {
            return new TotalScalarRule(_factory.Boolean(false), [],
                FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape));
        }
    }

    private static bool MatchesAssembly(IAssemblySymbol assembly, ImmutableArray<ApiSpecAssemblyIdentity> approved)
    {
        if (assembly is null)
        {
            throw new System.ArgumentNullException(nameof(assembly));
        }
        var identity = assembly.Identity;
        var token = identity.PublicKeyToken.IsDefaultOrEmpty ? string.Empty : HashEncoding.ToLowerHex(identity.PublicKeyToken);
        return approved.Any(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Name == identity.Name && candidate.PublicKeyToken == token));
    }
}
