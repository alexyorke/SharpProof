using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

internal sealed class SimpleAnalyzerAssemblyLoader : AssemblyLoadContext, IAnalyzerAssemblyLoader, IDisposable {
    private readonly Dictionary<string, string> _dependencyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SimpleAnalyzerAssemblyLoader()
        : base("SharpProof.VsixHarness.Analyzer", true) {
    }

    public void AddDependencyLocation(string fullPath) {
        var resolvedPath = Path.GetFullPath(fullPath);
        var assemblyName = Path.GetFileNameWithoutExtension(resolvedPath);
        if (assemblyName.Length == 0) return;

        lock (_gate)
            _dependencyPaths[assemblyName] = resolvedPath;
    }

    public Assembly LoadFromPath(string fullPath) {
        var resolvedPath = Path.GetFullPath(fullPath);
        AddDependencyLocation(resolvedPath);
        var requestedName = AssemblyName.GetAssemblyName(resolvedPath);
        var loaded = FindExactLoadedAssembly(requestedName);
        return loaded ?? LoadFromAssemblyPath(resolvedPath);
    }

    public void Dispose() => Unload();

    protected override Assembly? Load(AssemblyName requestedName) {
        if (requestedName.Name == null) return null;

        var loaded = FindExactLoadedAssembly(requestedName);
        if (loaded != null) return loaded;

        string? dependencyPath;
        lock (_gate)
            _dependencyPaths.TryGetValue(requestedName.Name, out dependencyPath);
        if (dependencyPath == null) return null;

        return LoadFromAssemblyPath(dependencyPath);
    }

    private Assembly? FindExactLoadedAssembly(AssemblyName requestedName) => Assemblies.Concat(AssemblyLoadContext.Default.Assemblies).FirstOrDefault(candidate => {
        var loadedName = candidate.GetName();
        return AssemblyName.ReferenceMatchesDefinition(loadedName, requestedName) &&
               Equals(loadedName.Version, requestedName.Version) &&
               string.Equals(loadedName.CultureName, requestedName.CultureName,
                   StringComparison.OrdinalIgnoreCase) &&
               PublicKeyTokensEqual(loadedName, requestedName);
    });

    private static bool PublicKeyTokensEqual(AssemblyName left, AssemblyName right) {
        var leftToken = left.GetPublicKeyToken() ?? Array.Empty<byte>();
        var rightToken = right.GetPublicKeyToken() ?? Array.Empty<byte>();
        return leftToken.AsSpan().SequenceEqual(rightToken);
    }
}

internal static class Program {
    private static readonly ImmutableHashSet<string> RequiredVsixEntries = new[] {
        "[Content_Types].xml",
        "catalog.json",
        "extension.vsixmanifest",
        "Humanizer.dll",
        "libz3.dll",
        "manifest.json",
        "Microsoft.Bcl.AsyncInterfaces.dll",
        "Microsoft.Z3.dll",
        "SharpProof.Analyzer.dll",
        "SharpProof.Analyzer.pdb",
        "SharpProof.Attributes.dll",
        "SharpProof.Attributes.pdb",
        "SharpProof.CodeFixes.dll",
        "SharpProof.CodeFixes.pdb",
        "SharpProof.ProofCore.dll",
        "SharpProof.ProofCore.pdb",
        "SharpProof.Symbolic.dll",
        "SharpProof.Symbolic.pdb",
        "SharpProof.Symbolic.xml",
        "System.Buffers.dll",
        "System.Collections.Immutable.dll",
        "System.IO.Pipelines.dll",
        "System.Memory.dll",
        "System.Numerics.Vectors.dll",
        "System.Reflection.Metadata.dll",
        "System.Runtime.CompilerServices.Unsafe.dll",
        "System.Text.Encoding.CodePages.dll",
        "System.Text.Encodings.Web.dll",
        "System.Text.Json.dll",
        "System.Threading.Channels.dll",
        "System.Threading.Tasks.Extensions.dll"
    }.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    private static int Main(string[] args) {
        try {
            return Run(args);
        }
        catch (Exception ex) {
            Console.Error.WriteLine("HARNESS ERROR: " + ex);
            return 1;
        }
    }

