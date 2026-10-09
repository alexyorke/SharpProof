using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmptyIteratorAllocationAuditTests
{
    private const int WarmupIterations = 10000;
    private const int MeasuredIterations = 128;
    private const int MeasurementRounds = 3;
    private static object? s_escaped;
    private static readonly Dictionary<ushort, OpCode> s_opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => unchecked((ushort)opcode.Value));
    private static readonly Case[] s_cases =
    [
        new("own-empty-iterator",
            "using System.Collections.Generic; using SharpProof.Attributes; public static class Subject { [ZeroAllocations] public static IEnumerable<int> Target() { yield break; } }",
            null),
        new("ordinary-null-control",
            "using System.Collections.Generic; using SharpProof.Attributes; public static class Subject { [ZeroAllocations] public static IEnumerable<int>? Target() { return null; } }",
            WorkerClaimOutcome.Proven),
        new("ordinary-object-allocation-control",
            "using SharpProof.Attributes; public static class Subject { [ZeroAllocations] public static object Target() { return new object(); } }",
            WorkerClaimOutcome.Refuted),
        new("nested-unused-iterator-null-control",
            "using System.Collections.Generic; using SharpProof.Attributes; public static class Subject { [ZeroAllocations] public static IEnumerable<int>? Target() { static IEnumerable<int> Nested() { yield break; } return null; } }",
            WorkerClaimOutcome.Proven)
    ];

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task IteratorCreationPreservesNativeAllocationEvidence(int caseIndex)
    {
        var subject = s_cases[caseIndex];
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ?? TestContext.CurrentContext.WorkDirectory;
        var evidence = Path.Combine(repository, "artifacts", "correctness", "empty-iterator-allocation-audit",
            subject.Name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        var sourcePath = Path.Combine(evidence, "Subject.cs");
        await File.WriteAllTextAsync(sourcePath, subject.Source, new UTF8Encoding(false));
        var sourceHash = Hash(Encoding.UTF8.GetBytes(subject.Source));
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: []);
        var tree = CSharpSyntaxTree.ParseText(subject.Source, parseOptions, sourcePath, Encoding.UTF8);
        var compilation = CSharpCompilation.Create("EmptyIterator_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var diagnostics = compilation.GetDiagnostics();
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await Save(evidence, "compilation.json", new
        {
            subject.Name,
            SourceSha256 = sourceHash,
            Compiler = typeof(CSharpCompilation).Assembly.FullName,
            CompilerMvid = typeof(CSharpCompilation).Module.ModuleVersionId,
            Language = parseOptions.LanguageVersion.ToString(),
            Symbols = parseOptions.PreprocessorSymbolNames.ToArray(),
            Optimization = compilation.Options.OptimizationLevel.ToString(),
            Diagnostics = diagnostics.Select(DiagnosticObservation).ToArray(),
            EmissionDiagnostics = emission.Diagnostics.Select(DiagnosticObservation).ToArray(),
            emission.Success
        });
        Assert.That(parseOptions.PreprocessorSymbolNames, Is.Empty);
        Assert.That(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var warnings = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).ToArray();
        if (caseIndex == 3)
        { Assert.That(warnings.All(diagnostic => diagnostic.Id == "CS8321"), Is.True); }
        else
        { Assert.That(warnings, Is.Empty); }
        var bytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "Subject.dll"), bytes);

        var syntax = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var attribute = target.GetAttributes().Single();
        var attributeSyntax = (AttributeSyntax)(await attribute.ApplicationSyntaxReference!.GetSyntaxAsync());
        var callableId = DocumentationCommentId.CreateDeclarationId(target)!;
        var ownYields = syntax.DescendantNodes(node => node is not LocalFunctionStatementSyntax &&
            node is not AnonymousFunctionExpressionSyntax).OfType<YieldStatementSyntax>().Count();
        var allYields = syntax.DescendantNodes().OfType<YieldStatementSyntax>().Count();
        await Save(evidence, "symbol-ownership.json", new
        {
            SourceSha256 = sourceHash,
            CallableId = callableId,
            target.IsStatic,
            target.IsAsync,
            MethodKind = target.MethodKind.ToString(),
            ReturnType = target.ReturnType.ToDisplayString(),
            Parameters = target.Parameters.Length,
            OwnYields = ownYields,
            AllYields = allYields,
            AttributeType = attribute.AttributeClass?.ToDisplayString(),
            AttributeAssembly = attribute.AttributeClass?.ContainingAssembly.Identity.ToString(),
            AttributeStart = attributeSyntax.Span.Start,
            AttributeLength = attributeSyntax.Span.Length
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.IsStatic, Is.True);
            Assert.That(target.IsAsync, Is.False);
            Assert.That(target.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(target.Parameters, Is.Empty);
            Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
                compilation.GetTypeByMetadataName("SharpProof.Attributes.ZeroAllocationsAttribute")), Is.True);
            Assert.That(ownYields, Is.EqualTo(caseIndex == 0 ? 1 : 0));
            Assert.That(allYields, Is.EqualTo(caseIndex is 0 or 3 ? 1 : 0));
        }

        var runtime = ObserveRuntime(bytes, caseIndex);
        await Save(evidence, "clr-allocation-observations.json", new
        {
            subject.Name,
            SourceSha256 = sourceHash,
            PeSha256 = Hash(bytes),
            WarmupIterations,
            MeasuredIterations,
            MeasurementRounds,
            MeasurementScope = "Synchronous typed Target calls only; delegates, metadata, enumeration and evidence writes are outside counters",
            Observation = runtime
        });
        QualifyRuntime(runtime, caseIndex);

        var manifestPath = Path.Combine(evidence, "public-compiler-manifest.json");
        Assert.That(File.Exists(manifestPath), Is.False);
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_property._SharpProofCompilerManifestPath"] = manifestPath,
            ["build_property._SharpProofCompilationTargetFramework"] = "net9.0",
            ["build_property._SharpProofProjectDirectory"] = evidence,
            ["build_property.SharpProofVerifyMaximumExpressionDepth"] = WorkerBudgets.DefaultMaximumExpressionDepth.ToString(CultureInfo.InvariantCulture),
            ["build_property.SharpProofProfile"] = "advisory",
            ["build_property.SharpProofFeatures"] = "all",
            ["build_property.SharpProofVerifyPolicy"] = "advisory",
            ["build_property.SharpProofAssumptionPolicy"] = "allow"
        };
        await Save(evidence, "collector-options.json", options);
        var analyzerOptions = new AnalyzerOptions([], new OptionsProvider(options));
        var collectorDiagnostics = await compilation.WithAnalyzers([new FinalCompilationCollectorAnalyzer()], analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();
        var outputExists = File.Exists(manifestPath);
        await Save(evidence, "collector-observations.json", new
        {
            subject.Name,
            SourceSha256 = sourceHash,
            PeSha256 = Hash(bytes),
            CallableId = callableId,
            OutputExists = outputExists,
            Diagnostics = collectorDiagnostics.Select(DiagnosticObservation).ToArray()
        });
        Assert.That(collectorDiagnostics, Is.Empty);
        Assert.That(outputExists, Is.True, "A fresh public artifact must retain the selected allocation claim.");

        var artifact = CompilerManifestArtifactJson.Deserialize(await File.ReadAllTextAsync(manifestPath));
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var artifactBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidence, "worker-compiler-artifact.json"), artifactBytes);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-request.json"), WorkerProtocolJson.SerializeRequest(project.Request));
        var preparation = project.Snapshot.Callables.Single(item => item.Entry.CallableId == callableId);
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparation, project.Request.Budgets);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(project.Request),
            Program.ComputeExpectedInputHash(project.Request, artifactBytes), artifact.Manifest, project.Request, Program.ExpectedVersions());
        var allocations = preparation.Total?.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAllocationInstruction>().ToArray() ?? [];
        await Save(evidence, "native-observations.json", new
        {
            subject.Name,
            SourceSha256 = sourceHash,
            PeSha256 = Hash(bytes),
            CallableId = callableId,
            ArtifactSha256 = Hash(artifactBytes),
            project.Request.CompilerManifest.Sha256,
            ArtifactDigest = project.Bind().ArtifactDigest,
            CacheEnabled = project.Request.Cache.Enabled,
            project.Request.Budgets,
            HasTotal = preparation.Total != null,
            BodyAbstraction = preparation.Total?.IsBodyAbstraction,
            EffectsCompleteAtEntry = preparation.Total?.EffectsCompleteAtEntry,
            NativeOutcome = native.Outcome?.GetType().Name ?? "Unknown",
            NativeReason = native.Reason.ToString(),
            native.QueryCompleted,
            native.HasFeasibleEntryWitness,
            NativeAllocationWitness = native.AllocationWitness?.ToString(),
            NativeEntryModel = native.EntryModel.Select(pair => new { Variable = pair.Key.ToString(), Value = pair.Value.ToString() }).ToArray(),
            Allocations = allocations.Select(allocation => new
            {
                Operation = allocation.Operation.ToString(),
                Type = allocation.AllocatedType.ToString(),
                Target = allocation.Target?.ToString(),
                Length = allocation.Length?.ToString(),
                MatchesWitness = allocation.Operation == native.AllocationWitness
            }).ToArray(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            response.Errors,
            CallableResults = response.CallableResults.Select(item => new
            {
                item.CallableId,
                Coverage = item.Coverage.ToString(),
                Reason = item.Reason.ToString()
            }).ToArray(),
            Claims = response.ClaimResults.Select(item => new
            {
                item.ClaimId,
                Outcome = item.Outcome.ToString(),
                Reason = item.Reason.ToString(),
                Certainty = item.EffectCertainty.ToString(),
                Vacuity = item.Vacuity.ToString(),
                item.EffectWitness
            }).ToArray(),
            ExpectedOutcome = subject.Outcome?.ToString() ?? "SemanticNotProven"
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Hash(artifactBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
            Assert.That(Hash(artifactBytes), Is.EqualTo(project.Bind().ArtifactDigest));
            Assert.That(project.Request.Cache.Enabled, Is.False);
            Assert.That(JsonSerializer.Serialize(project.Request.Budgets), Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets())));
            Assert.That(validation.IsValid, Is.True, JsonSerializer.Serialize(validation.Errors));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims.Single().CallableId, Is.EqualTo(callableId));
            Assert.That(artifact.Manifest.Claims.Single().Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.ZeroAllocations));
            Assert.That(artifact.Manifest.Claims.Single().Location.Path, Is.EqualTo(sourcePath));
            Assert.That(artifact.Manifest.Claims.Single().Location.Start, Is.EqualTo(attributeSyntax.Span.Start));
            Assert.That(artifact.Manifest.Claims.Single().Location.Length, Is.EqualTo(attributeSyntax.Span.Length));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
        }
        var claim = response.ClaimResults.Single();
        var callable = response.CallableResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(callable.CallableId, Is.EqualTo(callableId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (claim.Outcome == WorkerClaimOutcome.Unknown)
            {
                Assert.That(caseIndex, Is.Zero);
                Assert.That(native.Outcome, Is.Null);
                Assert.That(native.Reason, Is.EqualTo(claim.Reason));
                Assert.That(claim.Reason, Is.AnyOf(WorkerClaimReason.UnsupportedBody, WorkerClaimReason.CounterexampleNotReplayable));
                Assert.That(callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
                Assert.That(callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.SemanticUnknown));
                Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
                Assert.That(claim.EffectWitness, Is.Null);
            }
            else
            {
                Assert.That(preparation.Total, Is.Not.Null);
                Assert.That(preparation.Total!.IsBodyAbstraction, Is.False);
                Assert.That(preparation.Total.EffectsCompleteAtEntry, Is.True);
                Assert.That(native.HasFeasibleEntryWitness, Is.True);
                Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
                Assert.That(callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.None));
                if (claim.Outcome == WorkerClaimOutcome.Proven)
                {
                    Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>());
                    Assert.That(native.AllocationWitness, Is.Null);
                    Assert.That(claim.EffectWitness, Is.Null);
                    Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
                }
                else
                {
                    Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
                    Assert.That(native.Outcome, Is.TypeOf<RefutedOutcome>());
                    Assert.That(native.AllocationWitness, Is.Not.Null);
                    Assert.That(allocations.Count(allocation => allocation.Operation == native.AllocationWitness), Is.EqualTo(1));
                    Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
                    Assert.That(claim.EffectWitness, Is.Not.Null);
                    Assert.That(claim.EffectWitness!.Kind, Is.EqualTo("managed-allocation"));
                    Assert.That(claim.EffectWitness.Location.Path, Is.EqualTo(sourcePath));
                }
            }
        }
        if (subject.Outcome is { } expected)
        { Assert.That(claim.Outcome, Is.EqualTo(expected)); }
        else
        { Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), "A positive warmed iterator-creation allocation cannot receive a valid allocation-free proof."); }
    }

    private static RuntimeObservation ObserveRuntime(byte[] bytes, int caseIndex)
    {
        var context = new AssemblyLoadContext("EmptyIterator_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var stream = new MemoryStream(bytes);
            var type = context.LoadFromStream(stream).GetType("Subject")!;
            var method = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var iteratorType = method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            var measured = caseIndex == 2
                ? MeasureCalls(method.CreateDelegate<Func<object?>>())
                : MeasureCalls(method.CreateDelegate<Func<IEnumerable<int>?>>());
            bool? enumerationHasElement = null;
            if (measured.First is IEnumerable<int> sequence)
            {
                using var enumerator = sequence.GetEnumerator();
                enumerationHasElement = enumerator.MoveNext();
            }
            using var peStream = new MemoryStream(bytes);
            using var pe = new PEReader(peStream);
            var metadata = pe.GetMetadataReader();
            var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff));
            var attributes = definition.GetCustomAttributes().Select(handle =>
            {
                var attribute = metadata.GetCustomAttribute(handle);
                var constructor = method.Module.ResolveMethod(MetadataTokens.GetToken(attribute.Constructor))!;
                return new AttributeObservation(MetadataTokens.GetRowNumber(handle), constructor.DeclaringType!.FullName!,
                    constructor.DeclaringType.Assembly.FullName!, MetadataTokens.GetToken(attribute.Constructor),
                    Convert.ToHexStringLower(metadata.GetBlobBytes(attribute.Value)));
            }).ToArray();
            return new(method.Module.ModuleVersionId, metadata.GetGuid(metadata.GetModuleDefinition().Mvid), method.MetadataToken,
                measured.DelegateMethodToken, measured.DelegateMvid, measured.DelegateHasNoTarget,
                type.TypeInitializer != null, attributes, iteratorType?.FullName, iteratorType?.MetadataToken,
                iteratorType?.Module.ModuleVersionId, iteratorType?.DeclaringType == type,
                iteratorType?.IsInstanceOfType(measured.First), measured.First == null, measured.Second == null,
                ReferenceEquals(measured.First, measured.Second), measured.First?.GetType().FullName, enumerationHasElement,
                measured.Rounds, DecodeIl(method, bytes));
        }
        finally
        {
            s_escaped = null;
            context.Unload();
        }
    }

    private static MeasuredCalls MeasureCalls<T>(Func<T?> target) where T : class
    {
        Func<T?> control = Null<T>;
        for (var repeat = 0; repeat < WarmupIterations; repeat++)
        {
            s_escaped = target();
            s_escaped = control();
        }
        var first = target();
        var second = target();
        s_escaped = second;
        var rounds = new Measurement[MeasurementRounds];
        for (var round = 0; round < MeasurementRounds; round++)
        {
            var before = Measure(control);
            var subject = Measure(target);
            var after = Measure(control);
            rounds[round] = new(round, before, subject, after);
        }
        GC.KeepAlive(first);
        GC.KeepAlive(second);
        return new(first, second, target.Method.MetadataToken, target.Method.Module.ModuleVersionId, target.Target == null, rounds);
    }

    private static T? Null<T>() where T : class
    {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Measure<T>(Func<T?> target) where T : class
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < MeasuredIterations; repeat++)
        { s_escaped = target(); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(target);
        GC.KeepAlive(s_escaped);
        return bytes;
    }

    private static void QualifyRuntime(RuntimeObservation observation, int caseIndex)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.LoadedMvid, Is.EqualTo(observation.PeMvid));
            Assert.That(observation.DelegateMvid, Is.EqualTo(observation.PeMvid));
            Assert.That(observation.DelegateMethodToken, Is.EqualTo(observation.MethodToken));
            Assert.That(observation.DelegateHasNoTarget, Is.True);
            Assert.That(observation.HasTypeInitializer, Is.False);
            Assert.That(observation.Attributes.Count(attribute => attribute.TypeName == typeof(ZeroAllocationsAttribute).FullName), Is.EqualTo(1));
            Assert.That(observation.Rounds, Has.Length.EqualTo(MeasurementRounds));
            foreach (var round in observation.Rounds)
            {
                Assert.That(round.ControlBeforeBytes, Is.Zero);
                Assert.That(round.ControlAfterBytes, Is.Zero);
                if (caseIndex is 0 or 2)
                { Assert.That(round.SubjectBytes, Is.GreaterThan(0)); }
                else
                { Assert.That(round.SubjectBytes, Is.Zero); }
            }
            var allocations = observation.Target.Instructions.Where(instruction => instruction.OpCode == "newobj").ToArray();
            if (caseIndex == 0)
            {
                Assert.That(observation.Attributes.Count(attribute => attribute.TypeName == typeof(IteratorStateMachineAttribute).FullName), Is.EqualTo(1));
                Assert.That(observation.IteratorType, Is.Not.Null);
                Assert.That(observation.IteratorMvid, Is.EqualTo(observation.PeMvid));
                Assert.That(observation.IteratorDeclaringTypeMatches, Is.True);
                Assert.That(observation.ResultMatchesIteratorType, Is.True);
                Assert.That(observation.FirstIsNull, Is.False);
                Assert.That(observation.SecondIsNull, Is.False);
                Assert.That(observation.SameReference, Is.False);
                Assert.That(observation.EnumerationHasElement, Is.False);
                Assert.That(allocations, Has.Length.EqualTo(1));
                Assert.That(allocations.Single().MethodName, Is.EqualTo(".ctor"));
                Assert.That(allocations.Single().MethodOwner, Is.EqualTo(observation.IteratorType));
                Assert.That(allocations.Single().OwnerToken, Is.EqualTo(observation.IteratorTypeToken));
                Assert.That(allocations.Single().MethodMvid, Is.EqualTo(observation.PeMvid));
            }
            else
            {
                Assert.That(observation.IteratorType, Is.Null);
                Assert.That(observation.Attributes.Any(attribute => attribute.TypeName == typeof(IteratorStateMachineAttribute).FullName), Is.False);
                if (caseIndex == 2)
                {
                    Assert.That(observation.FirstIsNull, Is.False);
                    Assert.That(observation.SecondIsNull, Is.False);
                    Assert.That(observation.SameReference, Is.False);
                    Assert.That(observation.ResultType, Is.EqualTo(typeof(object).FullName));
                    Assert.That(allocations, Has.Length.EqualTo(1));
                    Assert.That(allocations.Single().MethodName, Is.EqualTo(".ctor"));
                    Assert.That(allocations.Single().MethodOwner, Is.EqualTo(typeof(object).FullName));
                }
                else
                {
                    Assert.That(observation.FirstIsNull, Is.True);
                    Assert.That(observation.SecondIsNull, Is.True);
                    Assert.That(allocations, Is.Empty);
                }
            }
        }
    }

    private static IlObservation DecodeIl(MethodInfo method, byte[] bytes)
    {
        var body = method.GetMethodBody()!.GetILAsByteArray()!;
        using var stream = new MemoryStream(bytes);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff));
        Assert.That(body, Is.EqualTo(pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()));
        var instructions = new List<IlInstruction>();
        for (var offset = 0; offset < body.Length;)
        {
            var start = offset;
            ushort value = body[offset++];
            if (value == 0xfe)
            { value = (ushort)(0xfe00 | body[offset++]); }
            var opcode = s_opCodes[value];
            var size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(body, offset),
                _ => 4
            };
            var called = opcode.OperandType == OperandType.InlineMethod ? method.Module.ResolveMethod(BitConverter.ToInt32(body, offset)) : null;
            instructions.Add(new(start, opcode.Name!, Convert.ToHexStringLower(body.AsSpan(offset, size)), called?.MetadataToken,
                called?.Module.ModuleVersionId, called?.DeclaringType?.FullName, called?.DeclaringType?.MetadataToken, called?.Name));
            offset += size;
        }
        return new(method.MetadataToken, Convert.ToHexStringLower(body), [.. instructions]);
    }

    private static object DiagnosticObservation(Diagnostic diagnostic)
    {
        return new
        {
            diagnostic.Id,
            Severity = diagnostic.Severity.ToString(),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            Path = diagnostic.Location.SourceTree?.FilePath,
            Span = new { diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length }
        };
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static async Task Save(string directory, string name, object value)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value));
    }

    private sealed class OptionsProvider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new DictionaryOptions(values);
        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _options;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _options;
        }
    }

    private sealed class DictionaryOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }

    private sealed record Case(string Name, string Source, WorkerClaimOutcome? Outcome);
    private sealed record Measurement(int Round, long ControlBeforeBytes, long SubjectBytes, long ControlAfterBytes);
    private sealed record MeasuredCalls(object? First, object? Second, int DelegateMethodToken, Guid DelegateMvid,
        bool DelegateHasNoTarget, Measurement[] Rounds);
    private sealed record AttributeObservation(int Row, string TypeName, string Assembly, int ConstructorToken, string Blob);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, int? MethodToken,
        Guid? MethodMvid, string? MethodOwner, int? OwnerToken, string? MethodName);
    private sealed record IlObservation(int MethodToken, string Bytes, IlInstruction[] Instructions);
    private sealed record RuntimeObservation(Guid LoadedMvid, Guid PeMvid, int MethodToken, int DelegateMethodToken,
        Guid DelegateMvid, bool DelegateHasNoTarget, bool HasTypeInitializer, AttributeObservation[] Attributes,
        string? IteratorType, int? IteratorTypeToken, Guid? IteratorMvid, bool IteratorDeclaringTypeMatches,
        bool? ResultMatchesIteratorType, bool FirstIsNull, bool SecondIsNull, bool SameReference, string? ResultType,
        bool? EnumerationHasElement, Measurement[] Rounds, IlObservation Target);
}
