using System.Numerics;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerFrontendInstructionBoundContractTests
{
    // A valid auto-accessor has at most a receiver and a setter value input.
    private const long AccessorInstructionCeiling = 2L * 2 + 2;
    private const string ScalarAbstractionSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x >= 0);
                Contract.Ensures(Contract.Result<int>() == Contract.Result<int>());
                Contract.Ensures(Contract.Result<int>() == x);
                var unsupported = 1.5 * x;
                throw null!;
            }
        }
        """;
    private const string VoidAbstractionSource = """
        using SharpProof.Attributes;
        public static class Subject { public static void Target() {
            Contract.Ensures(true);
            Contract.Ensures(false);
            var unsupported = 0; System.Threading.Interlocked.Increment(ref unsupported);
            throw null!;
        } }
        """;

    [Test]
    public void InstructionBoundsHaveCompatiblePolicyRanges()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That((long)CompilerArtifactLimits.MaximumInstructions,
                Is.GreaterThanOrEqualTo((long)RoslynTotalProgramLowerer.MaximumRegionSteps));
            Assert.That((long)CompilerArtifactLimits.MaximumInstructions,
                Is.GreaterThanOrEqualTo(AccessorInstructionCeiling));
            Assert.That((long)CompilerArtifactLimits.MaximumInstructions,
                Is.LessThanOrEqualTo((int.MaxValue - 2L) / 2));
        }
    }

    [Test]
    public void AdmittedOrdinaryProgramRetainsTheFrontendInstructionBound()
    {
        const string source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes;" +
            "public static class Subject { [DoesNotThrow] public static int Root(int value) {" +
            "var result=value;result+=1;result+=1;result+=1;return result;} }";
        var compilation = Compile(source);
        Assert.That(Invoke(compilation, "Subject", static type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.GetMethod("Root")!.Invoke(null, [1]))),
            Is.EqualTo(4));
        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").Single();
        var preparation = Prepare(compilation, method);
        var total = preparation.Total;
        Assert.That(total, Is.Not.Null);
        Assert.That(total!.IsBodyAbstraction, Is.False);
        var count = Count(total.Program);
        Assert.That(count, Is.InRange(1L, (long)RoslynTotalProgramLowerer.MaximumRegionSteps));
        var parameter = total.Parameters.Single();
        var factory = total.Program.Factory;
        var execution = new IrProgramInterpreter(factory).Execute(total.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(parameter.Entry, factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, 1)));
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(4)));
        Observe(source, total, count);
    }

    [Test]
    public void AdmittedShadowProgramRetainsTheFrontendInstructionBound()
    {
        const string source = """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Positive(int value) { Contract.Requires(value > 0); return value; }
                public static int Root() { if (false) return Positive(-1); return 0; }
            }
            """;
        var compilation = Compile(source);
        Assert.That(Invoke(compilation, "Subject", static type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.GetMethod("Root")!.Invoke(null, null))),
            Is.EqualTo(0));
        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").Single();
        var identity = SemanticClaimIdentity.CreateCallableId(method);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken);
        Assert.That(batch.Gaps, Is.Empty);
        var body = batch.Callers.Single(caller => caller is null ? throw new System.ArgumentNullException(nameof(caller)) : (caller.Body.CallableId == identity)).Body;
        Assert.That(body.IsBodyAbstraction, Is.False);
        Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
        var count = Count(body.Program);
        Assert.That(count, Is.InRange(1L, (long)RoslynTotalProgramLowerer.MaximumRegionSteps));
        var execution = new IrProgramInterpreter(body.Program.Factory).Execute(body.Program);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
        Observe(source, body, count);
    }

    [Test]
    public void AdmittedScalarAbstractionUsesTheParameterInstructionFormula()
    { CheckAbstraction(ScalarAbstractionSource); }

    [Test]
    public void AdmittedVoidAbstractionUsesTheParameterInstructionFormula()
    { CheckAbstraction(VoidAbstractionSource); }

    [Test]
    public void AdmittedGetterRetainsTheReviewedAccessorCeiling()
    {
        CheckAccessor("using SharpProof.Attributes; public sealed class C { public int Value { " +
            "[EnforcePure, ZeroAllocations, DoesNotThrow] get; set; } }", getter: true);
    }

    [Test]
    public void AdmittedSetterRetainsTheReviewedAccessorCeiling()
    {
        CheckAccessor("using SharpProof.Attributes; public sealed class C { public int Value { " +
            "get; [EnforcePure] set; } }", getter: false);
    }

    private static void CheckAbstraction(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source);
        AssertEmits(compilation);
        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").Single();
        var preparation = Prepare(compilation, method);
        Assert.That(preparation.Entry.CallableId, Is.EqualTo(SemanticClaimIdentity.CreateCallableId(method)));
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        Assert.That(total.IsBodyAbstraction, Is.True);
        var count = Count(total.Program);
        Assert.That(count, Is.EqualTo(2L * method.Parameters.Length + 2));
        Assert.That(count, Is.LessThanOrEqualTo((long)CompilerArtifactLimits.MaximumInstructions));
        Observe(source, total, count);
    }

    private static void CheckAccessor(string source, bool getter)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source);
        Assert.That(Invoke(compilation, "C", static type =>
        {
            System.ArgumentNullException.ThrowIfNull(type);
            var instance = Activator.CreateInstance(type);
            var property = type.GetProperty("Value")!;
            property.SetValue(instance, 12);
            return property.GetValue(instance);
        }), Is.EqualTo(12));
        var property = (IPropertySymbol)compilation.GetTypeByMetadataName("C")!.GetMembers("Value").Single();
        var method = getter ? property.GetMethod! : property.SetMethod!;
        var preparation = Prepare(compilation, method);
        Assert.That(preparation.Entry.CallableId, Is.EqualTo(SemanticClaimIdentity.CreateCallableId(method)));
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        Assert.That(total.IsBodyAbstraction, Is.False);
        Assert.That(total.HasReceiver, Is.True);
        var count = Count(total.Program);
        Assert.That(count, Is.InRange(1L, AccessorInstructionCeiling));
        Assert.That(count, Is.LessThanOrEqualTo((long)CompilerArtifactLimits.MaximumInstructions));
        Observe(source, total, count);
    }

    private static CSharpCompilation Compile(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("InstructionBoundContract", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static CompilerCallablePreparation Prepare(CSharpCompilation compilation, IMethodSymbol method)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        System.ArgumentNullException.ThrowIfNull(compilation);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken), out var preparations, TestContext.CurrentContext.CancellationToken);
        var identity = SemanticClaimIdentity.CreateCallableId(method);
        return preparations.Single(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (preparation.Entry.CallableId == identity));
    }

    private static long Count(IrProgram program)
    {
        System.ArgumentNullException.ThrowIfNull(program);
        return program.Blocks.Sum(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : ((long)block.Instructions.Length));
    }

    private static void Observe(string source, CompilerTotalCallablePreparation total, long count)
    {
        System.ArgumentNullException.ThrowIfNull(total);
        System.ArgumentNullException.ThrowIfNull(source);
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            total.CallableId,
            total.IsBodyAbstraction,
            Instructions = count,
            ArtifactMaximum = CompilerArtifactLimits.MaximumInstructions,
            FrontendMaximum = RoslynTotalProgramLowerer.MaximumRegionSteps,
            AccessorMaximum = AccessorInstructionCeiling
        }));
    }

    private static void AssertEmits(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    private static object? Invoke(CSharpCompilation compilation, string typeName, Func<Type, object?> action)
    {
        System.ArgumentNullException.ThrowIfNull(action);
        System.ArgumentNullException.ThrowIfNull(typeName);
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var runtime = new AssemblyLoadContext("InstructionBoundContract", isCollectible: true);
        try
        { return action(runtime.LoadFromStream(image).GetType(typeName)!); }
        finally
        { runtime.Unload(); }
    }
}
