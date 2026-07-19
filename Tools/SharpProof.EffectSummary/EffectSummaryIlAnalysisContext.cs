internal sealed record EffectSummaryIlAnalysisContext(
    PEReader PeReader,
    MetadataReader Reader,
    IReadOnlyDictionary<string, MethodDefinitionHandle> MethodsByExactKey,
    IReadOnlyDictionary<string, FieldDefinitionHandle> FieldsBySymbol,
    IReadOnlyDictionary<string, FieldDefinitionHandle> FieldsByExactKey,
    IReadOnlyDictionary<int, StaticFieldFact> StaticFields,
    Dictionary<int, TrackedStackValue> KnownMethodReturns,
    HashSet<int> ReturnValueVisiting)
{
    internal EffectSummaryIlAnalysisContext WithStaticFields(
        IReadOnlyDictionary<int, StaticFieldFact> staticFields) =>
        this with { StaticFields = staticFields };
}
