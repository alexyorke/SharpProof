using System.Text.Json;

namespace SharpProof.Tools.Fuzz;

internal static class FuzzShapeRegistry
{
    private const string ClassNamePlaceholder = "__CLASS__";

    internal static ImmutableArray<ShapeRegistryEntry> Load(
        IReadOnlyDictionary<string, Func<int, Random, string, string>> generators)
    {
        var json = ToolEmbeddedText.Load(
            typeof(FuzzShapeRegistry).Assembly,
            "SharpProof.Fuzz.ShapeRegistry.json");
        var definitions = JsonSerializer.Deserialize<RegistryDefinition[]>(json) ??
                          throw new InvalidOperationException("The fuzz shape registry is empty.");
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var usedGenerators = new HashSet<string>(StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<ShapeRegistryEntry>(definitions.Length);
        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.Id) || !seenIds.Add(definition.Id))
                throw new InvalidOperationException("The fuzz shape registry contains a missing or duplicate id.");
            var hasGenerator = !string.IsNullOrWhiteSpace(definition.Generator);
            if (hasGenerator == (definition.SourceTemplate != null))
                throw new InvalidOperationException(
                    $"Fuzz shape '{definition.Id}' must define exactly one generator or source template.");

            Func<int, Random, string, string> build;
            if (hasGenerator)
            {
                if (!generators.TryGetValue(definition.Generator!, out build!))
                    throw new InvalidOperationException(
                        $"Fuzz shape '{definition.Id}' references unknown generator '{definition.Generator}'.");
                usedGenerators.Add(definition.Generator!);
            }
            else
            {
                var template = definition.SourceTemplate!;
                if (!template.Contains(ClassNamePlaceholder, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Fuzz shape '{definition.Id}' source template has no class-name placeholder.");
                build = (_, _, className) =>
                    template.Replace(ClassNamePlaceholder, className, StringComparison.Ordinal);
            }

            entries.Add(new ShapeRegistryEntry(
                definition.Id,
                definition.PrimaryShapes.ToImmutableArray(),
                definition.OperationKinds.ToImmutableArray(),
                definition.SyntaxKinds.ToImmutableArray(),
                CreateExpectation(definition),
                definition.AllowUnsafe,
                definition.AllowEffectPreservingWrappers,
                build));
        }

        var unusedGenerators = generators.Keys.Except(usedGenerators, StringComparer.Ordinal).ToArray();
        if (unusedGenerators.Length != 0)
            throw new InvalidOperationException(
                "The fuzz shape registry does not reference generator(s): " + string.Join(", ", unusedGenerators));
        return entries.MoveToImmutable();
    }

    private static FuzzExpectation CreateExpectation(RegistryDefinition definition)
    {
        if (!Enum.TryParse<Sp0002ExpectationKind>(definition.Sp0002, out var sp0002) ||
            !Enum.IsDefined(sp0002) ||
            !Enum.TryParse<Sp0010ExpectationKind>(definition.Sp0010, out var sp0010) ||
            !Enum.IsDefined(sp0010))
            throw new InvalidOperationException($"Fuzz shape '{definition.Id}' has an invalid expectation.");
        return new FuzzExpectation(
            sp0002,
            sp0010,
            definition.RequiredSp0002Properties.ToImmutableArray(),
            definition.RequiredSp0010Properties.ToImmutableArray(),
            definition.RequiredAnySp0010Properties.ToImmutableArray());
    }

    private sealed record RegistryDefinition(
        string Id,
        string[] PrimaryShapes,
        string[] OperationKinds,
        string[] SyntaxKinds,
        string Sp0002,
        string Sp0010,
        string[] RequiredSp0002Properties,
        string[] RequiredSp0010Properties,
        string[] RequiredAnySp0010Properties,
        bool AllowUnsafe,
        bool AllowEffectPreservingWrappers,
        string? Generator,
        string? SourceTemplate);
}
