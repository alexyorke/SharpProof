using Microsoft.CodeAnalysis;
using SearchLib.Smt;

namespace SharpProof.Symbolic;

public enum SymbolicWitnessStatus
{
    None,
    Exact,
    Approximate,
    Unsupported
}

public enum SymbolicInputRole
{
    Unknown,
    Parameter,
    Local,
    Receiver,
    ReceiverState,
    Derived
}

public enum SymbolicInputValueKind
{
    Unknown,
    Boolean,
    Integer,
    Reference,
    String
}

public enum SymbolicInputDomainKind
{
    Unknown,
    Boolean,
    Integer,
    Reference,
    String,
    Collection,
    Index
}

public enum SymbolicNullness
{
    Unknown,
    Null,
    NotNull
}

public enum SymbolicDomainPredicateKind
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

public sealed class SymbolicIntegerRange
{
    internal SymbolicIntegerRange(
        long? minimum,
        bool minimumInclusive,
        long? maximum,
        bool maximumInclusive)
    {
        Minimum = minimum;
        MinimumInclusive = minimum.HasValue && minimumInclusive;
        Maximum = maximum;
        MaximumInclusive = maximum.HasValue && maximumInclusive;
    }

    public long? Minimum { get; }

    public bool MinimumInclusive { get; }

    public long? Maximum { get; }

    public bool MaximumInclusive { get; }

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

public sealed class SymbolicDomainPredicate
{
    internal SymbolicDomainPredicate(
        SymbolicDomainPredicateKind kind,
        string text,
        string? value,
        bool isNegated,
        SymbolicWitnessStatus status,
        string reason)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Value = value;
        IsNegated = isNegated;
        Status = status;
        Reason = reason ?? string.Empty;
    }

    public SymbolicDomainPredicateKind Kind { get; }

    public string Text { get; }

    public string? Value { get; }

    public bool IsNegated { get; }

    public SymbolicWitnessStatus Status { get; }

    public string Reason { get; }
}

public sealed class SymbolicSatisfyingAssignment
{
    internal SymbolicSatisfyingAssignment(
        string symbolicName,
        string sourceName,
        SymbolicInputRole role,
        SymbolicInputValueKind valueKind,
        string value,
        bool? booleanValue,
        long? integerValue,
        string? stringValue,
        bool? isNull,
        SymbolicWitnessStatus status,
        string reason)
    {
        SymbolicName = symbolicName ?? string.Empty;
        SourceName = sourceName ?? string.Empty;
        Role = role;
        ValueKind = valueKind;
        Value = value ?? string.Empty;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        StringValue = stringValue;
        IsNull = isNull;
        Status = status;
        Reason = reason ?? string.Empty;
    }

    public string SymbolicName { get; }

    public string SourceName { get; }

    public SymbolicInputRole Role { get; }

    public SymbolicInputValueKind ValueKind { get; }

    public string Value { get; }

    public bool? BooleanValue { get; }

    public long? IntegerValue { get; }

    public string? StringValue { get; }

    public bool? IsNull { get; }

    public SymbolicWitnessStatus Status { get; }

    public string Reason { get; }
}

public sealed class SymbolicInputDomain
{
    internal SymbolicInputDomain(
        string name,
        SymbolicInputRole role,
        SymbolicInputValueKind valueKind,
        SymbolicInputDomainKind domainKind,
        SymbolicWitnessStatus status,
        string reason,
        IReadOnlyList<string> symbolicNames,
        SymbolicIntegerRange? integerRange,
        SymbolicNullness nullness,
        string? exactString,
        SymbolicIntegerRange? stringLengthRange,
        IReadOnlyList<string> requiredPrefixes,
        IReadOnlyList<string> requiredSuffixes,
        IReadOnlyList<string> requiredSubstrings,
        IReadOnlyList<string> regularExpressions,
        SymbolicIntegerRange? collectionLengthRange,
        bool isIndex,
        string? relatedCollection,
        IReadOnlyList<SymbolicDomainPredicate> predicates,
        int alternativeCount = 1)
    {
        Name = name ?? string.Empty;
        Role = role;
        ValueKind = valueKind;
        DomainKind = domainKind;
        Status = status;
        Reason = reason ?? string.Empty;
        SymbolicNames = symbolicNames ?? Array.Empty<string>();
        IntegerRange = integerRange;
        Nullness = nullness;
        ExactString = exactString;
        StringLengthRange = stringLengthRange;
        RequiredPrefixes = requiredPrefixes ?? Array.Empty<string>();
        RequiredSuffixes = requiredSuffixes ?? Array.Empty<string>();
        RequiredSubstrings = requiredSubstrings ?? Array.Empty<string>();
        RegularExpressions = regularExpressions ?? Array.Empty<string>();
        CollectionLengthRange = collectionLengthRange;
        IsIndex = isIndex;
        RelatedCollection = relatedCollection;
        Predicates = predicates ?? Array.Empty<SymbolicDomainPredicate>();
        AlternativeCount = alternativeCount;
    }

