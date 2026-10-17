namespace SharpProof.CompilerArtifact;

// Binds audited method evidence first to compiler symbols, then to Total IR values.
internal sealed class CompilerSpecificationMethodBinding(
    IrFactory factory, RelationalSpecPackMethod definition,
    ImmutableArray<IrTypeId> parameters, IrTypeId integerType)
{
    internal static RelationalSpecPackMethod? Admit(IMethodSymbol method, RelationalSpecPackMethod? definition)
    {
        if (definition == null ||
            method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic ||
            method.TypeParameters.Length != 0 ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
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
        RelationalSpecPackMethod definition)
    {
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
            if (arguments.Length != parameters.Length || arguments.Where((argument, ordinal) => argument.Type != parameters[ordinal]).Any())
            { throw new ArgumentException("A specification-pack argument has an invalid type."); }
            var result = CompilerTotalSpecTermInstantiator.Instantiate(factory, definition.Result, arguments, integerType, 0);
            return new TotalScalarRule(result, [], FrontendSubsetClassification.Exact);
        }
        catch (ArgumentException)
        {
            return new TotalScalarRule(factory.Boolean(false), [],
                FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape));
        }
    }

    private static bool MatchesAssembly(IAssemblySymbol assembly, ImmutableArray<ApiSpecAssemblyIdentity> approved)
    {
        var identity = assembly.Identity;
        var token = identity.PublicKeyToken.IsDefaultOrEmpty ? string.Empty : HashEncoding.ToLowerHex(identity.PublicKeyToken);
        return approved.Any(candidate => candidate.Name == identity.Name && candidate.PublicKeyToken == token);
    }
}
