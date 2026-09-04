using System.Collections.Immutable;
using System.Text;

namespace SharpProof.Gates.Corpus;

internal static class CorpusCatalog
{
    public static ImmutableArray<CorpusVariant> Variants
    {
        get;
    } =
        [.. Enum.GetValues<CorpusVariant>()];

    public static ImmutableArray<CorpusCase> CreateCases()
    {
        return CreateCases(RepositoryLayout.FindRoot());
    }

    public static ImmutableArray<CorpusCase> CreateCases(
        string repositoryRoot)
    {
        return CreateCases(OpenSourceCorpusCatalog.Load(repositoryRoot));
    }

    internal static ImmutableArray<CorpusCase> CreateCases(
        OpenSourceCorpusDocument openSourceDocument)
    {
        ArgumentNullException.ThrowIfNull(openSourceDocument);
        return [
            .. CreateSyntheticCases(),
            .. OpenSourceCorpusCatalog.CreateCases(openSourceDocument)
        ];
    }

    internal static ImmutableArray<CorpusCase> CreateSyntheticCases()
    {
        return [.. Seeds.SelectMany(CreateCases)];
    }

    internal static ImmutableArray<CorpusSeed> Seeds
    {
        get;
    } = [
        Effect(
            "E01",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[EnforcePure]",
            "return $INPUT$ + 1;"),
        Effect(
            "E02",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[EnforcePure]",
            "State = $INPUT$; return $INPUT$;",
            "private static int State;"),
        Effect(
            "E03",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[EnforcePure]",
            "var buffer = new int[1]; buffer[0] = $INPUT$; return buffer[0];"),
        Effect(
            "E04",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[ZeroAllocations]",
            "return $INPUT$ * 2;"),
        Effect(
            "E05",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[ZeroAllocations]",
            "_ = new object(); return $INPUT$;"),
        Effect(
            "E06",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[ZeroAllocations]",
            "_ = new int[1]; return $INPUT$;"),
        Effect(
            "E07",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[ZeroAllocations]",
            "_ = Guid.NewGuid(); return $INPUT$;"),
        Effect(
            "E08",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[DoesNotThrow]",
            "return $INPUT$ + 1;"),
        Effect(
            "E09",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[DoesNotThrow]",
            "return 1 / $INPUT$;"),
        Effect(
            "E10",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[DoesNotThrow]",
            "_ = Guid.NewGuid(); return $INPUT$;"),
        Effect(
            "E11",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[AllowedCapabilities(SharpProofCapability.None)]",
            "return $INPUT$ - 1;"),
        Effect(
            "E12",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[AllowedCapabilities(SharpProofCapability.None)]",
            "_ = Guid.NewGuid(); return $INPUT$;"),
        Effect(
            "E13",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[AllowedCapabilities(SharpProofCapability.None)]",
            "ExternalCorpusEffects.Synchronize(); return $INPUT$;"),
        Effect(
            "E14",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[AllowedCapabilities(SharpProofCapability.Synchronization)]",
            "ExternalCorpusEffects.Synchronize(); return $INPUT$;"),
        Effect(
            "E15",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[AllowedExceptions(typeof(Exception))]",
            "return 1 / $INPUT$;"),
        Effect(
            "E16",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[AllowedExceptions(typeof(InvalidOperationException))]",
            "return 1 / $INPUT$;"),
        Effect(
            "E17",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "[ZeroAllocations] [DoesNotThrow]",
            "return $INPUT$ + 1;"),
        Effect(
            "E18",
            CorpusVerdict.Unknown,
            CorpusSupport.IntentionallyUnsupported,
            "[ZeroAllocations] [DoesNotThrow]",
            "_ = new object(); return 1 / $INPUT$;"),
        Contract(
            "C01",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "Positive(1); return $INPUT$;",
            PositiveMember),
        Contract(
            "C02",
            CorpusVerdict.Refuted,
            CorpusSupport.Supported,
            "Positive(-1); return $INPUT$;",
            PositiveMember),
        Contract(
            "C03",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "EqualTo(4, 4); return $INPUT$;",
            EqualMember),
        Contract(
            "C04",
            CorpusVerdict.Refuted,
            CorpusSupport.Supported,
            "EqualTo(4, 5); return $INPUT$;",
            EqualMember),
        Contract(
            "C05",
            CorpusVerdict.Refuted,
            CorpusSupport.Supported,
            "MustBe(false); return $INPUT$;",
            "private static void MustBe(bool condition) { Contract.Requires(condition); }"),
        Contract(
            "C06",
            CorpusVerdict.SilentUnknown,
            CorpusSupport.IntentionallyUnsupported,
            "Positive(Unknown()); return $INPUT$;",
            PositiveMember + " private static int Unknown() => -1;"),
        Contract(
            "C07",
            CorpusVerdict.Refuted,
            CorpusSupport.Supported,
            "Range(value: -1, minimum: 0); return $INPUT$;",
            RangeMember),
        Contract(
            "C08",
            CorpusVerdict.Refuted,
            CorpusSupport.Supported,
            "NonNegative(-1); return $INPUT$;",
            "private static void NonNegative(int value) { Contract.Requires(value >= 0); }"),
        Contract(
            "C09",
            CorpusVerdict.Proven,
            CorpusSupport.Supported,
            "Between(5); return $INPUT$;",
            BetweenMember),
        Contract(
            "C10",
            CorpusVerdict.Refuted,
            CorpusSupport.Supported,
            "Between(15); return $INPUT$;",
            BetweenMember)
    ];

