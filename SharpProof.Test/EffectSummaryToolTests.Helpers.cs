using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Test;

public partial class EffectSummaryToolTests
{
    private static readonly object EffectSummaryToolBuildLock = new();
    private static readonly TimeSpan EffectSummaryToolTimeout = TimeSpan.FromSeconds(240);
    private static string? s_effectSummaryToolDllPath;

    private static void AssertThrownExceptions(JsonDocument summary, string methodSymbol,
        params string[] expectedExceptions)
    {
        var method = FindMethod(summary, methodSymbol);
        var thrownExceptions = method.GetProperty("ThrownExceptionTypes")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.That(thrownExceptions, Is.EqualTo(expectedExceptions));
    }

    private static async Task<JsonDocument> CreateSameAssemblyDerivedStaticFieldSummaryAsync()
    {
        var source = """
                     public abstract class StaticFieldBase
                     {
                         protected static readonly int Stable = 42;
                         protected static int Mutable = 7;
                         protected static readonly Token StableToken = new();
                     }

                     public sealed class Token
                     {
                     }

                     public sealed class StableDerived : StaticFieldBase
                     {
                         public static int ReadStable()
                         {
                             return Stable;
                         }
                     }

                     public sealed class MutableDerived : StaticFieldBase
                     {
                         public static int ReadMutable()
                         {
                             return Mutable;
                         }
                     }

                     public sealed class StableCacheDerived : StaticFieldBase
                     {
                         public static Token ReadStableToken()
                         {
                             return StableToken;
                         }
                     }
                     """;

        await using var fixture = await CreateFixtureAssemblyAsync("EffectSummaryDerivedStaticFields", source);
        return await RunEffectSummaryAsync(
            fixture.AssemblyPath,
            true,
            true);
    }

    private static void AssertTransitiveExceptions(JsonDocument summary, string methodSymbol,
        params string[] expectedExceptions)
    {
        var method = FindMethod(summary, methodSymbol);
        var transitiveExceptions = method.GetProperty("TransitiveThrownExceptionTypes")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.That(transitiveExceptions, Is.EqualTo(expectedExceptions));
    }

    private static void AssertTransitiveExceptionsContain(JsonDocument summary, string methodSymbol,
        params string[] expectedExceptions)
    {
        var method = FindMethod(summary, methodSymbol);
        var transitiveExceptions = method.GetProperty("TransitiveThrownExceptionTypes")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        foreach (var expectedException in expectedExceptions)
            Assert.That(transitiveExceptions, Does.Contain(expectedException));
    }

