using System.Collections.Immutable;
using System.Globalization;
using SharpProof.Frontend;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

// Test infrastructure: the inventory observes the production resolver and intrinsic
// predicates. It never grants support based on an API name alone.
internal static class DotnetApiInventory
{
    internal const string PackVersion = "10.0.13";
    internal const string Framework = "net10.0";
    internal sealed record Row(string Assembly, string Id, string Kind, string Evidence)
    {
        internal string Key => Assembly + "\t" + Id;
        internal string Status => Kind == "NamedType" ? "excluded-container" : Evidence.Length == 0 ? "unmodeled" : "partially-modeled";
        internal string Line => Key + "\t" + Kind + "\t" + Status + "\t" + (Evidence.Length == 0 ? "-" : Evidence);
        public string Assembly { get; init; } = Assembly ?? throw new global::System.ArgumentNullException(nameof(Assembly));
        public string Id { get; init; } = Id ?? throw new global::System.ArgumentNullException(nameof(Id));
        public string Kind { get; init; } = Kind ?? throw new global::System.ArgumentNullException(nameof(Kind));
        public string Evidence { get; init; } = Evidence ?? throw new global::System.ArgumentNullException(nameof(Evidence));
    }

    internal static ImmutableArray<Row> Collect(CSharpCompilation compilation)
    {
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        // Contract ghost APIs belong to SharpProof.Attributes, outside this pack.
        var failures = resolved.Failures.Where(failure => failure is null ? throw new global::System.ArgumentNullException(nameof(failure)) : (!failure.WitnessIdentifier.StartsWith("contract.", StringComparison.Ordinal))).ToImmutableArray();
        if (failures.Length != 0)
        {
            throw new InvalidOperationException("Framework catalog resolution failed: " + string.Join(", ", failures.Select(failure => failure is null ? throw new global::System.ArgumentNullException(nameof(failure)) : (failure.ToString()))));
        }

        return compilation.References.Select(compilation.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>().SelectMany(assembly => assembly is null ? throw new global::System.ArgumentNullException(nameof(assembly)) : (Members(assembly.GlobalNamespace))).Select(symbol => symbol is null ? throw new global::System.ArgumentNullException(nameof(symbol)) : (Describe(symbol, resolved))).OrderBy(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Key), StringComparer.Ordinal).ToImmutableArray();
    }

