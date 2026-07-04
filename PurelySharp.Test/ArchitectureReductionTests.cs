using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class ArchitectureReductionTests
    {
        [Test]
        public void AnalyzerReachability_DoesNotOpenCodeBranchProofQueries()
        {
            var repositoryRoot = FindRepositoryRoot();
            var analyzerFiles = Directory.GetFiles(
                Path.Combine(repositoryRoot, "PurelySharp.Analyzer"),
                "*.cs",
                SearchOption.AllDirectories);

            var offenders = analyzerFiles
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));

            Assert.That(source, Does.Contain("ClassifyPathFeasibility"));
            Assert.That(source, Does.Contain("PathConditionsImply"));
            Assert.That(source, Does.Contain("ClassifyBranchReachability"));
            Assert.That(source, Does.Contain("CollectPathConditionsAt"));
        }

        [Test]
        public void SymbolicProofService_KeepsFormulaCompatibilityPrivate()
        {
            var repositoryRoot = FindRepositoryRoot();
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var compatibilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicTranslatorCompatibility.cs"));
            var proofServiceSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProofService.cs"));

            Assert.That(reachabilitySource, Does.Not.Contain("ClassifyWithFormulaFallback("));
            Assert.That(reachabilitySource, Does.Not.Contain("new SmtAnalysisService("));
            Assert.That(reachabilitySource, Does.Not.Contain("new PurityProofQuery("));
            Assert.That(reachabilitySource, Does.Contain("ClassifyFormulaReachability(pathConditions, smtAnalysis).Info.Status"));
            Assert.That(reachabilitySource, Does.Contain("ClassifyFormulaConditionTruth(pathConditions, factFormula, smtAnalysis).Info.Status"));
            Assert.That(reachabilitySource, Does.Contain("ClassifyFormulaConditionTruth(pathConditions, formula, smtAnalysis).Info.Status"));
            Assert.That(reachabilitySource, Does.Contain("new SymbolicProofService(smtAnalysis).ClassifyFormula"));
            Assert.That(reachabilitySource, Does.Not.Contain("new SymbolicProofService(smtAnalysis: null)"));
            Assert.That(reachabilitySource, Does.Contain("SymbolicProofService.TryEncodeStatePathConditions(state, out pathConditions)"));
            Assert.That(reachabilitySource, Does.Contain("SymbolicProofService.CreateStateFromFormulaPath(pathConditions, expression)"));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.ClassifyFormulaConditionTruthWithIrFirst("));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.ClassifyFormulaConditionTruthWithIrFallback("));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.TryClassifyFormulaConditionTruthWithIr("));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.TryClassifyFormulaPathFeasibilityWithIr("));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.TryClassifyBranchConditionTruthWithIr("));
            Assert.That(reachabilitySource, Does.Not.Contain("private static SymbolicState CreateStateFromFormulaPath("));
            Assert.That(reachabilitySource, Does.Not.Contain("private static bool TryCreateStateFromFormulaPath("));
            Assert.That(reachabilitySource, Does.Not.Contain("IsNodeReachable("));
            Assert.That(reachabilitySource, Does.Not.Contain("IsNodeUnreachable("));
            Assert.That(proofServiceSource, Does.Contain("internal SymbolicIrProofResult ClassifyFormulaReachability"));
            Assert.That(proofServiceSource, Does.Contain("internal SymbolicIrProofResult ClassifyFormulaConditionTruth"));
            Assert.That(proofServiceSource, Does.Contain("internal PurityProofResult ClassifyFormulaImplication"));
            Assert.That(proofServiceSource, Does.Contain("internal PurityProofResult ClassifyFormulaBranchReachability"));
            Assert.That(proofServiceSource, Does.Contain("internal static SymbolicIrProofResult ClassifyFormulaConditionTruthWithIrFirst("));
            Assert.That(proofServiceSource, Does.Contain("internal static SymbolicIrProofResult ClassifyFormulaConditionTruthWithIrFallback("));
            Assert.That(proofServiceSource, Does.Contain("internal static bool TryClassifyFormulaConditionTruthWithIr("));
            Assert.That(proofServiceSource, Does.Contain("internal static bool TryClassifyFormulaPathFeasibilityWithIr("));
            Assert.That(proofServiceSource, Does.Contain("internal static bool TryClassifyBranchConditionTruthWithIr("));
            Assert.That(proofServiceSource, Does.Contain("public SymbolicIrProofResult ClassifyReachability(SymbolicState state)"));
            Assert.That(proofServiceSource, Does.Contain("public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicFact fact)"));
            Assert.That(proofServiceSource, Does.Contain("public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicCondition condition)"));
            Assert.That(proofServiceSource, Does.Contain("internal static bool TryEncodeStatePathConditions(SymbolicState state, out ImmutableArray<SmtFormula> pathConditions)"));
            Assert.That(proofServiceSource, Does.Contain("internal static bool TryEncodeTermWithPathState("));
            Assert.That(proofServiceSource, Does.Contain("internal static bool TryEncodeConditionWithPathState("));
            Assert.That(proofServiceSource, Does.Contain("internal static SymbolicState AddLoweredFormulaPathCondition("));
            Assert.That(proofServiceSource, Does.Contain("private static bool TryEncodeFactWithPathState("));
            Assert.That(proofServiceSource, Does.Contain("new SymbolicProofService(smtAnalysis: null).TryEncode(state, out pathConditions);"));
            Assert.That(proofServiceSource, Does.Contain("internal static SymbolicState CreateStateFromFormulaPath("));
            Assert.That(proofServiceSource, Does.Contain("internal static bool TryCreateStateFromFormulaPath("));
            Assert.That(proofServiceSource, Does.Contain("\"legacy_path_condition\""));
            Assert.That(proofServiceSource, Does.Contain("\"legacy-path-condition\""));
            Assert.That(proofServiceSource, Does.Contain("private static bool IsTermProvablyNonZero("));
            Assert.That(proofServiceSource, Does.Contain("TryEncodeFactWithPathState(fact, state, s_syntheticProofNode, out var formula)"));
            Assert.That(proofServiceSource, Does.Contain("TryEncodeConditionWithPathState(condition, state, s_syntheticProofNode, out var formula)"));
            Assert.That(proofServiceSource, Does.Contain("ConcurrentDictionary<string, EncodedStateCacheEntry> EncodedStates"));
            Assert.That(proofServiceSource, Does.Contain("state.NormalizedProofKey"));
            Assert.That(proofServiceSource, Does.Contain("EncodeStateUncached(state)"));
            Assert.That(proofServiceSource, Does.Contain("private static SymbolicState NormalizeState(SymbolicState state)"));
            Assert.That(proofServiceSource, Does.Contain("state = NormalizeState(state);"));
            Assert.That(proofServiceSource, Does.Contain("new Dictionary<string, bool>(StringComparer.Ordinal)"));
            Assert.That(proofServiceSource, Does.Contain("IDictionary<string, bool> memo"));
        }

        [Test]
        public void PurityAnalysisEngine_UsesSharedProofBridgesForBranchPathConditions()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));

            Assert.That(source, Does.Contain("return SymbolicProofService.TryEncodeConditionWithPathState("));
            Assert.That(source, Does.Contain("return SymbolicProofService.AddLoweredFormulaPathCondition("));
            Assert.That(source, Does.Not.Contain("return SymbolicIrFormulaEncoder.TryEncode(branchCondition, out formula);"));
            Assert.That(source, Does.Not.Contain("return SymbolicSmtFormulaLowerer.TryLowerCondition("));
        }

        [Test]
        public void ProductionSmtAnalysisServiceConstruction_IsLimitedToOwnedBoundaries()
        {
            var repositoryRoot = FindRepositoryRoot();
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "PurelySharp.Analyzer/Engine/CompilationPurityService.cs",
                "PurelySharp.Symbolic/SymbolicProofService.cs",
                "Tools/PurelySharp.SymbolicCli/Program.cs",
            };
            var offenders = Directory.GetFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}PurelySharp.Test{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}PurelySharp.ToolingTest{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(file => file.Source.Contains("new SmtAnalysisService(", StringComparison.Ordinal) &&
                    !allowed.Contains(file.Path))
                .Select(file => file.Path)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void ProductionDefaultSmtFallback_IsOnlyInSymbolicProofService()
        {
            var repositoryRoot = FindRepositoryRoot();
            var offenders = Directory.GetFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}PurelySharp.Test{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}PurelySharp.ToolingTest{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(file => file.Source.Contains("new SmtAnalysisService(SmtAnalysisOptions.Default)", StringComparison.Ordinal) &&
                    !string.Equals(file.Path, "PurelySharp.Symbolic/SymbolicProofService.cs", StringComparison.Ordinal))
                .Select(file => file.Path)
                .ToArray();
            var proofServiceSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProofService.cs"));

            Assert.That(offenders, Is.Empty);
            Assert.That(proofServiceSource, Does.Contain("only ad hoc SMT service fallback boundary"));
            Assert.That(proofServiceSource, Does.Contain("using var fallback = new SmtAnalysisService(SmtAnalysisOptions.Default);"));
        }

        [Test]
        public void SymbolicRuntimeHazardQueryService_RoutesIrTriggerProofsThroughProofService()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardQueryService.cs"));

            Assert.That(source, Does.Contain("ClassifyStateHazardTrigger("));
            Assert.That(source, Does.Contain("ClassifyStateConditionTruth("));
            Assert.That(source, Does.Contain("ClassifyFormulaConditionTruthWithIrFirst("));
            Assert.That(source, Does.Contain("PathConditionsImplyWithIrFirst("));
            Assert.That(source, Does.Not.Contain("new SymbolicNotCondition(new SymbolicFactCondition(triggerPrecondition))"));
            Assert.That(source, Does.Not.Contain("ClassifyStateImplication("));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaConditionTruth("));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.PathConditionsImply("));
        }

        [Test]
        public void FieldReferenceRule_DelegatesFreshOwnershipClassification()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "FieldReferencePurityRule.cs"));

            Assert.That(source, Does.Contain("OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableReadonlyFieldReference("));
            Assert.That(source, Does.Contain("OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference("));
            Assert.That(source, Does.Not.Contain("internal static class OwnedFreshMutableObjectClassifier"));
            Assert.That(source, Does.Not.Contain("private static bool IsOwnedFreshMutableObjectReference("));
            Assert.That(source, Does.Not.Contain("private static bool HasStableFreshMutableObjectValue("));
        }

        [Test]
        public void AssignmentRule_DelegatesFreshOwnershipClassification()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var classifierSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "OwnedFreshMutableObjectClassifier.cs"));
            var returnSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "ReturnStatementPurityRule.cs"));

            Assert.That(classifierSource, Does.Contain("PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(localSymbol, state)"));
            Assert.That(classifierSource, Does.Contain("IsAssignedFreshMutableObjectOnAllPaths("));
            Assert.That(classifierSource, Does.Contain("AnalyzeFreshMutableAssignments("));
            Assert.That(classifierSource, Does.Contain("return IsOwnedFreshMutableLocal(localReference.Local, initializerSyntax, semanticModel, currentState: null, visitedLocals);"));
            Assert.That(returnSource, Does.Contain("OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableLocal("));
            Assert.That(returnSource, Does.Contain("\"symbolic_fresh_mutable_object_return\""));
        }

        [Test]
        public void AnalyzerOwnedArrayFlowCaptures_ProjectOwnershipFactsIntoPathState()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));

            Assert.That(source, Does.Contain("WithOwnedArrayFlowCapture(flowCaptureOperation.Id, flowCaptureOperation.Syntax)"));
            Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateFreshOwned("));
            Assert.That(source, Does.Contain("RemoveOwnedArrayFlowCaptureFacts(PathState, id)"));
            Assert.That(source, Does.Contain("SymbolicResourceLifetimeAtom lifetime => Equals(lifetime.Resource, term)"));
        }

        [Test]
        public void AnalyzerOwnedLocalArrays_ProjectValueOwnershipFactsIntoPathState()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));

            Assert.That(source, Does.Contain("AddFreshMutableObjectFacts("));
            Assert.That(source, Does.Contain("RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type)"));
            Assert.That(source, Does.Contain("SymbolicOwnershipFactFactory.CreateFreshOwnedValue("));
            Assert.That(source, Does.Contain("\"analyzer.object.acquire\""));
            Assert.That(source, Does.Contain("\"evidence.object.acquire\""));
        }

        [Test]
        public void AnalyzerDisposeInvocations_ProjectDisposalFactsIntoPathState()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));

            Assert.That(source, Does.Contain("nextState = AddDisposeInvocationFacts(nextState, invocationOperation, currentState);"));
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
        public void AnalyzerReturnedOwnedResources_ProjectReturnedOwnershipFacts()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var engineSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var assignmentSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "AssignmentPurityRule.cs"));
            var invocationSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            Assert.That(assignmentSource, Does.Contain("HasSymbolicBorrowFactForLocal(local, currentState, SymbolicBorrowKind.Mutable)"));
            Assert.That(assignmentSource, Does.Contain("TryCreateMutableBorrowConflictEvidence("));
            Assert.That(assignmentSource, Does.Contain("earlyBorrowConflictEvidence"));
            Assert.That(assignmentSource.IndexOf("earlyBorrowConflictEvidence", StringComparison.Ordinal), Is.LessThan(assignmentSource.IndexOf("IsAssignmentTargetPure(", StringComparison.Ordinal)));
            Assert.That(invocationSource, Does.Contain("TryCreateMutableBorrowConflictEvidence("));
            Assert.That(invocationSource, Does.Contain("context.SemanticModel"));
            Assert.That(invocationSource, Does.Contain("context.CancellationToken"));
            Assert.That(engineSource, Does.Contain("\"analyzer.borrow.mutable-conflict\""));
        }

        [Test]
        public void ReturnRule_ConsumesSymbolicOwnershipFactsForOwnedArrayEscape()
        {
            var repositoryRoot = FindRepositoryRoot();
            var engineSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var returnSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "DelegateCreationPurityRule.cs"));

            Assert.That(source, Does.Contain("TryFindCapturedFreshMutableObject("));
            Assert.That(source, Does.Contain("TryFindCapturedFreshMutableObjectBySyntax("));
            Assert.That(source, Does.Contain("TryResolveTrackedSymbol(unwrappedOperation, currentState) is ILocalSymbol resolvedLocal"));
            Assert.That(source, Does.Contain("RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(resolvedLocal.Type)"));
            Assert.That(source, Does.Contain("HasSymbolicOwnedFactForSymbol(resolvedLocal, currentState)"));
            Assert.That(source, Does.Contain("HasStableFreshMutableObjectInitializer(localReferenceFallback.Local"));
        }

        [Test]
        public void ReturnRule_DelegatesReturnedClosureArrayCaptureToDelegateRule()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var engineSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var returnSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var engineSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var assignmentSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "MethodInvocationPurityRule.cs"));
            var propertySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "PropertyReferencePurityRule.cs"));
            var fieldSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "FieldReferencePurityRule.cs"));
            var engineSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            Assert.That(engineSource, Does.Contain("WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax"));
            Assert.That(engineSource, Does.Contain("statement is DoStatementSyntax doStatement"));
            Assert.That(engineSource, Does.Contain("HasDisposedResourceFactBefore("));
            Assert.That(engineSource, Does.Contain("HasDisposedResourceFactForTermBefore("));
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
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
        public void RuntimeHazardTriggers_CreateIrPreconditionTriggersBeforeFormulaProjection()
        {
            var repositoryRoot = FindRepositoryRoot();
            var candidateSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var triggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var helperIndex = triggerSource.IndexOf("private static bool TryEncodeIrExceptionPreconditionTrigger", StringComparison.Ordinal);
            var helperEndIndex = triggerSource.IndexOf("private static bool TryCreateReferenceNullCondition", StringComparison.Ordinal);
            var helperSource = triggerSource.Substring(helperIndex, helperEndIndex - helperIndex);
            var directThrowIndex = triggerSource.IndexOf("private static bool TryCreateDirectThrowTrigger", StringComparison.Ordinal);
            var directThrowEndIndex = triggerSource.IndexOf("private static bool TryCreateDivideByZeroTrigger", StringComparison.Ordinal);
            var directThrowSource = triggerSource.Substring(directThrowIndex, directThrowEndIndex - directThrowIndex);

            Assert.That(candidateSource, Does.Contain("internal static bool TryCreate(SymbolicFact irPrecondition"));
            Assert.That(helperSource, Does.Contain("RuntimeHazardTrigger.TryCreate(precondition, out trigger)"));
            Assert.That(helperSource, Does.Not.Contain("SymbolicIrFormulaEncoder.TryEncode(precondition"));
            Assert.That(directThrowSource, Does.Contain("RuntimeHazardTrigger.TryCreate(precondition, out trigger)"));
            Assert.That(directThrowSource, Does.Not.Contain("SymbolicIrFormulaEncoder.TryEncode(precondition"));
        }

        [Test]
        public void ExecutionVisibility_UsesSymbolicReachabilityForConditionProofs()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "ExecutionVisibility.cs"));

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.IsForInitialEntryConditionAlwaysFalse"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.EvaluateKnownConditionTruth"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.PathConditionsAreUnsatisfiableWithIrFirst"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.IsFormulaAlwaysFalseWithIrFirst"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.IsFormulaAlwaysTrueWithIrFirst"));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryClassifyFormulaConditionTruthWithIr"));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.TryClassifyFormulaPathFeasibilityWithIr"));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.IsFormulaAlwaysFalse("));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.IsFormulaAlwaysTrue("));
        }

        [Test]
        public void ForInitialEntryReachability_UsesSymbolicStateBeforeFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var methodStart = source.IndexOf("internal static bool IsForInitialEntryConditionAlwaysFalse", StringComparison.Ordinal);
            var methodEnd = source.IndexOf("internal static IEnumerable<SmtFormula> CollectForInitializerFacts", StringComparison.Ordinal);
            var methodSource = source.Substring(methodStart, methodEnd - methodStart);
            var stateIndex = methodSource.IndexOf("SymbolicProgramPointFacts.CollectForInitialEntryState", StringComparison.Ordinal);
            var proofIndex = methodSource.IndexOf("ClassifyStateConditionTruth(initialEntryState", StringComparison.Ordinal);
            var formulaIndex = methodSource.IndexOf("var pathConditions = CollectPathConditionsAt", StringComparison.Ordinal);

            Assert.That(stateIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(proofIndex, Is.GreaterThan(stateIndex));
            Assert.That(formulaIndex, Is.GreaterThan(proofIndex));
        }

        [Test]
        public void SymbolicPublicSurface_HidesImplementationTranslators()
        {
            Assert.That(typeof(CSharpConditionToFormula).IsPublic, Is.False);
            Assert.That(typeof(SymbolicQueryService).IsPublic, Is.True);
            Assert.That(typeof(SmtAnalysisService).IsPublic, Is.True);
            Assert.That(typeof(SmtAnalysisOptions).IsPublic, Is.True);
        }

        [Test]
        public void SymbolicIr_KeepsSmtConstructionBehindEncoderBoundary()
        {
            var repositoryRoot = FindRepositoryRoot();
            var irDirectory = Path.Combine(repositoryRoot, "PurelySharp.Symbolic", "Ir");
            var offenders = Directory.GetFiles(irDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(static path => !path.EndsWith("SymbolicIrFormulaEncoder.cs", StringComparison.Ordinal))
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
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
                    Source = File.ReadAllText(path),
                })
                .Where(static file =>
                    file.Source.Contains("Microsoft.CodeAnalysis", StringComparison.Ordinal) ||
                    file.Source.Contains("PurelySharp.Symbolic", StringComparison.Ordinal) ||
                    file.Source.Contains("PurelySharp.Analyzer", StringComparison.Ordinal))
                .Select(static file => file.Path)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void SymbolicIrLowerer_KeepsStringAndRegexLoweringsInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var stringSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Strings.cs"));
            var knownApiSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.KnownApis.cs"));

            Assert.That(coreSource, Does.Contain("internal static partial class SymbolicIrLowerer"));
            Assert.That(knownApiSource, Does.Contain("TryLowerStringStaticValueMember(memberSymbol, out term)"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerRegexIsMatchInvocation"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringPredicateInvocation"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringEqualityCondition"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateStringEqualityCondition"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringStaticValueMember"));
            Assert.That(coreSource, Does.Not.Contain("internal static bool TryCreateStringContentReferenceTerm"));
            Assert.That(coreSource, Does.Not.Contain("private static bool IsSystemStringType"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerRegexIsMatchInvocation"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerStringPredicateInvocation"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerStringEqualityCondition"));
            Assert.That(stringSource, Does.Contain("private static bool TryCreateStringEqualityCondition"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerStringStaticValueMember"));
            Assert.That(stringSource, Does.Contain("internal static bool TryCreateStringContentReferenceTerm"));
            Assert.That(stringSource, Does.Contain("private static bool IsSystemStringType"));
        }

        [Test]
        public void SymbolicIrLowerer_KeepsObjectLoweringsInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var objectSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Objects.cs"));
            var knownApiSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var patternSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var tupleSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Tuples.cs"));
            var memberSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var nullableSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Nullable.cs"));
            var memberSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Members.cs"));

            Assert.That(memberSource, Does.Contain("TryLowerNullableHasValueTerm(memberAccess.Expression"));
            Assert.That(memberSource, Does.Contain("TryLowerNullableValueTerm(memberAccess.Expression"));
            Assert.That(coreSource, Does.Not.Contain("public static bool TryLowerNullableHasValueTerm"));
            Assert.That(coreSource, Does.Not.Contain("public static bool TryLowerNullableValueTerm"));
            Assert.That(nullableSource, Does.Contain("public static bool TryLowerNullableHasValueTerm"));
            Assert.That(nullableSource, Does.Contain("public static bool TryLowerNullableValueTerm"));
            Assert.That(nullableSource, Does.Contain("private static bool TryLowerNullableGetValueOrDefaultInvocation"));
            Assert.That(nullableSource, Does.Contain("TryLowerArrayTotalLengthTerm(conditionalAccess.Expression"));
        }

        [Test]
        public void SymbolicIrLowerer_KeepsIndexingLoweringsInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var indexingSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Indexing.cs"));
            var knownApisSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.KnownApis.cs"));

            Assert.That(coreSource, Does.Contain("TryLowerElementAccessTerm(elementAccess"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerElementAccessTerm"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryGetElementAccessValueKind"));
            Assert.That(coreSource, Does.Not.Contain("public static bool TryLowerArrayDimensionLengthTerm"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerArrayGetLengthInvocation"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerArrayBoundInvocation"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerArrayTotalLengthTerm"));
            Assert.That(coreSource, Does.Not.Contain("internal static bool TryCreateBuiltInLengthReferenceTerm"));
            Assert.That(indexingSource, Does.Contain("private static bool TryLowerElementAccessTerm"));
            Assert.That(indexingSource, Does.Contain("private static bool TryGetElementAccessValueKind"));
            Assert.That(indexingSource, Does.Contain("private static bool TryLowerArrayGetLengthInvocation"));
            Assert.That(indexingSource, Does.Contain("private static bool TryLowerArrayBoundInvocation"));
            Assert.That(indexingSource, Does.Contain("public static bool TryLowerArrayDimensionLengthTerm"));
            Assert.That(indexingSource, Does.Contain("private static bool TryLowerArrayTotalLengthTerm"));
            Assert.That(indexingSource, Does.Contain("internal static bool TryCreateBuiltInLengthReferenceTerm"));
            Assert.That(indexingSource, Does.Contain("TryCreateArrayTotalLengthReferenceTerm(reference, multiDimensionalArray, out term)"));
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var conversionSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Conversions.cs"));

            Assert.That(coreSource, Does.Contain("TryLowerSupportedConversionTerm(expression"));
            Assert.That(coreSource, Does.Contain("TryLowerIdentityPreservingAsTerm(asExpression"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerIdentityPreservingAsTerm"));
            Assert.That(coreSource, Does.Not.Contain("private static bool IsIdentityPreservingReferenceConversion"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerSupportedConversionTerm"));
            Assert.That(conversionSource, Does.Contain("private static bool TryLowerIdentityPreservingAsTerm"));
            Assert.That(conversionSource, Does.Contain("private static bool IsIdentityPreservingReferenceConversion"));
            Assert.That(conversionSource, Does.Contain("private static bool TryLowerSupportedConversionTerm"));
        }

        [Test]
        public void SymbolicIrLowerer_KeepsMemberLoweringsInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var memberSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Members.cs"));

            Assert.That(coreSource, Does.Contain("TryLowerMemberTerm(memberAccess"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerMemberTerm"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryGetInstanceMemberValueKind"));
            Assert.That(coreSource, Does.Not.Contain("private static bool IsBuiltInSpanOrMemoryType"));
            Assert.That(memberSource, Does.Contain("private static bool TryLowerMemberTerm"));
            Assert.That(memberSource, Does.Contain("private static bool TryGetInstanceMemberValueKind"));
            Assert.That(memberSource, Does.Contain("private static bool IsBuiltInSpanOrMemoryType"));
            Assert.That(memberSource, Does.Contain("new SymbolicLengthTerm"));
            Assert.That(memberSource, Does.Contain("new SymbolicCountTerm"));
            Assert.That(memberSource, Does.Contain("new SymbolicIntegerConstantTerm(arrayType.Rank)"));
            Assert.That(memberSource, Does.Contain("TryLowerArrayTotalLengthTerm(memberAccess.Expression"));
        }

        [Test]
        public void SymbolicIrLowerer_KeepsNumericLoweringsInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var numericSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Numerics.cs"));
            var knownApiSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var typeSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var operatorSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var knownApiSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.KnownApis.cs"));
            var memberSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Members.cs"));

            Assert.That(coreSource, Does.Contain("TryLowerKnownApiInvocation(invocation, context, out condition)"));
            Assert.That(coreSource, Does.Contain("TryLowerKnownApiInvocationTerm(invocation, context, out term)"));
            Assert.That(memberSource, Does.Contain("TryLowerKnownStaticValueMember(memberAccess, context, out term)"));
            Assert.That(coreSource, Does.Not.Contain("KnownApiLowerings ="));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerKnownApiInvocation("));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerKnownApiInvocationTerm("));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerKnownStaticValueMember("));
            Assert.That(knownApiSource, Does.Contain("KnownApiLowerings ="));
            Assert.That(knownApiSource, Does.Contain("KnownApiTermLowerings"));
            Assert.That(knownApiSource, Does.Contain("private static bool TryLowerKnownApiInvocation("));
            Assert.That(knownApiSource, Does.Contain("private static bool TryLowerKnownApiInvocationTerm("));
            Assert.That(knownApiSource, Does.Contain("private static bool TryLowerKnownStaticValueMember("));
        }

        [Test]
        public void SymbolicIrLowerer_KeepsConditionFactoriesInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var conditionSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.cs"));
            var utilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
        public void RuntimeHazardDivideByZero_UsesIrExceptionPreconditionTriggerBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateIrExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DivideByZero"));
            Assert.That(source, Does.Contain("TryCreateNumericZeroCondition("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateExpressionNumericZeroComparison("));
            Assert.That(source, Does.Contain("ir.runtime-hazard.divide-by-zero.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.divide-by-zero.formula-fallback"));
            Assert.That(source, Does.Contain("CreateIrPreferredFormulaBackedExceptionPreconditionTrigger"));
            Assert.That(source, Does.Not.Contain("trigger = new RuntimeHazardTrigger(formula);"));
            Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(binaryExpression.Right"));
            Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(assignment.Right"));
        }

        [Test]
        public void RuntimeHazardDivideByZero_UsesIrZeroConditionBeforeFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var helperIndex = source.IndexOf("TryCreateNumericZeroCondition(\r\n                    divisor,", StringComparison.Ordinal);
            if (helperIndex < 0)
            {
                helperIndex = source.IndexOf("TryCreateNumericZeroCondition(\n                    divisor,", StringComparison.Ordinal);
            }

            var fallbackIndex = source.IndexOf("TryTranslateZeroCondition(divisor", StringComparison.Ordinal);
            var translatedIndex = source.IndexOf("ir.runtime-hazard.divide-by-zero.translated", StringComparison.Ordinal);
            var formulaFallbackIndex = source.IndexOf("\"ir.runtime-hazard.divide-by-zero.formula-fallback\"", StringComparison.Ordinal);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(fallbackIndex, Is.GreaterThan(helperIndex));
            Assert.That(translatedIndex, Is.GreaterThan(fallbackIndex));
            Assert.That(formulaFallbackIndex, Is.GreaterThan(translatedIndex));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.CreateIntegerZeroCondition("));
            Assert.That(source, Does.Contain("new SymbolicConstantCondition(true)"));
            Assert.That(source, Does.Contain("new SymbolicConstantCondition(false)"));
            Assert.That(source, Does.Contain("TryCreateDecimalZeroComparableTerm("));
            Assert.That(source, Does.Contain("SymbolicFactFactory.GetSmtVariableName(symbol)"));
        }

        [Test]
        public void RuntimeHazardSimpleIndexing_UsesIrBoundsPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateIrElementAccessOutOfRangeTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentOutOfRange"));
            Assert.That(source, Does.Contain("new SymbolicBoundsAtom"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange("));
            Assert.That(source, Does.Contain("ir.runtime-hazard.index.out-of-range.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.index.out-of-range.formula-fallback"));
            Assert.That(source, Does.Contain("CreateIrPreferredFormulaBackedExceptionPreconditionTrigger"));
            Assert.That(source, Does.Not.Contain("trigger = new RuntimeHazardTrigger(new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula));"));
        }

        [Test]
        public void RuntimeHazardIndexFallback_PrefersLoweredIrTriggerBeforeFormulaBackedTrigger()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var translatedIndex = source.IndexOf("ir.runtime-hazard.index.out-of-range.translated", StringComparison.Ordinal);
            var fallbackIndex = source.IndexOf("\"ir.runtime-hazard.index.out-of-range.formula-fallback\"", StringComparison.Ordinal);

            Assert.That(translatedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(fallbackIndex, Is.GreaterThan(translatedIndex));
        }

        [Test]
        public void AnalyzerExceptionSites_UseSharedIrFirstElementAccessRangeHelper()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "ExceptionFlowAnalyzer.ExceptionSites.cs"));
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var lowererSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Indexing.cs"));

            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange("));
            Assert.That(reachabilitySource, Does.Contain("TryCreateIrBuiltInElementAccessInRangeCondition("));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrLowerer.TryCreateBuiltInElementAccessInRangeCondition("));
            Assert.That(reachabilitySource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateSubsequenceInRangeCondition("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.CreateSubsequenceInRangeFormula("));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrLowerer.TryCreateSubsequenceInRangeCondition("));
            Assert.That(reachabilitySource, Does.Contain("SmtFormulaFactory.CreateSubsequenceInRangeFormula("));
            Assert.That(lowererSource, Does.Contain("public static bool TryCreateSubsequenceInRangeCondition("));
        }

        [Test]
        public void ElementAccessRangeHelper_UsesIrMultidimensionalBoundsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var lowererSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Indexing.cs"));
            var helperStart = reachabilitySource.IndexOf(
                "internal static bool TryCreateBuiltInElementAccessInRangeCondition(",
                StringComparison.Ordinal);
            var helperEnd = reachabilitySource.IndexOf(
                "private static bool TryCreateIrBuiltInElementAccessInRangeCondition(",
                StringComparison.Ordinal);
            var helperSource = reachabilitySource.Substring(helperStart, helperEnd - helperStart);
            var irIndex = helperSource.IndexOf(
                "TryCreateIrBuiltInElementAccessInRangeCondition(",
                StringComparison.Ordinal);
            var fallbackIndex = helperSource.IndexOf(
                "CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange(",
                StringComparison.Ordinal);

            Assert.That(helperStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperEnd, Is.GreaterThan(helperStart));
            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(fallbackIndex, Is.EqualTo(-1));
            Assert.That(reachabilitySource, Does.Not.Contain("TryCreateIrBuiltInElementAccessLengthTerm("));
            Assert.That(reachabilitySource, Does.Contain("TryCreateIrMultidimensionalArrayElementAccessInRangeCondition("));
            Assert.That(lowererSource, Does.Contain("public static bool TryCreateBuiltInElementAccessInRangeCondition("));
            Assert.That(lowererSource, Does.Contain("TryResolveBuiltInRangeLengthShape("));
            Assert.That(lowererSource, Does.Contain("TryResolveBuiltInIndexLengthShape("));
            Assert.That(lowererSource, Does.Contain("ApplyWellFormedPrecondition("));
            Assert.That(lowererSource, Does.Contain("RequiresNonNegativeValue"));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrLowerer.TryCreateArrayElementBoundsCondition("));
            Assert.That(reachabilitySource, Does.Contain("ir.element-access.multidimensional-bounds.in-range"));
            Assert.That(lowererSource, Does.Contain("public static bool TryCreateArrayElementBoundsCondition("));
            Assert.That(lowererSource, Does.Contain("TryLowerArrayDimensionLengthTerm(arrayExpression, dimension, context, out var length)"));
            Assert.That(lowererSource, Does.Contain("new SymbolicBoundsAtom("));
        }

        [Test]
        public void ElementAccessLengthTerms_AreLoweredBySharedIrLowerer()
        {
            var repositoryRoot = FindRepositoryRoot();
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var lowererSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Indexing.cs"));

            Assert.That(lowererSource, Does.Contain("public static bool TryLowerBuiltInLengthTerm("));
            Assert.That(lowererSource, Does.Contain("TryLowerDirectRangeAccessResultLengthTerm("));
            Assert.That(lowererSource, Does.Contain("TryLowerBuiltInViewResultLengthTerm("));
            Assert.That(lowererSource, Does.Contain("TryLowerBuiltInSliceInvocationResultLengthTerm("));
            Assert.That(lowererSource, Does.Contain("TryLowerMemoryExtensionsViewResultLengthTerm("));
            Assert.That(lowererSource, Does.Contain("TryResolveBuiltInRangeLengthShape("));
            Assert.That(lowererSource, Does.Contain("TryResolveAssignedRangeLengthShape("));
            Assert.That(lowererSource, Does.Contain("TryResolveBuiltInIndexLengthShape("));
            Assert.That(lowererSource, Does.Contain("TryLowerStringInvocationResultLengthTerm("));
            Assert.That(lowererSource, Does.Contain("internal static bool TryCreateBuiltInLengthReferenceTerm("));
            Assert.That(lowererSource, Does.Contain("type is not IArrayTypeSymbol &&"));
            Assert.That(lowererSource, Does.Contain("HasCountBackedIntIndexer(type)"));
            Assert.That(lowererSource, Does.Contain("term = new SymbolicCountTerm(reference);"));
            Assert.That(lowererSource, Does.Contain("TryCreateStringContentReferenceTerm(reference, out var stringContent)"));
            Assert.That(lowererSource, Does.Contain("CreateLengthTerm(reference, out term)"));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrLowerer.TryLowerBuiltInLengthTerm(valueExpression, context, out var term)"));
            Assert.That(irTriggerSource, Does.Contain("SymbolicIrLowerer.TryLowerBuiltInLengthTerm(elementAccess.Expression, context, out length)"));
            Assert.That(irTriggerSource, Does.Contain("new SymbolicCountTerm(receiver)"));
        }

        [Test]
        public void RuntimeHazardSlicing_PreservesIrExceptionPreconditionWhenFormulaLowers()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var lowererSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Indexing.cs"));

            Assert.That(source, Does.Contain("TryCreateSlicingArgumentOutOfRangeCandidate"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryCreateSubsequenceInRangeCondition("));
            Assert.That(source, Does.Contain("TryEncodeIrExceptionPreconditionTrigger("));
            Assert.That(source, Does.Contain("ir.runtime-hazard.slicing.in-range"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateSubsequenceInRangeCondition("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.CreateSubsequenceInRangeFormula"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentOutOfRange"));
            Assert.That(source, Does.Contain("CreateFormulaBackedExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.slicing.argument-out-of-range"));
            Assert.That(lowererSource, Does.Contain("provenance + \".count-within-remaining-length\""));
            Assert.That(lowererSource, Does.Contain("provenance + \".addition-does-not-overflow\""));
        }

        [Test]
        public void RuntimeHazardArrayGetValue_UsesIrBoundsPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var exceptionSitesSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "ExceptionFlowAnalyzer.ExceptionSites.cs"));
            var exceptionQuerySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "ExceptionFlowQuery.cs"));
            var lowererSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Indexing.cs"));

            Assert.That(source, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.bounds.in-range"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.multidimensional-bounds.in-range"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.multidimensional-index-out-of-range"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryCreateArrayElementBoundsCondition("));
            Assert.That(lowererSource, Does.Contain("new SymbolicBoundsAtom("));
            Assert.That(lowererSource, Does.Contain("TryLowerArrayDimensionLengthTerm(arrayExpression, dimension, context, out var length)"));
            Assert.That(coreSource, Does.Contain("SymbolicReachabilityService.TryCreateArrayGetValueIndexesInRangeFormula("));
            Assert.That(coreSource, Does.Not.Contain("CSharpSmtFormulaTranslator."));
            Assert.That(reachabilitySource, Does.Contain("private static bool TryTranslateArrayGetValueDimensionLength("));
            Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.index-out-of-range.fallback"));
            Assert.That(coreSource, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger("));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
            Assert.That(exceptionSitesSource, Does.Contain("GetDefiniteArrayGetValueIndexOutOfRangeNodes("));
            Assert.That(exceptionSitesSource, Does.Contain("SymbolicReachabilityService.TryCreateArrayGetValueIndexesInRangeFormula("));
            Assert.That(exceptionSitesSource, Does.Contain("TryGetArrayGetValueRuntimeArrayType("));
            Assert.That(exceptionQuerySource, Does.Contain("ExceptionFlowAnalyzer.GetDefiniteArrayGetValueIndexOutOfRangeNodes("));
            Assert.That(exceptionQuerySource, Does.Contain("ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange"));
            Assert.That(exceptionQuerySource, Does.Contain("ExceptionSources.ArrayGetValue"));
        }

        [Test]
        public void RuntimeHazardMultidimensionalElementAccess_UsesIrBoundsPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var lowererSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Ir",
                "SymbolicIrLowerer.Indexing.cs"));

            Assert.That(source, Does.Contain("TryCreateIrMultidimensionalArrayElementAccessOutOfRangeTrigger"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.index.multidimensional-bounds.in-range"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.index.multidimensional-out-of-range"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryCreateArrayElementBoundsCondition("));
            Assert.That(lowererSource, Does.Contain("public static bool TryCreateArrayElementBoundsCondition("));
            Assert.That(lowererSource, Does.Contain("new SymbolicBoundsAtom("));
            Assert.That(irTriggerSource, Does.Contain("GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is IArrayTypeSymbol { Rank: > 1 }"));
            Assert.That(irTriggerSource, Does.Contain("return TryCreateIrMultidimensionalArrayElementAccessOutOfRangeTrigger("));
        }

        [Test]
        public void RuntimeHazardNegativeLengths_UseIrExceptionPreconditionsBeforeLegacyFallback()
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
            Assert.That(source, Does.Contain("TryTranslateNegativeCondition(lengthExpression"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateNegativeLengthTrigger("));
            Assert.That(source, Does.Contain("CreateIrPreferredFormulaBackedExceptionPreconditionTrigger("));
            Assert.That(source, Does.Contain("provenance + \".formula-fallback\""));
            Assert.That(source, Does.Not.Contain("if (!TryTranslateNegativeCondition(lengthExpression"));
            Assert.That(source, Does.Not.Contain("trigger = new RuntimeHazardTrigger(formula);"));
        }

        [Test]
        public void RuntimeHazardNegativeLengthFallback_PrefersLoweredIrTriggerBeforeFormulaBackedTrigger()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var helperIndex = source.IndexOf("CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(\r\n                    lengthExpression,", StringComparison.Ordinal);
            if (helperIndex < 0)
            {
                helperIndex = source.IndexOf("CreateIrPreferredFormulaBackedExceptionPreconditionTrigger(\n                    lengthExpression,", StringComparison.Ordinal);
            }

            var translatedProvenanceIndex = source.IndexOf("provenance + \".translated\"", StringComparison.Ordinal);
            var fallbackProvenanceIndex = source.IndexOf("provenance + \".formula-fallback\"", StringComparison.Ordinal);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(translatedProvenanceIndex, Is.GreaterThan(helperIndex));
            Assert.That(fallbackProvenanceIndex, Is.GreaterThan(translatedProvenanceIndex));
        }

        [Test]
        public void RuntimeHazardCheckedIntegralOutOfRange_UsesIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var compatibilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicTranslatorCompatibility.cs"));
            var lowererSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateIntegerUnaryInRangeCondition("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateIntegerIncrementOrDecrementInRangeCondition("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateIntegerInRangeCondition("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateIntegerBinaryInRangeCondition("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateSignedDivisionOverflowCondition("));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.CreateSignedDivisionOverflowCondition("));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.binary-overflow.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.signed-division-overflow.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.unary-minus-overflow.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.increment-overflow.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.decrement-overflow.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.compound-assignment-overflow.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.compound-signed-division-overflow.translated"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-conversion.overflow.translated"));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator."));
            Assert.That(source, Does.Not.Contain("CreateIntegralOutOfRangeFormula("));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrLowerer.CreateIntegerInRangeCondition("));
            Assert.That(reachabilitySource, Does.Contain("\"ir.integer.in-range\""));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrLowerer.TryGetBinaryTermOperator(smtOperator, out var binaryOperator)"));
            Assert.That(reachabilitySource, Does.Contain("\"ir.integer.binary.in-range\""));
            Assert.That(reachabilitySource, Does.Contain("smtOperator == SmtIntegerUnaryOperator.Negate"));
            Assert.That(reachabilitySource, Does.Contain("\"ir.integer.unary.in-range\""));
            Assert.That(reachabilitySource, Does.Contain("binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract"));
            Assert.That(reachabilitySource, Does.Contain("\"ir.integer.update.in-range\""));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrLowerer.CreateSignedDivisionOverflowCondition("));
            Assert.That(reachabilitySource, Does.Contain("\"ir.integer.signed-division-overflow\""));
            Assert.That(lowererSource, Does.Contain("public static SymbolicCondition CreateIntegerInRangeCondition("));
            Assert.That(lowererSource, Does.Contain("public static SymbolicCondition CreateSignedDivisionOverflowCondition("));
            Assert.That(lowererSource, Does.Contain("provenance + \".lower-bound\""));
            Assert.That(lowererSource, Does.Contain("provenance + \".upper-bound\""));
            Assert.That(lowererSource, Does.Contain("provenance + \".left-min\""));
            Assert.That(lowererSource, Does.Contain("provenance + \".right-minus-one\""));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.binary-overflow.formula-fallback"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.signed-division-overflow.formula-fallback"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.unary-minus-overflow.formula-fallback"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.increment-overflow.formula-fallback"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.decrement-overflow.formula-fallback"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.compound-assignment-overflow.formula-fallback"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-integral.compound-signed-division-overflow.formula-fallback"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.checked-conversion.overflow.formula-fallback"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.CheckedOverflow"));
            Assert.That(source, Does.Contain("CreateFormulaBackedExceptionPreconditionTrigger"));
        }

        [Test]
        public void RuntimeHazardSignedDivisionOverflowFallback_PrefersLoweredIrTriggerBeforeFormulaBackedTrigger()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var translatedIndex = source.IndexOf("ir.runtime-hazard.checked-integral.signed-division-overflow.translated", StringComparison.Ordinal);
            var fallbackIndex = source.IndexOf("\"ir.runtime-hazard.checked-integral.signed-division-overflow.formula-fallback\"", StringComparison.Ordinal);
            var compoundTranslatedIndex = source.IndexOf("ir.runtime-hazard.checked-integral.compound-signed-division-overflow.translated", StringComparison.Ordinal);
            var compoundFallbackIndex = source.IndexOf("\"ir.runtime-hazard.checked-integral.compound-signed-division-overflow.formula-fallback\"", StringComparison.Ordinal);

            Assert.That(translatedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(fallbackIndex, Is.GreaterThan(translatedIndex));
            Assert.That(compoundTranslatedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(compoundFallbackIndex, Is.GreaterThan(compoundTranslatedIndex));
            Assert.That(source, Does.Contain("TryCreateIrExceptionPreconditionTriggerFromFormula("));
        }

        [Test]
        public void RuntimeHazardCheckedOverflowRangeFallbacks_PreferLoweredIrTriggerBeforeFormulaBackedTrigger()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var provenances = new[]
            {
                "ir.runtime-hazard.checked-integral.binary-overflow",
                "ir.runtime-hazard.checked-integral.unary-minus-overflow",
                "ir.runtime-hazard.checked-integral.increment-overflow",
                "ir.runtime-hazard.checked-integral.decrement-overflow",
                "ir.runtime-hazard.checked-integral.compound-assignment-overflow",
                "ir.runtime-hazard.checked-conversion.overflow",
            };

            foreach (var provenance in provenances)
            {
                var translatedIndex = source.IndexOf(provenance + ".translated", StringComparison.Ordinal);
                var fallbackIndex = source.IndexOf("\"" + provenance + ".formula-fallback\"", StringComparison.Ordinal);

                Assert.That(translatedIndex, Is.GreaterThanOrEqualTo(0), provenance);
                Assert.That(fallbackIndex, Is.GreaterThan(translatedIndex), provenance);
            }

            Assert.That(source, Does.Contain("TryCreateIrExceptionPreconditionTriggerFromFormula("));
        }

        [Test]
        public void RuntimeHazardStableNullDereferences_UseIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateNullDereferenceTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullDereference"));
            Assert.That(source, Does.Not.Contain("IsStableIrReferenceSubject"));
            Assert.That(source, Does.Contain("TryCreateIrRelationalExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("TryTranslateNullCondition(receiver"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateReferenceNullComparison("));
            Assert.That(source, Does.Contain("\"ir.runtime-hazard.null-dereference.formula-fallback\""));
            Assert.That(source, Does.Contain("!TryCreateNullDereferenceTrigger(receiver"));
        }

        [Test]
        public void RuntimeHazardUnboxNull_UsesIrExceptionPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateUnboxNullTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.UnboxNull"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.unbox-null"));
            Assert.That(source, Does.Contain("TryTranslateNullCondition(expression"));
            Assert.That(source, Does.Contain("\"ir.runtime-hazard.unbox-null.formula-fallback\""));
            Assert.That(source, Does.Contain("TryCreateUnboxNullTrigger("));
        }

        [Test]
        public void RuntimeHazardStableArgumentNull_UsesIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateArgumentNullTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentNull"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.argument-null"));
            Assert.That(source, Does.Not.Contain("IsStableIrReferenceSubject"));
            Assert.That(source, Does.Contain("TryTranslateNullCondition(expression"));
            Assert.That(source, Does.Contain("\"ir.runtime-hazard.argument-null.formula-fallback\""));
            Assert.That(source, Does.Contain("!TryCreateArgumentNullTrigger(expression"));
        }

        [Test]
        public void RuntimeHazardNullLikeFallbacks_PreferLoweredIrTriggerBeforeFormulaBackedTrigger()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var provenances = new[]
            {
                "ir.runtime-hazard.null-dereference",
                "ir.runtime-hazard.unbox-null",
                "ir.runtime-hazard.argument-null",
                "ir.runtime-hazard.nullable-value.without-value",
                "ir.runtime-hazard.invalid-cast",
                "ir.runtime-hazard.dynamic-null-binding",
            };

            foreach (var provenance in provenances)
            {
                var translatedIndex = source.IndexOf(provenance + ".translated", StringComparison.Ordinal);
                var fallbackIndex = source.IndexOf("\"" + provenance + ".formula-fallback\"", StringComparison.Ordinal);

                Assert.That(translatedIndex, Is.GreaterThanOrEqualTo(0), provenance);
                Assert.That(fallbackIndex, Is.GreaterThan(translatedIndex), provenance);
            }

            Assert.That(source, Does.Contain("CreateIrPreferredFormulaBackedExceptionPreconditionTrigger("));
        }

        [Test]
        public void RuntimeHazardNullableValue_UsesIrExceptionPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateNullableValueWithoutValueTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullableValueWithoutValue"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryLowerNullableHasValueTerm"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateNullableHasValueCondition("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateNullableHasValue("));
            Assert.That(source, Does.Contain("ir.runtime-hazard.nullable-value.without-value.formula-fallback"));
            Assert.That(source, Does.Contain("CreateFormulaBackedExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("!TryCreateNullableValueWithoutValueTrigger("));
        }

        [Test]
        public void RuntimeHazardInvalidReferenceCast_UsesIrTypeTestPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

            Assert.That(source, Does.Contain("TryCreateRuntimeReferenceInvalidCastTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.InvalidCast"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.invalid-cast.non-null"));
            Assert.That(source, Does.Contain("new SymbolicTypeTestAtom"));
            Assert.That(source, Does.Contain("SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey"));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateRuntimeTypeTestCondition("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateRuntimeTypeTestFormula("));
            Assert.That(source, Does.Contain("TryCreateReferenceNullCondition("));
            Assert.That(source, Does.Contain("\"ir.runtime-hazard.reference.non-null.guard\""));
            Assert.That(source, Does.Contain("CreateFormulaBackedExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("\"ir.runtime-hazard.invalid-cast.formula-fallback\""));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateRuntimeReferenceCastMismatchTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateRuntimeReferenceInvalidCastTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateReferenceNullCondition"));
        }

        [Test]
        public void RuntimeHazardDirectThrow_UsesIrExceptionPreconditionTrigger()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

            Assert.That(source, Does.Contain("TryCreateDirectThrowTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DirectThrow"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.direct-throw"));
            Assert.That(coreSource, Does.Contain("var trigger = !isRethrow"));
            Assert.That(coreSource, Does.Contain("TryCreateDirectThrowTrigger(throwNode"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateDirectThrowTrigger"));
        }

        [Test]
        public void RuntimeHazardSwitchExpressionNoMatch_PreservesIrExceptionPreconditionWhenLowerable()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateSwitchExpressionNoMatchCandidate"));
            Assert.That(source, Does.Contain("CreateFormulaBackedExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch"));
            Assert.That(source, Does.Contain("SymbolicSmtFormulaLowerer.TryLowerCondition"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.switch-expression.no-match"));
            Assert.That(source, Does.Contain("ExceptionTypes.SwitchExpressionException"));
            Assert.That(source, Does.Contain("ExceptionCategories.DefiniteSwitchExpressionNoMatch"));
        }

        [Test]
        public void RuntimeHazardDynamicNullBinding_UsesIrExceptionPreconditionBeforeFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateDynamicNullBindingTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DynamicNullBinding"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.dynamic-null-binding"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.dynamic-null-binding.formula-fallback"));
            Assert.That(source, Does.Contain("TryCreateOptionalReferenceSubject"));
            Assert.That(source, Does.Not.Contain("!TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var trigger)"));
        }

        [Test]
        public void RuntimeHazardIrTriggerBridge_LivesInDedicatedPartial()
        {
            var repositoryRoot = FindRepositoryRoot();
            var coreSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.cs"));
            var irTriggerSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));

            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateIrRelationalExceptionPreconditionTrigger"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateIrElementAccessOutOfRangeTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrRelationalExceptionPreconditionTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrElementAccessOutOfRangeTrigger"));
        }

        [Test]
        public void RuntimeHazardReferenceNullHelper_ReturnsIrConditionBeforeFormulaEncoding()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"));
            var helperIndex = source.IndexOf("private static bool TryCreateReferenceNullCondition", StringComparison.Ordinal);
            var helperEndIndex = source.IndexOf("\r\n    }\r\n}", helperIndex, StringComparison.Ordinal);
            if (helperEndIndex < 0)
            {
                helperEndIndex = source.IndexOf("\n    }\n}", helperIndex, StringComparison.Ordinal);
            }

            var helperSource = source.Substring(helperIndex, helperEndIndex - helperIndex);

            Assert.That(helperSource, Does.Contain("out SymbolicCondition condition"));
            Assert.That(helperSource, Does.Not.Contain("out SmtFormula trigger"));
            Assert.That(helperSource, Does.Not.Contain("SymbolicIrFormulaEncoder.TryEncode("));
            Assert.That(helperSource, Does.Contain("new SymbolicConstantCondition(true)"));
            Assert.That(helperSource, Does.Contain("SymbolicIrLowerer.CreateReferenceNullCondition("));
            Assert.That(helperSource, Does.Not.Contain("new SymbolicRelationAtom("));
        }

        [Test]
        public void RuntimeHazardCandidates_DelegateLegacyFormulaFallbacksToReachability()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var compatibilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicTranslatorCompatibility.cs"));

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator."));
            Assert.That(source, Does.Contain("SymbolicReachabilityService."));
            Assert.That(reachabilitySource, Does.Contain("SymbolicTranslatorCompatibility."));
            Assert.That(reachabilitySource, Does.Not.Contain("CSharpSmtFormulaTranslator."));
            Assert.That(compatibilitySource, Does.Contain("CSharpSmtFormulaTranslator."));
        }

        [Test]
        public void RuntimeExceptionEvidenceFacts_AcceptsAllSharedCategories()
        {
            var rejectedCategories = typeof(SymbolicRuntimeExceptionFacts.ExceptionCategories)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)
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
                "New analyzer raw-SMT hotspots must lower to PurelySharp.Symbolic.Ir and use shared proof services.");
        }

        [Test]
        public async Task RawSmtHotspotInventoryScript_ReportsApprovedAnalyzerMigrationHotspots()
        {
            var repositoryRoot = FindRepositoryRoot();
            using var document = await RunPowerShellJsonScriptAsync(
                repositoryRoot,
                "Get-PurelySharpRawSmtHotspots.ps1");
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
                    Text = usage.GetProperty("text").GetString() ?? string.Empty,
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

            Assert.That(root.GetProperty("symbolicTranslatorShimUsageCount").GetInt32(), Is.EqualTo(6));
            Assert.That(root.GetProperty("symbolicTranslatorShimFamilyCount").GetInt32(), Is.EqualTo(4));
            Assert.That(
                symbolicTranslatorShimCountsByPath,
                Is.EquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["PurelySharp.Symbolic/SymbolicTranslatorCompatibility.cs"] = 6,
                }));
            Assert.That(
                symbolicTranslatorShimCountsByText,
                Is.EquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["return CSharpSmtFormulaTranslator.TryTranslate("] = 1,
                    ["return CSharpSmtFormulaTranslator.TryTranslateValue("] = 1,
                    ["return CSharpSmtFormulaTranslator.TryCollectBranchAssumptions("] = 1,
                    ["return CSharpSmtFormulaTranslator.TryCollectDomainFacts("] = 1,
                    ["return CSharpSmtFormulaTranslator.TryCollectPatternBindingFacts("] = 1,
                    ["return CSharpSmtFormulaTranslator.TryTranslatePattern("] = 1,
                }));
            Assert.That(
                symbolicTranslatorShimFamilyCounts,
                Is.EquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["branch-facts"] = 2,
                    ["condition"] = 1,
                    ["pattern"] = 2,
                    ["value"] = 1,
                }));
            Assert.That(root.GetProperty("irKnownApiLoweringCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("irKnownApiConditionLoweringCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("irKnownApiTermLoweringCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(
                root.GetProperty("irKnownApiLoweringLocations")
                    .EnumerateArray()
                    .Select(static location => location.GetProperty("path").GetString() ?? string.Empty)
                    .All(static path => path.StartsWith("PurelySharp.Symbolic/Ir/", StringComparison.Ordinal)),
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
                    Text = location.GetProperty("text").GetString() ?? string.Empty,
                })
                .ToArray();
            var runtimeHazardFormulaFallbackCountsByPath = runtimeHazardFormulaFallbackLocations
                .GroupBy(static location => location.Path, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
            var runtimeHazardFormulaFallbackCountsByProvenance = runtimeHazardFormulaFallbackLocations
                .Select(static location => ExtractRuntimeHazardFallbackProvenance(location.Text))
                .GroupBy(static provenance => provenance, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

            Assert.That(root.GetProperty("runtimeHazardFormulaFallbackCount").GetInt32(), Is.EqualTo(17));
            Assert.That(
                runtimeHazardFormulaFallbackLocations.All(static location =>
                    location.Path.StartsWith("PurelySharp.Symbolic/", StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                runtimeHazardFormulaFallbackCountsByPath,
                Is.EquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["PurelySharp.Symbolic/SymbolicRuntimeHazardCandidateFactory.cs"] = 9,
                    ["PurelySharp.Symbolic/SymbolicRuntimeHazardCandidateFactory.IrTriggers.cs"] = 8,
                }));
            Assert.That(
                runtimeHazardFormulaFallbackCountsByProvenance,
                Is.EquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["ir.runtime-hazard.argument-null.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-conversion.overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-integral.binary-overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-integral.compound-assignment-overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-integral.compound-signed-division-overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-integral.decrement-overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-integral.increment-overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-integral.signed-division-overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.checked-integral.unary-minus-overflow.formula-fallback"] = 1,
                    ["ir.runtime-hazard.divide-by-zero.formula-fallback"] = 1,
                    ["ir.runtime-hazard.dynamic-null-binding.formula-fallback"] = 1,
                    ["ir.runtime-hazard.index.out-of-range.formula-fallback"] = 1,
                    ["ir.runtime-hazard.invalid-cast.formula-fallback"] = 2,
                    ["ir.runtime-hazard.nullable-value.without-value.formula-fallback"] = 1,
                    ["ir.runtime-hazard.null-dereference.formula-fallback"] = 1,
                    ["ir.runtime-hazard.unbox-null.formula-fallback"] = 1,
                }));
        }

        [Test]
        public async Task ProductionMetricsScript_TracksSymbolicPlatformPressureFiles()
        {
            var repositoryRoot = FindRepositoryRoot();
            using var document = await RunPowerShellJsonScriptAsync(
                repositoryRoot,
                "Get-PurelySharpProductionMetrics.ps1");
            var root = document.RootElement;
            var modules = root.GetProperty("modules")
                .EnumerateArray()
                .Select(static module => module.GetProperty("module").GetString() ?? string.Empty)
                .ToArray();
            var largestFiles = root.GetProperty("largestFiles")
                .EnumerateArray()
                .Select(static file => file.GetProperty("path").GetString() ?? string.Empty)
                .ToArray();

            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("totalFiles").GetInt32(), Is.GreaterThan(100));
            Assert.That(root.GetProperty("totalLines").GetInt32(), Is.GreaterThan(100000));
            Assert.That(modules, Does.Contain("Symbolic"));
            Assert.That(modules, Does.Contain("Analyzer"));
            Assert.That(modules, Does.Contain("Tools"));
            Assert.That(modules, Does.Contain("SearchLib"));
            Assert.That(modules, Does.Not.Contain("Other"));
            Assert.That(largestFiles, Does.Contain("PurelySharp.Symbolic/SymbolicProgramPointFacts.cs"));
            Assert.That(largestFiles, Does.Contain("PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs"));
            Assert.That(largestFiles, Does.Contain("PurelySharp.Symbolic/SymbolicSourceQueryService.cs"));
            Assert.That(largestFiles, Does.Contain("PurelySharp.Analyzer/Engine/Rules/MethodInvocationPurityRule.cs"));
            Assert.That(largestFiles, Does.Contain("Tools/PurelySharp.EffectSummary/Program.cs"));
        }

        [Test]
        public void PackageMetadata_UsesPlatformPositioningWithoutBreakingCompatibilityIdentity()
        {
            var repositoryRoot = FindRepositoryRoot();
            var packageMetadata = XDocument.Load(Path.Combine(
                repositoryRoot,
                "PurelySharp.Package",
                "PurelySharp.Package.csproj"));
            var attributesMetadata = XDocument.Load(Path.Combine(
                repositoryRoot,
                "PurelySharp.Attributes",
                "PurelySharp.Attributes.csproj"));
            var vsixManifest = XDocument.Load(Path.Combine(
                repositoryRoot,
                "PurelySharp.Vsix",
                "source.extension.vsixmanifest"));
            var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));

            Assert.That(ReadProjectElement(packageMetadata, "PackageId"), Is.EqualTo("PurelySharp"));
            Assert.That(ReadProjectElement(packageMetadata, "Title"), Is.EqualTo("SharpProof"));
            Assert.That(ReadProjectElement(packageMetadata, "Description"), Does.Contain("SharpProof bounded symbolic C# analysis platform"));
            Assert.That(ReadProjectElement(packageMetadata, "Description"), Does.Contain("PurelySharp compatibility package"));
            Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("SharpProof"));
            Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("SymbolicAnalysis"));
            Assert.That(ReadProjectElement(packageMetadata, "PackageTags"), Does.Contain("RuntimeHazards"));
            Assert.That(ReadProjectElement(attributesMetadata, "PackageId"), Is.EqualTo("PurelySharp.Attributes"));
            Assert.That(ReadProjectElement(attributesMetadata, "Title"), Is.EqualTo("SharpProof Attributes"));
            Assert.That(ReadProjectElement(attributesMetadata, "Description"), Does.Contain("SharpProof symbolic C# analysis"));
            Assert.That(ReadProjectElement(attributesMetadata, "PackageTags"), Does.Contain("SharpProof"));
            Assert.That(ReadProjectElement(attributesMetadata, "PackageTags"), Does.Contain("SymbolicAnalysis"));
            Assert.That(vsixManifest.Descendants().Single(element => element.Name.LocalName == "DisplayName").Value, Is.EqualTo("SharpProof"));
            Assert.That(vsixManifest.Descendants().Single(element => element.Name.LocalName == "Description").Value, Does.Contain("SharpProof bounded symbolic C# analysis"));
            Assert.That(readme, Does.Contain("SharpProof"));
            Assert.That(readme, Does.Contain("previously called PurelySharp"));
            Assert.That(readme, Does.Contain("package, namespace,"));
            Assert.That(readme, Does.Contain("diagnostic, configuration, additional-file, and summary-artifact identity"));
            Assert.That(readme, Does.Contain("summary-artifact identity"));
            Assert.That(readme, Does.Contain("remains `PurelySharp` for compatibility"));
        }

        [Test]
        public async Task SymbolicPublicFormulaSurface_IsLimitedToApprovedMigrationFiles()
        {
            var repositoryRoot = FindRepositoryRoot();
            using var document = await RunPowerShellJsonScriptAsync(
                repositoryRoot,
                "Get-PurelySharpRawSmtHotspots.ps1");
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
                "Get-PurelySharpRawSmtHotspots.ps1");
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicSourceQueryService.cs"));

            Assert.That(source, Does.Not.Contain("IReadOnlyList<SmtFormula> PathConditions => Analysis.PathConditions"));
            Assert.That(source, Does.Not.Contain("PathConditions => Invariant.Conditions"));
            Assert.That(source, Does.Not.Contain("result.PathConditions"));
            Assert.That(source, Does.Not.Contain("point.PathConditions"));
            Assert.That(source, Does.Not.Contain("SmtFormula MergedInvariant => Analysis.MergedInvariant"));
            Assert.That(source, Does.Not.Contain("internal SmtFormula MergedInvariant { get; }"));
            Assert.That(source, Does.Not.Contain("HasSmtFormula"));
            Assert.That(source, Does.Contain("public IReadOnlyList<string> Facts => Analysis.Facts"));
            Assert.That(source, Does.Contain("public IReadOnlyList<SymbolicFactInfo> SymbolicFacts => SymbolicFactInfo.FromState(Analysis.PathState)"));
        }

        [Test]
        public void SymbolicInvariantSnapshot_DoesNotStoreRawFormulaResults()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicInvariantService.cs"));

            Assert.That(source, Does.Contain("SymbolicCondition condition"));
            Assert.That(source, Does.Contain("ClassifyStateConditionTruth("));
            Assert.That(source, Does.Contain("ClassifyFormulaConditionTruthWithIrFallback("));
            Assert.That(source, Does.Not.Contain("ClassifyImplication("));
            Assert.That(source, Does.Contain("SymbolicIrFormulaEncoder.TryEncode(condition"));
            Assert.That(source, Does.Not.Contain("SmtFormula condition,"));
            Assert.That(source, Does.Contain("internal SyntaxNode SourceNode { get; }"));
            Assert.That(source, Does.Not.Contain("analysis.SourceNode != null"));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaConditionTruth("));
            Assert.That(source, Does.Contain("\"invariant.implication\""));
        }

        [Test]
        public void SymbolicSourceQueryResultConstruction_UsesSinglePathStateBridge()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicSourceQueryService.cs"));
            var queryApiSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicQueryApi.cs"));
            var combinedSource = source + "\n" + queryApiSource;

            Assert.That(source, Does.Contain("private static SymbolicSourceQueryResult CreateSourceQueryResult("));
            Assert.That(source.Split("new SymbolicSourceQueryResult(", StringSplitOptions.None).Length - 1, Is.EqualTo(1));
            Assert.That(source, Does.Contain("var mergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(query.Analysis.PathConditions);"));
            Assert.That(source, Does.Contain("SymbolicInvariantResult.FromFormulas("));
            Assert.That(combinedSource.Split("new SymbolicSourceQueryResult(", StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            Assert.That(combinedSource, Does.Not.Contain("FromPathConditions("));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\r\n                query.Analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\n                query.Analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\r\n                analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\n                analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("IReadOnlyList<SmtFormula>? pathConditions = null"));
            Assert.That(source, Does.Contain("SymbolicFactInfo.FromState(query.Analysis.PathState)"));
        }

        [Test]
        public void SymbolicSourceQueryService_DelegatesSpeculativeConditionFormulaFallbacks()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicSourceQueryService.cs"));
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator."));
            Assert.That(source, Does.Contain("ClassifyStateConditionTruth("));
            Assert.That(source, Does.Not.Contain("ClassifyStateImplication("));
            Assert.That(source, Does.Not.Contain("new SymbolicNotCondition(symbolicCondition)"));
            Assert.That(source, Does.Contain("ClassifyFormulaConditionTruthWithIrFallback("));
            Assert.That(source, Does.Contain("ClassifyStateFeasibilityWithFormulaFallback("));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaReachability("));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaConditionTruth("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryTranslateConditionFormula("));
            Assert.That(source, Does.Contain("\"source.query.condition\""));
            Assert.That(reachabilitySource, Does.Contain("TryTranslateConditionFormula("));
            Assert.That(reachabilitySource, Does.Contain("SymbolicTranslatorCompatibility.TryTranslateConditionLegacy("));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.ClassifyFormulaConditionTruthWithIrFallback("));
        }

        [Test]
        public void SwitchPathConditionBuilder_DelegatesLegacyPatternFallbacksThroughReachability()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Smt",
                "SwitchPathConditionBuilder.cs"));

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCollectDomainFacts("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCollectBranchAssumptions("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslate("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslatePattern("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCollectPatternBindingFacts("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryTranslatePattern("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCollectPatternBindingFacts("));
        }

        [Test]
        public void SymbolicReachabilityService_TranslatesPatternsThroughIrBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf("internal static bool TryTranslatePattern(", StringComparison.Ordinal);
            var helperEndIndex = source.IndexOf("internal static void AddUnsatisfiablePathCondition(", StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, helperEndIndex - helperIndex);
            var irValueIndex = helperSource.IndexOf("SymbolicSmtFormulaLowerer.TryLowerTerm(value, out var symbolicValue)", StringComparison.Ordinal);
            var irPatternIndex = helperSource.IndexOf("SymbolicIrLowerer.TryLowerPatternCondition(", StringComparison.Ordinal);
            var legacyIndex = helperSource.IndexOf("SymbolicTranslatorCompatibility.TryTranslatePatternLegacy(", StringComparison.Ordinal);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperEndIndex, Is.GreaterThan(helperIndex));
            Assert.That(irValueIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irPatternIndex, Is.GreaterThan(irValueIndex));
            Assert.That(legacyIndex, Is.GreaterThan(irPatternIndex));
            Assert.That(helperSource, Does.Contain("CanUseIrPatternTranslation(pattern)"));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncode(symbolicCondition, out var encodedFormula)"));
            Assert.That(helperSource, Does.Contain("formula = encodedFormula;"));
        }

        [Test]
        public void LegacyTranslatorReferencesOutsideShim_AreForbidden()
        {
            var repositoryRoot = FindRepositoryRoot();
            var symbolicDirectory = Path.Combine(repositoryRoot, "PurelySharp.Symbolic");
            var offenders = Directory.GetFiles(symbolicDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(static file =>
                    !file.Path.StartsWith("PurelySharp.Symbolic/Smt/CSharpConditionToFormula", StringComparison.Ordinal) &&
                    file.Path != "PurelySharp.Symbolic/Smt/CSharpSmtFormulaTranslator.cs" &&
                    file.Source.Contains("CSharpConditionToFormula.", StringComparison.Ordinal))
                .Select(static file => file.Path)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void LegacyTranslatorShimUsage_IsIsolatedToCompatibilityBoundary()
        {
            var repositoryRoot = FindRepositoryRoot();
            var symbolicDirectory = Path.Combine(repositoryRoot, "PurelySharp.Symbolic");
            var offenders = Directory.GetFiles(symbolicDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(static file =>
                    !file.Path.StartsWith("PurelySharp.Symbolic/Smt/CSharpConditionToFormula", StringComparison.Ordinal) &&
                    file.Path != "PurelySharp.Symbolic/Smt/CSharpSmtFormulaTranslator.cs" &&
                    file.Path != "PurelySharp.Symbolic/SymbolicTranslatorCompatibility.cs" &&
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
            };

            var offenders = dtoTypes
                .SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Select(member => new
                    {
                        Type = type,
                        Member = member,
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
                .Where(static type => !AllowedExportedSmtFormulaTypes.Contains(type.FullName ?? string.Empty, StringComparer.Ordinal))
                .SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Select(member => new
                    {
                        Type = type,
                        Member = member,
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
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
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
                    new SymbolicVariableTerm("x", SearchLib.Smt.SmtValueKind.Int),
                    new SymbolicIntegerConstantTerm(1)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x == 1"),
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
                    new SymbolicBinaryCondition(SymbolicConditionOperator.And, yPositive, zPositive)),
            });
            var right = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    new SymbolicBinaryCondition(SymbolicConditionOperator.And, zPositive, xPositive),
                    yPositive),
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
                xPositive,
            });
            var withIdentities = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicNotCondition(new SymbolicNotCondition(new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    new SymbolicConstantCondition(true),
                    new SymbolicBinaryCondition(
                        SymbolicConditionOperator.Or,
                        xPositive,
                        new SymbolicConstantCondition(false))))),
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
                xPositive,
            });
            var duplicatedAnd = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, xPositive, xPositive),
            });
            var duplicatedOr = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(SymbolicConditionOperator.Or, xPositive, xPositive),
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
                    new SymbolicNotCondition(xPositive)),
            });
            var tautology = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    xPositive,
                    new SymbolicNotCondition(xPositive)),
            });

            Assert.That(contradiction.NormalizedProofKey, Is.EqualTo(new SymbolicState(pathConditions: new[] { new SymbolicConstantCondition(false) }).NormalizedProofKey));
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
                    falseStringFact),
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
                xPositive,
            });
            var orAbsorption = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    xPositive,
                    new SymbolicBinaryCondition(
                        SymbolicConditionOperator.And,
                        xPositive,
                        yPositive)),
            });
            var andAbsorption = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    xPositive,
                    new SymbolicBinaryCondition(
                        SymbolicConditionOperator.Or,
                        xPositive,
                        yPositive)),
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
                new SymbolicNotCondition(new SymbolicFactCondition(fact)),
            });
            var negativeFactCondition = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicFactCondition(fact.Negate()),
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
                new SymbolicNotCondition(new SymbolicNotCondition(new SymbolicNotCondition(new SymbolicFactCondition(xFact)))),
            });
            var negatedOr = new SymbolicState(
                new[] { xFact },
                new SymbolicCondition[]
                {
                    new SymbolicNotCondition(new SymbolicBinaryCondition(
                        SymbolicConditionOperator.Or,
                        new SymbolicFactCondition(xFact),
                        new SymbolicFactCondition(yFact))),
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
                    new SymbolicConstantCondition(true),
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

            Assert.That(new SymbolicState(new[] { greaterThan }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { lessThan }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { equal }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { equalFlipped }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { notEqual }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { equal.Negate() }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { lessThanOrEqual }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { greaterThan.Negate() }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { greaterThanOrEqual }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { lessThanZero.Negate() }).NormalizedProofKey));
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
                new SymbolicFactCondition(lessThanSelf),
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
                new SymbolicFactCondition(integerGreaterThan),
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
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("items[1]"),
                "test.in-range");
            var negativeIndex = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    indexNegative,
                    lengthThree,
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("items[-1]"),
                "test.negative-index");
            var upperOutOfRange = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    lengthThree,
                    lengthThree,
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("items[3]"),
                "test.upper-out-of-range");
            var lowerOnly = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    indexOne,
                    lengthThree,
                    IncludeLowerBound: true,
                    IncludeUpperBound: false),
                SyntaxFactory.ParseExpression("index >= 0"),
                "test.lower-only");
            var upperOnly = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    lengthThree,
                    lengthThree,
                    IncludeLowerBound: false,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("index < length"),
                "test.upper-only");
            var computedLengthInRange = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    indexOne,
                    literalLength,
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("\"abc\"[1]"),
                "test.computed-length-in-range");
            var computedLengthOutOfRange = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    literalLength,
                    literalLength,
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("\"abc\"[3]"),
                "test.computed-length-out-of-range");
            var empty = new SymbolicState();
            var tautologicalBounds = new SymbolicState(new[] { inRange, lowerOnly, computedLengthInRange });
            var negativeContradiction = new SymbolicState(new[] { negativeIndex });
            var upperContradiction = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicFactCondition(upperOnly),
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
                new SymbolicFactCondition(doesNotContain),
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
                new SymbolicFactCondition(negatedIdentity),
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
                new SymbolicFactCondition(falseLengthRelation),
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

            Assert.That(new SymbolicState(new[] { lessThan }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { greaterThan }).NormalizedProofKey));
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

            Assert.That(new SymbolicState(new[] { trueSelected }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { direct }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { falseSelected }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { direct }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { identicalBranches }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { direct }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { exactIntegerConditional, exactStringConditional, exactBooleanConditional, trueFactSelectedConditional, falseFactSelectedConditional }).NormalizedProofKey, Is.EqualTo(empty.NormalizedProofKey));
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

            Assert.That(new SymbolicState(new[] { addLeft }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { addRight }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { subtractLeft }).NormalizedProofKey, Is.Not.EqualTo(new SymbolicState(new[] { subtractRight }).NormalizedProofKey));
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

            Assert.That(new SymbolicState(new[] { addLeftAssociated }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { addRightAssociated }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { multiplyLeftAssociated }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { multiplyRightAssociated }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { subtractLeftAssociated }).NormalizedProofKey, Is.Not.EqualTo(new SymbolicState(new[] { subtractRightAssociated }).NormalizedProofKey));
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

            Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { addZero }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { multiplyOne }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { subtractZero }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { direct }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { divideOne }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { multiplyZero }).NormalizedProofKey, Is.Not.EqualTo(new SymbolicState(new[] { directZero }).NormalizedProofKey));
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

            Assert.That(new SymbolicState(new[] { leftAssociated }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { rightAssociated }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { leftAssociated }).NormalizedProofKey, Is.Not.EqualTo(new SymbolicState(new[] { reordered }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { directValue }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { emptyPrefix }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { directValue }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { emptySuffix }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { adjacentLiteralConcat }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { adjacentLiteralDirect }).NormalizedProofKey));
            Assert.That(new SymbolicState(new[] { emptyOnlyConcat }).NormalizedProofKey, Is.EqualTo(new SymbolicState(new[] { directEmpty }).NormalizedProofKey));
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
                    new SymbolicBoundsAtom(index, length, IncludeLowerBound: true, IncludeUpperBound: true),
                    SyntaxFactory.ParseExpression("value[index]"),
                    "test.bounds"),
                SymbolicFact.Exact(
                    new SymbolicTypeTestAtom(value, "System.String"),
                    SyntaxFactory.ParseExpression("value is string"),
                    "test.type"),
                SymbolicFact.Exact(
                    new SymbolicOwnershipAtom(resource, Escaped: false),
                    SyntaxFactory.ParseExpression("resource"),
                    "test.ownership"),
                SymbolicFact.Exact(
                    new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Owned),
                    SyntaxFactory.ParseExpression("resource"),
                    "test.resource"),
                SymbolicFact.Exact(
                    new SymbolicExceptionPreconditionAtom(SymbolicExceptionPreconditionKind.IndexOutOfRange, value, trigger),
                    SyntaxFactory.ParseExpression("value[index]"),
                    "test.exception"),
            });
            var right = new SymbolicState(new[]
            {
                SymbolicFact.Exact(
                    new SymbolicExceptionPreconditionAtom(SymbolicExceptionPreconditionKind.IndexOutOfRange, value, trigger),
                    SyntaxFactory.ParseExpression("value[index]"),
                    "other.exception"),
                SymbolicFact.Exact(
                    new SymbolicResourceLifetimeAtom(resource, SymbolicResourceLifetimeState.Owned),
                    SyntaxFactory.ParseExpression("resource"),
                    "other.resource"),
                SymbolicFact.Exact(
                    new SymbolicOwnershipAtom(resource, Escaped: false),
                    SyntaxFactory.ParseExpression("resource"),
                    "other.ownership"),
                SymbolicFact.Exact(
                    new SymbolicTypeTestAtom(value, "System.String"),
                    SyntaxFactory.ParseExpression("value is string"),
                    "other.type"),
                SymbolicFact.Exact(
                    new SymbolicBoundsAtom(index, length, IncludeLowerBound: true, IncludeUpperBound: true),
                    SyntaxFactory.ParseExpression("value[index]"),
                    "other.bounds"),
                SymbolicFact.Exact(
                    new SymbolicStringPredicateAtom(SymbolicStringPredicateKind.StartsWith, text, prefix),
                    SyntaxFactory.ParseExpression("value.StartsWith(\"pre\")"),
                    "other.string"),
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
                mayAlias: true,
                SyntaxFactory.ParseExpression("alias"),
                "test.source-first");
            var targetFirst = SymbolicOwnershipFactFactory.CreateAlias(
                alias,
                owner,
                mayAlias: true,
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
                mayAlias: true,
                SyntaxFactory.ParseExpression("owner"),
                "test.alias.self");
            var cannotAliasSelf = SymbolicOwnershipFactFactory.CreateAlias(
                owner,
                owner,
                mayAlias: false,
                SyntaxFactory.ParseExpression("owner"),
                "test.alias.not-self");
            var empty = new SymbolicState();
            var tautologicalFact = new SymbolicState(new[] { mayAliasSelf });
            var contradictoryFact = new SymbolicState(new[] { cannotAliasSelf });
            var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicFactCondition(cannotAliasSelf),
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
                new SymbolicOwnershipAtom(resource, Escaped: false),
                source,
                "test.owned");
            var escaped = SymbolicFact.Exact(
                new SymbolicOwnershipAtom(resource, Escaped: true),
                source,
                "test.escaped");
            var approximateEscaped = escaped with { Confidence = SymbolicFactConfidence.Approximate };

            var contradictoryFacts = new SymbolicState(new[] { owned, escaped });
            var contradictoryPath = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicFactCondition(owned),
                new SymbolicFactCondition(escaped),
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
                new SymbolicFactCondition(notDisposed),
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
                new SymbolicFactCondition(released),
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
                new SymbolicFactCondition(nullIsString),
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
                    yPositive)),
            });
            var orOfNegatedOperands = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    new SymbolicNotCondition(xPositive),
                    new SymbolicNotCondition(yPositive)),
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
                new SymbolicConstantCondition(false),
            });
            var andFalseState = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, xPositive, new SymbolicConstantCondition(false)),
            });
            var trueState = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicConstantCondition(true),
            });
            var orTrueState = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicBinaryCondition(SymbolicConditionOperator.Or, xPositive, new SymbolicConstantCondition(true)),
            });
            var notTrueState = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicNotCondition(new SymbolicConstantCondition(true)),
            });
            var notFalseState = new SymbolicState(pathConditions: new SymbolicCondition[]
            {
                new SymbolicNotCondition(new SymbolicConstantCondition(false)),
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
                    new SymbolicNotCondition(new SymbolicFactCondition(fact))),
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
                new SymbolicNotCondition(tautology),
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
                Polarity: true,
                SymbolicFactConfidence.Unsupported,
                "test.unsupported-confidence",
                syntax.Span,
                Symbol: null,
                EvidenceKey: null);
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
                Polarity: true,
                SymbolicFactConfidence.Approximate,
                "test.approximate-confidence",
                syntax.Span,
                Symbol: null,
                EvidenceKey: null);
            var state = new SymbolicState(new[] { approximateFalseFact });
            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var reachability = SymbolicReachabilityService.ClassifyStateFeasibility(state, smtAnalysis);
            var implication = SymbolicReachabilityService.ClassifyStateImplication(
                new SymbolicState(),
                approximateFalseFact.Negate(),
                smtAnalysis);

            Assert.That(state.IsContradictory, Is.False);
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
                    new SymbolicConstantCondition(false),
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
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x > 0"),
                "test.state");
            var branchFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.LessThanOrEqual,
                    x,
                    new SymbolicIntegerConstantTerm(0)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x <= 0"),
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
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x > 0"),
                "test.state");
            var conditionFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.LessThanOrEqual,
                    x,
                    new SymbolicIntegerConstantTerm(0)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x <= 0"),
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
                mayAlias: true,
                SyntaxFactory.ParseExpression("alias"),
                "test.alias.stored");
            var queried = SymbolicOwnershipFactFactory.CreateAlias(
                alias,
                owner,
                mayAlias: true,
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
                mayAlias: true,
                SyntaxFactory.ParseExpression("owner"),
                "test.alias.self");
            var cannotAliasSelf = SymbolicOwnershipFactFactory.CreateAlias(
                owner,
                owner,
                mayAlias: false,
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
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("items[-1]"),
                "test.false-bounds-target");
            var trueComputedBoundsTarget = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    new SymbolicIntegerConstantTerm(2),
                    new SymbolicLengthTerm(new SymbolicStringConcatTerm(
                        new SymbolicStringConstantTerm("ab"),
                        new SymbolicStringConstantTerm("c"))),
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                SyntaxFactory.ParseExpression("\"abc\"[2]"),
                "test.true-computed-bounds-target");
            var falseComputedBoundsTarget = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    new SymbolicIntegerConstantTerm(3),
                    new SymbolicLengthTerm(new SymbolicStringConcatTerm(
                        new SymbolicStringConstantTerm("ab"),
                        new SymbolicStringConstantTerm("c"))),
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
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
                    new SymbolicFactCondition(yPositive)),
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
                new SymbolicNotCondition(condition),
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
                    smtAnalysis: null));
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
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("divisor != 0"),
                "test.state");
            var triggerCondition = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    divisor,
                    zero),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("divisor == 0"),
                "test.trigger.condition"));
            var triggerPrecondition = SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(
                    SymbolicExceptionPreconditionKind.DivideByZero,
                    divisor,
                    triggerCondition),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("10 / divisor"),
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
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("divisor == 0"),
                "test.zero");
            var triggerCondition = new SymbolicFactCondition(equalsZero);
            var triggerPrecondition = SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(
                    SymbolicExceptionPreconditionKind.DivideByZero,
                    divisor,
                    triggerCondition),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("10 / divisor"),
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
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("divisor == 0"),
                "test.zero");
            var triggerCondition = new SymbolicFactCondition(equalsZero);
            var triggerPrecondition = SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(
                    SymbolicExceptionPreconditionKind.DivideByZero,
                    divisor,
                    triggerCondition),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("10 / divisor"),
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
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("divisor > 1"),
                "test.positive");
            var equalsZero = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    divisor,
                    zero),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("divisor == 0"),
                "test.zero");
            var triggerPrecondition = SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(
                    SymbolicExceptionPreconditionKind.DivideByZero,
                    divisor,
                    new SymbolicFactCondition(equalsZero)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("10 / divisor"),
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
                new SymbolicFreshnessAtom(new SymbolicVariableTerm("value", SearchLib.Smt.SmtValueKind.Reference)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("value"),
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
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("resource"),
                "test.metadata");
            var supportedFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThan,
                    x,
                    new SymbolicIntegerConstantTerm(0)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x > 0"),
                "test.supported");
            var impossibleBranch = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.LessThan,
                    x,
                    new SymbolicIntegerConstantTerm(0)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x < 0"),
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
                maxPathConditions: 192,
                maxExpressionNodes: 2048);
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
                smtAnalysis: null);
            var second = SymbolicReachabilityService.ClassifyStateBranchFeasibility(
                new SymbolicState(new[] { positive, positive }),
                impossibleBranch,
                smtAnalysis: null);

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
                maxPathConditions: 192,
                maxExpressionNodes: 2048));
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
                maxPathConditions: 192,
                maxExpressionNodes: 2048));

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
                SymbolicReachabilityService.TryTranslateConditionFormula(
                    ifStatement.Condition,
                    semanticModel,
                    CancellationToken.None,
                    out var guardFormula),
                Is.True);
            Assert.That(guardFormula, Is.Not.Null);
            Assert.That(
                SymbolicReachabilityService.TryTranslateValueWithPathFacts(
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
        public void SymbolicReachabilityService_DoesNotTranslateDivisionValueWithoutNonZeroPathFacts()
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
                SymbolicReachabilityService.TryTranslateValueWithPathFacts(
                    divisionExpression,
                    semanticModel,
                    CancellationToken.None,
                    Array.Empty<SmtFormula>(),
                    out var translatedFormula),
                Is.False);
            Assert.That(translatedFormula, Is.Null);
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
                SymbolicReachabilityService.TryTranslateValue(
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
                SymbolicReachabilityService.TryTranslateConditionFormula(
                    returnExpression,
                    semanticModel,
                    CancellationToken.None,
                    out var translatedFormula),
                Is.True);
            Assert.That(translatedFormula, Is.Not.Null);
        }

        [Test]
        public void SymbolicReachabilityService_TriesIrBranchFactsBeforeLegacyTranslator()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var irIndex = source.IndexOf("TryAddIrBranchConditionFact(", StringComparison.Ordinal);
            var legacyIndex = source.IndexOf("SymbolicTranslatorCompatibility.TryCollectBranchAssumptions(", StringComparison.Ordinal);
            var helperStart = source.IndexOf("private static bool TryAddIrBranchConditionFact(", StringComparison.Ordinal);
            var helperEnd = source.IndexOf("private static bool ContainsDivisionOrModulo(", StringComparison.Ordinal);
            var helperSource = source.Substring(helperStart, helperEnd - helperStart);

            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperEnd, Is.GreaterThan(helperStart));
            Assert.That(irIndex, Is.LessThan(legacyIndex));
            Assert.That(helperSource, Does.Contain("SymbolicProofService.TryEncodeConditionWithPathState("));
            Assert.That(helperSource, Does.Not.Contain("ContainsDivisionOrModulo(condition)"));
            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
        }

        [Test]
        public void SymbolicProgramPointFacts_DelegatesBranchAssumptionsToSharedReachabilityService()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProgramPointFacts.cs"));

            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryAddBranchConditionFacts("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCollectBranchAssumptions("));
        }

        [Test]
        public void SymbolicProgramPointFacts_TriesIrHelpersBeforeLegacyTranslator()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProgramPointFacts.cs"));

            var valueHelperIndex = source.IndexOf("private static bool TryCreateValueFormula(", StringComparison.Ordinal);
            var conditionHelperIndex = source.IndexOf("private static bool TryCreateConditionFormula(", StringComparison.Ordinal);
            var branchHelperIndex = source.IndexOf("private static bool TryCollectBranchAssumptionFacts(", StringComparison.Ordinal);
            var valueHelperSource = source.Substring(valueHelperIndex, conditionHelperIndex - valueHelperIndex);
            var conditionHelperSource = source.Substring(conditionHelperIndex, branchHelperIndex - conditionHelperIndex);

            Assert.That(valueHelperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(conditionHelperIndex, Is.GreaterThan(valueHelperIndex));
            Assert.That(branchHelperIndex, Is.GreaterThan(conditionHelperIndex));
            Assert.That(valueHelperSource, Does.Contain("SymbolicReachabilityService.TryTranslateValue("));
            Assert.That(valueHelperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(conditionHelperSource, Does.Contain("SymbolicReachabilityService.TryTranslateConditionFormula("));
            Assert.That(conditionHelperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslate("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryTranslateBuiltInLengthValue("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryTranslateStringValue("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryTranslateValueWithPathFacts("));
            Assert.That(source, Does.Contain("new SymbolicElementTerm("));
            Assert.That(source, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm(element"));
            Assert.That(source, Does.Contain("new SymbolicMemberTerm("));
            Assert.That(source, Does.Contain("SymbolicSmtFormulaLowerer.TryLowerTerm(receiverFormula"));
            Assert.That(source, Does.Contain("new SymbolicVariableTerm(ImplicitThisVariableName, SmtValueKind.Reference)"));
            Assert.That(source, Does.Contain("new SymbolicNullableHasValueTerm("));
            Assert.That(source, Does.Contain("new SymbolicNullableValueTerm("));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryCreateStringContentReferenceTerm("));
            Assert.That(source, Does.Contain("TryCreateBuiltInLengthTerm("));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryCreateBuiltInLengthReferenceTerm(type, receiver, out term)"));
            Assert.That(source, Does.Contain("new SymbolicLengthTerm(stringTerm)"));
            Assert.That(source, Does.Contain("new SymbolicLengthTerm(stringLengthTerm)"));
            Assert.That(source, Does.Contain("new SymbolicArrayDimensionLengthTerm("));
            Assert.That(source, Does.Contain("TryCreateArrayDimensionLengthTerm("));
            Assert.That(source, Does.Contain("TryCreateTupleElementTerm("));
            Assert.That(source, Does.Contain("new SymbolicMemberTerm(tuple, elementName, elementKind)"));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInLengthValue("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateStringValue("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValueWithPathFacts("));
        }

        [Test]
        public void SymbolicProgramPointFacts_ProjectsAncestorReachabilityStateBeforeLegacyFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProgramPointFacts.cs"));
            var helperIndex = source.IndexOf(
                "internal static ImmutableArray<SmtFormula> CollectAncestorReachabilityConditions(",
                StringComparison.Ordinal);
            var legacyIndex = source.IndexOf(
                "private static ImmutableArray<SmtFormula> CollectAncestorReachabilityConditionsLegacy(",
                StringComparison.Ordinal);
            var stateIndex = source.IndexOf("CollectAncestorReachabilityState(", StringComparison.Ordinal);
            var encodeIndex = source.IndexOf("SymbolicProofService.TryEncodeStatePathConditions(state, out var pathConditions)", StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, legacyIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyIndex, Is.GreaterThan(helperIndex));
            Assert.That(stateIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(encodeIndex, Is.GreaterThan(stateIndex));
            Assert.That(helperSource, Does.Contain("CollectAncestorReachabilityState("));
            Assert.That(helperSource, Does.Contain("SymbolicProofService.TryEncodeStatePathConditions(state, out var pathConditions)"));
            Assert.That(helperSource, Does.Contain("return CollectAncestorReachabilityConditionsLegacy("));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrConditionTruthBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var methodIndex = source.IndexOf("internal static bool? EvaluateConditionTruth(", StringComparison.Ordinal);
            var irIndex = source.IndexOf("EvaluateConditionTruthWithIr(", StringComparison.Ordinal);
            var helperIndex = source.IndexOf("private static bool? EvaluateConditionTruthWithIr(", StringComparison.Ordinal);
            var helperEndIndex = source.IndexOf("internal static bool? EvaluateKnownConditionTruth(", StringComparison.Ordinal);
            var methodSource = source.Substring(methodIndex, helperIndex - methodIndex);
            var helperSource = source.Substring(helperIndex, helperEndIndex - helperIndex);

            Assert.That(methodIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperEndIndex, Is.GreaterThan(helperIndex));
            Assert.That(methodSource, Does.Contain("EvaluateConditionTruthWithIr("));
            Assert.That(methodSource, Does.Contain("TryTranslateConditionFormula("));
            Assert.That(methodSource, Does.Not.Contain("ContainsDivisionOrModulo(expression)"));
            Assert.That(methodSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslate(expression"));
            Assert.That(helperSource, Does.Contain("ClassifyStateConditionTruth(state"));
            Assert.That(helperSource, Does.Contain("SymbolicProofService.CreateStateFromFormulaPath(pathConditions, expression)"));
            Assert.That(helperSource, Does.Not.Contain("ClassifyStateBranchFeasibility(state"));
            Assert.That(helperSource, Does.Not.Contain("ClassifyStateImplication(state, condition"));
        }

        [Test]
        public void SymbolicReachabilityService_TranslatesConditionFormulaThroughIrBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf("internal static bool TryTranslateConditionFormula(", StringComparison.Ordinal);
            var helperEndIndex = source.IndexOf("internal static bool TryCreateArrayLengthCountAliasFact(", StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, helperEndIndex - helperIndex);
            var irIndex = helperSource.IndexOf("SymbolicIrLowerer.TryLowerCondition(condition", StringComparison.Ordinal);
            var legacyIndex = helperSource.IndexOf("SymbolicTranslatorCompatibility.TryTranslateConditionLegacy(", StringComparison.Ordinal);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperEndIndex, Is.GreaterThan(helperIndex));
            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyIndex, Is.GreaterThan(irIndex));
            Assert.That(helperSource, Does.Contain("SymbolicProofService.TryEncodeConditionWithPathState("));
            Assert.That(helperSource, Does.Not.Contain("!ContainsDivisionOrModulo(condition)"));
            Assert.That(helperSource, Does.Contain("formula = encodedFormula;"));
            Assert.That(helperSource, Does.Contain("return true;"));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrAssignedValueHelpersBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var lengthHelperIndex = source.IndexOf(
                "internal static bool TryTranslateBuiltInLengthValue(",
                StringComparison.Ordinal);
            var stringHelperIndex = source.IndexOf(
                "internal static bool TryTranslateStringValue(",
                StringComparison.Ordinal);
            var valueHelperIndex = source.IndexOf(
                "private static bool TryTranslateValue(",
                StringComparison.Ordinal);
            var untypedValueHelperIndex = source.IndexOf(
                "internal static bool TryTranslateValue(",
                StringComparison.Ordinal);
            var valueWithPathFactsHelperIndex = source.IndexOf(
                "internal static bool TryTranslateValueWithPathFacts(",
                StringComparison.Ordinal);
            var comparableHelperIndex = source.IndexOf(
                "private static bool TryTranslateComparableValue(",
                StringComparison.Ordinal);
            var stringNonNullIndex = source.IndexOf(
                "internal static bool TryCreateStringNonNullAssignedValueFact(",
                StringComparison.Ordinal);
            var lengthHelperSource = source.Substring(lengthHelperIndex, stringHelperIndex - lengthHelperIndex);
            var valueHelperSource = source.Substring(valueHelperIndex, untypedValueHelperIndex - valueHelperIndex);
            var untypedValueHelperSource = source.Substring(untypedValueHelperIndex, valueWithPathFactsHelperIndex - untypedValueHelperIndex);
            var valueWithPathFactsHelperSource = source.Substring(valueWithPathFactsHelperIndex, comparableHelperIndex - valueWithPathFactsHelperIndex);
            var comparableHelperSource = source.Substring(comparableHelperIndex, stringNonNullIndex - comparableHelperIndex);
            var stringHelperSource = source.Substring(stringHelperIndex, stringNonNullIndex - stringHelperIndex);

            Assert.That(lengthHelperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(stringHelperIndex, Is.GreaterThan(lengthHelperIndex));
            Assert.That(valueHelperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(untypedValueHelperIndex, Is.GreaterThan(valueHelperIndex));
            Assert.That(valueWithPathFactsHelperIndex, Is.GreaterThan(untypedValueHelperIndex));
            Assert.That(comparableHelperIndex, Is.GreaterThan(valueWithPathFactsHelperIndex));
            Assert.That(stringNonNullIndex, Is.GreaterThan(stringHelperIndex));
            Assert.That(valueHelperSource, Does.Contain("TryTranslateValue("));
            Assert.That(valueHelperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(untypedValueHelperSource, Does.Contain("SymbolicIrLowerer.TryLowerTerm(expression"));
            Assert.That(untypedValueHelperSource, Does.Contain("SymbolicProofService.TryEncodeTermWithPathState("));
            Assert.That(untypedValueHelperSource, Does.Not.Contain("!ContainsDivisionOrModulo(expression)"));
            Assert.That(untypedValueHelperSource, Does.Contain("SymbolicTranslatorCompatibility.TryTranslateValueLegacy("));
            Assert.That(
                untypedValueHelperSource.IndexOf("formula = encodedFormula;", StringComparison.Ordinal),
                Is.LessThan(untypedValueHelperSource.IndexOf("SymbolicTranslatorCompatibility.TryTranslateValueLegacy(", StringComparison.Ordinal)));
            Assert.That(valueWithPathFactsHelperSource, Does.Contain("TryTranslateValue("));
            Assert.That(valueWithPathFactsHelperSource, Does.Contain("pathFactArray.Length != 0"));
            Assert.That(valueWithPathFactsHelperSource, Does.Contain("SymbolicProofService.CreateStateFromFormulaPath(pathFactArray, expression)"));
            Assert.That(valueWithPathFactsHelperSource, Does.Contain("SymbolicProofService.TryEncodeTermWithPathState("));
            Assert.That(valueWithPathFactsHelperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValueWithPathFacts("));
            Assert.That(
                valueWithPathFactsHelperSource.IndexOf("TryTranslateValue(", StringComparison.Ordinal),
                Is.LessThan(valueWithPathFactsHelperSource.IndexOf("SymbolicProofService.TryEncodeTermWithPathState(", StringComparison.Ordinal)));
            Assert.That(comparableHelperSource, Does.Contain("TryTranslateValue("));
            Assert.That(comparableHelperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(lengthHelperSource, Does.Contain("SymbolicIrLowerer.TryLowerBuiltInLengthTerm(valueExpression"));
            Assert.That(lengthHelperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInLengthValue("));
            Assert.That(
                lengthHelperSource.IndexOf("formula = encodedFormula;", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(
                stringHelperSource.IndexOf("SymbolicIrLowerer.TryLowerStringTerm(valueExpression", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(stringHelperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateStringValue("));
            Assert.That(
                stringHelperSource.IndexOf("formula = encodedFormula;", StringComparison.Ordinal),
                Is.GreaterThanOrEqualTo(0));
            Assert.That(source, Does.Contain("SymbolicFactFactory.TryCreateBuiltInLengthFormula("));
            Assert.That(source, Does.Contain("TryCreateStringContentTerm("));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryCreateStringContentReferenceTerm(reference, out term)"));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrNullableHasValueWithoutLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "internal static bool TryCreateNullableHasValueCondition(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "internal static bool TryCreateRuntimeTypeTestCondition(",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Contain("SymbolicIrLowerer.TryLowerNullableHasValueTerm(expression"));
            Assert.That(helperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateNullableHasValue("));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm(hasValueTerm"));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrNullableTargetValueParts()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "private static bool TryCreateNullableHasValueFormula(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "internal static bool TryCreateReferenceBackedLengthFact(",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Contain("new SymbolicNullableHasValueTerm("));
            Assert.That(helperSource, Does.Contain("new SymbolicNullableValueTerm("));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm("));
            Assert.That(helperSource, Does.Not.Contain("SmtFormulaFactory.CreateBoolVariable("));
            Assert.That(helperSource, Does.Not.Contain("SmtFormulaFactory.CreateVariable("));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrNullableValuePartsWithoutLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "internal static bool TryTranslateNullableValueParts(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "internal readonly struct NullableValueParts",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateNullableValueParts("));
            Assert.That(helperSource, Does.Contain("SymbolicIrLowerer.TryLowerNullableHasValueTerm(expression"));
            Assert.That(
                helperSource,
                Does.Contain("SymbolicIrLowerer.TryLowerNullableValueTerm(expression"));
            Assert.That(helperSource, Does.Contain("TryTranslateIrNullableCoalesceValueParts("));
            Assert.That(helperSource, Does.Contain("TryTranslateIrNullableConditionalValueParts("));
            Assert.That(helperSource, Does.Contain("TryTranslateIrNullableConditionalAccessValueParts("));
            Assert.That(helperSource, Does.Contain("TryTranslateIrNullableWrappedValueParts("));
            Assert.That(helperSource, Does.Contain("TryTranslateIrNullLikeNullableValueParts("));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm(hasValueTerm"));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm(valueTerm"));
            Assert.That(helperSource, Does.Contain("new SymbolicConditionalTerm(leftHasValue, leftValueTerm, rightValueTerm)"));
            Assert.That(helperSource, Does.Contain("new SymbolicConditionalTerm("));
            Assert.That(helperSource, Does.Contain("TryCreateIrConditionalAccessWhenNotNullTerm("));
            Assert.That(helperSource, Does.Contain("ElementBindingExpressionSyntax"));
            Assert.That(helperSource, Does.Contain("SymbolicElementTerm"));
            Assert.That(helperSource, Does.Contain("new SmtBooleanConstant(true)"));
            Assert.That(helperSource, Does.Contain("new SmtBooleanConstant(false)"));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrArrayDimensionLengthWithoutLegacyTranslatorFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "internal static bool TryTranslateArrayDimensionLengthValue(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "internal static bool TryCreateCompoundAssignmentFact(",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Contain("SymbolicIrLowerer.TryLowerArrayDimensionLengthTerm(expression, dimension, context, out var term)"));
            Assert.That(helperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateArrayDimensionLengthValue("));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm(term"));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrArrayLengthCountAliasTerms()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "internal static bool TryCreateArrayLengthCountAliasFact(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "internal static bool TryCreateReferenceNullComparison(",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Contain("new SymbolicLengthTerm(receiver)"));
            Assert.That(helperSource, Does.Contain("new SymbolicCountTerm(receiver)"));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm("));
            Assert.That(helperSource, Does.Not.Contain("new SmtVariable(receiverVariable.Name + \".Length\""));
            Assert.That(helperSource, Does.Not.Contain("new SmtVariable(receiverVariable.Name + \".Count\""));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrStringNonNullWithoutLegacyTranslatorFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "private static bool TryCreateStringNonNullFormula(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "internal static bool TryCreateNotNullIfNotNullAssignedValueFact(",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Contain("SymbolicIrLowerer.TryLowerStringNonNullCondition(expression, context, out var condition)"));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded)"));
            Assert.That(helperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateStringNonNullFormula("));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrNotNullIfNotNullWithoutLegacyTranslatorFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "private static bool TryCreateNotNullIfNotNullResultNonNullFormula(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "internal static bool TryCreateAsExpressionAssignedValueFacts(",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Contain("return TryCreateIrNotNullIfNotNullResultNonNullFormula("));
            Assert.That(helperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateNotNullIfNotNullResultNonNullFormula("));
            Assert.That(helperSource, Does.Contain("CreateNotNullIfNotNullFallbackVariableName(resultExpression)"));
            Assert.That(helperSource, Does.Contain("SymbolicIrLowerer.TryLowerTerm(expression"));
        }

        [Test]
        public void SymbolicReachabilityService_UsesIrAsExpressionFactsWithoutLegacyTranslatorFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var helperIndex = source.IndexOf(
                "internal static bool TryCreateAsExpressionAssignedValueFacts(",
                StringComparison.Ordinal);
            var nextHelperIndex = source.IndexOf(
                "private static string GetVersionedSmtVariableName(",
                StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, nextHelperIndex - helperIndex);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nextHelperIndex, Is.GreaterThan(helperIndex));
            Assert.That(helperSource, Does.Contain("TryCreateIrAsExpressionAssignmentFacts("));
            Assert.That(helperSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateAsExpressionAssignmentFacts("));
            Assert.That(helperSource, Does.Contain("new SymbolicTypeTestAtom(source, typeKey)"));
            Assert.That(helperSource, Does.Contain("CreateIrRelationCondition("));
            Assert.That(helperSource, Does.Contain("SymbolicIrFormulaEncoder.TryEncode(condition"));
        }

        [Test]
        public void SymbolicReachabilityService_AddsIrLoweredBranchCondition()
        {
            var (semanticModel, ifStatement) = CreateSingleIfStatement("class C { void M(int x) { if (x > 0) { } } }");
            var pathConditions = new List<SmtFormula>();

            var added = SymbolicReachabilityService.TryAddBranchConditionFacts(
                ifStatement.Condition,
                branchWhenTrue: true,
                semanticModel,
                CancellationToken.None,
                pathConditions);

            Assert.That(added, Is.True);
            Assert.That(pathConditions, Is.Not.Empty);
            Assert.That(pathConditions.Select(static formula => formula.ToString() ?? string.Empty), Has.Some.Contains("x"));
        }

        [Test]
        public void SymbolicReachabilityService_CollectsIrBranchStateBeforeFormulaProjection()
        {
            var (semanticModel, ifStatement) = CreateSingleIfStatement("class C { void M(int x) { if (x <= 10) { } } }");
            var initialState = new SymbolicState();

            var added = SymbolicReachabilityService.TryCollectBranchState(
                initialState,
                ifStatement.Condition,
                branchWhenTrue: true,
                semanticModel,
                CancellationToken.None,
                out var branchState);
            var encoded = SymbolicReachabilityService.TryEncodeStatePathConditions(
                branchState,
                out var pathConditions);

            Assert.That(added, Is.True);
            Assert.That(branchState.PathConditions, Has.Length.EqualTo(1));
            Assert.That(encoded, Is.True);
            Assert.That(pathConditions, Has.Length.EqualTo(1));
            Assert.That(pathConditions[0].ToString(), Does.Contain("x"));
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
            var pathConditions = new List<SmtFormula>();
            using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);

            var added = SymbolicReachabilityService.TryAddBranchConditionFacts(
                ifStatement.Condition,
                branchWhenTrue: true,
                semanticModel,
                CancellationToken.None,
                pathConditions);
            var truth = SymbolicReachabilityService.EvaluateKnownConditionTruth(
                expression,
                semanticModel,
                CancellationToken.None,
                smtAnalysis,
                pathConditions);

            Assert.That(added, Is.True);
            Assert.That(truth, Is.True);
        }

        [Test]
        public void SymbolicReachabilityService_CollectsNegatedIrBranchState()
        {
            var (semanticModel, ifStatement) = CreateSingleIfStatement("class C { void M(int x) { if (x == 0) { } } }");

            var added = SymbolicReachabilityService.TryCollectBranchState(
                new SymbolicState(),
                ifStatement.Condition,
                branchWhenTrue: false,
                semanticModel,
                CancellationToken.None,
                out var branchState);

            Assert.That(added, Is.True);
            Assert.That(branchState.PathConditions.Single(), Is.TypeOf<SymbolicNotCondition>());
        }

        [Test]
        public void SymbolicProgramPointFacts_ProjectsAncestorReachabilityStateToFormulas()
        {
            var (semanticModel, ifStatement) = CreateSingleIfStatement(
                "class C { void M(int x) { if (x <= 10) { int y = x; } } }");
            var statement = ifStatement.Statement
                .DescendantNodesAndSelf()
                .OfType<LocalDeclarationStatementSyntax>()
                .Single();

            var conditions = SymbolicProgramPointFacts.CollectAncestorReachabilityConditions(
                statement,
                semanticModel,
                CancellationToken.None);

            Assert.That(conditions, Has.Length.EqualTo(1));
            Assert.That(conditions[0].ToString(), Does.Contain("x"));
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
        public void SymbolicInvariantAnalysis_TriesStateFeasibilityBeforeFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicInvariantService.cs"));
            var stateProofIndex = source.IndexOf("ClassifyStateFeasibility(pathState", StringComparison.Ordinal);
            var fallbackProofIndex = source.IndexOf("ClassifyStateFeasibilityWithFormulaFallback(", StringComparison.Ordinal);

            Assert.That(stateProofIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(fallbackProofIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(stateProofIndex, Is.LessThan(fallbackProofIndex));
            Assert.That(source, Does.Contain("stateProof?.Info.Status == SymbolicProofStatus.Unreachable"));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaReachability(formulas"));
            Assert.That(source, Does.Not.Contain("PathFeasibility switch"));
        }

        [Test]
        public void AnalyzerPurityState_CarriesSymbolicPathStateBesideLegacyFormulas()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var branchStateIndex = source.IndexOf("TryCollectBranchState(", StringComparison.Ordinal);
            var legacyBranchIndex = source.IndexOf("TryAddBranchConditionFacts(", StringComparison.Ordinal);

            Assert.That(source, Does.Contain("public SymbolicState PathState { get; }"));
            Assert.That(source, Does.Contain("WithPathConditionsAndState("));
            Assert.That(source, Does.Contain("TryCreateReferenceNullPathState("));
            Assert.That(source, Does.Contain("\"analyzer.branch.edge\""));
            Assert.That(source, Does.Contain("addTranslatedFormulaFallback: true"));
            Assert.That(source, Does.Contain("TryEncodeSymbolicBranchFormula("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslate(expressionSyntax"));
            Assert.That(branchStateIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyBranchIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(branchStateIndex, Is.LessThan(legacyBranchIndex));
        }

        [Test]
        public void AnalyzerPathFeasibility_TriesSymbolicStateBeforeLegacyFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var symbolicProofIndex = source.IndexOf("ClassifyStateFeasibility(pathState", StringComparison.Ordinal);
            var irFirstFallbackIndex = source.IndexOf("SymbolicReachabilityService.PathConditionsAreUnsatisfiableWithOptionalIrFirst(", StringComparison.Ordinal);

            Assert.That(symbolicProofIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irFirstFallbackIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(symbolicProofIndex, Is.LessThan(irFirstFallbackIndex));
            Assert.That(source, Does.Contain("proof.Info.Status == SymbolicProofStatus.Unreachable"));
            Assert.That(source, Does.Contain("SyntaxNode? sourceNode = null"));
            Assert.That(source, Does.Contain("ArePathConditionsUnsatisfiable(currentState, currentState.PathConditions, context.SmtAnalysis, operation.Syntax)"));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.IsUnsatisfiable("));
        }

        [Test]
        public void ExceptionFlowPathProofs_TrySymbolicStateBeforeLegacyFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var pathFactsSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "ExceptionFlowAnalyzer.PathFacts.cs"));
            var exceptionSitesSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "ExceptionFlowAnalyzer.ExceptionSites.cs"));
            var reachabilitySource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var proofServiceSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProofService.cs"));
            var symbolicProgramPointFactsSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProgramPointFacts.cs"));

            Assert.That(pathFactsSource, Does.Contain("SymbolicPathConditionsAreSatisfiable("));
            Assert.That(pathFactsSource, Does.Contain("SymbolicReachabilityService.TryGetCurrentSymbolValue("));
            Assert.That(pathFactsSource, Does.Contain("SymbolicReachabilityService.TryCreateCompoundAssignmentFact("));
            Assert.That(pathFactsSource, Does.Contain("SymbolicReachabilityService.TryCreateIncrementOrDecrementFact("));
            Assert.That(pathFactsSource, Does.Contain("SymbolicReachabilityService.AddUnsatisfiablePathCondition("));
            Assert.That(pathFactsSource, Does.Contain("PathConditionsAllowAndImplyWithIrFirst("));
            Assert.That(pathFactsSource, Does.Contain("PathConditionsAreSatisfiableWithIrFirst("));
            Assert.That(pathFactsSource, Does.Contain("PathConditionsImplyBranchWithIrFirst("));
            Assert.That(pathFactsSource, Does.Not.Contain("CSharpSmtFormulaTranslator."));
            Assert.That(pathFactsSource, Does.Not.Contain("SmtFormulaFactory."));
            Assert.That(pathFactsSource, Does.Not.Contain("SymbolicMutationFactFactory."));
            Assert.That(pathFactsSource, Does.Not.Contain("SymbolicReachabilityService.PathConditionsAllowAndImply(pathConditions, factFormula"));
            Assert.That(pathFactsSource, Does.Not.Contain("SymbolicReachabilityService.PathConditionsImplyBranch("));
            Assert.That(pathFactsSource, Does.Not.Contain("SymbolicReachabilityService.IsSatisfiable("));
            Assert.That(pathFactsSource, Does.Not.Contain("TryCreateSymbolicPathState("));
            Assert.That(pathFactsSource, Does.Not.Contain("SymbolicSmtFormulaLowerer.TryLowerCondition("));
            Assert.That(pathFactsSource, Does.Not.Contain("ClassifyStateFeasibility("));
            Assert.That(pathFactsSource, Does.Not.Contain("ClassifyStateConditionTruth("));
            Assert.That(pathFactsSource, Does.Not.Contain("ClassifyStateImplication("));
            var symbolFactFormulaIndex = pathFactsSource.IndexOf(
                "private static bool TryCreateFactFormula(\r\n            ISymbol symbol",
                StringComparison.Ordinal);
            if (symbolFactFormulaIndex < 0)
            {
                symbolFactFormulaIndex = pathFactsSource.IndexOf(
                    "private static bool TryCreateFactFormula(\n            ISymbol symbol",
                    StringComparison.Ordinal);
            }

            var expressionFactFormulaIndex = pathFactsSource.IndexOf(
                "private static bool TryCreateFactFormula(\r\n            ExpressionSyntax expression",
                StringComparison.Ordinal);
            if (expressionFactFormulaIndex < 0)
            {
                expressionFactFormulaIndex = pathFactsSource.IndexOf(
                    "private static bool TryCreateFactFormula(\n            ExpressionSyntax expression",
                    StringComparison.Ordinal);
            }

            Assert.That(symbolFactFormulaIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(expressionFactFormulaIndex, Is.GreaterThan(symbolFactFormulaIndex));
            var symbolFactFormulaSource = pathFactsSource.Substring(symbolFactFormulaIndex, expressionFactFormulaIndex - symbolFactFormulaIndex);
            Assert.That(symbolFactFormulaSource, Does.Contain("SymbolicReachabilityService.TryCreateSymbolReferenceNullComparison("));
            Assert.That(symbolFactFormulaSource, Does.Contain("SymbolicReachabilityService.TryCreateSymbolNumericZeroComparison("));
            Assert.That(symbolFactFormulaSource, Does.Not.Contain("SmtFormulaFactory.CreateReferenceVariable("));
            Assert.That(symbolFactFormulaSource, Does.Not.Contain("SmtFormulaFactory.CreateIntVariable("));
            var tryAddPathConditionIndex = pathFactsSource.IndexOf("private static void TryAddPathCondition(", StringComparison.Ordinal);
            Assert.That(tryAddPathConditionIndex, Is.GreaterThan(expressionFactFormulaIndex));
            var expressionFactFormulaSource = pathFactsSource.Substring(expressionFactFormulaIndex, tryAddPathConditionIndex - expressionFactFormulaIndex);
            Assert.That(expressionFactFormulaSource, Does.Contain("SymbolicReachabilityService.TryCreateReferenceNullComparison("));
            Assert.That(expressionFactFormulaSource, Does.Contain("SymbolicReachabilityService.TryCreateExpressionNumericZeroComparison("));
            Assert.That(expressionFactFormulaSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(expressionFactFormulaSource, Does.Not.Contain("SmtFormulaFactory.CreateReferenceNullComparison("));
            Assert.That(expressionFactFormulaSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerEqualsZero("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateFactFormula(\r\n            ITypeSymbol typeSymbol"));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateFactFormula(\n            ITypeSymbol typeSymbol"));
            var addArrayCreationFactsIndex = pathFactsSource.IndexOf("private static void AddArrayCreationNormalCompletionFacts(", StringComparison.Ordinal);
            var addSymbolNonNullFactIndex = pathFactsSource.IndexOf("private static void AddSymbolNonNullFact(", StringComparison.Ordinal);
            Assert.That(addArrayCreationFactsIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(addSymbolNonNullFactIndex, Is.GreaterThan(addArrayCreationFactsIndex));
            var addArrayCreationFactsSource = pathFactsSource.Substring(addArrayCreationFactsIndex, addSymbolNonNullFactIndex - addArrayCreationFactsIndex);
            Assert.That(addArrayCreationFactsSource, Does.Contain("SymbolicReachabilityService.TryCreateExpressionNonNegativeComparison("));
            Assert.That(addArrayCreationFactsSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(addArrayCreationFactsSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerGreaterThanOrEqualZero("));
            var addAssignedValueFactsIndex = pathFactsSource.IndexOf("private static void AddAssignedValueFacts(", StringComparison.Ordinal);
            var tryGetThrowGuardedValueIndex = pathFactsSource.IndexOf("private static bool TryGetThrowGuardedValue(", StringComparison.Ordinal);
            Assert.That(addAssignedValueFactsIndex, Is.GreaterThan(addArrayCreationFactsIndex));
            Assert.That(tryGetThrowGuardedValueIndex, Is.GreaterThan(addAssignedValueFactsIndex));
            var initialAssignedValueFactSource = pathFactsSource.Substring(addAssignedValueFactsIndex, tryGetThrowGuardedValueIndex - addAssignedValueFactsIndex);
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.TryCreateAssignedValueFact("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.AddNullableAssignedValueFacts("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.TryCreateReferenceBackedLengthFact("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.TryCreateCollectionExpressionLengthLowerBoundFact("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.AddArrayDimensionLengthAssignedValueFacts("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.AddReferenceBackedArrayDimensionLengthFacts("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.TryCreateStringContentAssignedValueFact("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.TryCreateReferenceBackedStringContentFact("));
            Assert.That(initialAssignedValueFactSource, Does.Contain("SymbolicReachabilityService.TryCreateStringNonNullAssignedValueFact("));
            Assert.That(initialAssignedValueFactSource, Does.Not.Contain("SymbolicFactFactory.CreateAssignedValueFact("));
            Assert.That(initialAssignedValueFactSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateNullableValueParts("));
            Assert.That(initialAssignedValueFactSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateStringValue("));
            Assert.That(initialAssignedValueFactSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateStringNonNullFormula("));
            Assert.That(initialAssignedValueFactSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateNotNullIfNotNullResultNonNullFormula("));
            Assert.That(initialAssignedValueFactSource, Does.Not.Contain("SmtFormulaFactory.CreateEquality("));
            Assert.That(initialAssignedValueFactSource, Does.Not.Contain("SmtFormulaFactory.CreateReferenceNullComparison("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static void AddNullableAssignedValueFacts("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryTranslateNullableWrappedValueForUnderlyingType("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateNullableHasValueFormula("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateNullableValueFormula("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateReferenceBackedLengthFact("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateReferenceBackedStringContentFact("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateCollectionExpressionLengthLowerBoundFact("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static void AddReferenceBackedArrayDimensionLengthFacts("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static void AddArrayDimensionLengthAssignedValueFacts("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateStringContentFormula("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateBuiltInLengthFormula("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateArrayDimensionLengthFormula("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateBuiltInLengthValueFormula("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateSymbolSmtValue("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateCompoundAssignmentFact("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryCreateIncrementOrDecrementFact("));
            Assert.That(pathFactsSource, Does.Not.Contain("private static bool TryGetCurrentSymbolValue("));
            Assert.That(reachabilitySource, Does.Contain("TryCreateReferenceSymbolTerm(targetSymbol"));
            Assert.That(reachabilitySource, Does.Contain("SymbolicIrFormulaEncoder.TryEncodeTerm(targetReferenceTerm"));
            Assert.That(symbolicProgramPointFactsSource, Does.Contain("SymbolicReachabilityService.TryCreateNotNullIfNotNullAssignedValueFact("));
            Assert.That(symbolicProgramPointFactsSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateNotNullIfNotNullResultNonNullFormula("));
            var addCompletedIfFactsIndex = pathFactsSource.IndexOf("private static void AddCompletedIfStatementFacts(", StringComparison.Ordinal);
            Assert.That(addSymbolNonNullFactIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(addCompletedIfFactsIndex, Is.GreaterThan(addSymbolNonNullFactIndex));
            var addSymbolNonNullFactSource = pathFactsSource.Substring(addSymbolNonNullFactIndex, addCompletedIfFactsIndex - addSymbolNonNullFactIndex);
            Assert.That(addSymbolNonNullFactSource, Does.Contain("SymbolicReachabilityService.TryCreateSymbolReferenceNullComparison("));
            Assert.That(addSymbolNonNullFactSource, Does.Not.Contain("TryCreateSymbolSmtValue("));
            Assert.That(addSymbolNonNullFactSource, Does.Not.Contain("SmtFormulaFactory.CreateReferenceNullComparison("));
            var addNonNullFactIndex = pathFactsSource.IndexOf("private static void AddReferenceNonNullFact(", StringComparison.Ordinal);
            var removeFactsIndex = pathFactsSource.IndexOf("private static void RemoveFactsInvalidatedByNestedMutations(", StringComparison.Ordinal);
            Assert.That(addNonNullFactIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(removeFactsIndex, Is.GreaterThan(addNonNullFactIndex));
            var addNonNullFactSource = pathFactsSource.Substring(addNonNullFactIndex, removeFactsIndex - addNonNullFactIndex);
            Assert.That(addNonNullFactSource, Does.Contain("SymbolicReachabilityService.TryCreateReferenceNullComparison("));
            Assert.That(addNonNullFactSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(addNonNullFactSource, Does.Not.Contain("SmtFormulaFactory.CreateReferenceNullComparison("));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.TryClassifyFormulaPathFeasibilityWithIr("));
            Assert.That(reachabilitySource, Does.Contain("return SymbolicProofService.TryClassifyFormulaConditionTruthWithIr("));
            Assert.That(proofServiceSource, Does.Contain("TryCreateStateFromFormulaPath(pathConditions, sourceNode, provenance, evidenceKey, out var state)"));
            Assert.That(proofServiceSource, Does.Contain("ClassifyConditionTruth(state, condition)"));
            Assert.That(proofServiceSource, Does.Contain("ClassifyReachability(state)"));
            var negativeArrayLengthIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyNegativeArrayLength(", StringComparison.Ordinal);
            var checkedOperatorIndex = exceptionSitesSource.IndexOf("private static bool TryGetCheckedIntegralBinaryOperator(", StringComparison.Ordinal);
            Assert.That(negativeArrayLengthIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(checkedOperatorIndex, Is.GreaterThan(negativeArrayLengthIndex));
            var negativeArrayLengthSource = exceptionSitesSource.Substring(negativeArrayLengthIndex, checkedOperatorIndex - negativeArrayLengthIndex);
            Assert.That(negativeArrayLengthSource, Does.Contain("SymbolicReachabilityService.TryCreateNegativeLengthTrigger("));
            Assert.That(negativeArrayLengthSource, Does.Not.Contain("TryTranslateIntExpression("));
            Assert.That(negativeArrayLengthSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerLessThanZero("));
            var nullableMissingIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyMissingNullableValue(", StringComparison.Ordinal);
            var checkedOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\r\n            BinaryExpressionSyntax", StringComparison.Ordinal);
            if (checkedOverflowIndex < 0)
            {
                checkedOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\n            BinaryExpressionSyntax", StringComparison.Ordinal);
            }

            Assert.That(nullableMissingIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(checkedOverflowIndex, Is.GreaterThan(nullableMissingIndex));
            var nullableMissingSource = exceptionSitesSource.Substring(nullableMissingIndex, checkedOverflowIndex - nullableMissingIndex);
            Assert.That(nullableMissingSource, Does.Contain("SymbolicReachabilityService.TryCreateNullableHasValueCondition("));
            Assert.That(nullableMissingSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateNullableHasValue("));
            var checkedPrefixOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\r\n            PrefixUnaryExpressionSyntax", StringComparison.Ordinal);
            if (checkedPrefixOverflowIndex < 0)
            {
                checkedPrefixOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\n            PrefixUnaryExpressionSyntax", StringComparison.Ordinal);
            }

            Assert.That(checkedPrefixOverflowIndex, Is.GreaterThan(checkedOverflowIndex));
            var checkedBinaryOverflowSource = exceptionSitesSource.Substring(checkedOverflowIndex, checkedPrefixOverflowIndex - checkedOverflowIndex);
            Assert.That(checkedBinaryOverflowSource, Does.Contain("SymbolicReachabilityService.TryCreateIntegerBinaryInRangeCondition("));
            Assert.That(checkedBinaryOverflowSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(checkedBinaryOverflowSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerBinaryTerm("));
            Assert.That(checkedBinaryOverflowSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerInRange("));
            var checkedPostfixOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\r\n            PostfixUnaryExpressionSyntax", StringComparison.Ordinal);
            if (checkedPostfixOverflowIndex < 0)
            {
                checkedPostfixOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\n            PostfixUnaryExpressionSyntax", StringComparison.Ordinal);
            }

            Assert.That(checkedPostfixOverflowIndex, Is.GreaterThan(checkedPrefixOverflowIndex));
            var checkedPrefixOverflowSource = exceptionSitesSource.Substring(checkedPrefixOverflowIndex, checkedPostfixOverflowIndex - checkedPrefixOverflowIndex);
            Assert.That(checkedPrefixOverflowSource, Does.Contain("SymbolicReachabilityService.TryCreateIntegerUnaryInRangeCondition("));
            Assert.That(checkedPrefixOverflowSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(checkedPrefixOverflowSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerUnaryTerm("));
            Assert.That(checkedPrefixOverflowSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerInRange("));
            var checkedCastOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\r\n            CastExpressionSyntax", StringComparison.Ordinal);
            if (checkedCastOverflowIndex < 0)
            {
                checkedCastOverflowIndex = exceptionSitesSource.IndexOf("private static bool IsDefinitelyCheckedIntegralOverflow(\n            CastExpressionSyntax", StringComparison.Ordinal);
            }

            Assert.That(checkedCastOverflowIndex, Is.GreaterThan(checkedOverflowIndex));
            Assert.That(negativeArrayLengthIndex, Is.GreaterThan(checkedCastOverflowIndex));
            var checkedCastOverflowSource = exceptionSitesSource.Substring(checkedCastOverflowIndex, negativeArrayLengthIndex - checkedCastOverflowIndex);
            Assert.That(checkedCastOverflowSource, Does.Contain("SymbolicReachabilityService.TryCreateIntegerInRangeCondition("));
            Assert.That(checkedCastOverflowSource, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(checkedCastOverflowSource, Does.Not.Contain("SmtFormulaFactory.CreateIntegerInRange("));
            Assert.That(exceptionSitesSource, Does.Contain("PathConditionsAllowAndImplyWithIrFirst("));
            Assert.That(exceptionSitesSource, Does.Not.Contain("SymbolicReachabilityService.PathConditionsAllowAndImply("));
        }

        [Test]
        public void AnalyzerNullPathProbes_CarrySymbolicNullStateBeforeFeasibility()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var nullProbeIndex = source.IndexOf("var nullPathState = TryCreateReferenceNullPathState(", StringComparison.Ordinal);
            var nullFeasibilityIndex = source.IndexOf("ArePathConditionsUnsatisfiable(currentState, nullPathConditions, nullPathState", StringComparison.Ordinal);
            var nonNullProbeIndex = source.IndexOf("var nonNullPathState = TryCreateReferenceNullPathState(", StringComparison.Ordinal);
            var nonNullFeasibilityIndex = source.IndexOf("ArePathConditionsUnsatisfiable(currentState, nonNullPathConditions, nonNullPathState", StringComparison.Ordinal);

            Assert.That(nullProbeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nullFeasibilityIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nullProbeIndex, Is.LessThan(nullFeasibilityIndex));
            Assert.That(nonNullProbeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nonNullFeasibilityIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(nonNullProbeIndex, Is.LessThan(nonNullFeasibilityIndex));
        }

        [Test]
        public void AnalyzerAssignmentFacts_AreMirroredIntoSymbolicState()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.cs"));
            var rawFactIndex = source.IndexOf("SymbolicReachabilityService.TryCreateAssignedValueFact(", StringComparison.Ordinal);
            var symbolicFactIndex = source.IndexOf("AddAssignedSymbolicEqualityFact(", StringComparison.Ordinal);

            Assert.That(rawFactIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(symbolicFactIndex, Is.GreaterThan(rawFactIndex));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateStringContentAssignedValueFact("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateStringNonNullAssignedValueFact("));
            Assert.That(source, Does.Contain("SymbolicReachabilityService.TryCreateAsExpressionAssignedValueFacts("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateValue("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryTranslateStringValue("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateStringNonNullFormula("));
            Assert.That(source, Does.Not.Contain("CSharpSmtFormulaTranslator.TryCreateAsExpressionAssignmentFacts("));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryLowerTerm("));
            Assert.That(source, Does.Contain("TryLowerAssignedLengthTerm"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryLowerStringTerm"));
            Assert.That(source, Does.Contain("SymbolicSmtFormulaLowerer.TryLowerEqualityFact("));
            Assert.That(source, Does.Contain("SymbolicProofService.AddLoweredFormulaPathCondition("));
            Assert.That(source, Does.Not.Contain("return SymbolicSmtFormulaLowerer.TryLowerCondition("));
            Assert.That(source, Does.Contain("new SymbolicRelationAtom("));
            Assert.That(source, Does.Contain("\"analyzer.assignment.value\""));
            Assert.That(source, Does.Contain("\"analyzer.assignment.length\""));
            Assert.That(source, Does.Contain("\"analyzer.assignment.string\""));
            Assert.That(source, Does.Contain("\"analyzer.assignment.reference_length\""));
            Assert.That(source, Does.Contain("\"analyzer.assignment.reference_string\""));
            Assert.That(source, Does.Contain("\"analyzer.assignment.collection_length\""));
            Assert.That(source, Does.Contain("\"analyzer.assignment.as_expression\""));
            Assert.That(source, Does.Contain("\"analyzer.assignment.string_nonnull\""));
        }

        [Test]
        public void AnalyzerStateMerge_PreservesCommonSymbolicPathState()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "PurityAnalysisEngine.StateMerge.cs"));

            Assert.That(source, Does.Contain("MergePathStatesAcrossAll("));
            Assert.That(source, Does.Contain("IntersectSymbolicFacts("));
            Assert.That(source, Does.Contain("IntersectSymbolicConditions("));
            Assert.That(source, Does.Contain("pathState: MergePathStatesAcrossAll("));
        }

        [Test]
        public void RuntimeHazardClassification_TriesIrProofBeforeLegacyFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardQueryService.cs"));
            var irIndex = source.IndexOf("TryClassifyIrTrigger(", StringComparison.Ordinal);
            var irFirstFormulaIndex = source.LastIndexOf("SymbolicReachabilityService.ClassifyFormulaConditionTruthWithIrFirst(", StringComparison.Ordinal);

            Assert.That(source, Does.Contain("ClassifyStateHazardTrigger("));
            Assert.That(source, Does.Contain("ClassifyFormulaConditionTruthWithIrFirst("));
            Assert.That(source, Does.Not.Contain("new SmtUnaryFormula(SmtUnaryOperator.Not, triggerCondition)"));
            Assert.That(source, Does.Contain("analysis.PathState"));
            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irFirstFormulaIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irIndex, Is.LessThan(irFirstFormulaIndex));
        }

        [Test]
        public void RuntimeHazardThrowNullRefinement_UsesIrPathStateBeforeLegacyFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardQueryService.cs"));
            var helperIndex = source.IndexOf("TryCreateReferenceNullCondition(", StringComparison.Ordinal);
            var irProofIndex = source.IndexOf("ClassifyStateConditionTruth(\r\n                    analysis.PathState,\r\n                    nullCondition,", StringComparison.Ordinal);
            if (irProofIndex < 0)
            {
                irProofIndex = source.IndexOf("ClassifyStateConditionTruth(\n                    analysis.PathState,\n                    nullCondition,", StringComparison.Ordinal);
            }

            var legacyFallbackIndex = source.IndexOf("PathConditionsImplyWithIrFirst(", StringComparison.Ordinal);

            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irProofIndex, Is.GreaterThan(helperIndex));
            Assert.That(legacyFallbackIndex, Is.GreaterThan(irProofIndex));
            Assert.That(source, Does.Contain("\"ir.runtime-hazard.throw-null.trigger\""));
            Assert.That(source, Does.Contain("out var throwNullTriggerPrecondition"));
            Assert.That(source, Does.Contain("triggerPrecondition = throwNullTriggerPrecondition"));
            Assert.That(source, Does.Contain("private static SymbolicFact? TryGetFactPrecondition(SymbolicCondition condition)"));
        }

        [Test]
        public async Task ProductionMetricsScript_ReportsProductionModulesAndExcludesTests()
        {
            var repositoryRoot = FindRepositoryRoot();
            using var document = await RunPowerShellJsonScriptAsync(
                repositoryRoot,
                "Get-PurelySharpProductionMetrics.ps1");
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
            Assert.That(largestPaths, Has.None.StartsWith("PurelySharp.Test/"));
        }

        private static async Task<JsonDocument> RunPowerShellJsonScriptAsync(
            string repositoryRoot,
            string scriptName)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = FindPowerShellExecutable(),
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
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
                process.Kill(entireProcessTree: true);
                throw;
            }

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    scriptName + " failed.",
                    "Exit code: " + process.ExitCode,
                    "stdout:",
                    output,
                    "stderr:",
                    error));
            }

            Assert.That(error, Is.Empty);
            return JsonDocument.Parse(output);
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
                        method.GetParameters().Any(static parameter => TypeReferencesSmtFormula(parameter.ParameterType));
                case ConstructorInfo constructor:
                    return constructor.GetParameters().Any(static parameter => TypeReferencesSmtFormula(parameter.ParameterType));
                default:
                    return false;
            }
        }

        private static bool TypeReferencesSmtFormula(Type type)
        {
            if (string.Equals(type.FullName, "SearchLib.Smt.SmtFormula", StringComparison.Ordinal))
            {
                return true;
            }

            if (type.HasElementType &&
                type.GetElementType() is { } elementType &&
                TypeReferencesSmtFormula(elementType))
            {
                return true;
            }

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
            var analyzerDirectory = Path.Combine(repositoryRoot, "PurelySharp.Analyzer");
            var files = Directory.GetFiles(analyzerDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
            var hotspots = new List<(string Path, int MatchCount)>();

            foreach (var file in files)
            {
                var source = File.ReadAllText(file);
                var matchCount = CountOrdinalOccurrences(source, "CSharpConditionToFormula.");
                foreach (var constructionNeedle in RawSmtConstructionNeedles)
                {
                    matchCount += CountOrdinalOccurrences(source, constructionNeedle);
                }

                if (matchCount > 0)
                {
                    hotspots.Add((
                        Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'),
                        matchCount));
                }
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
            "new SmtConditionalFormula",
        };

        private static int CountOrdinalOccurrences(string source, string needle)
        {
            var count = 0;
            var index = 0;
            while (index < source.Length)
            {
                var found = source.IndexOf(needle, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    return count;
                }

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
                if (File.Exists(Path.Combine(directory.FullName, "PurelySharp.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }

        private static string ReadRuntimeHazardCandidateSources(string repositoryRoot)
        {
            return string.Concat(
                File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "PurelySharp.Symbolic",
                    "SymbolicRuntimeHazardCandidateFactory.cs")),
                Environment.NewLine,
                File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "PurelySharp.Symbolic",
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
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }

            return OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        }

        private static string FindExecutableOnPath(string fileName)
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }
    }
}
