namespace SharpProof.Tools.Fuzz;

internal static class FuzzShapeRegistry {
    private const string ClassNamePlaceholder = "__CLASS__";

    internal static ImmutableArray<ShapeRegistryEntry> Load(
        IReadOnlyDictionary<string, Func<int, Random, string, string>> generators) {
        var json = ToolEmbeddedText.Load(
            typeof(FuzzShapeRegistry).Assembly,
            "SharpProof.Fuzz.ShapeRegistry.json");
        var definitions = JsonSerializer.Deserialize<RegistryDefinition[]>(json) ??
                          throw new InvalidOperationException("The fuzz shape registry is empty.");
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var usedGenerators = new HashSet<string>(StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<ShapeRegistryEntry>(definitions.Length);
        foreach (var definition in definitions) {
            if (string.IsNullOrWhiteSpace(definition.Id) || !seenIds.Add(definition.Id))
                throw new InvalidOperationException("The fuzz shape registry contains a missing or duplicate id.");
            var hasGenerator = !string.IsNullOrWhiteSpace(definition.Generator);
            if (hasGenerator == (definition.SourceTemplate != null))
                throw new InvalidOperationException(
                    $"Fuzz shape '{definition.Id}' must define exactly one generator or source template.");

            Func<int, Random, string, string> build;
            if (hasGenerator) {
                if (!generators.TryGetValue(definition.Generator!, out build!))
                    throw new InvalidOperationException(
                        $"Fuzz shape '{definition.Id}' references unknown generator '{definition.Generator}'.");
                usedGenerators.Add(definition.Generator!);
            }
            else {
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
                build));
        }

        var unusedGenerators = generators.Keys.Except(usedGenerators, StringComparer.Ordinal).ToArray();
        if (unusedGenerators.Length != 0)
            throw new InvalidOperationException(
                "The fuzz shape registry does not reference generator(s): " + string.Join(", ", unusedGenerators));
        return entries.MoveToImmutable();
    }

    private static FuzzExpectation CreateExpectation(RegistryDefinition definition) {
        if (!Enum.TryParse<SharpProofVerdict>(definition.PurityVerdict, out var purityVerdict) ||
            !Enum.IsDefined(purityVerdict))
            throw new InvalidOperationException($"Fuzz shape '{definition.Id}' has an invalid expectation.");
        return new FuzzExpectation(
            purityVerdict,
            Compact(definition.RequiredEffects).Select(ParseEffect).ToImmutableArray(),
            Compact(definition.ForbiddenEffects).Select(ParseEffect).ToImmutableArray(),
            Compact(definition.RequiredUnknownCategories).ToImmutableArray(),
            Compact(definition.RequiredDiagnosticIds).ToImmutableArray());
    }

    private static IEnumerable<string> Compact(string[]? values) =>
        (values ?? []).Where(static value => !string.IsNullOrWhiteSpace(value));

    private static SharpProofEffect ParseEffect(string value) =>
        Enum.TryParse<SharpProofEffect>(value, out var effect) && Enum.IsDefined(effect)
            ? effect
            : throw new InvalidOperationException("Invalid fuzz effect expectation: " + value);

    sealed record RegistryDefinition(
        string Id,
        string[] PrimaryShapes,
        string[] OperationKinds,
        string[] SyntaxKinds,
        string PurityVerdict,
        string[]? RequiredEffects,
        string[]? ForbiddenEffects,
        string[]? RequiredUnknownCategories,
        string[]? RequiredDiagnosticIds,
        bool AllowUnsafe,
        string? Generator,
        string? SourceTemplate);
}
