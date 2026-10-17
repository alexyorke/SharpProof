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
    internal const string PackVersion = "9.0.18";
    internal const string Framework = "net9.0";

    internal sealed record Row(string Assembly, string Id, string Kind, string Evidence)
    {
        internal string Key => Assembly + "\t" + Id;
        internal string Status => Kind == "NamedType" ? "excluded-container" :
            Evidence.Length == 0 ? "unmodeled" : "partially-modeled";
        internal string Line => Key + "\t" + Kind + "\t" + Status + "\t" + (Evidence.Length == 0 ? "-" : Evidence);
    }

    internal sealed record Delta(string[] Added, string[] Removed, string[] Changed);

    internal static Row[] Collect(CSharpCompilation compilation)
    {
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        // Contract ghost APIs belong to SharpProof.Attributes, outside this pack.
        var failures = resolved.Failures.Where(failure =>
            !failure.WitnessIdentifier.StartsWith("contract.", StringComparison.Ordinal)).ToArray();
        if (failures.Length != 0)
        {
            throw new InvalidOperationException("Framework catalog resolution failed: " +
                string.Join(", ", failures.Select(failure => failure.ToString())));
        }

        return compilation.References.Select(compilation.GetAssemblyOrModuleSymbol)
            .OfType<IAssemblySymbol>()
            .SelectMany(assembly => Members(assembly.GlobalNamespace))
            .Select(symbol => Describe(symbol, resolved))
            .OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
    }

    internal static IEnumerable<ISymbol> Members(INamespaceOrTypeSymbol owner)
    {
        foreach (var member in owner.GetMembers())
        {
            if (member is INamespaceSymbol space)
            {
                foreach (var child in Members(space))
                { yield return child; }
                continue;
            }
            if (member.DeclaredAccessibility is not (Accessibility.Public or
                Accessibility.Protected or Accessibility.ProtectedOrInternal))
            { continue; }
            // Accessors are accounted for by their property/event. Inherited members
            // are accounted for once, at their declaration. No forwarder duplicates.
            if (member is IMethodSymbol { AssociatedSymbol: not null })
            { continue; }
            yield return member;
            if (member is INamedTypeSymbol type)
            {
                foreach (var child in Members(type))
                { yield return child; }
            }
        }
    }

    private static Row Describe(ISymbol symbol, ResolvedApiSpecTable resolved)
    {
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
        if (symbol is IPropertySymbol { GetMethod: { } countGetter } &&
            CSharpOperationSemantics.IsNonnegativeCollectionCount(countGetter))
        { evidence.Add("intrinsic=concrete-count-nonnegative"); }
        if (symbol is IPropertySymbol interfaceProperty && CSharpOperationSemantics.IsArrayCollectionDeclaration(interfaceProperty))
        { evidence.Add("intrinsic=scalar-array-backed-interface"); }
        if (symbol is IMethodSymbol method)
        {
            if (CSharpOperationSemantics.IsIntegerCompareTo(method))
            { evidence.Add("intrinsic=integer-comparison"); }
            if (CSharpOperationSemantics.IsIntegerComparerDeclaration(method))
            { evidence.Add("intrinsic=default-integer-comparer-only"); }
            if (CSharpOperationSemantics.IsStringEqualsCall(method))
            { evidence.Add("intrinsic=string-static-equality"); }
            if (CSharpOperationSemantics.IsStringInstanceEqualsCall(method))
            { evidence.Add("intrinsic=string-instance-equality"); }
        }
        return new Row(symbol.ContainingAssembly.Identity.Name,
            symbol.GetDocumentationCommentId() ?? throw new InvalidOperationException("Missing API identity: " + symbol),
            symbol.Kind.ToString(), string.Join(";", evidence));
    }

    internal static Delta Compare(IEnumerable<string> baseline, IEnumerable<string> current)
    {
        static Dictionary<string, string> Index(IEnumerable<string> lines)
        {
            return lines.ToDictionary(line => string.Join('\t', line.Split('\t').Take(2)), StringComparer.Ordinal);
        }
        var before = Index(baseline);
        var after = Index(current);
        return new Delta(
            after.Keys.Except(before.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            before.Keys.Except(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            after.Keys.Intersect(before.Keys, StringComparer.Ordinal)
                .Where(key => !string.Equals(before[key], after[key], StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ToArray());
    }

    internal static string Fingerprint(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    internal static string Markdown(Row[] rows, Delta delta)
    {
        var report = new StringBuilder("# .NET API coverage inventory\n\n")
            .Append(CultureInfo.InvariantCulture, $"Microsoft.NETCore.App.Ref {PackVersion}, {Framework}. {rows.Length} declarations.\n\n")
            .Append(CultureInfo.InvariantCulture, $"Partial evidence: {rows.Count(row => row.Status == "partially-modeled")}; unmodeled: {rows.Count(row => row.Status == "unmodeled")}; excluded containers: {rows.Count(row => row.Status == "excluded-container")}.\n\n")
            .Append(CultureInfo.InvariantCulture, $"Added: {delta.Added.Length}; removed: {delta.Removed.Length}; evidence changed: {delta.Changed.Length}.\n\n")
            .Append("Partial evidence is not a universal proof guarantee. Unmodeled means no evidence from the default catalog or the audited intrinsic predicates.\n\n")
            .Append("| Assembly | Declarations | Partial evidence | Unmodeled | Excluded containers |\n| --- | ---: | ---: | ---: | ---: |\n");
        foreach (var group in rows.GroupBy(row => row.Assembly))
        {
            report.Append(CultureInfo.InvariantCulture,
                $"| {group.Key} | {group.Count()} | {group.Count(row => row.Status == "partially-modeled")} | {group.Count(row => row.Status == "unmodeled")} | {group.Count(row => row.Status == "excluded-container")} |\n");
        }
        report.Append("\n## Modeled facets and intrinsics\n\n| API | Evidence |\n| --- | --- |\n");
        foreach (var row in rows.Where(row => row.Evidence.Length != 0))
        { report.Append("| ").Append(row.Id).Append(" | ").Append(row.Evidence).Append(" |\n"); }
        foreach (var (label, keys) in new[] { ("Added", delta.Added), ("Removed", delta.Removed), ("Evidence changed", delta.Changed) })
        {
            report.Append("\n## ").Append(label).Append("\n\n");
            foreach (var key in keys.Take(100))
            { report.Append("- `").Append(key.Replace('\t', ' ')).Append("`\n"); }
            if (keys.Length > 100)
            { report.Append("\nShowing first 100; delta.json contains every identity.\n"); }
        }
        return report.ToString();
    }
}
