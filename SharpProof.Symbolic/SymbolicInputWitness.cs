using System.Text.Json.Serialization;

namespace SharpProof.Symbolic;

internal enum SymbolicWitnessStatus
{
    None,
    Exact,
    Approximate,
    Unsupported
}

internal enum SymbolicInputRole
{
    Unknown,
    Parameter,
    Local,
    Receiver,
    ReceiverState,
    Derived
}

internal enum SymbolicInputValueKind
{
    Unknown,
    Boolean,
    Integer,
    Reference,
    String
}

internal enum SymbolicInputDomainKind
{
    Unknown,
    Boolean,
    Integer,
    Reference,
    String,
    Collection,
    Index
}

internal enum SymbolicNullness
{
    Unknown,
    Null,
    NotNull
}

internal enum SymbolicDomainPredicateKind
{
    Equality,
    Inequality,
    Range,
    Nullness,
    StringLength,
    StringContent,
    StringPrefix,
    StringSuffix,
    StringContains,
    RegularExpression,
    CollectionLength,
    IndexBound,
    BooleanValue,
    Alternative,
    Unsupported
}

internal sealed record SymbolicIntegerRange(
    long? Minimum,
    bool MinimumInclusive,
    long? Maximum,
    bool MaximumInclusive)
{
    public bool HasLowerBound => Minimum.HasValue;

    public bool HasUpperBound => Maximum.HasValue;

    public bool IsExact =>
        Minimum.HasValue &&
        Maximum.HasValue &&
        Minimum.Value == Maximum.Value &&
        MinimumInclusive &&
        MaximumInclusive;

    public long? ExactValue => IsExact ? Minimum : null;
}

internal sealed record SymbolicDomainPredicate(
    SymbolicDomainPredicateKind Kind,
    string Text,
    string? Value,
    bool IsNegated,
    SymbolicWitnessStatus Status,
    string Reason);

internal sealed record SymbolicSatisfyingAssignment(
    string SymbolicName,
    string SourceName,
    SymbolicInputRole Role,
    SymbolicInputValueKind ValueKind,
    string Value,
    bool? BooleanValue,
    long? IntegerValue,
    string? StringValue,
    bool? IsNull,
    SymbolicWitnessStatus Status,
    string Reason);

internal sealed record SymbolicInputDomain(
    string Name,
    SymbolicInputRole Role,
    SymbolicInputValueKind ValueKind,
    SymbolicInputDomainKind DomainKind,
    SymbolicWitnessStatus Status,
    string Reason,
    IReadOnlyList<string> SymbolicNames,
    SymbolicIntegerRange? IntegerRange,
    SymbolicNullness Nullness,
    string? ExactString,
    SymbolicIntegerRange? StringLengthRange,
    IReadOnlyList<string> RequiredPrefixes,
    IReadOnlyList<string> RequiredSuffixes,
    IReadOnlyList<string> RequiredSubstrings,
    IReadOnlyList<string> RegularExpressions,
    SymbolicIntegerRange? CollectionLengthRange,
    bool IsIndex,
    string? RelatedCollection,
    IReadOnlyList<SymbolicDomainPredicate> Predicates,
    int AlternativeCount = 1);

internal sealed record SymbolicInputDomainSummary(
    [property: JsonPropertyOrder(0)] SymbolicWitnessStatus Status,
    [property: JsonPropertyOrder(1)] string Reason,
    [property: JsonPropertyOrder(2)] IReadOnlyList<SymbolicInputDomain> Domains,
    [property: JsonPropertyOrder(4)] int AlternativeCount)
{
    [JsonPropertyOrder(3)] public int DomainCount => Domains.Count;

    [JsonPropertyOrder(5)] public bool HasApproximation =>
        Status == SymbolicWitnessStatus.Approximate ||
        Domains.Any(static domain =>
            domain.Status == SymbolicWitnessStatus.Approximate ||
            domain.Predicates.Any(static predicate => predicate.Status == SymbolicWitnessStatus.Approximate));

    [JsonPropertyOrder(6)] public bool HasUnsupportedDomains =>
        Status == SymbolicWitnessStatus.Unsupported ||
        Domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Unsupported);
}

internal sealed record SymbolicInputWitness(
    [property: JsonPropertyOrder(0)] SymbolicWitnessStatus Status,
    [property: JsonPropertyOrder(1)] string Reason,
    [property: JsonPropertyOrder(2)] IReadOnlyList<SymbolicSatisfyingAssignment> Assignments,
    [property: JsonPropertyOrder(4)] SymbolicInputDomainSummary DomainSummary)
{
    [JsonPropertyOrder(3)] public int AssignmentCount => Assignments.Count;

    [JsonPropertyOrder(5)] public bool IsAvailable =>
        Status is SymbolicWitnessStatus.Exact or SymbolicWitnessStatus.Approximate;
}

