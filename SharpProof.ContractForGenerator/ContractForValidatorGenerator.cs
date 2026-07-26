namespace SharpProof.ContractForGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ContractForValidatorGenerator : IIncrementalGenerator {
    private const string ContractForMetadataName =
        "SharpProof.Attributes.ContractForAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ContractForMetadataName,
                static (_, cancellationToken) => {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                },
                static (attributeContext, cancellationToken) => {
                    cancellationToken.ThrowIfCancellationRequested();
                    return attributeContext.TargetSymbol as INamedTypeSymbol;
                })
            .Where(static candidate => candidate != null)
            .Select(static (candidate, _) => candidate!)
            .Collect()
            .WithTrackingName("ContractForCandidates");
        var input = context.CompilationProvider
            .Combine(candidates)
            .WithTrackingName("ContractForValidationInput");
        context.RegisterSourceOutput(
            input,
            static (productionContext, value) =>
                Execute(
                    productionContext,
                    value.Left,
                    value.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidateSymbols) {
        context.CancellationToken.ThrowIfCancellationRequested();
        var contractFor = compilation.GetTypeByMetadataName(
            ContractForMetadataName);
        if (contractFor == null) return;

        var diagnostics = new List<Diagnostic>();
        var companions = ResolveCompanions(
            contractFor,
            candidateSymbols,
            diagnostics,
            context.CancellationToken);
        var contractClauses = ContractClauseSymbols.Resolve(compilation);
        var groups = new Dictionary<
            INamedTypeSymbol,
            List<ResolvedCompanion>>(SymbolEqualityComparer.Default);
        foreach (var companion in companions) {
            if (!groups.TryGetValue(companion.Target, out var group)) {
                group = [];
                groups.Add(companion.Target, group);
            }
            group.Add(companion);
        }

        foreach (var group in groups
                     .OrderBy(
                         static pair => pair.Key,
                         NamedTypeDeterministicComparer.Instance)) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var ordered = group.Value
                .OrderBy(
                    static companion => companion.Companion,
                    NamedTypeDeterministicComparer.Instance)
                .ToImmutableArray();
            if (ordered.Length != 1) {
                foreach (var duplicate in ordered)
                    diagnostics.Add(Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.DuplicateCompanion,
                        duplicate.AttributeLocation,
                        duplicate.Target.Name));
                continue;
            }
            ValidateCompanion(
                compilation,
                ordered[0],
                contractClauses,
                diagnostics,
                context.CancellationToken);
        }

        foreach (var diagnostic in diagnostics
                     .OrderBy(
                         static diagnostic =>
                             diagnostic.Location.SourceTree?.FilePath,
                         StringComparer.Ordinal)
                     .ThenBy(static diagnostic =>
                         diagnostic.Location.SourceSpan.Start)
                     .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
                     .ThenBy(
                         static diagnostic => diagnostic.GetMessage(),
                         StringComparer.Ordinal))
            context.ReportDiagnostic(diagnostic);
    }

    private static ImmutableArray<ResolvedCompanion> ResolveCompanions(
        INamedTypeSymbol contractFor,
        ImmutableArray<INamedTypeSymbol> candidateSymbols,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken) {
        var unique = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var candidate in candidateSymbols)
            unique.Add(candidate);
        var result = ImmutableArray.CreateBuilder<ResolvedCompanion>();
        foreach (var companion in unique.OrderBy(
                     static type => type,
                     NamedTypeDeterministicComparer.Instance)) {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = companion.GetAttributes()
                .Where(attribute =>
                    SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass?.OriginalDefinition,
                        contractFor.OriginalDefinition))
                .OrderBy(
                    static attribute =>
                        attribute.ApplicationSyntaxReference?.Span.Start ?? -1)
                .ToImmutableArray();
            var fallback = GetSourceLocation(companion, Location.None);
            if (attributes.Length != 1) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.InvalidTarget,
                    attributes.FirstOrDefault() is { } first
                        ? GetAttributeLocation(first, cancellationToken, fallback)
                        : fallback,
                    companion.Name));
                continue;
            }
            var attribute = attributes[0];
            var attributeLocation = GetAttributeLocation(
                attribute,
                cancellationToken,
                fallback);
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Kind != TypedConstantKind.Type ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol target ||
                target.TypeKind == TypeKind.Error) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.InvalidTarget,
                    attributeLocation,
                    companion.Name));
                continue;
            }
            var isOpenTarget = target.IsUnboundGenericType;
            result.Add(new ResolvedCompanion(
                companion,
                isOpenTarget ? target.OriginalDefinition : target,
                attributeLocation,
                isOpenTarget));
        }
        return result.ToImmutable();
    }

    private static void ValidateCompanion(
        Compilation compilation,
        ResolvedCompanion companion,
        ContractClauseSymbols clauses,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken) {
        if (!CompanionTypeMatches(companion)) {
            diagnostics.Add(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.InvalidCompanionType,
                companion.AttributeLocation,
                companion.Companion.Name,
                companion.Target.Name));
            return;
        }

        var targetMethods = companion.Target.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.MethodKind == MethodKind.Ordinary &&
                !method.IsImplicitlyDeclared)
            .OrderBy(
                static method => method,
                MethodDeterministicComparer.Instance)
            .ToImmutableArray();
        var companionMethods = companion.Companion.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.MethodKind == MethodKind.Ordinary &&
                !method.IsImplicitlyDeclared)
            .OrderBy(
                static method => method,
                MethodDeterministicComparer.Instance)
            .ToImmutableArray();
        var matchesByTarget = new Dictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>>(
            SymbolEqualityComparer.Default);
        var matchesByCompanion = new Dictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>>(
            SymbolEqualityComparer.Default);

        foreach (var target in targetMethods)
            matchesByTarget.Add(
                target,
                [.. companionMethods.Where(candidate =>
                    SignaturesMatch(target, candidate))]);
        foreach (var candidate in companionMethods)
            matchesByCompanion.Add(
                candidate,
                [.. targetMethods.Where(target =>
                    SignaturesMatch(target, candidate))]);

        var diagnosedCandidates = new HashSet<IMethodSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var target in targetMethods) {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = matchesByTarget[target];
            if (matches.Length > 1) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.AmbiguousMember,
                    GetSourceLocation(target, companion.AttributeLocation),
                    target.Name));
                foreach (var candidate in matches)
                    diagnosedCandidates.Add(candidate);
                continue;
            }
            if (matches.Length == 1) continue;

            var mismatches = companionMethods
                .Where(candidate =>
                    string.Equals(
                        candidate.Name,
                        target.Name,
                        StringComparison.Ordinal) &&
                    matchesByCompanion[candidate].IsDefaultOrEmpty)
                .ToImmutableArray();
            if (mismatches.IsDefaultOrEmpty) {
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.MissingMember,
                    GetSourceLocation(target, companion.AttributeLocation),
                    target.Name,
                    companion.Companion.Name));
            }
            else {
                foreach (var mismatch in mismatches)
                    if (diagnosedCandidates.Add(mismatch))
                        diagnostics.Add(Diagnostic.Create(
                            GeneratedDiagnosticDescriptors.SignatureMismatch,
                            GetSourceLocation(
                                mismatch,
                                companion.AttributeLocation),
                            mismatch.Name));
            }
        }

        foreach (var candidate in companionMethods) {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = matchesByCompanion[candidate];
            if (matches.Length > 1 &&
                diagnosedCandidates.Add(candidate))
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.AmbiguousMember,
                    GetSourceLocation(candidate, companion.AttributeLocation),
                    candidate.Name));
            else if (matches.IsDefaultOrEmpty &&
                     targetMethods.Any(target =>
                         string.Equals(
                             target.Name,
                             candidate.Name,
                             StringComparison.Ordinal)) &&
                     diagnosedCandidates.Add(candidate))
                diagnostics.Add(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SignatureMismatch,
                    GetSourceLocation(candidate, companion.AttributeLocation),
                    candidate.Name));
        }

        foreach (var target in targetMethods) {
            var matches = matchesByTarget[target];
            if (matches.Length != 1) continue;
            var candidate = matches[0];
            if (matchesByCompanion[candidate].Length != 1) continue;
            ValidateBody(
                compilation,
                NormalizePartialMethod(candidate),
                clauses,
                diagnostics,
                companion.AttributeLocation,
                cancellationToken);
        }
    }

    private static bool CompanionTypeMatches(ResolvedCompanion companion) {
        if (companion.Companion.TypeKind != TypeKind.Class ||
            !companion.Companion.IsStatic)
            return false;
        if (!companion.IsOpenTarget)
            return companion.Companion.Arity == 0;
        if (companion.Companion.Arity != companion.Target.Arity)
            return false;
        for (var index = 0; index < companion.Target.TypeParameters.Length; index++)
            if (!TypeParameterConstraintsMatch(
                    companion.Target.TypeParameters[index],
                    companion.Companion.TypeParameters[index]))
                return false;
        return true;
    }

    private static bool SignaturesMatch(
        IMethodSymbol target,
        IMethodSymbol companion) {
        if (!string.Equals(
                target.Name,
                companion.Name,
                StringComparison.Ordinal) ||
            !companion.IsStatic ||
            companion.Arity != target.Arity ||
            companion.ReturnsByRef != target.ReturnsByRef ||
            companion.ReturnsByRefReadonly != target.ReturnsByRefReadonly ||
            !TypesMatch(target.ReturnType, companion.ReturnType))
            return false;
        var receiverOffset = target.IsStatic ? 0 : 1;
        if (companion.Parameters.Length !=
            target.Parameters.Length + receiverOffset)
            return false;
        if (!target.IsStatic) {
            var receiver = companion.Parameters[0];
            if (receiver.RefKind != RefKind.None ||
                receiver.ScopedKind != ScopedKind.None ||
                receiver.IsParams ||
                receiver.IsOptional ||
                !TypesMatch(
                    target.ContainingType.WithNullableAnnotation(
                        NullableAnnotation.NotAnnotated),
                    receiver.Type,
                    normalizeMappedTypeParameters: true))
                return false;
        }
        for (var index = 0; index < target.Parameters.Length; index++) {
            var left = target.Parameters[index];
            var right = companion.Parameters[index + receiverOffset];
            if (left.RefKind != right.RefKind ||
                left.ScopedKind != right.ScopedKind ||
                left.IsParams != right.IsParams ||
                left.IsOptional != right.IsOptional ||
                left.HasExplicitDefaultValue != right.HasExplicitDefaultValue ||
                left.HasExplicitDefaultValue &&
                !Equals(left.ExplicitDefaultValue, right.ExplicitDefaultValue) ||
                !TypesMatch(left.Type, right.Type))
                return false;
        }
        for (var index = 0; index < target.TypeParameters.Length; index++)
            if (!TypeParameterConstraintsMatch(
                    target.TypeParameters[index],
                    companion.TypeParameters[index]))
                return false;
        return true;
    }

    private static bool TypeParameterConstraintsMatch(
        ITypeParameterSymbol left,
        ITypeParameterSymbol right) {
        if (left.HasConstructorConstraint != right.HasConstructorConstraint ||
            left.HasReferenceTypeConstraint != right.HasReferenceTypeConstraint ||
            left.ReferenceTypeConstraintNullableAnnotation !=
            right.ReferenceTypeConstraintNullableAnnotation ||
            left.HasValueTypeConstraint != right.HasValueTypeConstraint ||
            left.HasNotNullConstraint != right.HasNotNullConstraint ||
            left.HasUnmanagedTypeConstraint != right.HasUnmanagedTypeConstraint ||
            left.AllowsRefLikeType != right.AllowsRefLikeType ||
            left.ConstraintTypes.Length != right.ConstraintTypes.Length)
            return false;
        var matched = new bool[right.ConstraintTypes.Length];
        foreach (var leftConstraint in left.ConstraintTypes) {
            var found = false;
            for (var index = 0; index < right.ConstraintTypes.Length; index++) {
                if (matched[index] ||
                    !TypesMatch(leftConstraint, right.ConstraintTypes[index]))
                    continue;
                matched[index] = true;
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }

    private static bool TypesMatch(
        ITypeSymbol left,
        ITypeSymbol right,
        bool normalizeMappedTypeParameters = false) {
        var leftAnnotation =
            normalizeMappedTypeParameters &&
            left is ITypeParameterSymbol &&
            left.NullableAnnotation == NullableAnnotation.None
                ? NullableAnnotation.NotAnnotated
                : left.NullableAnnotation;
        var rightAnnotation =
            normalizeMappedTypeParameters &&
            right is ITypeParameterSymbol &&
            right.NullableAnnotation == NullableAnnotation.None
                ? NullableAnnotation.NotAnnotated
                : right.NullableAnnotation;
        if (leftAnnotation != rightAnnotation ||
            left.TypeKind != right.TypeKind)
            return false;
        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter)
            return leftParameter.TypeParameterKind ==
                   rightParameter.TypeParameterKind &&
                   leftParameter.Ordinal == rightParameter.Ordinal;
        if (left is IArrayTypeSymbol leftArray &&
            right is IArrayTypeSymbol rightArray)
            return leftArray.Rank == rightArray.Rank &&
                   leftArray.IsSZArray == rightArray.IsSZArray &&
                   TypesMatch(
                       leftArray.ElementType,
                       rightArray.ElementType,
                       normalizeMappedTypeParameters);
        if (left is IPointerTypeSymbol leftPointer &&
            right is IPointerTypeSymbol rightPointer)
            return TypesMatch(
                leftPointer.PointedAtType,
                rightPointer.PointedAtType,
                normalizeMappedTypeParameters);
        if (left is INamedTypeSymbol leftNamed &&
            right is INamedTypeSymbol rightNamed) {
            if (!SymbolEqualityComparer.Default.Equals(
                    leftNamed.OriginalDefinition,
                    rightNamed.OriginalDefinition) ||
                leftNamed.TypeArguments.Length != rightNamed.TypeArguments.Length ||
                leftNamed.IsTupleType != rightNamed.IsTupleType)
                return false;
            for (var index = 0; index < leftNamed.TypeArguments.Length; index++)
                if (!TypesMatch(
                        leftNamed.TypeArguments[index],
                        rightNamed.TypeArguments[index],
                        normalizeMappedTypeParameters))
                    return false;
            if (leftNamed.IsTupleType) {
                if (leftNamed.TupleElements.Length != rightNamed.TupleElements.Length)
                    return false;
                for (var index = 0; index < leftNamed.TupleElements.Length; index++)
                    if (!string.Equals(
                            leftNamed.TupleElements[index].Name,
                            rightNamed.TupleElements[index].Name,
                            StringComparison.Ordinal))
                        return false;
            }
            return true;
        }
        return SymbolEqualityComparer.IncludeNullability.Equals(left, right);
    }

    private static void ValidateBody(
        Compilation compilation,
        IMethodSymbol method,
        ContractClauseSymbols clauses,
        List<Diagnostic> diagnostics,
        Location fallback,
        CancellationToken cancellationToken) {
        var body = GetOperationRoot(compilation, method, cancellationToken);
        if (body == null) {
            diagnostics.Add(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.BodyRequired,
                GetSourceLocation(method, fallback),
                method.Name));
            return;
        }
        if (!clauses.IsAvailable) return;
        ValidateClauseOwnership(
            body,
            method,
            clauses,
            diagnostics,
            cancellationToken,
            nested: false);
    }

    private static IOperation? GetOperationRoot(
        Compilation compilation,
        IMethodSymbol method,
        CancellationToken cancellationToken) {
        if (method.IsAbstract || method.IsExtern)
            return null;
        foreach (var reference in method.DeclaringSyntaxReferences
                     .OrderBy(
                         static syntaxReference =>
                             syntaxReference.SyntaxTree.FilePath,
                         StringComparer.Ordinal)
                     .ThenBy(static syntaxReference =>
                         syntaxReference.Span.Start)) {
            cancellationToken.ThrowIfCancellationRequested();
            var syntax = reference.GetSyntax(cancellationToken);
            var model =
                SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(compilation, syntax.SyntaxTree);
            var operation = model.GetOperation(syntax, cancellationToken);
            if (operation is IMethodBodyOperation or IBlockOperation)
                return operation;
            foreach (var child in syntax.ChildNodes()) {
                operation = model.GetOperation(child, cancellationToken);
                if (operation is IMethodBodyOperation or IBlockOperation)
                    return operation;
            }
        }
        return null;
    }

    private static void ValidateClauseOwnership(
        IOperation operation,
        IMethodSymbol method,
        ContractClauseSymbols clauses,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken,
        bool nested) {
        cancellationToken.ThrowIfCancellationRequested();
        var nowNested = nested ||
                        operation is IAnonymousFunctionOperation or
                        ILocalFunctionOperation;
        if (nowNested &&
            operation is IInvocationOperation invocation &&
            clauses.TryGetClauseName(invocation.TargetMethod) is { } clauseName)
            diagnostics.Add(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.NestedClause,
                invocation.Syntax.GetLocation(),
                clauseName,
                method.Name));
        foreach (var child in operation.ChildOperations)
            ValidateClauseOwnership(
                child,
                method,
                clauses,
                diagnostics,
                cancellationToken,
                nowNested);
    }

    private static IMethodSymbol NormalizePartialMethod(IMethodSymbol method) =>
        method.PartialImplementationPart ?? method;

    private static Location GetAttributeLocation(
        AttributeData attribute,
        CancellationToken cancellationToken,
        Location fallback) =>
        attribute.ApplicationSyntaxReference?
            .GetSyntax(cancellationToken)
            .GetLocation() ?? fallback;

    private static Location GetSourceLocation(
        ISymbol symbol,
        Location fallback) =>
        symbol.Locations
            .Where(static location => location.IsInSource)
            .OrderBy(
                static location => location.SourceTree?.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static location => location.SourceSpan.Start)
            .FirstOrDefault() ?? fallback;
}