    private const string PositiveMember =
        "private static void Positive(int value) { Contract.Requires(value > 0); }";
    private const string EqualMember =
        "private static void EqualTo(int left, int right) { Contract.Requires(left == right); }";
    private const string RangeMember =
        "private static void Range(int minimum, int value) { Contract.Requires(value >= minimum); }";
    private const string BetweenMember =
        "private static void Between(int value) { Contract.Requires(value >= 0 && value <= 10); }";

    private static CorpusSeed Effect(
        string id,
        CorpusVerdict expected,
        CorpusSupport support,
        string attributes,
        string body,
        string additionalMembers = "")
    {
        return new(
            id,
            "effects",
            expected,
            RequireExplicitSupport(id, support),
            attributes,
            body,
            additionalMembers);
    }

    private static CorpusSeed Contract(
        string id,
        CorpusVerdict expected,
        CorpusSupport support,
        string body,
        string additionalMembers)
    {
        return new(
            id,
            "contracts",
            expected,
            RequireExplicitSupport(id, support),
            "",
            body,
            additionalMembers);
    }

    private static CorpusSupport RequireExplicitSupport(
        string id,
        CorpusSupport support)
    {
        return support is
            CorpusSupport.Supported or
            CorpusSupport.IntentionallyUnsupported
                ? support
                : throw new InvalidDataException(
                    $"Corpus seed {id} requires an explicit support classification.");
    }

    private static IEnumerable<CorpusCase> CreateCases(CorpusSeed seed)
    {
        var cases = ImmutableArray.CreateBuilder<CorpusCase>(Variants.Length);
        var baseline = CreateCase(seed, CorpusVariant.Baseline);
        cases.Add(baseline);
        // Alpha-renaming is meaningful only when the seed actually contains
        // contract formals. Do not spend a metamorphic slot on an identical
        // source (effect seeds otherwise produced duplicate cases).
        foreach (var variant in Variants)
        {
            if (variant == CorpusVariant.Baseline)
            {
                continue;
            }
            if (variant == CorpusVariant.AlphaRenameContractFormals &&
                !string.Equals(seed.Mode, "contracts", StringComparison.Ordinal))
            {
                continue;
            }

            var item = CreateCase(seed, variant);
            if (variant == CorpusVariant.AlphaRenameContractFormals &&
                string.Equals(item.Source, baseline.Source, StringComparison.Ordinal))
            {
                continue;
            }

            cases.Add(item);
        }

        return cases.ToImmutable();
    }

    private static CorpusCase CreateCase(
        CorpusSeed seed,
        CorpusVariant variant)
    {
        var suffix = seed.Id;
        var (className, methodName, helperName, inputName) = variant switch
        {
            CorpusVariant.Rename =>
                ($"Renamed_{suffix}", $"Evaluate_{suffix}", $"Pass_{suffix}", "value"),
            CorpusVariant.EscapedIdentifiers =>
                ($"@Corpus_{suffix}", $"@Focus_{suffix}", $"@Identity_{suffix}", "@input"),
            _ =>
                ($"Corpus_{suffix}", $"Focus_{suffix}", $"Identity_{suffix}", "input")
        };
        var prelude = CreatePrelude(variant, helperName, inputName);
        var body = ReplaceTokens(
            seed.Body,
            helperName,
            inputName);
        var members = ReplaceTokens(
            seed.AdditionalMembers,
            helperName,
            inputName);
        if (variant == CorpusVariant.AlphaRenameContractFormals)
        {
            body = body.Replace(
                "Range(value: -1, minimum: 0)",
                "Range(contractValue: -1, contractMinimum: 0)",
                StringComparison.Ordinal);
            members = AlphaRenameContractFormals(members);
        }
        var trivia = variant == CorpusVariant.Trivia
            ? "// Deliberate metamorphic trivia.\n        "
            : "";
        var builder = new StringBuilder();
        builder.AppendLine("using System;");
        builder.AppendLine("using SharpProof.Attributes;");
        builder.AppendLine();
        builder.Append("public static class ").Append(className).AppendLine(" {");
        if (!string.IsNullOrWhiteSpace(members))
        {
            builder.Append("    ").AppendLine(members);
        }

        builder.Append("    private static int ")
            .Append(helperName)
            .AppendLine("(int value) => value;");
        if (!string.IsNullOrWhiteSpace(seed.Attributes))
        {
            builder.Append("    ").AppendLine(seed.Attributes);
        }

        builder.Append("    public static int ")
            .Append(methodName)
            .Append("(int ")
            .Append(inputName)
            .AppendLine(") {");
        builder.Append("        ").Append(trivia).AppendLine(prelude);
        builder.Append("        ").AppendLine(body);
        builder.AppendLine("    }");
        builder.AppendLine("}");
        var caseId = $"{seed.Id}.{VariantKey(variant)}";
        return new CorpusCase(
            caseId,
            seed.Id,
            variant,
            seed.Mode,
            seed.ExpectedVerdict,
            seed.Support,
            builder.ToString());
    }

