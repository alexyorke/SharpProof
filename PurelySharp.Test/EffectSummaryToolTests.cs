using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace PurelySharp.Test
{
    [TestFixture]
    public class EffectSummaryToolTests
    {
        [Test]
        public async Task EffectSummaryTool_CollectsCommonDirectExceptionTypes()
        {
            var source = """
using System;

public static class ExceptionFixture
{
    public static void ThrowIndexOutOfRange() => throw new IndexOutOfRangeException();

    public static void ThrowInvalidCast() => throw new InvalidCastException();

    public static void ThrowObjectDisposed() => throw new ObjectDisposedException("stream");

    public static void ThrowFormat() => throw new FormatException();

    public static void ThrowOverflow() => throw new OverflowException();
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryCommonExceptions", source);
            using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            AssertThrownExceptions(summary, "ExceptionFixture.ThrowIndexOutOfRange()", "System.IndexOutOfRangeException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowInvalidCast()", "System.InvalidCastException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowObjectDisposed()", "System.ObjectDisposedException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowFormat()", "System.FormatException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowOverflow()", "System.OverflowException");
        }

        [Test]
        public async Task EffectSummaryTool_SuppressesCaughtThrows_And_PreservesRethrowAndTransitiveExceptions()
        {
            var source = """
using System;

public static class ExceptionFixture
{
    public static void ThrowDirect()
    {
        throw new InvalidOperationException("boom");
    }

    public static void ThrowViaLocal()
    {
        var ex = new ObjectDisposedException("stream");
        throw ex;
    }

    public static void ThrowViaCallee()
    {
        ThrowDirect();
    }

    public static int HandleLocally()
    {
        try
        {
            throw new FormatException();
        }
        catch (FormatException)
        {
            return 1;
        }
    }

    public static void RethrowOverflow()
    {
        try
        {
            throw new OverflowException();
        }
        catch (OverflowException)
        {
            throw;
        }
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryControlFlow", source);
            using var summary = await RunEffectSummaryAsync(fixture.AssemblyPath, includeTransitiveRoots: true);

            AssertThrownExceptions(summary, "ExceptionFixture.ThrowDirect()", "System.InvalidOperationException");
            AssertThrownExceptions(summary, "ExceptionFixture.ThrowViaLocal()", "System.ObjectDisposedException");
            AssertTransitiveExceptions(summary, "ExceptionFixture.ThrowViaCallee()", "System.InvalidOperationException");
            AssertThrownExceptions(summary, "ExceptionFixture.HandleLocally()");
            AssertThrownExceptions(summary, "ExceptionFixture.RethrowOverflow()", "System.OverflowException");
        }

        [Test]
        public async Task EffectSummaryTool_Produces_ReportOnly_Purity_Classifications()
        {
            var source = """
using System;

public interface IWorker
{
    int Get();
}

public abstract class AbstractWorker
{
    public abstract int Get();
}

public static class PurityFixture
{
    private static int _state;

    public static int PureLeaf() => 42;

    public static int PureViaCallee() => PureLeaf();

    public static int ImpureWrite()
    {
        _state++;
        return _state;
    }

    public static int ImpureViaCallee() => ImpureWrite();

    public static int UnknownViaInterface(IWorker worker) => worker.Get();

    public static byte[] PureFreshArray()
    {
        var bytes = new byte[4];
        bytes[0] = 1;
        return bytes;
    }

    public static void MutateCallerArray(byte[] bytes)
    {
        bytes[0] = 1;
    }
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryPurityClassification", source);
            using var summary = await RunEffectSummaryAsync(
                fixture.AssemblyPath,
                includeTransitiveRoots: true,
                classifyPurity: true,
                compareManualCatalogs: true);

            AssertPurityClassification(summary, "PurityFixture.PureLeaf()", "pure");
            AssertPurityClassification(summary, "PurityFixture.PureViaCallee()", "pure");
            AssertPurityClassification(summary, "PurityFixture.ImpureWrite()", "impure", "global_state_write");
            AssertPurityClassification(summary, "PurityFixture.ImpureViaCallee()", "impure", "impure_callee");
            AssertPurityClassification(summary, "PurityFixture.UnknownViaInterface(IWorker)", "conservative_unknown", "dynamic_dispatch");
            AssertPurityClassification(summary, "AbstractWorker.Get()", "conservative_unknown", "metadata_only_or_external");
            AssertPurityClassification(summary, "PurityFixture.PureFreshArray()", "pure");
            AssertPurityClassification(summary, "PurityFixture.MutateCallerArray(byte[])", "impure", "caller_visible_memory_write");
            AssertFreshnessClassification(summary, "PurityFixture.PureFreshArray()", "fresh_owned_array_write");

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThanOrEqualTo(8));
            Assert.That(report.GetProperty("PureCount").GetInt32(), Is.GreaterThanOrEqualTo(3));
            Assert.That(report.GetProperty("ImpureCount").GetInt32(), Is.GreaterThanOrEqualTo(3));

            var catalogComparison = report.GetProperty("CatalogComparison");
            Assert.That(catalogComparison.GetProperty("KnownPureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownImpureMembers").GetArrayLength(), Is.EqualTo(0));
            Assert.That(catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers").GetArrayLength(), Is.EqualTo(0));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeBitConverterSlice_ProducesCatalogComparisonWithoutCrashing()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.BitConverter.GetBytes", limit: 20);

            var report = summary.RootElement.GetProperty("PurityReport");
            Assert.That(report.GetProperty("MethodCount").GetInt32(), Is.GreaterThan(0));

            var catalogComparison = report.GetProperty("CatalogComparison");
            var knownPureRow = catalogComparison.GetProperty("KnownPureMembers")
                .EnumerateArray()
                .Single(row => string.Equals(
                    row.GetProperty("Symbol").GetString(),
                    "System.BitConverter.GetBytes(int)",
                    StringComparison.Ordinal));
            var knownFreshRow = catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers")
                .EnumerateArray()
                .Single(row => string.Equals(
                    row.GetProperty("Symbol").GetString(),
                    "System.BitConverter.GetBytes(int)",
                    StringComparison.Ordinal));

            Assert.That(knownPureRow.GetProperty("Classification").GetString(), Is.EqualTo("pure"));
            Assert.That(knownFreshRow.GetProperty("Note").GetString(), Is.EqualTo("fresh_owned_array_write"));
        }

        [Test]
        public async Task EffectSummaryTool_RuntimeStringSlice_NormalizesManualCatalogAliases()
        {
            using var summary = await RunRuntimeEffectSummaryAsync("System.String.ToCharArray", limit: 10);

            var report = summary.RootElement.GetProperty("PurityReport");
            var catalogComparison = report.GetProperty("CatalogComparison");
            var knownPureRow = catalogComparison.GetProperty("KnownPureMembers")
                .EnumerateArray()
                .Single(row => string.Equals(
                    row.GetProperty("Symbol").GetString(),
                    "string.ToCharArray()",
                    StringComparison.Ordinal));
            var knownFreshRow = catalogComparison.GetProperty("KnownFreshOwnedArrayReturningMembers")
                .EnumerateArray()
                .Single(row => string.Equals(
                    row.GetProperty("Symbol").GetString(),
                    "string.ToCharArray()",
                    StringComparison.Ordinal));

            Assert.That(knownPureRow.GetProperty("Classification").GetString(), Is.Not.EqualTo("unclassified"));
            Assert.That(knownFreshRow.GetProperty("Note").GetString(), Is.Not.EqualTo("unclassified"));
            Assert.That(knownPureRow.GetProperty("MatchedExactSymbolKeys").GetArrayLength(), Is.GreaterThan(0));
            Assert.That(knownFreshRow.GetProperty("MatchedExactSymbolKeys").GetArrayLength(), Is.GreaterThan(0));
        }

        [Test]
        public async Task EffectSummaryTool_GeneratedPurityCatalog_UsesDistinctExactKeys_ForDuplicateDisplaySymbols()
        {
            var source = """
public readonly struct ConversionFixture
{
    private readonly int _value;

    public ConversionFixture(int value)
    {
        _value = value;
    }

    public static explicit operator int(ConversionFixture value) => value._value;

    public static explicit operator long(ConversionFixture value) => value._value;
}
""";

            await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryDuplicateDisplaySymbols", source);
            using var summary = await RunEffectSummaryAsync(
                fixture.AssemblyPath,
                includeTransitiveRoots: true,
                classifyPurity: true,
                compareManualCatalogs: false);

            var generatedCatalog = summary.RootElement.GetProperty("GeneratedPurityCatalog");
            var operatorEntries = generatedCatalog.GetProperty("Entries")
                .EnumerateArray()
                .Where(entry => string.Equals(
                    entry.GetProperty("Symbol").GetString(),
                    "ConversionFixture.op_Explicit(ConversionFixture)",
                    StringComparison.Ordinal))
                .ToArray();

            Assert.That(operatorEntries.Length, Is.EqualTo(2));
            Assert.That(
                operatorEntries
                    .Select(entry => entry.GetProperty("ExactSymbolKey").GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(2));
        }

        private static void AssertThrownExceptions(JsonDocument summary, string methodSymbol, params string[] expectedExceptions)
        {
            var method = FindMethod(summary, methodSymbol);
            var thrownExceptions = method.GetProperty("ThrownExceptionTypes")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.That(thrownExceptions, Is.EqualTo(expectedExceptions));
        }

        private static void AssertTransitiveExceptions(JsonDocument summary, string methodSymbol, params string[] expectedExceptions)
        {
            var method = FindMethod(summary, methodSymbol);
            var transitiveExceptions = method.GetProperty("TransitiveThrownExceptionTypes")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            Assert.That(transitiveExceptions, Is.EqualTo(expectedExceptions));
        }

        private static void AssertPurityClassification(
            JsonDocument summary,
            string methodSymbol,
            string expectedClassification,
            params string[] expectedCategories)
        {
            var method = FindMethod(summary, methodSymbol);
            var classification = method.GetProperty("PurityClassification");
            Assert.That(classification.GetProperty("Classification").GetString(), Is.EqualTo(expectedClassification));

            var categories = classification.GetProperty("Categories")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            foreach (var expectedCategory in expectedCategories)
            {
                Assert.That(categories, Does.Contain(expectedCategory));
            }
        }

        private static void AssertFreshnessClassification(
            JsonDocument summary,
            string methodSymbol,
            string expectedFreshnessClassification)
        {
            var method = FindMethod(summary, methodSymbol);
            var classification = method.GetProperty("PurityClassification");
            Assert.That(
                classification.GetProperty("FreshnessClassification").GetString(),
                Is.EqualTo(expectedFreshnessClassification));
        }

        private static JsonElement FindMethod(JsonDocument summary, string methodSymbol)
        {
            var methods = summary.RootElement
                .GetProperty("Assemblies")[0]
                .GetProperty("Methods")
                .EnumerateArray()
                .ToArray();

            return methods.Single(method => string.Equals(
                method.GetProperty("Symbol").GetString(),
                methodSymbol,
                StringComparison.Ordinal));
        }

        private static async Task<JsonDocument> RunEffectSummaryAsync(
            string assemblyPath,
            bool includeTransitiveRoots,
            bool classifyPurity = false,
            bool compareManualCatalogs = false)
        {
            var outputPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, Guid.NewGuid().ToString("N") + ".json");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = GetRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add("Tools\\PurelySharp.EffectSummary\\PurelySharp.EffectSummary.csproj");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);
            if (includeTransitiveRoots)
            {
                startInfo.ArgumentList.Add("--transitive-roots");
            }
            if (classifyPurity)
            {
                startInfo.ArgumentList.Add("--classify-purity");
            }
            if (compareManualCatalogs)
            {
                startInfo.ArgumentList.Add("--compare-manual-catalogs");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed." + Environment.NewLine +
                    standardOutput + Environment.NewLine +
                    standardError);
            }

            return JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        }

        private static async Task<JsonDocument> RunRuntimeEffectSummaryAsync(string symbolPrefix, int limit)
        {
            var outputPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "runtime-effect-summary-" + Guid.NewGuid().ToString("N") + ".json");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = GetRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add("Tools\\PurelySharp.EffectSummary\\PurelySharp.EffectSummary.csproj");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--framework");
            startInfo.ArgumentList.Add("net8.0");
            startInfo.ArgumentList.Add("--symbol-prefix");
            startInfo.ArgumentList.Add(symbolPrefix);
            startInfo.ArgumentList.Add("--include-callees");
            startInfo.ArgumentList.Add("--classify-purity");
            startInfo.ArgumentList.Add("--compare-manual-catalogs");
            startInfo.ArgumentList.Add("--limit");
            startInfo.ArgumentList.Add(limit.ToString());
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed." + Environment.NewLine +
                    standardOutput + Environment.NewLine +
                    standardError);
            }

            return JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
        }

        private static async Task<FixtureAssembly> CreateFixtureAssemblyAsync(string assemblyName, string source)
        {
            var tempDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "effect-summary-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var assemblyPath = Path.Combine(tempDirectory, assemblyName + ".dll");

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            await using var stream = File.Create(assemblyPath);
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            }

            return new FixtureAssembly(tempDirectory, assemblyPath);
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }

        private static string GetRepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        }

        private sealed class FixtureAssembly : IAsyncDisposable
        {
            public FixtureAssembly(string directoryPath, string assemblyPath)
            {
                DirectoryPath = directoryPath;
                AssemblyPath = assemblyPath;
            }

            public string DirectoryPath { get; }

            public string AssemblyPath { get; }

            public ValueTask DisposeAsync()
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
