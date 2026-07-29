using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
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
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
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
        return new CompilerCallableLowerer(compilation, factory).Prepare(target);
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
}
