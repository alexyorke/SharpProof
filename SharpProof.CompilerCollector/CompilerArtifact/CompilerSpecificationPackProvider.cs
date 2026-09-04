using System.Reflection;
using System.Text.Json;

// Specification packs are audited package data. They are never discovered
// from the consumer's filesystem and are inactive unless explicitly selected.
namespace SharpProof.CompilerArtifact;

internal sealed class CompilerSpecificationPackProvider
{
    private const string ResourceName =
        "SharpProof.Specs.RelationalSpecPackCatalog.json";
    private const int MaximumCatalogBytes = 1024 * 1024;
    private const int MaximumTermDepth = 64;
    private static readonly Lazy<Catalog> SharedCatalog = new(
        LoadCatalog,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly IrFactory _factory;
    private readonly ImmutableDictionary<string, MethodDefinition> _methods;
    private readonly Dictionary<IMethodSymbol, MethodDefinition?> _resolved =
        new(SymbolEqualityComparer.Default);

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        IEnumerable<string>? enabledPacks)
        : this(factory, ResolveAuthority(enabledPacks))
    {
    }

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        CompilerSpecificationPackAuthority authority)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));

        var catalog = SharedCatalog.Value;
        authority = ArgumentNullGuard.NotNull(authority, nameof(authority));
        if (!CompilerSpecificationPackAuthorityValidation.IsValid(
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
            MethodDefinition>(StringComparer.Ordinal);
        foreach (var packId in authority.SpecificationPackIds)
        {
            if (!catalog.Packs.TryGetValue(packId, out var pack))
            {
                throw new InvalidOperationException(
                    "Unknown SharpProof specification pack '" +
                    packId + "'.");
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

    internal static CompilerSpecificationPackAuthority ResolveAuthority(
        IEnumerable<string>? enabledPacks)
    {
        var catalog = SharedCatalog.Value;
        var selected = CanonicalizeSelection(enabledPacks, catalog);
        return new CompilerSpecificationPackAuthority
        {
            SpecificationPackIds = selected,
            SpecificationPackCatalogVersion = catalog.Version,
            SpecificationPackCatalogSha256 = catalog.EvidenceSha256
        };
    }

    private static string[] CanonicalizeSelection(
        IEnumerable<string>? enabledPacks,
        Catalog catalog)
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
                    "Unknown SharpProof specification pack '" +
                    packId + "'.");
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
            memberInfo.ParameterTypes.Length !=
            definition.ParameterTypes.Length ||
            memberInfo.ReturnType != TypeId(definition.ResultType))
        {
            return false;
        }

        var specificationPackPrefix =
            CompilerSpecificationPackAuthorityValidation.GetSummaryPrefix(
                CompilerSummaryOrigin.SpecificationPack)!;
        var parameters = memberInfo.ParameterTypes
            .Select((type, ordinal) => _factory.CreateVariable(
                specificationPackPrefix + ":parameter:" + ordinal.ToString(
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
            resultExpression = Instantiate(
                definition.Result,
                parameters,
                depth: 0);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (resultExpression.Type != memberInfo.ReturnType)
        {
            return false;
        }

        var result = _factory.CreateVariable(
            specificationPackPrefix + ":result",
            memberInfo.ReturnType);
        var builder = new IrProgramBuilder(_factory);
        var entry = builder.CreateBlock(specificationPackPrefix + ":entry");
        builder.SetEntry(entry);
        builder.Return(
            entry,
            _factory.CreateOperation(specificationPackPrefix + ":return"),
            resultExpression);
        var signature = new IrSummarySignature(
            member,
            receiver: null,
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
        out MethodDefinition definition)
    {
        method = SemanticClaimIdentity.NormalizeCandidate(method)
            .OriginalDefinition;
        if (_resolved.TryGetValue(method, out var cached))
        {
            definition = cached!;
            return cached != null;
        }
        var identity = method.GetDocumentationCommentId();
        if (identity == null ||
            !_methods.TryGetValue(identity, out var resolved) ||
            resolved == null)
        {
            definition = null!;
            _resolved[method] = null;
            return false;
        }

        if (
            method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic ||
            method.TypeParameters.Length != 0 ||
            method.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None) ||
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
        Term term,
        ImmutableArray<IrVarId> parameters,
        int depth)
    {
        if (depth > MaximumTermDepth)
        {
            throw new ArgumentException("A specification-pack term is too deep.");
        }

        IrTerm result = term switch
        {
            ParameterTerm parameter when
                parameter.Ordinal >= 0 &&
                parameter.Ordinal < parameters.Length =>
                _factory.Variable(parameters[parameter.Ordinal]),
            BooleanTerm boolean => _factory.Boolean(boolean.Value),
            IntegerTerm integer => _factory.Integer(integer.Value),
            UnaryTerm unary => _factory.Unary(
                unary.Operator,
                Instantiate(unary.Operand, parameters, depth + 1)),
            BinaryTerm binary => _factory.Binary(
                binary.Operator,
                Instantiate(binary.Left, parameters, depth + 1),
                Instantiate(binary.Right, parameters, depth + 1)),
            ConditionalTerm conditional => _factory.Conditional(
                Instantiate(conditional.Condition, parameters, depth + 1),
                Instantiate(conditional.WhenTrue, parameters, depth + 1),
                Instantiate(conditional.WhenFalse, parameters, depth + 1)),
            _ => throw new ArgumentException(
                "A specification-pack term is invalid.")
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
            IrTypeKind.Boolean =>
                type.SpecialType == SpecialType.System_Boolean,
            IrTypeKind.Integer =>
                type.SpecialType is
                    SpecialType.System_Int32 or
                    SpecialType.System_Int64,
            _ => false
        };
    }

    private static bool MatchesAssembly(
        IAssemblySymbol assembly,
        ImmutableArray<AssemblyIdentity> approved)
    {
        var identity = assembly.Identity;
        var token = identity.PublicKeyToken.IsDefaultOrEmpty
            ? string.Empty
            : HashEncoding.ToLowerHex(identity.PublicKeyToken);
        return approved.Any(candidate =>
            candidate.Name == identity.Name &&
            candidate.PublicKeyToken == token);
    }

    private static Catalog LoadCatalog()
    {
        var assembly = typeof(ApiSpecTable).GetTypeInfo().Assembly;
        using var resource = assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidDataException(
                "The relational specification-pack catalog is missing.");
        if (!resource.CanRead ||
            resource.Length <= 0 ||
            resource.Length > MaximumCatalogBytes)
        {
            throw new InvalidDataException(
                "The relational specification-pack catalog has an invalid size.");
        }

        using var buffer = new MemoryStream(
            checked((int)resource.Length));
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var evidenceSha256 = HashEncoding.ComputeSha256Hex(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumTermDepth
        });
        var root = document.RootElement;
        RequireObject(
            root,
            "catalog",
            "schema",
            "schemaVersion",
            "packs");
        if (RequiredString(root, "schema", "catalog") !=
                "SharpProof.RelationalSpecPackCatalog" ||
            RequiredInt32(root, "schemaVersion", "catalog") !=
                CompilerSpecificationPackCatalogVersions.Current)
        {
            throw new InvalidDataException(
                "The relational specification-pack catalog schema is unsupported.");
        }

        var packs = ImmutableDictionary.CreateBuilder<
            string,
            PackDefinition>(StringComparer.Ordinal);
        string? previousPack = null;
        foreach (var element in RequiredArray(root, "packs", "catalog"))
        {
            var pack = ParsePack(element);
            if (previousPack != null &&
                StringComparer.Ordinal.Compare(previousPack, pack.Id) >= 0)
            {
                throw new InvalidDataException(
                    "Specification-pack identifiers must be unique and sorted.");
            }

            previousPack = pack.Id;
            packs.Add(pack.Id, pack);
        }

        if (packs.Count == 0)
        {
            throw new InvalidDataException(
                "The relational specification-pack catalog is empty.");
        }

        if (evidenceSha256 != CompilerSpecificationPackCatalogVersions.Sha256)
        {
            throw new InvalidDataException(
                "The relational specification-pack catalog digest is not authoritative.");
        }

        return new Catalog(
            packs.ToImmutable(),
            CompilerSpecificationPackCatalogVersions.Current,
            evidenceSha256);
    }

    private static PackDefinition ParsePack(JsonElement element)
    {
        RequireObject(
            element,
            "pack",
            "id",
            "version",
            "evidence",
            "methods");
        var id = RequiredIdentifier(element, "id", "pack");
        var version = RequiredIdentifier(element, "version", "pack");
        _ = RequiredString(element, "evidence", "pack");
        var methods = ImmutableArray.CreateBuilder<MethodDefinition>();
        string? previousMethod = null;
        foreach (var methodElement in RequiredArray(
                     element,
                     "methods",
                     "pack"))
        {
            var method = ParseMethod(methodElement);
            if (previousMethod != null &&
                StringComparer.Ordinal.Compare(
                    previousMethod,
                    method.DocumentationCommentId) >= 0)
            {
                throw new InvalidDataException(
                    "Specification-pack methods must be unique and sorted.");
            }

            previousMethod = method.DocumentationCommentId;
            methods.Add(method);
        }

        if (methods.Count == 0)
        {
            throw new InvalidDataException(
                "A relational specification pack cannot be empty.");
        }

        return new PackDefinition(
            id,
            version,
            methods.ToImmutable());
    }

    private static MethodDefinition ParseMethod(JsonElement element)
    {
        RequireObject(
            element,
            "method",
            "documentationCommentId",
            "assemblies",
            "parameterTypes",
            "resultType",
            "result");
        var identity = RequiredString(
            element,
            "documentationCommentId",
            "method");
        if (!identity.StartsWith("M:", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A specification-pack method identity is invalid.");
        }

        var assemblies = ImmutableArray.CreateBuilder<AssemblyIdentity>();
        string? previousAssembly = null;
        foreach (var assemblyElement in RequiredArray(
                     element,
                     "assemblies",
                     "method"))
        {
            RequireObject(
                assemblyElement,
                "assembly",
                "name",
                "publicKeyToken");
            var name = RequiredString(
                assemblyElement,
                "name",
                "assembly");
            var token = RequiredString(
                assemblyElement,
                "publicKeyToken",
                "assembly",
                allowEmpty: true);
            if (token.Length != 0 &&
                (token.Length != 16 || !token.All(static character =>
                    character is >= '0' and <= '9' or
                    >= 'a' and <= 'f')))
            {
                throw new InvalidDataException(
                    "A specification-pack public-key token is invalid.");
            }

            var key = name + "|" + token;
            if (previousAssembly != null &&
                StringComparer.Ordinal.Compare(previousAssembly, key) >= 0)
            {
                throw new InvalidDataException(
                    "Specification-pack assemblies must be unique and sorted.");
            }

            previousAssembly = key;
            assemblies.Add(new AssemblyIdentity(name, token));
        }

        if (assemblies.Count == 0)
        {
            throw new InvalidDataException(
                "A specification-pack method requires an assembly identity.");
        }

        var parameterTypes = RequiredArray(
                element,
                "parameterTypes",
                "method")
            .Select(static value => ParseType(value, "parameterTypes"))
            .ToImmutableArray();
        var resultType = ParseType(
            RequiredProperty(element, "resultType", "method"),
            "resultType");
        var result = ParseTerm(
            RequiredProperty(element, "result", "method"),
            depth: 0);
        if (result.Type != resultType)
        {
            throw new InvalidDataException(
                "A specification-pack result expression has the wrong type.");
        }

        return new MethodDefinition(
            identity,
            assemblies.ToImmutable(),
            parameterTypes,
            resultType,
            result,
            EvidenceSha256: string.Empty,
            EvidenceIdentity: string.Empty);
    }

    private static Term ParseTerm(JsonElement element, int depth)
    {
        if (depth > MaximumTermDepth)
        {
            throw new InvalidDataException(
                "A specification-pack term is too deep.");
        }

        var kind = RequiredString(element, "kind", "term");
        var type = ParseType(
            RequiredProperty(element, "type", "term"),
            "term.type");
        var context = kind + " term";
        JsonElement Get(string name)
        {
            return RequiredProperty(element, name, context);
        }

        string GetString(string name)
        {
            return RequiredString(element, name, context);
        }

        switch (kind)
        {
            case "parameter":
                RequireObject(element, context, "kind", "type", "ordinal");
                return new ParameterTerm(
                    type,
                    RequiredInt32(element, "ordinal", context));
            case "boolean":
                RequireObject(element, context, "kind", "type", "value");
                var booleanValue = Get("value");
                if (type != IrTypeKind.Boolean ||
                    booleanValue.ValueKind is not JsonValueKind.True and
                        not JsonValueKind.False)
                {
                    throw new InvalidDataException(
                        "A specification-pack Boolean literal is invalid.");
                }

                return new BooleanTerm(booleanValue.GetBoolean());
            case "integer":
                RequireObject(element, context, "kind", "type", "value");
                if (type != IrTypeKind.Integer ||
                    !Get("value").TryGetInt64(out var integer))
                {
                    throw new InvalidDataException(
                        "A specification-pack integer literal is invalid.");
                }

                return new IntegerTerm(integer);
            case "unary":
                RequireObject(element, context, "kind", "type", "operator", "operand");
                var unary = ParseUnaryOperator(GetString("operator"));
                return new UnaryTerm(
                    type,
                    unary,
                    ParseTerm(Get("operand"), depth + 1));
            case "binary":
                RequireObject(element, context, "kind", "type", "operator", "left", "right");
                var binary = ParseBinaryOperator(GetString("operator"));
                return new BinaryTerm(
                    type,
                    binary,
                    ParseTerm(Get("left"), depth + 1),
                    ParseTerm(Get("right"), depth + 1));
            case "conditional":
                RequireObject(
                    element,
                    context,
                    "kind",
                    "type",
                    "condition",
                    "whenTrue",
                    "whenFalse");
                return new ConditionalTerm(
                    type,
                    ParseTerm(Get("condition"), depth + 1),
                    ParseTerm(Get("whenTrue"), depth + 1),
                    ParseTerm(Get("whenFalse"), depth + 1));
            default:
                throw new InvalidDataException(
                    "A specification-pack term kind is unsupported.");
        }
    }

    private static IrUnaryOperator ParseUnaryOperator(string value)
    {
        return value switch
        {
            "Not" => IrUnaryOperator.Not,
            "Negate" => IrUnaryOperator.Negate,
            _ => throw new InvalidDataException(
                "A specification-pack unary operator is unsupported.")
        };
    }

    private static IrBinaryOperator ParseBinaryOperator(string value)
    {
        return value switch
        {
            "Add" => IrBinaryOperator.Add,
            "Subtract" => IrBinaryOperator.Subtract,
            "Multiply" => IrBinaryOperator.Multiply,
            "Divide" => IrBinaryOperator.Divide,
            "Remainder" => IrBinaryOperator.Remainder,
            "AndAlso" => IrBinaryOperator.AndAlso,
            "OrElse" => IrBinaryOperator.OrElse,
            "Equal" => IrBinaryOperator.Equal,
            "NotEqual" => IrBinaryOperator.NotEqual,
            "LessThan" => IrBinaryOperator.LessThan,
            "LessThanOrEqual" => IrBinaryOperator.LessThanOrEqual,
            "GreaterThan" => IrBinaryOperator.GreaterThan,
            "GreaterThanOrEqual" => IrBinaryOperator.GreaterThanOrEqual,
            _ => throw new InvalidDataException(
                "A specification-pack binary operator is unsupported.")
        };
    }

    private static IrTypeKind ParseType(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                context + " must be a scalar type name.");
        }

        return element.GetString() switch
        {
            "Boolean" => IrTypeKind.Boolean,
            "Integer" => IrTypeKind.Integer,
            _ => throw new InvalidDataException(
                context + " has an unsupported scalar type.")
        };
    }

    private static void RequireObject(
        JsonElement element,
        string context,
        params string[] properties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(context + " must be an object.");
        }

        var expected = new HashSet<string>(
            properties,
            StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (expected.Contains(property.Name) &&
                actual.Add(property.Name))
            {
                continue;
            }

            throw new InvalidDataException(
                context + " has an invalid property set.");
        }

        if (actual.Count != expected.Count)
        {
            throw new InvalidDataException(
                context + " has an invalid property set.");
        }
    }

    private static JsonElement RequiredProperty(
        JsonElement element,
        string name,
        string context)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new InvalidDataException(
                context + " is missing '" + name + "'.");
        }

        return value;
    }

    private static string RequiredString(
        JsonElement element,
        string name,
        string context,
        bool allowEmpty = false)
    {
        var value = RequiredProperty(element, name, context);
        if (value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { } text ||
            (!allowEmpty && string.IsNullOrWhiteSpace(text)))
        {
            throw new InvalidDataException(
                context + "." + name + " must be a string with content.");
        }

        return text;
    }

    private static string RequiredIdentifier(
        JsonElement element,
        string name,
        string context)
    {
        var value = RequiredString(element, name, context);
        if (!value.All(static character =>
                character is >= 'a' and <= 'z' or
                >= '0' and <= '9' or '.' or '-'))
        {
            throw new InvalidDataException(
                context + "." + name + " is not a canonical identifier.");
        }

        return value;
    }

    private static int RequiredInt32(
        JsonElement element,
        string name,
        string context)
    {
        var value = RequiredProperty(element, name, context);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new InvalidDataException(
                context + "." + name + " must be an Int32.");
        }

        return result;
    }

    private static JsonElement.ArrayEnumerator RequiredArray(
        JsonElement element,
        string name,
        string context)
    {
        var value = RequiredProperty(element, name, context);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                context + "." + name + " must be an array.");
        }

        return value.EnumerateArray();
    }

    private sealed record Catalog(
        ImmutableDictionary<string, PackDefinition> Packs,
        int Version,
        string EvidenceSha256);

    private sealed record PackDefinition(
        string Id,
        string Version,
        ImmutableArray<MethodDefinition> Methods);

    private sealed record MethodDefinition(
        string DocumentationCommentId,
        ImmutableArray<AssemblyIdentity> Assemblies,
        ImmutableArray<IrTypeKind> ParameterTypes,
        IrTypeKind ResultType,
        Term Result,
        string EvidenceSha256,
        string EvidenceIdentity);

    private sealed record AssemblyIdentity(
        string Name,
        string PublicKeyToken);

    private abstract record Term(IrTypeKind Type);

    private sealed record ParameterTerm(
        IrTypeKind Type,
        int Ordinal) : Term(Type);

    private sealed record BooleanTerm(bool Value) :
        Term(IrTypeKind.Boolean);

    private sealed record IntegerTerm(long Value) :
        Term(IrTypeKind.Integer);

    private sealed record UnaryTerm(
        IrTypeKind Type,
        IrUnaryOperator Operator,
        Term Operand) : Term(Type);

    private sealed record BinaryTerm(
        IrTypeKind Type,
        IrBinaryOperator Operator,
        Term Left,
        Term Right) : Term(Type);

    private sealed record ConditionalTerm(
        IrTypeKind Type,
        Term Condition,
        Term WhenTrue,
        Term WhenFalse) : Term(Type);
}