internal sealed class ResolvedCompanion(
    INamedTypeSymbol companion,
    INamedTypeSymbol target,
    Location attributeLocation,
    bool isOpenTarget) {
    internal INamedTypeSymbol Companion { get; } = companion;
    internal INamedTypeSymbol Target { get; } = target;
    internal Location AttributeLocation { get; } = attributeLocation;
    internal bool IsOpenTarget { get; } = isOpenTarget;
}

internal sealed class ContractClauseSymbols(
    IMethodSymbol? requires,
    IMethodSymbol? ensures) {
    private readonly IMethodSymbol? _requires = requires;
    private readonly IMethodSymbol? _ensures = ensures;

    internal bool IsAvailable => _requires != null && _ensures != null;

    internal static ContractClauseSymbols Resolve(Compilation compilation) {
        var contract = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.Contract");
        return new ContractClauseSymbols(
            ResolveClause(contract, "Requires", compilation),
            ResolveClause(contract, "Ensures", compilation));
    }

    internal string? TryGetClauseName(IMethodSymbol method) {
        var original = method.OriginalDefinition;
        if (_requires != null &&
            SymbolEqualityComparer.Default.Equals(
                original,
                _requires.OriginalDefinition))
            return "Requires";
        if (_ensures != null &&
            SymbolEqualityComparer.Default.Equals(
                original,
                _ensures.OriginalDefinition))
            return "Ensures";
        return null;
    }

    private static IMethodSymbol? ResolveClause(
        INamedTypeSymbol? contract,
        string name,
        Compilation compilation) {
        var boolean = compilation.GetSpecialType(
            SpecialType.System_Boolean);
        return contract?.GetMembers(name)
            .OfType<IMethodSymbol>()
            .SingleOrDefault(method =>
                method.IsStatic &&
                method.ReturnsVoid &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(
                    method.Parameters[0].Type,
                    boolean));
    }
}