    internal static IEnumerable<ISymbol> Members(INamespaceOrTypeSymbol owner)
    {
        global::System.ArgumentNullException.ThrowIfNull(owner);
        foreach (var member in owner.GetMembers())
        {
            if (member is INamespaceSymbol space)
            {
                foreach (var child in Members(space))
                {
                    yield return child;
                }

                continue;
            }

            if (member.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            {
                continue;
            }

            // Accessors are accounted for by their property/event. Inherited members
            // are accounted for once, at their declaration. No forwarder duplicates.
            if (member is IMethodSymbol { AssociatedSymbol: not null })
            {
                continue;
            }

            yield return member;
            if (member is INamedTypeSymbol type)
            {
                foreach (var child in Members(type))
                {
                    yield return child;
                }
            }
        }
    }

    private static Row Describe(ISymbol symbol, ResolvedApiSpecTable resolved)
    {
        global::System.ArgumentNullException.ThrowIfNull(resolved);
        var evidence = ImmutableArray.CreateBuilder<string>();
        if (resolved.TryGet(symbol, out var spec))
        {
            var template = spec.Template;
            var facets = template.Facets;
            evidence.Add("catalog=" + template.Target.WitnessIdentifier);
            evidence.Add("effects=" + facets.Effects.Effects);
            evidence.Add("allocation=" + facets.Allocation.Behavior);
            evidence.Add("throws=" + facets.Throws.Behavior);
            evidence.Add("exceptions=" + string.Join(",", facets.Throws.ExceptionMetadataNames.Order(StringComparer.Ordinal)));
            evidence.Add("nullness=" + facets.Nullness.Result);
            evidence.Add("cardinality=" + facets.Cardinality.Result);
            evidence.Add("termination=" + (facets.Termination?.Behavior.ToString() ?? "Unknown"));
            evidence.Add("postconditions=" + template.Postconditions.Length.ToString(CultureInfo.InvariantCulture));
        }

        if (symbol is IPropertySymbol { GetMethod: { } countGetter } && CSharpOperationSemantics.IsNonnegativeCollectionCount(countGetter))
        {
            evidence.Add("intrinsic=concrete-count-nonnegative");
        }

        if (symbol is IPropertySymbol interfaceProperty && CSharpOperationSemantics.IsArrayCollectionDeclaration(interfaceProperty))
        {
            evidence.Add("intrinsic=scalar-array-backed-interface");
        }

        if (CSharpOperationSemantics.IsListStateDeclaration(symbol))
        {
            evidence.Add("intrinsic=fresh-list-state");
            if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.List`1.Add(`0)" or
                "M:System.Collections.Generic.ICollection`1.Add(`0)" or "P:System.Collections.Generic.List`1.Item(System.Int32)" or
                "P:System.Collections.Generic.IList`1.Item(System.Int32)" or "M:System.Collections.Generic.List`1.Insert(System.Int32,`0)" or
                "M:System.Collections.Generic.IList`1.Insert(System.Int32,`0)" or "M:System.Collections.Generic.List`1.GetRange(System.Int32,System.Int32)")
            { evidence.Add("intrinsic=opaque-list-value-storage"); }
            if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.List`1.Add(`0)" or
                "P:System.Collections.Generic.List`1.Count" or "P:System.Collections.Generic.List`1.Item(System.Int32)")
            { evidence.Add("intrinsic=concrete-list-state"); }
        }

        if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.List`1.Insert(System.Int32,`0)" or
            "M:System.Collections.Generic.IList`1.Insert(System.Int32,`0)" or "M:System.Collections.Generic.List`1.GetRange(System.Int32,System.Int32)" &&
            CSharpOperationSemantics.IsListStateDeclaration(symbol))
        { evidence.Add("intrinsic=list-insertion-range-state"); }
        if (CSharpOperationSemantics.IsStackStateDeclaration(symbol))
        { evidence.Add("intrinsic=concrete-stack-state"); }
        if ((symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod) is { } queueMethod && CSharpOperationSemantics.IsQueueStateMember(queueMethod))
        { evidence.Add("intrinsic=concrete-queue-state"); }
        if (CSharpOperationSemantics.IsHashSetStateDeclaration(symbol))
        { evidence.Add("intrinsic=default-primitive-hash-set-state"); }
        if (CSharpOperationSemantics.IsHashSetInterfaceStateDeclaration(symbol))
        { evidence.Add("intrinsic=default-hash-set-interface-state"); }
        if (CSharpOperationSemantics.IsAuditedMessageExceptionDeclaration(symbol))
        { evidence.Add("intrinsic=guarded-exception-message-construction"); }
        if (CSharpOperationSemantics.IsExceptionResourceLockDeclaration(symbol))
        { evidence.Add("intrinsic=guarded-exception-resource-synchronization"); }

        if (CSharpOperationSemantics.IsDictionaryStateDeclaration(symbol))
        {
            evidence.Add("intrinsic=fresh-default-integer-key-dictionary-state");
            evidence.Add("intrinsic=constructor-origin-dictionary-state");
            if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.Dictionary`2.Add(`0,`1)" or
                "P:System.Collections.Generic.Dictionary`2.Item(`0)")
            { evidence.Add("intrinsic=opaque-dictionary-value-storage"); }
            if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.Dictionary`2.#ctor" or "M:System.Collections.Generic.Dictionary`2.#ctor(System.Int32)" or "P:System.Collections.Generic.Dictionary`2.Count")
            { evidence.Add("intrinsic=fresh-default-dictionary-construction-state"); }
            if (symbol.OriginalDefinition.GetDocumentationCommentId() == "M:System.Collections.Generic.Dictionary`2.#ctor(System.Int32)")
            { evidence.Add("intrinsic=capacity-dictionary-construction-state"); }
        }

        if (CSharpOperationSemantics.IsDefaultComparerValueDeclaration(symbol))
        { evidence.Add("intrinsic=default-comparer-cached-value"); }

        if (symbol is IMethodSymbol method)
        {
            if (CSharpOperationSemantics.IsArrayClear(method))
            { evidence.Add("intrinsic=typed-sz-array-clear-state"); }
            if (CSharpOperationSemantics.IsIntegralConvertToInt64(method))
            { evidence.Add("intrinsic=checked-integral-to-int64"); }

            if (CSharpOperationSemantics.IsObjectReferenceEquals(method))
            { evidence.Add("intrinsic=object-reference-identity"); }

            if (CSharpOperationSemantics.IsIntegerCompareTo(method))
            {
                evidence.Add("intrinsic=integer-comparison");
            }

            if (CSharpOperationSemantics.IsIntegerComparerDeclaration(method))
            {
                evidence.Add("intrinsic=default-integer-comparer-only");
            }
            if (CSharpOperationSemantics.IsGenericComparableDeclaration(method))
            { evidence.Add("intrinsic=constrained-integer-comparable"); }

            if (CSharpOperationSemantics.IsStringEqualsCall(method))
            {
                evidence.Add("intrinsic=string-static-equality");
            }

            if (CSharpOperationSemantics.IsStringInstanceEqualsCall(method))
            {
                evidence.Add("intrinsic=string-instance-equality");
            }
        }

        return new Row(symbol.ContainingAssembly.Identity.Name, symbol.GetDocumentationCommentId() ?? throw new InvalidOperationException("Missing API identity: " + symbol), symbol.Kind.ToString(), string.Join(";", evidence.ToImmutable()));
    }
}
