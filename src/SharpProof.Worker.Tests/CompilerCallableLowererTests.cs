using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Specs;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class CompilerCallableLowererTests
{
    [Test]
    public void OrdinaryConsumerWithoutOptionalContractApiMapsItsActualBindingFailure()
    {
        const string source = "internal static class Subject { internal static int Identity(int value) => value; }";
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: [Contract.ConditionalSymbol]);
        var tree = CSharpSyntaxTree.ParseText(source, parse, "optional-api.cs");
        var missing = CSharpCompilation.Create("OptionalApiConsumer", [tree],
            TestMetadataReferences.WithoutSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary, NullableContextOptions.Enable));
        var available = missing.WithReferences(TestMetadataReferences.WithSharpProof);
        TestCompilation.AssertNoErrors(missing);
        TestCompilation.AssertNoErrors(available);
        using var missingImage = new MemoryStream();
        using var availableImage = new MemoryStream();
        Assert.That(missing.Emit(missingImage).Success, Is.True);
        Assert.That(available.Emit(availableImage).Success, Is.True);
        Assert.That(missing.GetTypeByMetadataName(typeof(Contract).FullName!), Is.Null);
        Assert.That(available.GetTypeByMetadataName(typeof(Contract).FullName!), Is.Not.Null);

        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var missingModel = missing.GetSemanticModel(tree);
        var availableModel = available.GetSemanticModel(tree);
        var missingMethod = (IMethodSymbol)missingModel.GetDeclaredSymbol(declaration)!;
        var availableMethod = (IMethodSymbol)availableModel.GetDeclaredSymbol(declaration)!;
        var rejected = new ContractBinder(missing, new IrFactory()).Bind(missingMethod);
        var control = new ContractBinder(available, new IrFactory()).Bind(availableMethod);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missingImage.Length, Is.GreaterThan(0));
            Assert.That(availableImage.Length, Is.GreaterThan(0));
            Assert.That(missingMethod.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(availableMethod.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(missingModel.GetOperation(declaration), Is.Not.Null);
            Assert.That(availableModel.GetOperation(declaration), Is.Not.Null);
            Assert.That(control.IsSuccess, Is.True);
            Assert.That(control.Failure, Is.EqualTo(ContractBindingFailure.None));
            Assert.That(control.Contracts!.Clauses, Is.Empty);
            Assert.That(CompilerLoweringWireMappings.ToWorkerFailure(control.Failure),
                Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Contracts, Is.Null);
            Assert.That(rejected.Failure, Is.EqualTo(ContractBindingFailure.ContractApiUnavailable));
            Assert.That(CompilerLoweringWireMappings.ToWorkerFailure(rejected.Failure),
                Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        }
    }

    [Test]
    public void CompilerProducedDestructorFailureMapsToUnsupportedCallable()
    {
        var compilation = TestCompilation.Create("DestructorFailureMapping",
            "internal sealed class Subject { internal static int Identity(int value) => value; ~Subject() { } }");
        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var declaration = root.DescendantNodes().OfType<DestructorDeclarationSyntax>().Single();
        var destructor = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var ordinaryDeclaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var ordinary = (IMethodSymbol)model.GetDeclaredSymbol(ordinaryDeclaration)!;
        var binder = new ContractBinder(compilation, new IrFactory());
        var control = binder.Bind(ordinary);
        var rejected = binder.Bind(destructor);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(destructor.MethodKind, Is.EqualTo(MethodKind.Destructor));
            Assert.That(model.GetOperation(declaration), Is.Not.Null);
            Assert.That(ordinary.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(control.IsSuccess, Is.True);
            Assert.That(control.Failure, Is.EqualTo(ContractBindingFailure.None));
            Assert.That(control.Contracts!.Clauses, Is.Empty);
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Contracts, Is.Null);
            Assert.That(rejected.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
            Assert.That(CompilerLoweringWireMappings.ToWorkerFailure(rejected.Failure),
                Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        }
    }

    [TestCase(ContractBindingFailure.UnsupportedExpression,
        WorkerClaimReason.UnsupportedExpression)]
    // A non-Boolean clause violates contract admission.
    [TestCase(ContractBindingFailure.NonBooleanCondition,
        WorkerClaimReason.UnsupportedContract)]
    [TestCase(ContractBindingFailure.InvalidClausePlacement,
        WorkerClaimReason.UnsupportedContract)]
    [TestCase(ContractBindingFailure.CompanionBodyUnavailable,
        WorkerClaimReason.UnsupportedCallable)]
    public void BindingFailureWireMappingIsTyped(
        ContractBindingFailure failure,
        WorkerClaimReason expected)
    {
        Assert.That(
            CompilerLoweringWireMappings.ToWorkerFailure(failure),
            Is.EqualTo(expected));
        Assert.That(
            (Action)(() => CompilerLoweringWireMappings.ToWorkerFailure(
                (ContractBindingFailure)int.MaxValue)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void LeadingGotoLoopIsLoweredByTheTotalIr()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int SelectReachable(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() == value);
                    goto Loop;
                Dead:
                    return 0;

                Loop:
                    if (value == 0) {
                        return value;
                    }
                    value = value - 1;
                    goto Loop;
                }
            }
            """,
            "SelectReachable");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.IsSuccess, Is.True);
            Assert.That(preparation.Total, Is.Not.Null);
        }
    }

    [Test]
    public void NullableValueGetterIsLoweredByTheTotalIr()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int NullableValue(int? value) {
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return value.Value;
                }
            }
            """,
            "NullableValue");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.IsSuccess, Is.True);
            Assert.That(preparation.Total, Is.Not.Null);
        }
    }

    [Test]
    public void DirectAcyclicSourceCallUsesTotalPreparation()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                private static bool Read(bool value) => value;

                internal static bool Verify(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    return Read(value);
                }
            }
            """,
            "Verify");

        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.Program.Factory.Semantics, Is.EqualTo(IrExecutionSemantics.Total));
    }

    [Test]
    public void RelativeSourceSummaryTreePathBindsToCapturedSnapshot()
    {
        var parse = new CSharpParseOptions(
            LanguageVersion.CSharp12,
            preprocessorSymbols: [Contract.ConditionalSymbol]);
        var mainPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "RelativeSummarySubject.cs");
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(
                """
                #undef SHARPPROOF_CONTRACTS
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static bool Verify(bool value) {
                        Contract.Ensures(
                            Contract.Result<bool>() == value);
                        return Helper.Read(value);
                    }
                }
                """,
                parse,
                mainPath),
            CSharpSyntaxTree.ParseText(
                """
                #undef SHARPPROOF_CONTRACTS
                internal static class Helper {
                    internal static bool Read(bool value) => value;
                }
                """,
                parse,
                "generated/helper.g.cs")
        };
        var compilation = CSharpCompilation.Create(
            "RelativeSourceSummaryTreePath",
            trees,
            TestMetadataReferences.WithSharpProof,
            TestCompilation.CreateOptions(
                OutputKind.DynamicallyLinkedLibrary,
                NullableContextOptions.Enable));
        var discovery = new ClaimManifestBuilder(compilation).Build();

        var artifact = CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);

        var preparation = CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.Program.Factory.Semantics, Is.EqualTo(IrExecutionSemantics.Total));
    }

    [Test]
    public void PlainConstructorIsLoweredAndRefBodyIsUnsupported()
    {
        var constructor = Prepare(
            """
            using SharpProof.Attributes;
            internal sealed class Subject {
                internal Subject() {
                    Contract.Ensures(true);
                }
            }
            """,
            ".ctor");
        var byReference = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Read(ref int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            "Read");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(constructor.IsSuccess, Is.True);
            Assert.That(constructor.Total, Is.Not.Null);
            Assert.That(byReference.IsSuccess, Is.False);
            Assert.That(
                byReference.FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        }
    }

    [Test]
    public void BodyAboveTheReplayInstructionBoundIsTypedUnsupported()
    {
        var statements = string.Concat(Enumerable.Repeat(
            "value = value;\n",
            CompilerArtifactLimits.MaximumInstructions));
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Oversized(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
            """ + statements +
            """
                    return value;
                }
            }
            """,
            "Oversized");

        Assert.That(preparation.IsSuccess, Is.False);
        Assert.That(
            preparation.FailureReason,
            Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public async Task RequiresOnlySupportedBodyIsAdmittedAndComplete()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Requires(value >= 0);
                    return value;
                }
            }
            """,
            "Identity");

        var verification = await VerifyCoverageAsync(preparation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.IsSuccess, Is.True);
            Assert.That(preparation.FailureReason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(preparation.Entry.ClaimIds, Is.Empty);
            Assert.That(
                verification.Callable.Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(
                verification.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.None));
            Assert.That(verification.Claims, Is.Empty);
        }
    }

    [TestCase(
        "while (value > 0) { value--; }\nreturn value;",
        TestName = "RequiresOnlyLoopHasNativeCoverage")]
    [TestCase(
        "return UnsupportedCall(value);",
        TestName = "RequiresOnlyUnsupportedCallHasNativeCoverage")]
    [TestCase(
        "return new[] { value }[0];",
        TestName = "RequiresOnlyHeapAccessHasNativeCoverage")]
    public async Task RequiresOnlyLoweredBodyHasNativeCoverage(
        string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(
            $$"""
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Verify(int value) {
                    Contract.Requires(value >= 0);
                    {{body}}
                }

                private static int UnsupportedCall(int value) =>
                    UnsupportedCall(value);
            }
            """,
            "Verify");

        var verification = await VerifyCoverageAsync(preparation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.IsSuccess, Is.True);
            Assert.That(preparation.Total, Is.Not.Null);
            Assert.That(preparation.Entry.ClaimIds, Is.Empty);
            Assert.That(
                verification.Callable.Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(
                verification.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.None));
            Assert.That(verification.Claims, Is.Empty);
        }
    }

    [Test]
    public async Task MixedEffectAndRequiresLoopBodyProvesNatively()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow]
                internal static void Verify(int value) {
                    Contract.Requires(value >= 0);
                    while (value > 0) {
                        value--;
                    }
                }
            }
            """,
            "Verify");

        var verification = await VerifyCoverageAsync(preparation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.IsSuccess, Is.True);
            Assert.That(preparation.Total, Is.Not.Null);
            Assert.That(preparation.Entry.ClaimIds, Has.Length.EqualTo(1));
            Assert.That(
                preparation.EffectClaims.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                verification.Callable.Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(
                verification.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.None));
            Assert.That(verification.Claims, Has.Length.EqualTo(1));
            Assert.That(
                verification.Claims[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                verification.Claims[0].Reason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                verification.Claims[0].EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(
                verification.Claims[0].ProofCore,
                Is.EqualTo(["native-effect:" + preparation.EffectClaims.Single().ClaimId]));
        }
    }

    [Test]
    public async Task EffectOnlyUnsupportedSymbolicBodyDoesNotTriggerAdmission()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow]
                internal static void Verify(int value) {
                    while (value > 0) {
                        value--;
                    }
                }
            }
            """,
            "Verify");

        var verification = await VerifyCoverageAsync(preparation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.IsSuccess, Is.True);
            Assert.That(preparation.FailureReason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(preparation.Entry.ClaimIds, Has.Length.EqualTo(1));
            Assert.That(
                verification.Callable.Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(
                verification.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.None));
            Assert.That(verification.Claims, Has.Length.EqualTo(1));
            Assert.That(
                verification.Claims[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                verification.Claims[0].Reason,
                Is.EqualTo(WorkerClaimReason.None));
        }
    }

    [Test]
    public void ManifestClaimAndAssumptionDriftFailsClosed()
    {
        var (compilation, target, factory) = CreateTarget(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Assume(value >= 0);
                    Contract.Assume(value <= 10);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            "Identity");
        var lowerer = new CompilerCallableLowerer(compilation, factory);

        var valid = lowerer.Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var missingClaim = lowerer.Prepare(target with
        {
            Claims = []
        }, cancellationToken: TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid.IsSuccess, Is.True);
            Assert.That(
                missingClaim.FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CopiedManifestClaimMetadataDriftFailsClosed(bool changeOrdinal)
    {
        var (compilation, target, factory) = CreateTarget(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            "Identity");
        var lowerer = new CompilerCallableLowerer(compilation, factory);
        var valid = lowerer.Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(valid.IsSuccess, Is.True);

        var original = target.Claims.Single();
        var entry = original.Entry;
        var copiedEntry = new WorkerClaimManifestEntry
        {
            ClaimId = entry.ClaimId,
            CallableId = entry.CallableId,
            Ordinal = changeOrdinal ? entry.Ordinal + 1 : entry.Ordinal,
            Kind = changeOrdinal ? entry.Kind : WorkerClaimKind.Effect,
            Evidence = entry.Evidence,
            EffectContractKind = entry.EffectContractKind,
            Location = entry.Location
        };
        var copied = target with { Claims = [original with { Entry = copiedEntry }] };
        var rejected = lowerer.Prepare(copied, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(copied.Method, Is.SameAs(target.Method));
            Assert.That(copied.SemanticModel, Is.SameAs(target.SemanticModel));
            Assert.That(copied.Claims.Single().SourceOperation, Is.SameAs(original.SourceOperation));
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
            Assert.That(lowerer.Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken).IsSuccess, Is.True);
        }
    }

    [Test]
    public void CancellationStopsPreparationBeforeBinding()
    {
        var (compilation, target, factory) = CreateTarget(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            "Identity");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            (Action)(() => new CompilerCallableLowerer(compilation, factory)
                .Prepare(target, cancellation.Token)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task UnsignaledBackendCancellationIsInfrastructureFailure()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            "Identity");
        using var projectBoundary = new CancellationTokenSource();

        var verification = await CallableVerificationPolicy.VerifyNativeTargetAsync(
            new UnsignaledCancellationBackend(),
            preparation,
            new WorkerBudgets(),
            null,
            WorkerBudgets.DefaultMethodWallTimeMilliseconds,
            projectBoundary,
            TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                verification.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.InfrastructureFailure));
            Assert.That(
                verification.Claims.Select(static claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.Reason)),
                Is.All.EqualTo(WorkerClaimReason.InfrastructureFailure));
        }
    }

    private static CompilerCallablePreparation Prepare(
        string source,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(source);
        var (compilation, target, factory) = CreateTarget(source, methodName);
        return new CompilerCallableLowerer(compilation, factory).Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken) with
        {
            EffectClaims = [.. target.EffectClaims.Select(static claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.Evidence))]
        };
    }

    private static async Task<CallableVerificationResult> VerifyCoverageAsync(
        CompilerCallablePreparation preparation)
    {
        System.ArgumentNullException.ThrowIfNull(preparation);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var backend = new NativeCallableBackend(new IrSmtBackendOptions(WorkerBudgets.DefaultQueryRlimit));
        using var projectBoundary = new CancellationTokenSource();
        var verification = await CallableVerificationPolicy.VerifyNativeTargetAsync(
            backend,
            preparation,
            new WorkerBudgets(),
            () => backend.ConsumedResourceCount,
            WorkerBudgets.DefaultMethodWallTimeMilliseconds,
            projectBoundary,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(backend.ConsumedResourceCount == 0,
            Is.EqualTo(!preparation.Entry.Assumptions.Any(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.Precondition))));
        return verification;
    }

    private static (
        CSharpCompilation Compilation,
        ManifestCallableTarget Target,
        IrFactory Factory) CreateTarget(
        string source,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create(
            "CompilerCallableLowererTests",
            ("Subject.cs", source));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single(candidate =>
            candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Method.MetadataName == methodName));
        return (compilation, target, new IrFactory());
    }

    private sealed class UnsignaledCancellationBackend : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            System.ArgumentNullException.ThrowIfNull(query);
            return Task.FromException<BackendCheckResult>(
                new OperationCanceledException());
        }
    }
}