    private static int Run(string[] args) {
        var solutionRoot = FindRepoRoot();
        var configuration = GetConfiguration(args);
        var vsixPath = args.Length > 0
            ? args[0]
            : Path.Combine(solutionRoot, "SharpProof.Vsix", "bin", configuration, "SharpProof.Vsix.vsix");

        string? simulatedVsixDirectory = null;
        if (!File.Exists(vsixPath)) {
            vsixPath = CreateSimulatedVsix(solutionRoot, configuration);
            simulatedVsixDirectory = Path.GetDirectoryName(vsixPath);
            Console.WriteLine($"Created simulated VSIX at: {vsixPath}");
        }

        try {
            var payload = ExtractVsixPayload(vsixPath, simulatedVsixDirectory == null);
            try {
                var attributesDll = Path.Combine(solutionRoot, "SharpProof.Attributes", "bin", configuration,
                "netstandard2.0", "SharpProof.Attributes.dll");
                var useRealAttributes = File.Exists(attributesDll);
                var source = useRealAttributes
                    ? """
                  using SharpProof.Attributes;
                  namespace TestNamespace;

                  public class C
                  {
                      [EnforcePure]
                      public void M() => System.Console.WriteLine("effect boundary");
                  }
                  """
                    : """
                  using System;
                  namespace SharpProof.Attributes
                  {
                      public sealed class EnforcePureAttribute : Attribute { }
                      public sealed class EffectContractAttribute : Attribute { }
                  }

                  namespace TestNamespace
                  {
                      public class C
                      {
                          [SharpProof.Attributes.EnforcePure]
                          public void M() => System.Console.WriteLine("effect boundary");
                      }
                  }
                  """;

                var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
                var references = GetTrustedPlatformReferences().ToList();
                if (useRealAttributes) {
                    references.Add(MetadataReference.CreateFromFile(attributesDll));
                    Console.WriteLine($"Using real attributes assembly: {attributesDll}");
                }
                else {
                    Console.WriteLine("Using in-source attribute stubs.");
                }

                var compilation = CSharpCompilation.Create(
                    "VsixHarnessCompilation",
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var compilationErrors = compilation.GetDiagnostics().Where(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error).ToImmutableArray();
                if (!compilationErrors.IsEmpty)
                    throw new InvalidOperationException("Harness sample did not compile: " +
                                                        string.Join(Environment.NewLine, compilationErrors));

                using var loader = new SimpleAnalyzerAssemblyLoader();
                foreach (var dependencyPath in payload.ManagedAssemblyPaths)
                    loader.AddDependencyLocation(dependencyPath);

                var analyzerRef = new AnalyzerFileReference(payload.AnalyzerPath, loader);
                var analyzers = analyzerRef.GetAnalyzers(LanguageNames.CSharp);
                if (analyzers.IsDefaultOrEmpty)
                    throw new InvalidOperationException("VSIX contained no loadable C# analyzers.");

                var analyzerDiagnostics = compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync()
                    .GetAwaiter().GetResult();
                Console.WriteLine($"Analyzer executed. Diagnostics count: {analyzerDiagnostics.Length}");
                foreach (var diagnostic in analyzerDiagnostics) {
                    var location = diagnostic.Location.GetLineSpan();
                    Console.WriteLine(
                        $"  {diagnostic.Id}: {diagnostic.GetMessage()} @ {location.Path}({location.StartLinePosition.Line + 1},{location.StartLinePosition.Character + 1})");
                }

                if (analyzerDiagnostics.Any(static diagnostic => diagnostic.Id == "AD0001"))
                    throw new InvalidOperationException("Analyzer execution produced AD0001.");
                var contractFailure = analyzerDiagnostics.FirstOrDefault(static diagnostic => diagnostic.Id == "SP0002");
                if (contractFailure == null)
                    throw new InvalidOperationException("Analyzer did not report the unresolved [EnforcePure] contract.");
                if (!contractFailure.Properties.ContainsKey("sharpproof.effects.flags"))
                    throw new InvalidOperationException("Analyzer diagnostic did not carry canonical method-effect evidence.");

                return 0;
            }
            finally {
                TryDeleteDirectory(payload.Directory.FullName);
            }
        }
        finally {
            if (simulatedVsixDirectory != null)
                TryDeleteDirectory(simulatedVsixDirectory);
        }
    }

    private static IEnumerable<MetadataReference> GetTrustedPlatformReferences() {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES was not available.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }

    private static ExtractedVsixPayload ExtractVsixPayload(string vsixPath, bool validateManifest) {
        var directory = Directory.CreateTempSubdirectory("SharpProofVsixHarness");
        try {
            var root = directory.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string? analyzerPath = null;

            using (var archive = ZipFile.OpenRead(vsixPath)) {
                if (validateManifest)
                    ValidateVsixManifest(archive);

                foreach (var entry in archive.Entries) {
                    if (entry.Name.Length == 0 ||
                        entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                        entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                        continue;

                    var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    var destinationPath = Path.GetFullPath(Path.Combine(directory.FullName, relativePath));
                    if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"VSIX entry escapes extraction root: {entry.FullName}");

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    entry.ExtractToFile(destinationPath, true);
                    if (entry.FullName.EndsWith("SharpProof.Analyzer.dll", StringComparison.OrdinalIgnoreCase))
                        analyzerPath = destinationPath;
                }
            }

            if (analyzerPath == null)
                throw new FileNotFoundException("Analyzer DLL not found inside VSIX.");

            var managedAssemblies = Directory.GetFiles(directory.FullName, "*.dll", SearchOption.AllDirectories)
                .Where(static path => {
                    try {
                        _ = AssemblyName.GetAssemblyName(path);
                        return true;
                    }
                    catch (BadImageFormatException) {
                        return false;
                    }
                })
                .ToImmutableArray();
            return new ExtractedVsixPayload(directory, analyzerPath, managedAssemblies);
        }
        catch {
            TryDeleteDirectory(directory.FullName);
            throw;
        }
    }

    private static void ValidateVsixManifest(ZipArchive archive) {
        var actualEntries = archive.Entries
            .Where(static entry => entry.Name.Length != 0)
            .Select(static entry => entry.FullName.Replace('\\', '/'))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = RequiredVsixEntries.Except(actualEntries).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var unexpected = actualEntries.Except(RequiredVsixEntries).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length != 0 || unexpected.Length != 0)
            throw new InvalidDataException(
                $"VSIX payload differs from the required manifest. Missing: [{string.Join(", ", missing)}]. " +
                $"Unexpected: [{string.Join(", ", unexpected)}].");
    }

