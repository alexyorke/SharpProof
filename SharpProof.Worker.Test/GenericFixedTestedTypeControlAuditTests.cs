using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class GenericFixedTestedTypeControlAuditTests
{
    private const int WarmupCount = 10_000;
    private const int MeasurementCount = 4_096;
    private const int InputGuard = 1;
    private const string SourceTemplate = """
        using System;
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;
        
        public static class Subject
        {
            [ZeroAllocations]
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool Target<T>(T value, int guard)
            {
                return value is TESTED_TYPE;
            }
        
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool Control<T>(T value, int guard)
            {
                return guard == 1;
            }
        }
        """;
    private static readonly string[] Forms = ["fixed-object", "fixed-IComparable", "fixed-IComparable-int"];

    [Test]
    public async Task FixedTestedTypesMatchSampledPreservationAndAllocationImplication()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var runLabel = Environment.GetEnvironmentVariable("SHARPPROOF_GENERIC_FIXED_TYPE_EVIDENCE_RUN") ?? "observation-" + Guid.NewGuid().ToString("N");
        Assert.That(runLabel, Does.Match("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"),
            "A fresh observation-run directory is required.");
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "generic-type-values-audit",
            "fixed-tested-type-readiness", "observations", runLabel!);
        Assert.That(Directory.Exists(evidenceDirectory), Is.False, "Preserve every previous observation run.");
        Directory.CreateDirectory(evidenceDirectory);
        var observations = new List<BoundaryObservation>();
        foreach (var form in Forms)
        {
            observations.Add(await ObserveForm(form, evidenceDirectory));
        }
        using (Assert.EnterMultipleScope())
        {
            foreach (var observation in observations)
            {
                foreach (var measured in observation.Cases)
                {
                    Assert.That(measured.Target.AllocatedBytes == 0 || observation.Outcome != WorkerClaimOutcome.Proven, Is.True,
                        $"{measured.Name}: bytes{MeasurementCount}={measured.Target.AllocatedBytes}; " +
                        $"native={observation.Outcome}/{observation.Reason}");
                }
                if (observation.Cases.All(measured => measured.Target.AllocatedBytes == 0))
                {
                    Assert.That(observation.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                        observation.Boundary + ": existing-behavior preservation control for the four sampled inputs; " +
                        "zero observations do not prove zero allocation for every T or runtime.");
                }
            }
        }
    }

    private static async Task<BoundaryObservation> ObserveForm(string form, string evidenceDirectory)
    {
        var source = SourceFor(form);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Assert.That(sourceHash, Is.EqualTo(ExpectedSourceHash(form)), "Frozen fixed-type subject changed.");
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, form + "-subject.cs"), source);
        var compilation = TestCompilation.Create("GenericFixedTestedType_" + form, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var runtime = new AssemblyLoadContext("GenericFixedTestedType_" + form, isCollectible: true);
        CaseObservation[] cases;
        try
        {
            var subject = runtime.LoadFromStream(image).GetType("Subject")!;
            var target = subject.GetMethod("Target")!;
            var control = subject.GetMethod("Control")!;
            cases = ObserveCases(form, target, control);
            var flags = target.GetMethodImplementationFlags();
            var controlFlags = control.GetMethodImplementationFlags();
            var il = target.GetMethodBody()!.GetILAsByteArray()!;
            var instructions = ReadIl(il);
            await Save(evidenceDirectory, form + "-runtime.json", new
            {
                Form = form,
                SourceSha256 = sourceHash,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                WarmupCount,
                MeasurementCount,
                InputGuard,
                TypedDelegate = "Func<TValue,int,bool>; T=int, int?, string",
                TargetNoInlining = (flags & MethodImplAttributes.NoInlining) != 0,
                TargetNoOptimization = (flags & MethodImplAttributes.NoOptimization) != 0,
                ControlNoInlining = (controlFlags & MethodImplAttributes.NoInlining) != 0,
                ControlNoOptimization = (controlFlags & MethodImplAttributes.NoOptimization) != 0,
                HasBox = instructions.Any(instruction => instruction.OpCode == "box"),
                HasIsinst = instructions.Any(instruction => instruction.OpCode == "isinst"),
                IlHex = Convert.ToHexString(il),
                Instructions = instructions,
                Cases = cases,
                TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                TieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                ByteExpectation = "Subject counts and IL opcodes are observations; no zero or positive amount is assumed. Typed control bytes must be zero.",
                Coverage = "Three fixed tested types and four typed inputs each on this runtime/tiering configuration; no JIT machine-code disassembly."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That((flags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((controlFlags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((flags & MethodImplAttributes.NoOptimization) != 0, Is.False);
                Assert.That((controlFlags & MethodImplAttributes.NoOptimization) != 0, Is.False);
                foreach (var observed in cases)
                {
                    AssertMeasurement(observed.Name, observed.Target);
                    AssertMeasurement(observed.Name + "/control", observed.Control);
                    Assert.That(observed.Control.AllocatedBytes, Is.Zero, observed.Name + ": typed-loop control.");
                }
            }
        }
        finally
        {
            runtime.Unload();
        }
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await Save(evidenceDirectory, form + "-native.json", new
        {
            Form = form,
            SourceSha256 = sourceHash,
            SameCompilationForClrAndNative = true,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            Errors = response.Errors,
            ManifestClaims = discovery.Manifest.Claims.Select(claim => new
            {
                claim.ClaimId,
                Kind = claim.Kind.ToString(),
                ContractKind = claim.EffectContractKind.ToString()
            }).ToArray(),
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString()
            }).ToArray(),
            Cases = cases,
            AnyObservedAllocation = cases.Any(observed => observed.Target.AllocatedBytes > 0),
            AllFourObservedZero = cases.All(observed => observed.Target.AllocatedBytes == 0),
            FrozenCorrectnessRule = "Any observed allocation > 0 implies the corresponding universal ZeroAllocations claim cannot be Proven.",
            FrozenPreservationControl = "When all four sampled inputs are zero-byte observations, preserve existing Proven behavior; sampling does not establish zero allocation for every T or runtime."
        });
        Assert.That(response.Errors, Is.Empty);
        Assert.That(project.Request.Cache.Enabled, Is.False);
        Assert.That(discovery.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(discovery.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.ZeroAllocations));
        Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
        var claim = response.ClaimResults.Single();
        Assert.That(claim.ClaimId, Is.EqualTo(discovery.Manifest.Claims.Single().ClaimId));
        return new(form, cases, claim.Outcome, claim.Reason);
    }

    private static CaseObservation[] ObserveCases(string form, MethodInfo target, MethodInfo control)
    {
        return
        [
            Observe<int>(form + "/int7", form, target, control, 7, expected: true),
            Observe<int?>(form + "/nullable7", form, target, control, 7, expected: true),
            Observe<int?>(form + "/nullable-null", form, target, control, null, expected: false),
            Observe<string>(form + "/string-text", form, target, control, "text", expected: form != "fixed-IComparable-int")
        ];
    }

    private static CaseObservation Observe<TValue>(string name, string testedType, MethodInfo target, MethodInfo control,
        TValue value, bool expected)
    {
        var typedTarget = target.MakeGenericMethod(typeof(TValue)).CreateDelegate<Func<TValue, int, bool>>();
        var typedControl = control.MakeGenericMethod(typeof(TValue)).CreateDelegate<Func<TValue, int, bool>>();
        return new(name, typeof(TValue).ToString(), testedType,
            Measure(typedTarget, value, expected), Measure(typedControl, value, expected: true));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Measurement Measure<TValue>(Func<TValue, int, bool> target, TValue value, bool expected)
    {
        var actual = target(value, InputGuard);
        for (var index = 0; index < WarmupCount; index++)
        {
            _ = target(value, InputGuard);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var trueResults = 0;
        for (var index = 0; index < MeasurementCount; index++)
        {
            if (target(value, InputGuard))
            {
                trueResults++;
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return new(expected, actual, trueResults, allocated, allocated / (double)MeasurementCount);
    }

    private static void AssertMeasurement(string name, Measurement measured)
    {
        Assert.That(measured.Actual, Is.EqualTo(measured.Expected), name);
        Assert.That(measured.TrueResults, Is.EqualTo(measured.Expected ? MeasurementCount : 0), name);
        Assert.That(measured.AllocatedBytes, Is.GreaterThanOrEqualTo(0), name);
    }
    private static string SourceFor(string form)
    {
        var testedType = form switch
        {
            "fixed-object" => "object",
            "fixed-IComparable" => "IComparable",
            "fixed-IComparable-int" => "IComparable<int>",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        return SourceTemplate.Replace("TESTED_TYPE", testedType, StringComparison.Ordinal) + "\n";
    }

    private static string ExpectedSourceHash(string form)
    {
        return form switch
        {
            "fixed-object" => "34990D0DED79F605E5561C15EC30D084EDBAD12F21AD8235CDA426A95313115A",
            "fixed-IComparable" => "A5943D66E4DC642FF1735A6CA4F5126EBF85C0A62EE223DEEB5C2FF53CEB0E68",
            "fixed-IComparable-int" => "26A73836FE5664F0C6A2193C0CCDB39D9399DEAF5784A958056ADF21CF125167",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
    }

    private static IlInstruction[] ReadIl(byte[] bytes)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        var instructions = new List<IlInstruction>();
        for (var position = 0; position < bytes.Length;)
        {
            var offset = position;
            ushort code = bytes[position++];
            if (code == 0xFE)
            {
                code = (ushort)(0xFE00 | bytes[position++]);
            }
            var opcode = opcodes[code];
            var operandSize = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, position),
                _ => 4
            };
            instructions.Add(new(offset, opcode.Name!, Convert.ToHexString(bytes.AsSpan(position, operandSize))));
            position += operandSize;
        }
        return [.. instructions];
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed record BoundaryObservation(string Boundary, CaseObservation[] Cases, WorkerClaimOutcome Outcome, WorkerClaimReason Reason);

    private sealed record CaseObservation(string Name, string ValueType, string TestedType, Measurement Target, Measurement Control);

    private sealed record Measurement(bool Expected, bool Actual, int TrueResults, long AllocatedBytes, double BytesPerCall);

    private sealed record IlInstruction(int Offset, string OpCode, string OperandHex);
}
