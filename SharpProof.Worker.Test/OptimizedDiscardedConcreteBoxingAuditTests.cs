using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class OptimizedDiscardedConcreteBoxingAuditTests
{
    private const int WarmupIterations = 10000;
    private const int MeasuredIterations = 128;
    private const int MeasurementRounds = 3;
    private static readonly decimal[] WitnessValues = [0m, 1.25m];
    private static readonly byte[] IdentityBody = [0x02, 0x2A];
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => unchecked((ushort)code.Value))
        .ToDictionary(group => group.Key, group => group.First());

    private const string BoundarySource = """
        using SharpProof.Attributes;
        using System.Runtime.CompilerServices;
        public static class Boundary {
            [SharpProofTrusted("Reviewed identity body: return the supplied object reference.")]
            [EffectContract(SharpProofEffect.None, Complete = true, PreconditionFree = true)]
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static object Echo(object value) => value;
        }
        """ + "\n";

    private const string SourceTemplate = """
        #undef SHARPPROOF_CONTRACTS
        using SharpProof.Attributes;
        using System.Runtime.CompilerServices;
        public static class Subject {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object SourceEcho(object value) => value;
            [ZeroAllocations, MethodImpl(MethodImplOptions.NoInlining)]
            public static int Target(decimal value, object sentinel) {
                __TARGET_BODY__
                return 0;
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Control(decimal value, object sentinel) {
                __CONTROL_BODY__
                return 0;
            }
        }
        """ + "\n";

    [TestCase("direct")]
    [TestCase("source")]
    [TestCase("opaque")]
    public async Task DiscardedBoxingVerdictRequiresObservedAllocationEvidence(string shape)
    {
        var bodies = shape switch
        {
            "direct" => (Target: "_ = (object)value;", Control: "_ = value;"),
            "source" => (Target: "_ = SourceEcho((object)value);", Control: "_ = SourceEcho(sentinel);"),
            "opaque" => (Target: "_ = Boundary.Echo((object)value);", Control: "_ = Boundary.Echo(sentinel);"),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var source = SourceTemplate.Replace("__TARGET_BODY__", bodies.Target, StringComparison.Ordinal)
            .Replace("__CONTROL_BODY__", bodies.Control, StringComparison.Ordinal);
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "optimized-discarded-boxing-audit",
            "runtime", shape + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "Subject.cs"), source);
        await File.WriteAllTextAsync(Path.Combine(evidence, "Boundary.cs"), BoundarySource);

        var external = TestCompilation.Create("OptimizedDiscardBoundary_" + shape, ("Boundary.cs", BoundarySource));
        external = external.WithOptions(external.Options.WithOptimizationLevel(OptimizationLevel.Release));
        using var directory = new TempDirectory("sharpproof-optimized-boxing");
        var boundaryPath = Path.Combine(directory.FullName, "Boundary.dll");
        var externalEmission = external.Emit(boundaryPath);
        Assert.That(externalEmission.Success, Is.True, string.Join("\n", externalEmission.Diagnostics));
        var boundaryBytes = await File.ReadAllBytesAsync(boundaryPath);
        await File.WriteAllBytesAsync(Path.Combine(evidence, "boundary.dll"), boundaryBytes);
        var tree = CSharpSyntaxTree.ParseText(source, (CSharpParseOptions)external.SyntaxTrees.Single().Options, "Subject.cs");
        var compilation = CSharpCompilation.Create("OptimizedDiscardCaller_" + shape, [tree],
            external.References.Append(MetadataReference.CreateFromFile(boundaryPath)), external.Options);
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var cast = (IConversionOperation)model.GetOperation(targetSyntax.DescendantNodes().OfType<CastExpressionSyntax>().Single())!;
        Assert.That(cast.Type!.SpecialType, Is.EqualTo(SpecialType.System_Object));
        Assert.That(cast.Operand.Type!.SpecialType, Is.EqualTo(SpecialType.System_Decimal));
        Assert.That(cast.Conversion.Exists, Is.True);
        Assert.That(cast.OperatorMethod, Is.Null);
        Assert.That(cast.IsImplicit, Is.False);
        Assert.That(cast.IsTryCast, Is.False);
        if (shape != "direct")
        {
            var call = (IInvocationOperation)model.GetOperation(targetSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single())!;
            Assert.That(call.TargetMethod.IsStatic, Is.True);
            Assert.That(call.TargetMethod.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Object));
            Assert.That(call.TargetMethod.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Object));
            Assert.That(call.Arguments.Single().ArgumentKind, Is.EqualTo(ArgumentKind.Explicit));
            Assert.That(call.TargetMethod.Name, Is.EqualTo(shape == "source" ? "SourceEcho" : "Echo"));
            Assert.That(call.TargetMethod.ContainingAssembly.Identity.Name,
                Is.EqualTo(shape == "source" ? compilation.AssemblyName : external.AssemblyName));
            Assert.That(call.TargetMethod.DeclaringSyntaxReferences.Length, Is.EqualTo(shape == "source" ? 1 : 0));
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-binding.txt"),
            $"shape={shape}; assembly={compilation.Assembly.Identity}; optimization={compilation.Options.OptimizationLevel}; " +
            $"compiler={typeof(CSharpCompilation).Assembly.GetName().Version}; cast={cast.Syntax}; " +
            $"operand={cast.Operand.Type}; target={cast.Type}; explicit={!cast.IsImplicit}; " +
            $"sourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(source))}; " +
            $"boundarySourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(BoundarySource))}\n");

        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "oracle.dll"), imageBytes);
        image.Position = 0;
        var observations = new List<RuntimeObservation>();
        var runtime = new AssemblyLoadContext("OptimizedDiscardOracle_" + shape, isCollectible: true);
        try
        {
            using var externalImage = new MemoryStream(boundaryBytes);
            var boundaryMethod = runtime.LoadFromStream(externalImage).GetType("Boundary")!.GetMethod("Echo")!;
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var targetMethod = type.GetMethod("Target")!;
            var controlMethod = type.GetMethod("Control")!;
            var sourceMethod = type.GetMethod("SourceEcho", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var method in new[] { targetMethod, controlMethod, sourceMethod, boundaryMethod })
            { AssertNoInliningOnly(method); }
            Assert.That(boundaryMethod.GetMethodBody()!.GetILAsByteArray(), Is.EqualTo(IdentityBody));
            Assert.That(sourceMethod.GetMethodBody()!.GetILAsByteArray(), Is.EqualTo(IdentityBody));
            var il = new[] { targetMethod, controlMethod, sourceMethod, boundaryMethod }.Select(DescribeIl).ToArray();
            await File.WriteAllTextAsync(Path.Combine(evidence, "decoded-il.json"),
                JsonSerializer.Serialize(il, WorkerProtocolJson.SharedOptions));
            var target = targetMethod.CreateDelegate<Func<decimal, object, int>>();
            var control = controlMethod.CreateDelegate<Func<decimal, object, int>>();
            var sentinel = new object();
            foreach (var witness in WitnessValues)
            {
                for (var repeat = 0; repeat < WarmupIterations; repeat++)
                {
                    _ = target(witness, sentinel);
                    _ = control(witness, sentinel);
                }
                for (var round = 0; round < MeasurementRounds; round++)
                {
                    var before = Measure(control, witness, sentinel);
                    var subject = Measure(target, witness, sentinel);
                    var after = Measure(control, witness, sentinel);
                    observations.Add(new(witness.ToString(CultureInfo.InvariantCulture), round,
                        subject.Bytes, subject.Result, before.Bytes, before.Result, after.Bytes, after.Result));
                }
            }
        }
        finally { runtime.Unload(); }
        await File.WriteAllTextAsync(Path.Combine(evidence, "clr-measurements.json"),
            JsonSerializer.Serialize(observations, WorkerProtocolJson.SharedOptions));
        await File.WriteAllTextAsync(Path.Combine(evidence, "compilation-identity.txt"),
            $"assembly={compilation.Assembly.Identity}; PE_SHA256={WorkerProtocolJson.ComputeSha256(imageBytes)}; " +
            $"boundaryAssembly={external.Assembly.Identity}; boundaryPE_SHA256={WorkerProtocolJson.ComputeSha256(boundaryBytes)}; " +
            $"warmup={WarmupIterations}; measured={MeasuredIterations}; rounds={MeasurementRounds}; " +
            $"framework={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
            $"architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; " +
            $"DOTNET_TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<unset>"}; " +
            $"COMPlus_TieredCompilation={Environment.GetEnvironmentVariable("COMPlus_TieredCompilation") ?? "<unset>"}\n");

        // Native proof and the optimized CLR oracle use the same compilation.
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        var callableId = artifact.Manifest.Claims.Single().CallableId;
        var captured = artifact.Callables.Single(callable => callable.CallableId == callableId);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        var preparation = project.Snapshot.Callables.Single(callable => callable.EffectClaims.Any(effect => effect.ClaimId == claim.ClaimId));
        var reachedCandidateSites = preparation.Total?.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAllocationInstruction>().Count();
        var observedAllocation = observations.Any(row => row.SubjectBytes > 0);
        var allSubjectZero = observations.All(row => row.SubjectBytes == 0);
        var observation = $"shape={shape}; native={claim.Outcome}; reason={claim.Reason}; vacuity={claim.Vacuity}; " +
            $"capturedTotal={captured.Total != null}; bodyAbstraction={captured.Total?.IsBodyAbstraction}; " +
            $"candidateAllocationSites={reachedCandidateSites}; anyPositiveBytes={observedAllocation}; allSubjectZero={allSubjectZero}; " +
            $"zeroByteRefutedNeedsWitnessReview={allSubjectZero && claim.Outcome == WorkerClaimOutcome.Refuted}; evidence={evidence}";
        await File.WriteAllTextAsync(Path.Combine(evidence, "observation.txt"), observation + "\n");
        TestContext.WriteLine(observation);
        Assert.That(observations.All(row => row.BeforeControlBytes == 0 && row.AfterControlBytes == 0), Is.True,
            "Signature-matched preboxed/no-box controls must calibrate to zero bytes.");
        Assert.That(observations.All(row => row.SubjectResult == 0 && row.BeforeControlResult == 0 && row.AfterControlResult == 0), Is.True);
        if (observedAllocation)
        {
            Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                "A positive warmed CLR allocation witness cannot receive a valid allocation-free proof. " + observation);
        }
        // Zero bytes at these finite inputs do not materialize an opaque native
        // counterexample or justify a universal no-allocation/refutation claim.
    }

    private static (long Bytes, int Result) Measure(Func<decimal, object, int> target, decimal value, object sentinel)
    {
        var result = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < MeasuredIterations; repeat++)
        { result = target(value, sentinel); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(target);
        GC.KeepAlive(sentinel);
        return (bytes, result);
    }

    private static void AssertNoInliningOnly(MethodInfo method)
    {
        var flags = method.GetMethodImplementationFlags();
        Assert.That(flags & MethodImplAttributes.NoInlining, Is.EqualTo(MethodImplAttributes.NoInlining));
        Assert.That(flags & MethodImplAttributes.NoOptimization, Is.EqualTo((MethodImplAttributes)0));
    }

    private static IlObservation DescribeIl(MethodInfo method)
    {
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var rows = new List<IlInstruction>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var start = offset;
            ushort value = bytes[offset++];
            if (value == 0xFE)
            { value = (ushort)(0xFE00 | bytes[offset++]); }
            if (!OpCodesByValue.TryGetValue(value, out var code))
            { throw new InvalidOperationException("Unknown emitted IL opcode."); }
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
            if (offset + size > bytes.Length)
            { throw new InvalidOperationException("Truncated emitted IL operand."); }
            var operand = code.OperandType switch
            {
                OperandType.InlineMethod => method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset))?.ToString(),
                OperandType.InlineType => method.Module.ResolveType(BitConverter.ToInt32(bytes, offset)).ToString(),
                OperandType.InlineField => method.Module.ResolveField(BitConverter.ToInt32(bytes, offset))?.ToString(),
                OperandType.InlineTok => method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset))?.ToString(),
                _ => null
            };
            rows.Add(new(start, code.Name ?? "<unnamed>", Convert.ToHexString(bytes.AsSpan(offset, size)), operand));
            offset += size;
        }
        return new(method.DeclaringType!.FullName + "." + method.Name, method.Module.ModuleVersionId,
            method.MetadataToken, method.GetMethodImplementationFlags().ToString(), Convert.ToHexString(bytes), rows);
    }

    private sealed record RuntimeObservation(string Witness, int Round, long SubjectBytes, int SubjectResult,
        long BeforeControlBytes, int BeforeControlResult, long AfterControlBytes, int AfterControlResult);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, string? ResolvedOperand);
    private sealed record IlObservation(string Method, Guid ModuleVersionId, int MetadataToken, string ImplementationFlags,
        string RawIl, List<IlInstruction> Instructions);
}
