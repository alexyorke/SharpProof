using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Configuration
{

    internal class AnalyzerConfiguration
    {
        public ImmutableHashSet<string> ExtraKnownImpureMethods { get; }
        public ImmutableHashSet<string> ExtraKnownPureMethods { get; }
        public ImmutableHashSet<string> ExtraKnownImpureNamespaces { get; }
        public ImmutableHashSet<string> ExtraKnownImpureTypes { get; }
        public bool EnableDebugLogging { get; }
        public bool SuggestMissingEnforcePure { get; }
        public MissingPuritySuggestionOptions MissingPuritySuggestions { get; }
        public bool EmitExplanations { get; }
        public bool ReportBclFallbackGuesses { get; }
        public RuntimeHazardMode RuntimeHazardMode { get; }
        public bool ReportExceptions { get; }
        public bool CheckedExceptions { get; }
        public bool EnableEffectSummaryJson { get; }
        public string PurityProfile { get; }
        public SmtAnalysisOptions SmtOptions { get; }

        private AnalyzerConfiguration(
            ImmutableHashSet<string> extraImpureMethods,
            ImmutableHashSet<string> extraPureMethods,
            ImmutableHashSet<string> extraImpureNamespaces,
            ImmutableHashSet<string> extraImpureTypes,
            bool enableDebugLogging,
            bool suggestMissingEnforcePure,
            MissingPuritySuggestionOptions missingPuritySuggestions,
            bool emitExplanations,
            bool reportBclFallbackGuesses,
            RuntimeHazardMode runtimeHazardMode,
            bool reportExceptions,
            bool checkedExceptions,
            bool enableEffectSummaryJson,
            string purityProfile,
            SmtAnalysisOptions smtOptions)
        {
            ExtraKnownImpureMethods = extraImpureMethods;
            ExtraKnownPureMethods = extraPureMethods;
            ExtraKnownImpureNamespaces = extraImpureNamespaces;
            ExtraKnownImpureTypes = extraImpureTypes;
            EnableDebugLogging = enableDebugLogging;
            SuggestMissingEnforcePure = suggestMissingEnforcePure;
            MissingPuritySuggestions = missingPuritySuggestions;
            EmitExplanations = emitExplanations;
            ReportBclFallbackGuesses = reportBclFallbackGuesses;
            RuntimeHazardMode = runtimeHazardMode;
            ReportExceptions = reportExceptions;
            CheckedExceptions = checkedExceptions;
            EnableEffectSummaryJson = enableEffectSummaryJson;
            PurityProfile = purityProfile;
            SmtOptions = smtOptions;
        }

        public static AnalyzerConfiguration FromOptions(AnalyzerOptions options)
        {
            var impureMethods = GetValues(options, ConfigKeys.KnownImpureMethods);
            var pureMethods = GetValues(options, ConfigKeys.KnownPureMethods);
            var impureNamespaces = GetValues(options, ConfigKeys.KnownImpureNamespaces);
            var impureTypes = GetValues(options, ConfigKeys.KnownImpureTypes);
            bool debug = GetBool(options, "sharpproof_enable_debug_logging");
            bool suggestMissing = GetBoolOrDefaultTrue(options, ConfigKeys.SuggestMissingEnforcePure);
            var missingPuritySuggestions = new MissingPuritySuggestionOptions(
                suggestMissing,
                GetMissingPuritySuggestionScope(options),
                GetBool(options, ConfigKeys.SuggestMissingEnforcePureExcludeGenerated),
                GetBool(options, ConfigKeys.SuggestMissingEnforcePureExcludeTests),
                GetNonNegativeInt(options, ConfigKeys.SuggestMissingEnforcePureMinComplexity),
                GetValues(options, ConfigKeys.SuggestMissingEnforcePureNamespaceFilters));
            bool emitExplanations = GetBool(options, ConfigKeys.EmitExplanations);
            bool reportBclFallbackGuesses = GetBool(options, ConfigKeys.ReportBclFallbackGuesses);
            var runtimeHazardMode = GetRuntimeHazardMode(options, RuntimeHazardMode.Off);
            bool reportExceptions = GetBool(options, ConfigKeys.ReportExceptions);
            bool checkedExceptions = GetBool(options, ConfigKeys.CheckedExceptions);
            bool enableEffectSummaryJson = GetBool(options, ConfigKeys.EnableEffectSummaryJson);
            return new AnalyzerConfiguration(
                impureMethods,
                pureMethods,
                impureNamespaces,
                impureTypes,
                debug,
                suggestMissing,
                missingPuritySuggestions,
                emitExplanations,
                reportBclFallbackGuesses,
                runtimeHazardMode,
                reportExceptions,
                checkedExceptions,
                enableEffectSummaryJson,
                GetPurityProfile(options),
                GetSmtOptions(options));
        }

        public static MissingPuritySuggestionOptions GetMissingPuritySuggestionOptions(
            AnalyzerOptions options,
            SyntaxTree syntaxTree,
            MissingPuritySuggestionOptions fallback)
        {
            try
            {
                var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
                bool suggestMissing = GetBoolOrDefault(treeOptions, ConfigKeys.SuggestMissingEnforcePure, fallback.Enabled);
                return new MissingPuritySuggestionOptions(
                    suggestMissing,
                    GetMissingPuritySuggestionScope(treeOptions, fallback.Scope),
                    GetBoolOrDefault(treeOptions, ConfigKeys.SuggestMissingEnforcePureExcludeGenerated, fallback.ExcludeGeneratedFiles),
                    GetBoolOrDefault(treeOptions, ConfigKeys.SuggestMissingEnforcePureExcludeTests, fallback.ExcludeTestFiles),
                    GetNonNegativeInt(treeOptions, ConfigKeys.SuggestMissingEnforcePureMinComplexity, fallback.MinimumComplexity),
                    GetValues(treeOptions, ConfigKeys.SuggestMissingEnforcePureNamespaceFilters, fallback.NamespaceFilters));
            }
            catch
            {
                return fallback;
            }
        }

        public static bool GetEmitExplanations(
            AnalyzerOptions options,
            SyntaxTree syntaxTree,
            bool fallback)
        {
            try
            {
                var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
                return GetBoolOrDefault(treeOptions, ConfigKeys.EmitExplanations, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        public static bool GetReportBclFallbackGuesses(
            AnalyzerOptions options,
            SyntaxTree syntaxTree,
            bool fallback)
        {
            try
            {
                var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
                return GetBoolOrDefault(treeOptions, ConfigKeys.ReportBclFallbackGuesses, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        public static bool GetReportExceptions(
            AnalyzerOptions options,
            SyntaxTree syntaxTree,
            bool fallback)
        {
            try
            {
                var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
                return GetBoolOrDefault(treeOptions, ConfigKeys.ReportExceptions, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        public static bool GetCheckedExceptions(
            AnalyzerOptions options,
            SyntaxTree syntaxTree,
            bool fallback)
        {
            try
            {
                var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
                return GetBoolOrDefault(treeOptions, ConfigKeys.CheckedExceptions, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        public static RuntimeHazardMode GetRuntimeHazardMode(
            AnalyzerOptions options,
            SyntaxTree syntaxTree,
            RuntimeHazardMode fallback)
        {
            try
            {
                var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
                return GetRuntimeHazardMode(treeOptions, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        public static bool RuntimeHazardReportsMethodSummaries(RuntimeHazardMode mode)
        {
            return mode == RuntimeHazardMode.Summaries || mode == RuntimeHazardMode.All;
        }

        public static bool RuntimeHazardReportsSites(RuntimeHazardMode mode)
        {
            return mode == RuntimeHazardMode.Sites || mode == RuntimeHazardMode.All;
        }

        private static ImmutableHashSet<string> GetValues(AnalyzerOptions options, string key)
        {
            var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            if (TryGetGlobalOption(options, key, out var value))
            {
                foreach (var token in value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var item = token.Trim();
                    if (item.Length > 0)
                    {
                        builder.Add(item);
                    }
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableHashSet<string> GetValues(
            AnalyzerConfigOptions options,
            string key,
            ImmutableHashSet<string> fallback)
        {
            var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            if (!options.TryGetValue(key, out var value))
            {
                return fallback;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return builder.ToImmutable();
            }

            foreach (var token in value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var item = token.Trim();
                if (item.Length > 0)
                {
                    builder.Add(item);
                }
            }

            return builder.ToImmutable();
        }

        private static bool GetBool(AnalyzerOptions options, string key)
        {
            return TryGetGlobalOption(options, key, out var value) &&
                   TryParseBool(value, out var parsed) &&
                   parsed;
        }

        private static bool GetBoolOrDefaultTrue(AnalyzerOptions options, string key)
        {
            if (!TryGetGlobalOption(options, key, out var value))
            {
                return true;
            }

            if (TryParseBool(value, out var parsed))
            {
                return parsed;
            }

            return true;
        }

        private static bool GetBoolOrDefault(AnalyzerConfigOptions options, string key, bool fallback)
        {
            if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return TryParseBool(value, out var parsed) ? parsed : fallback;
        }

        private static bool TryParseBool(string value, out bool parsed)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    parsed = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    parsed = false;
                    return true;
                default:
                    parsed = false;
                    return false;
            }
        }

        private static MissingPuritySuggestionScope GetMissingPuritySuggestionScope(AnalyzerOptions options)
        {
            if (TryGetGlobalOption(options, ConfigKeys.SuggestMissingEnforcePureScope, out var value))
            {
                switch (value.Trim().ToLowerInvariant())
                {
                    case "all":
                        return MissingPuritySuggestionScope.All;
                    case "public":
                    case "public-only":
                        return MissingPuritySuggestionScope.Public;
                    case "internal":
                    case "internal-only":
                        return MissingPuritySuggestionScope.Internal;
                    case "off":
                    case "none":
                    case "false":
                        return MissingPuritySuggestionScope.Off;
                }
            }

            return MissingPuritySuggestionScope.All;
        }

        private static string GetPurityProfile(AnalyzerOptions options)
        {
            if (TryGetGlobalOption(options, ConfigKeys.PurityProfile, out var value))
            {
                var normalized = value.Trim().ToLowerInvariant();
                if (normalized == "strict" || normalized == "balanced" || normalized == "pragmatic")
                {
                    return normalized;
                }
            }

            return "balanced";
        }

        private static RuntimeHazardMode GetRuntimeHazardMode(AnalyzerOptions options, RuntimeHazardMode fallback)
        {
            return TryGetGlobalOption(options, ConfigKeys.RuntimeHazardMode, out var value)
                ? ParseRuntimeHazardMode(value, fallback)
                : fallback;
        }

        private static RuntimeHazardMode GetRuntimeHazardMode(AnalyzerConfigOptions options, RuntimeHazardMode fallback)
        {
            if (options.TryGetValue(ConfigKeys.RuntimeHazardMode, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return ParseRuntimeHazardMode(value, fallback);
            }

            return fallback;
        }

        private static RuntimeHazardMode ParseRuntimeHazardMode(string value, RuntimeHazardMode fallback)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "none":
                case "disabled":
                    return RuntimeHazardMode.Off;
                case "sites":
                case "site":
                case "checked":
                case "checked-exceptions":
                case "warnings":
                case "warning":
                    return RuntimeHazardMode.Sites;
                case "summaries":
                case "summary":
                case "method-summaries":
                case "method-summary":
                case "report":
                    return RuntimeHazardMode.Summaries;
                case "all":
                case "both":
                    return RuntimeHazardMode.All;
            }

            if (TryParseBool(value, out var parsed))
            {
                return parsed ? RuntimeHazardMode.Sites : RuntimeHazardMode.Off;
            }

            return fallback;
        }

        private static SmtAnalysisOptions GetSmtOptions(AnalyzerOptions options)
        {
            var mode = GetSmtMode(options, SmtAnalysisOptions.Default.Mode);
            var defaults = SmtAnalysisOptions.ForMode(mode);
            var timeoutMs = GetPositiveInt(options, ConfigKeys.SmtTimeoutMs, (int)defaults.QueryTimeout.TotalMilliseconds);
            var methodBudgetMs = GetPositiveInt(options, ConfigKeys.SmtMethodBudgetMs, (int)defaults.MethodBudget.TotalMilliseconds);
            var maxPathConditions = GetPositiveInt(options, ConfigKeys.SmtMaxPathConditions, defaults.MaxPathConditions);
            var maxExpressionNodes = GetPositiveInt(options, ConfigKeys.SmtMaxExpressionNodes, defaults.MaxExpressionNodes);
            return new SmtAnalysisOptions(
                mode,
                TimeSpan.FromMilliseconds(timeoutMs),
                TimeSpan.FromMilliseconds(methodBudgetMs),
                maxPathConditions,
                maxExpressionNodes,
                useSharedResultCache: true);
        }

        private static SmtAnalysisMode GetSmtMode(AnalyzerOptions options, SmtAnalysisMode fallback)
        {
            if (TryGetGlobalOption(options, ConfigKeys.SmtMode, out var value))
            {
                switch (value.Trim().ToLowerInvariant())
                {
                    case "disabled":
                        return SmtAnalysisMode.Off;
                    case "bounded":
                    case "default":
                        return SmtAnalysisMode.Bounded;
                    case "deep":
                    case "aggressive":
                        return SmtAnalysisMode.Deep;
                }

                if (TryParseBool(value, out var parsed))
                {
                    return parsed ? SmtAnalysisMode.Bounded : SmtAnalysisMode.Off;
                }
            }

            return fallback;
        }

        private static int GetPositiveInt(AnalyzerOptions options, string key, int fallback)
        {
            if (TryGetGlobalOption(options, key, out var value) &&
                int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                parsed > 0)
            {
                return parsed;
            }

            return fallback;
        }

        private static MissingPuritySuggestionScope GetMissingPuritySuggestionScope(
            AnalyzerConfigOptions options,
            MissingPuritySuggestionScope fallback)
        {
            if (options.TryGetValue(ConfigKeys.SuggestMissingEnforcePureScope, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                switch (value.Trim().ToLowerInvariant())
                {
                    case "all":
                        return MissingPuritySuggestionScope.All;
                    case "public":
                    case "public-only":
                        return MissingPuritySuggestionScope.Public;
                    case "internal":
                    case "internal-only":
                        return MissingPuritySuggestionScope.Internal;
                    case "off":
                    case "none":
                    case "false":
                        return MissingPuritySuggestionScope.Off;
                }
            }

            return fallback;
        }

        private static int GetNonNegativeInt(AnalyzerOptions options, string key)
        {
            if (TryGetGlobalOption(options, key, out var value) &&
                int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                parsed >= 0)
            {
                return parsed;
            }

            return 0;
        }

        private static int GetNonNegativeInt(AnalyzerConfigOptions options, string key, int fallback)
        {
            return options.TryGetValue(key, out var value) &&
                   int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                   parsed >= 0
                ? parsed
                : fallback;
        }

        private static bool TryGetGlobalOption(AnalyzerOptions options, string key, out string value)
        {
            try
            {
                var global = options.AnalyzerConfigOptionsProvider.GlobalOptions;
                if (global.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
                {
                    value = found;
                    return true;
                }
            }
            catch
            {
            }

            value = string.Empty;
            return false;
        }
    }

    internal enum MissingPuritySuggestionScope
    {
        All,
        Public,
        Internal,
        Off
    }

    internal enum RuntimeHazardMode
    {
        Off,
        Sites,
        Summaries,
        All
    }

    internal sealed class MissingPuritySuggestionOptions
    {
        public MissingPuritySuggestionOptions(
            bool enabled,
            MissingPuritySuggestionScope scope,
            bool excludeGeneratedFiles,
            bool excludeTestFiles,
            int minimumComplexity,
            ImmutableHashSet<string> namespaceFilters)
        {
            Enabled = enabled;
            Scope = scope;
            ExcludeGeneratedFiles = excludeGeneratedFiles;
            ExcludeTestFiles = excludeTestFiles;
            MinimumComplexity = minimumComplexity;
            NamespaceFilters = namespaceFilters;
        }

        public bool Enabled { get; }
        public MissingPuritySuggestionScope Scope { get; }
        public bool ExcludeGeneratedFiles { get; }
        public bool ExcludeTestFiles { get; }
        public int MinimumComplexity { get; }
        public ImmutableHashSet<string> NamespaceFilters { get; }

        public bool IsEnabled => Enabled && Scope != MissingPuritySuggestionScope.Off;
    }
}