    private static void AssertTransitiveExceptionSourcePaths(
        JsonDocument summary,
        string methodSymbol,
        params (string ExceptionType, string SourcePath)[] expectedEntries)
    {
        var method = FindMethod(summary, methodSymbol);
        var actualEntries = method.GetProperty("TransitiveThrownExceptionSourcePaths")
            .EnumerateArray()
            .Select(entry => (
                ExceptionType: entry.GetProperty("ExceptionType").GetString(),
                SourcePath: entry.GetProperty("SourcePath").GetString()))
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.ExceptionType) &&
                !string.IsNullOrWhiteSpace(entry.SourcePath))
            .OrderBy(entry => entry.ExceptionType, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourcePath, StringComparer.Ordinal)
            .ToArray();

        var normalizedExpectedEntries = expectedEntries
            .OrderBy(entry => entry.ExceptionType, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourcePath, StringComparer.Ordinal)
            .ToArray();

        Assert.That(actualEntries, Is.EqualTo(normalizedExpectedEntries));
    }

    private static void AssertTransitiveExceptionEdges(
        JsonDocument summary,
        string methodSymbol,
        params (string ExceptionType, string CalleeExactSymbolKey, string SourcePath, int Depth)[] expectedEntries)
    {
        var method = FindMethod(summary, methodSymbol);
        var actualEntries = method.GetProperty("TransitiveThrownExceptionEdges")
            .EnumerateArray()
            .Select(entry =>
            {
                var hasCalleeExactSymbolKey =
                    entry.TryGetProperty("CalleeExactSymbolKey", out var calleeExactSymbolKeyElement);
                return (
                    ExceptionType: entry.GetProperty("ExceptionType").GetString(),
                    CalleeExactSymbolKey: hasCalleeExactSymbolKey ? calleeExactSymbolKeyElement.GetString() : null,
                    SourcePath: entry.GetProperty("SourcePath").GetString(),
                    Depth: entry.GetProperty("Depth").GetInt32());
            })
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.ExceptionType) &&
                !string.IsNullOrWhiteSpace(entry.CalleeExactSymbolKey) &&
                !string.IsNullOrWhiteSpace(entry.SourcePath))
            .OrderBy(entry => entry.ExceptionType, StringComparer.Ordinal)
            .ThenBy(entry => entry.CalleeExactSymbolKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourcePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Depth)
            .ToArray();

        var normalizedExpectedEntries = expectedEntries
            .OrderBy(entry => entry.ExceptionType, StringComparer.Ordinal)
            .ThenBy(entry => entry.CalleeExactSymbolKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourcePath, StringComparer.Ordinal)
            .ThenBy(entry => entry.Depth)
            .ToArray();

        Assert.That(actualEntries, Is.EqualTo(normalizedExpectedEntries));
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

        foreach (var expectedCategory in expectedCategories) Assert.That(categories, Does.Contain(expectedCategory));
    }

    private static void AssertPrimaryCategory(
        JsonDocument summary,
        string methodSymbol,
        string expectedPrimaryCategory)
    {
        var entry = summary.RootElement
            .GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("Symbol").GetString(),
                methodSymbol,
                StringComparison.Ordinal));
        Assert.That(entry.GetProperty("PrimaryCategory").GetString(), Is.EqualTo(expectedPrimaryCategory));
    }

    private static void AssertCategoriesDoNotContain(
        JsonDocument summary,
        string methodSymbol,
        string unexpectedCategory)
    {
        var method = FindMethod(summary, methodSymbol);
        var classification = method.GetProperty("PurityClassification");
        var categories = classification.GetProperty("Categories")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.That(categories, Does.Not.Contain(unexpectedCategory));
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

    private static void AssertEffectVisibilityClassification(
        JsonDocument summary,
        string methodSymbol,
        string expectedEffectVisibilityClassification)
    {
        var method = FindMethod(summary, methodSymbol);
        var classification = method.GetProperty("PurityClassification");
        Assert.That(
            classification.GetProperty("EffectVisibilityClassification").GetString(),
            Is.EqualTo(expectedEffectVisibilityClassification));
    }

    private static void AssertInventoryEntry(
        JsonElement inventory,
        string symbol,
        string guess,
        string reason)
    {
        var entry = inventory.GetProperty("Entries")
            .EnumerateArray()
            .Single(candidate => string.Equals(
                candidate.GetProperty("Symbol").GetString(),
                symbol,
                StringComparison.Ordinal));

        Assert.That(entry.GetProperty("Guess").GetString(), Is.EqualTo(guess));
        Assert.That(entry.GetProperty("Confidence").GetString(), Is.EqualTo("low"));
        Assert.That(entry.GetProperty("Reason").GetString(), Is.EqualTo(reason));
        Assert.That(entry.GetProperty("Category").GetString(), Does.StartWith("bcl_fallback_"));
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

    private static JsonElement[] FindMethodsByPrefix(JsonDocument summary, string methodSymbolPrefix)
    {
        return summary.RootElement
            .GetProperty("Assemblies")[0]
            .GetProperty("Methods")
            .EnumerateArray()
            .Where(method =>
            {
                var symbol = method.GetProperty("Symbol").GetString();
                return !string.IsNullOrWhiteSpace(symbol) &&
                       symbol.StartsWith(methodSymbolPrefix, StringComparison.Ordinal);
            })
            .ToArray();
    }

    internal static async Task<JsonDocument> RunEffectSummaryAsync(
        string assemblyPath,
        bool includeTransitiveRoots,
        bool classifyPurity = false,
        bool compareManualCatalogs = false)
    {
        return await RunEffectSummaryAsync(
            new[] { assemblyPath },
            includeTransitiveRoots,
            classifyPurity,
            compareManualCatalogs);
    }

    internal static async Task<JsonDocument> RunEffectSummaryAsync(
        string[] assemblyPaths,
        bool includeTransitiveRoots,
        bool classifyPurity = false,
        bool compareManualCatalogs = false)
    {
        var outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-" + Guid.NewGuid().ToString("N") + ".json");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
        foreach (var assemblyPath in assemblyPaths)
        {
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assemblyPath);
        }

        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        if (includeTransitiveRoots) startInfo.ArgumentList.Add("--transitive-roots");
        if (classifyPurity) startInfo.ArgumentList.Add("--classify-purity");
        if (compareManualCatalogs) startInfo.ArgumentList.Add("--compare-manual-catalogs");

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start effect summary tool.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(EffectSummaryToolTimeout);
        }
        catch (TimeoutException)
        {
            TryKillProcess(process);
            throw new AssertionException("Effect summary tool timed out after " +
                                         (int)EffectSummaryToolTimeout.TotalSeconds + " seconds.");
        }

        if (process.ExitCode != 0)
            throw new AssertionException(
                "Effect summary tool failed with exit code " + process.ExitCode + "." + Environment.NewLine +
                "Assemblies: " + string.Join(", ", assemblyPaths) + Environment.NewLine +
                "Output: " + outputPath);

        return JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
    }

    internal static async Task<(int ExitCode, string StandardOutput, string StandardError)>
        RunEffectSummaryProcessAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start effect summary tool.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(EffectSummaryToolTimeout);
        }
        catch (TimeoutException)
        {
            TryKillProcess(process);
            throw new AssertionException("Effect summary tool timed out after " +
                                         (int)EffectSummaryToolTimeout.TotalSeconds + " seconds.");
        }

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    private static async Task<JsonDocument> RunFilteredEffectSummaryAsync(
        string assemblyPath,
        bool includeTransitiveRoots,
        int maxDepth,
        params string[] symbolPrefixes)
    {
        return await RunFilteredEffectSummaryAsync(
            assemblyPath,
            includeTransitiveRoots,
            maxDepth,
            true,
            symbolPrefixes);
    }

    private static async Task<JsonDocument> RunFilteredEffectSummaryAsync(
        string assemblyPath,
        bool includeTransitiveRoots,
        int maxDepth,
        bool includeCallees = true,
        params string[] symbolPrefixes)
    {
        var outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "effect-summary-filtered-" + Guid.NewGuid().ToString("N") + ".json");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
        startInfo.ArgumentList.Add("--assembly");
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var symbolPrefix in symbolPrefixes)
        {
            startInfo.ArgumentList.Add("--symbol-prefix");
            startInfo.ArgumentList.Add(symbolPrefix);
        }

        if (includeCallees) startInfo.ArgumentList.Add("--include-callees");
        startInfo.ArgumentList.Add("--max-depth");
        startInfo.ArgumentList.Add(maxDepth.ToString());
        if (includeTransitiveRoots) startInfo.ArgumentList.Add("--transitive-roots");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start effect summary tool.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(EffectSummaryToolTimeout);
        }
        catch (TimeoutException)
        {
            TryKillProcess(process);
            throw new AssertionException("Effect summary tool timed out after " +
                                         (int)EffectSummaryToolTimeout.TotalSeconds + " seconds.");
        }

        if (process.ExitCode != 0)
            throw new AssertionException(
                "Effect summary tool failed with exit code " + process.ExitCode + "." + Environment.NewLine +
                "Assembly: " + assemblyPath + Environment.NewLine +
                "Symbol prefixes: " + string.Join(", ", symbolPrefixes) + Environment.NewLine +
                "Output: " + outputPath);

        return JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
    }

    private static async Task RunEffectSummaryToolAsync(params string[] arguments)
    {
        await RunEffectSummaryToolAsyncWithWorkingDirectory(GetRepositoryRoot(), arguments);
    }

    private static async Task RunEffectSummaryToolAsyncWithWorkingDirectory(string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start effect summary tool.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync().WaitAsync(EffectSummaryToolTimeout);
        }
        catch (TimeoutException)
        {
            TryKillProcess(process);
            throw new AssertionException("Effect summary tool timed out after " +
                                         (int)EffectSummaryToolTimeout.TotalSeconds + " seconds.");
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        if (process.ExitCode != 0)
            throw new AssertionException(
                "Effect summary tool failed with exit code " + process.ExitCode + "." + Environment.NewLine +
                "Arguments: " + string.Join(" ", arguments) + Environment.NewLine +
                standardOutput + Environment.NewLine +
                standardError);
    }

    private static Task<JsonDocument> RunRuntimeEffectSummaryAsync(string symbolPrefix, int limit)
    {
        return RunRuntimeEffectSummaryAsync(limit, symbolPrefix);
    }

    private static async Task<JsonDocument> RunRuntimeEffectSummaryAsync(int limit, params string[] symbolPrefixes)
    {
        return await RunRuntimeEffectSummaryAsyncCore(limit, null, 1, false, symbolPrefixes);
    }

    private static Task<JsonDocument> RunRuntimeEffectSummaryAsyncForAssembly(
        string runtimeAssemblyName,
        int limit,
        params string[] symbolPrefixes)
    {
        return RunRuntimeEffectSummaryAsyncForAssembly(runtimeAssemblyName, limit, 1, false, symbolPrefixes);
    }

    private static Task<JsonDocument> RunRuntimeEffectSummaryAsyncForAssembly(
        string runtimeAssemblyName,
        int limit,
        int maxDepth,
        params string[] symbolPrefixes)
    {
        return RunRuntimeEffectSummaryAsyncForAssembly(runtimeAssemblyName, limit, maxDepth, false, symbolPrefixes);
    }

    private static Task<JsonDocument> RunRuntimeEffectSummaryAsyncForAssembly(
        string runtimeAssemblyName,
        int limit,
        int maxDepth,
        bool includeTransitiveRoots,
        params string[] symbolPrefixes)
    {
        return RunRuntimeEffectSummaryAsyncCore(limit, runtimeAssemblyName, maxDepth, includeTransitiveRoots,
            symbolPrefixes);
    }

    private static async Task<JsonDocument> RunRuntimeEffectSummaryAsyncCore(
        int limit,
        string? runtimeAssemblyName,
        int maxDepth,
        bool includeTransitiveRoots,
        params string[] symbolPrefixes)
    {
        if (symbolPrefixes.Length == 0)
            throw new ArgumentException("At least one symbol prefix is required.", nameof(symbolPrefixes));

        var outputPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "runtime-effect-summary-" + Guid.NewGuid().ToString("N") + ".json");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetRepositoryRoot(),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
        startInfo.ArgumentList.Add("--framework");
        startInfo.ArgumentList.Add("net8.0");
        if (!string.IsNullOrWhiteSpace(runtimeAssemblyName))
        {
            startInfo.ArgumentList.Add("--runtime-assembly");
            startInfo.ArgumentList.Add(runtimeAssemblyName);
        }

        foreach (var symbolPrefix in symbolPrefixes)
        {
            startInfo.ArgumentList.Add("--symbol-prefix");
            startInfo.ArgumentList.Add(symbolPrefix);
        }

        startInfo.ArgumentList.Add("--include-callees");
        startInfo.ArgumentList.Add("--max-depth");
        startInfo.ArgumentList.Add(maxDepth.ToString());
        if (includeTransitiveRoots) startInfo.ArgumentList.Add("--transitive-roots");
        startInfo.ArgumentList.Add("--classify-purity");
        startInfo.ArgumentList.Add("--compare-manual-catalogs");
        startInfo.ArgumentList.Add("--limit");
        startInfo.ArgumentList.Add(limit.ToString());
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start effect summary tool.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(EffectSummaryToolTimeout);
        }
        catch (TimeoutException)
        {
            TryKillProcess(process);
            throw new AssertionException("Effect summary tool timed out after " +
                                         (int)EffectSummaryToolTimeout.TotalSeconds + " seconds.");
        }

        if (process.ExitCode != 0)
            throw new AssertionException(
                "Effect summary tool failed with exit code " + process.ExitCode + "." + Environment.NewLine +
                "Symbol prefixes: " + string.Join(", ", symbolPrefixes) + Environment.NewLine +
                "Output: " + outputPath);

        return JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
    }

    internal static string GetEffectSummaryToolDllPath()
    {
        lock (EffectSummaryToolBuildLock)
        {
            if (!string.IsNullOrWhiteSpace(s_effectSummaryToolDllPath) && File.Exists(s_effectSummaryToolDllPath))
                return s_effectSummaryToolDllPath;

            var repositoryRoot = GetRepositoryRoot();
            var projectPath = Path.Combine(repositoryRoot, "Tools", "SharpProof.EffectSummary",
                "SharpProof.EffectSummary.csproj");
            var dllPath = Path.Combine(repositoryRoot, "Tools", "SharpProof.EffectSummary", "bin", "Debug", "net8.0",
                "SharpProof.EffectSummary.dll");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("-m:20");
            startInfo.ArgumentList.Add("--no-restore");

            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException("Failed to build effect summary tool.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(dllPath))
                throw new AssertionException(
                    "Effect summary tool build failed." + Environment.NewLine +
                    standardOutput + Environment.NewLine +
                    standardError);

            s_effectSummaryToolDllPath = dllPath;
            return s_effectSummaryToolDllPath;
        }
    }

    internal static async Task<FixtureAssembly> CreateFixtureAssemblyAsync(
        string assemblyName,
        string source,
        params MetadataReference[] additionalReferences)
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
            GetTrustedPlatformReferences().AddRange(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        await using var stream = File.Create(assemblyPath);
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
            throw new AssertionException(string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return new FixtureAssembly(tempDirectory, assemblyPath);
    }

    internal static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            return ImmutableArray.Create<MetadataReference>(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToImmutableArray();
    }

    internal static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
    }

    private static async Task GenerateReviewedSourceSummaryAsync(
        string outputPath,
        string runtimeAssemblyName,
        int limit,
        params string[] symbolPrefixes)
    {
        var arguments = new List<string>
        {
            "--framework",
            "net8.0",
            "--runtime-assembly",
            runtimeAssemblyName
        };

        foreach (var symbolPrefix in symbolPrefixes)
        {
            arguments.Add("--symbol-prefix");
            arguments.Add(symbolPrefix);
        }

        arguments.Add("--include-callees");
        arguments.Add("--classify-purity");
        arguments.Add("--compare-manual-catalogs");
        arguments.Add("--limit");
        arguments.Add(limit.ToString());
        arguments.Add("--output");
        arguments.Add(outputPath);

        await RunEffectSummaryToolAsync(arguments.ToArray());
    }

    private static string CreateGeneratedOnlySummaryDocument(JsonDocument summary)
    {
        return CreateGeneratedPurityCatalogSummaryDocument(summary.RootElement.GetProperty("GeneratedPurityCatalog"));
    }

    private static string CreateGeneratedPurityCatalogSummaryDocument(object generatedPurityCatalog)
    {
        return JsonSerializer.Serialize(new
        {
            GeneratedPurityCatalog = generatedPurityCatalog
        });
    }

    private static string[] GetGeneratedPurityCatalogSymbols(JsonDocument summary)
    {
        return summary.RootElement.GetProperty("GeneratedPurityCatalog")
            .GetProperty("Entries")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("Symbol").GetString())
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol!)
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch
        {
        }
    }

    internal sealed class FixtureAssembly : IAsyncDisposable
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
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);

            return ValueTask.CompletedTask;
        }
    }
}