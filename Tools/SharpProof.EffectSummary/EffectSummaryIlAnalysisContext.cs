internal sealed class EffectSummaryIlAnalysisContext
{
    internal EffectSummaryIlAnalysisContext(
        PEReader peReader,
        MetadataReader reader,
        IReadOnlyDictionary<string, MethodDefinitionHandle> methodsByExactKey,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldsBySymbol,
        IReadOnlyDictionary<string, FieldDefinitionHandle> fieldsByExactKey,
        IReadOnlyDictionary<int, StaticFieldFact> staticFields,
        Dictionary<int, TrackedStackValue> knownMethodReturns,
        HashSet<int> returnValueVisiting)
    {
        PeReader = peReader;
        Reader = reader;
        MethodsByExactKey = methodsByExactKey;
        FieldsBySymbol = fieldsBySymbol;
        FieldsByExactKey = fieldsByExactKey;
        StaticFields = staticFields;
        KnownMethodReturns = knownMethodReturns;
        ReturnValueVisiting = returnValueVisiting;
    }

    internal PEReader PeReader { get; }
    internal MetadataReader Reader { get; }
    internal IReadOnlyDictionary<string, MethodDefinitionHandle> MethodsByExactKey { get; }
    internal IReadOnlyDictionary<string, FieldDefinitionHandle> FieldsBySymbol { get; }
    internal IReadOnlyDictionary<string, FieldDefinitionHandle> FieldsByExactKey { get; }
    internal IReadOnlyDictionary<int, StaticFieldFact> StaticFields { get; }
    internal Dictionary<int, TrackedStackValue> KnownMethodReturns { get; }
    internal HashSet<int> ReturnValueVisiting { get; }

    internal EffectSummaryIlAnalysisContext WithStaticFields(
        IReadOnlyDictionary<int, StaticFieldFact> staticFields)
    {
        return new EffectSummaryIlAnalysisContext(
            PeReader,
            Reader,
            MethodsByExactKey,
            FieldsBySymbol,
            FieldsByExactKey,
            staticFields,
            KnownMethodReturns,
            ReturnValueVisiting);
    }
}
