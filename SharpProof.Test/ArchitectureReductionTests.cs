using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class ArchitectureReductionTests
{
    private static readonly ConcurrentDictionary<string, string> s_fileContentCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<PowerShellJsonScriptCacheKey, Lazy<Task<string>>>
        s_powerShellJsonOutputCache =
            new();

    private readonly record struct PowerShellJsonScriptCacheKey(
        string RepositoryRoot,
        string ScriptName);

    private static string ReadFileCached(string path)
    {
        return s_fileContentCache.GetOrAdd(path, static key =>
        {
            var fileName = Path.GetFileName(key);
            if (string.Equals(fileName, "PurityAnalysisEngine.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "ExecutionVisibility.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "MethodInvocationPurityRule.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "AssignmentPurityRule.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "PropertyReferencePurityRule.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "ReturnStatementPurityRule.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "ExceptionFlowAnalyzer.PathFacts.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "ExceptionFlowAnalyzer.ExceptionSites.cs",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "ExceptionFlowQuery.cs", StringComparison.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(key) ??
                                throw new InvalidOperationException($"{fileName} path has no directory.");
                var partialPattern = Path.GetFileNameWithoutExtension(fileName) + "*.cs";
                if (string.Equals(fileName, "ExceptionFlowAnalyzer.PathFacts.cs", StringComparison.OrdinalIgnoreCase))
                {
                    var pathFactsFiles = new[]
                    {
                        "ExceptionFlowAnalyzer.PathFacts.cs",
                        "ExceptionFlowAnalyzer.PathFacts.Branches.cs",
                        "ExceptionFlowAnalyzer.PathFacts.Symbols.cs",
                        "ExceptionFlowAnalyzer.PathFacts.Finally.cs",
                        "ExceptionFlowAnalyzer.PathFacts.NormalCompletion.cs",
                        "ExceptionFlowAnalyzer.PathFacts.AssignmentInvalidation.cs",
                        "ExceptionFlowAnalyzer.PathFacts.ExpressionFacts.cs",
                        "ExceptionFlowAnalyzer.PathFacts.MutationTracking.cs"
                    };
                    return string.Join(
                        Environment.NewLine,
                        pathFactsFiles
                            .Select(partialFile => Path.Combine(directory, partialFile))
                            .Where(File.Exists)
                            .Select(File.ReadAllText));
                }

                if (string.Equals(fileName, "ExecutionVisibility.cs", StringComparison.OrdinalIgnoreCase))
                {
                    var executionVisibilityFiles = new[]
                    {
                        "ExecutionVisibility.cs",
                        "ExecutionVisibility.Descendants.cs",
                        "ExecutionVisibility.EvaluationPathFacts.cs",
                        "ExecutionVisibility.SharedFacts.cs",
                        "ExecutionVisibility.SwitchStatements.cs",
                        "ExecutionVisibility.SwitchExpressions.cs",
                        "ExecutionVisibility.ConditionTruth.cs"
                    };
                    return string.Join(
                        Environment.NewLine,
                        executionVisibilityFiles
                            .Select(partialFile => Path.Combine(directory, partialFile))
                            .Where(File.Exists)
                            .Select(File.ReadAllText));
                }

                if (string.Equals(fileName, "AssignmentPurityRule.cs", StringComparison.OrdinalIgnoreCase))
                {
                    var assignmentRuleFiles = new[]
                    {
                        "AssignmentPurityRule.cs",
                        "AssignmentPurityRule.CompoundAssignments.cs",
                        "AssignmentPurityRule.PropertySetters.cs",
                        "AssignmentPurityRule.TargetPurity.cs",
                        "PropertyDispatchHelper.cs",
                        "RuleAnalysisHelper.cs"
                    };
                    return string.Join(
                        Environment.NewLine,
                        assignmentRuleFiles
                            .Select(partialFile => Path.Combine(directory, partialFile))
                            .Where(File.Exists)
                            .Select(File.ReadAllText));
                }

                if (string.Equals(fileName, "PropertyReferencePurityRule.cs", StringComparison.OrdinalIgnoreCase))
                {
                    var propertyRuleFiles = new[]
                    {
                        "PropertyReferencePurityRule.cs",
                        "PropertyReferencePurityRule.Arguments.cs",
                        "PropertyReferencePurityRule.SpecialCases.cs",
                        "PropertyReferencePurityRule.DictionaryDispatch.cs",
                        "PropertyReferencePurityRule.GetterDispatch.cs",
                        "PropertyReferencePurityRule.MetadataGetters.cs",
                        "PropertyReferencePurityRule.GetterTargets.cs"
                    };
                    return string.Join(
                        Environment.NewLine,
                        propertyRuleFiles
                            .Select(partialFile => Path.Combine(directory, partialFile))
                            .Where(File.Exists)
                            .Select(File.ReadAllText));
                }

                if (string.Equals(fileName, "ExceptionFlowAnalyzer.ExceptionSites.cs",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var exceptionSiteFiles = new[]
                    {
                        "ExceptionFlowAnalyzer.ExceptionSites.cs",
                        "ExceptionFlowAnalyzer.ExceptionSites.NullFacts.cs",
                        "ExceptionFlowAnalyzer.ExceptionSites.CheckedOverflow.cs",
                        "ExceptionFlowAnalyzer.ExceptionSites.CastsAndStores.cs",
                        "ExceptionFlowAnalyzer.ExceptionSites.NullableAccess.cs",
                        "ExceptionFlowAnalyzer.ExceptionSites.RangeAccess.cs",
                        "ExceptionFlowAnalyzer.ExceptionSites.TypeFacts.cs"
                    };
                    return string.Join(
                        Environment.NewLine,
                        exceptionSiteFiles
                            .Select(partialFile => Path.Combine(directory, partialFile))
                            .Where(File.Exists)
                            .Select(File.ReadAllText));
                }

                if (string.Equals(fileName, "ExceptionFlowQuery.cs", StringComparison.OrdinalIgnoreCase))
                {
                    var exceptionQueryFiles = new[]
                    {
                        "ExceptionFlowQuery.cs",
                        "ExceptionFlowQuery.SiteCollection.cs",
                        "ExceptionFlowQuery.RuntimeHazards.cs",
                        "ExceptionFlowQuery.Callees.cs",
                        "ExceptionFlowQuery.Catches.cs",
                        "ExceptionFlowQuery.Models.cs"
                    };
                    return string.Join(
                        Environment.NewLine,
                        exceptionQueryFiles
                            .Select(partialFile => Path.Combine(directory, partialFile))
                            .Where(File.Exists)
                            .Select(File.ReadAllText));
                }

                return string.Join(
                    Environment.NewLine,
                    Directory.GetFiles(directory, partialPattern)
                        .OrderBy(static partialPath => partialPath, StringComparer.OrdinalIgnoreCase)
                        .Select(File.ReadAllText));
            }

            return File.ReadAllText(key);
        });
    }

    [Test]
    public void AnalyzerReachability_DoesNotOpenCodeBranchProofQueries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var analyzerFiles = Directory.GetFiles(
            Path.Combine(repositoryRoot, "SharpProof.Analyzer"),
            "*.cs",
            SearchOption.AllDirectories);

        var offenders = analyzerFiles
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path)
            })
            .Where(static file =>
                file.Source.Contains("new PurityProofQuery", StringComparison.Ordinal) ||
                file.Source.Contains("PurityHazardKind.BranchReachability", StringComparison.Ordinal) ||
                file.Source.Contains(".ClassifyPathFeasibility(", StringComparison.Ordinal) ||
                file.Source.Contains("CSharpConditionToFormula.TryCollectBranchAssumptions", StringComparison.Ordinal))
            .Select(static file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void SymbolicReachabilityService_IsCanonicalProofFacade()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicReachabilityService.cs"));

        Assert.That(source, Does.Contain("ClassifyStateFeasibility"));
        Assert.That(source, Does.Contain("ClassifyStateConditionTruth"));
        Assert.That(source, Does.Contain("ApplyBranchFacts"));
        Assert.That(source, Does.Contain("CollectPathStateAt"));
        Assert.That(source, Does.Not.Contain("SmtFormula"));
    }

    [Test]
    public void CompactDomainProjections_LiveInPublicSymbolicApi()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cliSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "Tools",
            "SharpProof.SymbolicCli",
            "Program.cs"));
        var publicSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicCompactDomainResults.cs"));

        Assert.That(cliSource, Does.Contain("capabilityResult.ToCompactResult()"));
        Assert.That(cliSource, Does.Contain("complexityResult.ToCompactResult()"));
        Assert.That(cliSource, Does.Contain("hazardResult.ToCompactResult("));
        Assert.That(cliSource, Does.Not.Contain("internal sealed class CompactSymbolic"));
        Assert.That(cliSource, Does.Not.Contain("internal sealed class CompactRuntimeHazard"));
        Assert.That(publicSource, Does.Contain("public interface ISymbolicCompactResult"));
        Assert.That(publicSource, Does.Contain("public sealed class SymbolicCompactCapabilityResult"));
        Assert.That(publicSource, Does.Contain("public sealed class SymbolicCompactComplexityResult"));
        Assert.That(publicSource, Does.Contain("public sealed class SymbolicCompactRuntimeHazardQueryResult"));
        Assert.That(publicSource, Does.Contain("SharpProofEvidenceSchema.CurrentVersion"));
        Assert.That(publicSource, Does.Contain("SharpProofEvidenceSchema.CompatibilityPolicy"));
    }

    [Test]
    public void ProductionSmtAnalysisServiceConstruction_IsLimitedToOwnedBoundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "SharpProof.Analyzer/Engine/CompilationPurityService.cs",
            "SharpProof.Analyzer/SharpProofDiagnosticSuppressor.cs",
            "SharpProof.Symbolic/SymbolicProofPipeline.cs",
            "scripts/package-consumers/SymbolicConsumer.cs",
            "Tools/SharpProof.SymbolicCli/Program.cs"
        };
        var offenders = Directory.GetFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains(
                                      $"{Path.DirectorySeparatorChar}SharpProof.Test{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains(
                                      $"{Path.DirectorySeparatorChar}SharpProof.ToolingTest{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path)
            })
            .Where(file => file.Source.Contains("new SmtAnalysisService(", StringComparison.Ordinal) &&
                           !allowed.Contains(file.Path))
            .Select(file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ProductionDefaultSmtFallback_IsOwnedBySymbolicProofPipeline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var offenders = Directory.GetFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains(
                                      $"{Path.DirectorySeparatorChar}SharpProof.Test{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains(
                                      $"{Path.DirectorySeparatorChar}SharpProof.ToolingTest{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path)
            })
            .Where(file =>
                file.Source.Contains("new SmtAnalysisService(SmtAnalysisOptions.Default)", StringComparison.Ordinal) &&
                !string.Equals(file.Path, "SharpProof.Symbolic/SymbolicProofPipeline.cs", StringComparison.Ordinal) &&
                !string.Equals(file.Path, "scripts/package-consumers/SymbolicConsumer.cs", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToArray();
        var proofPipelineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProofPipeline.cs"));

        Assert.That(offenders, Is.Empty);
        Assert.That(proofPipelineSource, Does.Contain("only ad hoc solver fallback"));
        Assert.That(proofPipelineSource,
            Does.Contain("using var fallback = new SmtAnalysisService(SmtAnalysisOptions.Default);"));
    }

    [Test]
    public void SymbolicRuntimeHazardQueryService_RoutesIrTriggerProofsThroughProofService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardQueryService.cs"));

        Assert.That(source, Does.Contain("ClassifyStateHazardTrigger("));
        Assert.That(source, Does.Contain("ClassifyStateConditionTruth("));
        Assert.That(source, Does.Contain("ClassifyIrTrigger("));
        Assert.That(source,
            Does.Not.Contain("new SymbolicNotCondition(new SymbolicFactCondition(triggerPrecondition))"));
        Assert.That(source, Does.Not.Contain("ClassifyStateImplication("));
        Assert.That(source, Does.Not.Contain("ClassifyFormulaConditionTruth"));
        Assert.That(source, Does.Not.Contain("PathConditionsImply"));
    }

    [Test]
    public void FieldReferenceRule_DelegatesFreshOwnershipClassification()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "FieldReferencePurityRule.cs"));

        Assert.That(source,
            Does.Contain("OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableReadonlyFieldReference("));
        Assert.That(source, Does.Contain("OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference("));
        Assert.That(source, Does.Not.Contain("internal static class OwnedFreshMutableObjectClassifier"));
        Assert.That(source, Does.Not.Contain("private static bool IsOwnedFreshMutableObjectReference("));
        Assert.That(source, Does.Not.Contain("private static bool HasStableFreshMutableObjectValue("));
    }

    [Test]
    public void AssignmentRule_DelegatesFreshOwnershipClassification()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "AssignmentPurityRule.cs"));

        Assert.That(source, Does.Contain("OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference("));
        Assert.That(source, Does.Not.Contain("private static bool IsOwnedFreshMutableObjectReference("));
        Assert.That(source, Does.Not.Contain("private static bool IsOwnedFreshMutableReadonlyFieldReference("));
        Assert.That(source, Does.Not.Contain("private static bool HasStableFreshMutableObjectValue("));
        Assert.That(source, Does.Not.Contain("private static bool ConstructorStoresParameterInStableMember("));
    }

    [Test]
    public void FreshMutableObjectClassifier_ConsumesSymbolicAndAllPathAssignmentFacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var classifierSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "OwnedFreshMutableObjectClassifier.cs"));
        var returnSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "ReturnStatementPurityRule.cs"));

        Assert.That(classifierSource,
            Does.Contain("PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(localSymbol, state)"));
        Assert.That(classifierSource, Does.Contain("IsAssignedFreshMutableObjectOnAllPaths("));
        Assert.That(classifierSource, Does.Contain("AnalyzeFreshMutableAssignments("));
        Assert.That(classifierSource,
            Does.Contain(
                "return IsOwnedFreshMutableLocal(localReference.Local, initializerSyntax, semanticModel, null, visitedLocals,"));
        Assert.That(returnSource, Does.Contain("OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableLocal("));
        Assert.That(returnSource, Does.Contain("\"symbolic_fresh_mutable_object_return\""));
    }

    [Test]
    public void AnalyzerOwnedArrayFlowCaptures_ProjectOwnershipFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source,
            Does.Contain("WithOwnedArrayFlowCapture(flowCaptureOperation.Id, flowCaptureOperation.Syntax)"));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateFreshOwned("));
        Assert.That(source, Does.Contain("RemoveOwnedArrayFlowCaptureFacts(PathState, id)"));
        Assert.That(source, Does.Contain("SymbolicResourceLifetimeAtom lifetime => Equals(lifetime.Resource, term)"));
    }

    [Test]
    public void AnalyzerOwnedLocalArrays_ProjectValueOwnershipFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddOwnedLocalArrayFacts("));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateFreshOwnedValue("));
        Assert.That(source, Does.Contain("\"analyzer.array.acquire\""));
        Assert.That(source, Does.Contain("\"evidence.array.acquire\""));
    }

    [Test]
    public void AnalyzerFreshMutableObjects_ProjectValueOwnershipFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddFreshMutableObjectFacts("));
        Assert.That(source,
            Does.Contain("RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type)"));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateFreshOwnedValue("));
        Assert.That(source, Does.Contain("\"analyzer.object.acquire\""));
        Assert.That(source, Does.Contain("\"evidence.object.acquire\""));
    }

    [Test]
    public void AnalyzerDisposeInvocations_ProjectDisposalFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source,
            Does.Contain("nextState = AddDisposeInvocationFacts(nextState, invocationOperation, currentState);"));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateDisposal("));
        Assert.That(source, Does.Contain("SymbolicDisposalState.Disposed"));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateResourceLifetime("));
        Assert.That(source, Does.Contain("SymbolicResourceLifetimeState.Released"));
        Assert.That(source, Does.Contain("targetMethod.Name is nameof(IDisposable.Dispose) or \"DisposeAsync\""));
    }

    [Test]
    public void AnalyzerUsingStatements_ProjectImplicitDisposalFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddUsingStatementDisposeFacts("));
        Assert.That(source, Does.Contain("EnumerateUsingStatementDisposedSymbols("));
        Assert.That(source, Does.Contain("\"analyzer.resource.using.dispose\""));
        Assert.That(source, Does.Contain("\"evidence.resource.using.dispose\""));
        Assert.That(source, Does.Contain("AddResourceDisposedFacts("));
        Assert.That(source, Does.Contain("AddCompletedStraightLineUsingDisposeFacts("));
        Assert.That(source, Does.Contain("AddScopeEndResourceDisposeFacts("));
        Assert.That(source, Does.Contain("AddStraightLineResourceActionFacts("));
        Assert.That(source, Does.Contain("AddUsingDeclarationDisposeFacts("));
        Assert.That(source, Does.Contain("IsStraightLineUsingStatement("));
    }

    [Test]
    public void AnalyzerDisposableLocalAcquisition_ProjectsOwnershipFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddOwnedDisposableLocalFacts("));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateFreshOwned("));
        Assert.That(source, Does.Contain("\"analyzer.resource.acquire\""));
        Assert.That(source, Does.Contain("SymbolicDisposalState.NotDisposed"));
        Assert.That(source, Does.Contain("IsUsingResourceDeclarator("));
        Assert.That(source, Does.Contain("type.ToDisplayString() == \"System.IAsyncDisposable\""));
        Assert.That(source, Does.Contain("interfaceType.ToDisplayString() == \"System.IAsyncDisposable\""));
    }

    [Test]
    public void AnalyzerMissingDisposal_UsesPostCfgSymbolicResourceFacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("TryCreateMissingOwnedResourceDisposalResult("));
        Assert.That(source, Does.Contain("postCfgExitResourceState"));
        Assert.That(source, Does.Contain("SymbolicResourceLifetimeState.Owned"));
        Assert.That(source, Does.Contain("SymbolicDisposalState.NotDisposed"));
        Assert.That(source, Does.Contain("IsResourceReleased("));
        Assert.That(source, Does.Contain("EnumerateSymbolicAliasTerms(resource, state)"));
        Assert.That(source, Does.Contain("TryFindAliasedOwnedResourceLostByReassignment("));
        Assert.That(source, Does.Contain("AddPreservedOwnedDisposableAliasFacts("));
        Assert.That(source, Does.Contain("\"analyzer.resource.alias-preserve\""));
        Assert.That(source, Does.Contain("\"resource_missing_dispose\""));
    }

    [Test]
    public void AnalyzerRoslynLookups_ThreadCancellationToken()
    {
        var repositoryRoot = FindRepositoryRoot();
        var analyzerFiles = Directory.GetFiles(
                Path.Combine(repositoryRoot, "SharpProof.Analyzer"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path).Replace("\r\n", "\n")
            })
            .ToArray();
        var tokenFreeRoslynLookupPatterns = new[]
        {
            @"GetSyntax\(\)",
            @"GetOperation\([^,\r\n]+\)",
            @"GetTypeInfo\([^,\r\n]+\)",
            @"GetSymbolInfo\([^,\r\n]+\)",
            @"GetDeclaredSymbol\([^,\r\n]+\)",
            @"GetConstantValue\([^,\r\n]+\)"
        };

        var offenders = analyzerFiles
            .Where(file => tokenFreeRoslynLookupPatterns.Any(pattern => Regex.IsMatch(file.Source, pattern)))
            .Select(static file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void AnalyzerHelpers_RequireExplicitCancellationTokens()
    {
        var repositoryRoot = FindRepositoryRoot();
        var analyzerFiles = Directory.GetFiles(
                Path.Combine(repositoryRoot, "SharpProof.Analyzer"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path).Replace("\r\n", "\n")
            })
            .ToArray();

        var offenders = analyzerFiles
            .Where(static file => Regex.IsMatch(
                file.Source,
                @"CancellationToken\s+cancellationToken\s*=\s*default"))
            .Select(static file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ProductionFallbackCatches_DoNotSwallowCancellation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionRoots = new[]
        {
            "SharpProof.Analyzer",
            "SharpProof.Symbolic",
            "SearchLib"
        };
        var productionFiles = productionRoots
            .SelectMany(root => Directory.GetFiles(
                Path.Combine(repositoryRoot, root),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path).Replace("\r\n", "\n")
            })
            .ToArray();

        var cleanupAndRethrow = new HashSet<string>(StringComparer.Ordinal)
        {
            "SharpProof.Analyzer/AnalyzerSession.cs",
            "SharpProof.Analyzer/MethodBodyAnalysisState.cs"
        };
        var offenders = productionFiles
            .Where(static file =>
                Regex.IsMatch(file.Source, @"catch\s*\{") ||
                Regex.IsMatch(file.Source, @"catch\s*\(\s*Exception(?:\s+\w+)?\s*\)(?!\s*when)"))
            .Where(file => !cleanupAndRethrow.Contains(file.Path))
            .Select(static file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void SymbolicProductionCode_DoesNotDropCancellationTokens()
    {
        var repositoryRoot = FindRepositoryRoot();
        var symbolicFiles = Directory.GetFiles(
                Path.Combine(repositoryRoot, "SharpProof.Symbolic"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path).Replace("\r\n", "\n")
            })
            .ToArray();

        var offenders = symbolicFiles
            .Where(static file => file.Source.Contains("CancellationToken.None", StringComparison.Ordinal))
            .Select(static file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void SymbolicPatternLowering_ThreadsCancellationTokenThroughDesignationLookups()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
                repositoryRoot,
                "SharpProof.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Patterns.cs"))
            .Replace("\r\n", "\n");

        Assert.That(source, Does.Not.Contain("GetDeclaredSymbol(singleDesignation)"));
        Assert.That(source,
            Does.Contain("context.SemanticModel.GetDeclaredSymbol(singleDesignation, context.CancellationToken)"));
        Assert.That(source, Does.Contain("TryLowerDesignationPatternCondition("));
        Assert.That(source, Does.Contain("\"ir.pattern.designation\""));
    }

    [Test]
    public void AnalyzerReturnedOwnedResources_ProjectReturnedOwnershipFacts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddReturnedOwnedResourceFacts("));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateReturnedOwnership("));
        Assert.That(source, Does.Contain("SymbolicResourceLifetimeState.Returned"));
        Assert.That(source, Does.Contain("TryResolveTrackedSymbol(returnOperation.ReturnedValue, currentState)"));
        Assert.That(source, Does.Contain("\"analyzer.resource.returned\""));
        Assert.That(source, Does.Contain("\"evidence.resource.returned\""));
    }

    [Test]
    public void AnalyzerCallerVisibleMutation_ProjectsMutationFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddCallerVisibleMutationFact("));
        Assert.That(source, Does.Contain("TryCreateCallerVisibleMutationTerm("));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateMutation("));
        Assert.That(source, Does.Contain("\"analyzer.mutation.caller-visible\""));
        Assert.That(source, Does.Contain("\"evidence.mutation.caller-visible\""));
    }

    [Test]
    public void AnalyzerAssignment_ProjectsAliasFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddAssignedAliasFact("));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateAlias("));
        Assert.That(source, Does.Contain("\"analyzer.assignment.alias\""));
        Assert.That(source, Does.Contain("\"evidence.assignment.alias\""));
    }

    [Test]
    public void AnalyzerRefLocalDeclarations_ProjectBorrowFactsIntoPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("AddDeclaredBorrowFact("));
        Assert.That(source, Does.Contain("operationToTrack is IVariableDeclaratorOperation"));
        Assert.That(source, Does.Contain("RefExpressionSyntax"));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateBorrow("));
        Assert.That(source, Does.Contain("SymbolicBorrowKind.Mutable"));
        Assert.That(source, Does.Contain("SymbolicBorrowKind.Shared"));
        Assert.That(source, Does.Contain("\"analyzer.declaration.borrow\""));
        Assert.That(source, Does.Contain("\"evidence.declaration.borrow\""));
    }

    [Test]
    public void AssignmentRule_ConsumesSymbolicBorrowFactsForRefAliasMutation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var engineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));
        var assignmentSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "AssignmentPurityRule.cs"));
        var invocationSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "MethodInvocationPurityRule.cs"));

        Assert.That(engineSource, Does.Contain("HasSymbolicBorrowFactForLocal("));
        Assert.That(engineSource, Does.Contain("HasSymbolicBorrowFactForTerm("));
        Assert.That(engineSource, Does.Contain("HasSymbolicBorrowerFactForSymbol("));
        Assert.That(engineSource, Does.Contain("HasSymbolicBorrowerFactForTerm("));
        Assert.That(engineSource, Does.Contain("SymbolicBorrowAtom borrow"));
        Assert.That(engineSource, Does.Contain("EnumerateSymbolicAliasTerms(localTerm, currentState)"));
        Assert.That(engineSource, Does.Contain("EnumerateSymbolicAliasTerms(ownerTerm, currentState)"));
        Assert.That(engineSource, Does.Contain("TryCreateMutableBorrowConflictEvidence("));
        Assert.That(engineSource, Does.Contain("HasActiveRefLocalBorrowAfterWrite("));
        Assert.That(engineSource, Does.Contain("IsLocalUsedAfter("));
        Assert.That(assignmentSource,
            Does.Contain("HasSymbolicBorrowFactForLocal(local, currentState, SymbolicBorrowKind.Mutable)"));
        Assert.That(assignmentSource, Does.Contain("TryCreateMutableBorrowConflictEvidence("));
        Assert.That(assignmentSource, Does.Contain("earlyBorrowConflictEvidence"));
        Assert.That(assignmentSource.IndexOf("earlyBorrowConflictEvidence", StringComparison.Ordinal),
            Is.LessThan(assignmentSource.IndexOf("IsAssignmentTargetPure(", StringComparison.Ordinal)));
        Assert.That(invocationSource, Does.Contain("TryCreateMutableBorrowConflictEvidence("));
        Assert.That(invocationSource, Does.Contain("context.SemanticModel"));
        Assert.That(invocationSource, Does.Contain("context.CancellationToken"));
        Assert.That(engineSource, Does.Contain("\"analyzer.borrow.mutable-conflict\""));
    }

    [Test]
    public void ReturnRule_ConsumesSymbolicOwnershipFactsForOwnedArrayEscape()
    {
        var repositoryRoot = FindRepositoryRoot();
        var engineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));
        var returnSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "ReturnStatementPurityRule.cs"));

        Assert.That(engineSource, Does.Contain("HasSymbolicOwnedFactForSymbol("));
        Assert.That(engineSource, Does.Contain("SymbolicOwnershipAtom { Escaped: false }"));
        Assert.That(engineSource, Does.Contain("SymbolicResourceLifetimeState.Owned"));
        Assert.That(engineSource, Does.Contain("EnumerateSymbolicAliasTerms("));
        Assert.That(engineSource, Does.Contain("SymbolicAliasAtom { MayAlias: true }"));
        Assert.That(returnSource, Does.Contain("HasSymbolicOwnedFactForSymbol(trackedLocal, currentState)"));
    }

    [Test]
    public void DelegateRule_ConsumesSymbolicOwnershipFactsForOwnedArrayCapture()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "DelegateCreationPurityRule.cs"));

        Assert.That(source, Does.Contain("TryFindCapturedOwnedLocalArray("));
        Assert.That(source, Does.Contain("HasSymbolicOwnedFactForSymbol(localReference.Local, currentState)"));
        Assert.That(source, Does.Contain("currentState.IsOwnedLocalArraySymbol(localReference.Local)"));
    }

    [Test]
    public void DelegateRule_ConsumesSymbolicOwnershipFactsForFreshMutableObjectCapture()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "DelegateCreationPurityRule.cs"));

        Assert.That(source, Does.Contain("TryFindCapturedFreshMutableObject("));
        Assert.That(source, Does.Contain("TryFindCapturedFreshMutableObjectBySyntax("));
        Assert.That(source,
            Does.Contain("PurityAnalysisEngine.TryResolveTrackedSymbol(unwrappedOperation,"));
        Assert.That(source, Does.Contain("currentState) is ILocalSymbol resolvedLocal"));
        Assert.That(source, Does.Contain("RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(resolvedLocal.Type)"));
        Assert.That(source, Does.Contain("HasSymbolicOwnedFactForSymbol(resolvedLocal, currentState)"));
        Assert.That(source, Does.Contain("HasStableFreshMutableObjectInitializer(localReferenceFallback.Local"));
    }

    [Test]
    public void ReturnRule_DelegatesReturnedClosureArrayCaptureToDelegateRule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "ReturnStatementPurityRule.cs"));

        Assert.That(source, Does.Contain("TryFindReturnedDelegateOwnedLocalArrayCapture("));
        Assert.That(source, Does.Contain("DelegateCreationPurityRule.TryFindCapturedOwnedLocalArray("));
        Assert.That(source, Does.Contain("DelegateCreationPurityRule.TryFindLocalFunctionCapturedOwnedLocalArray("));
        Assert.That(source, Does.Contain("\"escaping_closure_owned_array_capture\""));
    }

    [Test]
    public void ReturnRule_DelegatesReturnedClosureFreshObjectCaptureToDelegateRule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "ReturnStatementPurityRule.cs"));

        Assert.That(source, Does.Contain("TryFindReturnedDelegateFreshMutableObjectCapture("));
        Assert.That(source, Does.Contain("DelegateCreationPurityRule.TryFindCapturedFreshMutableObject("));
        Assert.That(source, Does.Contain("DelegateCreationPurityRule.TryFindLocalFunctionCapturedFreshMutableObject("));
        Assert.That(source, Does.Contain("\"escaping_closure_fresh_mutable_object_capture\""));
    }

    [Test]
    public void ReturnRule_ConsumesSymbolicEscapeEvidenceForMutableReturns()
    {
        var repositoryRoot = FindRepositoryRoot();
        var engineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));
        var returnSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "ReturnStatementPurityRule.cs"));

        Assert.That(engineSource, Does.Contain("TryCreateReturnEscapeEvidence("));
        Assert.That(engineSource, Does.Contain("SymbolicEscapeAtom { Kind: SymbolicEscapeKind.Return }"));
        Assert.That(returnSource, Does.Contain("TryCreateReturnEscapeEvidence("));
        Assert.That(returnSource, Does.Contain("out var escapeEvidence"));
    }

    [Test]
    public void AnalyzerByRefReturns_UseSymbolicEscapeEvidence()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("CreateByRefReturnEscapeEvidence("));
        Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateEscape("));
        Assert.That(source, Does.Contain("\"analyzer.escape.return.byref\""));
        Assert.That(source, Does.Contain("\"evidence.escape.return.byref\""));
    }

    [Test]
    public void AssignmentRule_ConsumesSymbolicMutationEvidenceForCallerVisibleWrites()
    {
        var repositoryRoot = FindRepositoryRoot();
        var engineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));
        var assignmentSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "AssignmentPurityRule.cs"));

        Assert.That(engineSource, Does.Contain("TryCreateCallerVisibleMutationEvidence("));
        Assert.That(engineSource, Does.Contain("SymbolicMutationAtom { CallerVisible: true }"));
        Assert.That(assignmentSource, Does.Contain("TryCreateCallerVisibleMutationEvidence("));
        Assert.That(assignmentSource, Does.Contain("out var mutationEvidence"));
    }

    [Test]
    public void MemberAccessRules_UseSymbolicDisposalFactsForUseAfterDispose()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "MethodInvocationPurityRule.cs"));
        var propertySource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "PropertyReferencePurityRule.cs"));
        var fieldSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "FieldReferencePurityRule.cs"));
        var engineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("TryCheckDoubleDispose("));
        Assert.That(source, Does.Contain("TryCheckUseAfterDispose("));
        Assert.That(source, Does.Contain("TryCreateUseAfterDisposeEvidence("));
        Assert.That(propertySource, Does.Contain("TryCreateUseAfterDisposeEvidence("));
        Assert.That(fieldSource, Does.Contain("TryCreateUseAfterDisposeEvidence("));
        Assert.That(engineSource, Does.Contain("HasDisposedResourceFact(currentState, resourceSymbol)"));
        Assert.That(engineSource, Does.Contain("HasDisposedResourceFactForTerm("));
        Assert.That(engineSource, Does.Contain("EnumerateSymbolicAliasTerms(resourceTerm, currentState)"));
        Assert.That(engineSource, Does.Contain("TryCreateUseAfterDisposeEvidence("));
        Assert.That(engineSource, Does.Contain("TryCreateDoubleDisposeEvidence("));
        Assert.That(engineSource, Does.Contain("WasResourceDisposedByEarlierUsingStatement("));
        Assert.That(engineSource, Does.Contain("WasResourceDisposedByEarlierRelatedLocal("));
        Assert.That(engineSource, Does.Contain("GetRelatedLocalAliases("));
        Assert.That(engineSource, Does.Contain("IsStaleRelatedLocalDisposal("));
        Assert.That(engineSource, Does.Contain("AddFinallyResourceDisposeFacts("));
        Assert.That(engineSource, Does.Contain("\"analyzer.resource.finally.dispose\""));
        Assert.That(engineSource, Does.Contain("FinallyBlockReleasesResource("));
        Assert.That(engineSource, Does.Contain("AnalyzeSwitchResourceReleaseStatement("));
        Assert.That(engineSource, Does.Contain("DefaultSwitchLabelSyntax"));
        Assert.That(engineSource, Does.Contain("fallthroughStates.Add(initiallyReleased)"));
        Assert.That(engineSource,
            Does.Contain("statement is WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax"));
        Assert.That(engineSource, Does.Contain("or ForEachVariableStatementSyntax"));
        Assert.That(engineSource, Does.Contain("statement is DoStatementSyntax doStatement"));
        Assert.That(engineSource, Does.Contain("HasDisposedResourceFactBefore("));
        Assert.That(engineSource,
            Does.Contain("observationSyntax != null && !IsPriorDisposalFactOnCompatiblePath(fact, observationSyntax)"));
        Assert.That(engineSource, Does.Contain("IsPriorDisposalFactOnCompatiblePath("));
        Assert.That(engineSource, Does.Contain("IsPriorDisposalSpanOnCompatiblePath("));
        Assert.That(engineSource, Does.Contain("FirstAncestorOrSelf<SwitchSectionSyntax>()"));
        Assert.That(engineSource, Does.Contain("observationSection.Span.Contains(sourceSpanStart)"));
        Assert.That(engineSource, Does.Contain("LocalDeclarationStatementSyntax"));
        Assert.That(engineSource, Does.Contain("UsingKeyword.IsKind(SyntaxKind.UsingKeyword)"));
        Assert.That(engineSource, Does.Contain("\"resource_double_dispose\""));
        Assert.That(engineSource, Does.Contain("\"resource_use_after_dispose\""));
        Assert.That(engineSource, Does.Contain("\"symbolic_resource_lifetime\""));
    }

    [Test]
    public void PostCfgChecks_CarryMergedSymbolicPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.cs"));

        Assert.That(source, Does.Contain("out mergedPathStateFromCfg"));
        Assert.That(source, Does.Contain("pathState: mergedPathStateFromCfg"));
        Assert.That(source, Does.Contain("mergedPathStateFromBlocks = MergePathStatesAcrossAll("));
        Assert.That(source, Does.Contain("ShouldAnalyzeStateSensitiveBranchValue(block.BranchValue.Syntax)"));
        Assert.That(source, Does.Contain("IsReturnExpressionBranchValue("));
    }

    [Test]
    public void OwnedFreshMutableObjectClassifier_IsDedicatedOwnershipHelper()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "Rules",
            "OwnedFreshMutableObjectClassifier.cs"));

        Assert.That(source, Does.Contain("internal static class OwnedFreshMutableObjectClassifier"));
        Assert.That(source, Does.Contain("internal static bool IsOwnedFreshMutableObjectReference("));
        Assert.That(source, Does.Contain("internal static bool IsOwnedFreshMutableReadonlyFieldReference("));
        Assert.That(source, Does.Contain("RuleAnalysisHelper.IsFreshMutableEscapingReferenceType("));
        Assert.That(source, Does.Not.Contain("nameof(FieldReferencePurityRule)"));
        Assert.That(source, Does.Not.Contain("nameof(AssignmentPurityRule)"));
    }

    [Test]
    public void RuntimeHazardTriggers_RemainTypedUntilProofOrPresentation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var candidateSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.cs"));
        var triggerSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var helperIndex = triggerSource.IndexOf("private static bool TryCreateIrExceptionPreconditionTrigger",
            StringComparison.Ordinal);
        var helperEndIndex = triggerSource.IndexOf("private static bool TryCreateReferenceNullCondition",
            StringComparison.Ordinal);
        var helperSource = triggerSource.Substring(helperIndex, helperEndIndex - helperIndex);
        var directThrowIndex =
            triggerSource.IndexOf("private static bool TryCreateDirectThrowTrigger", StringComparison.Ordinal);
        var directThrowEndIndex =
            triggerSource.IndexOf("private static bool TryCreateDivideByZeroTrigger", StringComparison.Ordinal);
        var directThrowSource = triggerSource.Substring(directThrowIndex, directThrowEndIndex - directThrowIndex);

        Assert.That(candidateSource, Does.Contain("internal static bool TryCreate(SymbolicFact precondition"));
        Assert.That(candidateSource, Does.Contain("TriggerPrecondition = trigger.Precondition"));
        Assert.That(candidateSource, Does.Not.Contain("TriggerCondition ="));
        Assert.That(candidateSource, Does.Not.Contain("internal SmtFormula Condition"));
        Assert.That(candidateSource, Does.Not.Contain("SymbolicIrFormulaEncoder.TryEncode("));
        Assert.That(helperSource, Does.Contain("RuntimeHazardTrigger.TryCreate(precondition, out trigger)"));
        Assert.That(helperSource, Does.Not.Contain("SymbolicIrFormulaEncoder.TryEncode(precondition"));
        Assert.That(directThrowSource, Does.Contain("RuntimeHazardTrigger.TryCreate(precondition, out trigger)"));
        Assert.That(directThrowSource, Does.Not.Contain("SymbolicIrFormulaEncoder.TryEncode(precondition"));
    }

    [Test]
    public void ExecutionVisibility_UsesSymbolicReachabilityForConditionProofs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "ExecutionVisibility.cs"));

        Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
        Assert.That(source, Does.Contain("SymbolicReachabilityService.IsForInitialEntryConditionAlwaysFalse"));
        Assert.That(source, Does.Contain("SymbolicReachabilityService.CollectPathStateAt("));
        Assert.That(source, Does.Contain("SymbolicReachabilityService.ClassifyStateConditionTruth("));
        Assert.That(source, Does.Contain("SymbolicReachabilityService.ClassifyStateFeasibility("));
        Assert.That(source, Does.Contain("new(512)"));
        Assert.That(source, Does.Not.Contain("CollectPathConditionsAt("));
        Assert.That(source, Does.Not.Contain("IsFormulaAlwaysFalse("));
        Assert.That(source, Does.Not.Contain("IsFormulaAlwaysTrue("));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryClassifyFormulaConditionTruthWithIr"));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryClassifyFormulaPathFeasibilityWithIr"));
        Assert.That(source, Does.Not.Contain("WithIrFirst"));

        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        Assert.That(lowererSource, Does.Contain("NullableFlowFacts.TryEvaluateNullTest("));
    }

    [Test]
    public void ForInitialEntryReachability_UsesOnlySymbolicState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicReachabilityService.cs"));
        var methodStart = source.IndexOf("internal static bool IsForInitialEntryConditionAlwaysFalse",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf("internal static SymbolicCacheInfo GetStructuralPathCacheInfo",
            StringComparison.Ordinal);
        var methodSource = source.Substring(methodStart, methodEnd - methodStart);
        var stateIndex = methodSource.IndexOf("SymbolicProgramPointFacts.CollectForInitialEntryState",
            StringComparison.Ordinal);
        var proofIndex =
            methodSource.IndexOf("ClassifyStateConditionTruth(initialEntryState", StringComparison.Ordinal);
        Assert.That(stateIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(proofIndex, Is.GreaterThan(stateIndex));
        Assert.That(methodSource, Does.Not.Contain("TryTranslateConditionFormula"));
        Assert.That(methodSource, Does.Not.Contain("CollectPathConditionsAt"));
        Assert.That(methodSource, Does.Not.Contain("IsFormulaAlwaysFalse"));
    }

    [Test]
    public void SymbolicPublicSurface_ExposesOnlyQueryAndSolverServices()
    {
        Assert.That(typeof(SymbolicQueryService).IsPublic, Is.True);
        Assert.That(typeof(SmtAnalysisService).IsPublic, Is.True);
        Assert.That(typeof(SmtAnalysisOptions).IsPublic, Is.True);
    }

    [Test]
    public void SymbolicIr_KeepsSmtConstructionBehindEncoderBoundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var irDirectory = Path.Combine(repositoryRoot, "SharpProof.Symbolic", "Ir");
        var offenders = Directory.GetFiles(irDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith("SymbolicIrFormulaEncoder.cs", StringComparison.Ordinal))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path)
            })
            .Where(static file =>
                file.Source.Contains("new Smt", StringComparison.Ordinal) ||
                file.Source.Contains(": SmtFormula", StringComparison.Ordinal))
            .Select(static file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void SearchLib_RemainsSolverBackendWithoutRoslynOrSymbolicSemantics()
    {
        var repositoryRoot = FindRepositoryRoot();
        var searchLibDirectory = Path.Combine(repositoryRoot, "SearchLib");
        var offenders = Directory.GetFiles(searchLibDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path)
            })
            .Where(static file =>
                file.Source.Contains("Microsoft.CodeAnalysis", StringComparison.Ordinal) ||
                file.Source.Contains("SharpProof.Symbolic", StringComparison.Ordinal) ||
                file.Source.Contains("SharpProof.Analyzer", StringComparison.Ordinal))
            .Select(static file => file.Path)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void SearchLib_RegexTranslationTimeoutsFallbackConservatively()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solverSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SearchLib",
            "SmtSolver.cs"));
        var encoderSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SearchLib",
            "Z3FormulaEncoder.cs"));
        var smtAnalysisSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Smt",
            "SmtAnalysisService.cs"));

        Assert.That(solverSource, Does.Contain("ex is RegexMatchTimeoutException"));
        Assert.That(encoderSource, Does.Contain("CreateRegexCharacterRangesOrEmpty"));
        Assert.That(encoderSource, Does.Contain("catch (RegexMatchTimeoutException)"));
        Assert.That(encoderSource, Does.Contain("return Array.Empty<CharacterRange>();"));
        Assert.That(smtAnalysisSource, Does.Contain("catch (RegexMatchTimeoutException)"));
        Assert.That(smtAnalysisSource, Does.Contain("return Unknown(\"smt_timeout\");"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsStringAndRegexLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var stringSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Strings.cs"));
        var knownApiSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.KnownApis.cs"));

        Assert.That(coreSource, Does.Contain("internal static partial class SymbolicIrLowerer"));
        Assert.That(knownApiSource, Does.Contain("TryLowerStringStaticValueMember(memberSymbol, out term)"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerRegexIsMatchInvocation"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringPredicateInvocation"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringEqualityCondition"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateStringEqualityCondition"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringStaticValueMember"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateStringContentReferenceTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool IsSystemStringType"));
        Assert.That(stringSource, Does.Contain("private static bool TryLowerRegexIsMatchInvocation"));
        Assert.That(stringSource, Does.Contain("private static bool TryLowerStringPredicateInvocation"));
        Assert.That(stringSource, Does.Contain("private static bool TryLowerStringEqualityCondition"));
        Assert.That(stringSource, Does.Contain("private static bool TryCreateStringEqualityCondition"));
        Assert.That(stringSource, Does.Contain("private static bool TryLowerStringStaticValueMember"));
        Assert.That(stringSource, Does.Contain("private static bool TryCreateStringContentReferenceTerm"));
        Assert.That(stringSource, Does.Contain("private static bool IsSystemStringType"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsObjectLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var objectSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Objects.cs"));
        var knownApiSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.KnownApis.cs"));

        Assert.That(knownApiSource, Does.Contain("TryLowerObjectReferenceEqualsInvocation"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerObjectReferenceEqualsInvocation"));
        Assert.That(objectSource, Does.Contain("private static bool TryLowerObjectReferenceEqualsInvocation"));
        Assert.That(objectSource, Does.Contain("ir.known-api.object.reference-equals"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsPatternLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var patternSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Patterns.cs"));

        Assert.That(coreSource, Does.Contain("TryLowerBinaryPatternCondition(isPatternExpression"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerBinaryPatternCondition"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerTypeTestCondition"));
        Assert.That(coreSource, Does.Not.Contain("private static PatternSyntax UnwrapPattern"));
        Assert.That(patternSource, Does.Contain("private static bool TryLowerBinaryPatternCondition"));
        Assert.That(patternSource, Does.Contain("private static bool TryLowerTypeTestCondition"));
        Assert.That(patternSource, Does.Contain("private static PatternSyntax UnwrapPattern"));
        Assert.That(patternSource, Does.Contain("ir.pattern.type.test"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsTupleLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var tupleSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Tuples.cs"));
        var memberSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Members.cs"));

        Assert.That(coreSource, Does.Contain("TryLowerTupleEqualityCondition(binaryExpression"));
        Assert.That(memberSource, Does.Contain("TryLowerTupleElementMemberTerm(memberAccess"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerTupleEqualityCondition"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerTupleElementMemberTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerTupleElementTerms"));
        Assert.That(tupleSource, Does.Contain("private static bool TryLowerTupleEqualityCondition"));
        Assert.That(tupleSource, Does.Contain("private static bool TryLowerTupleElementMemberTerm"));
        Assert.That(tupleSource, Does.Contain("private static bool TryLowerTupleElementTerms"));
        Assert.That(tupleSource, Does.Contain("ir.tuple.equality.element"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsNullableLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var nullableSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Nullable.cs"));
        var memberSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Members.cs"));

        Assert.That(memberSource, Does.Contain("TryLowerNullableHasValueTerm(memberAccess.Expression"));
        Assert.That(memberSource, Does.Contain("TryLowerNullableValueTerm(memberAccess.Expression"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerNullableHasValueTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerNullableValueTerm"));
        Assert.That(nullableSource, Does.Contain("private static bool TryLowerNullableHasValueTerm"));
        Assert.That(nullableSource, Does.Contain("private static bool TryLowerNullableValueTerm"));
        Assert.That(nullableSource, Does.Contain("private static bool TryLowerNullableGetValueOrDefaultInvocation"));
        Assert.That(nullableSource, Does.Contain("TryLowerArrayTotalLengthTerm(conditionalAccess.Expression"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsIndexingLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var indexingSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));
        var knownApisSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.KnownApis.cs"));

        Assert.That(coreSource, Does.Contain("TryLowerElementAccessTerm(elementAccess"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerElementAccessTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetBuiltInElementAccessElementType"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerArrayDimensionLengthTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerArrayGetLengthInvocation"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerArrayBoundInvocation"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerArrayTotalLengthTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateBuiltInLengthReferenceTerm"));
        Assert.That(indexingSource, Does.Contain("private static bool TryLowerElementAccessTerm"));
        Assert.That(indexingSource, Does.Contain("private static bool TryGetBuiltInElementAccessElementType"));
        Assert.That(indexingSource, Does.Contain("private static bool TryLowerArrayGetLengthInvocation"));
        Assert.That(indexingSource, Does.Contain("private static bool TryLowerArrayBoundInvocation"));
        Assert.That(indexingSource, Does.Contain("private static bool TryLowerArrayDimensionLengthTerm"));
        Assert.That(indexingSource, Does.Contain("private static bool TryLowerArrayTotalLengthTerm"));
        Assert.That(indexingSource, Does.Contain("private static bool TryCreateBuiltInLengthReferenceTerm"));
        Assert.That(indexingSource,
            Does.Contain("TryCreateArrayTotalLengthReferenceTerm(reference, multiDimensionalArray, out term)"));
        Assert.That(indexingSource, Does.Contain("new SymbolicArrayDimensionLengthTerm"));
        Assert.That(knownApisSource, Does.Contain("nameof(Array.GetLength)"));
        Assert.That(knownApisSource, Does.Contain("nameof(Array.GetLongLength)"));
        Assert.That(knownApisSource, Does.Contain("nameof(Array.GetLowerBound)"));
        Assert.That(knownApisSource, Does.Contain("nameof(Array.GetUpperBound)"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsConversionLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var conversionSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Conversions.cs"));

        Assert.That(coreSource, Does.Contain("TryLowerSupportedConversionTerm(expression"));
        Assert.That(coreSource, Does.Contain("TryLowerReferenceAsTerm(asExpression"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerIdentityPreservingAsTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool IsIdentityPreservingReferenceConversion"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerSupportedConversionTerm"));
        Assert.That(conversionSource, Does.Contain("private static bool TryLowerIdentityPreservingAsTerm"));
        Assert.That(conversionSource, Does.Contain("private static bool TryLowerReferenceAsTerm"));
        Assert.That(conversionSource, Does.Contain("private static bool IsIdentityPreservingReferenceConversion"));
        Assert.That(conversionSource, Does.Contain("private static bool TryLowerSupportedConversionTerm"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsMemberLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var memberSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Members.cs"));
        var indexingSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));

        Assert.That(coreSource, Does.Contain("TryLowerMemberTerm(memberAccess"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerMemberTerm"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetInstanceMemberValueKind"));
        Assert.That(coreSource, Does.Not.Contain("private static bool IsBuiltInSpanOrMemoryType"));
        Assert.That(memberSource, Does.Contain("private static bool TryLowerMemberTerm"));
        Assert.That(memberSource, Does.Contain("private static bool TryGetInstanceMemberValueKind"));
        Assert.That(memberSource, Does.Contain("private static bool IsBuiltInSpanOrMemoryType"));
        Assert.That(indexingSource, Does.Contain("new SymbolicLengthTerm"));
        Assert.That(memberSource, Does.Contain("new SymbolicCountTerm"));
        Assert.That(memberSource, Does.Contain("new SymbolicIntegerConstantTerm(arrayType.Rank)"));
        Assert.That(indexingSource, Does.Contain("TryLowerArrayTotalLengthTerm("));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsNumericLoweringsInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var numericSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Numerics.cs"));
        var knownApiSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.KnownApis.cs"));

        Assert.That(knownApiSource, Does.Contain("TryLowerBigIntegerStaticValueMember(memberSymbol, out term)"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerBigIntegerStaticValueMember"));
        Assert.That(coreSource, Does.Not.Contain("private static bool IsBigIntegerType"));
        Assert.That(numericSource, Does.Contain("private static bool TryLowerBigIntegerStaticValueMember"));
        Assert.That(numericSource, Does.Contain("private static bool IsBigIntegerType"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsTypeAndValueKindHelpersInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var typeSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Types.cs"));

        Assert.That(coreSource, Does.Contain("TryGetSymbolType(symbol"));
        Assert.That(coreSource, Does.Contain("TryGetValueKind(symbolType"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetSymbolType"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetValueKind"));
        Assert.That(coreSource, Does.Not.Contain("private static bool IsIntegerSmtType"));
        Assert.That(coreSource, Does.Not.Contain("private static bool IsSupportedTupleCarrierType"));
        Assert.That(typeSource, Does.Contain("private static bool TryGetSymbolType"));
        Assert.That(typeSource, Does.Contain("private static bool TryGetValueKind"));
        Assert.That(typeSource, Does.Contain("private static bool IsIntegerSmtType"));
        Assert.That(typeSource, Does.Contain("private static bool IsSupportedTupleCarrierType"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsOperatorHelpersInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var operatorSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Operators.cs"));

        Assert.That(coreSource, Does.Contain("TryGetRelationOperator(binaryExpression.Kind()"));
        Assert.That(coreSource, Does.Contain("TryGetBinaryTermOperator(binary.Kind()"));
        Assert.That(coreSource, Does.Contain("CanCompareTerms(left, right, relationOperator)"));
        Assert.That(coreSource, Does.Not.Contain("private static bool CanCompareTerms"));
        Assert.That(coreSource, Does.Not.Contain("private static bool IsEqualityExpression"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetRelationOperator"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetBinaryTermOperator"));
        Assert.That(operatorSource, Does.Contain("private static bool CanCompareTerms"));
        Assert.That(operatorSource, Does.Contain("private static bool IsEqualityExpression"));
        Assert.That(operatorSource, Does.Contain("private static bool TryGetRelationOperator"));
        Assert.That(operatorSource, Does.Contain("private static bool TryGetBinaryTermOperator"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsKnownApiDispatchInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var knownApiSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.KnownApis.cs"));
        var memberSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Members.cs"));

        Assert.That(coreSource, Does.Contain("TryLowerKnownApiInvocation(knownInvocation, context, out condition)"));
        Assert.That(coreSource, Does.Contain("TryLowerKnownApiInvocationTerm(invocation, context, out term)"));
        Assert.That(memberSource, Does.Contain("TryLowerKnownStaticValueMember(memberAccess, context, out term)"));
        Assert.That(coreSource, Does.Not.Contain("KnownApiLowerings ="));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerKnownApiInvocation("));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerKnownApiInvocationTerm("));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerKnownStaticValueMember("));
        Assert.That(knownApiSource, Does.Contain("KnownApiLowerings ="));
        Assert.That(knownApiSource, Does.Contain("KnownApiTermLowerings"));
        Assert.That(knownApiSource, Does.Contain("\"System.Math\""));
        Assert.That(knownApiSource, Does.Contain("TryLowerIntegralMathMinMaxInvocation"));
        Assert.That(knownApiSource, Does.Contain("TryLowerIntegralMathAbsInvocation"));
        Assert.That(knownApiSource, Does.Contain("TryLowerIntegralMathClampInvocation"));
        Assert.That(knownApiSource, Does.Contain("private static bool TryLowerKnownApiInvocation("));
        Assert.That(knownApiSource, Does.Contain("private static bool TryLowerKnownApiInvocationTerm("));
        Assert.That(knownApiSource, Does.Contain("private static bool TryLowerKnownStaticValueMember("));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsConditionFactoriesInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var conditionSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Conditions.cs"));

        Assert.That(coreSource, Does.Contain("CreateFactCondition("));
        Assert.That(coreSource, Does.Contain("CreateRelationCondition("));
        Assert.That(coreSource, Does.Not.Contain("private static SymbolicCondition CreateFactCondition"));
        Assert.That(coreSource, Does.Not.Contain("private static SymbolicCondition CreateRelationCondition"));
        Assert.That(coreSource, Does.Not.Contain("private static SymbolicCondition CreateReferenceIsNullCondition"));
        Assert.That(conditionSource, Does.Contain("private static SymbolicCondition CreateFactCondition"));
        Assert.That(conditionSource, Does.Contain("private static SymbolicCondition CreateRelationCondition"));
        Assert.That(conditionSource, Does.Contain("private static SymbolicCondition CreateReferenceIsNullCondition"));
        Assert.That(conditionSource, Does.Contain("SymbolicFact.Exact(atom, node, provenance)"));
    }

    [Test]
    public void SymbolicIrLowerer_KeepsSharedUtilitiesInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.cs"));
        var utilitySource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Utilities.cs"));

        Assert.That(coreSource, Does.Contain("UnwrapExpression(expression)"));
        Assert.That(coreSource, Does.Contain("TryGetIntegralConstant(constantValue.Value"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetStableVariableSymbol"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryGetIntegralConstant"));
        Assert.That(coreSource, Does.Not.Contain("private static ExpressionSyntax UnwrapExpression"));
        Assert.That(utilitySource, Does.Contain("private static bool TryGetStableVariableSymbol"));
        Assert.That(utilitySource, Does.Contain("private static bool TryGetIntegralConstant"));
        Assert.That(utilitySource, Does.Contain("private static ExpressionSyntax UnwrapExpression"));
    }

    [Test]
    public void RuntimeHazardDivideByZero_UsesTypedIrProjection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateIrExceptionPreconditionTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DivideByZero"));
        Assert.That(source, Does.Contain("TryCreateNumericZeroCondition("));
        Assert.That(source, Does.Contain("ir.runtime-hazard.divide-by-zero.unsupported"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.divide-by-zero.formula-fallback"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger"));
        Assert.That(source, Does.Not.Contain("trigger = new RuntimeHazardTrigger(formula);"));
        Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(binaryExpression.Right"));
        Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(assignment.Right"));
    }

    [Test]
    public void RuntimeHazardDivideByZero_UsesIrZeroConditionBeforeTypedProjection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var pipelineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicSemanticPipeline.cs"));
        var helperIndex = source.IndexOf("TryCreateNumericZeroCondition(", StringComparison.Ordinal);

        var unsupportedIndex = source.IndexOf("ir.runtime-hazard.divide-by-zero.unsupported", StringComparison.Ordinal);
        var formulaFallbackIndex = source.IndexOf("\"ir.runtime-hazard.divide-by-zero.formula-fallback\"",
            StringComparison.Ordinal);

        Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(unsupportedIndex, Is.GreaterThan(helperIndex));
        Assert.That(formulaFallbackIndex, Is.EqualTo(-1));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerNumericZeroCondition("));
        Assert.That(source, Does.Not.Contain("TryCreateDecimalZeroComparableTerm("));
        Assert.That(pipelineSource, Does.Contain("SymbolicIrLowerer.CreateIntegerZeroCondition("));
        Assert.That(pipelineSource, Does.Contain("new SymbolicConstantCondition(true)"));
        Assert.That(pipelineSource, Does.Contain("new SymbolicConstantCondition(false)"));
        Assert.That(pipelineSource, Does.Contain("SpecialType.System_Decimal"));
        Assert.That(pipelineSource, Does.Contain("context.GetVariableName(symbol)"));
    }

    [Test]
    public void RuntimeHazardSimpleIndexing_UsesTypedIrBoundsPrecondition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateIrElementAccessOutOfRangeTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentOutOfRange"));
        Assert.That(source,
            Does.Contain("SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition("));
        Assert.That(source, Does.Contain("new SymbolicNotCondition(inRangeCondition)"));
        Assert.That(source, Does.Not.Contain("new SymbolicBoundsAtom"));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange("));
        Assert.That(source, Does.Contain("ir.runtime-hazard.index.out-of-range.unsupported"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.index.out-of-range.formula-fallback"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger"));
        Assert.That(source,
            Does.Not.Contain(
                "trigger = new RuntimeHazardTrigger(new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula));"));
    }

    [Test]
    public void RuntimeHazardIndexFallback_UsesTypedProjectionWithoutFormulaFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var unsupportedIndex =
            source.IndexOf("ir.runtime-hazard.index.out-of-range.unsupported", StringComparison.Ordinal);
        var fallbackIndex = source.IndexOf("\"ir.runtime-hazard.index.out-of-range.formula-fallback\"",
            StringComparison.Ordinal);

        Assert.That(unsupportedIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(fallbackIndex, Is.EqualTo(-1));
    }

    [Test]
    public void AnalyzerExceptionSites_UseSharedTypedElementAccessRangeHelper()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "ExceptionFlowAnalyzer.ExceptionSites.RangeAccess.cs"));
        var pipelineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicSemanticPipeline.cs"));
        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));

        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition("));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange("));
        Assert.That(pipelineSource, Does.Contain("LowerBuiltInElementAccessInRangeCondition("));
        Assert.That(pipelineSource,
            Does.Contain("SymbolicIrLowerer.LowerBuiltInElementAccessInRangeCondition("));
        Assert.That(pipelineSource,
            Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange("));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerSubsequenceInRangeCondition("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.CreateSubsequenceInRangeFormula("));
        Assert.That(pipelineSource, Does.Contain("SymbolicIrLowerer.LowerSubsequenceInRangeCondition("));
        Assert.That(lowererSource, Does.Contain("private static bool TryCreateSubsequenceInRangeCondition("));
    }

    [Test]
    public void ElementAccessRangeHelper_UsesIrMultidimensionalBoundsBeforeLegacyFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pipelineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicSemanticPipeline.cs"));
        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));
        Assert.That(pipelineSource, Does.Contain("LowerBuiltInElementAccessInRangeCondition("));
        Assert.That(pipelineSource, Does.Contain("SymbolicIrLowerer.LowerBuiltInElementAccessInRangeCondition("));
        Assert.That(pipelineSource, Does.Contain("SymbolicIrLowerer.LowerArrayElementBoundsCondition("));
        Assert.That(pipelineSource, Does.Not.Contain("CSharpSmtFormulaTranslator"));
        Assert.That(lowererSource, Does.Contain("private static bool TryCreateBuiltInElementAccessInRangeCondition("));
        Assert.That(lowererSource, Does.Contain("TryResolveBuiltInRangeLengthShape("));
        Assert.That(lowererSource, Does.Contain("TryResolveBuiltInIndexLengthShape("));
        Assert.That(lowererSource, Does.Contain("ApplyWellFormedPrecondition("));
        Assert.That(lowererSource, Does.Contain("RequiresNonNegativeValue"));
        Assert.That(lowererSource, Does.Contain("private static bool TryCreateArrayElementBoundsCondition("));
        Assert.That(lowererSource,
            Does.Contain("TryLowerArrayDimensionLengthTerm(arrayExpression, dimension, context, out var length)"));
        Assert.That(lowererSource, Does.Contain("new SymbolicBoundsAtom("));
    }

    [Test]
    public void ElementAccessLengthTerms_AreLoweredBySharedIrLowerer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pipelineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicSemanticPipeline.cs"));
        var irTriggerSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));

        Assert.That(lowererSource, Does.Contain("private static bool TryLowerBuiltInLengthTerm("));
        Assert.That(lowererSource, Does.Contain("TryLowerDirectRangeAccessResultLengthTerm("));
        Assert.That(lowererSource, Does.Contain("TryLowerBuiltInViewResultLengthTerm("));
        Assert.That(lowererSource, Does.Contain("TryLowerBuiltInSliceInvocationResultLengthTerm("));
        Assert.That(lowererSource, Does.Contain("TryLowerMemoryExtensionsViewResultLengthTerm("));
        Assert.That(lowererSource, Does.Contain("TryResolveBuiltInRangeLengthShape("));
        Assert.That(lowererSource, Does.Contain("TryResolveAssignedRangeLengthShape("));
        Assert.That(lowererSource, Does.Contain("TryResolveBuiltInIndexLengthShape("));
        Assert.That(lowererSource, Does.Contain("TryLowerStringInvocationResultLengthTerm("));
        Assert.That(lowererSource, Does.Contain("private static bool TryCreateBuiltInLengthReferenceTerm("));
        Assert.That(lowererSource, Does.Contain("type is not IArrayTypeSymbol &&"));
        Assert.That(lowererSource, Does.Contain("HasCountBackedIntIndexer(type)"));
        Assert.That(lowererSource, Does.Contain("term = new SymbolicCountTerm(reference);"));
        Assert.That(lowererSource,
            Does.Contain("TryCreateStringContentReferenceTerm(reference, out var stringContent)"));
        Assert.That(lowererSource, Does.Contain("CreateLengthTerm(reference, out term)"));
        Assert.That(pipelineSource, Does.Contain("LowerBuiltInLengthTerm("));
        Assert.That(pipelineSource, Does.Contain("ProjectBuiltInLengthTerm("));
        Assert.That(irTriggerSource,
            Does.Contain("SymbolicSemanticPipeline.LowerBuiltInLengthTerm(elementAccess.Expression, context)"));
        Assert.That(irTriggerSource,
            Does.Contain("SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition("));
        Assert.That(irTriggerSource, Does.Not.Contain("new SymbolicCountTerm("));
    }

    [Test]
    public void RuntimeHazardSlicing_UsesTypedProjectionWhenFormulaLowers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));

        Assert.That(source, Does.Contain("TryCreateSlicingArgumentOutOfRangeCandidate"));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerSubsequenceInRangeCondition("));
        Assert.That(source, Does.Not.Contain("SymbolicIrLowerer.TryCreateSubsequenceInRangeCondition("));
        Assert.That(source, Does.Contain("TryCreateIrExceptionPreconditionTrigger("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.CreateSubsequenceInRangeFormula"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentOutOfRange"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.slicing.argument-out-of-range.unsupported"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.slicing.argument-out-of-range.fallback"));
        Assert.That(lowererSource, Does.Contain("provenance + \".count-within-remaining-length\""));
        Assert.That(lowererSource, Does.Contain("provenance + \".addition-does-not-overflow\""));
    }

    [Test]
    public void RuntimeHazardArrayGetValue_UsesTypedBoundsProjection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.cs"));
        var irTriggerSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var exceptionSitesSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "ExceptionFlowAnalyzer.ExceptionSites.cs"));
        var exceptionRangeSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "ExceptionFlowAnalyzer.ExceptionSites.RangeAccess.cs"));
        var exceptionQuerySource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "ExceptionFlowQuery.cs"));
        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));

        Assert.That(source, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.multidimensional-index-out-of-range"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerArrayElementBoundsCondition("));
        Assert.That(source, Does.Not.Contain("SymbolicIrLowerer.TryCreateArrayElementBoundsCondition("));
        Assert.That(lowererSource, Does.Contain("new SymbolicBoundsAtom("));
        Assert.That(lowererSource,
            Does.Contain("TryLowerArrayDimensionLengthTerm(arrayExpression, dimension, context, out var length)"));
        Assert.That(coreSource, Does.Not.Contain("CSharpSmtFormulaTranslator."));
        Assert.That(irTriggerSource, Does.Contain("SymbolicSemanticPipeline.LowerArrayElementBoundsCondition("));
        Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.index-out-of-range.unsupported"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.array-get-value.index-out-of-range.fallback"));
        Assert.That(coreSource, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger("));
        Assert.That(irTriggerSource,
            Does.Contain("private static bool TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
        Assert.That(exceptionSitesSource, Does.Contain("GetDefiniteArrayGetValueIndexOutOfRangeNodes("));
        Assert.That(exceptionRangeSource,
            Does.Contain("SymbolicSemanticPipeline.LowerArrayElementBoundsCondition("));
        Assert.That(exceptionRangeSource,
            Does.Not.Contain("SymbolicReachabilityService.TryCreateArrayGetValueIndexesInRangeFormula("));
        Assert.That(exceptionRangeSource, Does.Contain("TryGetArrayGetValueRuntimeArrayType("));
        Assert.That(exceptionQuerySource,
            Does.Contain("ExceptionFlowAnalyzer.GetDefiniteArrayGetValueIndexOutOfRangeNodes("));
        Assert.That(exceptionQuerySource, Does.Contain("ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange"));
        Assert.That(exceptionQuerySource, Does.Contain("ExceptionSources.ArrayGetValue"));
    }

    [Test]
    public void RuntimeHazardMultidimensionalElementAccess_UsesTypedIrBoundsPrecondition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
        var irTriggerSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Indexing.cs"));

        Assert.That(source, Does.Contain("TryCreateIrMultidimensionalArrayElementAccessOutOfRangeTrigger"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.index.multidimensional-out-of-range"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerArrayElementBoundsCondition("));
        Assert.That(source, Does.Not.Contain("SymbolicIrLowerer.TryCreateArrayElementBoundsCondition("));
        Assert.That(lowererSource, Does.Contain("private static bool TryCreateArrayElementBoundsCondition("));
        Assert.That(lowererSource, Does.Contain("new SymbolicBoundsAtom("));
        Assert.That(irTriggerSource,
            Does.Contain("GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken)"));
        Assert.That(irTriggerSource, Does.Contain("Rank: > 1"));
        Assert.That(irTriggerSource,
            Does.Contain("return TryCreateIrMultidimensionalArrayElementAccessOutOfRangeTrigger("));
    }

    [Test]
    public void RuntimeHazardNegativeLengths_UseTypedIrExceptionPreconditions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateNegativeLengthTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NegativeLength"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NegativeStackAllocLength"));
        Assert.That(source, Does.Contain("SymbolicRelationOperator.LessThan"));
        Assert.That(source, Does.Contain("CreateAggregateExceptionPreconditionTrigger"));
        Assert.That(source, Does.Contain("TryGetExceptionPrecondition"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.array.negative-length.aggregate"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.stackalloc.negative-length.aggregate"));
        Assert.That(source, Does.Not.Contain("TryTranslateNegativeCondition(lengthExpression"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger("));
        Assert.That(source, Does.Not.Contain("provenance + \".formula-fallback\""));
        Assert.That(source, Does.Not.Contain("if (!TryTranslateNegativeCondition(lengthExpression"));
        Assert.That(source, Does.Not.Contain("trigger = new RuntimeHazardTrigger(formula);"));
    }

    [Test]
    public void RuntimeHazardNegativeLengthFallback_UsesTypedProjectionWithoutFormulaFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var helperIndex = source.IndexOf(
            "CreateUnsupportedExceptionPreconditionTrigger(",
            StringComparison.Ordinal);

        var unsupportedProvenanceIndex = source.IndexOf("provenance + \".unsupported\"", StringComparison.Ordinal);
        var fallbackProvenanceIndex = source.IndexOf("provenance + \".formula-fallback\"", StringComparison.Ordinal);

        Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(unsupportedProvenanceIndex, Is.GreaterThan(helperIndex));
        Assert.That(fallbackProvenanceIndex, Is.EqualTo(-1));
    }

    [Test]
    public void RuntimeHazardCheckedIntegralOutOfRange_UsesTypedIrProjections()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
        var pipelineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicSemanticPipeline.cs"));
        var lowererSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicIrLowerer.Conditions.cs"));

        Assert.That(source, Does.Contain("TryCreateCheckedIntegralOutOfRangeTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.CheckedOverflow"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.binary-overflow"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-conversion.overflow"));
        Assert.That(source, Does.Contain("TryCreateCheckedSignedDivisionOverflowTrigger"));
        Assert.That(source, Does.Contain("TryCreateCheckedEqualityOverflowTrigger"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.signed-division-overflow"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.compound-signed-division-overflow"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.unary-minus-overflow"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.increment-overflow"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.decrement-overflow"));
        Assert.That(source, Does.Contain("IsSignedDivisionOverflowOperator"));
        Assert.That(source, Does.Contain("SymbolicIrLowerer.CreateSignedDivisionOverflowCondition("));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.binary-overflow.unsupported"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.signed-division-overflow.unsupported"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.unary-minus-overflow.unsupported"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.increment-overflow.unsupported"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.decrement-overflow.unsupported"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.compound-assignment-overflow.unsupported"));
        Assert.That(source,
            Does.Contain("ir.runtime-hazard.checked-integral.compound-signed-division-overflow.unsupported"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.checked-conversion.overflow.unsupported"));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator."));
        Assert.That(source, Does.Not.Contain("CreateIntegralOutOfRangeFormula("));
        Assert.That(pipelineSource, Does.Contain("SymbolicIrLowerer.CreateIntegerInRangeCondition("));
        Assert.That(pipelineSource, Does.Contain("\"ir.integer.in-range\""));
        Assert.That(pipelineSource,
            Does.Contain("SymbolicIrLowerer.GetBinaryTermOperator(smtOperator)"));
        Assert.That(pipelineSource, Does.Contain("\"ir.integer.binary.in-range\""));
        Assert.That(pipelineSource, Does.Contain("\"ir.integer.unary.in-range\""));
        Assert.That(pipelineSource,
            Does.Contain("binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract"));
        Assert.That(pipelineSource, Does.Contain("\"ir.integer.update.in-range\""));
        Assert.That(lowererSource, Does.Contain("public static SymbolicCondition CreateIntegerInRangeCondition("));
        Assert.That(lowererSource,
            Does.Contain("public static SymbolicCondition CreateSignedDivisionOverflowCondition("));
        Assert.That(lowererSource, Does.Contain("provenance + \".lower-bound\""));
        Assert.That(lowererSource, Does.Contain("provenance + \".upper-bound\""));
        Assert.That(lowererSource, Does.Contain("provenance + \".left-min\""));
        Assert.That(lowererSource, Does.Contain("provenance + \".right-minus-one\""));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.checked-integral.binary-overflow.formula-fallback"));
        Assert.That(source,
            Does.Not.Contain("ir.runtime-hazard.checked-integral.signed-division-overflow.formula-fallback"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.checked-integral.unary-minus-overflow.formula-fallback"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.checked-integral.increment-overflow.formula-fallback"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.checked-integral.decrement-overflow.formula-fallback"));
        Assert.That(source,
            Does.Not.Contain("ir.runtime-hazard.checked-integral.compound-assignment-overflow.formula-fallback"));
        Assert.That(source,
            Does.Not.Contain("ir.runtime-hazard.checked-integral.compound-signed-division-overflow.formula-fallback"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.checked-conversion.overflow.formula-fallback"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.CheckedOverflow"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger"));
    }

    [Test]
    public void RuntimeHazardSignedDivisionOverflow_UsesTypedProjectionWithoutFormulaFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.cs"));
        var unsupportedIndex = source.IndexOf("ir.runtime-hazard.checked-integral.signed-division-overflow.unsupported",
            StringComparison.Ordinal);
        var fallbackIndex =
            source.IndexOf("\"ir.runtime-hazard.checked-integral.signed-division-overflow.formula-fallback\"",
                StringComparison.Ordinal);
        var compoundUnsupportedIndex =
            source.IndexOf("ir.runtime-hazard.checked-integral.compound-signed-division-overflow.unsupported",
                StringComparison.Ordinal);
        var compoundFallbackIndex =
            source.IndexOf("\"ir.runtime-hazard.checked-integral.compound-signed-division-overflow.formula-fallback\"",
                StringComparison.Ordinal);

        Assert.That(unsupportedIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(fallbackIndex, Is.EqualTo(-1));
        Assert.That(compoundUnsupportedIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(compoundFallbackIndex, Is.EqualTo(-1));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger("));
    }

    [Test]
    public void RuntimeHazardCheckedOverflowRanges_UseTypedProjectionWithoutFormulaFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.cs"));
        var provenances = new[]
        {
            "ir.runtime-hazard.checked-integral.binary-overflow",
            "ir.runtime-hazard.checked-integral.unary-minus-overflow",
            "ir.runtime-hazard.checked-integral.increment-overflow",
            "ir.runtime-hazard.checked-integral.decrement-overflow",
            "ir.runtime-hazard.checked-integral.compound-assignment-overflow",
            "ir.runtime-hazard.checked-conversion.overflow"
        };

        foreach (var provenance in provenances)
        {
            var unsupportedIndex = source.IndexOf(provenance + ".unsupported", StringComparison.Ordinal);
            var fallbackIndex = source.IndexOf(
                "\"" + provenance + ".formula-fallback\"",
                unsupportedIndex >= 0 ? unsupportedIndex : 0,
                StringComparison.Ordinal);

            Assert.That(unsupportedIndex, Is.GreaterThanOrEqualTo(0), provenance);
            Assert.That(fallbackIndex, Is.EqualTo(-1), provenance);
        }

        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger("));
    }

    [Test]
    public void RuntimeHazardStableNullDereferences_UseTypedIrExceptionPreconditions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateNullDereferenceTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullDereference"));
        Assert.That(source, Does.Not.Contain("IsStableIrReferenceSubject"));
        Assert.That(source, Does.Contain("TryCreateIrRelationalExceptionPreconditionTrigger"));
        Assert.That(source, Does.Not.Contain("TryTranslateNullCondition(receiver"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger("));
        Assert.That(source, Does.Not.Contain("\"ir.runtime-hazard.null-dereference.formula-fallback\""));
        Assert.That(source, Does.Contain("!TryCreateNullDereferenceTrigger(receiver"));
    }

    [Test]
    public void RuntimeHazardUnboxNull_UsesTypedIrExceptionPrecondition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateUnboxNullTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.UnboxNull"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.unbox-null"));
        Assert.That(source, Does.Not.Contain("TryTranslateNullCondition(expression"));
        Assert.That(source, Does.Not.Contain("\"ir.runtime-hazard.unbox-null.formula-fallback\""));
        Assert.That(source, Does.Contain("TryCreateUnboxNullTrigger("));
    }

    [Test]
    public void RuntimeHazardStableArgumentNull_UsesTypedIrExceptionPreconditions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateArgumentNullTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentNull"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.argument-null"));
        Assert.That(source, Does.Not.Contain("IsStableIrReferenceSubject"));
        Assert.That(source, Does.Not.Contain("TryTranslateNullCondition(expression"));
        Assert.That(source, Does.Not.Contain("\"ir.runtime-hazard.argument-null.formula-fallback\""));
        Assert.That(source, Does.Contain("!TryCreateArgumentNullTrigger(expression"));
    }

    [Test]
    public void RuntimeHazardNullLikeFallbacks_UseTypedProjectionsWithoutFormulaFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var provenances = new[]
        {
            "ir.runtime-hazard.null-dereference",
            "ir.runtime-hazard.unbox-null",
            "ir.runtime-hazard.argument-null",
            "ir.runtime-hazard.nullable-value.without-value",
            "ir.runtime-hazard.invalid-cast",
            "ir.runtime-hazard.dynamic-null-binding"
        };

        foreach (var provenance in provenances)
        {
            var unsupportedIndex = source.IndexOf(provenance + ".unsupported", StringComparison.Ordinal);
            var fallbackIndex = source.IndexOf(
                "\"" + provenance + ".formula-fallback\"",
                unsupportedIndex >= 0 ? unsupportedIndex : 0,
                StringComparison.Ordinal);

            Assert.That(unsupportedIndex, Is.GreaterThanOrEqualTo(0), provenance);
            Assert.That(fallbackIndex, Is.EqualTo(-1), provenance);
        }

        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger("));
    }

    [Test]
    public void RuntimeHazardNullableValue_UsesTypedIrExceptionPrecondition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateNullableValueWithoutValueTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullableValueWithoutValue"));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerNullableHasValueTerm"));
        Assert.That(source, Does.Not.Contain("SymbolicIrLowerer.TryLowerNullableHasValueTerm"));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateNullableHasValue("));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.nullable-value.without-value.formula-fallback"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger"));
        Assert.That(source, Does.Contain("!TryCreateNullableValueWithoutValueTrigger("));
    }

    [Test]
    public void RuntimeHazardInvalidReferenceCast_UsesTypedIrTypeTestPrecondition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.cs"));
        var irTriggerSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

        Assert.That(source, Does.Contain("TryCreateRuntimeReferenceInvalidCastTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.InvalidCast"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.invalid-cast.non-null"));
        Assert.That(source, Does.Contain("new SymbolicTypeTestAtom"));
        Assert.That(source, Does.Contain("SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey"));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateRuntimeTypeTestFormula("));
        Assert.That(source, Does.Contain("TryCreateReferenceNullCondition("));
        Assert.That(source, Does.Contain("\"ir.runtime-hazard.reference.non-null.guard\""));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger"));
        Assert.That(source, Does.Not.Contain("\"ir.runtime-hazard.invalid-cast.formula-fallback\""));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateRuntimeReferenceCastMismatchTrigger"));
        Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateExactRuntimeInvalidCastTrigger"));
        Assert.That(irTriggerSource,
            Does.Not.Contain("private static RuntimeHazardTrigger CreateInvalidCastTypedProjectionTrigger"));
        Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateRuntimeReferenceInvalidCastTrigger"));
        Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateReferenceNullCondition"));
    }

    [Test]
    public void RuntimeHazardDirectThrow_UsesIrExceptionPreconditionTrigger()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.cs"));
        var irTriggerSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

        Assert.That(source, Does.Contain("TryCreateDirectThrowTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DirectThrow"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.direct-throw"));
        Assert.That(coreSource, Does.Contain("if (!TryCreateDirectThrowTrigger(throwNode, out var trigger))"));
        Assert.That(coreSource, Does.Not.Contain("new RuntimeHazardTrigger(new Smt"));
        Assert.That(coreSource, Does.Contain("TryCreateDirectThrowTrigger(throwNode"));
        Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateDirectThrowTrigger"));
    }

    [Test]
    public void RuntimeHazardSwitchExpressionNoMatch_PreservesIrExceptionPreconditionWhenLowerable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateSwitchExpressionNoMatchCandidate"));
        Assert.That(source, Does.Contain("CreateUnsupportedExceptionPreconditionTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch"));
        Assert.That(source, Does.Contain("TryCreateSwitchExpressionArmSymbolicCondition"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.switch-expression.no-match"));
        Assert.That(source, Does.Contain("ExceptionTypes.SwitchExpressionException"));
        Assert.That(source, Does.Contain("ExceptionCategories.DefiniteSwitchExpressionNoMatch"));
    }

    [Test]
    public void RuntimeHazardDynamicNullBinding_UsesTypedIrExceptionPrecondition()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

        Assert.That(source, Does.Contain("TryCreateDynamicNullBindingTrigger"));
        Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DynamicNullBinding"));
        Assert.That(source, Does.Contain("ir.runtime-hazard.dynamic-null-binding"));
        Assert.That(source, Does.Not.Contain("ir.runtime-hazard.dynamic-null-binding.formula-fallback"));
        Assert.That(source, Does.Contain("TryCreateOptionalReferenceSubject"));
        Assert.That(source,
            Does.Not.Contain(
                "!TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var trigger)"));
    }

    [Test]
    public void RuntimeHazardIrTriggerBridge_LivesInDedicatedPartial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.cs"));
        var irTriggerSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

        Assert.That(coreSource,
            Does.Not.Contain("private static bool TryCreateIrRelationalExceptionPreconditionTrigger"));
        Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateIrElementAccessOutOfRangeTrigger"));
        Assert.That(irTriggerSource,
            Does.Contain("private static bool TryCreateIrRelationalExceptionPreconditionTrigger"));
        Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrElementAccessOutOfRangeTrigger"));
    }

    [Test]
    public void RuntimeHazardReferenceNullHelper_ReturnsIrConditionBeforeFormulaEncoding()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
        var helperIndex =
            source.IndexOf("private static bool TryCreateReferenceNullCondition", StringComparison.Ordinal);
        var helperEndIndex = source.IndexOf("\r\n    }\r\n}", helperIndex, StringComparison.Ordinal);
        if (helperEndIndex < 0) helperEndIndex = source.IndexOf("\n    }\n}", helperIndex, StringComparison.Ordinal);

        var helperSource = source.Substring(helperIndex, helperEndIndex - helperIndex);

        Assert.That(helperSource, Does.Contain("out SymbolicCondition condition"));
        Assert.That(helperSource, Does.Not.Contain("out SmtFormula trigger"));
        Assert.That(helperSource, Does.Not.Contain("SymbolicIrFormulaEncoder.TryEncode("));
        Assert.That(helperSource, Does.Contain("new SymbolicConstantCondition(true)"));
        Assert.That(helperSource, Does.Contain("SymbolicIrLowerer.CreateReferenceNullCondition("));
        Assert.That(helperSource, Does.Not.Contain("new SymbolicRelationAtom("));
    }

    [Test]
    public void RuntimeExceptionEvidenceFacts_AcceptsAllSharedCategories()
    {
        var rejectedCategories = typeof(SymbolicRuntimeExceptionFacts.ExceptionCategories)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                       BindingFlags.FlattenHierarchy)
            .Where(static field => field.IsLiteral &&
                                   !field.IsInitOnly &&
                                   field.FieldType == typeof(string))
            .Select(static field => (string)field.GetRawConstantValue()!)
            .Where(static category => !SymbolicRuntimeExceptionFacts.IsKnownEvidenceCategory(category))
            .ToArray();

        Assert.That(rejectedCategories, Is.Empty);
    }

    [Test]
    public void AnalyzerRawSmtUsage_IsLimitedToApprovedMigrationHotspots()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hotspots = GetAnalyzerRawSmtHotspots(repositoryRoot);
        var approved = ApprovedAnalyzerRawSmtHotspots;
        var offenders = hotspots
            .Select(static hotspot => hotspot.Path)
            .Where(path => !approved.Contains(path, StringComparer.Ordinal))
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "New analyzer raw-SMT hotspots must lower to SharpProof.Symbolic.Ir and use shared proof services.");
    }

    [Test]
    public async Task RawSmtHotspotInventoryScript_ReportsApprovedAnalyzerMigrationHotspots()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = await RunPowerShellJsonScriptAsync(
            repositoryRoot,
            "Get-SharpProofRawSmtHotspots.ps1");
        var root = document.RootElement;
        var hotspotPaths = root.GetProperty("hotspots")
            .EnumerateArray()
            .Select(static hotspot => hotspot.GetProperty("path").GetString() ?? string.Empty)
            .ToArray();

        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("module").GetString(), Is.EqualTo("Analyzer"));
        Assert.That(root.GetProperty("hotspotCount").GetInt32(), Is.EqualTo(0));
        Assert.That(hotspotPaths, Is.EquivalentTo(ApprovedAnalyzerRawSmtHotspots));
        Assert.That(root.GetProperty("analyzerTranslatorShimUsageCount").GetInt32(), Is.EqualTo(0));
        Assert.That(
            root.GetProperty("analyzerTranslatorShimUsages")
                .EnumerateArray()
                .Select(static usage => usage.GetProperty("path").GetString() ?? string.Empty)
                .Distinct(StringComparer.Ordinal),
            Is.Empty);
        Assert.That(root.GetProperty("symbolicPublicFormulaSurfaceCount").GetInt32(), Is.EqualTo(0));
        Assert.That(root.GetProperty("symbolicCompatibilitySurfaceCount").GetInt32(), Is.EqualTo(0));
        Assert.That(root.GetProperty("symbolicDirectTranslatorUsageCount").GetInt32(), Is.EqualTo(0));
        Assert.That(
            root.GetProperty("symbolicDirectTranslatorUsages")
                .EnumerateArray()
                .Select(static usage => usage.GetProperty("path").GetString() ?? string.Empty)
                .Distinct(StringComparer.Ordinal),
            Is.Empty);
        var symbolicTranslatorShimUsages = root.GetProperty("symbolicTranslatorShimUsages")
            .EnumerateArray()
            .Select(static usage => new
            {
                Path = usage.GetProperty("path").GetString() ?? string.Empty,
                Text = usage.GetProperty("text").GetString() ?? string.Empty
            })
            .ToArray();
        var symbolicTranslatorShimCountsByPath = symbolicTranslatorShimUsages
            .GroupBy(static usage => usage.Path, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var symbolicTranslatorShimCountsByText = symbolicTranslatorShimUsages
            .GroupBy(static usage => usage.Text, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var symbolicTranslatorShimFamilyCounts = root.GetProperty("symbolicTranslatorShimFamilies")
            .EnumerateArray()
            .ToDictionary(
                static family => family.GetProperty("family").GetString() ?? string.Empty,
                static family => family.GetProperty("count").GetInt32(),
                StringComparer.Ordinal);

        Assert.That(root.GetProperty("symbolicTranslatorShimUsageCount").GetInt32(), Is.EqualTo(0));
        Assert.That(root.GetProperty("symbolicTranslatorShimFamilyCount").GetInt32(), Is.EqualTo(0));
        Assert.That(
            symbolicTranslatorShimCountsByPath,
            Is.Empty);
        Assert.That(
            symbolicTranslatorShimCountsByText,
            Is.Empty);
        Assert.That(
            symbolicTranslatorShimFamilyCounts,
            Is.Empty);
        Assert.That(root.GetProperty("irKnownApiLoweringCount").GetInt32(), Is.GreaterThanOrEqualTo(17));
        Assert.That(root.GetProperty("irKnownApiConditionLoweringCount").GetInt32(), Is.GreaterThan(0));
        Assert.That(root.GetProperty("irKnownApiTermLoweringCount").GetInt32(), Is.GreaterThanOrEqualTo(9));
        Assert.That(
            root.GetProperty("irKnownApiLoweringLocations")
                .EnumerateArray()
                .Select(static location => location.GetProperty("path").GetString() ?? string.Empty)
                .All(static path => path.StartsWith("SharpProof.Symbolic/Ir/", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            root.GetProperty("irKnownApiLoweringLocations")
                .EnumerateArray()
                .Select(static location => location.GetProperty("kind").GetString() ?? string.Empty)
                .All(static kind => kind is "condition" or "term"),
            Is.True);
        var runtimeHazardFormulaFallbackLocations = root.GetProperty("runtimeHazardFormulaFallbackLocations")
            .EnumerateArray()
            .Select(static location => new
            {
                Path = location.GetProperty("path").GetString() ?? string.Empty,
                Text = location.GetProperty("text").GetString() ?? string.Empty
            })
            .ToArray();
        var runtimeHazardFormulaFallbackCountsByPath = runtimeHazardFormulaFallbackLocations
            .GroupBy(static location => location.Path, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var runtimeHazardFormulaFallbackCountsByProvenance = runtimeHazardFormulaFallbackLocations
            .Select(static location => ExtractRuntimeHazardFallbackProvenance(location.Text))
            .GroupBy(static provenance => provenance, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        Assert.That(root.GetProperty("runtimeHazardFormulaFallbackCount").GetInt32(), Is.EqualTo(0));
        Assert.That(
            runtimeHazardFormulaFallbackLocations.All(static location =>
                location.Path.StartsWith("SharpProof.Symbolic/", StringComparison.Ordinal)),
            Is.True);
        Assert.That(
            runtimeHazardFormulaFallbackCountsByPath,
            Is.Empty);
        Assert.That(
            runtimeHazardFormulaFallbackCountsByProvenance,
            Is.Empty);
    }

    [Test]
    public async Task ProductionMetricsScript_TracksSymbolicPlatformPressureFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = await RunPowerShellJsonScriptAsync(
            repositoryRoot,
            "Get-SharpProofProductionMetrics.ps1");
        var root = document.RootElement;
        var modules = root.GetProperty("modules")
            .EnumerateArray()
            .Select(static module => new
            {
                Name = module.GetProperty("module").GetString() ?? string.Empty,
                Lines = module.GetProperty("lines").GetInt32()
            })
            .ToArray();
        var largestFiles = root.GetProperty("largestFiles")
            .EnumerateArray()
            .Select(static file => file.GetProperty("path").GetString() ?? string.Empty)
            .ToArray();
        var otherModule = modules.SingleOrDefault(static module => module.Name == "Other");

        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("totalFiles").GetInt32(), Is.GreaterThan(100));
        Assert.That(root.GetProperty("totalLines").GetInt32(), Is.GreaterThan(100000));
        Assert.That(modules.Select(static module => module.Name), Does.Contain("Symbolic"));
        Assert.That(modules.Select(static module => module.Name), Does.Contain("Analyzer"));
        Assert.That(modules.Select(static module => module.Name), Does.Contain("Tools"));
        Assert.That(modules.Select(static module => module.Name), Does.Contain("SearchLib"));
        Assert.That(otherModule == null || otherModule.Lines < 100, Is.True,
            "Unexpected production code growth fell into the catch-all 'Other' bucket.");
        Assert.That(largestFiles, Does.Contain("SharpProof.Symbolic/SymbolicProgramPointFacts.cs"));
        Assert.That(
            largestFiles.Any(static path =>
                path.StartsWith("SharpProof.Analyzer/Engine/PurityAnalysisEngine", StringComparison.Ordinal)),
            Is.False);
        Assert.That(largestFiles, Does.Contain("SharpProof.Symbolic/SymbolicSourceQueryService.cs"));
        Assert.That(largestFiles, Does.Not.Contain("SharpProof.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs"));
        Assert.That(largestFiles, Does.Contain("Tools/SharpProof.EffectSummary/Program.cs"));
    }

    [Test]
    public void PackageMetadata_UsesPlatformPositioningWithoutBreakingCompatibilityIdentity()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageMetadata = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SharpProof.Package",
            "SharpProof.Package.csproj"));
        var attributesMetadata = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SharpProof.Attributes",
            "SharpProof.Attributes.csproj"));
        var vsixManifest = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SharpProof.Vsix",
            "source.extension.vsixmanifest"));
        var readme = ReadFileCached(Path.Combine(repositoryRoot, "README.md"));
        var readmeSource = ReadFileCached(Path.Combine(repositoryRoot, "README.source.md"));
        var capabilityDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "capability-analysis.md"));
        var complexityDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "complexity-queries.md"));
        var coverageDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "coverage-and-limits.md"));
        var evidenceSchemaDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "evidence-schema.md"));
        var proofQueriesDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "proof-queries.md"));
        var explainReportsDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "explain-reports.md"));
        var nativeSmtPackagingDoc = ReadFileCached(Path.Combine(
            repositoryRoot,
            "docs",
            "native-smt-packaging.md"));
        var standaloneInputsDoc = ReadFileCached(Path.Combine(
            repositoryRoot,
            "docs",
            "standalone-query-inputs.md"));
        var ciExitGatesDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "ci-exit-gates.md"));
        var errorModelDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "error-model.md"));
        var effectSummaryDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "effect-summary.md"));
        var semanticPipelineMigrationDoc = ReadFileCached(Path.Combine(
            repositoryRoot,
            "docs",
            "semantic-pipeline-migration.md"));
        var diagnosticExamplesDoc = ReadFileCached(Path.Combine(repositoryRoot, "docs", "diagnostic-examples.md"));
        var readmeGeneratorScript = Path.Combine(repositoryRoot, "scripts", "Generate-Readme.ps1");
        var shippedReleaseNotes = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "AnalyzerReleases.Shipped.md"));

        Assert.That(ReadProjectElement(packageMetadata, "PackageId"), Is.EqualTo("SharpProof"));
        Assert.That(ReadProjectElement(packageMetadata, "Title"), Is.EqualTo("SharpProof"));
        Assert.That(ReadProjectElement(packageMetadata, "PackageVersion"), Is.EqualTo("0.1.0-preview.1"));
        Assert.That(ReadProjectElement(packageMetadata, "Description"),
            Does.Contain("SharpProof bounded symbolic C# analysis platform"));
        Assert.That(ReadProjectElement(packageMetadata, "Description"),
            Does.Contain("zero-allocation and capability contracts"));
        Assert.That(ReadProjectElement(packageMetadata, "Description"), Does.Contain("complexity queries"));
        Assert.That(ReadProjectElement(packageMetadata, "PackageReleaseNotes"), Does.Contain("Public preview release"));
        Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("SharpProof"));
        Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("SymbolicAnalysis"));
        Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("RuntimeHazards"));
        Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("Capabilities"));
        Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("Complexity"));
        Assert.That(ReadProjectElement(attributesMetadata, "PackageId"), Is.EqualTo("SharpProof.Attributes"));
        Assert.That(ReadProjectElement(attributesMetadata, "Title"), Is.EqualTo("SharpProof Attributes"));
        Assert.That(ReadProjectElement(attributesMetadata, "Version"), Is.EqualTo("0.1.0-preview.1"));
        Assert.That(ReadProjectElement(attributesMetadata, "Description"),
            Does.Contain("SharpProof contract attributes for bounded symbolic C# analysis"));
        Assert.That(ReadProjectElement(attributesMetadata, "Description"), Does.Contain("ZeroAllocationsAttribute"));
        Assert.That(ReadProjectElement(attributesMetadata, "Description"),
            Does.Contain("AllowedCapabilitiesAttribute"));
        Assert.That(ReadProjectElement(attributesMetadata, "Description"), Does.Contain("DoesNotThrowAttribute"));
        Assert.That(ReadProjectElement(attributesMetadata, "Description"), Does.Contain("AllowedExceptionsAttribute"));
        Assert.That(ReadProjectElement(attributesMetadata, "PackageTags"), Does.Contain("SharpProof"));
        Assert.That(ReadProjectElement(attributesMetadata, "PackageTags"), Does.Contain("SymbolicAnalysis"));
        Assert.That(ReadProjectElement(attributesMetadata, "PackageTags"), Does.Contain("Capabilities"));
        Assert.That(ReadProjectElement(attributesMetadata, "PackageTags"), Does.Contain("ZeroAllocations"));
        Assert.That(ReadProjectElement(attributesMetadata, "PackageTags"), Does.Contain("Exceptions"));
        Assert.That(vsixManifest.Descendants().Single(element => element.Name.LocalName == "DisplayName").Value,
            Is.EqualTo("SharpProof"));
        Assert.That(vsixManifest.Descendants().Single(element => element.Name.LocalName == "Description").Value,
            Does.Contain("SharpProof bounded symbolic C# analysis"));
        Assert.That(readme, Does.Contain("SharpProof"));
        Assert.That(readme, Does.Contain("Generated from README.source.md"));
        Assert.That(readme, Does.Contain("alpha/beta quality"));
        Assert.That(readme, Does.Contain("AI-assisted iteration"));
        Assert.That(readme, Does.Contain("\"vibe-coded\""));
        Assert.That(readme, Does.Not.Contain("previously called"));
        Assert.That(readme, Does.Contain("## What SharpProof Does"));
        Assert.That(readme, Does.Contain("## Who It Is For"));
        Assert.That(readme, Does.Contain("## How To Inspect Proof Results"));
        Assert.That(readme, Does.Contain("## What It Can Prove Today"));
        Assert.That(readme, Does.Contain("## Deeper Docs"));
        Assert.That(readme, Does.Contain("## Help And Feedback"));
        Assert.That(readme, Does.Contain("0.1.0-preview.1"));
        Assert.That(readme, Does.Contain("SP0013"));
        Assert.That(readme, Does.Contain("from `SP0002` through `SP0047`"));
        Assert.That(readme, Does.Contain("[ZeroAllocations]"));
        Assert.That(readme, Does.Contain("[AllowedCapabilities(...)]"));
        Assert.That(readme, Does.Contain("[Requires(...)]"));
        Assert.That(readme, Does.Contain("[DoesNotThrow]"));
        Assert.That(readme, Does.Contain("[AllowedExceptions(...)]"));
        Assert.That(readme, Does.Contain("--capabilities"));
        Assert.That(readme, Does.Contain("--complexity"));
        Assert.That(readme, Does.Contain("--runtime-hazards"));
        Assert.That(readme, Does.Contain("--check-reachability"));
        Assert.That(readme, Does.Contain("explain --file"));
        Assert.That(readme, Does.Contain(@".\build-nuget.ps1"));
        Assert.That(readme, Does.Contain(@"artifacts\nuget"));
        Assert.That(readme, Does.Contain("## Selected Examples"));
        Assert.That(readme, Does.Not.Contain("## Capability Matrix"));
        Assert.That(readme, Does.Not.Contain("## Diagnostics"));
        Assert.That(readme, Does.Not.Contain("## Configuration"));
        Assert.That(readme, Does.Not.Contain("## Roadmap"));
        Assert.That(readme, Does.Contain("ReadmeGeneratedExamplesTests.PurityAnalyzerExample_MatchesSnapshot"));
        Assert.That(readme,
            Does.Contain("ReadmeGeneratedExamplesTests.ZeroAllocationsAnalyzerExample_MatchesSnapshot"));
        Assert.That(readme, Does.Contain("ReadmeGeneratedExamplesTests.CapabilitiesCliExample_MatchesSnapshot"));
        Assert.That(readme, Does.Contain("ReadmeGeneratedExamplesTests.InvariantsCliExample_MatchesSnapshot"));
        Assert.That(readme, Does.Contain("ReadmeGeneratedExamplesTests.RuntimeHazardCliExample_MatchesSnapshot"));
        Assert.That(readme, Does.Contain("ReadmeGeneratedExamplesTests.ComplexityCliExample_MatchesSnapshot"));
        Assert.That(readme, Does.Contain("docs/readme-examples/purity-clock/input.cs"));
        Assert.That(readme, Does.Contain("docs/readme-examples/zero-allocations/input.cs"));
        Assert.That(readme, Does.Contain("docs/readme-examples/capabilities-console/input.cs"));
        Assert.That(readme, Does.Contain("docs/readme-examples/invariants-positive/input.cs"));
        Assert.That(readme, Does.Contain("docs/readme-examples/runtime-hazard-divide-by-zero/input.cs"));
        Assert.That(readme, Does.Contain("docs/readme-examples/complexity-linear/input.cs"));
        Assert.That(readme, Does.Contain("docs/diagnostic-examples.md"));
        Assert.That(readme, Does.Contain("docs/contracts.md"));
        Assert.That(readme, Does.Contain("docs/proof-queries.md"));
        Assert.That(readme, Does.Contain("docs/explain-reports.md"));
        Assert.That(readme, Does.Contain("docs/native-smt-packaging.md"));
        Assert.That(readme, Does.Contain("docs/standalone-query-inputs.md"));
        Assert.That(readme, Does.Contain("docs/ci-exit-gates.md"));
        Assert.That(readme, Does.Contain("docs/error-model.md"));
        Assert.That(readme, Does.Contain("docs/evidence-schema.md"));
        Assert.That(readme, Does.Contain("docs/semantic-pipeline-migration.md"));
        Assert.That(readme, Does.Contain("docs/coverage-and-limits.md"));
        Assert.That(readme, Does.Contain("docs/capability-analysis.md"));
        Assert.That(readme, Does.Contain("docs/complexity-queries.md"));
        Assert.That(readme, Does.Contain("docs/symbolic-invariants.md"));
        Assert.That(readme, Does.Contain("docs/effect-summary.md"));
        Assert.That(readme, Does.Not.Contain("REMAINING_ANALYZER_BACKLOG.md"));
        Assert.That(readmeSource, Does.Contain("## Selected Examples"));
        Assert.That(readmeSource, Does.Contain("## What SharpProof Does"));
        Assert.That(readmeSource, Does.Contain("## How To Inspect Proof Results"));
        Assert.That(readmeSource, Does.Contain("<!-- README_EXAMPLES -->"));
        Assert.That(File.Exists(readmeGeneratorScript), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "REMAINING_ANALYZER_BACKLOG.md")), Is.False);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "PLAN.md")), Is.True);
        var plan = ReadFileCached(Path.Combine(repositoryRoot, "PLAN.md"));
        Assert.That(plan,
            Does.Contain(
                "Write contracts -> build gets diagnostics -> inspect proof/evidence -> query deeper with CLI/API"));
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "contracts.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "proof-queries.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "explain-reports.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "native-smt-packaging.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "standalone-query-inputs.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "ci-exit-gates.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "error-model.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "evidence-schema.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "semantic-pipeline-migration.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(repositoryRoot, "docs", "coverage-and-limits.md")), Is.True);
        Assert.That(capabilityDoc, Does.Contain("SP0015"));
        Assert.That(capabilityDoc, Does.Contain("SP0016"));
        Assert.That(capabilityDoc, Does.Contain("SP0017"));
        Assert.That(capabilityDoc, Does.Contain("--capabilities"));
        Assert.That(complexityDoc, Does.Contain("QueryComplexity"));
        Assert.That(complexityDoc, Does.Contain("--complexity"));
        Assert.That(coverageDoc, Does.Contain("`Math.Min`"));
        Assert.That(coverageDoc, Does.Contain("`Math.Max`"));
        Assert.That(coverageDoc, Does.Contain("`Math.Abs`"));
        Assert.That(coverageDoc, Does.Contain("`Math.Clamp`"));
        Assert.That(coverageDoc, Does.Contain("`SymbolicExceptionPreconditionAtom`"));
        Assert.That(coverageDoc, Does.Contain("`unsupported_typed_projection`"));
        Assert.That(coverageDoc, Does.Contain("renders source-like evidence as"));
        Assert.That(coverageDoc, Does.Contain("`unknown(...)`"));
        Assert.That(coverageDoc, Does.Contain("Formula provenance is metadata only"));
        Assert.That(semanticPipelineMigrationDoc, Does.Contain("evidenceSchemaVersion: 2"));
        Assert.That(semanticPipelineMigrationDoc, Does.Contain("Effect-summary schema 5"));
        Assert.That(semanticPipelineMigrationDoc, Does.Contain("SharpProof.Baseline -- migrate"));
        Assert.That(evidenceSchemaDoc, Does.Contain("`exact-v2` Compatibility Policy"));
        Assert.That(evidenceSchemaDoc, Does.Contain("Compact symbolic JSON"));
        Assert.That(evidenceSchemaDoc, Does.Contain("Analyzer diagnostic properties"));
        Assert.That(evidenceSchemaDoc, Does.Contain("Effect summaries"));
        Assert.That(evidenceSchemaDoc, Does.Contain("Diagnostic baseline documents and entries"));
        Assert.That(evidenceSchemaDoc, Does.Contain("SharpProofEvidenceSchema.IsReadCompatible"));
        Assert.That(proofQueriesDoc, Does.Contain("SymbolicSourceCompilationProfile"));
        Assert.That(proofQueriesDoc, Does.Contain("SymbolicSourceInput.FromTextWithProfile"));
        Assert.That(proofQueriesDoc, Does.Contain("`--language-version`"));
        Assert.That(proofQueriesDoc, Does.Contain("repeated `--define`"));
        Assert.That(proofQueriesDoc, Does.Contain("`--nullable`"));
        Assert.That(proofQueriesDoc, Does.Contain("`--allow-unsafe`"));
        Assert.That(proofQueriesDoc, Does.Contain("`--documentation-mode`"));
        Assert.That(proofQueriesDoc, Does.Contain("`--platform`"));
        Assert.That(proofQueriesDoc, Does.Contain("`--optimization`"));
        Assert.That(proofQueriesDoc, Does.Contain("`--assembly-name`"));
        Assert.That(proofQueriesDoc, Does.Contain("standalone-query-inputs.md"));
        Assert.That(proofQueriesDoc, Does.Contain("explain-reports.md"));
        Assert.That(explainReportsDoc, Does.Contain("`explain --json`"));
        Assert.That(explainReportsDoc, Does.Contain("`explain --sarif`"));
        Assert.That(explainReportsDoc, Does.Contain("`explain --markdown`"));
        Assert.That(explainReportsDoc, Does.Contain("`--report-max-diagnostics <n>`"));
        Assert.That(explainReportsDoc, Does.Contain("`--report-max-hazards <n>`"));
        Assert.That(explainReportsDoc, Does.Contain("`--report-max-items <n>`"));
        Assert.That(explainReportsDoc, Does.Contain("SPQ-REPORT-TRUNCATED"));
        Assert.That(explainReportsDoc, Does.Contain("properties.crossLinks"));
        Assert.That(nativeSmtPackagingDoc, Does.Contain("`runtimes/{rid}/native/` convention"));
        Assert.That(nativeSmtPackagingDoc, Does.Contain("`buildTransitive/SharpProof.targets`"));
        Assert.That(nativeSmtPackagingDoc, Does.Contain("`smt_native_library_missing`"));
        Assert.That(nativeSmtPackagingDoc, Does.Contain("`smt_native_library_incompatible`"));
        Assert.That(nativeSmtPackagingDoc, Does.Contain("`macos-15-intel`"));
        Assert.That(nativeSmtPackagingDoc, Does.Contain("Test-SharpProofPackageConsumers.ps1"));
        Assert.That(standaloneInputsDoc, Does.Contain("`--request-json-stdin`"));
        Assert.That(standaloneInputsDoc, Does.Contain("`SymbolicSourceInput.SourceMap`"));
        Assert.That(standaloneInputsDoc, Does.Contain("\"schemaVersion\": 1"));
        Assert.That(ciExitGatesDoc, Does.Contain("`--fail-on-unproven-implies`"));
        Assert.That(ciExitGatesDoc, Does.Contain("`--fail-on-capability-violation`"));
        Assert.That(ciExitGatesDoc, Does.Contain("`--fail-on-complexity-exceeded <bound>`"));
        Assert.That(ciExitGatesDoc, Does.Contain("`--fail-on-compact-threshold <metric=max>`"));
        Assert.That(ciExitGatesDoc, Does.Contain("results remain on stdout"));
        Assert.That(errorModelDoc, Does.Contain("`SPQ1000`"));
        Assert.That(errorModelDoc, Does.Contain("`SPQ2000`"));
        Assert.That(errorModelDoc, Does.Contain("`SPQ3000`"));
        Assert.That(errorModelDoc, Does.Contain("`SymbolicOperationResult<T>`"));
        Assert.That(errorModelDoc, Does.Contain("`--error-json`"));
        Assert.That(proofQueriesDoc, Does.Contain("SymbolicCompactCapabilityResult"));
        Assert.That(proofQueriesDoc, Does.Contain("SymbolicCompactComplexityResult"));
        Assert.That(proofQueriesDoc, Does.Contain("SymbolicCompactRuntimeHazardQueryResult"));
        Assert.That(proofQueriesDoc, Does.Contain("SymbolicCompactRuntimeHazardQueryOptions"));
        Assert.That(proofQueriesDoc, Does.Contain("ISymbolicCompactResult"));
        Assert.That(effectSummaryDoc, Does.Contain("The root `README.md` is intentionally the landing page."));
        Assert.That(effectSummaryDoc, Does.Not.Contain("REMAINING_ANALYZER_BACKLOG.md"));
        Assert.That(diagnosticExamplesDoc, Does.Contain("from `SP0002` through `SP0047`"));
        Assert.That(diagnosticExamplesDoc, Does.Contain("### SP0031"));
        Assert.That(shippedReleaseNotes, Does.Contain("## Release 0.1.0"));
        Assert.That(shippedReleaseNotes, Does.Contain("SP0013"));
        Assert.That(shippedReleaseNotes, Does.Contain("SP0017"));
        Assert.That(shippedReleaseNotes, Does.Contain("SP0021"));
        Assert.That(shippedReleaseNotes, Does.Not.Contain("### Enhancements"));
    }

    [Test]
    public async Task SymbolicPublicFormulaSurface_IsLimitedToApprovedMigrationFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = await RunPowerShellJsonScriptAsync(
            repositoryRoot,
            "Get-SharpProofRawSmtHotspots.ps1");
        var root = document.RootElement;
        var unexpectedPaths = root.GetProperty("symbolicPublicFormulaSurfaces")
            .EnumerateArray()
            .Select(static surface => surface.GetProperty("path").GetString() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !ApprovedSymbolicPublicFormulaSurfaceFiles.Contains(path, StringComparer.Ordinal))
            .ToArray();

        Assert.That(
            unexpectedPaths,
            Is.Empty,
            "New public symbolic API surfaces must expose fact/proof DTOs instead of SmtFormula.");
        Assert.That(root.GetProperty("symbolicPublicFormulaSurfaceCount").GetInt32(), Is.EqualTo(0));
    }

    [Test]
    public async Task SymbolicCompatibilitySurface_IsLimitedToApprovedMigrationFiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = await RunPowerShellJsonScriptAsync(
            repositoryRoot,
            "Get-SharpProofRawSmtHotspots.ps1");
        var root = document.RootElement;
        var surfaces = root.GetProperty("symbolicCompatibilitySurfaces")
            .EnumerateArray()
            .ToArray();
        var unexpectedPaths = surfaces
            .Select(static surface => surface.GetProperty("path").GetString() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !ApprovedSymbolicCompatibilitySurfaceFiles.Contains(path, StringComparer.Ordinal))
            .ToArray();
        var categories = surfaces
            .Select(static surface => surface.GetProperty("category").GetString() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            unexpectedPaths,
            Is.Empty,
            "New formula-shaped compatibility surfaces must expose SymbolicFactInfo, SymbolicInvariantInfo, or SymbolicProofInfo instead.");
        Assert.That(surfaces, Is.Empty);
        Assert.That(categories, Does.Not.Contain("formula-metadata"));
        Assert.That(categories, Does.Not.Contain("merged-invariant"));
        Assert.That(categories, Does.Not.Contain("path-conditions"));
    }

    [Test]
    public void ProgramPointQueryResult_DoesNotPubliclyExposeRawFormulaAccessors()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicSourceQueryService.cs"));

        Assert.That(source, Does.Not.Contain("IReadOnlyList<SmtFormula> PathConditions => Analysis.PathConditions"));
        Assert.That(source, Does.Not.Contain("PathConditions => Invariant.Conditions"));
        Assert.That(source, Does.Not.Contain("result.PathConditions"));
        Assert.That(source, Does.Not.Contain("point.PathConditions"));
        Assert.That(source, Does.Not.Contain("SmtFormula MergedInvariant => Analysis.MergedInvariant"));
        Assert.That(source, Does.Not.Contain("internal SmtFormula MergedInvariant { get; }"));
        Assert.That(source, Does.Not.Contain("HasSmtFormula"));
        Assert.That(source, Does.Contain("public IReadOnlyList<string> Facts => Analysis.Facts"));
        Assert.That(source,
            Does.Contain(
                "public IReadOnlyList<SymbolicFactInfo> SymbolicFacts => SymbolicFactInfo.FromState(Analysis.PathState)"));
    }

    [Test]
    public void SymbolicInvariantSnapshot_DoesNotStoreRawFormulaResults()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicInvariantService.cs"));
        var start = source.IndexOf("internal sealed class SymbolicInvariantSnapshot", StringComparison.Ordinal);
        var end = source.IndexOf("public sealed class SymbolicInvariantFactSummary", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var snapshotSource = source[start..end];

        Assert.That(snapshotSource, Does.Not.Contain("IReadOnlyList<SmtFormula> formulas"));
        Assert.That(snapshotSource, Does.Not.Contain("internal IReadOnlyList<SmtFormula> Formulas { get; }"));
        Assert.That(snapshotSource, Does.Not.Contain("internal SmtFormula MergedInvariant { get; }"));
        Assert.That(snapshotSource, Does.Contain("public IReadOnlyList<string> Facts { get; }"));
        Assert.That(snapshotSource, Does.Contain("public string MergedInvariantText { get; }"));
    }

    [Test]
    public void SymbolicInvariantImplication_UsesIrConditionEntryPoint()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicInvariantService.cs"));

        Assert.That(source, Does.Contain("SymbolicCondition condition"));
        Assert.That(source, Does.Contain("ClassifyStateConditionTruth("));
        Assert.That(source, Does.Not.Contain("ClassifyFormulaConditionTruth"));
        Assert.That(source, Does.Not.Contain("ClassifyImplication("));
        Assert.That(source, Does.Not.Contain("SmtFormula condition,"));
        Assert.That(source, Does.Contain("internal SyntaxNode SourceNode { get; }"));
        Assert.That(source, Does.Not.Contain("analysis.SourceNode != null"));
        Assert.That(source, Does.Not.Contain("invariant.implication"));
    }

    [Test]
    public void SymbolicSourceQueryResultConstruction_UsesSinglePathStateBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicSourceQueryService.cs"));
        var queryApiSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicQueryApi.cs"));
        var combinedSource = source + "\n" + queryApiSource;

        Assert.That(source, Does.Contain("private static SymbolicSourceQueryResult CreateSourceQueryResult("));
        Assert.That(source.Split("new SymbolicSourceQueryResult(").Length - 1, Is.EqualTo(1));
        Assert.That(source,
            Does.Contain(
                "var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions);"));
        Assert.That(source, Does.Contain("SymbolicInvariantResult.FromFormulas("));
        Assert.That(combinedSource.Split("new SymbolicSourceQueryResult(").Length - 1, Is.EqualTo(2));
        Assert.That(combinedSource, Does.Not.Contain("FromPathConditions("));
        Assert.That(combinedSource,
            Does.Not.Contain(
                "SymbolicInvariantResult.FromPathConditions(\r\n                query.Analysis.PathConditions"));
        Assert.That(combinedSource,
            Does.Not.Contain(
                "SymbolicInvariantResult.FromPathConditions(\n                query.Analysis.PathConditions"));
        Assert.That(combinedSource,
            Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\r\n                analysis.PathConditions"));
        Assert.That(combinedSource,
            Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\n                analysis.PathConditions"));
        Assert.That(combinedSource, Does.Not.Contain("IReadOnlyList<SmtFormula>? pathConditions = null"));
        Assert.That(source, Does.Contain("SymbolicFactInfo.FromState(query.Analysis.PathState)"));
    }

    [Test]
    public void LegacyTranslatorReferencesOutsideShim_AreForbidden()
    {
        var repositoryRoot = FindRepositoryRoot();
        var symbolicDirectory = Path.Combine(repositoryRoot, "SharpProof.Symbolic");
        var offenders = Directory.GetFiles(symbolicDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path)
            })
            .Where(static file =>
                !file.Path.StartsWith("SharpProof.Symbolic/Smt/CSharpConditionToFormula", StringComparison.Ordinal) &&
                file.Path != "SharpProof.Symbolic/Smt/CSharpSmtFormulaTranslator.cs" &&
                file.Source.Contains("CSharpConditionToFormula.", StringComparison.Ordinal))
            .Select(static file => file.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void LegacyTranslatorShimUsage_IsAbsentFromSymbolicLayer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var symbolicDirectory = Path.Combine(repositoryRoot, "SharpProof.Symbolic");
        var offenders = Directory.GetFiles(symbolicDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = ReadFileCached(path)
            })
            .Where(static file =>
                !file.Path.StartsWith("SharpProof.Symbolic/Smt/CSharpConditionToFormula", StringComparison.Ordinal) &&
                file.Path != "SharpProof.Symbolic/Smt/CSharpSmtFormulaTranslator.cs" &&
                file.Source.Contains("CSharpSmtFormulaTranslator.", StringComparison.Ordinal))
            .Select(static file => file.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void SymbolicCleanBreakDtos_DoNotExposeSmtFormula()
    {
        var dtoTypes = new[]
        {
            typeof(SymbolicFactInfo),
            typeof(SymbolicInvariantInfo),
            typeof(SymbolicProofInfo),
            typeof(SymbolicBudgetInfo),
            typeof(SymbolicProofBackend),
            typeof(SymbolicProofStatus),
            typeof(SymbolicUnknownReason),
            typeof(SymbolicCapabilitySite),
            typeof(SymbolicCapabilityResult),
            typeof(SymbolicCapabilityUnknownReason),
            typeof(SymbolicComplexityInfo),
            typeof(SymbolicComplexityDriverInfo),
            typeof(SymbolicComplexityCalleeInfo),
            typeof(SymbolicComplexityResult),
            typeof(SymbolicComplexityKind),
            typeof(SymbolicComplexityUnknownReason)
        };

        var offenders = dtoTypes
            .SelectMany(static type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => new
                {
                    Type = type,
                    Member = member
                }))
            .Where(static item => PublicMemberReferencesSmtFormula(item.Member))
            .Select(static item => item.Type.FullName + "." + item.Member.Name)
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void ExportedSymbolicApi_SmtFormulaExposureIsLimitedToAdvancedBackend()
    {
        var assembly = typeof(SymbolicQueryService).Assembly;
        var offenders = assembly
            .GetExportedTypes()
            .Where(static type =>
                !AllowedExportedSmtFormulaTypes.Contains(type.FullName ?? string.Empty, StringComparer.Ordinal))
            .SelectMany(static type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(member => new
                {
                    Type = type,
                    Member = member
                }))
            .Where(static item => PublicMemberReferencesSmtFormula(item.Member))
            .Select(static item => item.Type.FullName + "." + item.Member.Name)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "Exported symbolic APIs should expose symbolic facts/proofs, not backend SmtFormula.");
    }

    [Test]
    public void SmtAnalysisService_RawBackendQueryMethodsAreInternal()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Smt",
            "SmtAnalysisService.cs"));

        Assert.That(source, Does.Not.Contain("public PurityProofResult ClassifyPathFeasibility("));
        Assert.That(source, Does.Not.Contain("public PurityProofResult ClassifyImplication("));
        Assert.That(source, Does.Not.Contain("public bool PathConditionsImply("));
        Assert.That(source, Does.Not.Contain("public PurityProofResult Classify(PurityProofQuery query)"));
        Assert.That(source, Does.Contain("internal PurityProofResult ClassifyPathFeasibility("));
        Assert.That(source, Does.Contain("internal PurityProofResult ClassifyImplication("));
        Assert.That(source, Does.Contain("internal bool PathConditionsImply("));
        Assert.That(source, Does.Contain("internal PurityProofResult Classify(PurityProofQuery query)"));
    }

    [Test]
    public void SymbolicFactInfo_ProjectsIrFactWithoutSolverTypes()
    {
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicVariableTerm("x", SmtValueKind.Int),
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("x == 1"),
            "test.fact",
            evidenceKey: "evidence.symbolic.fact");

        var info = SymbolicFactInfo.FromFact(fact);

        Assert.That(info.Kind, Is.EqualTo(nameof(SymbolicRelationAtom)));
        Assert.That(info.Provenance, Is.EqualTo("test.fact"));
        Assert.That(info.Confidence, Is.EqualTo(SymbolicFactConfidence.Exact.ToString()));
        Assert.That(info.EvidenceKey, Is.EqualTo("evidence.symbolic.fact"));
        Assert.That(info.Text, Is.EqualTo("x == 1"));
    }

    [Test]
    public void SymbolicState_ConstructorDeduplicatesFactsAndCreatesStableProofKey()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var first = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("x == 1"),
            "test.first");
        var duplicateWithDifferentProvenance = first with { Provenance = "test.second" };
        var y = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                new SymbolicVariableTerm("y", SmtValueKind.Int),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y");

        var left = new SymbolicState(new[] { first, duplicateWithDifferentProvenance, y });
        var right = new SymbolicState(new[] { y, first });

        Assert.That(left.Facts, Has.Length.EqualTo(2));
        Assert.That(left.NormalizedProofKey, Is.EqualTo(right.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyIncludesSymbolVersions()
    {
        var baseline = new SymbolicState().WithSymbolVersion("x", 0);
        var advanced = new SymbolicState().WithSymbolVersion("x", 1);

        Assert.That(baseline.SymbolVersions["x"], Is.EqualTo(0));
        Assert.That(advanced.SymbolVersions["x"], Is.EqualTo(1));
        Assert.That(baseline.NormalizedProofKey, Is.Not.EqualTo(advanced.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCanonicalizesCommutativeBinaryConditions()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var z = new SymbolicVariableTerm("z", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var yPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y"));
        var zPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                z,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("z > 0"),
            "test.z"));

        var left = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                xPositive,
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, yPositive, zPositive))
        });
        var right = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, zPositive, xPositive),
                yPositive)
        });

        Assert.That(left.PathConditions, Has.Length.EqualTo(1));
        Assert.That(right.PathConditions, Has.Length.EqualTo(1));
        Assert.That(left.NormalizedProofKey, Is.EqualTo(right.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesConditionIdentities()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var direct = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            xPositive
        });
        var withIdentities = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicNotCondition(new SymbolicNotCondition(new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicConstantCondition(true),
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    xPositive,
                    new SymbolicConstantCondition(false)))))
        });

        Assert.That(direct.NormalizedProofKey, Is.EqualTo(withIdentities.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesDuplicateConditionOperands()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var direct = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            xPositive
        });
        var duplicatedAnd = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, xPositive, xPositive)
        });
        var duplicatedOr = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(SymbolicConditionOperator.Or, xPositive, xPositive)
        });

        Assert.That(direct.NormalizedProofKey, Is.EqualTo(duplicatedAnd.NormalizedProofKey));
        Assert.That(direct.NormalizedProofKey, Is.EqualTo(duplicatedOr.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesComplementaryConditionOperands()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var contradiction = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                xPositive,
                new SymbolicNotCondition(xPositive))
        });
        var tautology = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                xPositive,
                new SymbolicNotCondition(xPositive))
        });

        Assert.That(contradiction.NormalizedProofKey,
            Is.EqualTo(new SymbolicState(pathConditions: new[] { new SymbolicConstantCondition(false) })
                .NormalizedProofKey));
        Assert.That(tautology.NormalizedProofKey, Is.EqualTo(new SymbolicState().NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCollapsesEvaluatedFalseDisjunction()
    {
        var falseIntegerFact = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                new SymbolicIntegerConstantTerm(1),
                new SymbolicIntegerConstantTerm(2)),
            SyntaxFactory.ParseExpression("1 > 2"),
            "test.false-integer"));
        var falseStringFact = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConstantTerm("left"),
                new SymbolicStringConstantTerm("right")),
            SyntaxFactory.ParseExpression("\"left\" == \"right\""),
            "test.false-string"));
        var contradiction = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                falseIntegerFact,
                falseStringFact)
        });
        var directContradiction = new SymbolicState(pathConditions: new[] { new SymbolicConstantCondition(false) });

        Assert.That(contradiction.IsContradictory, Is.True);
        Assert.That(contradiction.NormalizedProofKey, Is.EqualTo(directContradiction.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesAbsorbedConditionOperands()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var yPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y"));
        var direct = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            xPositive
        });
        var orAbsorption = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                xPositive,
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    xPositive,
                    yPositive))
        });
        var andAbsorption = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                xPositive,
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    xPositive,
                    yPositive))
        });

        Assert.That(direct.NormalizedProofKey, Is.EqualTo(orAbsorption.NormalizedProofKey));
        Assert.That(direct.NormalizedProofKey, Is.EqualTo(andAbsorption.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCanonicalizesNegatedFactConditions()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x");
        var negatedCondition = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicNotCondition(new SymbolicFactCondition(fact))
        });
        var negativeFactCondition = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(fact.Negate())
        });

        Assert.That(negatedCondition.NormalizedProofKey, Is.EqualTo(negativeFactCondition.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCollapsesContradictoryStates()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("x == 1"),
            "test.x");
        var yFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("y > 10"),
            "test.y");
        var factContradiction = new SymbolicState(
            new[] { xFact, xFact.Negate(), yFact },
            symbolVersions: new[] { new KeyValuePair<string, int>("x", 1) });
        var pathContradiction = new SymbolicState(
            new[] { yFact },
            new SymbolicCondition[] { new SymbolicConstantCondition(false) },
            new[] { new KeyValuePair<string, int>("y", 2) });

        Assert.That(factContradiction.IsContradictory, Is.True);
        Assert.That(pathContradiction.IsContradictory, Is.True);
        Assert.That(factContradiction.Facts, Has.Length.EqualTo(3));
        Assert.That(pathContradiction.Facts, Has.Length.EqualTo(1));
        Assert.That(factContradiction.NormalizedProofKey, Is.EqualTo(pathContradiction.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_DetectsContradictionFromNormalizedNegatedPathFacts()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x");
        var yFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y");
        var tripleNegation = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(xFact),
            new SymbolicNotCondition(
                new SymbolicNotCondition(new SymbolicNotCondition(new SymbolicFactCondition(xFact))))
        });
        var negatedOr = new SymbolicState(
            new[] { xFact },
            new SymbolicCondition[]
            {
                new SymbolicNotCondition(new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    new SymbolicFactCondition(xFact),
                    new SymbolicFactCondition(yFact)))
            });

        Assert.That(tripleNegation.IsContradictory, Is.True);
        Assert.That(negatedOr.IsContradictory, Is.True);
        Assert.That(tripleNegation.NormalizedProofKey, Is.EqualTo(negatedOr.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_DeduplicatesPathConditionsAlreadyStoredAsFacts()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.fact");
        var state = new SymbolicState(
            new[] { fact },
            new SymbolicCondition[]
            {
                new SymbolicFactCondition(fact),
                new SymbolicConstantCondition(true)
            });

        Assert.That(state.Facts, Has.Length.EqualTo(1));
        Assert.That(state.PathConditions, Is.Empty);
        Assert.That(state.NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { fact }).NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCanonicalizesEquivalentRelationAtoms()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var greaterThan = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                zero),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.gt");
        var lessThan = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                zero,
                x),
            SyntaxFactory.ParseExpression("0 < x"),
            "test.lt");
        var equal = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                zero),
            SyntaxFactory.ParseExpression("x == 0"),
            "test.eq");
        var equalFlipped = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                zero,
                x),
            SyntaxFactory.ParseExpression("0 == x"),
            "test.eq.flipped");
        var notEqual = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                x,
                zero),
            SyntaxFactory.ParseExpression("x != 0"),
            "test.ne");
        var lessThanOrEqual = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                x,
                zero),
            SyntaxFactory.ParseExpression("x <= 0"),
            "test.le");
        var greaterThanOrEqual = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThanOrEqual,
                x,
                zero),
            SyntaxFactory.ParseExpression("x >= 0"),
            "test.ge");
        var lessThanZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                x,
                zero),
            SyntaxFactory.ParseExpression("x < 0"),
            "test.lt.zero");

        Assert.That(new SymbolicState(new[] { greaterThan }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { lessThan }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { equal }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { equalFlipped }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { notEqual }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { equal.Negate() }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { lessThanOrEqual }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { greaterThan.Negate() }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { greaterThanOrEqual }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { lessThanZero.Negate() }).NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesSelfRelationFacts()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var equalSelf = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                x),
            SyntaxFactory.ParseExpression("x == x"),
            "test.equal-self");
        var lessThanOrEqualSelf = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                x,
                x),
            SyntaxFactory.ParseExpression("x <= x"),
            "test.less-equal-self");
        var notEqualSelf = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                x,
                x),
            SyntaxFactory.ParseExpression("x != x"),
            "test.not-equal-self");
        var lessThanSelf = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                x,
                x),
            SyntaxFactory.ParseExpression("x < x"),
            "test.less-self");
        var empty = new SymbolicState();
        var tautologicalFacts = new SymbolicState(new[] { equalSelf, lessThanOrEqualSelf });
        var contradictoryFact = new SymbolicState(new[] { notEqualSelf });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(lessThanSelf)
        });

        Assert.That(tautologicalFacts.Facts, Is.Empty);
        Assert.That(tautologicalFacts.NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(contradictoryFact.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFact.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesConstantFacts()
    {
        var trueFact = SymbolicFact.Exact(
            new SymbolicTruthAtom(new SymbolicBooleanConstantTerm(true)),
            SyntaxFactory.ParseExpression("true"),
            "test.true");
        var falseFact = SymbolicFact.Exact(
            new SymbolicTruthAtom(new SymbolicBooleanConstantTerm(false)),
            SyntaxFactory.ParseExpression("false"),
            "test.false");
        var integerLessThan = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicIntegerConstantTerm(1),
                new SymbolicIntegerConstantTerm(2)),
            SyntaxFactory.ParseExpression("1 < 2"),
            "test.integer-less-than");
        var stringEqual = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConstantTerm("a"),
                new SymbolicStringConstantTerm("a")),
            SyntaxFactory.ParseExpression("\"a\" == \"a\""),
            "test.string-equal");
        var nullNotEqual = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                new SymbolicNullTerm(),
                new SymbolicNullTerm()),
            SyntaxFactory.ParseExpression("null != null"),
            "test.null-not-equal");
        var integerGreaterThan = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                new SymbolicIntegerConstantTerm(1),
                new SymbolicIntegerConstantTerm(2)),
            SyntaxFactory.ParseExpression("1 > 2"),
            "test.integer-greater-than");
        var empty = new SymbolicState();
        var tautologicalFacts = new SymbolicState(new[] { trueFact, integerLessThan, stringEqual });
        var contradictoryFalseFact = new SymbolicState(new[] { falseFact });
        var contradictoryNullFact = new SymbolicState(new[] { nullNotEqual });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(integerGreaterThan)
        });

        Assert.That(tautologicalFacts.Facts, Is.Empty);
        Assert.That(tautologicalFacts.NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(contradictoryFalseFact.IsContradictory, Is.True);
        Assert.That(contradictoryNullFact.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFalseFact.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesConstantBoundsFacts()
    {
        var indexOne = new SymbolicIntegerConstantTerm(1);
        var indexNegative = new SymbolicIntegerConstantTerm(-1);
        var lengthThree = new SymbolicIntegerConstantTerm(3);
        var literalLength = new SymbolicLengthTerm(new SymbolicStringConcatTerm(
            new SymbolicStringConstantTerm("ab"),
            new SymbolicStringConstantTerm("c")));
        var inRange = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                indexOne,
                lengthThree,
                true,
                true),
            SyntaxFactory.ParseExpression("items[1]"),
            "test.in-range");
        var negativeIndex = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                indexNegative,
                lengthThree,
                true,
                true),
            SyntaxFactory.ParseExpression("items[-1]"),
            "test.negative-index");
        var upperOutOfRange = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                lengthThree,
                lengthThree,
                true,
                true),
            SyntaxFactory.ParseExpression("items[3]"),
            "test.upper-out-of-range");
        var lowerOnly = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                indexOne,
                lengthThree,
                true,
                false),
            SyntaxFactory.ParseExpression("index >= 0"),
            "test.lower-only");
        var upperOnly = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                lengthThree,
                lengthThree,
                false,
                true),
            SyntaxFactory.ParseExpression("index < length"),
            "test.upper-only");
        var computedLengthInRange = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                indexOne,
                literalLength,
                true,
                true),
            SyntaxFactory.ParseExpression("\"abc\"[1]"),
            "test.computed-length-in-range");
        var computedLengthOutOfRange = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                literalLength,
                literalLength,
                true,
                true),
            SyntaxFactory.ParseExpression("\"abc\"[3]"),
            "test.computed-length-out-of-range");
        var empty = new SymbolicState();
        var tautologicalBounds = new SymbolicState(new[] { inRange, lowerOnly, computedLengthInRange });
        var negativeContradiction = new SymbolicState(new[] { negativeIndex });
        var upperContradiction = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(upperOnly)
        });
        var computedLengthContradiction = new SymbolicState(new[] { computedLengthOutOfRange });

        Assert.That(tautologicalBounds.Facts, Is.Empty);
        Assert.That(tautologicalBounds.NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(negativeContradiction.IsContradictory, Is.True);
        Assert.That(upperContradiction.IsContradictory, Is.True);
        Assert.That(computedLengthContradiction.IsContradictory, Is.True);
        Assert.That(negativeContradiction.NormalizedProofKey, Is.EqualTo(upperContradiction.NormalizedProofKey));
        Assert.That(computedLengthContradiction.NormalizedProofKey, Is.EqualTo(upperContradiction.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesConstantStringPredicateFacts()
    {
        var haystack = new SymbolicStringConstantTerm("prefix-value");
        var prefix = new SymbolicStringConstantTerm("prefix");
        var suffix = new SymbolicStringConstantTerm("value");
        var missing = new SymbolicStringConstantTerm("missing");
        var containsPrefix = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.Contains,
                haystack,
                prefix),
            SyntaxFactory.ParseExpression("\"prefix-value\".Contains(\"prefix\")"),
            "test.contains");
        var startsWithPrefix = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.StartsWith,
                haystack,
                prefix),
            SyntaxFactory.ParseExpression("\"prefix-value\".StartsWith(\"prefix\")"),
            "test.starts-with");
        var endsWithSuffix = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.EndsWith,
                haystack,
                suffix),
            SyntaxFactory.ParseExpression("\"prefix-value\".EndsWith(\"value\")"),
            "test.ends-with");
        var doesNotContain = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.Contains,
                haystack,
                missing),
            SyntaxFactory.ParseExpression("\"prefix-value\".Contains(\"missing\")"),
            "test.missing");
        var empty = new SymbolicState();
        var tautologicalPredicates = new SymbolicState(new[] { containsPrefix, startsWithPrefix, endsWithSuffix });
        var contradictoryPredicate = new SymbolicState(new[] { doesNotContain });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(doesNotContain)
        });

        Assert.That(tautologicalPredicates.Facts, Is.Empty);
        Assert.That(tautologicalPredicates.NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(contradictoryPredicate.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryPredicate.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesStringPredicateIdentityFacts()
    {
        var text = new SymbolicVariableTerm("text", SmtValueKind.String);
        var empty = new SymbolicStringConstantTerm(string.Empty);
        var startsWithSelf = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.StartsWith,
                text,
                text),
            SyntaxFactory.ParseExpression("text.StartsWith(text)"),
            "test.starts-with-self");
        var endsWithEmpty = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.EndsWith,
                text,
                empty),
            SyntaxFactory.ParseExpression("text.EndsWith(\"\")"),
            "test.ends-with-empty");
        var containsEmpty = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.Contains,
                text,
                empty),
            SyntaxFactory.ParseExpression("text.Contains(\"\")"),
            "test.contains-empty");
        var negatedIdentity = startsWithSelf.Negate();
        var emptyState = new SymbolicState();
        var tautologicalPredicates = new SymbolicState(new[] { startsWithSelf, endsWithEmpty, containsEmpty });
        var contradictoryFact = new SymbolicState(new[] { negatedIdentity });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(negatedIdentity)
        });

        Assert.That(tautologicalPredicates.Facts, Is.Empty);
        Assert.That(tautologicalPredicates.NormalizedProofKey, Is.EqualTo(emptyState.NormalizedProofKey));
        Assert.That(contradictoryFact.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFact.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesConstantStringTermFacts()
    {
        var prefixConcat = new SymbolicStringConcatTerm(
            new SymbolicStringConstantTerm("pre"),
            new SymbolicStringConstantTerm("fix"));
        var prefixText = new SymbolicStringConstantTerm("prefix");
        var prefixLength = new SymbolicLengthTerm(prefixConcat);
        var trueConcatEquality = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                prefixConcat,
                prefixText),
            SyntaxFactory.ParseExpression("\"pre\" + \"fix\" == \"prefix\""),
            "test.concat-equality");
        var trueLengthEquality = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                prefixLength,
                new SymbolicIntegerConstantTerm(6)),
            SyntaxFactory.ParseExpression("(\"pre\" + \"fix\").Length == 6"),
            "test.length-equality");
        var falseLengthRelation = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                prefixLength,
                new SymbolicIntegerConstantTerm(6)),
            SyntaxFactory.ParseExpression("(\"pre\" + \"fix\").Length < 6"),
            "test.false-length");
        var empty = new SymbolicState();
        var tautologicalFacts = new SymbolicState(new[] { trueConcatEquality, trueLengthEquality });
        var contradictoryFact = new SymbolicState(new[] { falseLengthRelation });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(falseLengthRelation)
        });

        Assert.That(tautologicalFacts.Facts, Is.Empty);
        Assert.That(tautologicalFacts.NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(contradictoryFact.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFact.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyUsesStableNestedTermKeys()
    {
        var array = new SymbolicVariableTerm("items", SmtValueKind.Reference);
        var length = new SymbolicLengthTerm(array);
        var count = new SymbolicCountTerm(array);
        var binary = new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Add,
            length,
            new SymbolicIntegerConstantTerm(1));
        var conditional = new SymbolicConditionalTerm(
            new SymbolicConstantCondition(true),
            binary,
            count);
        var lessThan = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                conditional,
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("items.Length + 1 < 10"),
            "test.lt");
        var greaterThan = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                new SymbolicIntegerConstantTerm(10),
                conditional),
            SyntaxFactory.ParseExpression("10 > items.Length + 1"),
            "test.gt");

        Assert.That(new SymbolicState(new[] { lessThan }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { greaterThan }).NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesConstantConditionalTerms()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var guard = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.guard"));
        var trueSelected = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(new SymbolicConstantCondition(true), x, y),
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("true ? x : y"),
            "test.conditional.true");
        var falseSelected = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(new SymbolicConstantCondition(false), y, x),
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("false ? y : x"),
            "test.conditional.false");
        var direct = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("x == 1"),
            "test.direct");
        var identicalBranches = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(guard, x, x),
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("guard ? x : x"),
            "test.conditional.same-branches");
        var exactIntegerConditional = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(
                    new SymbolicConstantCondition(true),
                    new SymbolicIntegerConstantTerm(1),
                    new SymbolicIntegerConstantTerm(2)),
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("true ? 1 : 2"),
            "test.conditional.exact-integer");
        var exactStringConditional = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(
                    new SymbolicConstantCondition(false),
                    new SymbolicStringConstantTerm("left"),
                    new SymbolicStringConstantTerm("right")),
                new SymbolicStringConstantTerm("right")),
            SyntaxFactory.ParseExpression("false ? \"left\" : \"right\""),
            "test.conditional.exact-string");
        var exactBooleanConditional = SymbolicFact.Exact(
            new SymbolicTruthAtom(new SymbolicConditionalTerm(
                new SymbolicConstantCondition(false),
                new SymbolicBooleanConstantTerm(false),
                new SymbolicBooleanConstantTerm(true))),
            SyntaxFactory.ParseExpression("false ? false : true"),
            "test.conditional.exact-boolean");
        var falseBooleanConditional = SymbolicFact.Exact(
            new SymbolicTruthAtom(new SymbolicConditionalTerm(
                new SymbolicConstantCondition(true),
                new SymbolicBooleanConstantTerm(false),
                new SymbolicBooleanConstantTerm(true))),
            SyntaxFactory.ParseExpression("true ? false : true"),
            "test.conditional.false-boolean");
        var trueFactSelectedConditional = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(
                    new SymbolicFactCondition(SymbolicFact.Exact(
                        new SymbolicRelationAtom(
                            SymbolicRelationOperator.LessThan,
                            new SymbolicIntegerConstantTerm(1),
                            new SymbolicIntegerConstantTerm(2)),
                        SyntaxFactory.ParseExpression("1 < 2"),
                        "test.conditional.true-selector")),
                    new SymbolicIntegerConstantTerm(10),
                    new SymbolicIntegerConstantTerm(20)),
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("1 < 2 ? 10 : 20"),
            "test.conditional.fact-selected-true");
        var falseFactSelectedConditional = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(
                    new SymbolicFactCondition(SymbolicFact.Exact(
                        new SymbolicRelationAtom(
                            SymbolicRelationOperator.GreaterThan,
                            new SymbolicIntegerConstantTerm(1),
                            new SymbolicIntegerConstantTerm(2)),
                        SyntaxFactory.ParseExpression("1 > 2"),
                        "test.conditional.false-selector")),
                    new SymbolicStringConstantTerm("left"),
                    new SymbolicStringConstantTerm("right")),
                new SymbolicStringConstantTerm("right")),
            SyntaxFactory.ParseExpression("1 > 2 ? \"left\" : \"right\""),
            "test.conditional.fact-selected-false");
        var empty = new SymbolicState();

        Assert.That(new SymbolicState(new[] { trueSelected }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { direct }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { falseSelected }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { direct }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { identicalBranches }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { direct }).NormalizedProofKey));
        Assert.That(
            new SymbolicState(new[]
            {
                exactIntegerConditional, exactStringConditional, exactBooleanConditional, trueFactSelectedConditional,
                falseFactSelectedConditional
            }).NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { falseBooleanConditional }).IsContradictory, Is.True);
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCanonicalizesCommutativeBinaryTerms()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var one = new SymbolicIntegerConstantTerm(1);
        var ten = new SymbolicIntegerConstantTerm(10);
        var addLeft = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, x, one),
                ten),
            SyntaxFactory.ParseExpression("x + 1 < 10"),
            "test.add.left");
        var addRight = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, one, x),
                ten),
            SyntaxFactory.ParseExpression("1 + x < 10"),
            "test.add.right");
        var subtractLeft = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, x, one),
                ten),
            SyntaxFactory.ParseExpression("x - 1 < 10"),
            "test.subtract.left");
        var subtractRight = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, one, x),
                ten),
            SyntaxFactory.ParseExpression("1 - x < 10"),
            "test.subtract.right");

        Assert.That(new SymbolicState(new[] { addLeft }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { addRight }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { subtractLeft }).NormalizedProofKey,
            Is.Not.EqualTo(new SymbolicState(new[] { subtractRight }).NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyFlattensAssociativeCommutativeBinaryTerms()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var z = new SymbolicVariableTerm("z", SmtValueKind.Int);
        var addLeftAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Add,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, x, y),
                    z),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("(x + y) + z == 0"),
            "test.add.left");
        var addRightAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Add,
                    y,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, z, x)),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y + (z + x) == 0"),
            "test.add.right");
        var multiplyLeftAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Multiply,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Multiply, x, y),
                    z),
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("(x * y) * z == 1"),
            "test.multiply.left");
        var multiplyRightAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Multiply,
                    z,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Multiply, y, x)),
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("z * (y * x) == 1"),
            "test.multiply.right");
        var subtractLeftAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Subtract,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, x, y),
                    z),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("(x - y) - z == 0"),
            "test.subtract.left");
        var subtractRightAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Subtract,
                    x,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, y, z)),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x - (y - z) == 0"),
            "test.subtract.right");

        Assert.That(new SymbolicState(new[] { addLeftAssociated }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { addRightAssociated }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { multiplyLeftAssociated }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { multiplyRightAssociated }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { subtractLeftAssociated }).NormalizedProofKey,
            Is.Not.EqualTo(new SymbolicState(new[] { subtractRightAssociated }).NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesArithmeticIdentityTerms()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var one = new SymbolicIntegerConstantTerm(1);
        var direct = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("x == 10"),
            "test.direct");
        var addZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Add,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, zero, x),
                    zero),
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("0 + x + 0 == 10"),
            "test.add-zero");
        var multiplyOne = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Multiply,
                    new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Multiply, one, x),
                    one),
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("1 * x * 1 == 10"),
            "test.multiply-one");
        var subtractZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, x, zero),
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("x - 0 == 10"),
            "test.subtract-zero");
        var divideOne = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Divide, x, one),
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("x / 1 == 10"),
            "test.divide-one");
        var multiplyZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Multiply, x, zero),
                zero),
            SyntaxFactory.ParseExpression("x * 0 == 0"),
            "test.multiply-zero");
        var directZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                zero,
                zero),
            SyntaxFactory.ParseExpression("0 == 0"),
            "test.direct-zero");

        Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { addZero }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { multiplyOne }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { subtractZero }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { divideOne }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { multiplyZero }).NormalizedProofKey,
            Is.Not.EqualTo(new SymbolicState(new[] { directZero }).NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyFlattensStringConcatTerms()
    {
        var a = new SymbolicStringConstantTerm("a");
        var b = new SymbolicStringConstantTerm("b");
        var c = new SymbolicStringConstantTerm("c");
        var empty = new SymbolicStringConstantTerm(string.Empty);
        var value = new SymbolicVariableTerm("value", SmtValueKind.String);
        var leftAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(new SymbolicStringConcatTerm(a, b), c),
                new SymbolicStringConstantTerm("abc")),
            SyntaxFactory.ParseExpression("\"a\" + \"b\" + \"c\" == \"abc\""),
            "test.left");
        var rightAssociated = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(a, new SymbolicStringConcatTerm(b, c)),
                new SymbolicStringConstantTerm("abc")),
            SyntaxFactory.ParseExpression("\"a\" + (\"b\" + \"c\") == \"abc\""),
            "test.right");
        var reordered = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(new SymbolicStringConcatTerm(b, a), c),
                new SymbolicStringConstantTerm("abc")),
            SyntaxFactory.ParseExpression("\"b\" + \"a\" + \"c\" == \"abc\""),
            "test.reordered");
        var directValue = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                value,
                new SymbolicStringConstantTerm("text")),
            SyntaxFactory.ParseExpression("value == \"text\""),
            "test.direct-value");
        var emptyPrefix = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(empty, value),
                new SymbolicStringConstantTerm("text")),
            SyntaxFactory.ParseExpression("\"\" + value == \"text\""),
            "test.empty-prefix");
        var emptySuffix = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(value, empty),
                new SymbolicStringConstantTerm("text")),
            SyntaxFactory.ParseExpression("value + \"\" == \"text\""),
            "test.empty-suffix");
        var adjacentLiteralConcat = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(new SymbolicStringConcatTerm(a, b), value),
                new SymbolicStringConstantTerm("target")),
            SyntaxFactory.ParseExpression("\"a\" + \"b\" + value == \"target\""),
            "test.adjacent-literals");
        var adjacentLiteralDirect = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(new SymbolicStringConstantTerm("ab"), value),
                new SymbolicStringConstantTerm("target")),
            SyntaxFactory.ParseExpression("\"ab\" + value == \"target\""),
            "test.adjacent-literal-direct");
        var emptyOnlyConcat = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(empty, empty),
                empty),
            SyntaxFactory.ParseExpression("\"\" + \"\" == \"\""),
            "test.empty-only");
        var directEmpty = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                empty,
                empty),
            SyntaxFactory.ParseExpression("\"\" == \"\""),
            "test.direct-empty");

        Assert.That(new SymbolicState(new[] { leftAssociated }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { rightAssociated }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { leftAssociated }).NormalizedProofKey,
            Is.Not.EqualTo(new SymbolicState(new[] { reordered }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { directValue }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { emptyPrefix }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { directValue }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { emptySuffix }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { adjacentLiteralConcat }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { adjacentLiteralDirect }).NormalizedProofKey));
        Assert.That(new SymbolicState(new[] { emptyOnlyConcat }).NormalizedProofKey,
            Is.EqualTo(new SymbolicState(new[] { directEmpty }).NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyUsesStableNonRelationAtomKeys()
    {
        var value = new SymbolicVariableTerm("value", SmtValueKind.Reference);
        var text = new SymbolicStringContentTerm(value);
        var prefix = new SymbolicStringConstantTerm("pre");
        var index = new SymbolicVariableTerm("index", SmtValueKind.Int);
        var length = new SymbolicLengthTerm(value);
        var resource = new SymbolicVariableTerm("resource", SmtValueKind.Reference);
        var trigger = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                index,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("index == 0"),
            "test.trigger"));
        var left = new SymbolicState(new[]
        {
            SymbolicFact.Exact(
                new SymbolicStringPredicateAtom(SymbolicStringPredicateKind.StartsWith, text, prefix),
                SyntaxFactory.ParseExpression("value.StartsWith(\"pre\")"),
                "test.string"),
            SymbolicFact.Exact(
                new SymbolicBoundsAtom(index, length, true, true),
                SyntaxFactory.ParseExpression("value[index]"),
                "test.bounds"),
            SymbolicFact.Exact(
                new SymbolicTypeTestAtom(value, "System.String"),
                SyntaxFactory.ParseExpression("value is string"),
                "test.type"),
            SymbolicFact.Exact(
                new SymbolicOwnershipAtom(resource, false),
                SyntaxFactory.ParseExpression("resource"),
                "test.ownership"),
            SymbolicFact.Exact(
                new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Owned),
                SyntaxFactory.ParseExpression("resource"),
                "test.resource"),
            SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(SymbolicExceptionPreconditionKind.IndexOutOfRange, value,
                    trigger),
                SyntaxFactory.ParseExpression("value[index]"),
                "test.exception")
        });
        var right = new SymbolicState(new[]
        {
            SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(SymbolicExceptionPreconditionKind.IndexOutOfRange, value,
                    trigger),
                SyntaxFactory.ParseExpression("value[index]"),
                "other.exception"),
            SymbolicFact.Exact(
                new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Owned),
                SyntaxFactory.ParseExpression("resource"),
                "other.resource"),
            SymbolicFact.Exact(
                new SymbolicOwnershipAtom(resource, false),
                SyntaxFactory.ParseExpression("resource"),
                "other.ownership"),
            SymbolicFact.Exact(
                new SymbolicTypeTestAtom(value, "System.String"),
                SyntaxFactory.ParseExpression("value is string"),
                "other.type"),
            SymbolicFact.Exact(
                new SymbolicBoundsAtom(index, length, true, true),
                SyntaxFactory.ParseExpression("value[index]"),
                "other.bounds"),
            SymbolicFact.Exact(
                new SymbolicStringPredicateAtom(SymbolicStringPredicateKind.StartsWith, text, prefix),
                SyntaxFactory.ParseExpression("value.StartsWith(\"pre\")"),
                "other.string")
        });

        Assert.That(left.NormalizedProofKey, Is.EqualTo(right.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCanonicalizesAliasAtoms()
    {
        var owner = new SymbolicVariableTerm("owner", SmtValueKind.Reference);
        var alias = new SymbolicVariableTerm("alias", SmtValueKind.Reference);
        var sourceFirst = SymbolicOwnershipFactFactory.CreateAlias(
            owner,
            alias,
            true,
            SyntaxFactory.ParseExpression("alias"),
            "test.source-first");
        var targetFirst = SymbolicOwnershipFactFactory.CreateAlias(
            alias,
            owner,
            true,
            SyntaxFactory.ParseExpression("owner"),
            "test.target-first");

        var merged = new SymbolicState(new[] { sourceFirst, targetFirst });
        var baseline = new SymbolicState(new[] { sourceFirst });

        Assert.That(merged.Facts, Has.Length.EqualTo(1));
        Assert.That(merged.NormalizedProofKey, Is.EqualTo(baseline.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesSelfAliasFacts()
    {
        var owner = new SymbolicVariableTerm("owner", SmtValueKind.Reference);
        var mayAliasSelf = SymbolicOwnershipFactFactory.CreateAlias(
            owner,
            owner,
            true,
            SyntaxFactory.ParseExpression("owner"),
            "test.alias.self");
        var cannotAliasSelf = SymbolicOwnershipFactFactory.CreateAlias(
            owner,
            owner,
            false,
            SyntaxFactory.ParseExpression("owner"),
            "test.alias.not-self");
        var empty = new SymbolicState();
        var tautologicalFact = new SymbolicState(new[] { mayAliasSelf });
        var contradictoryFact = new SymbolicState(new[] { cannotAliasSelf });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(cannotAliasSelf)
        });

        Assert.That(tautologicalFact.Facts, Is.Empty);
        Assert.That(tautologicalFact.NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(contradictoryFact.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFact.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_DetectsContradictoryExactOwnershipStates()
    {
        var resource = new SymbolicVariableTerm("resource", SmtValueKind.Reference);
        var source = SyntaxFactory.ParseExpression("resource");
        var owned = SymbolicFact.Exact(
            new SymbolicOwnershipAtom(resource, false),
            source,
            "test.owned");
        var escaped = SymbolicFact.Exact(
            new SymbolicOwnershipAtom(resource, true),
            source,
            "test.escaped");
        var approximateEscaped = escaped with { Confidence = SymbolicFactConfidence.Approximate };

        var contradictoryFacts = new SymbolicState(new[] { owned, escaped });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(owned),
            new SymbolicFactCondition(escaped)
        });
        var approximateState = new SymbolicState(new[] { owned, approximateEscaped });

        Assert.That(contradictoryFacts.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFacts.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
        Assert.That(approximateState.IsContradictory, Is.False);
    }

    [Test]
    public void SymbolicState_DetectsContradictoryExactDisposalStates()
    {
        var resource = new SymbolicVariableTerm("resource", SmtValueKind.Reference);
        var disposed = SymbolicOwnershipFactFactory.CreateDisposal(
            resource,
            SymbolicDisposalState.Disposed,
            SyntaxFactory.ParseExpression("resource.Dispose()"),
            "test.disposed");
        var notDisposed = SymbolicOwnershipFactFactory.CreateDisposal(
            resource,
            SymbolicDisposalState.NotDisposed,
            SyntaxFactory.ParseExpression("resource"),
            "test.not-disposed");
        var maybeDisposed = SymbolicOwnershipFactFactory.CreateDisposal(
            resource,
            SymbolicDisposalState.MaybeDisposed,
            SyntaxFactory.ParseExpression("resource"),
            "test.maybe-disposed");

        var contradictoryFacts = new SymbolicState(new[] { disposed, notDisposed });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(disposed),
            new SymbolicFactCondition(notDisposed)
        });
        var maybeState = new SymbolicState(new[] { disposed, maybeDisposed });

        Assert.That(contradictoryFacts.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFacts.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
        Assert.That(maybeState.IsContradictory, Is.False);
    }

    [Test]
    public void SymbolicState_DetectsContradictoryExactResourceLifetimeStates()
    {
        var resource = new SymbolicVariableTerm("resource", SmtValueKind.Reference);
        var owned = SymbolicOwnershipFactFactory.CreateResourceLifetime(
            resource,
            SymbolicResourceLifetimeState.Owned,
            SyntaxFactory.ParseExpression("resource"),
            "test.owned");
        var released = SymbolicOwnershipFactFactory.CreateResourceLifetime(
            resource,
            SymbolicResourceLifetimeState.Released,
            SyntaxFactory.ParseExpression("resource.Dispose()"),
            "test.released");
        var approximateReleased = released with { Confidence = SymbolicFactConfidence.Approximate };

        var contradictoryFacts = new SymbolicState(new[] { owned, released });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(owned),
            new SymbolicFactCondition(released)
        });
        var approximateState = new SymbolicState(new[] { owned, approximateReleased });

        Assert.That(contradictoryFacts.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFacts.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
        Assert.That(approximateState.IsContradictory, Is.False);
    }

    [Test]
    public void SymbolicProofService_ContradictoryResourceStateShortCircuitsWithoutSmt()
    {
        var resource = new SymbolicVariableTerm("resource", SmtValueKind.Reference);
        var owned = SymbolicOwnershipFactFactory.CreateResourceLifetime(
            resource,
            SymbolicResourceLifetimeState.Owned,
            SyntaxFactory.ParseExpression("resource"),
            "test.owned");
        var released = SymbolicOwnershipFactFactory.CreateResourceLifetime(
            resource,
            SymbolicResourceLifetimeState.Released,
            SyntaxFactory.ParseExpression("resource.Dispose()"),
            "test.released");
        var state = new SymbolicState(new[] { owned, released });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesNullTypeTestFacts()
    {
        var nullIsString = SymbolicFact.Exact(
            new SymbolicTypeTestAtom(new SymbolicNullTerm(), "System.String"),
            SyntaxFactory.ParseExpression("null is string"),
            "test.null-type");
        var notNullIsString = nullIsString.Negate();
        var empty = new SymbolicState();
        var tautologicalFact = new SymbolicState(new[] { notNullIsString });
        var contradictoryFact = new SymbolicState(new[] { nullIsString });
        var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicFactCondition(nullIsString)
        });

        Assert.That(tautologicalFact.Facts, Is.Empty);
        Assert.That(tautologicalFact.NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
        Assert.That(contradictoryFact.IsContradictory, Is.True);
        Assert.That(contradictoryPath.IsContradictory, Is.True);
        Assert.That(contradictoryFact.NormalizedProofKey, Is.EqualTo(contradictoryPath.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeyCanonicalizesDeMorganConditions()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var yPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y"));
        var negatedAnd = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicNotCondition(new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                xPositive,
                yPositive))
        });
        var orOfNegatedOperands = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(xPositive),
                new SymbolicNotCondition(yPositive))
        });

        Assert.That(negatedAnd.NormalizedProofKey, Is.EqualTo(orOfNegatedOperands.NormalizedProofKey));
    }

    [Test]
    public void SymbolicState_NormalizedProofKeySimplifiesAbsorbingConstants()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var falseState = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicConstantCondition(false)
        });
        var andFalseState = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, xPositive, new SymbolicConstantCondition(false))
        });
        var trueState = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicConstantCondition(true)
        });
        var orTrueState = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(SymbolicConditionOperator.Or, xPositive, new SymbolicConstantCondition(true))
        });
        var notTrueState = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicNotCondition(new SymbolicConstantCondition(true))
        });
        var notFalseState = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicNotCondition(new SymbolicConstantCondition(false))
        });

        Assert.That(falseState.NormalizedProofKey, Is.EqualTo(andFalseState.NormalizedProofKey));
        Assert.That(falseState.NormalizedProofKey, Is.EqualTo(notTrueState.NormalizedProofKey));
        Assert.That(trueState.NormalizedProofKey, Is.EqualTo(orTrueState.NormalizedProofKey));
        Assert.That(trueState.NormalizedProofKey, Is.EqualTo(notFalseState.NormalizedProofKey));
    }

    [Test]
    public void SymbolicProofService_ContradictoryStateShortCircuitsWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("x == 1"),
            "test.fact");
        var state = new SymbolicState(new[] { fact, fact.Negate() });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ContradictoryConjunctionShortCircuitsWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("x == 1"),
            "test.fact");
        var state = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicFactCondition(fact),
                new SymbolicNotCondition(new SymbolicFactCondition(fact)))
        });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);

        Assert.That(state.IsContradictory, Is.True);
        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_NegatedTautologicalDisjunctionShortCircuitsWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                x,
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("x == 1"),
            "test.fact");
        var tautology = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicFactCondition(fact),
            new SymbolicNotCondition(new SymbolicFactCondition(fact)));
        var state = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicNotCondition(tautology)
        });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);

        Assert.That(state.IsContradictory, Is.True);
        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_UnsupportedTargetFactStaysConservative()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var supportedStateFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.state");
        var unsupportedTarget = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm("resource", SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression("resource"),
            "test.target");
        var state = new SymbolicState(new[] { supportedStateFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, unsupportedTarget, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(result.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.None));
    }

    [Test]
    public void SymbolicProofService_UnsupportedConfidenceFactsDoNotEvaluateSyntactically()
    {
        var syntax = SyntaxFactory.ParseExpression("1 > 2");
        var unsupportedFalseFact = new SymbolicFact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                new SymbolicIntegerConstantTerm(1),
                new SymbolicIntegerConstantTerm(2)),
            true,
            SymbolicFactConfidence.Unsupported,
            "test.unsupported-confidence",
            syntax.Span,
            null,
            null);
        var state = new SymbolicState(new[] { unsupportedFalseFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var reachability = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);
        var implication = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            unsupportedFalseFact.Negate(),
            smtAnalysis);

        Assert.That(state.IsContradictory, Is.False);
        Assert.That(reachability.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(reachability.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(implication.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(implication.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
    }

    [Test]
    public void SymbolicProofService_ApproximateConfidenceFactsDoNotEvaluateSyntactically()
    {
        var syntax = SyntaxFactory.ParseExpression("1 > 2");
        var approximateFalseFact = new SymbolicFact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                new SymbolicIntegerConstantTerm(1),
                new SymbolicIntegerConstantTerm(2)),
            true,
            SymbolicFactConfidence.Approximate,
            "test.approximate-confidence",
            syntax.Span,
            null,
            null);
        var state = new SymbolicState(new[] { approximateFalseFact });
        var exactOpposite = approximateFalseFact.Negate() with { Confidence = SymbolicFactConfidence.Exact };
        var mixedConfidenceState = new SymbolicState(new[] { approximateFalseFact, exactOpposite });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var reachability = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);
        var implication = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            approximateFalseFact.Negate(),
            smtAnalysis);

        Assert.That(state.IsContradictory, Is.False);
        Assert.That(mixedConfidenceState.IsContradictory, Is.False);
        Assert.That(reachability.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(reachability.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(implication.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(implication.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
    }

    [Test]
    public void SymbolicProofService_StateFactImpliesItselfWithoutSmt()
    {
        var target = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm("resource", SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression("resource"),
            "test.unsupported");
        var state = new SymbolicState(new[] { target });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, target, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_NegativeStateFactProvesTargetFalseWithoutSmt()
    {
        var target = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm("resource", SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression("resource"),
            "test.unsupported");
        var state = new SymbolicState(new[] { target.Negate() });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, target, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ClassifiesIrStateWithoutPublicFormulaInput()
    {
        var state = new SymbolicState(
            pathConditions: new SymbolicCondition[]
            {
                new SymbolicConstantCondition(false)
            });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
    }

    [Test]
    public void SymbolicProofService_ClassifiesEmptyStateReachabilityWithoutSmt()
    {
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(new SymbolicState(), smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Reachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ClassifiesIrBranchFeasibilityWithoutPublicFormulaInput()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var stateFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.state");
        var branchFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x <= 0"),
            "test.branch");
        var state = new SymbolicState(new[] { stateFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            state,
            new SymbolicFactCondition(branchFact),
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ClassifiesConstantBranchFeasibilityWithoutSmt()
    {
        var state = new SymbolicState();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var trueBranch = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            state,
            new SymbolicConstantCondition(true),
            smtAnalysis);
        var falseBranch = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            state,
            new SymbolicConstantCondition(false),
            smtAnalysis);

        Assert.That(trueBranch.Info.Status, Is.EqualTo(SymbolicProofStatus.Reachable));
        Assert.That(trueBranch.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseBranch.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(falseBranch.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_StateFactsClassifyCompositeBranchFeasibilityWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xPositive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x");
        var yPositive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y");
        var branch = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            new SymbolicFactCondition(xPositive),
            new SymbolicFactCondition(yPositive));
        var reachableState = new SymbolicState(new[] { xPositive, yPositive });
        var unreachableState = new SymbolicState(new[] { xPositive, yPositive });
        using var reachableSmtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        using var unreachableSmtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var reachableBranch = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            reachableState,
            branch,
            reachableSmtAnalysis);
        var unreachableBranch = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            unreachableState,
            new SymbolicNotCondition(branch),
            unreachableSmtAnalysis);

        Assert.That(reachableBranch.Info.Status, Is.EqualTo(SymbolicProofStatus.Reachable));
        Assert.That(reachableBranch.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(unreachableBranch.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(unreachableBranch.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(unreachableSmtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ClassifiesIrConditionTruthWithoutPublicFormulaInput()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var stateFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.state");
        var conditionFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x <= 0"),
            "test.condition");
        var state = new SymbolicState(new[] { stateFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateConditionTruth(
            state,
            new SymbolicFactCondition(conditionFact),
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ClassifiesSyntacticConditionTruthWithoutSmt()
    {
        var state = new SymbolicState();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var trueResult = SymbolicReachabilityService.ClassifyStateConditionTruth(
            state,
            new SymbolicConstantCondition(true),
            smtAnalysis);
        var executedAfterTrue = smtAnalysis.ExecutedQueryCount;
        var falseResult = SymbolicReachabilityService.ClassifyStateConditionTruth(
            state,
            new SymbolicConstantCondition(false),
            smtAnalysis);
        var notFalseResult = SymbolicReachabilityService.ClassifyStateConditionTruth(
            state,
            new SymbolicNotCondition(new SymbolicConstantCondition(false)),
            smtAnalysis);

        Assert.That(trueResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(notFalseResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(notFalseResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(executedAfterTrue, Is.EqualTo(0));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_StateConditionImpliesItselfWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.condition"));
        var state = new SymbolicState(pathConditions: new SymbolicCondition[] { condition });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, condition, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_StateFactImpliesMatchingConditionWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.fact");
        var state = new SymbolicState(new[] { fact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(
            state,
            new SymbolicFactCondition(fact),
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ReversedAliasFactImpliesWithoutSmt()
    {
        var owner = new SymbolicVariableTerm("owner", SmtValueKind.Reference);
        var alias = new SymbolicVariableTerm("alias", SmtValueKind.Reference);
        var stored = SymbolicOwnershipFactFactory.CreateAlias(
            owner,
            alias,
            true,
            SyntaxFactory.ParseExpression("alias"),
            "test.alias.stored");
        var queried = SymbolicOwnershipFactFactory.CreateAlias(
            alias,
            owner,
            true,
            SyntaxFactory.ParseExpression("owner"),
            "test.alias.queried");
        var state = new SymbolicState(new[] { stored });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(
            state,
            queried,
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_SelfAliasFactsClassifyWithoutSmt()
    {
        var owner = new SymbolicVariableTerm("owner", SmtValueKind.Reference);
        var mayAliasSelf = SymbolicOwnershipFactFactory.CreateAlias(
            owner,
            owner,
            true,
            SyntaxFactory.ParseExpression("owner"),
            "test.alias.self");
        var cannotAliasSelf = SymbolicOwnershipFactFactory.CreateAlias(
            owner,
            owner,
            false,
            SyntaxFactory.ParseExpression("owner"),
            "test.alias.not-self");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var trueResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            mayAliasSelf,
            smtAnalysis);
        var falseResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            cannotAliasSelf,
            smtAnalysis);

        Assert.That(trueResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_NegativeStateFactImpliesNegatedConditionWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.fact");
        var state = new SymbolicState(new[] { fact.Negate() });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(
            state,
            new SymbolicNotCondition(new SymbolicFactCondition(fact)),
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_EquivalentRelationFactImpliesWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var stored = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                zero),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.stored");
        var queried = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                zero,
                x),
            SyntaxFactory.ParseExpression("0 < x"),
            "test.queried");
        var state = new SymbolicState(new[] { stored });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, queried, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_CommutativeBinaryTermFactImpliesWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var one = new SymbolicIntegerConstantTerm(1);
        var ten = new SymbolicIntegerConstantTerm(10);
        var stored = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, x, one),
                ten),
            SyntaxFactory.ParseExpression("x + 1 < 10"),
            "test.stored");
        var queried = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, one, x),
                ten),
            SyntaxFactory.ParseExpression("1 + x < 10"),
            "test.queried");
        var state = new SymbolicState(new[] { stored });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, queried, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_AssociativeStringConcatFactImpliesWithoutSmt()
    {
        var a = new SymbolicStringConstantTerm("a");
        var b = new SymbolicStringConstantTerm("b");
        var c = new SymbolicStringConstantTerm("c");
        var stored = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(new SymbolicStringConcatTerm(a, b), c),
                new SymbolicStringConstantTerm("target")),
            SyntaxFactory.ParseExpression("\"a\" + \"b\" + \"c\" == \"target\""),
            "test.stored");
        var queried = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(a, new SymbolicStringConcatTerm(b, c)),
                new SymbolicStringConstantTerm("target")),
            SyntaxFactory.ParseExpression("\"a\" + (\"b\" + \"c\") == \"target\""),
            "test.queried");
        var state = new SymbolicState(new[] { stored });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, queried, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ConstantTargetFactsClassifyWithoutSmt()
    {
        var trueTarget = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                new SymbolicIntegerConstantTerm(1),
                new SymbolicIntegerConstantTerm(2)),
            SyntaxFactory.ParseExpression("1 < 2"),
            "test.true-target");
        var falseTarget = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                new SymbolicStringConstantTerm("same"),
                new SymbolicStringConstantTerm("same")),
            SyntaxFactory.ParseExpression("\"same\" != \"same\""),
            "test.false-target");
        var falseBoundsTarget = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                new SymbolicIntegerConstantTerm(-1),
                new SymbolicIntegerConstantTerm(3),
                true,
                true),
            SyntaxFactory.ParseExpression("items[-1]"),
            "test.false-bounds-target");
        var trueComputedBoundsTarget = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                new SymbolicIntegerConstantTerm(2),
                new SymbolicLengthTerm(new SymbolicStringConcatTerm(
                    new SymbolicStringConstantTerm("ab"),
                    new SymbolicStringConstantTerm("c"))),
                true,
                true),
            SyntaxFactory.ParseExpression("\"abc\"[2]"),
            "test.true-computed-bounds-target");
        var falseComputedBoundsTarget = SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                new SymbolicIntegerConstantTerm(3),
                new SymbolicLengthTerm(new SymbolicStringConcatTerm(
                    new SymbolicStringConstantTerm("ab"),
                    new SymbolicStringConstantTerm("c"))),
                true,
                true),
            SyntaxFactory.ParseExpression("\"abc\"[3]"),
            "test.false-computed-bounds-target");
        var trueStringTarget = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.StartsWith,
                new SymbolicStringConstantTerm("prefix-value"),
                new SymbolicStringConstantTerm("prefix")),
            SyntaxFactory.ParseExpression("\"prefix-value\".StartsWith(\"prefix\")"),
            "test.true-string-target");
        var falseStringTarget = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.EndsWith,
                new SymbolicStringConstantTerm("prefix-value"),
                new SymbolicStringConstantTerm("missing")),
            SyntaxFactory.ParseExpression("\"prefix-value\".EndsWith(\"missing\")"),
            "test.false-string-target");
        var trueStringIdentityTarget = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.Contains,
                new SymbolicVariableTerm("text", SmtValueKind.String),
                new SymbolicVariableTerm("text", SmtValueKind.String)),
            SyntaxFactory.ParseExpression("text.Contains(text)"),
            "test.true-string-identity-target");
        var trueStringEmptyTarget = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.StartsWith,
                new SymbolicVariableTerm("text", SmtValueKind.String),
                new SymbolicStringConstantTerm(string.Empty)),
            SyntaxFactory.ParseExpression("text.StartsWith(\"\")"),
            "test.true-string-empty-target");
        var trueStringLengthTarget = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicLengthTerm(new SymbolicStringConcatTerm(
                    new SymbolicStringConstantTerm("pre"),
                    new SymbolicStringConstantTerm("fix"))),
                new SymbolicIntegerConstantTerm(6)),
            SyntaxFactory.ParseExpression("(\"pre\" + \"fix\").Length == 6"),
            "test.true-string-length-target");
        var falseStringConcatTarget = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicStringConcatTerm(
                    new SymbolicStringConstantTerm("pre"),
                    new SymbolicStringConstantTerm("fix")),
                new SymbolicStringConstantTerm("suffix")),
            SyntaxFactory.ParseExpression("\"pre\" + \"fix\" == \"suffix\""),
            "test.false-string-concat-target");
        var trueConditionalTarget = SymbolicFact.Exact(
            new SymbolicTruthAtom(new SymbolicConditionalTerm(
                new SymbolicConstantCondition(false),
                new SymbolicBooleanConstantTerm(false),
                new SymbolicBooleanConstantTerm(true))),
            SyntaxFactory.ParseExpression("false ? false : true"),
            "test.true-conditional-target");
        var falseConditionalTarget = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(
                    new SymbolicConstantCondition(true),
                    new SymbolicStringConstantTerm("left"),
                    new SymbolicStringConstantTerm("right")),
                new SymbolicStringConstantTerm("right")),
            SyntaxFactory.ParseExpression("true ? \"left\" : \"right\""),
            "test.false-conditional-target");
        var trueFactSelectedConditionalTarget = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(
                    new SymbolicFactCondition(SymbolicFact.Exact(
                        new SymbolicRelationAtom(
                            SymbolicRelationOperator.LessThan,
                            new SymbolicIntegerConstantTerm(1),
                            new SymbolicIntegerConstantTerm(2)),
                        SyntaxFactory.ParseExpression("1 < 2"),
                        "test.true-fact-selector")),
                    new SymbolicIntegerConstantTerm(10),
                    new SymbolicIntegerConstantTerm(20)),
                new SymbolicIntegerConstantTerm(10)),
            SyntaxFactory.ParseExpression("1 < 2 ? 10 : 20"),
            "test.true-fact-selected-conditional-target");
        var falseTypeTestTarget = SymbolicFact.Exact(
            new SymbolicTypeTestAtom(new SymbolicNullTerm(), "System.String"),
            SyntaxFactory.ParseExpression("null is string"),
            "test.false-type-test-target");
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var trueResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueTarget,
            smtAnalysis);
        var falseResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            falseTarget,
            smtAnalysis);
        var falseBoundsResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            falseBoundsTarget,
            smtAnalysis);
        var trueComputedBoundsResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueComputedBoundsTarget,
            smtAnalysis);
        var falseComputedBoundsResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            falseComputedBoundsTarget,
            smtAnalysis);
        var trueStringResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueStringTarget,
            smtAnalysis);
        var falseStringResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            falseStringTarget,
            smtAnalysis);
        var trueStringIdentityResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueStringIdentityTarget,
            smtAnalysis);
        var trueStringEmptyResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueStringEmptyTarget,
            smtAnalysis);
        var trueStringLengthResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueStringLengthTarget,
            smtAnalysis);
        var falseStringConcatResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            falseStringConcatTarget,
            smtAnalysis);
        var trueConditionalResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueConditionalTarget,
            smtAnalysis);
        var falseConditionalResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            falseConditionalTarget,
            smtAnalysis);
        var trueFactSelectedConditionalResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            trueFactSelectedConditionalTarget,
            smtAnalysis);
        var falseTypeTestResult = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            falseTypeTestTarget,
            smtAnalysis);

        Assert.That(trueResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseBoundsResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseBoundsResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(trueComputedBoundsResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueComputedBoundsResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseComputedBoundsResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseComputedBoundsResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(trueStringResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueStringResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseStringResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseStringResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(trueStringIdentityResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueStringIdentityResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(trueStringEmptyResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueStringEmptyResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(trueStringLengthResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueStringLengthResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseStringConcatResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseStringConcatResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(trueConditionalResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueConditionalResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseConditionalResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseConditionalResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(trueFactSelectedConditionalResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(trueFactSelectedConditionalResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(falseTypeTestResult.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(falseTypeTestResult.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ComplementaryRelationFactProvesFalseWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var stored = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                zero),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.stored");
        var queried = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                x,
                zero),
            SyntaxFactory.ParseExpression("x <= 0"),
            "test.queried");
        var state = new SymbolicState(new[] { stored });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, queried, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ComplementaryRelationFactsContradictWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var greaterThan = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                zero),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.gt");
        var lessThanOrEqual = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                x,
                zero),
            SyntaxFactory.ParseExpression("x <= 0"),
            "test.le");
        var state = new SymbolicState(new[] { greaterThan, lessThanOrEqual });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);

        Assert.That(state.IsContradictory, Is.True);
        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_DeMorganEquivalentConditionImpliesWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x"));
        var yPositive = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y"));
        var stored = new SymbolicNotCondition(new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            xPositive,
            yPositive));
        var queried = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(yPositive),
            new SymbolicNotCondition(xPositive));
        var state = new SymbolicState(pathConditions: new SymbolicCondition[] { stored });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, queried, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_CompositePathConditionFactsImplyWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xPositive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x");
        var yPositive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y");
        var state = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicFactCondition(xPositive),
                new SymbolicFactCondition(yPositive))
        });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var implication = SymbolicReachabilityService.ClassifyStateImplication(state, xPositive, smtAnalysis);
        var truth = SymbolicReachabilityService.ClassifyStateConditionTruth(
            state,
            new SymbolicFactCondition(xPositive),
            smtAnalysis);

        Assert.That(implication.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(implication.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(truth.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(truth.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_StateFactsEvaluateCompositeConditionsWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var y = new SymbolicVariableTerm("y", SmtValueKind.Int);
        var xPositive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.x");
        var yPositive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                y,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("y > 0"),
            "test.y");
        var conjunction = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            new SymbolicFactCondition(xPositive),
            new SymbolicFactCondition(yPositive));
        var disjunction = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicFactCondition(xPositive),
            new SymbolicFactCondition(yPositive));
        var state = new SymbolicState(new[] { xPositive, yPositive });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var conjunctionProof = SymbolicReachabilityService.ClassifyStateImplication(state, conjunction, smtAnalysis);
        var disjunctionTruth = SymbolicReachabilityService.ClassifyStateConditionTruth(state, disjunction, smtAnalysis);
        var negatedConjunctionTruth = SymbolicReachabilityService.ClassifyStateConditionTruth(
            state,
            new SymbolicNotCondition(conjunction),
            smtAnalysis);

        Assert.That(conjunctionProof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(conjunctionProof.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(disjunctionTruth.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(disjunctionTruth.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(negatedConjunctionTruth.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(negatedConjunctionTruth.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_NegativeStateConditionProvesConditionFalseWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.condition"));
        var state = new SymbolicState(pathConditions: new SymbolicCondition[]
        {
            new SymbolicNotCondition(condition)
        });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var implication = SymbolicReachabilityService.ClassifyStateImplication(state, condition, smtAnalysis);
        var truth = SymbolicReachabilityService.ClassifyStateConditionTruth(state, condition, smtAnalysis);

        Assert.That(implication.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(implication.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(truth.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(truth.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ClassifiesTrueImplicationWithoutSmt()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var supportedStateFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.state");
        var unsupportedStateFact = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm("resource", SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression("resource"),
            "test.unsupported");
        var state = new SymbolicState(new[] { supportedStateFact, unsupportedStateFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(
            state,
            new SymbolicNotCondition(new SymbolicConstantCondition(false)),
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ClassifiesFalseImplicationForEmptyStateWithoutSmt()
    {
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateImplication(
            new SymbolicState(),
            new SymbolicConstantCondition(false),
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenFalse));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ConditionTruthRejectsNullStateBeforeProof()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SymbolicReachabilityService.ClassifyStateConditionTruth(
                null!,
                new SymbolicConstantCondition(true),
                null));
    }

    [Test]
    public void SymbolicProofService_ClassifiesIrHazardTriggerWithoutPublicFormulaInput()
    {
        var divisor = new SymbolicVariableTerm("divisor", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var nonZeroFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                divisor,
                zero),
            SyntaxFactory.ParseExpression("divisor != 0"),
            "test.state");
        var triggerCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                divisor,
                zero),
            SyntaxFactory.ParseExpression("divisor == 0"),
            "test.trigger.condition"));
        var triggerPrecondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                divisor,
                triggerCondition),
            SyntaxFactory.ParseExpression("10 / divisor"),
            "test.trigger");
        var state = new SymbolicState(new[] { nonZeroFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateHazardTrigger(
            state,
            triggerPrecondition,
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ContradictoryStateMakesHazardTriggerUnreachableWithoutSmt()
    {
        var divisor = new SymbolicVariableTerm("divisor", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var equalsZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                divisor,
                zero),
            SyntaxFactory.ParseExpression("divisor == 0"),
            "test.zero");
        var triggerCondition = new SymbolicFactCondition(equalsZero);
        var triggerPrecondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                divisor,
                triggerCondition),
            SyntaxFactory.ParseExpression("10 / divisor"),
            "test.trigger");
        var state = new SymbolicState(new[] { equalsZero, equalsZero.Negate() });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateHazardTrigger(
            state,
            triggerPrecondition,
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_ExceptionTriggerConditionProvesHazardWithoutSmt()
    {
        var divisor = new SymbolicVariableTerm("divisor", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var equalsZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                divisor,
                zero),
            SyntaxFactory.ParseExpression("divisor == 0"),
            "test.zero");
        var triggerCondition = new SymbolicFactCondition(equalsZero);
        var triggerPrecondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                divisor,
                triggerCondition),
            SyntaxFactory.ParseExpression("10 / divisor"),
            "test.trigger");
        var state = new SymbolicState(new[] { equalsZero });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateHazardTrigger(
            state,
            triggerPrecondition,
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_SmtFalseExceptionTriggerKeepsSmtProvenance()
    {
        var divisor = new SymbolicVariableTerm("divisor", SmtValueKind.Int);
        var zero = new SymbolicIntegerConstantTerm(0);
        var greaterThanOne = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                divisor,
                new SymbolicIntegerConstantTerm(1)),
            SyntaxFactory.ParseExpression("divisor > 1"),
            "test.positive");
        var equalsZero = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                divisor,
                zero),
            SyntaxFactory.ParseExpression("divisor == 0"),
            "test.zero");
        var triggerPrecondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DivideByZero,
                divisor,
                new SymbolicFactCondition(equalsZero)),
            SyntaxFactory.ParseExpression("10 / divisor"),
            "test.trigger");
        var state = new SymbolicState(new[] { greaterThanOne });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateHazardTrigger(
            state,
            triggerPrecondition,
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.GreaterThan(0));
    }

    [Test]
    public void SymbolicProofService_UnsupportedIrStaysConservative()
    {
        var unsupportedFact = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm("value", SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression("value"),
            "test.unsupported");
        var state = new SymbolicState(new[] { unsupportedFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(result.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.None));
    }

    [Test]
    public void SymbolicProofService_UnsupportedMetadataFactsDoNotPoisonSupportedProofs()
    {
        var x = new SymbolicVariableTerm("x", SmtValueKind.Int);
        var unsupportedFact = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm("resource", SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression("resource"),
            "test.metadata");
        var supportedFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.supported");
        var impossibleBranch = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x < 0"),
            "test.branch"));
        var state = new SymbolicState(new[] { unsupportedFact, supportedFact });
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            state,
            impossibleBranch,
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
    }

    [Test]
    public void SymbolicProofService_ReusesNormalizedStateCacheForEquivalentBranchProofs()
    {
        var x = new SymbolicVariableTerm("proof_cache_x", SmtValueKind.Int);
        var positive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("proof_cache_x > 0"),
            "test.positive");
        var upperBound = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                x,
                new SymbolicIntegerConstantTerm(100)),
            SyntaxFactory.ParseExpression("proof_cache_x < 100"),
            "test.upper");
        var impossibleBranch = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("proof_cache_x < 0"),
            "test.branch"));
        var options = new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(5000),
            192,
            2048);
        using var smtAnalysis = new SmtAnalysisService(options);

        var first = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            new SymbolicState(new[] { positive, upperBound }),
            impossibleBranch,
            smtAnalysis);
        var executedAfterFirst = smtAnalysis.ExecutedQueryCount;
        var second = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            new SymbolicState(new[] { upperBound, positive, positive }),
            impossibleBranch,
            smtAnalysis);

        Assert.That(first.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(first.Info.CacheHit, Is.False);
        Assert.That(first.Info.Budget, Is.Not.Null);
        Assert.That(first.Info.Budget!.MaxPathConditions, Is.EqualTo(192));
        Assert.That(first.Info.Budget.TimeoutMilliseconds, Is.EqualTo(750));
        Assert.That(second.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(second.Info.CacheHit, Is.True);
        Assert.That(second.Info.Budget, Is.Not.Null);
        Assert.That(second.Info.Budget!.ExecutedQueryCount, Is.EqualTo(executedAfterFirst));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(executedAfterFirst));
    }

    [Test]
    public void SymbolicProofService_ReusesFallbackCacheWhenNoSmtServiceIsSupplied()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var x = new SymbolicVariableTerm("proof_fallback_cache_x_" + suffix, SmtValueKind.Int);
        var positive = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x > 0"),
            "test.positive");
        var impossibleBranch = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                x,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("x < 0"),
            "test.branch"));

        var first = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            new SymbolicState(new[] { positive }),
            impossibleBranch,
            null);
        var second = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            new SymbolicState(new[] { positive, positive }),
            impossibleBranch,
            null);

        Assert.That(first.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(first.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(first.Info.CacheHit, Is.False);
        Assert.That(second.Info.Status, Is.EqualTo(SymbolicProofStatus.Unreachable));
        Assert.That(second.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
        Assert.That(second.Info.CacheHit, Is.True);
    }

    [Test]
    public void SymbolicProofService_CachesUnsupportedIrConservatively()
    {
        var unsupportedFact = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm("proof_cache_resource", SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression("proof_cache_resource"),
            "test.unsupported");
        using var smtAnalysis = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.FromMilliseconds(750),
            TimeSpan.FromMilliseconds(5000),
            192,
            2048));
        var state = new SymbolicState(new[] { unsupportedFact });

        var first = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);
        var second = SymbolicReachabilityService.ClassifyStateFeasibility(state.Normalize(), smtAnalysis);

        Assert.That(first.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(first.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(first.Info.CacheHit, Is.False);
        Assert.That(second.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(second.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.UnsupportedIrEncoding));
        Assert.That(second.Info.CacheHit, Is.True);
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicProofService_RewritesQueryFactsToCurrentSymbolVersions()
    {
        var currentX = new SymbolicVariableTerm("proof_version_x@v1", SmtValueKind.Int);
        var stateFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                currentX,
                new SymbolicIntegerConstantTerm(5)),
            SyntaxFactory.ParseExpression("proof_version_x = 5"),
            "test.state");
        var queriedFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicVariableTerm("proof_version_x", SmtValueKind.Int),
                new SymbolicIntegerConstantTerm(5)),
            SyntaxFactory.ParseExpression("proof_version_x == 5"),
            "test.query");
        var state = new SymbolicState(new[] { stateFact })
            .WithSymbolVersion("proof_version_x", 1);

        var result = SymbolicReachabilityService.ClassifyStateImplication(state, queriedFact, null);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(result.Info.Reason, Is.EqualTo("ir_state_contains_fact"));
    }

    [Test]
    public void SymbolicProofService_RewritesQueryConditionsToCurrentSymbolVersions()
    {
        var currentX = new SymbolicVariableTerm("proof_version_condition_x@v2", SmtValueKind.Int);
        var stateCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                currentX,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("proof_version_condition_x > 0"),
            "test.state"));
        var queriedCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThan,
                new SymbolicVariableTerm("proof_version_condition_x", SmtValueKind.Int),
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("proof_version_condition_x > 0"),
            "test.query"));
        var state = new SymbolicState(pathConditions: new[] { stateCondition })
            .WithSymbolVersion("proof_version_condition_x", 2);

        var result = SymbolicReachabilityService.ClassifyStateConditionTruth(state, queriedCondition, null);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Syntactic));
        Assert.That(result.Info.Reason, Is.EqualTo("ir_state_contains_condition"));
    }

    [Test]
    public void SymbolicProofService_RewritesQueryTermsToCurrentSymbolVersionsForSafeDivisors()
    {
        var currentDivisor = new SymbolicVariableTerm("proof_version_divisor@v1", SmtValueKind.Int);
        var nonZeroFact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.NotEqual,
                currentDivisor,
                new SymbolicIntegerConstantTerm(0)),
            SyntaxFactory.ParseExpression("proof_version_divisor != 0"),
            "test.state");
        var state = new SymbolicState(new[] { nonZeroFact })
            .WithSymbolVersion("proof_version_divisor", 1);
        var term = new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Divide,
            new SymbolicIntegerConstantTerm(10),
            new SymbolicVariableTerm("proof_version_divisor", SmtValueKind.Int));

        var success = SymbolicProofService.TryEncodeTermWithPathState(
            term,
            state,
            SyntaxFactory.ParseExpression("10 / proof_version_divisor"),
            out var formula);

        Assert.That(success, Is.True);
        Assert.That(formula, Is.EqualTo(new SmtIntegerBinaryTerm(
            SmtIntegerBinaryOperator.Divide,
            new SmtIntegerConstant(10),
            new SmtVariable("proof_version_divisor@v1", SmtValueKind.Int))));
    }

    [Test]
    public void SymbolicProofService_TimeoutReasonMapsToPublicUnknownReason()
    {
        var value = new SymbolicVariableTerm("proof_timeout_value", SmtValueKind.String);
        var containsNeedle = SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.Contains,
                value,
                new SymbolicStringConstantTerm("needle")),
            SyntaxFactory.ParseExpression("proof_timeout_value.Contains(\"needle\")"),
            "test.timeout");
        using var smtAnalysis = new SmtAnalysisService(new SmtAnalysisOptions(
            SmtAnalysisMode.Bounded,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(5000),
            192,
            2048));

        var result = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
            new SymbolicState(),
            new SymbolicFactCondition(containsNeedle),
            smtAnalysis);

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(result.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.Timeout));
        Assert.That(result.Info.Reason, Is.EqualTo("smt_timeout"));
        Assert.That(smtAnalysis.ExecutedQueryCount, Is.EqualTo(0));
    }

    [Test]
    public void SymbolicReachabilityService_TranslatesDivisionValueWithLoweredNonZeroPathFacts()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            public class TestClass
            {
                public int TestMethod(int dividend, int divisor)
                {
                    if (divisor != 0)
                    {
                        return dividend / divisor;
                    }

                    return 0;
                }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilitySafeDivision",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var ifStatement = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single();
        var divisionExpression = tree.GetRoot()
            .DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(static binary => binary.IsKind(SyntaxKind.DivideExpression));

        Assert.That(
            TypedSymbolicTestLowering.TryTranslateConditionFormula(
                ifStatement.Condition,
                semanticModel,
                CancellationToken.None,
                out var guardFormula),
            Is.True);
        Assert.That(guardFormula, Is.Not.Null);
        Assert.That(
            TypedSymbolicTestLowering.TryTranslateValueWithPathFacts(
                divisionExpression,
                semanticModel,
                CancellationToken.None,
                new[] { guardFormula! },
                out var translatedFormula),
            Is.True);
        Assert.That(translatedFormula, Is.TypeOf<SmtIntegerBinaryTerm>());
        Assert.That(((SmtIntegerBinaryTerm)translatedFormula!).Operator, Is.EqualTo(SmtIntegerBinaryOperator.Divide));
    }

    [Test]
    public void SemanticPipeline_LowersDivisionForSolverSafetyCheckWithoutConcretePathFacts()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            public class TestClass
            {
                public int TestMethod(int dividend, int divisor)
                {
                    return dividend / divisor;
                }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityUnsafeDivision",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var divisionExpression = tree.GetRoot()
            .DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(static binary => binary.IsKind(SyntaxKind.DivideExpression));

        Assert.That(
            TypedSymbolicTestLowering.TryTranslateValueWithPathFacts(
                divisionExpression,
                semanticModel,
                CancellationToken.None,
                Array.Empty<SmtFormula>(),
                out var translatedFormula),
            Is.True);
        Assert.That(translatedFormula, Is.TypeOf<SmtIntegerBinaryTerm>());
        Assert.That(((SmtIntegerBinaryTerm)translatedFormula!).Operator, Is.EqualTo(SmtIntegerBinaryOperator.Divide));
    }

    [Test]
    public void SymbolicReachabilityService_TranslatesConstantDivisionValueThroughSafeIrEncoding()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            public class TestClass
            {
                public int TestMethod()
                {
                    return 10 / 2;
                }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityConstantDivisionValue",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var divisionExpression = tree.GetRoot()
            .DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Single(static binary => binary.IsKind(SyntaxKind.DivideExpression));

        Assert.That(
            TypedSymbolicTestLowering.TryTranslateValue(
                divisionExpression,
                semanticModel,
                CancellationToken.None,
                out var translatedFormula),
            Is.True);
        Assert.That(translatedFormula, Is.Not.Null);
    }

    [Test]
    public void SymbolicReachabilityService_TranslatesConstantDivisionConditionThroughSafeIrEncoding()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            public class TestClass
            {
                public bool TestMethod()
                {
                    return 10 / 2 == 5;
                }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityConstantDivisionCondition",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnExpression = tree.GetRoot()
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single()
            .Expression!;

        Assert.That(
            TypedSymbolicTestLowering.TryTranslateConditionFormula(
                returnExpression,
                semanticModel,
                CancellationToken.None,
                out var translatedFormula),
            Is.True);
        Assert.That(translatedFormula, Is.Not.Null);
    }

    [Test]
    public void NullableFlowFacts_OwnsCodeAnalysisAttributeMetadataAcrossProofConsumers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var nullableFlowSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "NullableFlowFacts.cs"));
        var attributeNames = new[]
        {
            "AllowNullAttribute",
            "DisallowNullAttribute",
            "MaybeNullAttribute",
            "NotNullAttribute",
            "MaybeNullWhenAttribute",
            "NotNullWhenAttribute",
            "NotNullIfNotNullAttribute",
            "MemberNotNullAttribute",
            "MemberNotNullWhenAttribute",
            "DoesNotReturnAttribute",
            "DoesNotReturnIfAttribute"
        };

        foreach (var attributeName in attributeNames)
            Assert.That(
                nullableFlowSource,
                Does.Contain("System.Diagnostics.CodeAnalysis." + attributeName));

        var consumerPaths = new[]
        {
            Path.Combine(repositoryRoot, "SharpProof.Symbolic", "SymbolicProgramPointFacts.cs"),
            Path.Combine(repositoryRoot, "SharpProof.Symbolic", "Ir", "SymbolicSemanticPipeline.cs"),
            Path.Combine(repositoryRoot, "SharpProof.Symbolic", "Ir", "SymbolicIrLowerer.Nullable.cs"),
            Path.Combine(repositoryRoot, "SharpProof.Analyzer", "MethodEnsuresAnalyzer.cs")
        };

        foreach (var consumerPath in consumerPaths)
        {
            var consumerSource = ReadFileCached(consumerPath);
            Assert.That(consumerSource, Does.Contain("NullableFlowFacts."));
            Assert.That(consumerSource, Does.Not.Contain("System.Diagnostics.CodeAnalysis."));
        }
    }

    [Test]
    public void SymbolicReachabilityService_CollectsIrSimplePatternBranchAssumptions()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              class C
                                              {
                                                  bool M(object x)
                                                  {
                                                      return x is string s;
                                                  }
                                              }
                                              """);
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityPatternBranchAssumptions",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var isPatternExpression = tree.GetRoot()
            .DescendantNodes()
            .OfType<IsPatternExpressionSyntax>()
            .Single();
        var localDesignation = tree.GetRoot()
            .DescendantNodes()
            .OfType<SingleVariableDesignationSyntax>()
            .Single();
        var parameter = semanticModel.GetDeclaredSymbol(
            tree.GetRoot().DescendantNodes().OfType<ParameterSyntax>().Single(),
            CancellationToken.None)!;
        var local = semanticModel.GetDeclaredSymbol(localDesignation, CancellationToken.None)!;
        var parameterName = SymbolicFactFactory.GetSmtVariableName(parameter);
        var localName = SymbolicFactFactory.GetSmtVariableName(local);
        var formulas = new List<SmtFormula>();

        Assert.That(
            TypedSymbolicTestLowering.TryCollectBranchAssumptions(
                isPatternExpression,
                true,
                semanticModel,
                CancellationToken.None,
                formulas),
            Is.True);
        using var analysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var localReference = new SmtVariable(localName, SmtValueKind.Reference);
        var parameterReference = new SmtVariable(parameterName, SmtValueKind.Reference);
        Assert.That(
            analysis.ClassifyImplication(
                formulas,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, localReference, parameterReference)).Outcome,
            Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(
            analysis.ClassifyImplication(
                formulas,
                new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    localReference,
                    new SmtNullConstant())).Outcome,
            Is.EqualTo(PurityProofOutcome.ProvablyPure));
        Assert.That(
            analysis.ClassifyImplication(
                formulas,
                new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    parameterReference,
                    new SmtNullConstant())).Outcome,
            Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void SymbolicReachabilityService_CollectsIrNotNullWhenInvocationBranchAssumptions()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              using System.Diagnostics.CodeAnalysis;

                                              class C
                                              {
                                                  static bool IsPresent([NotNullWhen(true)] string? value) => value is not null;

                                                  bool M(string? x)
                                                  {
                                                      return IsPresent(x);
                                                  }
                                              }
                                              """);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(NotNullWhenAttribute).Assembly.Location)
        };
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityNotNullWhenBranchAssumptions",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();
        var parameter = semanticModel.GetDeclaredSymbol(
            tree.GetRoot().DescendantNodes().OfType<ParameterSyntax>().Last(),
            CancellationToken.None)!;
        var parameterName = SymbolicFactFactory.GetSmtVariableName(parameter);
        var formulas = new List<SmtFormula>();

        Assert.That(
            TypedSymbolicTestLowering.TryCollectBranchAssumptions(
                invocation,
                true,
                semanticModel,
                CancellationToken.None,
                formulas),
            Is.True);
        using var analysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        Assert.That(
            analysis.ClassifyImplication(
                formulas,
                new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    new SmtVariable(parameterName, SmtValueKind.Reference),
                    new SmtNullConstant())).Outcome,
            Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void SymbolicReachabilityService_CollectsIrNegatedNotNullWhenBranchAssumptions()
    {
        static bool IsReferenceNonNullComparison(SmtFormula formula, string variableName)
        {
            return formula is SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.NotEqual,
                Left: SmtVariable { Name: var name, Kind: SmtValueKind.Reference },
                Right: SmtNullConstant
            } && name == variableName;
        }

        var tree = CSharpSyntaxTree.ParseText("""
                                              using System.Diagnostics.CodeAnalysis;

                                              class C
                                              {
                                                  static bool IsMissing([NotNullWhen(false)] string? value) => value is null;

                                                  bool M(string? x)
                                                  {
                                                      return !IsMissing(x);
                                                  }
                                              }
                                              """);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(NotNullWhenAttribute).Assembly.Location)
        };
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityNegatedNotNullWhenBranchAssumptions",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnExpression = tree.GetRoot()
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single()
            .Expression!;
        var parameter = semanticModel.GetDeclaredSymbol(
            tree.GetRoot().DescendantNodes().OfType<ParameterSyntax>().Last(),
            CancellationToken.None)!;
        var parameterName = SymbolicFactFactory.GetSmtVariableName(parameter);
        var formulas = new List<SmtFormula>();

        Assert.That(
            TypedSymbolicTestLowering.TryCollectBranchAssumptions(
                returnExpression,
                true,
                semanticModel,
                CancellationToken.None,
                formulas),
            Is.True);
        Assert.That(formulas.Any(formula => IsReferenceNonNullComparison(formula, parameterName)), Is.True);
    }

    [Test]
    public void SymbolicReachabilityService_CollectsIrNonNullOperandImplicationBranchAssumptions()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              class C
                                              {
                                                  bool M(object x)
                                                  {
                                                      return (x as string) != null;
                                                  }
                                              }
                                              """);
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityAsBranchAssumptions",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnExpression = tree.GetRoot()
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Single()
            .Expression!;
        var parameter = semanticModel.GetDeclaredSymbol(
            tree.GetRoot().DescendantNodes().OfType<ParameterSyntax>().Single(),
            CancellationToken.None)!;
        var parameterName = SymbolicFactFactory.GetSmtVariableName(parameter);
        var formulas = new List<SmtFormula>();

        Assert.That(
            TypedSymbolicTestLowering.TryCollectBranchAssumptions(
                returnExpression,
                true,
                semanticModel,
                CancellationToken.None,
                formulas),
            Is.True);
        using var analysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        Assert.That(
            analysis.ClassifyImplication(
                formulas,
                new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    new SmtVariable(parameterName, SmtValueKind.Reference),
                    new SmtNullConstant())).Outcome,
            Is.EqualTo(PurityProofOutcome.ProvablyPure));
    }

    [Test]
    public void SymbolicProgramPointFacts_LowersBranchAssumptionsToTypedState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("SymbolicReachabilityService.ApplyBranchFacts("));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryAddBranchConditionFacts("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCollectBranchAssumptions("));
    }

    [Test]
    public void SymbolicProgramPointFacts_ProjectsAncestorReachabilityStateWithoutLegacyFormulaCompatibility()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));
        Assert.That(source, Does.Contain("public static SymbolicState CollectAncestorReachabilityState("));
        Assert.That(source, Does.Contain("internal static SymbolicState CollectPriorAssignmentState("));
        Assert.That(source, Does.Contain("internal static SymbolicState CollectLoopBodyInvariantState("));
        Assert.That(source, Does.Contain("internal static SymbolicState CollectCompletedLoopExitInvariantState("));
        Assert.That(source, Does.Not.Contain("SmtFormula"));
        Assert.That(source, Does.Not.Contain("TryEncodeStatePathConditions"));
    }

    [Test]
    public void SymbolicProgramPointFacts_ProjectsSwitchStatementStateFactsIntoAncestorState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));
        var stateHelperIndex = source.IndexOf("public static SymbolicState CollectAncestorReachabilityState(",
            StringComparison.Ordinal);
        var switchStatementIndex = source.IndexOf("else if (ancestor is SwitchStatementSyntax switchStatementSyntax)",
            stateHelperIndex, StringComparison.Ordinal);
        var switchExpressionIndex =
            source.IndexOf("else if (ancestor is SwitchExpressionSyntax switchExpressionSyntax)", switchStatementIndex,
                StringComparison.Ordinal);
        var switchStatementSource =
            source.Substring(switchStatementIndex, switchExpressionIndex - switchStatementIndex);

        Assert.That(stateHelperIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(switchStatementIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(switchExpressionIndex, Is.GreaterThan(switchStatementIndex));
        Assert.That(switchStatementSource, Does.Contain("AddSwitchStatementSectionStateFacts("));
        Assert.That(source, Does.Contain("private static void AddSwitchStatementSectionStateFacts("));
        Assert.That(source, Does.Contain("TryCreateSwitchStatementSectionSymbolicCondition("));
        Assert.That(source, Does.Contain("state = state.AddPathCondition(sectionCondition)"));
        Assert.That(source, Does.Contain("AddSwitchBranchPatternBindingStateFacts("));
        Assert.That(source, Does.Contain("AddSwitchBranchGuardStateFacts("));
    }

    [Test]
    public void SymbolicReachabilityService_HasNoLegacyFormulaAdapters()
    {
        var repositoryRoot = FindRepositoryRoot();
        var reachabilitySource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicReachabilityService.cs"));
        var pipelineSource = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "Ir",
            "SymbolicSemanticPipeline.cs"));

        Assert.That(reachabilitySource, Does.Not.Contain("SmtFormula"));
        Assert.That(reachabilitySource, Does.Not.Contain("TryTranslate"));
        Assert.That(reachabilitySource, Does.Not.Contain("TryCreateAsExpressionAssignedValueFacts"));
        Assert.That(pipelineSource, Does.Contain("LowerNullableHasValueTerm("));
        Assert.That(pipelineSource, Does.Contain("LowerNullableValueTerm("));
        Assert.That(pipelineSource, Does.Contain("LowerArrayDimensionLengthTerm("));
        Assert.That(pipelineSource, Does.Contain("LowerArrayLengthCountAliasCondition("));
        Assert.That(pipelineSource, Does.Contain("LowerStringNonNullCondition("));
        Assert.That(pipelineSource, Does.Contain("LowerAsExpressionAssignmentFacts("));
    }
    [Test]
    public void SymbolicReachabilityService_AddsIrLoweredBranchCondition()
    {
        var (semanticModel, ifStatement) = CreateSingleIfStatement("class C { void M(int x) { if (x > 0) { } } }");
        var pathConditions = new List<SmtFormula>();

        var added = TypedSymbolicTestLowering.TryAddBranchConditionFacts(
            ifStatement.Condition,
            true,
            semanticModel,
            CancellationToken.None,
            pathConditions);

        Assert.That(added, Is.True);
        Assert.That(pathConditions, Is.Not.Empty);
        Assert.That(pathConditions.Select(static formula => formula.ToString() ?? string.Empty),
            Has.Some.Contains("x"));
    }

    [Test]
    public void SymbolicReachabilityService_AppliesTypedIrBranchState()
    {
        var (semanticModel, ifStatement) = CreateSingleIfStatement("class C { void M(int x) { if (x <= 10) { } } }");
        var initialState = new SymbolicState();

        var lowering = SymbolicReachabilityService.ApplyBranchFacts(
            initialState,
            ifStatement.Condition,
            true,
            semanticModel,
            CancellationToken.None);
        var branchState = lowering.Value!;

        Assert.That(lowering.IsExact, Is.True);
        Assert.That(branchState.PathConditions, Has.Length.EqualTo(1));
        Assert.That(SymbolicStructuralKey.ForCondition(branchState.PathConditions[0]), Does.Contain("x"));
    }

    [Test]
    public void SymbolicReachabilityService_EvaluatesConditionTruthFromIrLoweredPathState()
    {
        var (semanticModel, ifStatement) = CreateSingleIfStatement(
            "class C { void M(int x) { if (x > 0) { var y = x > -1; } } }");
        var expression = ifStatement.Statement
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single()
            .Initializer!
            .Value;
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var branch = SymbolicReachabilityService.ApplyBranchFacts(
            new SymbolicState(),
            ifStatement.Condition,
            true,
            semanticModel,
            CancellationToken.None);
        var condition = SymbolicSemanticPipeline.LowerCondition(
            expression,
            new SymbolicLoweringContext(semanticModel, CancellationToken.None));
        var proof = SymbolicReachabilityService.ClassifyStateConditionTruth(
            branch.Value!,
            condition.Value!,
            smtAnalysis);

        Assert.That(branch.IsExact, Is.True);
        Assert.That(condition.IsExact, Is.True);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicReachabilityService_CollectsNegatedIrBranchState()
    {
        var (semanticModel, ifStatement) = CreateSingleIfStatement("class C { void M(int x) { if (x == 0) { } } }");

        var lowering = SymbolicReachabilityService.ApplyBranchFacts(
            new SymbolicState(),
            ifStatement.Condition,
            false,
            semanticModel,
            CancellationToken.None);
        var branchState = lowering.Value!;

        Assert.That(lowering.IsExact, Is.True);
        Assert.That(branchState.PathConditions.Single(), Is.TypeOf<SymbolicNotCondition>());
    }

    [Test]
    public void SymbolicProgramPointFacts_CollectsAncestorReachabilityState()
    {
        var (semanticModel, ifStatement) = CreateSingleIfStatement(
            "class C { void M(int x) { if (x <= 10) { int y = x; } } }");
        var statement = ifStatement.Statement
            .DescendantNodesAndSelf()
            .OfType<LocalDeclarationStatementSyntax>()
            .Single();

        var state = SymbolicProgramPointFacts.CollectAncestorReachabilityState(
            statement,
            semanticModel,
            CancellationToken.None);

        Assert.That(state.PathConditions, Has.Length.EqualTo(1));
        Assert.That(SymbolicStructuralKey.ForCondition(state.PathConditions[0]), Does.Contain("x"));
    }

    [Test]
    public void SymbolicProgramPointFacts_CollectsInlineAssignmentBranchStateBeforeSharedReachabilityFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));
        var helperIndex = source.IndexOf("private static void AddReachabilityCondition(", StringComparison.Ordinal);
        var helperEndIndex = source.IndexOf("private static bool TryAddInlineAssignmentReachabilityState(", helperIndex,
            StringComparison.Ordinal);
        var helperSource = source.Substring(helperIndex, helperEndIndex - helperIndex);
        var inlineAssignmentIndex =
            helperSource.IndexOf("if (TryAddInlineAssignmentReachabilityState(", StringComparison.Ordinal);
        var sharedReachabilityIndex = helperSource.IndexOf("SymbolicReachabilityService.ApplyBranchFacts(",
            StringComparison.Ordinal);

        Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(helperEndIndex, Is.GreaterThan(helperIndex));
        Assert.That(inlineAssignmentIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(sharedReachabilityIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(inlineAssignmentIndex, Is.LessThan(sharedReachabilityIndex));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrAncestorState()
    {
        var (semanticModel, ifStatement) = CreateSingleIfStatement(
            "class C { int M(int divisor) { if (divisor == 0) { return 10 / divisor; } return 0; } }");
        var returnStatement = ifStatement.Statement
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);

        Assert.That(analysis.PathState.PathConditions, Has.Length.EqualTo(1));
        var condition = (SymbolicFactCondition)analysis.PathState.PathConditions.Single();
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition.Fact,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrInlineAssignmentAncestorState()
    {
        var (semanticModel, ifStatement) = CreateSingleIfStatement(
            "class C { int M(int input) { int divisor = input; if ((divisor = divisor + 1) == 0) { return divisor; } return 0; } }");
        var returnStatement = ifStatement.Statement
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var state = analysis.PathState;
        var assignedValueCondition = state.PathConditions
            .OfType<SymbolicFactCondition>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Fact.Provenance,
                "ir.path.inline-assignment.assigned-value",
                StringComparison.Ordinal));
        var comparisonCondition = state.PathConditions
            .OfType<SymbolicFactCondition>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Fact.Provenance,
                "ir.path.inline-assignment.comparison",
                StringComparison.Ordinal));

        Assert.That(assignedValueCondition, Is.Not.Null);
        Assert.That(comparisonCondition, Is.Not.Null);
        Assert.That(
            SymbolicReachabilityService.ClassifyStateImplication(
                state,
                assignedValueCondition!.Fact,
                smtAnalysis).Info.Status,
            Is.EqualTo(SymbolicProofStatus.ProvenTrue));
        Assert.That(
            SymbolicReachabilityService.ClassifyStateImplication(
                state,
                comparisonCondition!.Fact,
                smtAnalysis).Info.Status,
            Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointFacts_BuildsNativeContainingBlockEntryStateInPriorAssignmentPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("private static void RemoveStateFactsInvalidatedByContainingBlockEntry("));
        Assert.That(source, Does.Contain("private static void AddContainingBlockEntryStateFacts("));
        Assert.That(source, Does.Contain("RemoveStateFactsInvalidatedByContainingBlockEntry("));
        Assert.That(source, Does.Contain("AddContainingBlockEntryStateFacts("));
        Assert.That(source, Does.Contain("if (site is BlockSyntax siteBlock)"));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrPriorAssignmentState()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { int M() { int divisor = 5; return 10 / divisor; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicPriorAssignmentState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = analysis.PathState.PathConditions
            .OfType<SymbolicFactCondition>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Fact.Provenance,
                "ir.path.prior-statement.assigned-value",
                StringComparison.Ordinal));

        Assert.That(condition, Is.Not.Null);
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!.Fact,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointFacts_CarriesIrContainingBlockEntryState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M(int[] values) { foreach (var value in values) { return value; } return 0; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicContainingBlockEntryState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var bodyBlock = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ForEachStatementSyntax>()
            .Single()
            .Statement as BlockSyntax;

        Assert.That(bodyBlock, Is.Not.Null);

        var state = SymbolicProgramPointFacts.CollectPriorAssignmentState(
            bodyBlock!,
            semanticModel,
            CancellationToken.None);
        var condition = state.PathConditions
            .OfType<SymbolicFactCondition>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Fact.Provenance,
                "ir.path.foreach-entry.not-null",
                StringComparison.Ordinal));

        Assert.That(condition, Is.Not.Null);
    }

    [Test]
    public void SymbolicProgramPointFacts_CarriesIrForInitializerState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { for (int divisor = 5; divisor == 5;) { return 10 / divisor; } return 0; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicForInitializerState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var forStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ForStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var state = SymbolicProgramPointFacts.CollectForInitializerState(
            forStatement,
            semanticModel,
            CancellationToken.None);
        var condition = state.PathConditions
            .OfType<SymbolicFactCondition>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Fact.Provenance,
                "ir.path.for-initializer.assigned-value",
                StringComparison.Ordinal));

        Assert.That(condition, Is.Not.Null);
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            state,
            condition!.Fact,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointFacts_UsesTypedIncrementOrDecrementStateHelper()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("TryCreateIncrementOrDecrementStateTerm("));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryCreateIncrementOrDecrementFact("));
        Assert.That(source, Does.Not.Contain("private static bool TryCreateIncrementOrDecrementFact("));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrIncrementState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { int divisor = 4; divisor++; return 10 / divisor; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicIncrementState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = analysis.PathState.PathConditions
            .OfType<SymbolicFactCondition>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Fact.Provenance,
                "ir.path.prior-statement.increment",
                StringComparison.Ordinal));

        Assert.That(condition, Is.Not.Null);
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!.Fact,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointFacts_ContainsNativeCompoundAssignmentStateBuilder()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("TryCreateCompoundAssignmentStateTerm("));
        Assert.That(source, Does.Contain("ir.path.prior-statement.compound-assignment"));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrCompoundAssignmentState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { int divisor = 4; divisor += 1; return 10 / divisor; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicCompoundAssignmentState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = analysis.PathState.PathConditions
            .OfType<SymbolicFactCondition>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Fact.Provenance,
                "ir.path.prior-statement.compound-assignment",
                StringComparison.Ordinal));

        Assert.That(condition, Is.Not.Null);
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!.Fact,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointFacts_ContainsNativeCoalesceAssignmentStateHelpers()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("coalesceAssignmentIsKnownNoOp"));
        Assert.That(source, Does.Contain("IsKnownNullReferenceSymbol(state,"));
        Assert.That(source, Does.Contain("IsKnownNullableNoValueSymbol(state,"));
        Assert.That(source, Does.Contain("AddAssignedNonNullStateFacts("));
        Assert.That(source, Does.Contain("\"ir.path.prior-statement.coalesce-assignment\""));
    }

    [Test]
    public void SymbolicProgramPointFacts_ContainsNativeTupleStateBuilders()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("TryHandleTupleDeconstructionDeclarationState("));
        Assert.That(source, Does.Contain("TryHandleTupleAssignmentState("));
        Assert.That(source, Does.Contain("AddTupleElementAssignedValueStateFacts("));
        Assert.That(source, Does.Contain("AddTupleElementSourceSymbolSnapshotStateFacts("));
        Assert.That(source, Does.Contain("AddTupleElementTargetStateFacts("));
    }

    [Test]
    public void SymbolicProgramPointFacts_ContainsNormalCompletionStateBuilders()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("AddNormalCompletionStateFacts("));
        Assert.That(source, Does.Contain("AddTopLevelNotNullParameterNormalCompletionStateFacts("));
        Assert.That(source, Does.Contain("AddTopLevelDoesNotReturnIfNormalCompletionStateFacts("));
        Assert.That(source, Does.Contain("AddTopLevelMemberNotNullNormalCompletionStateFacts("));
        Assert.That(source, Does.Contain("AddTopLevelArrayCreationNormalCompletionStateFacts("));
        Assert.That(source, Does.Contain("AddTopLevelThrowGuardNormalCompletionStateFacts("));
        Assert.That(source, Does.Contain("AddTopLevelDereferenceNormalCompletionStateFacts("));
        Assert.That(source, Does.Contain("AddStableReferenceNonNullStateFact("));
        Assert.That(source,
            Does.Contain("SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(elementAccess, context)"));
    }

    [Test]
    public void SymbolicProgramPointFacts_ContainsNativeDivideModuloCompoundAssignmentStateOperators()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicProgramPointFacts.cs"));

        Assert.That(source, Does.Contain("SyntaxKind.DivideAssignmentExpression"));
        Assert.That(source, Does.Contain("SyntaxKind.ModuloAssignmentExpression"));
        Assert.That(source, Does.Contain("SymbolicBinaryTermOperator.Divide"));
        Assert.That(source, Does.Contain("SymbolicBinaryTermOperator.Remainder"));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_ProjectsDivideAssignmentState()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { int M() { int x = 10; x /= 2; return x; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicDivideAssignmentState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        var localSymbol = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .Select(node => semanticModel.GetDeclaredSymbol(node))
            .OfType<ILocalSymbol>()
            .Single(symbol => string.Equals(symbol.Name, "x", StringComparison.Ordinal));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var variableName = SymbolicFactFactory.GetSmtVariableName(localSymbol.OriginalDefinition);
        var condition = FindIntegerEqualityFact(analysis.PathState, variableName, 5);

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_ProjectsModuloAssignmentState()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { int M() { int x = 17; x %= 5; return x; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicModuloAssignmentState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        var localSymbol = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .Select(node => semanticModel.GetDeclaredSymbol(node))
            .OfType<ILocalSymbol>()
            .Single(symbol => string.Equals(symbol.Name, "x", StringComparison.Ordinal));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var variableName = SymbolicFactFactory.GetSmtVariableName(localSymbol.OriginalDefinition);
        var condition = FindIntegerEqualityFact(analysis.PathState, variableName, 2);

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    private static bool IsIntegerEqualityFact(SymbolicFact fact, string variableName, long expectedValue)
    {
        return fact.Atom switch
        {
            SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: var left,
                Right: SymbolicIntegerConstantTerm { Value: var value }
            } when TryGetTermVariableName(left, out var name) &&
                       string.Equals(name, variableName, StringComparison.Ordinal) &&
                       value == expectedValue => true,
            SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: SymbolicIntegerConstantTerm { Value: var value },
                Right: var right
            } when TryGetTermVariableName(right, out var name) &&
                       string.Equals(name, variableName, StringComparison.Ordinal) &&
                       value == expectedValue => true,
            _ => false
        };
    }

    private static bool IsLengthEqualityFact(SymbolicFact fact, string variableName, long expectedValue)
    {
        return fact.Atom switch
        {
            SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: SymbolicLengthTerm { Value: var leftValue },
                Right: SymbolicIntegerConstantTerm { Value: var value }
            } when TryGetTermVariableName(leftValue, out var name) &&
                       string.Equals(name, variableName, StringComparison.Ordinal) &&
                       value == expectedValue => true,
            SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: SymbolicIntegerConstantTerm { Value: var value },
                Right: SymbolicLengthTerm { Value: var rightValue }
            } when TryGetTermVariableName(rightValue, out var name) &&
                       string.Equals(name, variableName, StringComparison.Ordinal) &&
                       value == expectedValue => true,
            _ => false
        };
    }

    private static bool IsVariableEqualityFact(SymbolicFact fact, string leftVariableName, string rightVariableName)
    {
        return fact.Atom switch
        {
            SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: var left,
                Right: var right
            } when TryGetTermVariableName(left, out var leftName) &&
                       TryGetTermVariableName(right, out var rightName) &&
                       string.Equals(leftName, leftVariableName, StringComparison.Ordinal) &&
                       string.Equals(rightName, rightVariableName, StringComparison.Ordinal) => true,
            SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: var left,
                Right: var right
            } when TryGetTermVariableName(left, out var leftName) &&
                       TryGetTermVariableName(right, out var rightName) &&
                       string.Equals(leftName, rightVariableName, StringComparison.Ordinal) &&
                       string.Equals(rightName, leftVariableName, StringComparison.Ordinal) => true,
            _ => false
        };
    }

    private static bool TryGetTermVariableName(SymbolicTerm term, out string variableName)
    {
        switch (term)
        {
            case SymbolicVariableTerm { Name: var name }:
                variableName = name;
                return true;
            case SymbolicMemberTerm
            {
                Receiver: SymbolicVariableTerm { Name: var receiverName },
                MemberName: var memberName
            }:
                variableName = receiverName + "." + memberName;
                return true;
            default:
                variableName = string.Empty;
                return false;
        }
    }

    private static SymbolicFact? FindIntegerEqualityFact(
        SymbolicState state,
        string variableName,
        long expectedValue,
        string? provenance = null)
    {
        return state.PathConditions
            .OfType<SymbolicFactCondition>()
            .Select(condition => condition.Fact)
            .Concat(state.Facts)
            .Where(candidate =>
                provenance == null || string.Equals(candidate.Provenance, provenance, StringComparison.Ordinal))
            .FirstOrDefault(candidate => IsIntegerEqualityFact(candidate, variableName, expectedValue));
    }

    private static SymbolicFact? FindLengthEqualityFact(
        SymbolicState state,
        string variableName,
        long expectedValue,
        string? provenance = null)
    {
        return state.PathConditions
            .OfType<SymbolicFactCondition>()
            .Select(condition => condition.Fact)
            .Concat(state.Facts)
            .Where(candidate =>
                provenance == null || string.Equals(candidate.Provenance, provenance, StringComparison.Ordinal))
            .FirstOrDefault(candidate => IsLengthEqualityFact(candidate, variableName, expectedValue));
    }

    private static SymbolicFact? FindVariableEqualityFact(
        SymbolicState state,
        string leftVariableName,
        string rightVariableName,
        string? provenance = null)
    {
        return state.PathConditions
            .OfType<SymbolicFactCondition>()
            .Select(condition => condition.Fact)
            .Concat(state.Facts)
            .Where(candidate =>
                provenance == null || string.Equals(candidate.Provenance, provenance, StringComparison.Ordinal))
            .FirstOrDefault(candidate => IsVariableEqualityFact(candidate, leftVariableName, rightVariableName));
    }

    private static SymbolicFact? FindFactByProvenance(SymbolicState state, string provenance)
    {
        return state.PathConditions
            .OfType<SymbolicFactCondition>()
            .Select(condition => condition.Fact)
            .Concat(state.Facts)
            .SingleOrDefault(candidate => string.Equals(candidate.Provenance, provenance, StringComparison.Ordinal));
    }

    private static string DescribeState(SymbolicState state)
    {
        var pathConditions = state.PathConditions
            .Select(static condition => condition is SymbolicFactCondition factCondition
                ? "PC:" + factCondition.Fact.Provenance + ":" + factCondition.Fact
                : "PC:" + condition)
            .ToArray();
        var facts = state.Facts
            .Select(static fact => "F:" + fact.Provenance + ":" + fact)
            .ToArray();
        return string.Join(Environment.NewLine, pathConditions.Concat(facts));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrCoalesceAssignmentState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { int[] values = null; values ??= new int[1]; return values.Length; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicCoalesceAssignmentState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = FindFactByProvenance(
            analysis.PathState,
            "ir.path.prior-statement.coalesce-assignment.assigned-non-null");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesCompletedReceiverNonNullState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M(object value) { var hash = value.GetHashCode(); if (value == null) { return 1; } return hash; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicCompletedReceiverState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var ifStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<IfStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            ifStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = FindFactByProvenance(
            analysis.PathState,
            "ir.path.normal-completion.dereference.receiver-not-null");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesNotNullParameterCompletionState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public static class Guard { public static void Require([NotNull] object? value) { } }
            class C { int M(string? value) { Guard.Require(value); return value.Length; } }
            """);
        var compilation = CSharpCompilation.Create(
            "SymbolicNotNullParameterCompletionState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = FindFactByProvenance(
            analysis.PathState,
            "ir.path.normal-completion.parameter-not-null");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesArrayCreationCompletionState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int[] M(int length) { var values = new int[length]; if (length < 0) { return new int[0]; } return values; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicArrayCreationCompletionState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var ifStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<IfStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            ifStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = FindFactByProvenance(
            analysis.PathState,
            "ir.path.normal-completion.array-length.non-negative");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_PreservesIrCoalesceAssignmentNoOpState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { int[] values = new int[2]; values ??= new int[1]; return values.Length; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicCoalesceAssignmentNoOpState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var condition = FindFactByProvenance(
            analysis.PathState,
            "ir.path.prior-statement.assigned-non-null");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrTupleElementLiteralState()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { int M() { var pair = (1, 2); return pair.Item1; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicTupleElementLiteralState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        var pairSymbol = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .Select(node => semanticModel.GetDeclaredSymbol(node))
            .OfType<ILocalSymbol>()
            .Single(symbol => string.Equals(symbol.Name, "pair", StringComparison.Ordinal));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var tupleElementName = SymbolicFactFactory.GetSmtVariableName(pairSymbol.OriginalDefinition) + ".Item1";
        var condition = FindIntegerEqualityFact(
            analysis.PathState,
            tupleElementName,
            1,
            "ir.path.prior-statement.tuple-element.assigned-value");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrTupleArrayLengthState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { var pair = (values: new int[2], other: 1); return pair.values.Length; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicTupleArrayLengthState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        var pairSymbol = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .Select(node => semanticModel.GetDeclaredSymbol(node))
            .OfType<ILocalSymbol>()
            .Single(symbol => string.Equals(symbol.Name, "pair", StringComparison.Ordinal));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var tupleElementName = SymbolicFactFactory.GetSmtVariableName(pairSymbol.OriginalDefinition) + ".Item1";
        var condition = FindLengthEqualityFact(
            analysis.PathState,
            tupleElementName,
            2,
            "ir.path.prior-statement.tuple-element.assigned-length");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrTupleSnapshotState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { var pair = (1, 2); var copy = pair; return copy.Item1; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicTupleSnapshotState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        var copySymbol = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<VariableDeclaratorSyntax>()
            .Select(node => semanticModel.GetDeclaredSymbol(node))
            .OfType<ILocalSymbol>()
            .Single(symbol => string.Equals(symbol.Name, "copy", StringComparison.Ordinal));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var copyTupleElementName = SymbolicFactFactory.GetSmtVariableName(copySymbol.OriginalDefinition) + ".Item1";
        var pairTupleElementName = SymbolicFactFactory.GetSmtVariableName(
            tree.GetRoot()
                .DescendantNodesAndSelf()
                .OfType<VariableDeclaratorSyntax>()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .OfType<ILocalSymbol>()
                .Single(symbol => string.Equals(symbol.Name, "pair", StringComparison.Ordinal))
                .OriginalDefinition) + ".Item1";
        var condition = FindVariableEqualityFact(
            analysis.PathState,
            copyTupleElementName,
            pairTupleElementName,
            "ir.path.prior-statement.tuple-element.snapshot");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicProgramPointAnalysis_CarriesIrTupleDeconstructionState()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "class C { int M() { var pair = (1, 2); var (divisor, other) = pair; return 10 / divisor; } }");
        var compilation = CSharpCompilation.Create(
            "SymbolicTupleDeconstructionState",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var returnStatement = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>()
            .Single();
        var divisorSymbol = tree.GetRoot()
            .DescendantNodesAndSelf()
            .OfType<SingleVariableDesignationSyntax>()
            .Select(node => semanticModel.GetDeclaredSymbol(node))
            .OfType<ILocalSymbol>()
            .Single(symbol => string.Equals(symbol.Name, "divisor", StringComparison.Ordinal));
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var analysis = new SymbolicInvariantService().AnalyzeAt(
            returnStatement,
            semanticModel,
            smtAnalysis,
            CancellationToken.None);
        var variableName = SymbolicFactFactory.GetSmtVariableName(divisorSymbol.OriginalDefinition);
        var pairTupleElementName = SymbolicFactFactory.GetSmtVariableName(
            tree.GetRoot()
                .DescendantNodesAndSelf()
                .OfType<VariableDeclaratorSyntax>()
                .Select(node => semanticModel.GetDeclaredSymbol(node))
                .OfType<ILocalSymbol>()
                .Single(symbol => string.Equals(symbol.Name, "pair", StringComparison.Ordinal))
                .OriginalDefinition) + ".Item1";
        var condition = FindVariableEqualityFact(
            analysis.PathState,
            variableName,
            pairTupleElementName,
            "ir.path.prior-statement.tuple-target.assigned-value");

        Assert.That(condition, Is.Not.Null, DescribeState(analysis.PathState));
        var proof = SymbolicReachabilityService.ClassifyStateImplication(
            analysis.PathState,
            condition!,
            smtAnalysis);
        Assert.That(proof.Info.Status, Is.EqualTo(SymbolicProofStatus.ProvenTrue));
    }

    [Test]
    public void SymbolicInvariantAnalysis_UsesStateFeasibilityWithoutFormulaFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicInvariantService.cs"));
        var stateProofIndex = source.IndexOf("ClassifyStateFeasibility(pathState", StringComparison.Ordinal);
        Assert.That(stateProofIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(source, Does.Contain("stateProof?.Info.Status == SymbolicProofStatus.Unreachable"));
        Assert.That(source, Does.Not.Contain("ClassifyStateFeasibilityWithFormulaFallback"));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaReachability(formulas"));
        Assert.That(source, Does.Not.Contain("PathFeasibility switch"));
    }

    [Test]
    public void AnalyzerNullPathProbes_CarrySymbolicNullStateBeforeFeasibility()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.CfgBranchAssumptions.cs"));
        var nullProbeIndex =
            source.IndexOf("var nullPathState = TryCreateReferenceNullPathState(", StringComparison.Ordinal);
        var nullFeasibilityIndex =
            source.IndexOf("IsPathStateUnsatisfiable(currentState, nullPathState, smtAnalysis",
                StringComparison.Ordinal);
        var nonNullProbeIndex = source.IndexOf("var nonNullPathState = TryCreateReferenceNullPathState(",
            StringComparison.Ordinal);
        var nonNullFeasibilityIndex =
            source.IndexOf("IsPathStateUnsatisfiable(currentState, nonNullPathState, smtAnalysis",
                StringComparison.Ordinal);

        Assert.That(nullProbeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(nullFeasibilityIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(nullProbeIndex, Is.LessThan(nullFeasibilityIndex));
        Assert.That(nonNullProbeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(nonNullFeasibilityIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(nonNullProbeIndex, Is.LessThan(nonNullFeasibilityIndex));
    }

    [Test]
    public void AnalyzerAssignmentFacts_UseOnlyTypedSymbolicState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.SymbolicState.cs"));
        var symbolicFactIndex = source.IndexOf("AddAssignedSymbolicEqualityFact(", StringComparison.Ordinal);

        Assert.That(symbolicFactIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryCreateAssignedValueFact("));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact("));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryCreateStringContentAssignedValueFact("));
        Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryCreateStringNonNullAssignedValueFact("));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerAsExpressionAssignmentFacts("));
        Assert.That(source,
            Does.Not.Contain("SymbolicReachabilityService.TryCreateAsExpressionAssignedValueConditions("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateStringValue("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateStringNonNullFormula("));
        Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateAsExpressionAssignmentFacts("));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerTerm"));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerLengthProjectionTerm"));
        Assert.That(source, Does.Contain("SymbolicSemanticPipeline.LowerStringTerm"));
        Assert.That(source, Does.Not.Contain("SymbolicIrLowerer.TryLower"));
        Assert.That(source, Does.Contain("AddAssignedSymbolicEqualityFact("));
        Assert.That(source, Does.Not.Contain("return SymbolicSmtFormulaLowerer.TryLowerCondition("));
        Assert.That(source, Does.Contain("new SymbolicRelationAtom("));
        Assert.That(source, Does.Contain("\"analyzer.assignment.value\""));
        Assert.That(source, Does.Contain("\"analyzer.assignment.length\""));
        Assert.That(source, Does.Contain("\"analyzer.assignment.string\""));
        Assert.That(source, Does.Contain("\"analyzer.assignment.collection_length\""));
        Assert.That(source, Does.Contain("\"analyzer.assignment.string_nonnull\""));
    }

    [Test]
    public void AnalyzerStateMerge_PreservesCommonSymbolicPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Analyzer",
            "Engine",
            "PurityAnalysisEngine.StateMerge.cs"));

        Assert.That(source, Does.Contain("MergePathStatesAcrossAll("));
        Assert.That(source, Does.Contain("IntersectSymbolicFacts("));
        Assert.That(source, Does.Contain("SymbolicStateMerger.MergePathConditionsAcrossAll(normalizedStates)"));
        Assert.That(source, Does.Not.Contain("IntersectSymbolicConditions("));
        Assert.That(source,
            Does.Contain("MergePathStatesAcrossAll(new[] { state1, state2 }, mergedSmtSymbolVersions)"));
    }

    [Test]
    public void RuntimeHazardClassification_UsesOnlyTypedTriggerProof()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardQueryService.cs"));
        Assert.That(source, Does.Contain("ClassifyStateHazardTrigger("));
        Assert.That(source, Does.Contain("ClassifyIrTrigger("));
        Assert.That(source, Does.Not.Contain("new SmtUnaryFormula(SmtUnaryOperator.Not, triggerCondition)"));
        Assert.That(source, Does.Contain("analysis.PathState"));
        Assert.That(source, Does.Not.Contain("ClassifyFormulaConditionTruth"));
        Assert.That(source, Does.Not.Contain("WithIrFirst"));
    }

    [Test]
    public void RuntimeHazardThrowNullRefinement_UsesOnlyTypedPathState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = ReadFileCached(Path.Combine(
            repositoryRoot,
            "SharpProof.Symbolic",
            "SymbolicRuntimeHazardQueryService.cs"));
        var helperIndex = source.IndexOf("TryCreateReferenceNullCondition(", StringComparison.Ordinal);
        var irProofIndex = source.IndexOf(
            "ClassifyStateConditionTruth(",
            helperIndex,
            StringComparison.Ordinal);

        Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(irProofIndex, Is.GreaterThan(helperIndex));
        Assert.That(source, Does.Not.Contain("PathConditionsImply"));
        Assert.That(source, Does.Not.Contain("TryTranslateNullCondition"));
        Assert.That(source, Does.Contain("\"ir.runtime-hazard.throw-null.trigger\""));
        Assert.That(source, Does.Contain("out var throwNullTriggerPrecondition"));
        Assert.That(source, Does.Contain("triggerPrecondition = throwNullTriggerPrecondition"));
        Assert.That(source,
            Does.Contain("private static SymbolicFact? TryGetFactPrecondition(SymbolicCondition condition)"));
    }

    [Test]
    public void StandaloneTestFixtures_DeclareAtLeastOneRunnableTest()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testRoots = new[]
        {
            Path.Combine(repositoryRoot, "SharpProof.Test"),
            Path.Combine(repositoryRoot, "SharpProof.ToolingTest")
        };
        var emptyFixtures = new List<string>();

        foreach (var sourcePath in testRoots.SelectMany(root =>
                     Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)))
        {
            if (sourcePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                sourcePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                continue;

            var root = CSharpSyntaxTree.ParseText(ReadFileCached(sourcePath)).GetRoot();
            foreach (var fixture in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!HasAttribute(fixture.AttributeLists, "TestFixture") ||
                    fixture.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                    fixture.BaseList != null)
                    continue;

                var hasRunnableTest = fixture.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Any(method =>
                        HasAttribute(method.AttributeLists, "Test") ||
                        HasAttribute(method.AttributeLists, "TestCase") ||
                        HasAttribute(method.AttributeLists, "TestCaseSource") ||
                        HasAttribute(method.AttributeLists, "Theory"));
                if (!hasRunnableTest)
                    emptyFixtures.Add(
                        Path.GetRelativePath(repositoryRoot, sourcePath).Replace('\\', '/') +
                        ":" + fixture.Identifier.ValueText);
            }
        }

        Assert.That(
            emptyFixtures,
            Is.Empty,
            "Standalone [TestFixture] classes must declare at least one runnable test.");

        static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string expectedName)
        {
            return attributeLists
                .SelectMany(static list => list.Attributes)
                .Select(static attribute => attribute.Name.ToString())
                .Any(name =>
                    string.Equals(name, expectedName, StringComparison.Ordinal) ||
                    string.Equals(name, expectedName + "Attribute", StringComparison.Ordinal) ||
                    name.EndsWith("." + expectedName, StringComparison.Ordinal) ||
                    name.EndsWith("." + expectedName + "Attribute", StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task ProductionMetricsScript_ReportsProductionModulesAndExcludesTests()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = await RunPowerShellJsonScriptAsync(
            repositoryRoot,
            "Get-SharpProofProductionMetrics.ps1");
        var root = document.RootElement;
        var moduleNames = root.GetProperty("modules")
            .EnumerateArray()
            .Select(static module => module.GetProperty("module").GetString() ?? string.Empty)
            .ToArray();
        var largestPaths = root.GetProperty("largestFiles")
            .EnumerateArray()
            .Select(static file => file.GetProperty("path").GetString() ?? string.Empty)
            .ToArray();

        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("totalFiles").GetInt32(), Is.GreaterThan(50));
        Assert.That(root.GetProperty("totalLines").GetInt32(), Is.GreaterThan(10000));
        Assert.That(moduleNames, Does.Contain("Analyzer"));
        Assert.That(moduleNames, Does.Contain("Symbolic"));
        Assert.That(moduleNames, Does.Contain("SearchLib"));
        Assert.That(largestPaths, Has.None.StartsWith("SharpProof.Test/"));
    }

    private static async Task<JsonDocument> RunPowerShellJsonScriptAsync(
        string repositoryRoot,
        string scriptName)
    {
        var cacheKey = new PowerShellJsonScriptCacheKey(repositoryRoot, scriptName);
        var output = await s_powerShellJsonOutputCache.GetOrAdd(
                cacheKey,
                static key =>
                    new Lazy<Task<string>>(() => RunPowerShellJsonScriptCoreAsync(key.RepositoryRoot, key.ScriptName)))
            .Value;
        return JsonDocument.Parse(output);
    }

    private static async Task<string> RunPowerShellJsonScriptCoreAsync(
        string repositoryRoot,
        string scriptName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindPowerShellExecutable(),
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "scripts", scriptName));
        startInfo.ArgumentList.Add("-Json");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start script.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            process.Kill(true);
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new AssertionException(string.Join(
                Environment.NewLine,
                scriptName + " failed.",
                "Exit code: " + process.ExitCode,
                "stdout:",
                output,
                "stderr:",
                error));

        Assert.That(error, Is.Empty);
        return output;
    }

    private static readonly string[] ApprovedAnalyzerRawSmtHotspots = Array.Empty<string>();

    private static readonly string[] ApprovedSymbolicPublicFormulaSurfaceFiles = Array.Empty<string>();

    private static readonly string[] ApprovedSymbolicCompatibilitySurfaceFiles = Array.Empty<string>();

    private static readonly string[] AllowedExportedSmtFormulaTypes = Array.Empty<string>();

    private static string ReadProjectElement(XDocument document, string elementName)
    {
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, elementName, StringComparison.Ordinal))
            .Select(static element => element.Value)
            .FirstOrDefault() ?? string.Empty;
    }

    private static bool PublicMemberReferencesSmtFormula(MemberInfo member)
    {
        switch (member)
        {
            case PropertyInfo property:
                return TypeReferencesSmtFormula(property.PropertyType);
            case FieldInfo field:
                return TypeReferencesSmtFormula(field.FieldType);
            case MethodInfo method:
                return TypeReferencesSmtFormula(method.ReturnType) ||
                       method.GetParameters()
                           .Any(static parameter => TypeReferencesSmtFormula(parameter.ParameterType));
            case ConstructorInfo constructor:
                return constructor.GetParameters()
                    .Any(static parameter => TypeReferencesSmtFormula(parameter.ParameterType));
            default:
                return false;
        }
    }

    private static bool TypeReferencesSmtFormula(Type type)
    {
        if (string.Equals(type.FullName, "SearchLib.Smt.SmtFormula", StringComparison.Ordinal)) return true;

        if (type.HasElementType &&
            type.GetElementType() is { } elementType &&
            TypeReferencesSmtFormula(elementType))
            return true;

        return type.IsGenericType &&
               type.GetGenericArguments().Any(TypeReferencesSmtFormula);
    }

    private static (SemanticModel SemanticModel, IfStatementSyntax IfStatement) CreateSingleIfStatement(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SymbolicReachabilityBranchFacts",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var ifStatement = tree.GetRoot()
            .DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Single();
        return (semanticModel, ifStatement);
    }

    private static IReadOnlyList<(string Path, int MatchCount)> GetAnalyzerRawSmtHotspots(string repositoryRoot)
    {
        var analyzerDirectory = Path.Combine(repositoryRoot, "SharpProof.Analyzer");
        var files = Directory.GetFiles(analyzerDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal) &&
                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                                      StringComparison.Ordinal))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        var hotspots = new List<(string Path, int MatchCount)>();

        foreach (var file in files)
        {
            var source = ReadFileCached(file);
            var matchCount = CountOrdinalOccurrences(source, "CSharpConditionToFormula.");
            foreach (var constructionNeedle in RawSmtConstructionNeedles)
                matchCount += CountOrdinalOccurrences(source, constructionNeedle);

            if (matchCount > 0)
                hotspots.Add((
                    Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'),
                    matchCount));
        }

        return hotspots;
    }

    private static readonly string[] RawSmtConstructionNeedles =
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

    private static int CountOrdinalOccurrences(string source, string needle)
    {
        var count = 0;
        var index = 0;
        while (index < source.Length)
        {
            var found = source.IndexOf(needle, index, StringComparison.Ordinal);
            if (found < 0) return count;

            count++;
            index = found + needle.Length;
        }

        return count;
    }

    private static string ExtractRuntimeHazardFallbackProvenance(string sourceLine)
    {
        const string Prefix = "ir.runtime-hazard.";
        const string Suffix = ".formula-fallback";

        var start = sourceLine.IndexOf(Prefix, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Expected runtime-hazard fallback provenance in: {sourceLine}");

        var end = sourceLine.IndexOf(Suffix, start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), $"Expected runtime-hazard fallback suffix in: {sourceLine}");

        return sourceLine.Substring(start, end + Suffix.Length - start);
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

    private static string ReadRuntimeHazardCandidateSources(string repositoryRoot)
    {
        return string.Concat(
            ReadFileCached(Path.Combine(
                repositoryRoot,
                "SharpProof.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs")),
            Environment.NewLine,
            ReadFileCached(Path.Combine(
                repositoryRoot,
                "SharpProof.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs")));
    }

    private static string FindPowerShellExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "pwsh.exe", "pwsh", "powershell.exe", "powershell" }
            : new[] { "pwsh" };

        foreach (var candidate in candidates)
        {
            var path = FindExecutableOnPath(candidate);
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }

        return OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
    }

    private static string FindExecutableOnPath(string fileName)
    {
        foreach (var directory in
                 (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return string.Empty;
    }
}