internal sealed class NamedTypeDeterministicComparer :
    IComparer<INamedTypeSymbol> {
    internal static NamedTypeDeterministicComparer Instance { get; } = new();

    private NamedTypeDeterministicComparer() {
    }

    public int Compare(INamedTypeSymbol? left, INamedTypeSymbol? right) {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        var result = string.Compare(
            left.ContainingAssembly?.Identity.Name,
            right.ContainingAssembly?.Identity.Name,
            StringComparison.Ordinal);
        if (result != 0) return result;
        result = CompareNamespace(
            left.ContainingNamespace,
            right.ContainingNamespace);
        if (result != 0) return result;
        result = CompareContainingType(
            left.ContainingType,
            right.ContainingType);
        if (result != 0) return result;
        result = string.Compare(
            left.MetadataName,
            right.MetadataName,
            StringComparison.Ordinal);
        if (result != 0) return result;
        result = left.TypeArguments.Length.CompareTo(
            right.TypeArguments.Length);
        if (result != 0) return result;
        for (var index = 0; index < left.TypeArguments.Length; index++) {
            result = CompareType(
                left.TypeArguments[index],
                right.TypeArguments[index]);
            if (result != 0) return result;
        }
        return CompareLocation(left, right);
    }

    internal static int CompareType(ITypeSymbol left, ITypeSymbol right) {
        var result = left.TypeKind.CompareTo(right.TypeKind);
        if (result != 0) return result;
        result = left.NullableAnnotation.CompareTo(right.NullableAnnotation);
        if (result != 0) return result;
        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter) {
            result = leftParameter.TypeParameterKind.CompareTo(
                rightParameter.TypeParameterKind);
            return result != 0
                ? result
                : leftParameter.Ordinal.CompareTo(rightParameter.Ordinal);
        }
        if (left is IArrayTypeSymbol leftArray &&
            right is IArrayTypeSymbol rightArray) {
            result = leftArray.Rank.CompareTo(rightArray.Rank);
            return result != 0
                ? result
                : CompareType(
                    leftArray.ElementType,
                    rightArray.ElementType);
        }
        if (left is INamedTypeSymbol leftNamed &&
            right is INamedTypeSymbol rightNamed)
            return Instance.Compare(leftNamed, rightNamed);
        return string.Compare(
            left.MetadataName,
            right.MetadataName,
            StringComparison.Ordinal);
    }

    private static int CompareNamespace(
        INamespaceSymbol? left,
        INamespaceSymbol? right) {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        var result = CompareNamespace(
            left.ContainingNamespace,
            right.ContainingNamespace);
        return result != 0
            ? result
            : string.Compare(
                left.MetadataName,
                right.MetadataName,
                StringComparison.Ordinal);
    }

    private static int CompareContainingType(
        INamedTypeSymbol? left,
        INamedTypeSymbol? right) {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        var result = CompareContainingType(
            left.ContainingType,
            right.ContainingType);
        if (result != 0) return result;
        result = string.Compare(
            left.MetadataName,
            right.MetadataName,
            StringComparison.Ordinal);
        return result != 0
            ? result
            : left.Arity.CompareTo(right.Arity);
    }

    private static int CompareLocation(ISymbol left, ISymbol right) {
        var leftLocation = left.Locations.FirstOrDefault(
            static location => location.IsInSource);
        var rightLocation = right.Locations.FirstOrDefault(
            static location => location.IsInSource);
        var result = string.Compare(
            leftLocation?.SourceTree?.FilePath,
            rightLocation?.SourceTree?.FilePath,
            StringComparison.Ordinal);
        return result != 0
            ? result
            : (leftLocation?.SourceSpan.Start ?? -1)
                .CompareTo(rightLocation?.SourceSpan.Start ?? -1);
    }
}

