using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
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
            var proofServiceSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProofService.cs"));

            Assert.That(reachabilitySource, Does.Contain("ClassifyWithFormulaFallback("));
            Assert.That(reachabilitySource, Does.Contain("ClassifyFormulaReachability(pathConditions, smtAnalysis).Info.Status"));
            Assert.That(reachabilitySource, Does.Contain("ClassifyFormulaConditionTruth(pathConditions, factFormula, smtAnalysis).Info.Status"));
            Assert.That(reachabilitySource, Does.Contain("ClassifyFormulaConditionTruth(pathConditions, formula, smtAnalysis).Info.Status"));
            Assert.That(reachabilitySource, Does.Not.Contain("new SymbolicProofService(smtAnalysis).ClassifyFormula"));
            Assert.That(reachabilitySource, Does.Not.Contain("IsNodeReachable("));
            Assert.That(reachabilitySource, Does.Not.Contain("IsNodeUnreachable("));
            Assert.That(proofServiceSource, Does.Not.Contain("internal SymbolicIrProofResult ClassifyFormula"));
            Assert.That(proofServiceSource, Does.Not.Contain("internal PurityProofResult ClassifyFormula"));
            Assert.That(proofServiceSource, Does.Contain("public SymbolicIrProofResult ClassifyReachability(SymbolicState state)"));
            Assert.That(proofServiceSource, Does.Contain("public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicFact fact)"));
            Assert.That(proofServiceSource, Does.Contain("public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicCondition condition)"));
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
        public void MethodInvocationRule_UsesSymbolicDisposalFactsForDoubleDispose()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Analyzer",
                "Engine",
                "Rules",
                "MethodInvocationPurityRule.cs"));

            Assert.That(source, Does.Contain("TryCheckDoubleDispose("));
            Assert.That(source, Does.Contain("TryCheckUseAfterDispose("));
            Assert.That(source, Does.Contain("HasDisposedResourceFact(currentState, resourceSymbol)"));
            Assert.That(source, Does.Contain("\"resource_double_dispose\""));
            Assert.That(source, Does.Contain("\"resource_use_after_dispose\""));
            Assert.That(source, Does.Contain("\"symbolic_resource_lifetime\""));
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

            Assert.That(coreSource, Does.Contain("internal static partial class SymbolicIrLowerer"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerRegexIsMatchInvocation"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryLowerStringPredicateInvocation"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerRegexIsMatchInvocation"));
            Assert.That(stringSource, Does.Contain("private static bool TryLowerStringPredicateInvocation"));
        }

        [Test]
        public void RuntimeHazardDivideByZero_UsesIrExceptionPreconditionTriggerBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateIrExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.DivideByZero"));
            Assert.That(source, Does.Contain("TryTranslateZeroCondition(divisor"));
            Assert.That(source, Does.Contain("new RuntimeHazardTrigger(formula)"));
            Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(binaryExpression.Right"));
            Assert.That(source, Does.Not.Contain("TryTranslateZeroCondition(assignment.Right"));
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
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange"));
        }

        [Test]
        public void RuntimeHazardSlicing_PreservesIrExceptionPreconditionWhenFormulaLowers()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateSlicingArgumentOutOfRangeCandidate"));
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.CreateSubsequenceInRangeFormula"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.ArgumentOutOfRange"));
            Assert.That(source, Does.Contain("CreateFormulaBackedExceptionPreconditionTrigger"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.slicing.argument-out-of-range"));
        }

        [Test]
        public void RuntimeHazardArrayGetValue1D_UsesIrBoundsPreconditionBeforeLegacyFallback()
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

            Assert.That(source, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.bounds.in-range"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.IndexOutOfRange"));
            Assert.That(source, Does.Contain("new SymbolicBoundsAtom"));
            Assert.That(source, Does.Contain("TryTranslateArrayGetValueDimensionLength"));
            Assert.That(source, Does.Contain("ir.runtime-hazard.array-get-value.index-out-of-range.fallback"));
            Assert.That(coreSource, Does.Contain("TryCreateIrArrayGetValueIndexOutOfRangeTrigger("));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateIrArrayGetValueIndexOutOfRangeTrigger"));
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
            Assert.That(source, Does.Not.Contain("if (!TryTranslateNegativeCondition(lengthExpression"));
        }

        [Test]
        public void RuntimeHazardCheckedIntegralOutOfRange_UsesIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

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
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryTranslateValue"));
        }

        [Test]
        public void RuntimeHazardStableNullDereferences_UseIrExceptionPreconditionsBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateNullDereferenceTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullDereference"));
            Assert.That(source, Does.Contain("IsStableIrReferenceSubject"));
            Assert.That(source, Does.Contain("TryTranslateNullCondition(receiver"));
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
            Assert.That(source, Does.Contain("TryTranslateNullCondition(expression"));
            Assert.That(source, Does.Contain("\"ir.runtime-hazard.argument-null.formula-fallback\""));
            Assert.That(source, Does.Contain("!TryCreateArgumentNullTrigger(expression"));
        }

        [Test]
        public void RuntimeHazardNullableValue_UsesIrExceptionPreconditionBeforeLegacyFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Contain("TryCreateNullableValueWithoutValueTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.NullableValueWithoutValue"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryLowerNullableHasValueTerm"));
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryTranslateNullableHasValue"));
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
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryCreateRuntimeTypeTestFormula"));
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
        public void RuntimeHazardCandidates_UseTranslatorShimForLegacyFallbacks()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = ReadRuntimeHazardCandidateSources(repositoryRoot);

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator."));
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
            Assert.That(root.GetProperty("symbolicPublicFormulaSurfaceCount").GetInt32(), Is.EqualTo(0));
            Assert.That(root.GetProperty("symbolicCompatibilitySurfaceCount").GetInt32(), Is.EqualTo(0));
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
            Assert.That(source, Does.Contain("SymbolicInvariantResult.FromFacts("));
            Assert.That(combinedSource.Split("new SymbolicSourceQueryResult(", StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            Assert.That(combinedSource, Does.Not.Contain("FromPathConditions("));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantCondition.FromFormula("));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\r\n                query.Analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\n                query.Analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\r\n                analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("SymbolicInvariantResult.FromPathConditions(\n                analysis.PathConditions"));
            Assert.That(combinedSource, Does.Not.Contain("IReadOnlyList<SmtFormula>? pathConditions = null"));
            Assert.That(source, Does.Contain("SymbolicFactInfo.FromState(query.Analysis.PathState)"));
        }

        [Test]
        public void SymbolicSourceQueryService_UsesTranslatorShimForSpeculativeConditionProofs()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicSourceQueryService.cs"));

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Contain("ClassifyStateConditionTruth("));
            Assert.That(source, Does.Not.Contain("ClassifyStateImplication("));
            Assert.That(source, Does.Not.Contain("new SymbolicNotCondition(symbolicCondition)"));
            Assert.That(source, Does.Contain("ClassifyFormulaConditionTruthWithIrFallback("));
            Assert.That(source, Does.Contain("ClassifyStateFeasibilityWithFormulaFallback("));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaReachability("));
            Assert.That(source, Does.Not.Contain("SymbolicReachabilityService.ClassifyFormulaConditionTruth("));
            Assert.That(source, Does.Contain("\"source.query.condition\""));
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryTranslate("));
        }

        [Test]
        public void SwitchPathConditionBuilder_UsesTranslatorShimForLegacyPatternFallbacks()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "Smt",
                "SwitchPathConditionBuilder.cs"));

            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryTranslatePattern("));
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryCollectPatternBindingFacts("));
            Assert.That(source, Does.Contain("CSharpSmtFormulaTranslator.TryCollectBranchAssumptions("));
        }

        [Test]
        public void LegacyTranslatorReferencesOutsideShim_AreIsolatedToProgramPointFacts()
        {
            var repositoryRoot = FindRepositoryRoot();
            var symbolicDirectory = Path.Combine(repositoryRoot, "PurelySharp.Symbolic");
            var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
            {
                "PurelySharp.Symbolic/SymbolicProgramPointFacts.cs",
            };
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
                .Except(allowedFiles, StringComparer.Ordinal)
                .ToArray();
            var programPointSource = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicProgramPointFacts.cs"));

            Assert.That(offenders, Is.Empty);
            Assert.That(programPointSource, Does.Contain("CSharpConditionToFormula."));
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
            Assert.That(info.Text, Does.Contain(nameof(SymbolicRelationOperator.Equal)));
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
            Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
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
            Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
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
            Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
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
            Assert.That(result.Info.Backend, Is.EqualTo(SymbolicProofBackend.Smt));
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
                    SymbolicRelationOperator.LessThanOrEqual,
                    x,
                    new SymbolicIntegerConstantTerm(0)),
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("x <= 0"),
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
        public void SymbolicReachabilityService_TriesIrBranchFactsBeforeLegacyTranslator()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var irIndex = source.IndexOf("TryAddIrBranchConditionFact(", StringComparison.Ordinal);
            var legacyIndex = source.IndexOf("CSharpSmtFormulaTranslator.TryCollectBranchAssumptions", StringComparison.Ordinal);

            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irIndex, Is.LessThan(legacyIndex));
            Assert.That(source, Does.Not.Contain("CSharpConditionToFormula."));
        }

        [Test]
        public void SymbolicReachabilityService_EvaluatesConditionTruthThroughIrBeforeLegacyTranslator()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var irIndex = source.IndexOf("EvaluateConditionTruthWithIr(", StringComparison.Ordinal);
            var legacyIndex = source.IndexOf("CSharpSmtFormulaTranslator.TryTranslate(expression", StringComparison.Ordinal);
            var helperIndex = source.IndexOf("private static bool? EvaluateConditionTruthWithIr(", StringComparison.Ordinal);
            var helperEndIndex = source.IndexOf("private static SymbolicState CreateStateFromFormulaPath", StringComparison.Ordinal);
            var helperSource = source.Substring(helperIndex, helperEndIndex - helperIndex);

            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(helperEndIndex, Is.GreaterThan(helperIndex));
            Assert.That(irIndex, Is.LessThan(legacyIndex));
            Assert.That(helperSource, Does.Contain("ClassifyStateConditionTruth(state"));
            Assert.That(helperSource, Does.Not.Contain("ClassifyStateBranchFeasibility(state"));
            Assert.That(helperSource, Does.Not.Contain("ClassifyStateImplication(state, condition"));
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
            Assert.That(reachabilitySource, Does.Contain("TryCreateStateFromFormulaPath("));
            Assert.That(reachabilitySource, Does.Contain("ClassifyStateFeasibility(state"));
            Assert.That(reachabilitySource, Does.Contain("ClassifyStateConditionTruth(state"));
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
            var rawFactIndex = source.IndexOf("CreateAssignedValueFact(targetFormula, valueFormula)", StringComparison.Ordinal);
            var symbolicFactIndex = source.IndexOf("AddAssignedSymbolicEqualityFact(", StringComparison.Ordinal);

            Assert.That(rawFactIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(symbolicFactIndex, Is.GreaterThan(rawFactIndex));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryLowerTerm("));
            Assert.That(source, Does.Contain("TryLowerAssignedLengthTerm"));
            Assert.That(source, Does.Contain("SymbolicIrLowerer.TryLowerStringTerm"));
            Assert.That(source, Does.Contain("SymbolicSmtFormulaLowerer.TryLowerEqualityFact("));
            Assert.That(source, Does.Contain("SymbolicSmtFormulaLowerer.TryLowerCondition("));
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
