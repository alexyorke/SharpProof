// Specification packs are audited package data. They are never discovered
// from the consumer's filesystem and are inactive unless explicitly selected.
namespace SharpProof.CompilerArtifact;

internal sealed class CompilerSpecificationPackProvider
{
    private readonly IrFactory _factory;
    private readonly ImmutableDictionary<string, RelationalSpecPackMethod> _methods;
    private readonly Dictionary<IMethodSymbol, RelationalSpecPackMethod?> _resolved =
        new(SymbolEqualityComparer.Default);

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        IEnumerable<string>? enabledPacks)
        : this(factory, ResolveConfiguration(enabledPacks))
    {
    }

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        CompilerSpecificationPackConfiguration authority)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        authority = ArgumentNullGuard.NotNull(authority, nameof(authority));
        var catalog = RelationalSpecPackCatalogData.Catalog;
        if (!CompilerSpecificationPackSelection.IsValid(
                authority.SpecificationPackIds,
                authority.SpecificationPackCatalogVersion,
                authority.SpecificationPackCatalogSha256) ||
            authority.SpecificationPackCatalogVersion != catalog.Version ||
            authority.SpecificationPackCatalogSha256 != catalog.EvidenceSha256)
        {
            throw new InvalidOperationException(
                "The SharpProof specification-pack authority is not current.");
        }

        var methods = ImmutableDictionary.CreateBuilder<
            string,
            RelationalSpecPackMethod>(StringComparer.Ordinal);
        foreach (var packId in authority.SpecificationPackIds)
        {
            if (!catalog.Packs.TryGetValue(packId, out var pack))
            {
                throw new InvalidOperationException(
                    "Unknown SharpProof specification pack '" + packId + "'.");
            }

            foreach (var method in pack.Methods)
            {
                var definition = method with
                {
                    EvidenceSha256 = catalog.EvidenceSha256,
                    EvidenceIdentity = pack.Id + "@" + pack.Version
                };
                try
                {
                    methods.Add(method.DocumentationCommentId, definition);
                }
                catch (ArgumentException exception)
                    when (exception is not ArgumentNullException)
                {
                    throw new InvalidOperationException(
                        "Enabled SharpProof specification packs overlap at '" +
                        method.DocumentationCommentId + "'.",
                        exception);
                }
            }
        }

        _methods = methods.ToImmutable();
    }

    internal static CompilerSpecificationPackConfiguration ResolveConfiguration(
        IEnumerable<string>? enabledPacks)
    {
        var catalog = RelationalSpecPackCatalogData.Catalog;
        var selected = CanonicalizeSelection(enabledPacks, catalog);
        return new CompilerSpecificationPackConfiguration
        {
            SpecificationPackIds = selected,
            SpecificationPackCatalogVersion = catalog.Version,
            SpecificationPackCatalogSha256 = catalog.EvidenceSha256
        };
    }

    private static string[] CanonicalizeSelection(
        IEnumerable<string>? enabledPacks,
        RelationalSpecPackCatalog catalog)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in enabledPacks ?? [])
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                continue;
            }

            if (!seen.Add(normalized))
            {
                throw new InvalidOperationException(
                    "SharpProof specification-pack identifiers must be unique.");
            }

            values.Add(normalized);
        }

        values.Sort(StringComparer.Ordinal);
        foreach (var packId in values)
        {
            if (!catalog.Packs.ContainsKey(packId))
            {
                throw new InvalidOperationException(
                    "Unknown SharpProof specification pack '" + packId + "'.");
            }
        }

        return values.ToArray();
    }

    internal bool CanResolve(IMethodSymbol method)
    {
        return TryResolve(method, out _);
    }

    internal bool TryBuild(
        IMethodSymbol method,
        IrMemberId member,
        CancellationToken cancellationToken,
        out IrRelationalSummary? summary)
    {
        summary = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolve(method, out var definition))
        {
            return false;
        }

        var memberInfo = _factory.GetMemberInfo(member);
        if (!memberInfo.IsStatic ||
            memberInfo.ParameterTypes.Length != definition.ParameterTypes.Length ||
            memberInfo.ReturnType != TypeId(definition.ResultType))
        {
            return false;
        }

        var prefix = CompilerSpecificationPackSelection.GetSummaryPrefix(
            CompilerSummaryOrigin.SpecificationPack)!;
        var parameters = memberInfo.ParameterTypes
            .Select((type, ordinal) => _factory.CreateVariable(
                prefix + ":parameter:" + ordinal.ToString(
                    CultureInfo.InvariantCulture),
                type))
            .ToImmutableArray();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (_factory.GetVariableInfo(parameters[index]).Type !=
                TypeId(definition.ParameterTypes[index]))
            {
                return false;
            }
        }

        IrTerm resultExpression;
        try
        {
            resultExpression = Instantiate(definition.Result, parameters);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var result = _factory.CreateVariable(prefix + ":result", memberInfo.ReturnType);
        var builder = new IrProgramBuilder(_factory);
        var entry = builder.CreateBlock(prefix + ":entry");
        builder.SetEntry(entry);
        builder.Return(
            entry,
            _factory.CreateOperation(prefix + ":return"),
            resultExpression);
        var signature = CompilerSummarySignature.Create(
            member,
            parameters,
            result,
            new IrSummaryProvenance(
                IrSummaryOrigin.SpecificationPack,
                definition.EvidenceSha256,
                definition.EvidenceIdentity,
                method.GetDocumentationCommentId() ?? string.Empty));
        var environment = parameters.ToImmutableDictionary(
            static parameter => parameter,
            parameter => (IrTerm)_factory.Variable(parameter));
        var built = IrRelationalSummaryBuilder.Build(
            builder.Build(),
            signature,
            environment);
        summary = built.Summary;
        return built.IsSuccess;
    }

    private bool TryResolve(
        IMethodSymbol method,
        out RelationalSpecPackMethod definition)
    {
        method = SemanticClaimIdentity.NormalizeCandidate(method).OriginalDefinition;
        if (_resolved.TryGetValue(method, out var cached))
        {
            definition = cached!;
            return cached != null;
        }

        var identity = method.GetDocumentationCommentId();
        if (identity == null ||
            !_methods.TryGetValue(identity, out var resolved) ||
            resolved == null ||
            method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic ||
            method.TypeParameters.Length != 0 ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            method.Parameters.Length != resolved.ParameterTypes.Length ||
            !MatchesAssembly(method.ContainingAssembly, resolved.Assemblies) ||
            !MatchesType(method.ReturnType, resolved.ResultType))
        {
            definition = null!;
            _resolved[method] = null;
            return false;
        }

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (!MatchesType(
                    method.Parameters[index].Type,
                    resolved.ParameterTypes[index]))
            {
                definition = null!;
                _resolved[method] = null;
                return false;
            }
        }

        definition = resolved;
        _resolved[method] = definition;
        return true;
    }

    private IrTerm Instantiate(
        SpecTermDeclaration term,
        ImmutableArray<IrVarId> parameters)
    {
        IrTerm result = term switch
        {
            SpecVariableDeclaration
            {
                Role: SpecVariableRole.Parameter
            } parameter when parameter.Ordinal >= 0 &&
                parameter.Ordinal < parameters.Length =>
                _factory.Variable(parameters[parameter.Ordinal]),
            SpecBooleanDeclaration value => _factory.Boolean(value.Value),
            SpecIntegerDeclaration value => _factory.Integer(value.Value),
            SpecUnaryDeclaration value => _factory.Unary(
                value.Operator,
                Instantiate(value.Operand, parameters)),
            SpecBinaryDeclaration value => _factory.Binary(
                value.Operator,
                Instantiate(value.Left, parameters),
                Instantiate(value.Right, parameters)),
            SpecConditionalDeclaration value => _factory.Conditional(
                Instantiate(value.Condition, parameters),
                Instantiate(value.WhenTrue, parameters),
                Instantiate(value.WhenFalse, parameters)),
            _ => throw new ArgumentException("A specification-pack term is invalid.")
        };
        if (result.Type != TypeId(term.Type))
        {
            throw new ArgumentException(
                "A specification-pack term has an invalid type.");
        }

        return result;
    }

    private IrTypeId TypeId(IrTypeKind kind)
    {
        return kind switch
        {
            IrTypeKind.Boolean => _factory.BooleanType,
            IrTypeKind.Integer => _factory.IntegerType,
            _ => throw new ArgumentException(
                "A specification-pack scalar type is unsupported.")
        };
    }

    private static bool MatchesType(ITypeSymbol type, IrTypeKind expected)
    {
        return expected switch
        {
            IrTypeKind.Boolean => type.SpecialType == SpecialType.System_Boolean,
            IrTypeKind.Integer => type.SpecialType is
                SpecialType.System_Int32 or SpecialType.System_Int64,
            _ => false
        };
    }

    private static bool MatchesAssembly(
        IAssemblySymbol assembly,
        ImmutableArray<ApiSpecAssemblyIdentity> approved)
    {
        var identity = assembly.Identity;
        var token = identity.PublicKeyToken.IsDefaultOrEmpty
            ? string.Empty
            : HashEncoding.ToLowerHex(identity.PublicKeyToken);
        return approved.Any(candidate =>
            candidate.Name == identity.Name &&
            candidate.PublicKeyToken == token);
    }
}
