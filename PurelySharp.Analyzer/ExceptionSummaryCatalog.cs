using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PurelySharp.Analyzer
{
    internal sealed class ExceptionSummaryCatalog
    {
        private const string SummaryFileName = "PurelySharp.EffectSummary.json";
        private static readonly SymbolDisplayFormat EffectSummaryContainingTypeFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        private static readonly SymbolDisplayFormat EffectSummaryParameterTypeFormat = new SymbolDisplayFormat(
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static readonly ExceptionSummaryCatalog Empty = new ExceptionSummaryCatalog(
            ImmutableDictionary<string, ImmutableArray<SummaryEntry>>.Empty);

        private static readonly ConcurrentDictionary<string, ActualAssemblyIdentity?> AssemblyIdentityCache =
            new ConcurrentDictionary<string, ActualAssemblyIdentity?>(StringComparer.OrdinalIgnoreCase);

        private readonly ImmutableDictionary<string, ImmutableArray<SummaryEntry>> _entriesBySymbol;

        private ExceptionSummaryCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
        {
            _entriesBySymbol = entriesBySymbol;
        }

        public static ExceptionSummaryCatalog FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
        {
            var entriesBySymbol = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (!IsSummaryFile(additionalFile.Path))
                {
                    continue;
                }

                var text = additionalFile.GetText(cancellationToken)?.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                foreach (var entry in ParseEntries(text!))
                {
                    if (!entriesBySymbol.TryGetValue(entry.Symbol, out var builder))
                    {
                        builder = ImmutableArray.CreateBuilder<SummaryEntry>();
                        entriesBySymbol.Add(entry.Symbol, builder);
                    }

                    builder.Add(entry);
                }
            }

            if (entriesBySymbol.Count == 0)
            {
                return Empty;
            }

            return new ExceptionSummaryCatalog(entriesBySymbol.ToImmutableDictionary(
                item => item.Key,
                item => item.Value.ToImmutable(),
                StringComparer.Ordinal));
        }

        public bool TryGetExceptions(IMethodSymbol methodSymbol, out ImmutableArray<string> exceptionTypes)
        {
            return TryGetExceptions(methodSymbol, compilation: null, out exceptionTypes);
        }

        public bool TryGetExceptions(
            IMethodSymbol methodSymbol,
            Compilation? compilation,
            out ImmutableArray<string> exceptionTypes)
        {
            var matchedExceptionTypes = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
            var actualAssemblyIdentity = compilation is null
                ? null
                : TryResolveActualAssemblyIdentity(methodSymbol, compilation);

            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (!_entriesBySymbol.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (!entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity))
                    {
                        continue;
                    }

                    matchedExceptionTypes.UnionWith(entry.ExceptionTypes);
                }
            }

            if (matchedExceptionTypes.Count == 0)
            {
                exceptionTypes = ImmutableArray<string>.Empty;
                return false;
            }

            exceptionTypes = matchedExceptionTypes.ToImmutableArray();
            return true;
        }

        private static bool IsSummaryFile(string path)
        {
            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, SummaryFileName, StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("." + SummaryFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<SummaryEntry> ParseEntries(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Assemblies", out var assembliesElement) ||
                assembliesElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var assemblyElement in assembliesElement.EnumerateArray())
            {
                var assemblyIdentity = SummaryAssemblyIdentity.FromJson(assemblyElement);
                if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                    methodsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var methodElement in methodsElement.EnumerateArray())
                {
                    var symbol = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "Symbol");
                    if (symbol == null)
                    {
                        continue;
                    }

                    var exceptionTypes = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                    AddExceptionTypes(exceptionTypes, methodElement, "ThrownExceptionTypes");
                    AddExceptionTypes(exceptionTypes, methodElement, "TransitiveThrownExceptionTypes");
                    if (exceptionTypes.Count == 0)
                    {
                        continue;
                    }
                    yield return new SummaryEntry(symbol, exceptionTypes.ToImmutableArray(), assemblyIdentity);
                }
            }
        }

        private static void AddExceptionTypes(
            ImmutableSortedSet<string>.Builder exceptionTypes,
            JsonElement methodElement,
            string propertyName)
        {
            if (!methodElement.TryGetProperty(propertyName, out var valuesElement) ||
                valuesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var valueElement in valuesElement.EnumerateArray())
            {
                if (valueElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = valueElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    exceptionTypes.Add(value.Trim());
                }
            }
        }

        private static IEnumerable<string> GetSymbolKeys(IMethodSymbol methodSymbol)
        {
            var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            AddSymbolKey(keys, methodSymbol.OriginalDefinition.ToDisplayString());
            AddSymbolKey(keys, methodSymbol.ToDisplayString());
            AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol));

            if (methodSymbol.IsGenericMethod)
            {
                AddSymbolKey(keys, methodSymbol.ConstructedFrom.ToDisplayString());
                AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol.ConstructedFrom));
            }

            return keys;
        }

        private static void AddSymbolKey(ImmutableHashSet<string>.Builder keys, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys.Add(value.Trim());
            }
        }

        private static string CreateEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(EffectSummaryContainingTypeFormat);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(EffectSummaryParameterTypeFormat)));
            return containingTypeName + "." + methodName + "(" + parameterList + ")";
        }

        private static ActualAssemblyIdentity? TryResolveActualAssemblyIdentity(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return null;
            }

            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(assemblySymbol, methodSymbol.ContainingAssembly))
                {
                    continue;
                }

                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
                {
                    return null;
                }

                return AssemblyIdentityCache.GetOrAdd(referencePath, static path => ActualAssemblyIdentity.FromFile(path));
            }

            return null;
        }

        private sealed class SummaryEntry
        {
            public SummaryEntry(
                string symbol,
                ImmutableArray<string> exceptionTypes,
                SummaryAssemblyIdentity? assemblyIdentity)
            {
                Symbol = symbol;
                ExceptionTypes = exceptionTypes;
                AssemblyIdentity = assemblyIdentity;
            }

            public string Symbol { get; }

            public ImmutableArray<string> ExceptionTypes { get; }

            public SummaryAssemblyIdentity? AssemblyIdentity { get; }

            public bool IsTrustedFor(IMethodSymbol methodSymbol, ActualAssemblyIdentity? actualAssemblyIdentity)
            {
                if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
                {
                    return false;
                }

                return AssemblyIdentity != null &&
                    AssemblyIdentity.IsComplete &&
                    actualAssemblyIdentity != null &&
                    AssemblyIdentity.Matches(actualAssemblyIdentity);
            }
        }

        private sealed class SummaryAssemblyIdentity
        {
            public SummaryAssemblyIdentity(
                string? assemblyName,
                string? assemblySha256,
                string? moduleVersionId)
            {
                AssemblyName = assemblyName;
                AssemblySha256 = assemblySha256;
                ModuleVersionId = moduleVersionId;
            }

            public string? AssemblyName { get; }

            public string? AssemblySha256 { get; }

            public string? ModuleVersionId { get; }

            public bool IsComplete =>
                !string.IsNullOrWhiteSpace(AssemblyName) &&
                !string.IsNullOrWhiteSpace(AssemblySha256) &&
                !string.IsNullOrWhiteSpace(ModuleVersionId);

            public bool Matches(ActualAssemblyIdentity actualAssemblyIdentity)
            {
                return string.Equals(AssemblyName, actualAssemblyIdentity.AssemblyName, StringComparison.Ordinal) &&
                    string.Equals(AssemblySha256, actualAssemblyIdentity.AssemblySha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ModuleVersionId, actualAssemblyIdentity.ModuleVersionId, StringComparison.OrdinalIgnoreCase);
            }

            public static SummaryAssemblyIdentity? FromJson(JsonElement assemblyElement)
            {
                var assemblyName = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblyName");
                var assemblySha256 = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblySha256");
                var moduleVersionId = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "ModuleVersionId");
                if (string.IsNullOrWhiteSpace(assemblyName) &&
                    string.IsNullOrWhiteSpace(assemblySha256) &&
                    string.IsNullOrWhiteSpace(moduleVersionId))
                {
                    return null;
                }

                return new SummaryAssemblyIdentity(
                    assemblyName?.Trim(),
                    assemblySha256?.Trim(),
                    moduleVersionId?.Trim());
            }
        }

        private sealed class ActualAssemblyIdentity
        {
            public ActualAssemblyIdentity(string assemblyName, string assemblySha256, string moduleVersionId)
            {
                AssemblyName = assemblyName;
                AssemblySha256 = assemblySha256;
                ModuleVersionId = moduleVersionId;
            }

            public string AssemblyName { get; }

            public string AssemblySha256 { get; }

            public string ModuleVersionId { get; }

            public static ActualAssemblyIdentity? FromFile(string path)
            {
                using var stream = File.OpenRead(path);
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                {
                    return null;
                }

                var metadataReader = peReader.GetMetadataReader();
                var assemblyName = metadataReader.IsAssembly
                    ? metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name)
                    : Path.GetFileNameWithoutExtension(path);
                var moduleVersionId = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid).ToString("D");
                var assemblySha256 = ComputeSha256(path);

                return new ActualAssemblyIdentity(assemblyName, assemblySha256, moduleVersionId);
            }

            private static string ComputeSha256(string path)
            {
                using var stream = File.OpenRead(path);
                using var sha256 = SHA256.Create();
                return CompatibilityHelpers.ToLowerHex(sha256.ComputeHash(stream));
            }
        }
    }
}
