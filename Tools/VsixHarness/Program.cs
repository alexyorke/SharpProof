using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

internal sealed class SimpleAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
{
    public void AddDependencyLocation(string fullPath) { }
    public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
}

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var solutionRoot = FindRepoRoot();
            var vsixPath = args != null && args.Length > 0
                ? args[0]
                : Path.Combine(solutionRoot, "SharpProof.Vsix", "bin", "Release", "SharpProof.Vsix.vsix");

            // If a VSIX was not produced by the build, simulate one by zipping the analyzer DLL.
            if (!File.Exists(vsixPath))
            {
                vsixPath = CreateSimulatedVsix(solutionRoot);
                Console.WriteLine($"Created simulated VSIX at: {vsixPath}");
            }

            string analyzerDllPath;
            using (var vsix = ZipFile.OpenRead(vsixPath))
            {
                var analyzerEntry = vsix.Entries.FirstOrDefault(e => e.FullName.EndsWith("SharpProof.Analyzer.dll", StringComparison.OrdinalIgnoreCase));
                if (analyzerEntry == null)
                {
                    throw new FileNotFoundException("Analyzer DLL not found inside VSIX.");
                }

                var tempDir = Directory.CreateTempSubdirectory("SharpProofVsixHarness");
                analyzerDllPath = Path.Combine(tempDir.FullName, "SharpProof.Analyzer.dll");
                analyzerEntry.ExtractToFile(analyzerDllPath, overwrite: true);
            }

            // Prefer referencing the real Attributes assembly if available to simulate a real user project.
            var attributesDll = Path.Combine(solutionRoot, "SharpProof.Attributes", "bin", "Release", "netstandard2.0", "SharpProof.Attributes.dll");
            bool useRealAttributes = File.Exists(attributesDll);
            var source = useRealAttributes
                ? @"
using SharpProof.Attributes;
namespace TestNamespace {
    public class C {
        [EnforcePure]
        public void M() { }
    }
}
"
                : @"
using System;
namespace SharpProof.Attributes { public sealed class EnforcePureAttribute : Attribute {} public sealed class PureAttribute : Attribute {} public sealed class AllowSynchronizationAttribute : Attribute {} }
namespace TestNamespace {
    public class C {
        [SharpProof.Attributes.EnforcePure]
        public void M() { }
    }
}
";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var references = new System.Collections.Generic.List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            };
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
                assemblyName: "VsixHarnessCompilation",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var loader = new SimpleAnalyzerAssemblyLoader();
            var analyzerRef = new AnalyzerFileReference(analyzerDllPath, loader);
            var analyzers = analyzerRef.GetAnalyzers(LanguageNames.CSharp);
            var diagnostics = compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Analyzer executed. Diagnostics count: {diagnostics.Length}");
            foreach (var d in diagnostics)
            {
                var loc = d.Location.GetLineSpan();
                Console.WriteLine($"  {d.Id}: {d.GetMessage()} @ {loc.Path}({loc.StartLinePosition.Line + 1},{loc.StartLinePosition.Character + 1})");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("HARNESS ERROR: " + ex.ToString());
            return 1;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpProof.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }

    private static string CreateSimulatedVsix(string solutionRoot)
    {
        var analyzerPath = Path.Combine(solutionRoot, "SharpProof.Analyzer", "bin", "Release", "netstandard2.0", "SharpProof.Analyzer.dll");
        if (!File.Exists(analyzerPath))
        {
            throw new FileNotFoundException($"Analyzer not found at {analyzerPath}. Build in Release first.");
        }

        var tempDir = Directory.CreateTempSubdirectory("SharpProofSimVsix");
        var vsixPath = Path.Combine(tempDir.FullName, "SharpProof.Simulated.vsix");
        using (var archive = ZipFile.Open(vsixPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(analyzerPath, "SharpProof.Analyzer.dll");
        }
        return vsixPath;
    }
}


