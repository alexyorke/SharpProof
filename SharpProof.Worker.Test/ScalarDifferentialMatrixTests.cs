using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ScalarDifferentialMatrixTests
{
    private static readonly ScalarCase[] SupportedCases = [
        new(
            "SByte",
            "sbyte",
            [sbyte.MinValue, (sbyte)-1, (sbyte)0, (sbyte)1, sbyte.MaxValue]),
        new(
            "Byte",
            "byte",
            [byte.MinValue, (byte)1, (byte)127, byte.MaxValue]),
        new(
            "Int16",
            "short",
            [short.MinValue, (short)-1, (short)0, (short)1, short.MaxValue]),
        new(
            "UInt16",
            "ushort",
            [ushort.MinValue, (ushort)1, (ushort)32767, ushort.MaxValue]),
        new(
            "Char",
            "char",
            [char.MinValue, (char)1, 'A', char.MaxValue]),
        new(
            "Int32",
            "int",
            [int.MinValue, -1, 0, 1, int.MaxValue]),
        new(
            "UInt32",
            "uint",
            [uint.MinValue, 1U, 2147483647U, uint.MaxValue]),
        new(
            "Int64",
            "long",
            [long.MinValue, -1L, 0L, 1L, long.MaxValue])
    ];

    private static readonly string[] RequiredReferenceFileNames = [
        "System.Private.CoreLib.dll",
        "System.Linq.dll",
        "System.Runtime.dll",
        "netstandard.dll"
    ];

    [Test]
    public async Task SupportedScalarMatrixAgreesAcrossRuntimeIrAndSmt()
    {
        using var project = DifferentialProject.Create(CreateSource());
        var request = project.CreateRequest();
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var supportedResults = SupportedCases
            .SelectMany(item => response.ClaimResults.Where(result =>
                CallableId(response, result).Contains(
                    "." + item.MethodName + "(",
                    StringComparison.Ordinal)))
            .ToArray();
        var proven = supportedResults.Where(static result =>
            result.Outcome == WorkerClaimOutcome.Proven).ToArray();
        var refuted = supportedResults.Where(static result =>
            result.Outcome == WorkerClaimOutcome.Refuted).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(supportedResults, Has.Length.EqualTo(24));
            Assert.That(
                proven,
                Has.Length.EqualTo(16));
            Assert.That(
                proven.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                refuted,
                Has.Length.EqualTo(8));
            Assert.That(
                refuted.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                supportedResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
        }
        foreach (var item in SupportedCases)
        {
            var counterexample = refuted.Single(result =>
                CallableId(response, result).Contains(
                    "." + item.MethodName + "(",
                    StringComparison.Ordinal));
            Assert.That(
                counterexample.Model.Single(value =>
                    value.Variable == "parameter:0").Value,
                Is.EqualTo(Format(item.BoundaryValues[^1])),
                item.MethodName);
        }

        using var runtime = project.EmitRuntimeAssembly();
        var subject = runtime.Assembly.GetType(
            "ScalarDifferentialSubject",
            throwOnError: true)!;
        foreach (var item in SupportedCases)
        {
            var method = subject.GetMethod(
                item.MethodName,
                BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException(
                    $"Runtime method '{item.MethodName}' is missing.");
            foreach (var input in item.BoundaryValues)
            {
                Assert.That(
                    method.Invoke(null, [input, true]),
                    Is.EqualTo(input),
                    $"{item.MethodName}({Format(input)}, true)");
                Assert.That(
                    method.Invoke(null, [input, false]),
                    Is.EqualTo(input),
                    $"{item.MethodName}({Format(input)}, false)");
            }
        }
    }

    [Test]
    public async Task WidthSensitiveConversionsRemainTypedUnknown()
    {
        using var project = DifferentialProject.Create(CreateSource());
        var request = project.CreateRequest();
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        var conversions = response.ClaimResults
            .Where(result => CallableId(response, result).Contains(
                "Conversion(",
                StringComparison.Ordinal))
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(conversions, Has.Length.EqualTo(2));
            Assert.That(
                conversions.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                conversions.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    private static string CreateSource()
    {
        var methods = SupportedCases.Select(static item =>
            $$"""
                public static {{item.TypeName}} {{item.MethodName}}(
                    {{item.TypeName}} value,
                    bool chooseFirst) {
                    Contract.Ensures(
                        Contract.Result<{{item.TypeName}}>() ==
                        Contract.Old(value));
                    Contract.Ensures(
                        Contract.Result<{{item.TypeName}}>() >=
                            {{item.TypeName}}.MinValue &&
                        Contract.Result<{{item.TypeName}}>() <=
                            {{item.TypeName}}.MaxValue);
                    Contract.Ensures(
                        Contract.Result<{{item.TypeName}}>() !=
                            {{item.TypeName}}.MaxValue);
                    var snapshot = value;
                    if (chooseFirst) {
                        value = snapshot;
                        return value;
                    }
                    value = snapshot;
                    return value;
                }
            """);
        return
            """
            using SharpProof.Attributes;

            public static class ScalarDifferentialSubject {
            """ +
            Environment.NewLine +
            string.Join(Environment.NewLine, methods) +
            Environment.NewLine +
            """
                public static int UncheckedConversion(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return unchecked((int)value);
                }

                public static int CheckedConversion(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return checked((int)value);
                }
            }
            """;
    }

    private static string CallableId(
        WorkerVerifyResponse response,
        WorkerClaimResult result)
    {
        return response.Manifest.Claims.Single(claim =>
            string.Equals(
                claim.ClaimId,
                result.ClaimId,
                StringComparison.Ordinal)).CallableId;
    }

    private static string Format(object value)
    {
        return value is char character
            ? ((int)character).ToString(CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ??
              string.Empty;
    }

    private sealed record ScalarCase(
        string MethodName,
        string TypeName,
        object[] BoundaryValues);

    private sealed class DifferentialProject : IDisposable
    {
        private readonly string _sourcePath;

        private DifferentialProject(string directory, string sourcePath)
        {
            DirectoryPath = directory;
            _sourcePath = sourcePath;
        }

        internal string DirectoryPath
        {
            get;
        }

        internal static DifferentialProject Create(string source)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.ScalarDifferential",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePath = Path.Combine(directory, "Subject.cs");
            File.WriteAllText(
                sourcePath,
                source,
                new System.Text.UTF8Encoding(false));
            return new DifferentialProject(directory, sourcePath);
        }

        internal WorkerVerifyRequest CreateRequest()
        {
            var compilation = CreateCompilation(includeContracts: true);
            var discovery = new ClaimManifestBuilder(compilation).Build();
            var artifact = CompilerManifestArtifactProducer.Create(
                compilation,
                DirectoryPath,
                "net8.0",
                WorkerFeatureSet.All,
                discovery,
                WorkerBudgets.DefaultMaximumExpressionDepth,
                CancellationToken.None);
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                CompilerManifestArtifactJson.Serialize(artifact));
            var artifactPath = Path.Combine(
                DirectoryPath,
                "compiler-manifest.json");
            File.WriteAllBytes(artifactPath, bytes);
            return new WorkerVerifyRequest
            {
                CompilerManifest = new WorkerFileReference
                {
                    Path = artifactPath,
                    Sha256 = string.Concat(
                        System.Security.Cryptography.SHA256.HashData(bytes)
                            .Select(static value => value.ToString(
                                "x2",
                                CultureInfo.InvariantCulture)))
                },
                Cache = new WorkerCacheOptions
                {
                    Enabled = false,
                    Directory = Path.Combine(DirectoryPath, "cache")
                }
            };
        }

        internal RuntimeAssembly EmitRuntimeAssembly()
        {
            var compilation = CreateCompilation(includeContracts: false);
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.That(
                emit.Success,
                Is.True,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
            image.Position = 0;
            var context = new AssemblyLoadContext(
                "SharpProof.ScalarDifferential." +
                Guid.NewGuid().ToString("N"),
                isCollectible: true);
            context.Resolving += ResolveContractAssembly;
            return new RuntimeAssembly(
                context,
                context.LoadFromStream(image));
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(DirectoryPath);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.ScalarDifferential"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }

        private CSharpCompilation CreateCompilation(bool includeContracts)
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: includeContracts
                    ? [Contract.ConditionalSymbol]
                    : []);
            var syntaxTree = CSharpSyntaxTree.ParseText(
                SourceText.From(
                    File.ReadAllText(_sourcePath),
                    System.Text.Encoding.UTF8,
                    SourceHashAlgorithm.Sha256),
                parseOptions,
                _sourcePath);
            var references = GetReferences().Select(
                static path => MetadataReference.CreateFromFile(path));
            return CSharpCompilation.Create(
                "ScalarDifferential",
                [syntaxTree],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable,
                    deterministic: true,
                    concurrentBuild: false));
        }

        private static string[] GetReferences()
        {
            var trusted = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator);
            var names = new HashSet<string>(
                RequiredReferenceFileNames,
                StringComparer.OrdinalIgnoreCase);
            return [.. trusted
                .Where(path => names.Contains(Path.GetFileName(path)))
                .Append(typeof(Contract).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.Ordinal)];
        }

        internal static Assembly? ResolveContractAssembly(
            AssemblyLoadContext context,
            AssemblyName name)
        {
            return string.Equals(
                name.Name,
                typeof(Contract).Assembly.GetName().Name,
                StringComparison.Ordinal)
                ? context.LoadFromAssemblyPath(typeof(Contract).Assembly.Location)
                : null;
        }
    }

    private sealed class RuntimeAssembly(
        AssemblyLoadContext context,
        Assembly assembly) : IDisposable
    {
        internal Assembly Assembly { get; } = assembly;

        public void Dispose()
        {
            context.Resolving -= DifferentialProject.ResolveContractAssembly;
            context.Unload();
        }
    }
}
