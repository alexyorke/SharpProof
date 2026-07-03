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
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange"));
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
            Assert.That(source, Does.Contain("IsSignedDivisionOverflowOperator"));
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryTranslateValue"));
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
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryTranslateNullableHasValue"));
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

            Assert.That(source, Does.Contain("TryCreateIrRuntimeReferenceCastMismatchTrigger"));
            Assert.That(source, Does.Contain("SymbolicExceptionPreconditionKind.InvalidCast"));
            Assert.That(source, Does.Contain("new SymbolicTypeTestAtom"));
            Assert.That(source, Does.Contain("SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey"));
            Assert.That(source, Does.Contain("CSharpConditionToFormula.TryCreateRuntimeTypeTestFormula"));
            Assert.That(coreSource, Does.Not.Contain("private static bool TryCreateRuntimeReferenceCastMismatchTrigger"));
            Assert.That(irTriggerSource, Does.Contain("private static bool TryCreateRuntimeReferenceCastMismatchTrigger"));
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
            Assert.That(root.GetProperty("hotspotCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(hotspotPaths, Is.EquivalentTo(ApprovedAnalyzerRawSmtHotspots));
            Assert.That(root.GetProperty("symbolicPublicFormulaSurfaceCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("symbolicCompatibilitySurfaceCount").GetInt32(), Is.GreaterThan(0));
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
            Assert.That(categories, Does.Contain("formula-metadata"));
            Assert.That(categories, Does.Contain("merged-invariant"));
            Assert.That(categories, Does.Contain("path-conditions"));
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
        public void SymbolicReachabilityService_TriesIrBranchFactsBeforeLegacyTranslator()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicReachabilityService.cs"));
            var irIndex = source.IndexOf("TryAddIrBranchConditionFact(", StringComparison.Ordinal);
            var legacyIndex = source.IndexOf("CSharpConditionToFormula.TryCollectBranchAssumptions", StringComparison.Ordinal);

            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irIndex, Is.LessThan(legacyIndex));
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
        public void RuntimeHazardClassification_TriesIrProofBeforeLegacyFormulaFallback()
        {
            var repositoryRoot = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "PurelySharp.Symbolic",
                "SymbolicRuntimeHazardQueryService.cs"));
            var irIndex = source.IndexOf("TryClassifyIrTrigger(", StringComparison.Ordinal);
            var legacyIndex = source.LastIndexOf("SymbolicReachabilityService.ClassifyImplication(", StringComparison.Ordinal);

            Assert.That(source, Does.Contain("ClassifyStateImplication("));
            Assert.That(source, Does.Contain("analysis.PathState"));
            Assert.That(irIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(legacyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(irIndex, Is.LessThan(legacyIndex));
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

        private static readonly string[] ApprovedAnalyzerRawSmtHotspots =
        {
            "PurelySharp.Analyzer/ExceptionFlowAnalyzer.ExceptionSites.cs",
            "PurelySharp.Analyzer/ExceptionFlowAnalyzer.PathFacts.cs",
            "PurelySharp.Analyzer/Engine/PurityAnalysisEngine.StateMerge.cs",
            "PurelySharp.Analyzer/Engine/PurityAnalysisEngine.cs",
        };

        private static readonly string[] ApprovedSymbolicPublicFormulaSurfaceFiles =
        {
            "PurelySharp.Symbolic/Ir/SymbolicIrFormulaEncoder.cs",
            "PurelySharp.Symbolic/Smt/CSharpConditionToFormula.cs",
            "PurelySharp.Symbolic/Smt/SmtAnalysisService.cs",
            "PurelySharp.Symbolic/Smt/SmtSyntacticClassifier.cs",
            "PurelySharp.Symbolic/SymbolicFactFactory.cs",
            "PurelySharp.Symbolic/SymbolicInvariantService.cs",
            "PurelySharp.Symbolic/SymbolicProgramPointFacts.cs",
            "PurelySharp.Symbolic/SymbolicProofService.cs",
            "PurelySharp.Symbolic/SymbolicReachabilityService.cs",
            "PurelySharp.Symbolic/SymbolicRuntimeHazardCandidateFactory.cs",
            "PurelySharp.Symbolic/SymbolicSourceQueryService.cs",
        };

        private static readonly string[] ApprovedSymbolicCompatibilitySurfaceFiles =
        {
            "PurelySharp.Symbolic/Smt/SmtAnalysisService.cs",
            "PurelySharp.Symbolic/SymbolicInvariantService.cs",
            "PurelySharp.Symbolic/SymbolicProofService.cs",
            "PurelySharp.Symbolic/SymbolicQueryApi.cs",
            "PurelySharp.Symbolic/SymbolicRuntimeHazardQueryService.cs",
            "PurelySharp.Symbolic/SymbolicSourceQueryService.cs",
        };

        private static readonly string[] AllowedExportedSmtFormulaTypes =
        {
            "PurelySharp.Symbolic.Smt.SmtAnalysisService",
        };

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
