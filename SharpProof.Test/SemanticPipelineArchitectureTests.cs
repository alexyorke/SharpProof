using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class SemanticPipelineArchitectureTests
{
    private static readonly string[] ProductionRoots =
    {
        "SharpProof.Analyzer",
        "SharpProof.CodeFixes",
        "SharpProof.Symbolic",
        "Tools"
    };

    [Test]
    public void LegacySourceTranslation_IsAbsentFromProduction()
    {
        AssertAllowlist(
            new[]
            {
                "LegacyFormulaCompatibility.",
                "CSharpConditionToFormula."
            },
            Array.Empty<string>());
    }

    [Test]
    public void SourceConsumers_UseResultBasedSemanticLoweringBoundary()
    {
        AssertAllowlist(
            new[] { "SymbolicIrLowerer.TryLower" },
            new[] { "SharpProof.Symbolic/Ir/SymbolicSemanticPipeline.cs" });
    }

    [Test]
    public void DirectSmtConstruction_IsLimitedToCanonicalAndLowLevelBoundaries()
    {
        AssertAllowlist(
            DirectSmtConstructionNeedles,
            new[]
            {
                "SharpProof.Symbolic/Ir/SymbolicIrFormulaEncoder.cs",
                "SharpProof.Symbolic/Smt/SmtAnalysisService.cs",
                "SharpProof.Symbolic/Smt/SmtFormulaFactory.cs",
                "SharpProof.Symbolic/Smt/SmtFormulaVersionRewriter.cs",
                "SharpProof.Symbolic/Smt/SmtPathConditionMerger.cs",
                "SharpProof.Symbolic/Smt/SmtSyntacticClassifier.cs",
                "SharpProof.Symbolic/SymbolicFactFactory.cs",
                "SharpProof.Symbolic/SymbolicInputDomainSynthesizer.cs",
                "SharpProof.Symbolic/SymbolicInvariantService.cs",
                "SharpProof.Symbolic/SymbolicProofPipeline.cs",
                "SharpProof.Symbolic/SymbolicReachabilityService.cs",
                "SharpProof.Symbolic/SymbolicSourceQueryService.cs"
            });
    }

    [Test]
    public void AttributeNameMatching_IsLimitedToTheMigrationAllowlist()
    {
        AssertAllowlist(
            new[]
            {
                "IsAttributeNamed(",
                "t.Name is \"EnforcePureAttribute\"",
                "t.Name == \"AllowSynchronizationAttribute\"",
                "c?.Name == \"EnforcePureAttribute\"",
                "string.Equals(attributeClass.Name",
                "string.Equals(originalDefinition.Name",
                "SharpProofAttributeNames.Contains(attributeClass.Name)",
                "attribute.AttributeClass?.ToDisplayString()"
            },
            new[]
            {
                "SharpProof.Analyzer/ExceptionFlowAnalyzer.SpecialCases.cs",
                "SharpProof.Analyzer/MethodPurityAnalyzer.cs",
                "SharpProof.Analyzer/SharpProofAttributeIdentityPolicy.cs",
                "SharpProof.Analyzer/TrustedBoundaryReviewAnalyzer.cs"
            });
    }

    [Test]
    public void ConfigurationParsing_IsLimitedToTheMigrationAllowlist()
    {
        AssertAllowlist(
            new[]
            {
                "ParseRuntimeHazardMode(",
                "GetSmtMode(",
                "TryParseBool("
            },
            new[]
            {
                "SharpProof.Analyzer/Configuration/AnalyzerConfiguration.cs",
                "SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs",
                "SharpProof.Symbolic/SymbolicProjectQueryContext.cs"
            });
    }

    [Test]
    public void ConfiguredMemberPolicy_UsesStructuralIdentityWithoutDisplayAliases()
    {
        var repositoryRoot = FindRepositoryRoot();
        var keySource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Configuration",
            "ConfiguredMemberKey.cs"));
        var catalogSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "ImpurityCatalog.cs"));

        Assert.That(keySource, Does.Contain("RoslynStructuralMethodIdentityAdapter.GetCanonicalKey"));
        Assert.That(keySource, Does.Contain("MethodKind.PropertyGet"));
        Assert.That(keySource, Does.Contain("MethodKind.PropertySet"));
        Assert.That(catalogSource, Does.Contain("ConfiguredMemberKey.TryCreate"));
        Assert.That(catalogSource, Does.Not.Contain("GetPropertyAccessorSignatureCandidates"));
        Assert.That(catalogSource, Does.Not.Contain("MatchesConfiguredKnownPureSignature"));
        Assert.That(catalogSource, Does.Not.Contain("NormalizeSignatures(Extra"));
    }

    [Test]
    public void EffectSummaryIdentity_HasNoLegacyAliasesOrDisplayKeyNormalizers()
    {
        AssertAllowlist(
            new[]
            {
                "ExactSymbolKey",
                "EffectSummarySymbolKeyFactory",
                "EffectSummaryExactSymbolKeyNormalizer",
                "LegacySignature"
            },
            Array.Empty<string>());
    }

    [Test]
    public void MigrationPipelineSelector_IsDeleted()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controlPath = Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicPipelineTestControl.cs");
        var productionSources = EnumerateProductionSources(repositoryRoot)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.That(File.Exists(controlPath), Is.False);
        Assert.That(
            productionSources.Any(static source =>
                source.Contains("SymbolicPipelineTestControl", StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public void ProofOrchestration_HasOneEncodedRequestBoundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pipelineSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProofPipeline.cs"));
        var serviceSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProofService.cs"));
        var smtSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Smt",
            "SmtAnalysisService.cs"));

        Assert.That(pipelineSource, Does.Contain("internal sealed class SymbolicProofPipeline"));
        Assert.That(pipelineSource, Does.Contain("using var fallback = new SmtAnalysisService"));
        Assert.That(serviceSource, Does.Not.Contain("new SmtAnalysisService("));
        Assert.That(serviceSource, Does.Contain("proofPipeline.ClassifyReachability("));
        Assert.That(serviceSource, Does.Contain("proofPipeline.ClassifyImplication("));

        var normalization = smtSource.IndexOf("var pathConditions = NormalizePathConditions", StringComparison.Ordinal);
        var syntactic = smtSource.IndexOf("TryClassifySyntactically", normalization, StringComparison.Ordinal);
        var budgeting = smtSource.IndexOf("Options.MaxPathConditions", syntactic, StringComparison.Ordinal);
        var execution = smtSource.IndexOf("ClassifyLocally(normalizedQuery, key)", budgeting, StringComparison.Ordinal);
        Assert.That(normalization, Is.GreaterThanOrEqualTo(0));
        Assert.That(syntactic, Is.GreaterThan(normalization));
        Assert.That(budgeting, Is.GreaterThan(syntactic));
        Assert.That(execution, Is.GreaterThan(budgeting));
    }

    private static readonly string[] DirectSmtConstructionNeedles =
    {
        "new SmtBinaryFormula",
        "new SmtUnaryFormula",
        "new SmtIntegerConstant",
        "new SmtNullConstant",
        "new SmtBooleanConstant",
        "new SmtVariable",
        "new SmtIntegerBinaryTerm",
        "new SmtIntegerUnaryTerm",
        "new SmtStringLengthTerm",
        "new SmtStringConcatTerm",
        "new SmtStringContainsFormula",
        "new SmtStringStartsWithFormula",
        "new SmtStringEndsWithFormula",
        "new SmtRegexMatchFormula",
        "new SmtRuntimeTypeTestFormula",
        "new SmtConditionalFormula"
    };

    private static void AssertAllowlist(IEnumerable<string> needles, IEnumerable<string> expectedPaths)
    {
        var repositoryRoot = FindRepositoryRoot();
        var actual = EnumerateProductionSources(repositoryRoot)
            .Where(path => ContainsAny(File.ReadAllText(path), needles))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var expected = expectedPaths.OrderBy(static path => path, StringComparer.Ordinal).ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static bool ContainsAny(string source, IEnumerable<string> needles)
    {
        return needles.Any(needle => source.Contains(needle, StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateProductionSources(string repositoryRoot)
    {
        return ProductionRoots
            .Select(root => Path.Combine(repositoryRoot, root))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(static path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