    public string Name { get; }

    public SymbolicInputRole Role { get; }

    public SymbolicInputValueKind ValueKind { get; }

    public SymbolicInputDomainKind DomainKind { get; }

    public SymbolicWitnessStatus Status { get; }

    public string Reason { get; }

    public IReadOnlyList<string> SymbolicNames { get; }

    public SymbolicIntegerRange? IntegerRange { get; }

    public SymbolicNullness Nullness { get; }

    public string? ExactString { get; }

    public SymbolicIntegerRange? StringLengthRange { get; }

    public IReadOnlyList<string> RequiredPrefixes { get; }

    public IReadOnlyList<string> RequiredSuffixes { get; }

    public IReadOnlyList<string> RequiredSubstrings { get; }

    public IReadOnlyList<string> RegularExpressions { get; }

    public SymbolicIntegerRange? CollectionLengthRange { get; }

    public bool IsIndex { get; }

    public string? RelatedCollection { get; }

    public IReadOnlyList<SymbolicDomainPredicate> Predicates { get; }

    public int AlternativeCount { get; }
}

public sealed class SymbolicInputDomainSummary
{
    internal SymbolicInputDomainSummary(
        SymbolicWitnessStatus status,
        string reason,
        IReadOnlyList<SymbolicInputDomain> domains,
        int alternativeCount)
    {
        Status = status;
        Reason = reason ?? string.Empty;
        Domains = domains ?? Array.Empty<SymbolicInputDomain>();
        AlternativeCount = alternativeCount;
    }

    public SymbolicWitnessStatus Status { get; }

    public string Reason { get; }

    public IReadOnlyList<SymbolicInputDomain> Domains { get; }

    public int DomainCount => Domains.Count;

    public int AlternativeCount { get; }

    public bool HasApproximation =>
        Status == SymbolicWitnessStatus.Approximate ||
        Domains.Any(static domain =>
            domain.Status == SymbolicWitnessStatus.Approximate ||
            domain.Predicates.Any(static predicate => predicate.Status == SymbolicWitnessStatus.Approximate));

    public bool HasUnsupportedDomains =>
        Status == SymbolicWitnessStatus.Unsupported ||
        Domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Unsupported);
}

public sealed class SymbolicInputWitness
{
    internal SymbolicInputWitness(
        SymbolicWitnessStatus status,
        string reason,
        IReadOnlyList<SymbolicSatisfyingAssignment> assignments,
        SymbolicInputDomainSummary domainSummary)
    {
        Status = status;
        Reason = reason ?? string.Empty;
        Assignments = assignments ?? Array.Empty<SymbolicSatisfyingAssignment>();
        DomainSummary = domainSummary ?? throw new ArgumentNullException(nameof(domainSummary));
    }

    public SymbolicWitnessStatus Status { get; }

    public string Reason { get; }

    public IReadOnlyList<SymbolicSatisfyingAssignment> Assignments { get; }

    public int AssignmentCount => Assignments.Count;

    public SymbolicInputDomainSummary DomainSummary { get; }

    public bool IsAvailable => Status is SymbolicWitnessStatus.Exact or SymbolicWitnessStatus.Approximate;
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

    internal static SymbolicInputWitness None(string reason)
    {
        return CreateEmpty(SymbolicWitnessStatus.None, reason, 0);
    }

    internal static SymbolicInputWitness Unsupported(string reason)
    {
        return CreateEmpty(SymbolicWitnessStatus.Unsupported, reason, 0);
    }

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

    private static string GetDisplayName(string root)
    {
        return RemoveNumericLocationSuffix(root, 0);
    }

    private static string RemoveNumericLocationSuffix(string value, int minimumPrefixLength)
    {
        var locationIndex = value.LastIndexOf('#');
        return locationIndex > minimumPrefixLength &&
               value.Substring(locationIndex + 1).All(char.IsDigit)
            ? value.Substring(0, locationIndex)
            : value;
    }
}
