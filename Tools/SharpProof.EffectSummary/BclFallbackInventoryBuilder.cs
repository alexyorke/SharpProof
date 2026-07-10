using SharpProof.Analyzer.Engine;

internal static class BclFallbackInventoryBuilder
{
    public static BclFallbackInventoryReport Build(AssemblyEffectReport[] assemblies)
    {
        var entries = assemblies
            .SelectMany(assembly => GetInventoryMethods(assembly)
                .Select(method => TryCreateEntry(assembly, method, out var entry) ? entry : null))
            .Where(static entry => entry != null)
            .Cast<BclFallbackInventoryEntry>()
            .GroupBy(static entry => entry.AssemblyName + "|" + entry.ExactSymbolKey, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static entry => entry.AssemblyName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Symbol, StringComparer.Ordinal)
            .ThenBy(static entry => entry.ExactSymbolKey, StringComparer.Ordinal)
            .ToArray();

        return new BclFallbackInventoryReport(
            1,
            entries.Length,
            CountGuess(entries, BclPurityFallbackHeuristics.ProbablyPure),
            CountGuess(entries, BclPurityFallbackHeuristics.ProbablyImpure),
            CountGuess(entries, BclPurityFallbackHeuristics.Unknown),
            entries);
    }

    private static IEnumerable<MethodEffectSummary> GetInventoryMethods(AssemblyEffectReport assembly)
    {
        return assembly.ClassificationMethods.Length == 0
            ? assembly.Methods
            : assembly.ClassificationMethods;
    }

    private static bool TryCreateEntry(
        AssemblyEffectReport assembly,
        MethodEffectSummary method,
        out BclFallbackInventoryEntry? entry)
    {
        entry = null;
        if (!TryCreateShape(assembly, method, out var shape)) return false;

        if (!BclPurityFallbackHeuristics.TryClassify(shape, out var classification)) return false;

        entry = new BclFallbackInventoryEntry(
            assembly.AssemblyName,
            method.Symbol,
            method.ExactSymbolKey,
            classification.Guess,
            classification.Confidence,
            classification.Reason,
            classification.Category,
            method.PurityClassification?.Classification);
        return true;
    }

    private static bool TryCreateShape(
        AssemblyEffectReport assembly,
        MethodEffectSummary method,
        out BclPurityFallbackHeuristics.Shape shape)
    {
        shape = default;
        if (!BclPurityFallbackHeuristics.IsFrameworkSystemAssemblyName(assembly.AssemblyName) ||
            !TryParseExactSymbolKey(method.ExactSymbolKey, out var parsed) ||
            !BclPurityFallbackHeuristics.IsSystemNamespace(parsed.NamespaceName))
            return false;

        var isGetter = parsed.MemberName.StartsWith("get_", StringComparison.Ordinal);
        var isSetter = parsed.MemberName.StartsWith("set_", StringComparison.Ordinal);
        var isProperty = isGetter || isSetter;
        var returnsVoid = string.Equals(parsed.ReturnTypeName, "void", StringComparison.Ordinal) ||
                          string.Equals(parsed.ReturnTypeName, "System.Void", StringComparison.Ordinal);
        var hasRefOrOutParameter = parsed.ParameterTypeNames.Any(static parameter =>
            parameter.EndsWith("&", StringComparison.Ordinal));
        var normalizedReturnType = BclPurityFallbackHeuristics.NormalizeTypeName(parsed.ReturnTypeName);

        shape = new BclPurityFallbackHeuristics.Shape(
            parsed.NamespaceName,
            parsed.TypeName,
            isProperty ? parsed.MemberName.Substring(4) : parsed.MemberName,
            true,
            isProperty,
            false,
            string.Equals(parsed.MemberName, ".ctor", StringComparison.Ordinal),
            method.IsStatic,
            returnsVoid,
            !string.Equals(normalizedReturnType, parsed.ReturnTypeName, StringComparison.Ordinal),
            hasRefOrOutParameter,
            BclPurityFallbackHeuristics.IsValueLikeTypeName(parsed.ReturnTypeName),
            BclPurityFallbackHeuristics.IsKnownValueTypeName(parsed.TypeName),
            parsed.ParameterTypeNames.All(static parameter =>
                BclPurityFallbackHeuristics.IsValueLikeTypeName(parameter) ||
                BclPurityFallbackHeuristics.IsReadOnlyViewTypeName(parameter)),
            isSetter);
        return true;
    }

