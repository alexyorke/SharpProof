using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectArchitectureTests {
    [Test]
    public void RemovedEffectInfrastructureCannotReturnToSupportedSurface() {
        var root = SymbolicCliTestHost.FindRepositoryRoot();
        Assert.That(
            File.Exists(Path.Combine(
                root,
                "Tools",
                "SharpProof." + "Effect" + "Summary",
                "SharpProof." + "Effect" + "Summary.csproj")),
            Is.False);

        var removedFiles = new[] {
            "SharpProof.Contracts/BclPurityFallbackHeuristics.cs",
            "SharpProof.Contracts/InferredMethodSummary.cs",
            "SharpProof.Analyzer/Engine/Rules/CompilationSyntaxAccess.cs",
            "SharpProof.Analyzer/Engine/Rules/InvocationEvidence.cs",
            "SharpProof.Analyzer/LowerHexEncoding.cs",
            "SharpProof.Symbolic/SymbolicCapabilityService.cs",
            "SharpProof.Symbolic/SymbolicCapabilityModels.cs",
            "Tools/SharpProof.SymbolicCli.Core/SharpProof.SymbolicCli.Core.csproj"
        };
        foreach (var relativePath in removedFiles)
            Assert.That(File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))),
                Is.False, $"Removed compatibility file returned: {relativePath}");

        var roots = new[] {
            "SharpProof.Analyzer", "SharpProof.Attributes", "SharpProof.CodeFixes", "SharpProof.Contracts",
            "SharpProof.Package", "SharpProof.Symbolic", "SharpProof.Vsix", "Tools", "config", "docs"
        };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".cs", ".csproj", ".json", ".props", ".targets", ".md"
        };
        var forbidden = new[] {
            "SharpProof." + "Effect" + "Summary",
            ".SharpProof." + "Effect" + "Summary.json",
            "Generated" + "Purity",
            "Known" + "Pure",
            "Known" + "Impure",
            "Purity" + "Profile",
            "Purity" + "ProofQuery",
            "Purity" + "ProofSearch",
            "Inferred" + "MethodSummary",
            "Bcl" + "PurityFallbackHeuristics",
            "Purity" + "PolicyImpact",
            "Missing" + "PuritySuggestionScope",
            "Collect" + "AncestorReachabilityState",
            "SHARPPROOF_" + "TRACE_CFG_FALLBACK",
            "Symbolic" + "CapabilityResult",
            "Method" + "PurityAnalyzer",
            "Sp0002" + "Expectation",
            "sharpproof." + "impurity.",
            "Exception" + "FlowQuery",
            "Allow" + "Synchronization",
            "Pure" + "External"
        };
        var legacyDiagnostic = new System.Text.RegularExpressions.Regex(
            @"SP00(?:4[89]|5[0-9]|6[0-9]|7[0-6])",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        foreach (var relativeRoot in roots) {
            var path = Path.Combine(root, relativeRoot);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) {
                if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    !extensions.Contains(Path.GetExtension(file))) continue;
                var text = File.ReadAllText(file);
                foreach (var value in forbidden)
                    Assert.That(text, Does.Not.Contain(value), $"Forbidden legacy surface in {file}: {value}");
                Assert.That(legacyDiagnostic.IsMatch(text), Is.False, $"Disabled diagnostic returned in {file}");
            }
        }
    }
}