internal static class SymbolicInputWitnessFactory
{
    internal static SymbolicInputWitness CreateReachability(
        SmtSatisfyingWitness? witness,
        IEnumerable<SmtFormula> pathConditions,
        SemanticModel? semanticModel,
        int position,
        SymbolicReachability reachability,
        string reason)
    {
        var conditions = pathConditions?.ToArray() ?? Array.Empty<SmtFormula>();
        if (reachability == SymbolicReachability.Unreachable) return None(reason);

        if (reachability == SymbolicReachability.Reachable &&
            conditions.Length == 0 &&
            witness == null)
            return Unconstrained();

        return Create(
            witness,
            conditions,
            semanticModel,
            position,
            SymbolicWitnessStatus.Unsupported,
            string.IsNullOrWhiteSpace(reason) ? "reachability_witness_unavailable" : reason);
    }

    internal static SymbolicInputWitness Create(
        SmtSatisfyingWitness? witness,
        IEnumerable<SmtFormula> formulas,
        SemanticModel? semanticModel,
        int position,
        SymbolicWitnessStatus missingStatus,
        string missingReason)
    {
        var formulaArray = formulas?.ToArray() ?? Array.Empty<SmtFormula>();
        var roles = SymbolicInputRoleMap.Create(semanticModel, position);
        var assignments = witness?.Assignments
            .Select(assignment => CreateAssignment(assignment, witness.Reason, roles))
            .ToArray() ?? Array.Empty<SymbolicSatisfyingAssignment>();
        var status = witness == null ? missingStatus : MapStatus(witness.Status);
        var reason = witness?.Reason ?? missingReason;
        var domains = SymbolicInputDomainSynthesizer.Synthesize(formulaArray, assignments, roles);
        var domainStatus = ResolveDomainStatus(domains, formulaArray.Length, status);
        var domainReason = ResolveDomainReason(domains, formulaArray.Length, reason);
        return new SymbolicInputWitness(
            status,
            reason,
            assignments,
            new SymbolicInputDomainSummary(domainStatus, domainReason, domains, 1));
    }

    internal static SymbolicInputWitness Unconstrained()
    {
        return CreateEmpty(
            SymbolicWitnessStatus.Exact,
            "unconstrained_inputs",
            1);
    }

    internal static SymbolicInputWitness None(string reason) =>
        CreateEmpty(SymbolicWitnessStatus.None, reason, 0);

    internal static SymbolicInputWitness Unsupported(string reason) =>
        CreateEmpty(SymbolicWitnessStatus.Unsupported, reason, 0);

    private static SymbolicInputWitness CreateEmpty(
        SymbolicWitnessStatus status,
        string reason,
        int alternativeCount)
    {
        return new SymbolicInputWitness(
            status,
            reason,
            Array.Empty<SymbolicSatisfyingAssignment>(),
            new SymbolicInputDomainSummary(
                status,
                reason,
                Array.Empty<SymbolicInputDomain>(),
                alternativeCount));
    }

    internal static SymbolicInputDomainSummary MergeAlternatives(IEnumerable<SymbolicInputWitness> witnesses)
    {
        var alternatives = witnesses
            .Where(static witness => witness.Status != SymbolicWitnessStatus.None)
            .ToArray();
        return SymbolicInputDomainSynthesizer.MergeAlternatives(
            alternatives.Select(static witness => witness.DomainSummary).ToArray());
    }

    private static SymbolicSatisfyingAssignment CreateAssignment(
        SmtModelAssignment assignment,
        string reason,
        SymbolicInputRoleMap roles)
    {
        var identity = roles.Resolve(assignment.Name);
        return new SymbolicSatisfyingAssignment(
            assignment.Name,
            identity.SourceName,
            identity.Role,
            MapValueKind(assignment.Kind),
            assignment.Value,
            assignment.BooleanValue,
            assignment.IntegerValue,
            assignment.StringValue,
            assignment.IsNull,
            MapStatus(assignment.Status),
            reason);
    }

    internal static SymbolicWitnessStatus MapStatus(SmtWitnessStatus status)
    {
        return status switch
        {
            SmtWitnessStatus.Exact => SymbolicWitnessStatus.Exact,
            SmtWitnessStatus.Approximate => SymbolicWitnessStatus.Approximate,
            SmtWitnessStatus.Unsupported => SymbolicWitnessStatus.Unsupported,
            _ => SymbolicWitnessStatus.None
        };
    }

    internal static SymbolicInputValueKind MapValueKind(SmtValueKind kind)
    {
        return kind switch
        {
            SmtValueKind.Bool => SymbolicInputValueKind.Boolean,
            SmtValueKind.Int => SymbolicInputValueKind.Integer,
            SmtValueKind.Reference => SymbolicInputValueKind.Reference,
            SmtValueKind.String => SymbolicInputValueKind.String,
            _ => SymbolicInputValueKind.Unknown
        };
    }

