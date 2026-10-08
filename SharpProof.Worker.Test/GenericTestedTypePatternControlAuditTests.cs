using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class GenericTestedTypePatternControlAuditTests
{
    private const int WarmupCount = 10_000;
    private const int MeasurementCount = 4_096;
    private const int InputValue = 7;
    private static readonly string[] ClosedRuntimeTypes = ["System.Int32", "System.Object"];

    [Test]
    public async Task AdmittedOpenTestedTypePatternsRespectMeasuredAllocation()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var runLabel = Environment.GetEnvironmentVariable("SHARPPROOF_GENERIC_PATTERN_EVIDENCE_RUN") ?? "observation-" + Guid.NewGuid().ToString("N");
        Assert.That(runLabel, Does.Match("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"),
            "A fresh observation-run directory is required.");
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "generic-type-values-audit",
            "pattern-readiness", "observations", runLabel!);
        Assert.That(Directory.Exists(evidenceDirectory), Is.False, "Preserve every previous observation run.");
        Directory.CreateDirectory(evidenceDirectory);
        var observations = new List<PatternObservation>();
        foreach (var form in new[] { "discard-declaration", "parenthesized-type" })
        {
            observations.Add(await ObserveForm(form, evidenceDirectory));
        }
        using (Assert.EnterMultipleScope())
        {
            foreach (var observation in observations)
            {
                Assert.That(observation.Target.AllocatedBytes == 0 || observation.Outcome != WorkerClaimOutcome.Proven, Is.True,
                    $"{observation.Form}: bytes{MeasurementCount}={observation.Target.AllocatedBytes}; " +
                    $"native={observation.Outcome}/{observation.Reason}");
            }
        }
    }

    private static async Task<PatternObservation> ObserveForm(string form, string evidenceDirectory)
    {
        var source = SourceFor(form);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Assert.That(sourceHash, Is.EqualTo(ExpectedSourceHash(form)), "Frozen pattern source changed.");
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, form + "-subject.cs"), source);
        var compilation = TestCompilation.Create("GenericTestedTypePattern_" + form, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var root = await tree.GetRootAsync(CancellationToken.None);
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node.Identifier.ValueText == "Target");
        var expression = method.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!;
        var operation = model.GetOperation(expression);
        var graph = ControlFlowGraph.Create(method, model, CancellationToken.None)!;
        var flowPatterns = graph.Blocks.Select(block => block.BranchValue).OfType<IIsPatternOperation>().ToArray();
        var expectedPattern = form == "discard-declaration" ? OperationKind.DeclarationPattern : OperationKind.TypePattern;
        await Save(evidenceDirectory, form + "-shape.json", new
        {
            SourceSha256 = sourceHash,
            ExpectedPattern = expectedPattern.ToString(),
            Operation = Shape(operation),
            FlowBranches = graph.Blocks.Select(block => new { block.Ordinal, Shape = Shape(block.BranchValue) }).ToArray(),
            Roslyn = typeof(IOperation).Assembly.GetName().Version!.ToString(),
            Role = "Compiler/CFG setup qualification; shape mismatch is not a product allocation red."
        });
        AssertShape(operation, expectedPattern);
        Assert.That(flowPatterns, Has.Length.EqualTo(1), form + ": one admitted pattern in return CFG.");
        AssertShape(flowPatterns.Single(), expectedPattern);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var runtime = new AssemblyLoadContext("GenericTestedTypePattern_" + form, isCollectible: true);
        CaseObservation measured;
        CaseObservation control;
        try
        {
            var subject = runtime.LoadFromStream(image).GetType("Subject")!;
            var generic = subject.GetMethod("Target")!;
            var controlMethod = subject.GetMethod("Control")!;
            var target = generic.MakeGenericMethod(typeof(int), typeof(object)).CreateDelegate<Func<int, bool>>();
            var typedControl = controlMethod.CreateDelegate<Func<int, bool>>();
            measured = Measure(form + "/int/object", target, expected: true);
            control = Measure(form + "/typed-control", typedControl, expected: true);
            var flags = generic.GetMethodImplementationFlags();
            var controlFlags = controlMethod.GetMethodImplementationFlags();
            var il = generic.GetMethodBody()!.GetILAsByteArray()!;
            var instructions = ReadIl(il);
            var hasBox = instructions.Any(instruction => instruction.OpCode == "box");
            var hasIsinst = instructions.Any(instruction => instruction.OpCode == "isinst");
            await Save(evidenceDirectory, form + "-runtime.json", new
            {
                SourceSha256 = sourceHash,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                WarmupCount,
                MeasurementCount,
                InputValue,
                TypedDelegate = "Func<int,bool>",
                ClosedTypes = ClosedRuntimeTypes,
                TargetNoInlining = (flags & MethodImplAttributes.NoInlining) != 0,
                TargetNoOptimization = (flags & MethodImplAttributes.NoOptimization) != 0,
                ControlNoInlining = (controlFlags & MethodImplAttributes.NoInlining) != 0,
                ControlNoOptimization = (controlFlags & MethodImplAttributes.NoOptimization) != 0,
                HasBox = hasBox,
                HasIsinst = hasIsinst,
                IlHex = Convert.ToHexString(il),
                Instructions = instructions,
                Target = measured,
                Control = control,
                TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                TieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                ByteExpectation = "Subject bytes are observations with no required zero or positive amount; typed control bytes must be zero.",
                Coverage = "Current-thread managed bytes on this runtime/tiering configuration; no JIT machine-code disassembly."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That((flags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((controlFlags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((flags & MethodImplAttributes.NoOptimization) != 0, Is.False);
                Assert.That((controlFlags & MethodImplAttributes.NoOptimization) != 0, Is.False);
                Assert.That(hasBox, Is.True, form + ": decoded generic box opcode.");
                Assert.That(hasIsinst, Is.True, form + ": decoded generic isinst opcode.");
                foreach (var observation in new[] { measured, control })
                {
                    Assert.That(observation.Actual, Is.EqualTo(observation.Expected), observation.Name);
                    Assert.That(observation.TrueResults, Is.EqualTo(MeasurementCount), observation.Name);
                    Assert.That(observation.AllocatedBytes, Is.GreaterThanOrEqualTo(0), observation.Name);
                }
                Assert.That(control.AllocatedBytes, Is.Zero, form + ": typed-delegate loop control.");
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
            Target = measured,
            Control = control,
            FrozenRule = "Observed allocation > 0 implies ZeroAllocations cannot be Proven. Zero measured allocation rejects this hypothesis for the tested form/runtime."
        });
        Assert.That(response.Errors, Is.Empty);
        Assert.That(project.Request.Cache.Enabled, Is.False);
        Assert.That(discovery.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(discovery.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.ZeroAllocations));
        Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
        var claim = response.ClaimResults.Single();
        Assert.That(claim.ClaimId, Is.EqualTo(discovery.Manifest.Claims.Single().ClaimId));
        return new(form, measured, claim.Outcome, claim.Reason);
    }

    private static object Shape(IOperation? operation)
    {
        var test = operation as IIsPatternOperation;
        var matchedType = test?.Pattern switch
        {
            IDeclarationPatternOperation declaration => declaration.MatchedType,
            ITypePatternOperation type => type.MatchedType,
            _ => null
        };
        return new
        {
            Kind = operation?.Kind.ToString(),
            PatternKind = test?.Pattern.Kind.ToString(),
            ValueType = test?.Value.Type?.ToDisplayString(),
            ValueTypeParameter = test?.Value.Type is ITypeParameterSymbol,
            ValueIsReferenceType = test?.Value.Type?.IsReferenceType,
            MatchedType = matchedType?.ToDisplayString(),
            MatchedTypeParameter = matchedType is ITypeParameterSymbol,
            DeclarationSymbolNull = test?.Pattern is IDeclarationPatternOperation declaredPattern ? declaredPattern.DeclaredSymbol == null : (bool?)null
        };
    }

    private static void AssertShape(IOperation? operation, OperationKind expectedPattern)
    {
        Assert.That(operation, Is.InstanceOf<IIsPatternOperation>());
        var test = (IIsPatternOperation)operation!;
        Assert.That(test.Pattern.Kind, Is.EqualTo(expectedPattern));
        Assert.That(test.Value.Type, Is.InstanceOf<ITypeParameterSymbol>());
        Assert.That(test.Value.Type!.Name, Is.EqualTo("T"));
        Assert.That(test.Value.Type.IsReferenceType, Is.False);
        var matchedType = test.Pattern switch
        {
            IDeclarationPatternOperation declaration => declaration.MatchedType,
            ITypePatternOperation type => type.MatchedType,
            _ => null
        };
        Assert.That(matchedType, Is.InstanceOf<ITypeParameterSymbol>());
        Assert.That(matchedType!.Name, Is.EqualTo("U"));
        if (test.Pattern is IDeclarationPatternOperation declarationPattern)
        {
            Assert.That(declarationPattern.DeclaredSymbol, Is.Null);
            Assert.That(declarationPattern.MatchesNull, Is.False);
        }
    }

    private static string ExpectedSourceHash(string form)
    {
        return form switch
        {
            "discard-declaration" => "8E608264C9B54CF1FE2043F898F7A0AE0F85335A3558F1FB97E20604CB0E91C7",
            "parenthesized-type" => "08A21D80F6FE43307814070D69B50E39ECD51095DAE24D6A50F064114A5A0F1E",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
    }

    private static string SourceFor(string form)
    {
        return form switch
        {
            "discard-declaration" => """
                using System.Runtime.CompilerServices;
                using SharpProof.Attributes;
                
                public static class Subject
                {
                    [ZeroAllocations]
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Target<T, U>(T value)
                    {
                        return value is U _;
                    }
                
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Control(int value)
                    {
                        return value == 7;
                    }
                }
                """ + "\n",
            "parenthesized-type" => """
                using System.Runtime.CompilerServices;
                using SharpProof.Attributes;
                
                public static class Subject
                {
                    [ZeroAllocations]
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Target<T, U>(T value)
                    {
                        return value is (U);
                    }
                
                    [MethodImpl(MethodImplOptions.NoInlining)]
                    public static bool Control(int value)
                    {
                        return value == 7;
                    }
                }
                """ + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CaseObservation Measure(string name, Func<int, bool> target, bool expected)
    {
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

    private sealed record CaseObservation(string Name, bool Expected, bool Actual, int TrueResults,
        long AllocatedBytes, double BytesPerCall);

    private sealed record IlInstruction(int Offset, string OpCode, string OperandHex);

    private sealed record PatternObservation(string Form, CaseObservation Target,
        WorkerClaimOutcome Outcome, WorkerClaimReason Reason);
}
