using System;
using System.IO;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer
{
    internal static class BuiltInEffectSummaryLoader
    {
        internal const string SummaryFileName = "SharpProof.EffectSummary.json";

        internal static void LoadBuiltInSummaryJsonDocuments(Action<string> addJson)
        {
            LoadEmbeddedSummaryJsonDocuments(addJson);
        }

        internal static void LoadAdditionalSummaryJsonDocuments(
            AnalyzerOptions options,
            CancellationToken cancellationToken,
            Action<string> addJson)
        {
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (!IsSummaryFile(additionalFile.Path))
                {
                    continue;
                }

                var text = additionalFile.GetText(cancellationToken)?.ToString();
                if (text == null || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                addJson(text);
            }
        }

        internal static bool HasAdditionalSummaryJsonDocuments(AnalyzerOptions options)
        {
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (IsSummaryFile(additionalFile.Path))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsSummaryFile(string path)
        {
            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, SummaryFileName, StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("." + SummaryFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static void LoadEmbeddedSummaryJsonDocuments(Action<string> addJson)
        {
            var assembly = typeof(BuiltInEffectSummaryLoader).Assembly;
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!IsSummaryResource(resourceName))
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                addJson(reader.ReadToEnd());
            }
        }

        private static bool IsSummaryResource(string resourceName)
        {
            return resourceName.EndsWith("." + SummaryFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resourceName, SummaryFileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