    private static SymbolicWitnessStatus ResolveDomainStatus(
        IReadOnlyList<SymbolicInputDomain> domains,
        int formulaCount,
        SymbolicWitnessStatus witnessStatus)
    {
        if (domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Unsupported))
            return SymbolicWitnessStatus.Unsupported;

        if (domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Approximate) ||
            witnessStatus == SymbolicWitnessStatus.Approximate)
            return SymbolicWitnessStatus.Approximate;

        if (domains.Count != 0 || formulaCount == 0) return SymbolicWitnessStatus.Exact;

        return witnessStatus == SymbolicWitnessStatus.None
            ? SymbolicWitnessStatus.None
            : SymbolicWitnessStatus.Unsupported;
    }

    private static string ResolveDomainReason(
        IReadOnlyList<SymbolicInputDomain> domains,
        int formulaCount,
        string witnessReason)
    {
        if (domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Unsupported))
            return "one_or_more_domains_unsupported";

        if (domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Approximate))
            return "one_or_more_domains_approximate";

        if (domains.Count == 0 && formulaCount != 0) return "input_domains_not_synthesized";

        return witnessReason;
    }
}

internal sealed class SymbolicInputRoleMap
{
    private readonly Dictionary<string, (string SourceName, SymbolicInputRole Role)> _identities;

    private SymbolicInputRoleMap(Dictionary<string, (string SourceName, SymbolicInputRole Role)> identities)
    {
        _identities = identities;
    }

    internal static SymbolicInputRoleMap Create(SemanticModel? semanticModel, int position)
    {
        var identities = new Dictionary<string, (string SourceName, SymbolicInputRole Role)>(StringComparer.Ordinal);
        identities["this"] = ("this", SymbolicInputRole.Receiver);
        if (semanticModel == null) return new SymbolicInputRoleMap(identities);

        try
        {
            foreach (var symbol in semanticModel.LookupSymbols(position))
            {
                var role = symbol switch
                {
                    IParameterSymbol => SymbolicInputRole.Parameter,
                    ILocalSymbol => SymbolicInputRole.Local,
                    IFieldSymbol or IPropertySymbol => SymbolicInputRole.ReceiverState,
                    _ => SymbolicInputRole.Unknown
                };
                if (role == SymbolicInputRole.Unknown) continue;

                identities[symbol.Name] = (symbol.Name, role);
                identities[SymbolicFactFactory.GetSmtVariableName(symbol)] = (symbol.Name, role);
                if (!SymbolEqualityComparer.Default.Equals(symbol, symbol.OriginalDefinition))
                    identities[SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition)] = (symbol.Name, role);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Preserve witness data even when a synthetic query position cannot be looked up.
        }

        return new SymbolicInputRoleMap(identities);
    }

    internal (string SourceName, SymbolicInputRole Role) Resolve(string symbolicName)
    {
        if (symbolicName.StartsWith("this.", StringComparison.Ordinal))
        {
            var receiverStateName = RemoveNumericLocationSuffix(symbolicName, "this.".Length);
            return (receiverStateName, SymbolicInputRole.ReceiverState);
        }

        var root = GetRootName(symbolicName);
        if (_identities.TryGetValue(root, out var identity))
        {
            var suffixStart = GetRootSegmentEnd(symbolicName);
            var suffix = suffixStart < symbolicName.Length
                ? symbolicName.Substring(suffixStart)
                : string.Empty;
            return (identity.SourceName + suffix, identity.Role);
        }

        if (string.Equals(root, "this", StringComparison.Ordinal))
            return ("this", SymbolicInputRole.Receiver);

        return (GetDisplayName(root), SymbolicInputRole.Derived);
    }

    internal static string GetRootName(string symbolicName)
    {
        var end = GetRootSegmentEnd(symbolicName);
        var root = symbolicName.Substring(0, end);
        var versionIndex = root.LastIndexOf("@v", StringComparison.Ordinal);
        if (versionIndex > 0 &&
            root.Substring(versionIndex + 2).All(char.IsDigit))
            root = root.Substring(0, versionIndex);

        return root;
    }

    private static int GetRootSegmentEnd(string symbolicName)
    {
        var end = symbolicName.Length;
        foreach (var marker in new[] { ".", "[", "?" })
        {
            var markerIndex = symbolicName.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0 && markerIndex < end) end = markerIndex;
        }

        return end;
    }

    private static string GetDisplayName(string root) =>
        RemoveNumericLocationSuffix(root, 0);

    private static string RemoveNumericLocationSuffix(string value, int minimumPrefixLength)
    {
        var locationIndex = value.LastIndexOf('#');
        return locationIndex > minimumPrefixLength &&
               value.Substring(locationIndex + 1).All(char.IsDigit)
            ? value.Substring(0, locationIndex)
            : value;
    }
}
