namespace SharpProof.CompilerCollector;

// Roslyn supplies constants without invoking an attribute constructor. Every
// declaration is checked again here and bound only to the directly named root.
internal static class CompilerTaskRuntimePremiseCollector
{
    private const string AssemblyName = "SharpProof.Attributes";
    private const string DeploymentAttributeName = AssemblyName + ".ResourceTaskDeploymentAttribute";
    private const string QuantityAttributeName = AssemblyName + ".ResourceTaskRuntimeUpperBoundAttribute";
    private const string ProfileTypeName = AssemblyName + ".ResourceTaskDeploymentProfile";
    private const string QuantityTypeName = AssemblyName + ".ResourceTaskRuntimeQuantity";

    // Runtime declarations are not source Contract.Assume clauses. Only the
    // complete root-owned manifest set may be excluded from source parity.
    internal static ImmutableHashSet<string>? VerifiedManifestAssumptionIds(IMethodSymbol root, string callableId,
        ImmutableArray<WorkerAssumptionEvidence> assumptions, CancellationToken cancellation)
    {
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        cancellation.ThrowIfCancellationRequested();
        if (assumptions.IsDefault)
        { return null; }
        var parsed = Parse(root, callableId, cancellation);
        if (parsed.UnknownReason == "TaskRuntimePremisesNotDeclared")
        { return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal); }
        if (parsed.Declarations is not { } declarations || parsed.UnknownReason is not null)
        { return null; }
        var expected = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        expected.Add(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations));
        foreach (var declaration in declarations.Quantities)
        {
            cancellation.ThrowIfCancellationRequested();
            expected.Add(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declaration));
        }
        var remaining = new HashSet<string>(expected, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assumption in assumptions)
        {
            cancellation.ThrowIfCancellationRequested();
            if (assumption is null || assumption.Used || string.IsNullOrWhiteSpace(assumption.Id) || !seen.Add(assumption.Id))
            { return null; }
            if (assumption.Kind == WorkerAssumptionKind.TrustedBoundary && !expected.Contains(assumption.Id))
            { continue; }
            if (assumption.Kind != WorkerAssumptionKind.UserAssume || !remaining.Remove(assumption.Id))
            { return null; }
        }
        return expected.Count == declarations.Quantities.Length + 1 && remaining.Count == 0 ? expected.ToImmutable() : null;
    }

    internal static CompilerTaskRuntimePremiseDeclarationCapture Parse(IMethodSymbol root, string callableId, CancellationToken cancellation)
    {
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        cancellation.ThrowIfCancellationRequested();
        var attributes = root.GetAttributes().Where(static attribute => attribute is null
            ? throw new ArgumentNullException(nameof(attribute))
            : MetadataName(attribute.AttributeClass) is DeploymentAttributeName or QuantityAttributeName).ToArray();
        if (attributes.Length == 0)
        { return new(null, "TaskRuntimePremisesNotDeclared"); }
        if (!ResourceTaskRuntimePremiseIdentity.IsRootCallableId(callableId) ||
            !StringComparer.Ordinal.Equals(callableId, SemanticClaimIdentity.CreateCallableId(root)) || root.MethodKind != MethodKind.Ordinary ||
            !root.IsStatic || root.Arity != 0 || root.ContainingType.IsGenericType)
        { return new(null, "TaskRuntimePremisesInvalid"); }
        var assemblies = root.ContainingModule.ReferencedAssemblySymbols.Where(static candidate => candidate is null
            ? throw new ArgumentNullException(nameof(candidate)) : candidate.Identity.Name == AssemblyName).ToArray();
        if (assemblies.Length != 1 || assemblies[0].Identity.Version != typeof(CompilerTaskRuntimePremiseCollector).Assembly.GetName().Version ||
            !string.IsNullOrEmpty(assemblies[0].Identity.CultureName))
        { return new(null, "TaskRuntimePremisesAttributeIdentityInvalid"); }
        var assembly = assemblies[0];
        var deploymentType = assembly.GetTypeByMetadataName(DeploymentAttributeName);
        var quantityType = assembly.GetTypeByMetadataName(QuantityAttributeName);
        var profileEnum = assembly.GetTypeByMetadataName(ProfileTypeName);
        var quantityEnum = assembly.GetTypeByMetadataName(QuantityTypeName);
        if (!MetadataType(deploymentType, TypeKind.Class) || !MetadataType(quantityType, TypeKind.Class) ||
            !MetadataType(profileEnum, TypeKind.Enum) || !MetadataType(quantityEnum, TypeKind.Enum))
        { return new(null, "TaskRuntimePremisesAttributeIdentityInvalid"); }
        AttributeData? deployment = null;
        var quantities = new SortedDictionary<WorkerResourceTaskRuntimeQuantity, WorkerResourceTaskRuntimeDeclaration>();
        foreach (var attribute in attributes)
        {
            cancellation.ThrowIfCancellationRequested();
            var isDeployment = SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, deploymentType);
            if (!isDeployment && !SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, quantityType) ||
                !Constructor(attribute, isDeployment ? deploymentType! : quantityType!, isDeployment ? profileEnum! : quantityEnum!, isDeployment))
            { return new(null, "TaskRuntimePremisesAttributeIdentityInvalid"); }
            var arguments = attribute.ConstructorArguments;
            if (arguments.Length != 3 || arguments.Any(static argument => argument.Kind == TypedConstantKind.Error) ||
                !SymbolEqualityComparer.Default.Equals(arguments[0].Type, isDeployment ? profileEnum : quantityEnum) ||
                !attribute.NamedArguments.IsEmpty)
            { return new(null, "TaskRuntimePremisesInvalid"); }
            if (isDeployment)
            {
                if (deployment is not null)
                { return new(null, "TaskRuntimePremisesDuplicate"); }
                if (arguments[0].Kind != TypedConstantKind.Enum || arguments[0].Value is not int profile ||
                    profile != (int)WorkerResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed ||
                    arguments[1].Kind != TypedConstantKind.Primitive || arguments[1].Value is not string deploymentHash ||
                    !WorkerProtocolJson.IsSha256(CanonicalDigest(deploymentHash)) || arguments[2].Kind != TypedConstantKind.Primitive ||
                    arguments[2].Value is not string evidence || !ResourceTaskRuntimePremiseIdentity.IsEvidence(evidence))
                { return new(null, "TaskRuntimePremisesInvalid"); }
                deployment = attribute;
            }
            else
            {
                if (arguments[0].Kind != TypedConstantKind.Enum || arguments[0].Value is not int rawQuantity ||
                    !ResourceTaskRuntimePremiseIdentity.RequiredQuantities.Contains((WorkerResourceTaskRuntimeQuantity)rawQuantity) ||
                    arguments[1].Kind != TypedConstantKind.Primitive || arguments[1].Value is not long upperBound || upperBound < 0 ||
                    arguments[2].Kind != TypedConstantKind.Primitive || arguments[2].Value is not string evidence ||
                    !ResourceTaskRuntimePremiseIdentity.IsEvidence(evidence))
                { return new(null, "TaskRuntimePremisesInvalid"); }
                var quantity = (WorkerResourceTaskRuntimeQuantity)rawQuantity;
                if (quantities.ContainsKey(quantity))
                { return new(null, "TaskRuntimePremisesDuplicate"); }
                quantities.Add(quantity, new(quantity, upperBound, evidence));
            }
        }
        if (deployment is null || quantities.Count != ResourceTaskRuntimePremiseIdentity.RequiredQuantities.Length)
        { return new(null, "TaskRuntimePremisesIncomplete"); }
        var declarations = new WorkerResourceTaskRuntimeDeclarations(callableId,
            (WorkerResourceTaskDeploymentProfile)(int)deployment.ConstructorArguments[0].Value!,
            CanonicalDigest((string)deployment.ConstructorArguments[1].Value!),
            (string)deployment.ConstructorArguments[2].Value!, [.. quantities.Values]);
        return ResourceTaskRuntimePremiseIdentity.Validate(declarations) is { } failure ? new(null, failure) : new(declarations, null);
    }

    internal static CompilerTaskRuntimePremiseCapture Capture(IMethodSymbol root, string callableId, string moduleSha256,
        string compilerIdentity, string frameworkIdentity, CancellationToken cancellation)
    {
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        moduleSha256 = ArgumentNullGuard.NotNull(moduleSha256, nameof(moduleSha256));
        compilerIdentity = ArgumentNullGuard.NotNull(compilerIdentity, nameof(compilerIdentity));
        frameworkIdentity = ArgumentNullGuard.NotNull(frameworkIdentity, nameof(frameworkIdentity));
        cancellation.ThrowIfCancellationRequested();
        var parsed = Parse(root, callableId, cancellation);
        if (parsed.Declarations is not { } declarations)
        { return new(null, parsed.UnknownReason); }
        if (!WorkerProtocolJson.IsSha256(moduleSha256) || !ResourceTaskRuntimePremiseIdentity.IsEvidence(compilerIdentity) ||
            !ResourceTaskRuntimePremiseIdentity.IsEvidence(frameworkIdentity))
        { return new(null, "TaskRuntimePremisesOwnershipInvalid"); }
        return new(new(callableId, moduleSha256, compilerIdentity, frameworkIdentity, declarations), null);
    }

    private static bool MetadataType(INamedTypeSymbol? type, TypeKind kind)
    {
        return type is not null && type.TypeKind == kind && type.Arity == 0 &&
            !type.Locations.Any(static location => location is null ? throw new ArgumentNullException(nameof(location)) : location.IsInSource);
    }

    private static string? MetadataName(INamedTypeSymbol? type)
    {
        if (type is null)
        { return null; }
        return type.ContainingType is { } owner ? MetadataName(owner) + "+" + type.MetadataName :
            (type.ContainingNamespace.IsGlobalNamespace ? "" : NamespaceName(type.ContainingNamespace) + ".") + type.MetadataName;
    }

    private static string NamespaceName(INamespaceSymbol symbol)
    {
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));
        if (symbol.IsGlobalNamespace)
        { return string.Empty; }
        return symbol.ContainingNamespace.IsGlobalNamespace ? symbol.MetadataName :
            NamespaceName(symbol.ContainingNamespace) + "." + symbol.MetadataName;
    }

    private static string CanonicalDigest(string value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        return new string(value.Select(static character =>
            character is >= 'A' and <= 'F' ? (char)(character + ('a' - 'A')) : character).ToArray());
    }

    private static bool Constructor(AttributeData attribute, INamedTypeSymbol expected, INamedTypeSymbol enumeration, bool deployment)
    {
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        expected = ArgumentNullGuard.NotNull(expected, nameof(expected));
        enumeration = ArgumentNullGuard.NotNull(enumeration, nameof(enumeration));
        var constructor = attribute.AttributeConstructor;
        if (constructor is null || !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, expected) || constructor.IsStatic ||
            constructor.MethodKind != MethodKind.Constructor || constructor.DeclaredAccessibility != Accessibility.Public ||
            constructor.Parameters.Length != 3 || constructor.Parameters.Any(static parameter => parameter is null
                ? throw new ArgumentNullException(nameof(parameter)) : parameter.RefKind != RefKind.None || parameter.IsOptional))
        { return false; }
        return SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, enumeration) &&
            constructor.Parameters[1].Type.SpecialType == (deployment ? SpecialType.System_String : SpecialType.System_Int64) &&
            constructor.Parameters[2].Type.SpecialType == SpecialType.System_String;
    }
}
