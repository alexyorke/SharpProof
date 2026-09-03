// Semantic claim identities are sealed by the build-time collector.
namespace SharpProof.CompilerArtifact;
internal static partial class SemanticClaimIdentity
{
    private const string ClaimDomain = "SharpProofClaim/v1";
    private const string AssumptionDomain = "SharpProofAssumption/v1";
    private const string FingerprintDomain = "SharpProofPredicate/v1";

    internal static string Create(
        string assemblyName, string callableId, string predicateFingerprint, int duplicateRank)
    {
        return CreateEvidenceId(
            "spc1:", ClaimDomain, assemblyName, callableId, "postcondition", predicateFingerprint, duplicateRank);
    }

    internal static string CreateAssumption(
        string assemblyName, string callableId, WorkerAssumptionKind kind, string evidenceFingerprint, int duplicateRank)
    {
        return CreateEvidenceId("spa1:", AssumptionDomain, assemblyName, callableId,
            kind.ToString(), evidenceFingerprint, duplicateRank);
    }

    private static string CreateEvidenceId(
        string prefix, string domain, string assemblyName, string callableId,
        string kind, string fingerprint, int duplicateRank)
    {
        duplicateRank = ArgumentNullGuard.RequireNonnegative(
            duplicateRank, nameof(duplicateRank));
        using var writer = new CanonicalHashWriter();
        writer.Add(domain).Add(assemblyName).Add(callableId).Add(kind)
            .Add(fingerprint).Add(duplicateRank);
        return prefix + writer.Finish();
    }

    internal static string CreateInvocationFingerprint(
        IInvocationOperation invocation, IMethodSymbol target, IMethodSymbol source, bool usesCompanion)
    {
        invocation = ArgumentNullGuard.NotNull(invocation, nameof(invocation));

        if (invocation.Arguments.Length != 1)
        {
            return CreateMalformedInvocationFingerprint(invocation, target, source);
        }

        var context = new ClaimIdentityContext(target, source, usesCompanion);
        return CreateOperationFingerprint(invocation.Arguments[0].Value, context);
    }

