using SearchLib.Smt;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal static class SymbolicInputDomainSynthesizer
{
    internal static IReadOnlyList<SymbolicInputDomain> Synthesize(
        IReadOnlyList<SmtFormula> formulas,
        IReadOnlyList<SymbolicSatisfyingAssignment> assignments,
        SymbolicInputRoleMap roles)
    {
        var builders = new Dictionary<string, DomainBuilder>(StringComparer.Ordinal);
        foreach (var formula in formulas) Visit(formula, true, builders, roles);

        foreach (var assignment in assignments)
        {
            var target = GetTarget(assignment.SymbolicName, assignment.ValueKind, roles);
            GetOrCreate(builders, target).AddSymbolicName(assignment.SymbolicName);
        }

        return builders.Values
            .OrderBy(static builder => builder.Name, StringComparer.Ordinal)
            .Select(static builder => builder.Build())
            .ToArray();
    }

    internal static SymbolicInputDomainSummary MergeAlternatives(
        IReadOnlyList<SymbolicInputDomainSummary> summaries)
    {
        if (summaries.Count == 0)
            return new SymbolicInputDomainSummary(
                SymbolicWitnessStatus.None,
                "no_satisfying_alternatives",
                Array.Empty<SymbolicInputDomain>(),
                0);

        if (summaries.Count == 1) return summaries[0];

        var domains = summaries
            .SelectMany(static summary => summary.Domains)
            .GroupBy(static domain => domain.Name, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => MergeDomainAlternatives(group.ToArray(), summaries.Count))
            .ToArray();
        var status = domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Unsupported)
            ? SymbolicWitnessStatus.Unsupported
            : SymbolicWitnessStatus.Approximate;
        return new SymbolicInputDomainSummary(
            status,
            "conservative_union_of_alternative_paths",
            domains,
            summaries.Count);
    }

    private static SymbolicInputDomain MergeDomainAlternatives(
        IReadOnlyList<SymbolicInputDomain> domains,
        int totalAlternativeCount)
    {
        var first = domains[0];
        var coversEveryAlternative = domains.Count == totalAlternativeCount;
        var role = domains.All(domain => domain.Role == first.Role)
            ? first.Role
            : SymbolicInputRole.Unknown;
        var valueKind = domains.All(domain => domain.ValueKind == first.ValueKind)
            ? first.ValueKind
            : SymbolicInputValueKind.Unknown;
        var domainKind = domains.All(domain => domain.DomainKind == first.DomainKind)
            ? first.DomainKind
            : SymbolicInputDomainKind.Unknown;
        var nullness = coversEveryAlternative && domains.All(domain => domain.Nullness == first.Nullness)
            ? first.Nullness
            : SymbolicNullness.Unknown;
        var exactString = coversEveryAlternative &&
                          domains.All(domain => string.Equals(domain.ExactString, first.ExactString,
                              StringComparison.Ordinal))
            ? first.ExactString
            : null;
        var relatedCollection = domains
            .Select(static domain => domain.RelatedCollection)
            .Distinct(StringComparer.Ordinal)
            .Count() == 1
            ? first.RelatedCollection
            : null;
        var predicates = domains
            .SelectMany(static domain => domain.Predicates)
            .Select(predicate => new SymbolicDomainPredicate(
                predicate.Kind,
                predicate.Text,
                predicate.Value,
                predicate.IsNegated,
                predicate.Status == SymbolicWitnessStatus.Unsupported
                    ? SymbolicWitnessStatus.Unsupported
                    : SymbolicWitnessStatus.Approximate,
                "predicate_applies_to_one_or_more_alternative_paths"))
            .GroupBy(static predicate =>
                    predicate.Kind + "|" + predicate.IsNegated + "|" + predicate.Text,
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        return new SymbolicInputDomain(
            first.Name,
            role,
            valueKind,
            domainKind,
            domains.Any(static domain => domain.Status == SymbolicWitnessStatus.Unsupported)
                ? SymbolicWitnessStatus.Unsupported
                : SymbolicWitnessStatus.Approximate,
            "conservative_union_of_alternative_paths",
            domains.SelectMany(static domain => domain.SymbolicNames)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray(),
            MergeRanges(domains.Select(static domain => domain.IntegerRange).ToArray(), coversEveryAlternative),
            nullness,
            exactString,
            MergeRanges(domains.Select(static domain => domain.StringLengthRange).ToArray(), coversEveryAlternative),
            IntersectValues(domains.Select(static domain => domain.RequiredPrefixes).ToArray(),
                coversEveryAlternative),
            IntersectValues(domains.Select(static domain => domain.RequiredSuffixes).ToArray(),
                coversEveryAlternative),
            IntersectValues(domains.Select(static domain => domain.RequiredSubstrings).ToArray(),
                coversEveryAlternative),
            IntersectValues(domains.Select(static domain => domain.RegularExpressions).ToArray(),
                coversEveryAlternative),
            MergeRanges(domains.Select(static domain => domain.CollectionLengthRange).ToArray(),
                coversEveryAlternative),
            domains.Any(static domain => domain.IsIndex),
            relatedCollection,
            predicates,
            totalAlternativeCount);
    }

    private static SymbolicIntegerRange? MergeRanges(
        IReadOnlyList<SymbolicIntegerRange?> ranges,
        bool coversEveryAlternative)
    {
        if (!coversEveryAlternative || ranges.Any(static range => range == null)) return null;

        var concrete = ranges.Cast<SymbolicIntegerRange>().ToArray();
        var minimum = concrete.Any(static range => !range.Minimum.HasValue)
            ? null
            : concrete.Min(static range => range.Minimum);
        var maximum = concrete.Any(static range => !range.Maximum.HasValue)
            ? null
            : concrete.Max(static range => range.Maximum);
        var minimumInclusive = minimum.HasValue && concrete
            .Where(range => range.Minimum == minimum)
            .Any(static range => range.MinimumInclusive);
        var maximumInclusive = maximum.HasValue && concrete
            .Where(range => range.Maximum == maximum)
            .Any(static range => range.MaximumInclusive);
        return new SymbolicIntegerRange(minimum, minimumInclusive, maximum, maximumInclusive);
    }

    private static IReadOnlyList<string> IntersectValues(
        IReadOnlyList<IReadOnlyList<string>> alternatives,
        bool coversEveryAlternative)
    {
        if (!coversEveryAlternative || alternatives.Count == 0) return Array.Empty<string>();

        var values = new HashSet<string>(alternatives[0], StringComparer.Ordinal);
        foreach (var alternative in alternatives.Skip(1)) values.IntersectWith(alternative);

        return values.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }

    private static void Visit(
        SmtFormula formula,
        bool polarity,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        switch (formula)
        {
            case SmtBooleanConstant:
                return;
            case SmtVariable { Kind: SmtValueKind.Bool } booleanVariable:
                var booleanBuilder = GetOrCreate(builders,
                    GetTarget(booleanVariable.Name, SymbolicInputValueKind.Boolean, roles));
                booleanBuilder.AddSymbolicName(booleanVariable.Name);
                booleanBuilder.AddPredicate(
                    SymbolicDomainPredicateKind.BooleanValue,
                    Format(formula, polarity),
                    polarity ? "true" : "false",
                    !polarity,
                    SymbolicWitnessStatus.Exact,
                    "boolean_path_constraint");
                return;
            case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } unary:
                Visit(unary.Operand, !polarity, builders, roles);
                return;
            case SmtBinaryFormula { Operator: SmtBinaryOperator.And } conjunction when polarity:
                Visit(conjunction.Left, true, builders, roles);
                Visit(conjunction.Right, true, builders, roles);
                return;
            case SmtBinaryFormula { Operator: SmtBinaryOperator.Or } disjunction when !polarity:
                Visit(disjunction.Left, false, builders, roles);
                Visit(disjunction.Right, false, builders, roles);
                return;
            case SmtBinaryFormula binary when
                binary.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or:
                MarkAlternative(formula, polarity, builders, roles);
                return;
            case SmtBinaryFormula binary:
                if (TryApplyBinary(binary, polarity, builders, roles)) return;

                MarkUnsupported(formula, polarity, builders, roles, "binary_domain_not_synthesized");
                return;
            case SmtStringStartsWithFormula startsWith:
                ApplyStringPredicate(
                    startsWith.Value,
                    startsWith.Prefix,
                    SymbolicDomainPredicateKind.StringPrefix,
                    polarity,
                    builders,
                    roles);
                return;
            case SmtStringEndsWithFormula endsWith:
                ApplyStringPredicate(
                    endsWith.Value,
                    endsWith.Suffix,
                    SymbolicDomainPredicateKind.StringSuffix,
                    polarity,
                    builders,
                    roles);
                return;
            case SmtStringContainsFormula contains:
                ApplyStringPredicate(
                    contains.Value,
                    contains.Search,
                    SymbolicDomainPredicateKind.StringContains,
                    polarity,
                    builders,
                    roles);
                return;
            case SmtRegexMatchFormula regexMatch when TryGetStringVariable(regexMatch.Value, out var stringVariable):
                var regexBuilder = GetOrCreate(builders,
                    GetTarget(stringVariable.Name, SymbolicInputValueKind.String, roles));
                regexBuilder.AddSymbolicName(stringVariable.Name);
                regexBuilder.AddRegex(regexMatch.Pattern, polarity);
                regexBuilder.AddPredicate(
                    SymbolicDomainPredicateKind.RegularExpression,
                    Format(formula, polarity),
                    regexMatch.Pattern,
                    !polarity,
                    SymbolicWitnessStatus.Approximate,
                    "regex_domain_may_use_approximate_translation");
                regexBuilder.MarkApproximate("regex_domain_may_use_approximate_translation");
                return;
            case SmtRuntimeTypeTestFormula runtimeType:
                MarkUnsupported(runtimeType, polarity, builders, roles, "runtime_type_domain_not_materialized");
                return;
            default:
                MarkUnsupported(formula, polarity, builders, roles, "formula_domain_not_synthesized");
                return;
        }
    }

    private static bool TryApplyBinary(
        SmtBinaryFormula binary,
        bool polarity,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        var comparison = polarity ? binary.Operator : SmtComparisonOperatorFacts.Negate(binary.Operator);
        if (TryApplyNullness(binary.Left, binary.Right, comparison, builders, roles) ||
            TryApplyNullness(binary.Right, binary.Left, SmtComparisonOperatorFacts.Reverse(comparison), builders, roles))
            return true;

        if (TryApplyIntegerComparison(binary.Left, binary.Right, comparison, builders, roles) ||
            TryApplyIntegerComparison(binary.Right, binary.Left, SmtComparisonOperatorFacts.Reverse(comparison), builders, roles))
            return true;

        if (TryApplyStringEquality(binary.Left, binary.Right, comparison, builders, roles) ||
            TryApplyStringEquality(binary.Right, binary.Left, SmtComparisonOperatorFacts.Reverse(comparison), builders, roles))
            return true;

        if (TryApplyBooleanEquality(binary.Left, binary.Right, comparison, builders, roles) ||
            TryApplyBooleanEquality(binary.Right, binary.Left, SmtComparisonOperatorFacts.Reverse(comparison), builders, roles))
            return true;

        return TryApplyIndexRelationship(binary.Left, binary.Right, comparison, builders, roles) ||
               TryApplyIndexRelationship(
                   binary.Right,
                   binary.Left,
                   SmtComparisonOperatorFacts.Reverse(comparison),
                   builders,
                   roles);
    }

    private static bool TryApplyNullness(
        SmtFormula candidate,
        SmtFormula value,
        SmtBinaryOperator comparison,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        if (candidate is not SmtVariable { Kind: SmtValueKind.Reference } variable ||
            value is not SmtNullConstant ||
            comparison is not SmtBinaryOperator.Equal and not SmtBinaryOperator.NotEqual)
            return false;

        var builder = GetOrCreate(builders,
            GetTarget(variable.Name, SymbolicInputValueKind.Reference, roles));
        builder.AddSymbolicName(variable.Name);
        builder.SetNullness(comparison == SmtBinaryOperator.Equal
            ? SymbolicNullness.Null
            : SymbolicNullness.NotNull);
        builder.AddPredicate(
            SymbolicDomainPredicateKind.Nullness,
            SymbolicFormulaDisplay.Format(new SmtBinaryFormula(comparison, candidate, value)),
            comparison == SmtBinaryOperator.Equal ? "null" : "not-null",
            false,
            SymbolicWitnessStatus.Exact,
            "reference_nullness_constraint");
        return true;
    }

    private static bool TryApplyIntegerComparison(
        SmtFormula candidate,
        SmtFormula value,
        SmtBinaryOperator comparison,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        if (value is not SmtIntegerConstant constant ||
            !TryGetIntegerTarget(candidate, roles, out var target, out var rangeKind, out var symbolicName))
            return false;

        var builder = GetOrCreate(builders, target);
        builder.AddSymbolicName(symbolicName);
        builder.ApplyRange(rangeKind, comparison, constant.Value);
        builder.AddPredicate(
            rangeKind switch
            {
                RangeKind.StringLength => SymbolicDomainPredicateKind.StringLength,
                RangeKind.CollectionLength => SymbolicDomainPredicateKind.CollectionLength,
                _ => SymbolicDomainPredicateKind.Range
            },
            SymbolicFormulaDisplay.Format(new SmtBinaryFormula(comparison, candidate, value)),
            constant.Value.ToString(),
            false,
            SymbolicWitnessStatus.Exact,
            "integer_range_constraint");
        return true;
    }

    private static bool TryApplyStringEquality(
        SmtFormula candidate,
        SmtFormula value,
        SmtBinaryOperator comparison,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        if (!TryGetStringVariable(candidate, out var variable) ||
            value is not SmtStringConstant constant ||
            comparison is not SmtBinaryOperator.Equal and not SmtBinaryOperator.NotEqual)
            return false;

        var builder = GetOrCreate(builders,
            GetTarget(variable.Name, SymbolicInputValueKind.String, roles));
        builder.AddSymbolicName(variable.Name);
        if (comparison == SmtBinaryOperator.Equal) builder.SetExactString(constant.Value);

        builder.AddPredicate(
            SymbolicDomainPredicateKind.StringContent,
            SymbolicFormulaDisplay.Format(new SmtBinaryFormula(comparison, candidate, value)),
            constant.Value,
            comparison == SmtBinaryOperator.NotEqual,
            SymbolicWitnessStatus.Exact,
            "string_content_constraint");
        return true;
    }

    private static bool TryApplyBooleanEquality(
        SmtFormula candidate,
        SmtFormula value,
        SmtBinaryOperator comparison,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        if (candidate is not SmtVariable { Kind: SmtValueKind.Bool } variable ||
            value is not SmtBooleanConstant constant ||
            comparison is not SmtBinaryOperator.Equal and not SmtBinaryOperator.NotEqual)
            return false;

        var booleanValue = comparison == SmtBinaryOperator.Equal ? constant.Value : !constant.Value;
        var builder = GetOrCreate(builders,
            GetTarget(variable.Name, SymbolicInputValueKind.Boolean, roles));
        builder.AddSymbolicName(variable.Name);
        builder.AddPredicate(
            SymbolicDomainPredicateKind.BooleanValue,
            SymbolicFormulaDisplay.Format(new SmtBinaryFormula(comparison, candidate, value)),
            booleanValue ? "true" : "false",
            false,
            SymbolicWitnessStatus.Exact,
            "boolean_path_constraint");
        return true;
    }

    private static bool TryApplyIndexRelationship(
        SmtFormula candidate,
        SmtFormula bound,
        SmtBinaryOperator comparison,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        if (candidate is not SmtVariable { Kind: SmtValueKind.Int } index ||
            !TryGetCollectionLengthVariable(bound, out var lengthVariable, out var collectionName) ||
            comparison is not SmtBinaryOperator.LessThan and
            not SmtBinaryOperator.LessThanOrEqual and
            not SmtBinaryOperator.GreaterThan and
            not SmtBinaryOperator.GreaterThanOrEqual)
            return false;

        var indexBuilder = GetOrCreate(builders,
            GetTarget(index.Name, SymbolicInputValueKind.Integer, roles));
        indexBuilder.AddSymbolicName(index.Name);
        indexBuilder.MarkIndex(roles.Resolve(collectionName).SourceName);
        indexBuilder.AddPredicate(
            SymbolicDomainPredicateKind.IndexBound,
            SymbolicFormulaDisplay.Format(new SmtBinaryFormula(comparison, candidate, bound)),
            lengthVariable.Name,
            false,
            SymbolicWitnessStatus.Exact,
            "index_to_collection_length_constraint");
        var collectionBuilder = GetOrCreate(builders,
            GetTarget(lengthVariable.Name, SymbolicInputValueKind.Integer, roles));
        collectionBuilder.AddSymbolicName(lengthVariable.Name);
        return true;
    }

    private static void ApplyStringPredicate(
        SmtFormula value,
        SmtFormula argument,
        SymbolicDomainPredicateKind kind,
        bool polarity,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        if (!TryGetStringVariable(value, out var variable) || argument is not SmtStringConstant constant)
        {
            MarkUnsupported(value, polarity, builders, roles, "string_predicate_domain_not_synthesized");
            return;
        }

        var builder = GetOrCreate(builders,
            GetTarget(variable.Name, SymbolicInputValueKind.String, roles));
        builder.AddSymbolicName(variable.Name);
        if (polarity) builder.AddRequiredStringValue(kind, constant.Value);

        builder.AddPredicate(
            kind,
            kind + "(" + variable.Name + ", " + constant.Value + ")",
            constant.Value,
            !polarity,
            SymbolicWitnessStatus.Exact,
            "string_predicate_constraint");
    }

    private static bool TryGetIntegerTarget(
        SmtFormula formula,
        SymbolicInputRoleMap roles,
        out Target target,
        out RangeKind rangeKind,
        out string symbolicName)
    {
        switch (formula)
        {
            case SmtVariable { Kind: SmtValueKind.Int } variable:
                symbolicName = variable.Name;
                target = GetTarget(variable.Name, SymbolicInputValueKind.Integer, roles);
                rangeKind = IsCollectionLengthName(variable.Name)
                    ? RangeKind.CollectionLength
                    : RangeKind.Integer;
                return true;
            case SmtStringLengthTerm stringLength when TryGetStringVariable(stringLength.Value, out var stringVariable):
                symbolicName = stringVariable.Name;
                target = GetTarget(stringVariable.Name, SymbolicInputValueKind.String, roles);
                rangeKind = RangeKind.StringLength;
                return true;
            default:
                target = default;
                rangeKind = default;
                symbolicName = string.Empty;
                return false;
        }
    }

    private static bool TryGetStringVariable(SmtFormula formula, out SmtVariable variable)
    {
        if (formula is SmtVariable { Kind: SmtValueKind.String } stringVariable)
        {
            variable = stringVariable;
            return true;
        }

        variable = null!;
        return false;
    }

    private static bool TryGetCollectionLengthVariable(
        SmtFormula formula,
        out SmtVariable variable,
        out string collectionName)
    {
        if (formula is SmtVariable { Kind: SmtValueKind.Int } lengthVariable &&
            IsCollectionLengthName(lengthVariable.Name))
        {
            variable = lengthVariable;
            collectionName = RemoveSuffix(lengthVariable.Name, ".Length", ".Count");
            return true;
        }

        variable = null!;
        collectionName = string.Empty;
        return false;
    }

    private static void MarkAlternative(
        SmtFormula formula,
        bool polarity,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles)
    {
        foreach (var variable in CollectVariables(formula))
        {
            var builder = GetOrCreate(builders,
                GetTarget(variable.Name, SymbolicInputWitnessFactory.MapValueKind(variable.Kind), roles));
            builder.AddSymbolicName(variable.Name);
            builder.AddPredicate(
                SymbolicDomainPredicateKind.Alternative,
                Format(formula, polarity),
                null,
                !polarity,
                SymbolicWitnessStatus.Approximate,
                "disjunctive_domain_requires_alternatives");
            builder.MarkApproximate("disjunctive_domain_requires_alternatives");
        }
    }

    private static void MarkUnsupported(
        SmtFormula formula,
        bool polarity,
        IDictionary<string, DomainBuilder> builders,
        SymbolicInputRoleMap roles,
        string reason)
    {
        foreach (var variable in CollectVariables(formula))
        {
            var builder = GetOrCreate(builders,
                GetTarget(variable.Name, SymbolicInputWitnessFactory.MapValueKind(variable.Kind), roles));
            builder.AddSymbolicName(variable.Name);
            builder.AddPredicate(
                SymbolicDomainPredicateKind.Unsupported,
                Format(formula, polarity),
                null,
                !polarity,
                SymbolicWitnessStatus.Unsupported,
                reason);
            builder.MarkUnsupported(reason);
        }
    }

    private static IReadOnlyList<SmtVariable> CollectVariables(SmtFormula formula)
    {
        var variables = new HashSet<SmtVariable>();
        foreach (var candidate in SmtFormulaTraversal.Enumerate(formula))
            if (candidate is SmtVariable variable)
                variables.Add(variable);

        return variables.ToArray();
    }

    private static Target GetTarget(
        string symbolicName,
        SymbolicInputValueKind valueKind,
        SymbolicInputRoleMap roles)
    {
        var baseName = RemoveSuffix(symbolicName, ".String", ".Length", ".Count");
        var identity = roles.Resolve(baseName);
        var isCollection = IsCollectionLengthName(symbolicName);
        var domainKind = isCollection
            ? SymbolicInputDomainKind.Collection
            : valueKind switch
            {
                SymbolicInputValueKind.Boolean => SymbolicInputDomainKind.Boolean,
                SymbolicInputValueKind.Integer => SymbolicInputDomainKind.Integer,
                SymbolicInputValueKind.Reference => SymbolicInputDomainKind.Reference,
                SymbolicInputValueKind.String => SymbolicInputDomainKind.String,
                _ => SymbolicInputDomainKind.Unknown
            };
        return new Target(
            baseName,
            identity.SourceName,
            identity.Role,
            isCollection ? SymbolicInputValueKind.Reference : valueKind,
            domainKind);
    }

    private static DomainBuilder GetOrCreate(IDictionary<string, DomainBuilder> builders, Target target)
    {
        if (builders.TryGetValue(target.Key, out var builder))
        {
            builder.MergeTarget(target);
            return builder;
        }

        builder = new DomainBuilder(target);
        builders.Add(target.Key, builder);
        return builder;
    }

    private static bool IsCollectionLengthName(string name)
    {
        return name.EndsWith(".Length", StringComparison.Ordinal) ||
               name.EndsWith(".Count", StringComparison.Ordinal);
    }

    private static string RemoveSuffix(string value, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
            if (value.EndsWith(suffix, StringComparison.Ordinal))
                return value.Substring(0, value.Length - suffix.Length);

        return value;
    }

    private static string Format(SmtFormula formula, bool polarity)
    {
        var text = SymbolicFormulaDisplay.Format(formula);
        return polarity ? text : "!(" + text + ")";
    }

    private enum RangeKind
    {
        Integer,
        StringLength,
        CollectionLength
    }

    private readonly record struct Target(
        string Key,
        string SourceName,
        SymbolicInputRole Role,
        SymbolicInputValueKind ValueKind,
        SymbolicInputDomainKind DomainKind);

    private sealed class DomainBuilder
    {
        private readonly HashSet<string> _contains = new(StringComparer.Ordinal);
        private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _predicateKeys = new(StringComparer.Ordinal);
        private readonly List<SymbolicDomainPredicate> _predicates = new();
        private readonly HashSet<string> _regexes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _suffixes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _symbolicNames = new(StringComparer.Ordinal);
        private readonly RangeBuilder _collectionLength = new();
        private readonly RangeBuilder _integer = new();
        private readonly RangeBuilder _stringLength = new();
        private string? _exactString;
        private string? _relatedCollection;
        private string _reason = "domain_synthesized";
        private SymbolicWitnessStatus _status = SymbolicWitnessStatus.Exact;

        internal DomainBuilder(Target target)
        {
            Name = target.SourceName;
            Role = target.Role;
            ValueKind = target.ValueKind;
            DomainKind = target.DomainKind;
        }

        internal string Name { get; private set; }

        private SymbolicInputRole Role { get; set; }

        private SymbolicInputValueKind ValueKind { get; set; }

        private SymbolicInputDomainKind DomainKind { get; set; }

        private SymbolicNullness Nullness { get; set; }

        private bool IsIndex { get; set; }

        internal void MergeTarget(Target target)
        {
            if (Role == SymbolicInputRole.Derived && target.Role != SymbolicInputRole.Derived) Role = target.Role;

            if (ValueKind == SymbolicInputValueKind.Reference && target.ValueKind == SymbolicInputValueKind.String)
                ValueKind = target.ValueKind;

            if (target.DomainKind is SymbolicInputDomainKind.String or SymbolicInputDomainKind.Collection)
                DomainKind = target.DomainKind;

            if (string.IsNullOrWhiteSpace(Name)) Name = target.SourceName;
        }

        internal void AddSymbolicName(string name)
        {
            _symbolicNames.Add(name);
        }

        internal void ApplyRange(RangeKind kind, SmtBinaryOperator comparison, long value)
        {
            var range = kind switch
            {
                RangeKind.StringLength => _stringLength,
                RangeKind.CollectionLength => _collectionLength,
                _ => _integer
            };
            range.Apply(comparison, value);
        }

        internal void SetNullness(SymbolicNullness nullness)
        {
            if (Nullness != SymbolicNullness.Unknown && Nullness != nullness)
                MarkUnsupported("conflicting_nullness_constraints");
            else
                Nullness = nullness;
        }

        internal void SetExactString(string value)
        {
            if (_exactString != null && !string.Equals(_exactString, value, StringComparison.Ordinal))
                MarkUnsupported("conflicting_string_content_constraints");
            else
                _exactString = value;
        }

        internal void AddRequiredStringValue(SymbolicDomainPredicateKind kind, string value)
        {
            switch (kind)
            {
                case SymbolicDomainPredicateKind.StringPrefix:
                    _prefixes.Add(value);
                    break;
                case SymbolicDomainPredicateKind.StringSuffix:
                    _suffixes.Add(value);
                    break;
                case SymbolicDomainPredicateKind.StringContains:
                    _contains.Add(value);
                    break;
            }
        }

        internal void AddRegex(string pattern, bool polarity)
        {
            if (polarity) _regexes.Add(pattern);
        }

        internal void MarkIndex(string relatedCollection)
        {
            IsIndex = true;
            DomainKind = SymbolicInputDomainKind.Index;
            _relatedCollection = relatedCollection;
        }

        internal void AddPredicate(
            SymbolicDomainPredicateKind kind,
            string text,
            string? value,
            bool isNegated,
            SymbolicWitnessStatus status,
            string reason)
        {
            var key = kind + "|" + isNegated + "|" + text + "|" + value;
            if (!_predicateKeys.Add(key)) return;

            _predicates.Add(new SymbolicDomainPredicate(kind, text, value, isNegated, status, reason));
        }

        internal void MarkApproximate(string reason)
        {
            if (_status == SymbolicWitnessStatus.Exact) _status = SymbolicWitnessStatus.Approximate;

            if (_status != SymbolicWitnessStatus.Unsupported) _reason = reason;
        }

        internal void MarkUnsupported(string reason)
        {
            _status = SymbolicWitnessStatus.Unsupported;
            _reason = reason;
        }

        internal SymbolicInputDomain Build()
        {
            return new SymbolicInputDomain(
                Name,
                Role,
                ValueKind,
                DomainKind,
                _status,
                _reason,
                _symbolicNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                _integer.Build(),
                Nullness,
                _exactString,
                _stringLength.Build(),
                _prefixes.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                _suffixes.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                _contains.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                _regexes.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
                _collectionLength.Build(),
                IsIndex,
                _relatedCollection,
                _predicates.ToArray());
        }
    }

    private sealed class RangeBuilder
    {
        private long? _maximum;
        private bool _maximumInclusive;
        private long? _minimum;
        private bool _minimumInclusive;

        internal void Apply(SmtBinaryOperator comparison, long value)
        {
            switch (comparison)
            {
                case SmtBinaryOperator.Equal:
                    ApplyMinimum(value, true);
                    ApplyMaximum(value, true);
                    break;
                case SmtBinaryOperator.GreaterThan:
                    ApplyMinimum(value, false);
                    break;
                case SmtBinaryOperator.GreaterThanOrEqual:
                    ApplyMinimum(value, true);
                    break;
                case SmtBinaryOperator.LessThan:
                    ApplyMaximum(value, false);
                    break;
                case SmtBinaryOperator.LessThanOrEqual:
                    ApplyMaximum(value, true);
                    break;
            }
        }

        internal SymbolicIntegerRange? Build()
        {
            return _minimum.HasValue || _maximum.HasValue
                ? new SymbolicIntegerRange(_minimum, _minimumInclusive, _maximum, _maximumInclusive)
                : null;
        }

        private void ApplyMinimum(long value, bool inclusive)
        {
            if (!_minimum.HasValue || value > _minimum.Value ||
                value == _minimum.Value && !inclusive && _minimumInclusive)
            {
                _minimum = value;
                _minimumInclusive = inclusive;
            }
        }

        private void ApplyMaximum(long value, bool inclusive)
        {
            if (!_maximum.HasValue || value < _maximum.Value ||
                value == _maximum.Value && !inclusive && _maximumInclusive)
            {
                _maximum = value;
                _maximumInclusive = inclusive;
            }
        }
    }
}