    private static string FindRepoRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string GetConfiguration(string[] args) {
        var configuration = args.Length > 1
            ? args[1]
            : Environment.GetEnvironmentVariable("SHARPPROOF_BUILD_CONFIGURATION");
        if (string.IsNullOrWhiteSpace(configuration) && args.Length > 0) {
            var candidate = Directory.GetParent(Path.GetFullPath(args[0]))?.Name;
            if (string.Equals(candidate, "Debug", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, "Release", StringComparison.OrdinalIgnoreCase))
                configuration = candidate;
        }

        configuration = string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration.Trim();
        if (configuration is "." or ".." ||
            Path.IsPathRooted(configuration) ||
            configuration.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Build configuration must be a single directory name.");

        Console.WriteLine($"Using build configuration: {configuration}");
        return configuration;
    }

    private static string CreateSimulatedVsix(string solutionRoot, string configuration) {
        var analyzerDirectory = Path.Combine(solutionRoot, "SharpProof.Analyzer", "bin", configuration,
            "netstandard2.0");
        var analyzerPath = Path.Combine(analyzerDirectory, "SharpProof.Analyzer.dll");
        if (!File.Exists(analyzerPath))
            throw new FileNotFoundException($"Analyzer not found at {analyzerPath}. Build in {configuration} first.");

        var tempDirectory = Directory.CreateTempSubdirectory("SharpProofSimVsix");
        try {
            var vsixPath = Path.Combine(tempDirectory.FullName, "SharpProof.Simulated.vsix");
            using (var archive = ZipFile.Open(vsixPath, ZipArchiveMode.Create))
                foreach (var file in Directory.GetFiles(analyzerDirectory, "*", SearchOption.AllDirectories)) {
                    var entryName = Path.GetRelativePath(analyzerDirectory, file).Replace('\\', '/');
                    archive.CreateEntryFromFile(file, entryName);
                }

            return vsixPath;
        }
        catch {
            TryDeleteDirectory(tempDirectory.FullName);
            throw;
        }
    }

    private static void TryDeleteDirectory(string path) {
        try {
            Directory.Delete(path, true);
        }
        catch (IOException) {
        }
        catch (UnauthorizedAccessException) {
        }
    }

    private sealed record ExtractedVsixPayload(
        DirectoryInfo Directory,
        string AnalyzerPath,
        ImmutableArray<string> ManagedAssemblyPaths);
}
