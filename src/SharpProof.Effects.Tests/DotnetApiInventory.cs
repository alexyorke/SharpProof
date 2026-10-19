using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    internal sealed record Delta(string[] Added, string[] Removed, string[] Changed)
    {
        public string[] Added { get; init; } = Added ?? throw new global::System.ArgumentNullException(nameof(Added));
        public string[] Removed { get; init; } = Removed ?? throw new global::System.ArgumentNullException(nameof(Removed));
        public string[] Changed { get; init; } = Changed ?? throw new global::System.ArgumentNullException(nameof(Changed));
    }

    internal static Row[] Collect(CSharpCompilation compilation)
    {
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        // Contract ghost APIs belong to SharpProof.Attributes, outside this pack.
        var failures = resolved.Failures.Where(failure => failure is null ? throw new global::System.ArgumentNullException(nameof(failure)) : (!failure.WitnessIdentifier.StartsWith("contract.", StringComparison.Ordinal))).ToArray();
        if (failures.Length != 0)
        {
            throw new InvalidOperationException("Framework catalog resolution failed: " + string.Join(", ", failures.Select(failure => failure is null ? throw new global::System.ArgumentNullException(nameof(failure)) : (failure.ToString()))));
        }

        return compilation.References.Select(compilation.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>().SelectMany(assembly => assembly is null ? throw new global::System.ArgumentNullException(nameof(assembly)) : (Members(assembly.GlobalNamespace))).Select(symbol => symbol is null ? throw new global::System.ArgumentNullException(nameof(symbol)) : (Describe(symbol, resolved))).OrderBy(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Key), StringComparer.Ordinal).ToArray();
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
        var evidence = new List<string>();
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
                "P:System.Collections.Generic.IList`1.Item(System.Int32)")
            { evidence.Add("intrinsic=opaque-list-value-storage"); }
            if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.List`1.Add(`0)" or
                "P:System.Collections.Generic.List`1.Count" or "P:System.Collections.Generic.List`1.Item(System.Int32)")
            { evidence.Add("intrinsic=concrete-list-state"); }
        }

        if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.List`1.Insert(System.Int32,`0)" or
            "M:System.Collections.Generic.IList`1.Insert(System.Int32,`0)" or "M:System.Collections.Generic.List`1.GetRange(System.Int32,System.Int32)" &&
            CSharpOperationSemantics.IsListStateDeclaration(symbol))
        { evidence.Add("intrinsic=list-insertion-range-state"); }
        if (CSharpOperationSemantics.IsDictionaryStateDeclaration(symbol))
        {
            evidence.Add("intrinsic=fresh-default-integer-key-dictionary-state");
            if (symbol.OriginalDefinition.GetDocumentationCommentId() is "M:System.Collections.Generic.Dictionary`2.#ctor" or "P:System.Collections.Generic.Dictionary`2.Count")
            { evidence.Add("intrinsic=fresh-default-dictionary-construction-state"); }
        }

        if (symbol is IMethodSymbol method)
        {
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

            if (CSharpOperationSemantics.IsStringEqualsCall(method))
            {
                evidence.Add("intrinsic=string-static-equality");
            }

            if (CSharpOperationSemantics.IsStringInstanceEqualsCall(method))
            {
                evidence.Add("intrinsic=string-instance-equality");
            }
        }

        return new Row(symbol.ContainingAssembly.Identity.Name, symbol.GetDocumentationCommentId() ?? throw new InvalidOperationException("Missing API identity: " + symbol), symbol.Kind.ToString(), string.Join(";", evidence));
    }

    internal static Delta Compare(IEnumerable<string> baseline, IEnumerable<string> current)
    {
        global::System.ArgumentNullException.ThrowIfNull(baseline);
        global::System.ArgumentNullException.ThrowIfNull(current);
        static Dictionary<string, string> Index(IEnumerable<string> lines)
        {
            global::System.ArgumentNullException.ThrowIfNull(lines);
            return lines.ToDictionary(line => line is null ? throw new global::System.ArgumentNullException(nameof(line)) : (string.Join('\t', line.Split('\t').Take(2))), StringComparer.Ordinal);
        }

        var before = Index(baseline);
        var after = Index(current);
        return new Delta(after.Keys.Except(before.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), before.Keys.Except(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), after.Keys.Intersect(before.Keys, StringComparer.Ordinal).Where(key => key is null ? throw new global::System.ArgumentNullException(nameof(key)) : (!string.Equals(before[key], after[key], StringComparison.Ordinal))).Order(StringComparer.Ordinal).ToArray());
    }

    internal static string Fingerprint(string path)
    {
        global::System.ArgumentNullException.ThrowIfNull(path);
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    internal static string Markdown(Row[] rows, Delta delta)
    {
        global::System.ArgumentNullException.ThrowIfNull(rows);
        global::System.ArgumentNullException.ThrowIfNull(delta);
        var report = new StringBuilder("# .NET API coverage inventory\n\n").Append(CultureInfo.InvariantCulture, $"Microsoft.NETCore.App.Ref {PackVersion}, {Framework}. {rows.Length} declarations.\n\n").Append(CultureInfo.InvariantCulture, $"Partial evidence: {rows.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "partially-modeled"))}; unmodeled: {rows.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "unmodeled"))}; excluded containers: {rows.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "excluded-container"))}.\n\n").Append(CultureInfo.InvariantCulture, $"Added: {delta.Added.Length}; removed: {delta.Removed.Length}; evidence changed: {delta.Changed.Length}.\n\n").Append("Partial evidence is not a universal proof guarantee. Unmodeled means no evidence from the default catalog or the audited intrinsic predicates.\n\n").Append("| Assembly | Declarations | Partial evidence | Unmodeled | Excluded containers |\n| --- | ---: | ---: | ---: | ---: |\n");
        foreach (var group in rows.GroupBy(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Assembly)))
        {
            report.Append(CultureInfo.InvariantCulture, $"| {group.Key} | {group.Count()} | {group.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "partially-modeled"))} | {group.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "unmodeled"))} | {group.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "excluded-container"))} |\n");
        }

        report.Append("\n## Modeled facets and intrinsics\n\n| API | Evidence |\n| --- | --- |\n");
        foreach (var row in rows.Where(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Evidence.Length != 0)))
        {
            report.Append("| ").Append(row.Id).Append(" | ").Append(row.Evidence).Append(" |\n");
        }

        foreach (var (label, keys) in new[]
        {
            ("Added", delta.Added),
            ("Removed", delta.Removed),
            ("Evidence changed", delta.Changed)
        }

        )
        {
            report.Append("\n## ").Append(label).Append("\n\n");
            foreach (var key in keys.Take(100))
            {
                report.Append("- `").Append(key.Replace('\t', ' ')).Append("`\n");
            }

            if (keys.Length > 100)
            {
                report.Append("\nShowing first 100; delta.json contains every identity.\n");
            }
        }

        return report.ToString();
    }
}
