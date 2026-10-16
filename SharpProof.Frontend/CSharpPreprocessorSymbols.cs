using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

internal static class CSharpPreprocessorSymbols
{
    internal static ImmutableHashSet<string> GetDefined(
        SyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        tree = ArgumentNullGuard.NotNull(tree, nameof(tree));

        if (tree.Options is not CSharpParseOptions options)
        {
            return ImmutableHashSet<string>.Empty;
        }

        var defined = options.PreprocessorSymbolNames
            .ToImmutableHashSet(StringComparer.Ordinal)
            .ToBuilder();
        // C# permits #define and #undef only before the first token, so the
        // first token's leading trivia is the complete directive inventory.
        // Avoid walking every trivia node in an otherwise unannotated build.
        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } define:
                    defined.Add(define.Name.ValueText);
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undef:
                    defined.Remove(undef.Name.ValueText);
                    break;
            }
        }

        return defined.ToImmutable();
    }

    internal static bool IsDefined(
        SyntaxTree tree,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "A preprocessor symbol is required.",
                nameof(symbol));
        }

        tree = ArgumentNullGuard.NotNull(tree, nameof(tree));
        if (tree.Options is not CSharpParseOptions options)
        {
            return false;
        }

        var defined = options.PreprocessorSymbolNames.Contains(symbol);
        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } define
                    when string.Equals(
                        define.Name.ValueText,
                        symbol,
                        StringComparison.Ordinal):
                    defined = true;
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undef
                    when string.Equals(
                        undef.Name.ValueText,
                        symbol,
                        StringComparison.Ordinal):
                    defined = false;
                    break;
            }
        }

        return defined;
    }
}

internal sealed class CSharpInvocationEmissionPolicy(Compilation compilation)
{
    // One policy is shared by every concurrent analyzer callback on a
    // compilation session, so the caches are guarded.  Values are pure
    // functions of their keys: compute outside the gate and let the last
    // writer store the identical result.
    private readonly object _cacheGate = new();
    private readonly Dictionary<SyntaxTree, ImmutableHashSet<string>>
        _definedPreprocessorSymbols = [];
    private readonly Dictionary<IMethodSymbol, bool>
        _unimplementedPartials = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, ImmutableArray<string>>
        _conditionalSymbols = new(SymbolEqualityComparer.Default);

    internal bool IsElided(IOperation operation, bool requireOwningInvocation = false)
    {
        ArgumentNullGuard.NotNull(operation, nameof(operation));
        var sourceSyntax = (operation is IExpressionStatementOperation statement ? statement.Operation : operation).Syntax;
        if (OwningInvocation(operation) is not { } invocation)
        { return false; }
        // A collection initializer can introduce a nested statement call in
        // an omitted argument. Only the recovered owning invocation ends
        // that argument subgraph during compiler-elision continuation scans.
        if (requireOwningInvocation && (sourceSyntax.SyntaxTree != invocation.Syntax.SyntaxTree ||
            sourceSyntax.Span != invocation.Syntax.Span))
        { return false; }
        var target = invocation.TargetMethod.ReducedFrom ??
            invocation.TargetMethod;
        var isUnimplementedPartial = GetOrAdd(
            _unimplementedPartials,
            target,
            IsUnimplementedPartial);
        if (isUnimplementedPartial)
        {
            return true;
        }

        if (!target.IsConditional ||
            invocation.Syntax.SyntaxTree.Options is not CSharpParseOptions)
        {
            return false;
        }
        var conditionalSymbols = GetOrAdd(
            _conditionalSymbols,
            target,
            GetConditionalSymbols);
        if (conditionalSymbols.IsDefaultOrEmpty)
        {
            return false;
        }
        var definedSymbols = GetOrAdd(
            _definedPreprocessorSymbols,
            invocation.Syntax.SyntaxTree,
            static tree => CSharpPreprocessorSymbols.GetDefined(tree));
        return conditionalSymbols.All(symbol =>
            !definedSymbols.Contains(symbol));
    }

