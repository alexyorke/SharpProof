namespace SharpProof.Frontend;

internal sealed class ContractApiShapeValidator
{
    private readonly INamedTypeSymbol? _conditionalAttribute;
    private readonly ContractApiMetadataNameMatcher _metadataNames;

    internal ContractApiShapeValidator(INamedTypeSymbol? conditionalAttribute, ContractApiMetadataNameMatcher metadataNames)
    {
        metadataNames = RepositoryParameterGuard.NotNull(metadataNames, nameof(metadataNames));
        _conditionalAttribute = conditionalAttribute;
        _metadataNames = metadataNames;
    }

    internal (IMethodSymbol Result, IMethodSymbol Old)? TryResolve(
        INamedTypeSymbol contract)
    {
        if (contract is not
            {
                TypeKind: TypeKind.Class,
                IsStatic: true,
                Arity: 0
            } ||
            !HasConditionalSymbol(contract) ||
            !HasSingleClause(contract, ContractApiMetadata.RequiresMethodName) ||
            !HasSingleClause(contract, ContractApiMetadata.EnsuresMethodName) ||
            !HasSingleClause(contract, ContractApiMetadata.AssumeMethodName))
        {
            return null;
        }

        var result = GetSingleGenericIdentityMethod(
            contract,
            ContractApiMetadata.ResultMethodName,
            parameterCount: 0);
        var old = GetSingleGenericIdentityMethod(
            contract,
            ContractApiMetadata.OldMethodName,
            parameterCount: 1);
        return result is { } resultMethod && old is { } oldMethod
            ? (resultMethod, oldMethod)
            : null;
    }

    private bool HasSingleClause(
        INamedTypeSymbol contract,
        string name)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        contract = RepositoryParameterGuard.NotNull(contract, nameof(contract));
        return GetSingleMethod(contract, name) is
        {
            MethodKind: MethodKind.Ordinary,
            DeclaredAccessibility: Accessibility.Public,
            IsStatic: true,
            Arity: 0,
            ReturnsVoid: true
        } method &&
            method.Parameters.Length == 1 &&
            method.Parameters[0] is
            {
                RefKind: RefKind.None,
                ScopedKind: ScopedKind.None,
                IsParams: false,
                IsOptional: false,
                Type.SpecialType: SpecialType.System_Boolean
            } &&
            HasOnlyElidingConditionalAttribute(method);
    }

    private bool HasOnlyElidingConditionalAttribute(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        if (_conditionalAttribute == null)
        {
            return false;
        }

        AttributeData? attribute = null;
        var count = 0;
        foreach (var candidate in method.GetAttributes())
        {
            if (!_metadataNames.Matches(
                    candidate.AttributeClass,
                    ContractApiMetadata.ConditionalAttribute))
            {
                continue;
            }

            count++;
            attribute ??= candidate;
        }

        return count == 1 &&
            SymbolEqualityComparer.Default.Equals(
                attribute!.AttributeClass?.OriginalDefinition,
                _conditionalAttribute.OriginalDefinition) &&
            attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0] is
            {
                Kind: TypedConstantKind.Primitive,
                Value: ContractApiCatalog.ConditionalSymbol
            };
    }

    private static bool HasConditionalSymbol(INamedTypeSymbol contract)
    {
        contract = RepositoryParameterGuard.NotNull(contract, nameof(contract));
        var members = contract.GetMembers("ConditionalSymbol");
        return members.Length == 1 &&
            members[0] is IFieldSymbol
            {
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                IsConst: true,
                Type.SpecialType: SpecialType.System_String,
                ConstantValue: ContractApiCatalog.ConditionalSymbol
            };
    }

    private static IMethodSymbol? GetSingleGenericIdentityMethod(
        INamedTypeSymbol contract,
        string name,
        int parameterCount)
    {
        if (GetSingleMethod(contract, name) is not
            {
                MethodKind: MethodKind.Ordinary,
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                Arity: 1,
                ReturnsByRef: false,
                ReturnsByRefReadonly: false
            } method ||
            method.Parameters.Length != parameterCount ||
            !HasUnconstrainedTypeParameter(method.TypeParameters[0]) ||
            !SymbolEqualityComparer.Default.Equals(
                method.ReturnType,
                method.TypeParameters[0]) ||
            (parameterCount != 0 && (method.Parameters[0] is not
            {
                RefKind: RefKind.None,
                ScopedKind: ScopedKind.None,
                IsParams: false,
                IsOptional: false
            } parameter ||
            !SymbolEqualityComparer.Default.Equals(
                parameter.Type,
                method.TypeParameters[0]))))
        {
            return null;
        }

        return method;
    }

    private static IMethodSymbol? GetSingleMethod(
        INamedTypeSymbol contract,
        string name)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        contract = RepositoryParameterGuard.NotNull(contract, nameof(contract));
        IMethodSymbol? member = null;
        var count = 0;
        foreach (var candidate in contract.GetMembers(name))
        {
            if (candidate is not IMethodSymbol candidateMethod)
            {
                continue;
            }

            count++;
            if (count > 1)
            {
                return null;
            }
            member = candidateMethod;
        }
        return member;
    }

    private static bool HasUnconstrainedTypeParameter(
        ITypeParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        return !parameter.HasConstructorConstraint &&
            !parameter.HasReferenceTypeConstraint &&
            !parameter.HasValueTypeConstraint &&
            !parameter.HasNotNullConstraint &&
            !parameter.HasUnmanagedTypeConstraint &&
            parameter.ConstraintTypes.IsDefaultOrEmpty;
    }

}
