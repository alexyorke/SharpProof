using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class GenericTestedTypeAllocationAuditTests
{
    private const int WarmupCount = 10_000;
    private const int MeasurementCount = 4_096;
    private const int InputValue = 7;
    private const string Source = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;
        
        public static class Subject
        {
            [ZeroAllocations]
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool Target<T, U>(T value)
            {
                return value is U;
            }
        
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool Control(int value)
            {
                return value == 7;
            }
        }
        """ + "\n";

    [Test]
    public async Task GenericTestedTypeAllocationClaimMatchesMeasuredClr()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "generic-type-values-audit");
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), Source);
        var compilation = TestCompilation.Create("GenericTestedTypeAllocationOracle", ("Subject.cs", Source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var runtime = new AssemblyLoadContext("GenericTestedTypeAllocationOracle", isCollectible: true);
        ImmutableArray<CaseObservation> cases;
        CaseObservation control;
        try
        {
            var subject = runtime.LoadFromStream(image).GetType("Subject")!;
            var generic = subject.GetMethod("Target")!;
            var objectTarget = generic.MakeGenericMethod(typeof(int), typeof(object)).CreateDelegate<Func<int, bool>>();
            var interfaceTarget = generic.MakeGenericMethod(typeof(int), typeof(IComparable)).CreateDelegate<Func<int, bool>>();
            var stringTarget = generic.MakeGenericMethod(typeof(int), typeof(string)).CreateDelegate<Func<int, bool>>();
            var controlTarget = subject.GetMethod("Control")!.CreateDelegate<Func<int, bool>>();
            var il = generic.GetMethodBody()!.GetILAsByteArray()!;
            var instructions = ReadIl(il);
            cases =
            [
                Measure("int/object", objectTarget, expected: true),
                Measure("int/IComparable", interfaceTarget, expected: true),
                Measure("int/string", stringTarget, expected: false)
            ];
            control = Measure("control", controlTarget, expected: true);
            var hasBox = instructions.Any(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.OpCode == "box"));
            var hasIsinst = instructions.Any(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.OpCode == "isinst"));
            var noInlining = (generic.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0;
            await Save(evidenceDirectory, "runtime-observations.json", new
            {
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                OperatingSystem = RuntimeInformation.OSDescription,
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                WarmupCount,
                MeasurementCount,
                InputValue,
                NoInlining = noInlining,
                HasBox = hasBox,
                HasIsinst = hasIsinst,
                IlHex = Convert.ToHexString(il),
                Instructions = instructions,
                Cases = cases,
                Control = control,
                TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                TieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                RuntimeCoverage = "One Release compilation, current runtime/tiering configuration, three closed reference-type U cases and one control; no JIT machine-code disassembly."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(noInlining, Is.True);
                Assert.That(hasBox, Is.True, "Record the actual generic box opcode, not a raw-byte match.");
                Assert.That(hasIsinst, Is.True, "Record the actual generic isinst opcode, not a raw-byte match.");
                foreach (var observed in cases.Append(control))
                {
                    Assert.That(observed.Actual, Is.EqualTo(observed.Expected), observed.Name);
                    Assert.That(observed.TrueResults, Is.EqualTo(observed.Expected ? MeasurementCount : 0), observed.Name);
                    Assert.That(observed.AllocatedBytes, Is.GreaterThanOrEqualTo(0), observed.Name);
                }
                Assert.That(control.AllocatedBytes, Is.Zero, "The typed-delegate measurement loop must not allocate.");
            }
        }
        finally
        {
            runtime.Unload();
        }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        await Save(evidenceDirectory, "native-observations.json", new
        {
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            Errors = response.Errors,
            Claims = response.ClaimResults.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (new { Outcome = claim.Outcome.ToString(), Reason = claim.Reason.ToString() })).ToImmutableArray(),
            Cases = cases,
            Control = control,
            AnyObservedAllocation = cases.Any(observed => observed is null ? throw new System.ArgumentNullException(nameof(observed)) : (observed.AllocatedBytes > 0)),
            FrozenRule = "Observed allocation > 0 implies the allocation-free claim cannot be Proven. Zero measured allocation rejects this hypothesis for the tested runtime."
        });
        Assert.That(response.Errors, Is.Empty);
        Assert.That(project.Request.Cache.Enabled, Is.False);
        var claim = response.ClaimResults.Single();
        foreach (var observed in cases)
        {
            var message = $"{observed.Name}: bytes{MeasurementCount}={observed.AllocatedBytes}; native={claim.Outcome}/{claim.Reason}";
            TestContext.WriteLine(message);
            Assert.That(observed.AllocatedBytes == 0 || claim.Outcome != WorkerClaimOutcome.Proven, Is.True, message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CaseObservation Measure(string name, Func<int, bool> target, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(target);
        var actual = target(InputValue);
        for (var index = 0; index < WarmupCount; index++)
        {
            _ = target(InputValue);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var trueResults = 0;
        for (var index = 0; index < MeasurementCount; index++)
        {
            if (target(InputValue))
            {
                trueResults++;
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return new(name, expected, actual, trueResults, allocated, allocated / (double)MeasurementCount);
    }

    private static ImmutableArray<IlInstruction> ReadIl(ReadOnlySpan<byte> bytes /* SPINT001: all values valid - an empty IL span yields an empty instruction vector */)
    {
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is null ? throw new System.ArgumentNullException(nameof(field)) : (field.FieldType == typeof(OpCode)))
            .Select(field => field is null ? throw new System.ArgumentNullException(nameof(field)) : ((OpCode)field.GetValue(null)!))
            .ToImmutableDictionary(opcode /* SPINT001: all values valid - all opcode values, including default nop, can be inspected */ => unchecked((ushort)opcode.Value));
        var instructions = ImmutableArray.CreateBuilder<IlInstruction>();
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
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes.Slice(position, sizeof(int))),
                _ => 4
            };
            instructions.Add(new(offset, opcode.Name!, Convert.ToHexString(bytes.Slice(position, operandSize))));
            position += operandSize;
        }
        return instructions.ToImmutable();
    }

    private static Task Save(string directory, string name, object value)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(value);
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed record CaseObservation(string Name, bool Expected, bool Actual, int TrueResults,
        long AllocatedBytes, double BytesPerCall)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
    }

    private sealed record IlInstruction(int Offset, string OpCode, string OperandHex)
    {
        public string OpCode { get; init; } = OpCode ?? throw new System.ArgumentNullException(nameof(OpCode));
        public string OperandHex { get; init; } = OperandHex ?? throw new System.ArgumentNullException(nameof(OperandHex));
    }
}
