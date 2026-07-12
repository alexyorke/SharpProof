using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

internal sealed class SimpleAnalyzerAssemblyLoader : AssemblyLoadContext, IAnalyzerAssemblyLoader, IDisposable
{
    private readonly Dictionary<string, string> _dependencyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SimpleAnalyzerAssemblyLoader()
        : base("SharpProof.VsixHarness.Analyzer", true)
    {
    }

    public void AddDependencyLocation(string fullPath)
    {
        var resolvedPath = Path.GetFullPath(fullPath);
        var assemblyName = Path.GetFileNameWithoutExtension(resolvedPath);
        if (assemblyName.Length == 0) return;

        lock (_gate)
            _dependencyPaths[assemblyName] = resolvedPath;
    }

    public Assembly LoadFromPath(string fullPath)
    {
        var resolvedPath = Path.GetFullPath(fullPath);
        AddDependencyLocation(resolvedPath);
        var requestedName = AssemblyName.GetAssemblyName(resolvedPath);
        var loaded = FindExactLoadedAssembly(requestedName);
        return loaded ?? LoadFromAssemblyPath(resolvedPath);
    }

    public void Dispose()
    {
        Unload();
    }

    protected override Assembly? Load(AssemblyName requestedName)
    {
        if (requestedName.Name == null) return null;

        var loaded = FindExactLoadedAssembly(requestedName);
        if (loaded != null) return loaded;

        string? dependencyPath;
        lock (_gate)
            _dependencyPaths.TryGetValue(requestedName.Name, out dependencyPath);
        if (dependencyPath == null) return null;

        return LoadFromAssemblyPath(dependencyPath);
    }

    private Assembly? FindExactLoadedAssembly(AssemblyName requestedName)
    {
        return Assemblies.Concat(AssemblyLoadContext.Default.Assemblies).FirstOrDefault(candidate =>
        {
            var loadedName = candidate.GetName();
            return AssemblyName.ReferenceMatchesDefinition(loadedName, requestedName) &&
                   Equals(loadedName.Version, requestedName.Version);
        });
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("HARNESS ERROR: " + ex);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var solutionRoot = FindRepoRoot();
        var vsixPath = args.Length > 0
            ? args[0]
            : Path.Combine(solutionRoot, "SharpProof.Vsix", "bin", "Release", "SharpProof.Vsix.vsix");

        if (!File.Exists(vsixPath))
        {
            vsixPath = CreateSimulatedVsix(solutionRoot);
            Console.WriteLine($"Created simulated VSIX at: {vsixPath}");
        }

        var payload = ExtractVsixPayload(vsixPath);
        try
        {
            var attributesDll = Path.Combine(solutionRoot, "SharpProof.Attributes", "bin", "Release",
                "netstandard2.0", "SharpProof.Attributes.dll");
            var useRealAttributes = File.Exists(attributesDll);
            var source = useRealAttributes
                ? """
                  using SharpProof.Attributes;
                  namespace TestNamespace;

                  public class C
                  {
                      [EnforcePure]
                      public void M() => System.Console.WriteLine("impure");
                  }
                  """
                : """
                  using System;
                  namespace SharpProof.Attributes
                  {
                      public sealed class EnforcePureAttribute : Attribute { }
                      public sealed class PureAttribute : Attribute { }
                      public sealed class AllowSynchronizationAttribute : Attribute { }
                  }

                  namespace TestNamespace
                  {
                      public class C
                      {
                          [SharpProof.Attributes.EnforcePure]
                          public void M() => System.Console.WriteLine("impure");
                      }
                  }
                  """;

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var references = GetTrustedPlatformReferences().ToList();
            if (useRealAttributes)
            {
                references.Add(MetadataReference.CreateFromFile(attributesDll));
                Console.WriteLine($"Using real attributes assembly: {attributesDll}");
            }
            else
            {
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
            foreach (var diagnostic in analyzerDiagnostics)
            {
                var location = diagnostic.Location.GetLineSpan();
                Console.WriteLine(
                    $"  {diagnostic.Id}: {diagnostic.GetMessage()} @ {location.Path}({location.StartLinePosition.Line + 1},{location.StartLinePosition.Character + 1})");
            }

            if (analyzerDiagnostics.Any(static diagnostic => diagnostic.Id == "AD0001"))
                throw new InvalidOperationException("Analyzer execution produced AD0001.");
            if (!analyzerDiagnostics.Any(static diagnostic => diagnostic.Id == "SP0002"))
                throw new InvalidOperationException("Analyzer did not produce the expected SP0002 diagnostic.");

            return 0;
        }
        finally
        {
            TryDeleteDirectory(payload.Directory.FullName);
        }
    }

    private static IEnumerable<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES was not available.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }

    private static ExtractedVsixPayload ExtractVsixPayload(string vsixPath)
    {
        var directory = Directory.CreateTempSubdirectory("SharpProofVsixHarness");
        var root = directory.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string? analyzerPath = null;

        using (var archive = ZipFile.OpenRead(vsixPath))
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;

                var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var destinationPath = Path.GetFullPath(Path.Combine(directory.FullName, relativePath));
                if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"VSIX entry escapes extraction root: {entry.FullName}");

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, true);
                if (entry.FullName.EndsWith("SharpProof.Analyzer.dll", StringComparison.OrdinalIgnoreCase))
                    analyzerPath = destinationPath;
            }

        if (analyzerPath == null)
            throw new FileNotFoundException("Analyzer DLL not found inside VSIX.");

        var managedAssemblies = Directory.GetFiles(directory.FullName, "*.dll", SearchOption.AllDirectories)
            .Where(static path =>
            {
                try
                {
                    _ = AssemblyName.GetAssemblyName(path);
                    return true;
                }
                catch (BadImageFormatException)
                {
                    return false;
                }
            })
            .ToImmutableArray();
        return new ExtractedVsixPayload(directory, analyzerPath, managedAssemblies);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string CreateSimulatedVsix(string solutionRoot)
    {
        var analyzerDirectory = Path.Combine(solutionRoot, "SharpProof.Analyzer", "bin", "Release",
            "netstandard2.0");
        var analyzerPath = Path.Combine(analyzerDirectory, "SharpProof.Analyzer.dll");
        if (!File.Exists(analyzerPath))
            throw new FileNotFoundException($"Analyzer not found at {analyzerPath}. Build in Release first.");

        var tempDirectory = Directory.CreateTempSubdirectory("SharpProofSimVsix");
        var vsixPath = Path.Combine(tempDirectory.FullName, "SharpProof.Simulated.vsix");
        using (var archive = ZipFile.Open(vsixPath, ZipArchiveMode.Create))
            foreach (var file in Directory.GetFiles(analyzerDirectory, "*", SearchOption.AllDirectories))
            {
                var entryName = Path.GetRelativePath(analyzerDirectory, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName);
            }

        return vsixPath;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ExtractedVsixPayload(
        DirectoryInfo Directory,
        string AnalyzerPath,
        ImmutableArray<string> ManagedAssemblyPaths);
}