    private static bool TryParseExactSymbolKey(string exactSymbolKey, out ParsedExactSymbolKey parsed)
    {
        parsed = default;
        var signatureStart = exactSymbolKey.IndexOf('(');
        var returnSeparator = exactSymbolKey.IndexOf(")->", StringComparison.Ordinal);
        if (signatureStart <= 0 || returnSeparator <= signatureStart) return false;

        var memberPrefix = exactSymbolKey.Substring(0, signatureStart);
        if (!TrySplitMemberPrefix(memberPrefix, out var typeName, out var memberName)) return false;
        var parameterText = exactSymbolKey.Substring(
            signatureStart + 1,
            returnSeparator - signatureStart - 1);
        var returnTypeName = exactSymbolKey.Substring(returnSeparator + 3);
        parsed = new ParsedExactSymbolKey(
            typeName,
            GetNamespaceName(typeName),
            memberName,
            SplitParameterTypes(parameterText),
            returnTypeName);
        return true;
    }

    private static bool TrySplitMemberPrefix(
        string memberPrefix,
        out string typeName,
        out string memberName)
    {
        const string instanceConstructorSuffix = "..ctor";
        const string staticConstructorSuffix = "..cctor";
        if (memberPrefix.EndsWith(instanceConstructorSuffix, StringComparison.Ordinal))
        {
            typeName = memberPrefix.Substring(0, memberPrefix.Length - instanceConstructorSuffix.Length);
            memberName = ".ctor";
            return typeName.Length > 0;
        }

        if (memberPrefix.EndsWith(staticConstructorSuffix, StringComparison.Ordinal))
        {
            typeName = memberPrefix.Substring(0, memberPrefix.Length - staticConstructorSuffix.Length);
            memberName = ".cctor";
            return typeName.Length > 0;
        }

        var memberSeparator = memberPrefix.LastIndexOf('.');
        if (memberSeparator <= 0 || memberSeparator == memberPrefix.Length - 1)
        {
            typeName = string.Empty;
            memberName = string.Empty;
            return false;
        }

        typeName = memberPrefix.Substring(0, memberSeparator);
        memberName = memberPrefix.Substring(memberSeparator + 1);
        return true;
    }

    private static string GetNamespaceName(string typeName)
    {
        var lastSeparator = typeName.LastIndexOf('.');
        return lastSeparator <= 0 ? string.Empty : typeName.Substring(0, lastSeparator);
    }

    private static string[] SplitParameterTypes(string parameterText)
    {
        if (string.IsNullOrWhiteSpace(parameterText)) return Array.Empty<string>();

        var parameters = new List<string>();
        var genericDepth = 0;
        var start = 0;
        for (var index = 0; index < parameterText.Length; index++)
        {
            var current = parameterText[index];
            if (current == '<')
            {
                genericDepth++;
            }
            else if (current == '>' && genericDepth > 0)
            {
                genericDepth--;
            }
            else if (current == ',' && genericDepth == 0)
            {
                AddParameter(parameterText, start, index, parameters);
                start = index + 1;
            }
        }

        AddParameter(parameterText, start, parameterText.Length, parameters);
        return parameters.ToArray();
    }

    private static void AddParameter(string parameterText, int start, int end, List<string> parameters)
    {
        var parameter = parameterText.Substring(start, end - start).Trim();
        if (parameter.Length > 0) parameters.Add(parameter);
    }

    private static int CountGuess(IReadOnlyList<BclFallbackInventoryEntry> entries, string guess)
    {
        return entries.Count(entry => string.Equals(entry.Guess, guess, StringComparison.Ordinal));
    }

    private readonly record struct ParsedExactSymbolKey(
        string TypeName,
        string NamespaceName,
        string MemberName,
        string[] ParameterTypeNames,
        string ReturnTypeName);
}

internal sealed record BclFallbackInventoryReport(
    int SchemaVersion,
    int CandidateCount,
    int ProbablyPureCount,
    int ProbablyImpureCount,
    int UnknownCount,
    BclFallbackInventoryEntry[] Entries);

internal sealed record BclFallbackInventoryEntry(
    string AssemblyName,
    string Symbol,
    string ExactSymbolKey,
    string Guess,
    string Confidence,
    string Reason,
    string Category,
    string? StrongerPurityClassification);