    internal static string CreateAttributeFingerprint(
        AttributeData attribute, IMethodSymbol target, IParameterSymbol? parameter = null)
    {
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));

        var context = new ClaimIdentityContext(target, target, false);
        using var writer = new CanonicalHashWriter();
        writer.Add(FingerprintDomain).Add(parameter == null ? "return-attribute" : "parameter-attribute");
        if (parameter != null)
        {
            writer.Add(parameter.Ordinal);
        }

        WriteType(writer, parameter?.Type ?? target.ReturnType, context);
        WriteType(writer, attribute.AttributeClass, context);
        WriteAttributeArguments(writer, attribute, context, includeNamed: true);
        return writer.Finish();
    }

    internal static string CreateTrustedFingerprint(AttributeData attribute, ISymbol scope, IMethodSymbol target)
    {
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        scope = ArgumentNullGuard.NotNull(scope, nameof(scope));

        var context = new ClaimIdentityContext(target, target, false);
        using var writer = new CanonicalHashWriter();
        writer.Add(FingerprintDomain).Add("trusted-boundary");
        WriteSymbol(writer, scope, context);
        WriteType(writer, attribute.AttributeClass, context);
        WriteAttributeArguments(writer, attribute, context, includeNamed: false);
        return writer.Finish();
    }

    internal static string CreateCallableId(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));

        method = NormalizePartial(method).OriginalDefinition;
        var documentationId = DocumentationCommentId.CreateDeclarationId(method);
        if (!string.IsNullOrEmpty(documentationId))
        {
            return documentationId!;
        }

        using var writer = new CanonicalHashWriter();
        writer.Add("SharpProofCallable/v1");
        WriteMethod(writer, method, new ClaimIdentityContext(method, method, false));
        return "spm1:" + writer.Finish();
    }
    internal static string CreateNestedCallableId(
        string parentId, IMethodSymbol method, int siblingOrdinal)
    {
        parentId = ArgumentNullGuard.NotNull(parentId, nameof(parentId));

        if (string.IsNullOrWhiteSpace(parentId))
        {
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", nameof(parentId));
        }

        method = ArgumentNullGuard.NotNull(method, nameof(method));

        siblingOrdinal = ArgumentNullGuard.RequireNonnegative(
            siblingOrdinal, nameof(siblingOrdinal));
        method = NormalizePartial(method).OriginalDefinition;
        using var writer = new CanonicalHashWriter();
        writer.Add("SharpProofCallable/v1").Add(parentId).Add(siblingOrdinal);
        WriteMethod(writer, method, new ClaimIdentityContext(method, method, false));
        return "spm1:" + writer.Finish();
    }
    internal static string CreateContainerId(ISymbol symbol)
    {
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));

        var documentationId = DocumentationCommentId.CreateDeclarationId(symbol);
        if (!string.IsNullOrEmpty(documentationId))
        {
            return documentationId!;
        }

        using var writer = new CanonicalHashWriter();
        writer.Add("SharpProofContainer/v1").Add(symbol.Kind.ToString())
            .Add(symbol.MetadataName)
            .Add(DocumentationCommentId.CreateReferenceId(symbol));
        return "sps1:" + writer.Finish();
    }

    private static string CreateMalformedInvocationFingerprint(
        IInvocationOperation invocation, IMethodSymbol target, IMethodSymbol source)
    {
        var context = new ClaimIdentityContext(target, source, false);
        using var writer = new CanonicalHashWriter();
        writer.Add(FingerprintDomain).Add("malformed-ensures");
        WriteMethod(writer, invocation.TargetMethod, context);
        writer.Add(invocation.Arguments.Length);
        foreach (var argument in invocation.Arguments)
        {
            WriteOperation(writer, argument, context);
        }

        return writer.Finish();
    }

    private static string CreateOperationFingerprint(IOperation operation, ClaimIdentityContext context)
    {
        using var writer = new CanonicalHashWriter();
        writer.Add(FingerprintDomain);
        WriteOperation(writer, operation, context);
        return writer.Finish();
    }

    /// <summary>
    /// Ceiling on operation nesting walked recursively. Nothing this deep can be
    /// verified anyway -- the verifier's hard expression-depth cap is the same
    /// 256 -- so truncating costs no reachable precision, and it keeps a
    /// generated expression from taking the compiler down with an uncatchable
    /// StackOverflowException.
    /// </summary>
    private const int MaximumFingerprintDepth = 256;

    private static void WriteOperation(
        CanonicalHashWriter writer, IOperation operation, ClaimIdentityContext context)
    {
        WriteOperation(writer, operation, context, 0);
    }

    private static void WriteOperation(
        CanonicalHashWriter writer, IOperation operation, ClaimIdentityContext context, int depth)
    {
        if (depth >= MaximumFingerprintDepth)
        {
            // Truncation is deterministic, and claim identity also carries a
            // duplicate rank (ClaimManifestBuilder.NextRank), so two claims whose
            // fingerprints truncate to the same value still receive distinct ids.
            writer.Add("depth-limit");
            return;
        }

        writer.Add(operation.Kind.ToString()).Add(operation.IsImplicit);
        WriteType(writer, operation.Type, context);
        WriteOptionalConstant(writer, operation.ConstantValue, context);
        switch (operation)
        {
            case IParameterReferenceOperation value:
                WriteParameterRole(writer, value.Parameter, context);
                break;
            case ILocalReferenceOperation value:
                writer.Add("local").Add(value.Local.IsConst);
                WriteLocalRole(writer, value.Local);
                WriteType(writer, value.Local.Type, context);
                break;
            case IInstanceReferenceOperation value:
                writer.Add(value.ReferenceKind.ToString());
                break;
            case IInvocationOperation value:
                WriteMethod(writer, value.TargetMethod, context);
                writer.Add(value.IsVirtual);
                break;
            case IObjectCreationOperation value:
                WriteMethod(writer, value.Constructor, context);
                break;
            case IMethodReferenceOperation value:
                WriteMethod(writer, value.Method, context);
                writer.Add(value.IsVirtual);
                break;
            case IMemberReferenceOperation value:
                WriteSymbol(writer, value.Member, context);
                break;
            case IArgumentOperation value:
                writer.Add(value.ArgumentKind.ToString()).Add(value.Parameter?.Ordinal ?? -1)
                    .Add(value.Parameter?.RefKind.ToString() ?? string.Empty);
                break;
            case IConversionOperation value:
                writer.Add(value.Conversion.Exists).Add(value.Conversion.IsIdentity)
                    .Add(value.Conversion.IsNumeric).Add(value.Conversion.IsReference)
                    .Add(value.Conversion.IsUserDefined).Add(value.IsChecked);
                WriteMethod(writer, value.OperatorMethod, context);
                break;
            case IBinaryOperation value:
                writer.Add(value.OperatorKind.ToString()).Add(value.IsLifted).Add(value.IsChecked);
                WriteMethod(writer, value.OperatorMethod, context);
                break;
            case IUnaryOperation value:
                writer.Add(value.OperatorKind.ToString()).Add(value.IsLifted).Add(value.IsChecked);
                WriteMethod(writer, value.OperatorMethod, context);
                break;
            case ITypeOfOperation value:
                WriteType(writer, value.TypeOperand, context);
                break;
            case IIsTypeOperation value:
                WriteType(writer, value.TypeOperand, context);
                writer.Add(value.IsNegated);
                break;
        }
        var children = operation.ChildOperations;
        writer.Add(children.Count);
        foreach (var child in children)
        {
            WriteOperation(writer, child, context, depth + 1);
        }
    }
    private static void WriteLocalRole(CanonicalHashWriter writer, ILocalSymbol local)
    {
        var declaration = local.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax()).FirstOrDefault();
        var owner = (local.ContainingSymbol as IMethodSymbol)?.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .FirstOrDefault(syntax => declaration != null &&
                syntax.SyntaxTree == declaration.SyntaxTree &&
                syntax.Span.Contains(declaration.Span));
        if (declaration == null || owner == null)
        {
            writer.Add(-1);
            return;
        }
        var path = new Stack<int>();
        for (var current = declaration; !HasSameSite(current, owner); current = current.Parent!)
        {
            if (current.Parent == null)
            {
                writer.Add(-1);
                return;
            }
            path.Push(current.Parent.ChildNodes().TakeWhile(
                child => !HasSameSite(child, current)).Count());
        }
        writer.Add(path.Count);
        foreach (var ordinal in path)
        {
            writer.Add(ordinal);
        }
    }

    private static void WriteParameterRole(
        CanonicalHashWriter writer, IParameterSymbol parameter, ClaimIdentityContext context)
    {
        if (SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol.OriginalDefinition, context.Source.OriginalDefinition))
        {
            if (context.UsesCompanion && !context.Target.IsStatic)
            {
                writer.Add(parameter.Ordinal == 0 ? "receiver" : "parameter");
                if (parameter.Ordinal != 0)
                {
                    writer.Add(parameter.Ordinal - 1);
                }
            }
            else
            {
                writer.Add("parameter").Add(parameter.Ordinal);
            }
            writer.Add(parameter.RefKind.ToString());
            WriteType(writer, parameter.Type, context);
            return;
        }
        writer.Add("external-parameter")
            .Add(parameter.Ordinal).Add(parameter.RefKind.ToString());
        WriteSymbol(writer, parameter.ContainingSymbol, context);
    }

    private static void WriteSymbol(CanonicalHashWriter writer, ISymbol? symbol, ClaimIdentityContext context)
    {
        if (symbol == null)
        {
            writer.Add("null-symbol");
            return;
        }
        writer.Add(symbol.Kind.ToString());
        switch (symbol)
        {
            case IMethodSymbol method:
                WriteMethod(writer, method, context);
                return;
            case INamedTypeSymbol type:
                WriteType(writer, type, context);
                return;
            case IPropertySymbol property:
                WriteReferenceId(writer, property);
                WriteType(writer, property.Type, context);
                writer.Add(property.Parameters.Length);
                foreach (var parameter in property.Parameters)
                {
                    writer.Add(parameter.RefKind.ToString());
                    WriteType(writer, parameter.Type, context);
                }
                return;
            case IFieldSymbol field:
                WriteReferenceId(writer, field);
                WriteType(writer, field.Type, context);
                return;
            case IEventSymbol @event:
                WriteReferenceId(writer, @event);
                WriteType(writer, @event.Type, context);
                return;
            case IParameterSymbol parameter:
                WriteParameterRole(writer, parameter, context);
                return;
            default:
                WriteReferenceId(writer, symbol);
                return;
        }
    }

    private static void WriteMethod(CanonicalHashWriter writer, IMethodSymbol? method, ClaimIdentityContext context)
    {
        if (method == null)
        {
            writer.Add("null-method");
            return;
        }
        writer.Add(method.MethodKind.ToString()).Add(method.Arity).Add(method.IsStatic);
        WriteReferenceId(writer, method);
        writer.Add(method.TypeArguments.Length);
        foreach (var argument in method.TypeArguments)
        {
            WriteType(writer, argument, context);
        }

        writer.Add(method.Parameters.Length);
        foreach (var parameter in method.Parameters)
        {
            writer.Add(parameter.RefKind.ToString()).Add(parameter.ScopedKind.ToString());
            WriteType(writer, parameter.Type, context);
        }
        writer.Add(method.RefKind.ToString());
        WriteType(writer, method.ReturnType, context);
    }

    private static void WriteType(CanonicalHashWriter writer, ITypeSymbol? type, ClaimIdentityContext context)
    {
        if (type == null)
        {
            writer.Add("null-type");
            return;
        }
        writer.Add(type.TypeKind.ToString()).Add(type.NullableAnnotation.ToString());
        if (type is ITypeParameterSymbol parameter)
        {
            WriteTypeParameter(writer, parameter, context);
            return;
        }
        writer.Add(DocumentationCommentId.CreateReferenceId(type));
    }

    private static void WriteTypeParameter(
        CanonicalHashWriter writer, ITypeParameterSymbol parameter, ClaimIdentityContext context)
    {
        if (parameter.ContainingSymbol is IMethodSymbol owner &&
            SymbolEqualityComparer.Default.Equals(owner.OriginalDefinition, context.Source.OriginalDefinition))
        {
            writer.Add("method-parameter").Add(parameter.Ordinal);
            return;
        }
        if (parameter.ContainingSymbol is INamedTypeSymbol typeOwner &&
            TryGetContainingTypeDepth(context.Source.ContainingType, typeOwner, out var depth))
        {
            writer.Add("type-parameter").Add(depth).Add(parameter.Ordinal);
            return;
        }
        writer.Add("external-type-parameter").Add(parameter.TypeParameterKind.ToString())
            .Add(parameter.Ordinal)
            .Add(parameter.ContainingSymbol.MetadataName);
    }

    private static bool TryGetContainingTypeDepth(
        INamedTypeSymbol source, INamedTypeSymbol candidate, out int depth)
    {
        depth = 0;
        for (var current = source; current != null; current = current.ContainingType, depth++)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, candidate.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteReferenceId(CanonicalHashWriter writer, ISymbol symbol)
    {
        writer.Add(DocumentationCommentId.CreateReferenceId(symbol));
    }

    private static void WriteOptionalConstant(
        CanonicalHashWriter writer, Optional<object?> constant, ClaimIdentityContext context)
    {
        writer.Add(constant.HasValue);
        if (constant.HasValue)
        {
            WriteConstant(writer, constant.Value, context);
        }
    }

    private static void WriteAttributeArguments(
        CanonicalHashWriter writer, AttributeData attribute, ClaimIdentityContext context, bool includeNamed)
    {
        writer.Add(attribute.ConstructorArguments.Length);
        foreach (var argument in attribute.ConstructorArguments)
        {
            WriteTypedConstant(writer, argument, context);
        }

        if (!includeNamed)
        {
            return;
        }

        writer.Add(attribute.NamedArguments.Length);
        foreach (var item in attribute.NamedArguments.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            writer.Add(item.Key);
            WriteTypedConstant(writer, item.Value, context);
        }
    }

    private static void WriteTypedConstant(
        CanonicalHashWriter writer, TypedConstant constant, ClaimIdentityContext context)
    {
        writer.Add(constant.Kind.ToString());
        WriteType(writer, constant.Type, context);
        if (constant.Kind != TypedConstantKind.Array)
        {
            WriteConstant(writer, constant.Value, context);
        }
        else
        {
            // Roslyn represents an ill-formed array argument with a default
            // Values array.  Treat it as an empty array for identity purposes;
            // fingerprinting must never turn a compiler diagnostic into an
            // exception while walking metadata.
            var values = constant.Values.IsDefault
                ? ImmutableArray<TypedConstant>.Empty
                : constant.Values;
            writer.Add(values.Length);
            foreach (var value in values)
            {
                WriteTypedConstant(writer, value, context);
            }
        }
    }

    private static void WriteConstant(CanonicalHashWriter writer, object? value, ClaimIdentityContext context)
    {
        if (value == null)
        {
            writer.Add("null");
            return;
        }
        var runtimeType = value.GetType();
        writer.Add(runtimeType.FullName ?? runtimeType.Name);
        switch (value)
        {
            case string text:
                writer.Add(Utf16WellFormedness.IsWellFormed(text)
                    ? text
                    : "ill-formed-utf16");
                break;
            case ITypeSymbol type:
                WriteType(writer, type, context);
                break;
            case float number:
                writer.Add(BitConverter.ToInt32(BitConverter.GetBytes(number), 0));
                break;
            case double number:
                writer.Add(BitConverter.DoubleToInt64Bits(number));
                break;
            case decimal number:
                foreach (var part in decimal.GetBits(number))
                {
                    writer.Add(part);
                }

                break;
            case IFormattable formattable:
                writer.Add(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                writer.Add(value.ToString() ?? string.Empty);
                break;
        }
    }

    private static IMethodSymbol NormalizePartial(IMethodSymbol method)
    {
        return method.PartialImplementationPart ?? method;
    }

    internal static IMethodSymbol NormalizeCandidate(IMethodSymbol method)
    {
        method = method.ReducedFrom ?? method;
        return method.PartialImplementationPart ?? method;
    }

    private static bool HasSameSite(SyntaxNode left, SyntaxNode right)
    {
        return SyntaxSite.IsSame(left, right);
    }

}
