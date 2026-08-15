using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerCallableLowererTests
{
    private static readonly CompilerContractKind[] ExpectedClauseKinds = [
        CompilerContractKind.Requires,
        CompilerContractKind.Assume,
        CompilerContractKind.Ensures
    ];

    [TestCase(ContractBindingFailure.UnsupportedExpression,
        WorkerClaimReason.UnsupportedExpression)]
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
    public void BoundContractsAndExecutableBodyRetainVerifierInputs()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Assume(value <= 100);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            "Identity");

        Assert.That(preparation.IsSuccess, Is.True);
        Assert.That(
            preparation.Clauses.Select(static clause => clause.Kind),
            Is.EqualTo(ExpectedClauseKinds));
        Assert.That(preparation.Entry.ClaimIds, Has.Length.EqualTo(1));
        Assert.That(
            preparation.Clauses.Single(static clause => clause.Kind == CompilerContractKind.Ensures).ClaimId,
            Is.EqualTo(preparation.Entry.ClaimIds[0]));
        Assert.That(
            preparation.Entry.Assumptions.Count(static evidence =>
                evidence.Kind == WorkerAssumptionKind.UserAssume),
            Is.EqualTo(1));
        var parameter = preparation.Variables.Single(static variable =>
            variable.Role == CompilerVariableRole.Parameter);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parameter.ModelLabel, Is.EqualTo("parameter:0"));
            Assert.That(
                parameter.SourceIntegerInterval,
                Is.EqualTo(new CompilerIntegerInterval(int.MinValue, int.MaxValue)));
            Assert.That(
                preparation.Variables.Single(static variable =>
                    variable.Role == CompilerVariableRole.Result).ModelLabel,
                Is.EqualTo("result"));
            Assert.That(preparation.Body, Is.Not.Null);
            Assert.That(preparation.Body!.Kind, Is.EqualTo(CompilerPreparedBodyKind.Program));
            Assert.That(preparation.Body.Program, Is.Not.Null);
            Assert.That(preparation.Body.ParameterBindings, Has.Count.EqualTo(1));
            Assert.That(preparation.Body.SpecCalls, Is.Empty);
            Assert.That(preparation.Body.Program!.Entry.Value, Is.Zero);
            Assert.That(preparation.Body.Program.Entry, Is.EqualTo(preparation.Body.Program.Blocks[0].Id));
        }
    }

    [Test]
    public void ResolvedSpecCallIsBoundToExactLoweredInstruction()
    {
        var preparation = Prepare(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Absolute(int value) {
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return System.Math.Abs(value);
                }
            }
            """,
            "Absolute");

        Assert.That(
            preparation.IsSuccess,
            Is.True,
            preparation.FailureReason.ToString());
        var body = preparation.Body!;
        var descriptor = body.SpecCalls.Values.Single();
        var call = body.Program!.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrCallInstruction>()
            .Single(instruction => instruction.Id == descriptor.Instruction);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                descriptor.WitnessIdentifier,
                Is.EqualTo("bcl.math.abs.int32"));
            Assert.That(descriptor.CallIdentity, Is.EqualTo("M:System.Math.Abs(System.Int32)"));
            Assert.That(descriptor.ConsumesMemoryHavoc, Is.False);
            Assert.That(call.Id, Is.EqualTo(descriptor.Instruction));
        }
    }

    [Test]
    public void DirectAcyclicSourceCallCarriesAReusableRelationalSummary()
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

        Assert.That(
            preparation.IsSuccess,
            Is.True,
            preparation.FailureReason.ToString());
        var body = preparation.Body!;
        var descriptor = body.SummaryCalls.Values.Single();
        var call = body.Program!.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrCallInstruction>()
            .Single(instruction => instruction.Id == descriptor.Instruction);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.SpecCalls, Is.Empty);
            Assert.That(
                descriptor.Origin,
                Is.EqualTo(CompilerSummaryOrigin.Source));
            Assert.That(descriptor.CallIdentity, Does.Contain(".Read("));
            Assert.That(descriptor.EvidenceSha256, Has.Length.EqualTo(64));
            Assert.That(
                descriptor.EvidenceIdentity,
                Is.EqualTo(descriptor.CallIdentity));
            Assert.That(descriptor.NormalRelation.Type, Is.EqualTo(preparation.Factory.BooleanType));
            Assert.That(call.Id, Is.EqualTo(descriptor.Instruction));
        }
    }

    [Test]
    public void ConstructorAndRefBodyAreTypedUnsupported()
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
            Assert.That(constructor.IsSuccess, Is.False);
            Assert.That(
                constructor.FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
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
            CompilerPreparedBody.MaximumInstructions));
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
            Assert.That(preparation.Body, Is.Null);
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
        TestName = "RequiresOnlyLoopIsTypedIncomplete")]
    [TestCase(
        "return UnsupportedCall(value);",
        TestName = "RequiresOnlyUnsupportedCallIsTypedIncomplete")]
    [TestCase(
        "return new[] { value }[0];",
        TestName = "RequiresOnlyHeapAccessIsTypedIncomplete")]
    public async Task RequiresOnlyUnsupportedBodyIsTypedIncomplete(
        string body)
    {
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
            Assert.That(preparation.IsSuccess, Is.False);
            Assert.That(
                preparation.FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(preparation.Entry.ClaimIds, Is.Empty);
            Assert.That(
                verification.Callable.Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(
                verification.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.SemanticUnknown));
            Assert.That(verification.Claims, Is.Empty);
        }
    }

    [Test]
    public async Task MixedEffectAndRequiresUnsupportedBodyIsTypedIncomplete()
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
            Assert.That(preparation.IsSuccess, Is.False);
            Assert.That(
                preparation.FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(preparation.Entry.ClaimIds, Has.Length.EqualTo(1));
            Assert.That(
                verification.Callable.Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(
                verification.Callable.Reason,
                Is.EqualTo(WorkerCallableCoverageReason.SemanticUnknown));
            Assert.That(verification.Claims, Has.Length.EqualTo(1));
            Assert.That(
                verification.Claims[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                verification.Claims[0].Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
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
            Assert.That(preparation.Body, Is.Null);
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

        var valid = lowerer.Prepare(target);
        var missingClaim = lowerer.Prepare(target with
        {
            Claims = []
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid.IsSuccess, Is.True);
            Assert.That(
                missingClaim.FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedContract));
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

    private static CompilerCallablePreparation Prepare(
        string source,
        string methodName)
    {
        var (compilation, target, factory) = CreateTarget(source, methodName);
        return new CompilerCallableLowerer(compilation, factory).Prepare(target) with
        {
            EffectClaims = [.. target.EffectClaims.Select(static claim => claim.Evidence)]
        };
    }

    private static async Task<CallableVerificationResult> VerifyCoverageAsync(
        CompilerCallablePreparation preparation)
    {
        var backend = new UnexpectedBackend();
        using var projectBoundary = new CancellationTokenSource();
        var verification = await CallableVerificationPolicy.VerifyTargetAsync(
            new CallableVerifier(
                backend,
                WorkerBudgets.DefaultMaximumExpressionDepth),
            preparation,
            new WorkerBudgets(),
            null,
            WorkerBudgets.DefaultMethodWallTimeMilliseconds,
            projectBoundary,
            CancellationToken.None);
        Assert.That(backend.CallCount, Is.Zero);
        return verification;
    }

    private static (
        CSharpCompilation Compilation,
        ManifestCallableTarget Target,
        IrFactory Factory) CreateTarget(
        string source,
        string methodName)
    {
        var compilation = CreateCompilation(source);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single(candidate =>
            candidate.Method.MetadataName == methodName);
        return (compilation, target, new IrFactory());
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parse = new CSharpParseOptions(
            LanguageVersion.CSharp12,
            preprocessorSymbols: [Contract.ConditionalSymbol]);
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Contract).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create(
            "CompilerCallableLowererTests",
            [CSharpSyntaxTree.ParseText(source, parse, "Subject.cs")],
            paths.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(Environment.NewLine, errors.Select(static error =>
                error.ToString())));
        return compilation;
    }

    private sealed class UnexpectedBackend : ISmtBackend
    {
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            throw new AssertionException(
                "A zero-claim callable reached the SMT backend.");
        }
    }
}
