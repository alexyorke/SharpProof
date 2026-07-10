using System.Reflection;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class ConfigurationProfileTests
    {
        private static readonly string[] Modes = { "migration", "audit", "ci", "strict" };
        private static readonly HashSet<string> AllowedSeverities = new(StringComparer.Ordinal)
        {
            "none",
            "silent",
            "suggestion",
            "warning",
            "error",
        };

        [TestCaseSource(nameof(Modes))]
        public void ProfilePair_CoversDiagnosticsAndUsesOptionsAtValidScopes(string mode)
        {
            var repositoryRoot = FindRepositoryRoot();
            var profilesDirectory = Path.Combine(repositoryRoot, "config", "profiles");
            var editorFileName = $"sharpproof-{mode}.editorconfig";
            var globalFileName = $"sharpproof-{mode}.globalconfig";
            var editorEntries = ReadEntries(Path.Combine(profilesDirectory, editorFileName));
            var globalEntries = ReadEntries(Path.Combine(profilesDirectory, globalFileName));
            var optionScopes = GetOptionScopes();
            var globalOnlyOptions = optionScopes
                .Where(option => option.Value == "GlobalOnly")
                .Select(option => option.Key)
                .ToHashSet(StringComparer.Ordinal);

            Assert.That(GetValue(editorEntries, "root"), Is.EqualTo("true"));
            Assert.That(GetValue(globalEntries, "is_global"), Is.EqualTo("true"));
            AssertProfileEntries(editorEntries, optionScopes, globalOnlyOptions, allowGlobalOnlyOptions: false);
            AssertProfileEntries(globalEntries, optionScopes, globalOnlyOptions, allowGlobalOnlyOptions: true);

            var editorPolicy = editorEntries
                .Where(entry => entry.Key != "root")
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToArray();
            var globalTreePolicy = globalEntries
                .Where(entry => entry.Key != "is_global" && !globalOnlyOptions.Contains(entry.Key))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToArray();
            Assert.That(globalTreePolicy, Is.EqualTo(editorPolicy));

            var documentation = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "configuration-profiles.md"));
            Assert.That(documentation, Does.Contain(editorFileName));
            Assert.That(documentation, Does.Contain(globalFileName));
        }

        [Test]
        public void ProfilesDirectory_ContainsEveryDocumentedModeAndFormat()
        {
            var profilesDirectory = Path.Combine(FindRepositoryRoot(), "config", "profiles");
            var expectedFiles = Modes
                .SelectMany(mode => new[]
                {
                    $"sharpproof-{mode}.editorconfig",
                    $"sharpproof-{mode}.globalconfig",
                })
                .OrderBy(fileName => fileName, StringComparer.Ordinal)
                .ToArray();
            var actualFiles = Directory
                .EnumerateFiles(profilesDirectory)
                .Select(Path.GetFileName)
                .OrderBy(fileName => fileName, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actualFiles, Is.EqualTo(expectedFiles));
        }

        private static void AssertProfileEntries(
            ProfileEntry[] entries,
            IReadOnlyDictionary<string, string> optionScopes,
            HashSet<string> globalOnlyOptions,
            bool allowGlobalOnlyOptions)
        {
            var duplicateKeys = entries
                .GroupBy(entry => entry.Key, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            Assert.That(duplicateKeys, Is.Empty);

            var severityEntries = entries
                .Where(entry => entry.Key.StartsWith("dotnet_diagnostic.SP", StringComparison.Ordinal) &&
                                entry.Key.EndsWith(".severity", StringComparison.Ordinal))
                .ToArray();
            var configuredDiagnosticIds = severityEntries
                .Select(entry => entry.Key.Substring("dotnet_diagnostic.".Length, "SP0000".Length))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var supportedDiagnosticIds = new SharpProofAnalyzer().SupportedDiagnostics
                .Select(descriptor => descriptor.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            Assert.That(configuredDiagnosticIds, Is.EqualTo(supportedDiagnosticIds));
            Assert.That(severityEntries.Select(entry => entry.Value), Is.All.Matches<string>(AllowedSeverities.Contains));

            var sharpProofEntries = entries
                .Where(entry => entry.Key.StartsWith("sharpproof_", StringComparison.Ordinal))
                .ToArray();
            Assert.That(
                sharpProofEntries.Select(entry => entry.Key),
                Is.All.Matches<string>(optionScopes.ContainsKey));
            if (!allowGlobalOnlyOptions)
            {
                Assert.That(
                    sharpProofEntries.Select(entry => entry.Key),
                    Has.None.Matches<string>(globalOnlyOptions.Contains));
            }
        }

        private static ProfileEntry[] ReadEntries(string path)
        {
            return File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith('['))
                .Select(line =>
                {
                    var separatorIndex = line.IndexOf('=');
                    Assert.That(separatorIndex, Is.GreaterThan(0), $"Invalid profile assignment in {path}: {line}");
                    return new ProfileEntry(
                        line.Substring(0, separatorIndex).Trim(),
                        line.Substring(separatorIndex + 1).Trim());
                })
                .ToArray();
        }

        private static string GetValue(IEnumerable<ProfileEntry> entries, string key)
        {
            return entries.Single(entry => entry.Key == key).Value;
        }

        private static Dictionary<string, string> GetOptionScopes()
        {
            var registryType = typeof(SharpProofAnalyzer).Assembly
                .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfigurationOptionRegistry", throwOnError: true)!;
            var options = (System.Collections.IEnumerable)registryType
                .GetProperty("All", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;

            return options
                .Cast<object>()
                .ToDictionary(
                    option => (string)option.GetType().GetProperty("Key")!.GetValue(option)!,
                    option => option.GetType().GetProperty("Scope")!.GetValue(option)!.ToString()!,
                    StringComparer.Ordinal);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PLAN.md")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }

        private readonly record struct ProfileEntry(string Key, string Value);
    }
}
