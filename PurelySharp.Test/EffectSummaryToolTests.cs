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

        private static async Task<JsonDocument> RunEffectSummaryAsync(string assemblyPath, bool includeTransitiveRoots)
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