    internal IInvocationOperation? OwningInvocation(IOperation operation)
    {
        ArgumentNullGuard.NotNull(operation, nameof(operation));
        if (operation is IExpressionStatementOperation statement)
        { operation = statement.Operation; }
        // CFG lowering separates receivers and branching arguments from their
        // invocation. Recover the enclosing source call so they are omitted
        // together, without crossing into an enclosing callable's body.
        var sourceSyntax = operation.Syntax;
        SyntaxNode? callSyntax = null;
        for (var syntax = sourceSyntax; syntax != null; syntax = syntax.Parent)
        {
            if (syntax is ConditionalAccessExpressionSyntax or InvocationExpressionSyntax)
            {
                callSyntax = syntax;
            }
            // A lowered delegate creation can use the lambda's own syntax;
            // only operations inside its body establish a callable boundary.
            if (syntax is StatementSyntax or MemberDeclarationSyntax ||
                syntax is AnonymousFunctionExpressionSyntax &&
                !(ReferenceEquals(syntax, operation.Syntax) &&
                    operation is IDelegateCreationOperation or IFlowCaptureOperation or
                        IFlowAnonymousFunctionOperation))
            {
                break;
            }
        }
        if (callSyntax != null &&
            (!ReferenceEquals(callSyntax, operation.Syntax) ||
                operation is not (IInvocationOperation or IConditionalAccessOperation)) &&
            SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
                compilation, callSyntax.SyntaxTree).GetOperation(callSyntax)
                is { } sourceCall)
        {
            operation = sourceCall;
        }
        while (operation is IConditionalAccessOperation conditional)
        {
            operation = conditional.WhenNotNull;
        }
        if (operation is not IInvocationOperation invocation)
        {
            return null;
        }
        return invocation;
    }

    private ImmutableArray<string> GetConditionalSymbols(IMethodSymbol method)
    {
        var symbols = ImmutableArray.CreateBuilder<string>();
        for (IMethodSymbol? current = method; current != null; current = current.OverriddenMethod)
        {
            var declaration = ConditionalDeclaration(current);
            if (declaration.DeclaringSyntaxReferences.Length != 0)
            {
                foreach (var attribute in declaration.GetAttributes())
                {
                    if (attribute.AttributeClass is not
                        { Name: "ConditionalAttribute", ContainingSymbol: INamespaceSymbol owner } ||
                        owner.Name != "Diagnostics" || owner.ContainingNamespace is not
                        { Name: "System", ContainingNamespace.IsGlobalNamespace: true } ||
                        attribute.AttributeConstructor == null ||
                        attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax ||
                        syntax.ArgumentList?.Arguments.Count(argument => argument.NameEquals == null) != 1 ||
                        attribute.ConstructorArguments.IsDefaultOrEmpty)
                    { continue; }
                    // Source early decoding counts supplied arguments, even when
                    // the bound constructor has object or optional parameters.
                    var argument = attribute.ConstructorArguments[0];
                    if (argument.Kind == TypedConstantKind.Primitive &&
                        argument.Type?.SpecialType == SpecialType.System_String && argument.Value is string symbol)
                    { symbols.Add(symbol); }
                }
            }
            else
            {
                AddMetadataConditionalSymbols(declaration, symbols);
            }
        }
        return symbols.ToImmutable();
    }

    private IMethodSymbol ConditionalDeclaration(IMethodSymbol method)
    {
        var declaration = method.OriginalDefinition;
        if (declaration.ContainingType.NativeIntegerUnderlyingType is not { } underlying)
        { return declaration; }
        if (compilation is not CSharpCompilation csharp)
        { throw new InvalidOperationException("Native conditional method compilation is unavailable."); }
        // Native integer wrappers preserve conditional decoding but expose neither
        // their underlying attributes nor their metadata token. Recover the actual
        // declaring member before choosing source or metadata attribute decoding.
        var matches = underlying.GetMembers(declaration.Name).OfType<IMethodSymbol>().Where(Matches).ToArray();
        if (matches.Length != 1)
        { throw new InvalidOperationException("Native conditional method declaration is not unique."); }
        return matches[0].OriginalDefinition;

        bool Matches(IMethodSymbol candidate)
        {
            if (candidate.MethodKind != declaration.MethodKind || candidate.IsStatic != declaration.IsStatic ||
                candidate.Arity != declaration.Arity || candidate.IsVararg != declaration.IsVararg ||
                candidate.RefKind != declaration.RefKind || candidate.Parameters.Length != declaration.Parameters.Length ||
                !SymbolEqualityComparer.Default.Equals(candidate.ContainingModule, declaration.ContainingModule) ||
                !SameType(candidate.ReturnType, declaration.ReturnType) ||
                !SameModifiers(candidate.ReturnTypeCustomModifiers, declaration.ReturnTypeCustomModifiers) ||
                !SameModifiers(candidate.RefCustomModifiers, declaration.RefCustomModifiers))
            { return false; }
            for (var ordinal = 0; ordinal < candidate.Parameters.Length; ordinal++)
            {
                var actual = candidate.Parameters[ordinal];
                var wrapped = declaration.Parameters[ordinal];
                if (actual.RefKind != wrapped.RefKind || !SameType(actual.Type, wrapped.Type) ||
                    !SameModifiers(actual.CustomModifiers, wrapped.CustomModifiers) ||
                    !SameModifiers(actual.RefCustomModifiers, wrapped.RefCustomModifiers))
                { return false; }
            }
            return true;
        }

        bool SameType(ITypeSymbol left, ITypeSymbol right)
        { return csharp.ClassifyConversion(left, right).IsIdentity && csharp.ClassifyConversion(right, left).IsIdentity; }

        static bool SameModifiers(ImmutableArray<CustomModifier> left, ImmutableArray<CustomModifier> right)
        {
            if (left.Length != right.Length)
            { return false; }
            for (var ordinal = 0; ordinal < left.Length; ordinal++)
            {
                if (left[ordinal].IsOptional != right[ordinal].IsOptional ||
                    !SymbolEqualityComparer.Default.Equals(left[ordinal].Modifier, right[ordinal].Modifier))
                { return false; }
            }
            return true;
        }
    }

    private static void AddMetadataConditionalSymbols(IMethodSymbol method, ImmutableArray<string>.Builder symbols)
    {
        // Borrow the original declaring module; no compilation-owned metadata
        // is disposed. Unavailable authority must propagate, not guess emission.
        var metadata = method.ContainingModule.GetMetadata() ??
            throw new InvalidOperationException("Conditional method metadata is unavailable.");
        var reader = metadata.GetMetadataReader();
        var entity = MetadataTokens.Handle(method.MetadataToken);
        if (entity.Kind != HandleKind.MethodDefinition)
        { throw new BadImageFormatException("Conditional method token is not a MethodDef."); }
        var handle = (MethodDefinitionHandle)entity;
        var row = MetadataTokens.GetRowNumber(handle);
        if (row <= 0 || row > reader.MethodDefinitions.Count)
        { throw new BadImageFormatException("Conditional method token is outside its module."); }
        try
        {
            foreach (var attributeHandle in reader.GetMethodDefinition(handle).GetCustomAttributes())
            {
                try
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);
                    if (IsMetadataConditionalConstructor(reader, attribute) &&
                        ReadMetadataConditionalSymbol(reader, attribute.Value) is { } symbol)
                    { symbols.Add(symbol); }
                }
                catch (BadImageFormatException)
                { }
            }
        }
        catch (BadImageFormatException)
        { }
    }

    private static bool IsMetadataConditionalConstructor(MetadataReader reader, CustomAttribute attribute)
    {
        var constructor = attribute.Constructor;
        EntityHandle owner;
        StringHandle name;
        BlobHandle signature;
        if (constructor.Kind == HandleKind.MethodDefinition)
        {
            var definition = reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
            owner = definition.GetDeclaringType();
            name = definition.Name;
            signature = definition.Signature;
        }
        else
        {
            var reference = reader.GetMemberReference((MemberReferenceHandle)constructor);
            owner = reference.Parent;
            name = reference.Name;
            signature = reference.Signature;
        }
        if (reader.GetString(name) != ".ctor" || !IsMetadataConditionalType(reader, owner))
        { return false; }
        var blob = reader.GetBlobReader(signature);
        return blob.ReadByte() == 0x20 && blob.ReadByte() == 0x01 &&
            blob.ReadByte() == 0x01 &&
            blob.ReadSignatureTypeCode() == SignatureTypeCode.String && blob.RemainingBytes == 0;
    }

    private static bool IsMetadataConditionalType(MetadataReader reader, EntityHandle owner)
    {
        StringHandle name;
        StringHandle namespaceName;
        switch (owner.Kind)
        {
            case HandleKind.TypeDefinition:
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)owner);
                if ((definition.Attributes & TypeAttributes.VisibilityMask) is not (TypeAttributes.NotPublic or TypeAttributes.Public))
                { return false; }
                name = definition.Name;
                namespaceName = definition.Namespace;
                break;
            case HandleKind.TypeReference:
                var reference = reader.GetTypeReference((TypeReferenceHandle)owner);
                if (reference.ResolutionScope.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference)
                { return false; }
                name = reference.Name;
                namespaceName = reference.Namespace;
                break;
            default:
                return false;
        }
        return reader.GetString(name) == "ConditionalAttribute" &&
            reader.GetString(namespaceName) == "System.Diagnostics";
    }

    private static string? ReadMetadataConditionalSymbol(MetadataReader reader, BlobHandle value)
    {
        if (value.IsNil)
        { return null; }
        var blob = reader.GetBlobReader(value);
        if (blob.Length <= 4 || blob.ReadByte() != 1 || blob.ReadByte() != 0 ||
            !blob.TryReadCompressedInteger(out var length) || blob.RemainingBytes < length)
        { return null; }
        // Match compiler string extraction; named/trailing data is separate.
        return blob.ReadUTF8(length).TrimEnd('\0');
    }

    private TValue GetOrAdd<TKey, TValue>(
        Dictionary<TKey, TValue> cache,
        TKey key,
        Func<TKey, TValue> create)
        where TKey : notnull
    {
        lock (_cacheGate)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var value = create(key);
        lock (_cacheGate)
        {
            cache[key] = value;
        }

        return value;
    }

    internal static bool IsUnimplementedPartial(IMethodSymbol method)
    {
        return method.PartialDefinitionPart == null &&
            method.PartialImplementationPart == null &&
            method.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is MethodDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                declaration.Body == null &&
                declaration.ExpressionBody == null);
    }
}
