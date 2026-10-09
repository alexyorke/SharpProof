using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
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
public sealed class CapturedPrimaryConstructorStorageAuditTests
{
    private static readonly Dictionary<ushort, OpCode> s_opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => unchecked((ushort)opcode.Value));

    private static readonly Case[] s_cases =
    [
        new("captured-simple-assignment",
            "using SharpProof.Attributes; public sealed class Counter(int value) { [EnforcePure] public void Reset() { value = 0; } public int Peek() => value; }",
            "E225CE4D73F043F1E1E12BA39F219D4FD02043137F04396005DA13536A5F425A", "Reset", 0, WorkerClaimOutcome.Unknown),
        new("captured-compound-assignment",
            "using SharpProof.Attributes; public sealed class Counter(int value) { [EnforcePure] public void Reset() { value += 1; } public int Peek() => value; }",
            "FBDEEBE49B4FDE3F09E46C88CD1BE1FC6A0C6C9B7FECDE00A9861F01D8613394", "Reset", 2, WorkerClaimOutcome.Unknown),
        new("captured-increment",
            "using SharpProof.Attributes; public sealed class Counter(int value) { [EnforcePure] public void Reset() { value++; } public int Peek() => value; }",
            "23D73F425CC8F98F633863BFE5C1ABC43B9A5FBDFF2760A16663C33493FEECFD", "Reset", 2, WorkerClaimOutcome.Unknown),
        new("captured-read-control",
            "using SharpProof.Attributes; public sealed class Counter(int value) { [EnforcePure] public int Read() => value; }",
            "15A49069EE0772CBE08B2ACE29EBCF4DC1A3C1A6AC11F1B44EA265D5A8C8422C", "Read", 1, WorkerClaimOutcome.Unknown),
        new("owned-method-parameter-control",
            "using SharpProof.Attributes; public sealed class Counter { [EnforcePure] public void Reset(int value) { value = 0; } }",
            "353F8BCC0CB4C2C4132CBF0163AF591B787DBE383315CB75D38539333EE1331D", "Reset", 1, WorkerClaimOutcome.Proven),
        new("explicit-instance-field-control",
            "using SharpProof.Attributes; public sealed class Counter { private int _value; [EnforcePure] public void Reset() { _value = 0; } public int Peek() => _value; }",
            "2614D13D32DE14049C4000F01456FE748FC09CE42CE28609EBA6B3DC1A4AB694", "Reset", 0, WorkerClaimOutcome.Refuted)
    ];

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public async Task StorageOwnershipPreservesCollectorPublicationAndNativePurity(int caseIndex)
    {
        var subject = s_cases[caseIndex];
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ?? TestContext.CurrentContext.WorkDirectory;
        var evidence = Path.Combine(repository, "artifacts", "correctness", "captured-primary-constructor-storage-audit",
            subject.Name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        var sourcePath = Path.Combine(evidence, "Subject.cs");
        await File.WriteAllTextAsync(sourcePath, subject.Source, new UTF8Encoding(false));
        var sourceHash = Hash(Encoding.UTF8.GetBytes(subject.Source));
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: []);
        var tree = CSharpSyntaxTree.ParseText(subject.Source, parseOptions, sourcePath, Encoding.UTF8);
        var compilation = CSharpCompilation.Create("CapturedStorage_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        var compilerDiagnostics = compilation.GetDiagnostics();
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await Save(evidence, "compilation.json", new
        {
            subject.Name,
            SourceSha256 = sourceHash,
            ExpectedSourceSha256 = subject.SourceHash,
            Compiler = typeof(CSharpCompilation).Assembly.FullName,
            CompilerMvid = typeof(CSharpCompilation).Module.ModuleVersionId,
            Language = parseOptions.LanguageVersion.ToString(),
            Symbols = parseOptions.PreprocessorSymbolNames.ToArray(),
            Optimization = compilation.Options.OptimizationLevel.ToString(),
            Diagnostics = compilerDiagnostics.Select(DiagnosticObservation).ToArray(),
            EmissionDiagnostics = emission.Diagnostics.Select(DiagnosticObservation).ToArray(),
            emission.Success
        });
        Assert.That(string.Equals(sourceHash, subject.SourceHash, StringComparison.OrdinalIgnoreCase), Is.True);
        Assert.That(parseOptions.PreprocessorSymbolNames, Is.Empty);
        Assert.That(compilerDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var bytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "Subject.dll"), bytes);

        var syntax = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == subject.Method);
        var model = compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var operation = model.GetOperation(syntax)!;
        var operations = Walk(operation).ToArray();
        var parameters = operations.OfType<IParameterReferenceOperation>().ToArray();
        var canonicalAttribute = compilation.GetTypeByMetadataName("SharpProof.Attributes.EnforcePureAttribute")!;
        var attribute = target.GetAttributes().Single();
        var attributeSyntax = (AttributeSyntax)(await attribute.ApplicationSyntaxReference!.GetSyntaxAsync());
        var callableId = DocumentationCommentId.CreateDeclarationId(target)!;
        await Save(evidence, "symbol-ownership.json", new
        {
            SourceSha256 = sourceHash,
            CallableId = callableId,
            Method = target.ToDisplayString(),
            Kind = target.MethodKind.ToString(),
            target.IsStatic,
            target.ReturnsVoid,
            DeclaredParameterCount = target.Parameters.Length,
            MethodSpan = new { syntax.Span.Start, syntax.Span.Length },
            AttributeSpan = new { attributeSyntax.Span.Start, attributeSyntax.Span.Length },
            AttributeType = attribute.AttributeClass!.ToDisplayString(),
            AttributeAssembly = attribute.AttributeClass.ContainingAssembly.Identity.ToString(),
            CanonicalAttributeBinding = SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, canonicalAttribute),
            ParameterReferences = parameters.Select(reference => new
            {
                reference.Parameter.Name,
                Owner = reference.Parameter.ContainingSymbol.ToDisplayString(),
                OwnerKind = ((IMethodSymbol)reference.Parameter.ContainingSymbol).MethodKind.ToString(),
                OwnedByTarget = SymbolEqualityComparer.Default.Equals(reference.Parameter.ContainingSymbol, target),
                Span = new { reference.Syntax.Span.Start, reference.Syntax.Span.Length }
            }).ToArray(),
            StorageOperations = operations.Where(item => item is ISimpleAssignmentOperation or ICompoundAssignmentOperation or
                IIncrementOrDecrementOperation).Select(item => item.Kind.ToString()).ToArray()
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(target.IsStatic, Is.False);
            Assert.That(target.Parameters, Has.Length.EqualTo(caseIndex == 4 ? 1 : 0));
            Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, canonicalAttribute), Is.True);
            Assert.That(canonicalAttribute.ContainingAssembly.Identity.Name, Is.EqualTo(typeof(EnforcePureAttribute).Assembly.GetName().Name));
            Assert.That(SymbolEqualityComparer.Default.Equals(canonicalAttribute.ContainingAssembly, compilation.Assembly), Is.False);
            Assert.That(attributeSyntax.SyntaxTree, Is.SameAs(tree));
            Assert.That(attributeSyntax.Name.ToString(), Is.EqualTo("EnforcePure"));
            Assert.That(parameters, Has.Length.EqualTo(caseIndex == 5 ? 0 : 1));
            if (caseIndex < 4)
            {
                Assert.That(((IMethodSymbol)parameters.Single().Parameter.ContainingSymbol).MethodKind, Is.EqualTo(MethodKind.Constructor));
                Assert.That(SymbolEqualityComparer.Default.Equals(parameters.Single().Parameter.ContainingSymbol, target), Is.False);
            }
            else if (caseIndex == 4)
            {
                Assert.That(SymbolEqualityComparer.Default.Equals(parameters.Single().Parameter.ContainingSymbol, target), Is.True);
                Assert.That(SymbolEqualityComparer.Default.Equals(parameters.Single().Parameter, target.Parameters.Single()), Is.True);
            }
            if (caseIndex is 0 or 4 or 5)
            { Assert.That(operations.OfType<ISimpleAssignmentOperation>(), Has.Exactly(1).Items); }
            else if (caseIndex == 1)
            { Assert.That(operations.OfType<ICompoundAssignmentOperation>(), Has.Exactly(1).Items); }
            else if (caseIndex == 2)
            { Assert.That(operations.OfType<IIncrementOrDecrementOperation>(), Has.Exactly(1).Items); }
        }
        var runtime = ObserveRuntime(bytes, subject.Method, caseIndex);
        await Save(evidence, "runtime-observations.json", new
        {
            subject.Name,
            SourceSha256 = sourceHash,
            PeSha256 = Hash(bytes),
            SameReleaseCompilation = true,
            TypedCalls = caseIndex == 4 ? "Closed Action<int>; caller scalar stays unchanged" :
                caseIndex == 3 ? "Closed Func<int> Read" : "Closed Action Reset and closed Func<int> Peek",
            ExplicitFieldSeed = caseIndex == 5 ? "Seed only this test-owned private field before typed Reset/Peek calls" : null,
            Observation = runtime
        });
        QualifyRuntime(runtime, caseIndex, subject.After);

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
            FreshRequestedPath = manifestPath,
            OutputExists = outputExists,
            Diagnostics = collectorDiagnostics.Select(DiagnosticObservation).ToArray(),
            InfrastructureFailures = collectorDiagnostics.Where(diagnostic => diagnostic.Id == "SP0049")
                .Select(DiagnosticObservation).ToArray(),
            DownstreamStarted = outputExists
        });
        if (!outputExists)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(collectorDiagnostics, Is.Empty, "The public collector must not crash on valid constructor-owned storage.");
                Assert.That(outputExists, Is.True, "A fresh public compiler artifact must be published for the same selected purity claim.");
            }
            return;
        }

        var artifact = CompilerManifestArtifactJson.Deserialize(await File.ReadAllTextAsync(manifestPath));
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var artifactBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidence, "worker-compiler-artifact.json"), artifactBytes);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-request.json"), WorkerProtocolJson.SerializeRequest(project.Request));
        var preparation = project.Snapshot.Callables.Single(item => item.Entry.CallableId == callableId);
        var native = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, project.Request.Budgets);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(project.Request),
            Program.ComputeExpectedInputHash(project.Request, artifactBytes), artifact.Manifest, project.Request, Program.ExpectedVersions());
        var nativeWrites = preparation.Total?.Program.Blocks.SelectMany(block => block.Instructions)
            .OfType<IrWriteInstruction>().Where(write => write.Operation == native.WriteWitness).ToArray() ?? [];
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
            NativeWriteWitness = native.WriteWitness?.ToString(),
            NativeWriteInstructions = nativeWrites.Select(write => new
            {
                Operation = write.Operation.ToString(),
                Region = write.Region.ToString(),
                write.IsFieldStore,
                write.IsStore,
                Receiver = write.Target?.ToString(),
                Field = write.Field?.ToString(),
                Value = write.Value?.ToString()
            }).ToArray(),
            NativeEntryModel = native.EntryModel.Select(pair => new { Variable = pair.Key.ToString(), Value = pair.Value.ToString() }).ToArray(),
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
            ExpectedOutcome = subject.Outcome.ToString()
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(collectorDiagnostics, Is.Empty);
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
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
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
            Assert.That(claim.Outcome, Is.EqualTo(subject.Outcome));
            if (subject.Outcome == WorkerClaimOutcome.Unknown)
            {
                Assert.That(preparation.Total == null || preparation.Total.IsBodyAbstraction ||
                    !preparation.Total.EffectsCompleteAtEntry, Is.True);
                Assert.That(native.Outcome, Is.Null);
                Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
                Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
                Assert.That(claim.EffectWitness, Is.Null);
                Assert.That(native.WriteWitness, Is.Null);
                Assert.That(callable.Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
                Assert.That(callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.SemanticUnknown));
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
                if (subject.Outcome == WorkerClaimOutcome.Proven)
                {
                    Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>());
                    Assert.That(native.WriteWitness, Is.Null);
                    Assert.That(claim.EffectWitness, Is.Null);
                    Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
                }
                else
                {
                    Assert.That(native.Outcome, Is.TypeOf<RefutedOutcome>());
                    Assert.That(native.WriteWitness, Is.Not.Null);
                    Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
                    Assert.That(claim.EffectWitness, Is.Not.Null);
                    Assert.That(claim.EffectWitness!.Kind, Is.EqualTo("nonlocal-write"));
                    Assert.That(claim.EffectWitness.Effects, Is.EqualTo(WorkerEffectSet.WritesReceiverState));
                    Assert.That(claim.EffectWitness.Location.Path, Is.EqualTo(sourcePath));
                    Assert.That(subject.Source.Substring(claim.EffectWitness.Location.Start, claim.EffectWitness.Location.Length),
                        Is.EqualTo("_value = 0"));
                    Assert.That(nativeWrites, Has.Length.EqualTo(1));
                    var write = nativeWrites.Single();
                    Assert.That(write.Region, Is.EqualTo(IrWriteRegion.Field));
                    Assert.That(write.IsFieldStore, Is.True);
                    Assert.That(write.IsStore, Is.True);
                    Assert.That(write.Target, Is.Not.Null);
                    Assert.That(write.Field, Is.Not.Null);
                    Assert.That(write.Value, Is.Not.Null);
                }
            }
        }
    }

    private static RuntimeObservation ObserveRuntime(byte[] bytes, string methodName, int caseIndex)
    {
        using var stream = new MemoryStream(bytes);
        var context = new AssemblyLoadContext("CapturedStorage_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var type = context.LoadFromStream(stream).GetType("Counter")!;
            var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single();
            var instance = constructor.Invoke(caseIndex < 4 ? [1] : []);
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (caseIndex == 5)
            { fields.Single().SetValue(instance, 1); }
            Delegate execute;
            int before;
            int after;
            int? returned = null;
            IlObservation? readIl = null;
            if (caseIndex == 4)
            {
                var reset = method.CreateDelegate<Action<int>>(instance);
                execute = reset;
                var callerValue = 1;
                before = callerValue;
                reset(callerValue);
                after = callerValue;
            }
            else if (caseIndex == 3)
            {
                var read = method.CreateDelegate<Func<int>>(instance);
                execute = read;
                before = read();
                returned = read();
                after = read();
            }
            else
            {
                var peekMethod = type.GetMethod("Peek", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
                var peek = peekMethod.CreateDelegate<Func<int>>(instance);
                var reset = method.CreateDelegate<Action>(instance);
                execute = reset;
                readIl = DecodeIl(peekMethod, bytes);
                before = peek();
                reset();
                after = peek();
            }
            using var peStream = new MemoryStream(bytes);
            using var pe = new PEReader(peStream);
            var metadata = pe.GetMetadataReader();
            var field = fields.SingleOrDefault();
            return new(before, after, returned, type.Module.ModuleVersionId, metadata.GetGuid(metadata.GetModuleDefinition().Mvid),
                type.TypeInitializer != null, type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly).Length,
                fields.Length, field?.Name, field?.MetadataToken, field?.IsInitOnly,
                field == null ? null : Convert.ToHexStringLower(metadata.GetBlobBytes(metadata.GetFieldDefinition(
                    MetadataTokens.FieldDefinitionHandle(field.MetadataToken & 0x00ffffff)).Signature)),
                field?.FieldType.FullName, field?.DeclaringType == type,
                method.MetadataToken, execute.Method.MetadataToken, execute.Method.Module.ModuleVersionId,
                ReferenceEquals(execute.Target, instance), execute.Target?.GetType() == type,
                method.CustomAttributes.Count(attribute => attribute.AttributeType == typeof(EnforcePureAttribute)),
                constructor.GetParameters().Length, method.GetParameters().Length,
                DecodeIl(constructor, bytes), DecodeIl(method, bytes), readIl);
        }
        finally
        {
            context.Unload();
        }
    }

    private static void QualifyRuntime(RuntimeObservation observation, int caseIndex, int expectedAfter)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Before, Is.EqualTo(1));
            Assert.That(observation.After, Is.EqualTo(expectedAfter));
            Assert.That(observation.LoadedMvid, Is.EqualTo(observation.PeMvid));
            Assert.That(observation.DelegateMvid, Is.EqualTo(observation.PeMvid));
            Assert.That(observation.MethodToken, Is.EqualTo(observation.DelegateMethodToken));
            Assert.That(observation.DelegateTargetIsInstance, Is.True);
            Assert.That(observation.DelegateTargetTypeMatches, Is.True);
            Assert.That(observation.EnforcePureAttributeCount, Is.EqualTo(1));
            Assert.That(observation.HasTypeInitializer, Is.False);
            Assert.That(observation.StaticFieldCount, Is.Zero);
            Assert.That(observation.ConstructorParameterCount, Is.EqualTo(caseIndex < 4 ? 1 : 0));
            Assert.That(observation.MethodParameterCount, Is.EqualTo(caseIndex == 4 ? 1 : 0));
            Assert.That(observation.InstanceFieldCount, Is.EqualTo(caseIndex == 4 ? 0 : 1));
            if (caseIndex == 4)
            {
                Assert.That(observation.Target.Instructions.Where(instruction => instruction.FieldToken != null), Is.Empty);
            }
            else
            {
                Assert.That(observation.FieldType, Is.EqualTo(typeof(int).FullName));
                Assert.That(observation.FieldSignature, Is.EqualTo("0608"));
                Assert.That(observation.FieldOwnerMatches, Is.True);
                var reads = (caseIndex == 3 ? observation.Target : observation.Peek!).Instructions.Where(instruction => instruction.OpCode == "ldfld");
                Assert.That(reads.Single().FieldToken, Is.EqualTo(observation.FieldToken));
                if (caseIndex < 4)
                {
                    var stores = observation.Constructor.Instructions.Where(instruction => instruction.OpCode == "stfld");
                    Assert.That(stores.Single().FieldToken, Is.EqualTo(observation.FieldToken));
                }
                if (caseIndex == 3)
                {
                    Assert.That(observation.Returned, Is.EqualTo(1));
                    Assert.That(observation.Target.Instructions.Where(instruction => instruction.OpCode == "stfld"), Is.Empty);
                }
                else
                {
                    var store = observation.Target.Instructions.Single(instruction => instruction.OpCode == "stfld");
                    Assert.That(store.FieldToken, Is.EqualTo(observation.FieldToken));
                    Assert.That(store.FieldMvid, Is.EqualTo(observation.PeMvid));
                    if (caseIndex is 1 or 2)
                    {
                        var read = observation.Target.Instructions.Single(instruction => instruction.OpCode == "ldfld");
                        var add = observation.Target.Instructions.Single(instruction => instruction.OpCode == "add");
                        Assert.That(read.FieldToken, Is.EqualTo(store.FieldToken));
                        Assert.That(read.Offset, Is.LessThan(add.Offset));
                        Assert.That(add.Offset, Is.LessThan(store.Offset));
                    }
                    if (caseIndex == 5)
                    { Assert.That(observation.FieldName, Is.EqualTo("_value")); }
                }
            }
        }
    }

    private static IlObservation DecodeIl(MethodBase method, byte[] bytes)
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
            FieldInfo? field = opcode.OperandType == OperandType.InlineField ? method.Module.ResolveField(BitConverter.ToInt32(body, offset)) : null;
            instructions.Add(new(start, opcode.Name!, Convert.ToHexStringLower(body.AsSpan(offset, size)),
                field?.MetadataToken, field?.Module.ModuleVersionId, field?.DeclaringType?.FullName, field?.Name));
            offset += size;
        }
        return new(method.MetadataToken, Convert.ToHexStringLower(body), [.. instructions]);
    }

    private static IEnumerable<IOperation> Walk(IOperation operation)
    {
        var pending = new Stack<IOperation>();
        pending.Push(operation);
        while (pending.TryPop(out var current))
        {
            yield return current;
            foreach (var child in current.ChildOperations)
            { pending.Push(child); }
        }
    }

    private static object DiagnosticObservation(Diagnostic diagnostic)
    {
        return new
        {
            diagnostic.Id,
            Severity = diagnostic.Severity.ToString(),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            LocationKind = diagnostic.Location.Kind.ToString(),
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

    private sealed record Case(string Name, string Source, string SourceHash, string Method, int After, WorkerClaimOutcome Outcome);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, int? FieldToken, Guid? FieldMvid, string? FieldOwner, string? FieldName);
    private sealed record IlObservation(int MethodToken, string Bytes, IlInstruction[] Instructions);
    private sealed record RuntimeObservation(int Before, int After, int? Returned, Guid LoadedMvid, Guid PeMvid,
        bool HasTypeInitializer, int StaticFieldCount, int InstanceFieldCount, string? FieldName, int? FieldToken,
        bool? FieldReadOnly, string? FieldSignature, string? FieldType, bool FieldOwnerMatches,
        int MethodToken, int DelegateMethodToken, Guid DelegateMvid, bool DelegateTargetIsInstance, bool DelegateTargetTypeMatches,
        int EnforcePureAttributeCount, int ConstructorParameterCount, int MethodParameterCount,
        IlObservation Constructor, IlObservation Target, IlObservation? Peek);
}