internal sealed class MethodDeterministicComparer :
    IComparer<IMethodSymbol> {
    internal static MethodDeterministicComparer Instance { get; } = new();

    private MethodDeterministicComparer() {
    }

    public int Compare(IMethodSymbol? left, IMethodSymbol? right) {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        var result = string.Compare(
            left.MetadataName,
            right.MetadataName,
            StringComparison.Ordinal);
        if (result != 0) return result;
        result = left.Arity.CompareTo(right.Arity);
        if (result != 0) return result;
        result = left.Parameters.Length.CompareTo(right.Parameters.Length);
        if (result != 0) return result;
        for (var index = 0; index < left.Parameters.Length; index++) {
            result = left.Parameters[index].RefKind.CompareTo(
                right.Parameters[index].RefKind);
            if (result != 0) return result;
            result = NamedTypeDeterministicComparer.CompareType(
                left.Parameters[index].Type,
                right.Parameters[index].Type);
            if (result != 0) return result;
        }
        var leftLocation = left.Locations.FirstOrDefault(
            static location => location.IsInSource);
        var rightLocation = right.Locations.FirstOrDefault(
            static location => location.IsInSource);
        result = string.Compare(
            leftLocation?.SourceTree?.FilePath,
            rightLocation?.SourceTree?.FilePath,
            StringComparison.Ordinal);
        return result != 0
            ? result
            : (leftLocation?.SourceSpan.Start ?? -1)
                .CompareTo(rightLocation?.SourceSpan.Start ?? -1);
    }
}