    private static string CreatePrelude(
        CorpusVariant variant,
        string helper,
        string input)
    {
        return variant switch
        {
            CorpusVariant.Parentheses =>
                $"var probe = {helper}((((({input}))))); _ = probe;",
            CorpusVariant.Temporary =>
                $"var source = {input}; var probe = {helper}(source); _ = probe;",
            CorpusVariant.IfTrue =>
                $"var probe = {input}; if (true) {{ probe = {helper}({input}); }} _ = probe;",
            CorpusVariant.NamedArguments =>
                $"var probe = {helper}(value: {input}); _ = probe;",
            CorpusVariant.ReorderIndependentStatements =>
                $"var independentRight = 1; " +
                $"var independentLeft = {helper}({input}); " +
                "_ = independentLeft; _ = independentRight;",
            _ =>
                $"var probe = {helper}({input}); _ = probe;"
        };
    }

    private static string AlphaRenameContractFormals(string members)
    {
        return members
            .Replace(
                "Positive(int value) { Contract.Requires(value > 0); }",
                "Positive(int contractValue) { " +
                "Contract.Requires(contractValue > 0); }",
                StringComparison.Ordinal)
            .Replace(
                "EqualTo(int left, int right) { " +
                "Contract.Requires(left == right); }",
                "EqualTo(int contractLeft, int contractRight) { " +
                "Contract.Requires(contractLeft == contractRight); }",
                StringComparison.Ordinal)
            .Replace(
                "Range(int minimum, int value) { " +
                "Contract.Requires(value >= minimum); }",
                "Range(int contractMinimum, int contractValue) { " +
                "Contract.Requires(contractValue >= contractMinimum); }",
                StringComparison.Ordinal)
            .Replace(
                "MustBe(bool condition) { Contract.Requires(condition); }",
                "MustBe(bool contractCondition) { " +
                "Contract.Requires(contractCondition); }",
                StringComparison.Ordinal)
            .Replace(
                "NonNegative(int value) { Contract.Requires(value >= 0); }",
                "NonNegative(int contractValue) { " +
                "Contract.Requires(contractValue >= 0); }",
                StringComparison.Ordinal)
            .Replace(
                "Between(int value) { " +
                "Contract.Requires(value >= 0 && value <= 10); }",
                "Between(int contractValue) { " +
                "Contract.Requires(contractValue >= 0 && " +
                "contractValue <= 10); }",
                StringComparison.Ordinal);
    }

    private static string ReplaceTokens(
        string value,
        string helper,
        string input)
    {
        return value.Replace("$HELPER$", helper, StringComparison.Ordinal)
            .Replace("$INPUT$", input, StringComparison.Ordinal);
    }

    internal static string VariantKey(CorpusVariant variant)
    {
        return variant switch
        {
            CorpusVariant.Baseline => "baseline",
            CorpusVariant.Rename => "rename",
            CorpusVariant.EscapedIdentifiers => "escaped",
            CorpusVariant.Trivia => "trivia",
            CorpusVariant.Parentheses => "parentheses",
            CorpusVariant.Temporary => "temporary",
            CorpusVariant.IfTrue => "if-true",
            CorpusVariant.NamedArguments => "named-arguments",
            CorpusVariant.AlphaRenameContractFormals =>
                "alpha-rename-contract-formals",
            CorpusVariant.ReorderIndependentStatements =>
                "reorder-independent-statements",
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
    }
}
