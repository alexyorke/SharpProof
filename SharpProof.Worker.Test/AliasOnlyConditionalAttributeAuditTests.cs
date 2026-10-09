using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;
using WorkerProgram = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AliasOnlyConditionalAttributeAuditTests
{
    private static readonly int[] RetainedInputs = [-7, 0, 42];
    private static readonly int[] ZeroInput = [0];
    private static readonly int[] OneInput = [1];
    private static readonly ImmutableArray<string> ShadowAliases = ["Shadow"];
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => unchecked((ushort)code.Value))
        .ToDictionary(group => group.Key, group => group.First());

    private const string NamespaceProducerSource = """
        namespace System.Diagnostics
        {
            public sealed class ConditionalAttribute : global::System.Attribute
            {
                public ConditionalAttribute(string symbol)
                {
                }
            }
        }
        """ + "\n";

    private const string NestedProducerSource = """
        namespace System
        {
            public static class Diagnostics
            {
                public sealed class ConditionalAttribute : global::System.Attribute
                {
                    public ConditionalAttribute(string symbol)
                    {
                    }
                }
            }
        }
        """ + "\n";

    private const string AliasConsumerSource = """
        extern alias Shadow;
        using SharpProof.Attributes;

        public static class Fixture
        {
            [Shadow::System.Diagnostics.Conditional("ABSENT")]
            private static void Log(int value)
            {
            }

            [DoesNotThrow]
            public static int Target(int x)
            {
                Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string OfficialConsumerSource = """
        extern alias Shadow;
        using SharpProof.Attributes;

        public static class Fixture
        {
            [global::System.Diagnostics.Conditional("ABSENT")]
            private static void Log(int value)
            {
            }

            [DoesNotThrow]
            public static int Target(int x)
            {
                Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string MixedConsumerSource = """
        extern alias Shadow;
        using SharpProof.Attributes;

        public static class Fixture
        {
            [global::System.Diagnostics.Conditional("OFFICIAL_ABSENT")]
            [Shadow::System.Diagnostics.Conditional("PRIVATE_PRESENT")]
            private static void Log(int value)
            {
            }

            [DoesNotThrow]
            public static int Target(int x)
            {
                Contract.Requires(x == 1);
                Log(x = 0);
                return 10 / x;
            }
        }
        """ + "\n";

    [TestCase("alias-only-absent")]
    [TestCase("official-absent")]
    [TestCase("official-present")]
    [TestCase("alias-only-present")]
    [TestCase("nested-name-negative")]
    [TestCase("mixed-private-present")]
    [TestCase("mixed-all-absent")]
    public async Task ConditionalCallEmissionMatchesNativeDoesNotThrow(string scenario)
    {
        var settings = Settings(scenario);
        var producerSource = settings.Nested ? NestedProducerSource : NamespaceProducerSource;
        var source = settings.Mixed ? MixedConsumerSource : settings.Official ? OfficialConsumerSource : AliasConsumerSource;
        var symbols = settings.Present
            ? ImmutableArray.Create(settings.Mixed ? "PRIVATE_PRESENT" : "ABSENT") : ImmutableArray<string>.Empty;
        var retained = settings.Nested || settings.Present;
        var throws = settings.Mixed ? settings.Present : !retained;
        var inputs = settings.Mixed ? OneInput : throws ? ZeroInput : RetainedInputs;
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT")!;
        Assert.That(repository, Is.Not.Null.And.Not.Empty);
        var directory = Path.Combine(repository, "artifacts", "correctness", "alias-only-conditional-audit",
            scenario, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Producer.cs"), producerSource);
        await File.WriteAllTextAsync(Path.Combine(directory, "Subject.cs"), source);
        var producer = TestCompilation.Create("ConditionalProducer", producerSource, includeSharpProofReference: false);
        producer = producer.WithOptions(producer.Options.WithOptimizationLevel(OptimizationLevel.Release));
        await Save(directory, "producer-diagnostics.json", Diagnostics(producer.GetDiagnostics()));
        var producerPath = Path.Combine(directory, "producer.dll");
        using (var image = new MemoryStream())
        {
            var emitted = producer.Emit(image);
            await Save(directory, "producer-emit-diagnostics.json", Diagnostics(emitted.Diagnostics));
            Assert.That(emitted.Success, Is.True);
            await File.WriteAllBytesAsync(producerPath, image.ToArray());
        }
        var producerBytes = await File.ReadAllBytesAsync(producerPath);
        var producerPe = DescribePe(producerBytes);
        await Save(directory, "producer-pe.json", producerPe);
        var shadow = MetadataReference.CreateFromFile(producerPath,
            MetadataReferenceProperties.Assembly.WithAliases(ShadowAliases));
        var options = ((CSharpParseOptions)producer.SyntaxTrees.Single().Options).WithPreprocessorSymbols(symbols);
        var tree = CSharpSyntaxTree.ParseText(source, options, "Subject.cs");
        var compilation = CSharpCompilation.Create("ConditionalCaller_" + Guid.NewGuid().ToString("N"),
            [tree], TestMetadataReferences.WithSharpProof.Add(shadow), producer.Options);
        var diagnostics = compilation.GetDiagnostics();
        await Save(directory, "caller-diagnostics.json", Diagnostics(diagnostics));
        Assert.That(diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var logSyntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Log");
        var target = (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!;
        var log = (IMethodSymbol)model.GetDeclaredSymbol(logSyntax)!;
        var discovery = new ClaimManifestBuilder(compilation).Build();
        await QualifyCompiler(directory, scenario, source, producerSource, settings, symbols, shadow,
            compilation, root, model, targetSyntax, logSyntax, target, log, discovery, producerPe);
        byte[] callerBytes;
        using (var image = new MemoryStream())
        {
            var emitted = compilation.Emit(image);
            await Save(directory, "caller-emit-diagnostics.json", Diagnostics(emitted.Diagnostics));
            Assert.That(emitted.Success, Is.True);
            callerBytes = image.ToArray();
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, "caller.dll"), callerBytes);
        var callerPe = DescribePe(callerBytes);
        await Save(directory, "caller-pe.json", callerPe);
        await ObserveClr(directory, producerBytes, callerBytes, producerPe, callerPe, settings,
            retained, throws, inputs);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, directory, "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        await ObserveNative(directory, scenario, source, producerSource, settings, throws, artifact, producerPe, callerPe);
    }

    private static async Task QualifyCompiler(string directory, string scenario, string source, string producerSource,
        CaseSettings settings, ImmutableArray<string> symbols, PortableExecutableReference shadow,
        CSharpCompilation compilation, SyntaxNode root, SemanticModel model, MethodDeclarationSyntax targetSyntax,
        MethodDeclarationSyntax logSyntax, IMethodSymbol target, IMethodSymbol log, ClaimManifestBuildResult discovery,
        PeObservation producerPe)
    {
        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(shadow)!;
        var privateType = assembly.GetTypeByMetadataName(settings.Nested
            ? "System.Diagnostics+ConditionalAttribute" : "System.Diagnostics.ConditionalAttribute")!;
        var officialType = compilation.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute")!;
        var attributes = log.GetAttributes();
        var invocationSyntax = targetSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression is IdentifierNameSyntax { Identifier.ValueText: "Log" });
        var invocation = (IInvocationOperation)model.GetOperation(invocationSyntax)!;
        var assignment = (ISimpleAssignmentOperation)invocation.Arguments.Single().Value;
        var parameter = (IParameterReferenceOperation)assignment.Target;
        var requires = targetSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Requires" }).ToArray();
        var selected = discovery.Targets.Values.Single();
        var attributeObservations = attributes.Select(attribute => new
        {
            Type = attribute.AttributeClass!.ToDisplayString(),
            Assembly = attribute.AttributeClass.ContainingAssembly.Identity.ToString(),
            OwnerKind = attribute.AttributeClass.ContainingSymbol.Kind.ToString(),
            Constructor = attribute.AttributeConstructor!.ToDisplayString(),
            Argument = (string)attribute.ConstructorArguments.Single().Value!,
            IsPrivate = SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, privateType),
            IsOfficial = SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, officialType)
        }).ToArray();
        await Save(directory, "source-and-compiler.json", new
        {
            Scenario = scenario,
            SourceSha256 = Hash(Encoding.UTF8.GetBytes(source)),
            ProducerSourceSha256 = Hash(Encoding.UTF8.GetBytes(producerSource)),
            CallerSymbols = symbols,
            Producer = producerPe,
            Alias = shadow.Properties.Aliases,
            ReferencePath = shadow.FilePath,
            Compiler = typeof(CSharpCompilation).Assembly.FullName,
            CompilerMvid = typeof(CSharpCompilation).Assembly.ManifestModule.ModuleVersionId,
            LogIsConditional = log.IsConditional,
            LogAttributes = attributeObservations,
            AssignmentValue = assignment.Value.ConstantValue.Value,
            RequiresCount = requires.Length,
            Target = target.ToDisplayString(),
            CallableId = selected.Entry.CallableId,
            TargetSpan = new { targetSyntax.Span.Start, targetSyntax.Span.Length },
            LogCallSpan = new { invocationSyntax.Span.Start, invocationSyntax.Span.Length }
        });
        Assert.That(OperatingSystem.IsLinux(), Is.True);
        Assert.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(Architecture.X64));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        Assert.That(((CSharpParseOptions)root.SyntaxTree.Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        Assert.That(((CSharpParseOptions)root.SyntaxTree.Options).PreprocessorSymbolNames, Is.EqualTo(symbols));
        Assert.That(symbols, Does.Not.Contain(Contract.ConditionalSymbol));
        Assert.That(shadow.Properties.Aliases, Is.EqualTo(ShadowAliases));
        Assert.That(shadow.FilePath, Is.EqualTo(Path.Combine(directory, "producer.dll")));
        Assert.That(assembly.Name, Is.EqualTo(producerPe.AssemblyName));
        Assert.That(SymbolEqualityComparer.Default.Equals(privateType, officialType), Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(privateType.BaseType,
            compilation.GetTypeByMetadataName("System.Attribute")), Is.True);
        Assert.That(privateType.ContainingSymbol.Kind, Is.EqualTo(settings.Nested ? SymbolKind.NamedType : SymbolKind.Namespace));
        Assert.That(log.MethodKind, Is.EqualTo(MethodKind.Ordinary));
        Assert.That(log.IsStatic && log.ReturnsVoid && !log.IsAsync && !log.IsVirtual && !log.IsOverride && !log.IsExtern, Is.True);
        Assert.That(log.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(log.Parameters.Single().RefKind, Is.EqualTo(RefKind.None));
        Assert.That(logSyntax.Body!.Statements, Is.Empty);
        Assert.That(log.IsConditional, Is.EqualTo(!settings.Nested));
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod, log), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(parameter.Parameter, target.Parameters.Single()), Is.True);
        Assert.That(assignment.Value.ConstantValue.Value, Is.EqualTo(settings.Mixed ? 0 : 1));
        Assert.That(attributes.Length, Is.EqualTo(settings.Mixed ? 2 : 1));
        Assert.That(attributeObservations.Count(item => item.IsPrivate), Is.EqualTo(settings.Official ? 0 : 1));
        Assert.That(attributeObservations.Count(item => item.IsOfficial), Is.EqualTo(settings.Official || settings.Mixed ? 1 : 0));
        Assert.That(attributeObservations.All(item => item.IsPrivate || item.IsOfficial), Is.True);
        foreach (var attribute in attributes)
        {
            Assert.That(attribute.AttributeConstructor!.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_String));
            Assert.That(attribute.ConstructorArguments.Single().Kind, Is.EqualTo(TypedConstantKind.Primitive));
        }
        Assert.That(attributeObservations.Where(item => item.IsPrivate).Select(item => item.Argument),
            Is.EqualTo(settings.Official ? Array.Empty<string>() : new[] { settings.Mixed ? "PRIVATE_PRESENT" : "ABSENT" }));
        Assert.That(attributeObservations.Where(item => item.IsOfficial).Select(item => item.Argument),
            Is.EqualTo(settings.Official || settings.Mixed ? new[] { settings.Mixed ? "OFFICIAL_ABSENT" : "ABSENT" }
                : Array.Empty<string>()));
        Assert.That(target.IsStatic && target.Parameters.Single().RefKind == RefKind.None, Is.True);
        Assert.That(target.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(target.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(root.DescendantNodes().OfType<FieldDeclarationSyntax>(), Is.Empty);
        Assert.That(root.DescendantNodes().OfType<ConstructorDeclarationSyntax>(), Is.Empty);
        Assert.That(SymbolEqualityComparer.Default.Equals(selected.Method, target), Is.True);
        Assert.That(selected.EffectClaims.Single().Evidence.ContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        Assert.That(selected.EffectClaims.Single().HasValidConstraint, Is.True);
        Assert.That(requires.Length, Is.EqualTo(settings.Mixed ? 1 : 0));
        if (settings.Mixed)
        {
            var required = (IInvocationOperation)model.GetOperation(requires.Single())!;
            var predicate = (IBinaryOperation)required.Arguments.Single().Value;
            Assert.That(required.TargetMethod.Name, Is.EqualTo("Requires"));
            Assert.That(SymbolEqualityComparer.Default.Equals(required.TargetMethod.ContainingType,
                compilation.GetTypeByMetadataName(typeof(Contract).FullName!)), Is.True);
            Assert.That(predicate.OperatorKind, Is.EqualTo(BinaryOperatorKind.Equals));
            Assert.That(SymbolEqualityComparer.Default.Equals(((IParameterReferenceOperation)predicate.LeftOperand).Parameter,
                target.Parameters.Single()), Is.True);
            Assert.That(predicate.RightOperand.ConstantValue.Value, Is.EqualTo(1));
        }
    }

    private static async Task ObserveNative(string directory, string scenario, string source, string producerSource,
        CaseSettings settings, bool throws, CompilerManifestArtifact artifact, PeObservation producerPe, PeObservation callerPe)
    {
        var capturedReference = artifact.Compilation.References.Single(reference => reference.Aliases.Contains("Shadow"));
        var capturedModule = capturedReference.Modules.Single();
        await Save(directory, "original-reference-capture.json", new
        {
            AssemblyName = artifact.Compilation.AssemblyName,
            SyntaxTrees = artifact.Compilation.SyntaxTrees,
            CapturedReference = capturedReference,
            Producer = producerPe,
            Caller = callerPe
        });
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var artifactJson = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        var artifactBytes = Encoding.UTF8.GetBytes(artifactJson);
        await File.WriteAllTextAsync(Path.Combine(directory, "compiler-artifact.json"), artifactJson);
        await File.WriteAllTextAsync(Path.Combine(directory, "worker-request.json"),
            WorkerProtocolJson.SerializeRequest(project.Request));
        var preparation = project.Snapshot.Callables.Single();
        var captured = artifact.Callables.Single();
        var manifestClaim = artifact.Manifest.Claims.Single();
        var bound = project.Bind();
        var roundTripReferences = project.Snapshot.CompilerManifest.Compilation.References;
        await Save(directory, "capture-binding.json", new
        {
            ArtifactSha256 = Hash(artifactBytes),
            RequestArtifactSha256 = project.Request.CompilerManifest.Sha256,
            BoundArtifactDigest = bound.ArtifactDigest,
            bound.InputHash,
            captured.CallableId,
            manifestClaim.ClaimId,
            CapturedReference = capturedReference,
            RoundTripReferences = roundTripReferences,
            Producer = producerPe,
            Caller = callerPe,
            TotalPresent = captured.Total != null,
            TotalIsBodyAbstraction = captured.Total?.IsBodyAbstraction,
            PreparedTotalPresent = preparation.Total != null,
            Clauses = captured.Total?.Clauses.Select(clause => clause.Kind.ToString())
        });
        var budgets = project.Request.Budgets;
        var enrolled = PassiveCallableArtifactAdapter.Enroll(preparation);
        PassiveCallableVcPlan? plan = null;
        PassiveCallableCheckResult? entry = null;
        var failure = WorkerClaimReason.None;
        if (enrolled != null && PassiveCallableVcBuilder.TryBuild(enrolled, out plan, out failure))
        {
            using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
            entry = await solver.VerifyEntryAsync();
        }
        var native = await NativeExceptionEffectVerifier.VerifyAsync(preparation, budgets);
        var nativeSpan = native.ExceptionWitness?.Site is { } site && plan != null
            ? plan.Factory.GetOperationInfo(site).SourceSpan : null;
        ReplayObservation? replayObservation = null;
        IrProgramExecutionResult? replay = null;
        if (native.Outcome is RefutedOutcome && plan != null)
        {
            replay = plan.ReplayException(native.EntryModel, CancellationToken.None);
            var replaySpan = replay.Exception?.Site is { } replaySite
                ? plan.Factory.GetOperationInfo(replaySite).SourceSpan : null;
            replayObservation = new(replay.Status.ToString(), replay.ConsumedApproximation,
                replay.Exception?.Kind.ToString(), replay.Exception?.Site?.ToString(), replaySpan,
                replay.Instruction is IrThrowInstruction);
        }
        using var worker = SharpProofWorker.CreateNative(budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory, "worker-response.json"),
            WorkerProtocolJson.SerializeResponse(response));
        var requestHash = WorkerProtocolJson.ComputeRequestHash(project.Request);
        var validation = WorkerProtocolJson.ValidateForRequest(response, requestHash,
            WorkerProgram.ComputeExpectedInputHash(project.Request, artifactBytes), artifact.Manifest,
            project.Request, WorkerProgram.ExpectedVersions());
        var workerClaim = response.ClaimResults.Single();
        var callableResult = response.CallableResults.Single();
        var entryInput = entry?.EntryModel.Values.SingleOrDefault()?.IntegerNumericValue;
        var directInput = native.EntryModel.Values.SingleOrDefault()?.IntegerNumericValue;
        await Save(directory, "native-observations.json", new
        {
            Scenario = scenario,
            SourceSha256 = Hash(Encoding.UTF8.GetBytes(source)),
            ProducerSourceSha256 = Hash(Encoding.UTF8.GetBytes(producerSource)),
            CallerPeSha256 = callerPe.Sha256,
            ProducerPeSha256 = producerPe.Sha256,
            CallerMvid = callerPe.Mvid,
            ProducerMvid = producerPe.Mvid,
            ArtifactDigest = bound.ArtifactDigest,
            bound.InputHash,
            RequestHash = requestHash,
            captured.CallableId,
            manifestClaim.ClaimId,
            TotalPresent = captured.Total != null,
            TotalIsBodyAbstraction = captured.Total?.IsBodyAbstraction,
            PreparedTotalPresent = preparation.Total != null,
            Enrolled = enrolled != null,
            PlanBuilt = plan != null,
            PlanFailure = failure.ToString(),
            EntryOutcome = entry?.Outcome?.GetType().Name,
            EntryReason = entry?.Reason.ToString(),
            EntryQueryCompleted = entry?.QueryCompleted,
            EntryInput = entryInput?.ToString(CultureInfo.InvariantCulture),
            EntryModel = entry?.EntryModel.Select(item => new { Variable = item.Key.ToString(), Value = item.Value.ToString() }),
            DirectOutcome = native.Outcome?.GetType().Name,
            DirectReason = native.Reason.ToString(),
            DirectQueryCompleted = native.QueryCompleted,
            DirectCore = native.Core,
            DirectBodyAssumptions = native.BodyAssumptions.Select(operation => operation.ToString()),
            native.HasFeasibleEntryWitness,
            AllocationWitness = native.AllocationWitness?.ToString(),
            WriteWitness = native.WriteWitness?.ToString(),
            LockWitness = native.LockWitness?.ToString(),
            CallPreconditionWitness = native.CallPreconditionWitness?.ToString(),
            DirectInput = directInput?.ToString(CultureInfo.InvariantCulture),
            DirectModel = native.EntryModel.Select(item => new { Variable = item.Key.ToString(), Value = item.Value.ToString() }),
            ExceptionKind = native.ExceptionWitness?.Kind.ToString(),
            ExceptionSite = native.ExceptionWitness?.Site?.ToString(),
            ExceptionSourceLocation = nativeSpan,
            IsIrThrowInstruction = replay?.Instruction is IrThrowInstruction,
            Replay = replayObservation,
            WorkerOutcome = workerClaim.Outcome.ToString(),
            WorkerReason = workerClaim.Reason.ToString(),
            WorkerCertainty = workerClaim.EffectCertainty.ToString(),
            WorkerVacuity = workerClaim.Vacuity.ToString(),
            workerClaim.EffectWitness,
            CallableCoverage = callableResult.Coverage.ToString(),
            CallableCoverageReason = callableResult.Reason.ToString(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            ResponseErrors = response.Errors,
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            Budgets = budgets,
            CacheEnabled = project.Request.Cache.Enabled,
            VerifyPolicy = project.Request.VerifyPolicy.ToString(),
            AssumptionPolicy = project.Request.AssumptionPolicy.ToString(),
            ExpectedOutcome = throws ? "Refuted" : "Proven"
        });
        Assert.That(Hash(artifactBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
        Assert.That(Hash(artifactBytes), Is.EqualTo(bound.ArtifactDigest));
        Assert.That(bound.InputHash, Is.EqualTo(project.Snapshot.InputHash));
        Assert.That(artifact.Compilation.AssemblyName, Is.EqualTo(callerPe.AssemblyName));
        Assert.That(capturedReference.Aliases, Is.EqualTo(ShadowAliases));
        Assert.That(roundTripReferences, Is.Empty, "Compilation references are intentionally excluded from worker transport.");
        Assert.That(capturedModule.Path, Is.EqualTo(Path.Combine(directory, "producer.dll")));
        Assert.That(capturedModule.Sha256, Is.EqualTo(producerPe.Sha256));
        Assert.That(capturedModule.SizeBytes, Is.EqualTo(producerPe.Bytes));
        Assert.That(Guid.Parse(capturedModule.Mvid), Is.EqualTo(producerPe.Mvid));
        Assert.That(captured.Total, Is.Not.Null);
        Assert.That(captured.Total!.IsBodyAbstraction, Is.False);
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.False);
        Assert.That(captured.CallableId, Is.EqualTo(manifestClaim.CallableId));
        Assert.That(manifestClaim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
        Assert.That(manifestClaim.Evidence, Is.EqualTo(WorkerClaimEvidence.Attribute));
        Assert.That(manifestClaim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        Assert.That(preparation.Total.ValidEffectClaimIds, Does.Contain(manifestClaim.ClaimId));
        Assert.That(preparation.Total.ExceptionConstraints.Single().AllowedKinds, Is.Empty);
        Assert.That(captured.Total.Clauses.Count(clause => clause.Kind == CompilerContractKind.Requires),
            Is.EqualTo(settings.Mixed ? 1 : 0));
        Assert.That(artifact.Manifest.Callables.Single().Assumptions.Length, Is.EqualTo(settings.Mixed ? 1 : 0));
        Assert.That(project.Request.Cache.Enabled, Is.False);
        Assert.That(project.Request.VerifyPolicy, Is.EqualTo(WorkerVerifyPolicy.Advisory));
        Assert.That(project.Request.AssumptionPolicy, Is.EqualTo(WorkerAssumptionPolicy.Allow));
        Assert.That(budgets.QueryRlimit, Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
        Assert.That(enrolled, Is.Not.Null);
        Assert.That(plan, Is.Not.Null, failure.ToString());
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Outcome, Is.TypeOf<RefutedOutcome>(), entry.Reason.ToString());
        Assert.That(entry.Reason, Is.EqualTo(WorkerClaimReason.None));
        if (settings.Mixed)
        {
            Assert.That((int)entry.EntryModel.Values.Single().IntegerNumericValue, Is.EqualTo(1));
        }
        Assert.That(validation.IsValid, Is.True, string.Join("; ", validation.Errors));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
        Assert.That(workerClaim.ClaimId, Is.EqualTo(manifestClaim.ClaimId));
        Assert.That(callableResult.CallableId, Is.EqualTo(captured.CallableId));
        Assert.That(callableResult.Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
        Assert.That(callableResult.Reason, Is.EqualTo(WorkerCallableCoverageReason.None));
        Assert.That(workerClaim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(workerClaim.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(native.Outcome is ProvenOutcome or RefutedOutcome, Is.True, native.Reason.ToString());
        Assert.That(workerClaim.Outcome, Is.EqualTo(native.Outcome is RefutedOutcome
            ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven));
        if (native.Outcome is RefutedOutcome)
        {
            Assert.That(native.ExceptionWitness!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
            Assert.That(replay!.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.Instruction, Is.TypeOf<IrThrowInstruction>());
            Assert.That(replay.Instruction!.Operation, Is.EqualTo(native.ExceptionWitness.Site));
            Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
            Assert.That(replay.Exception.Site, Is.EqualTo(native.ExceptionWitness.Site));
            Assert.That(nativeSpan, Is.Not.Null);
            Assert.That(nativeSpan!.Start, Is.EqualTo(source.IndexOf("10 / x", StringComparison.Ordinal)));
            Assert.That(nativeSpan.Length, Is.EqualTo("10 / x".Length));
            Assert.That(workerClaim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(workerClaim.EffectWitness!.Kind, Is.EqualTo("implicit-throw"));
            Assert.That(workerClaim.EffectWitness.Detail, Is.EqualTo("System.DivideByZeroException"));
            Assert.That(workerClaim.EffectWitness.Effects, Is.EqualTo(WorkerEffectSet.Throws));
            Assert.That(workerClaim.EffectWitness.Capabilities, Is.EqualTo(WorkerEffectCapabilitySet.None));
            Assert.That(workerClaim.EffectWitness.Location.Path, Is.EqualTo(nativeSpan.Document));
            Assert.That(workerClaim.EffectWitness.Location.Start, Is.EqualTo(nativeSpan.Start));
            Assert.That(workerClaim.EffectWitness.Location.Length, Is.EqualTo(nativeSpan.Length));
            if (settings.Mixed)
            {
                Assert.That((int)native.EntryModel.Values.Single().IntegerNumericValue, Is.EqualTo(1));
            }
        }
        else
        {
            Assert.That(native.ExceptionWitness, Is.Null);
            Assert.That(workerClaim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(workerClaim.EffectWitness, Is.Null);
            Assert.That(workerClaim.ProofCore, Does.Contain("native-effect:" + manifestClaim.ClaimId));
        }
        Assert.That(native.Outcome, throws ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>());
        Assert.That(workerClaim.Outcome, Is.EqualTo(throws ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven));
    }

    private static async Task ObserveClr(string directory, byte[] producerBytes, byte[] callerBytes,
        PeObservation producerPe, PeObservation callerPe, CaseSettings settings, bool retained, bool throws, int[] inputs)
    {
        var context = new AssemblyLoadContext("ConditionalOracle_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var producerImage = new MemoryStream(producerBytes);
            var producerAssembly = context.LoadFromStream(producerImage);
            using var callerImage = new MemoryStream(callerBytes);
            var callerAssembly = context.LoadFromStream(callerImage);
            var type = callerAssembly.GetType("Fixture")!;
            var targetMethod = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var logMethod = type.GetMethod("Log", BindingFlags.NonPublic | BindingFlags.Static)!;
            var target = targetMethod.CreateDelegate<Func<int, int>>();
            var targetIl = DecodeIl(targetMethod, callerBytes);
            var logIl = DecodeIl(logMethod, callerBytes);
            var attributes = logMethod.GetCustomAttributesData().Select(attribute => new
            {
                Type = attribute.AttributeType.FullName,
                Assembly = attribute.AttributeType.Assembly.GetName().Name,
                Mvid = attribute.AttributeType.Module.ModuleVersionId,
                ConstructorToken = attribute.Constructor.MetadataToken,
                Argument = (string)attribute.ConstructorArguments.Single().Value!,
                IsPrivate = attribute.AttributeType.Assembly == producerAssembly,
                IsOfficial = attribute.AttributeType == typeof(System.Diagnostics.ConditionalAttribute)
            }).ToArray();
            await Save(directory, "decoded-il.json", new
            {
                Target = targetIl,
                Log = logIl,
                RuntimeAttributes = attributes,
                ProducerAssembly = producerAssembly.GetName().Name,
                ProducerMvid = producerAssembly.ManifestModule.ModuleVersionId,
                CallerAssembly = callerAssembly.GetName().Name,
                CallerMvid = callerAssembly.ManifestModule.ModuleVersionId,
                DelegateMethodToken = target.Method.MetadataToken,
                DelegateMvid = target.Method.Module.ModuleVersionId,
                DelegateTargetIsNull = target.Target == null
            });
            Assert.That(producerAssembly.GetName().Name, Is.EqualTo(producerPe.AssemblyName));
            Assert.That(producerAssembly.ManifestModule.ModuleVersionId, Is.EqualTo(producerPe.Mvid));
            Assert.That(callerAssembly.GetName().Name, Is.EqualTo(callerPe.AssemblyName));
            Assert.That(callerAssembly.ManifestModule.ModuleVersionId, Is.EqualTo(callerPe.Mvid));
            Assert.That(target.Target, Is.Null);
            Assert.That(target.Method, Is.EqualTo(targetMethod));
            Assert.That(targetMethod.IsStatic, Is.True);
            Assert.That(targetMethod.ReturnType, Is.EqualTo(typeof(int)));
            Assert.That(targetMethod.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(logMethod.IsStatic, Is.True);
            Assert.That(logMethod.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(logMethod.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(type.TypeInitializer, Is.Null);
            Assert.That(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance), Is.Empty);
            Assert.That(logIl.Instructions.Single().OpCode, Is.EqualTo("ret"));
            var calls = targetIl.Instructions.Where(instruction => instruction.OpCode is "call" or "callvirt").ToArray();
            var stores = targetIl.Instructions.Where(instruction => instruction.OpCode is "starg" or "starg.s").ToArray();
            var divisionInstruction = targetIl.Instructions.Single(instruction => instruction.OpCode == "div");
            Assert.That(calls.Length, Is.EqualTo(retained ? 1 : 0), "Requires is absent from the emitted caller.");
            Assert.That(stores.Length, Is.EqualTo(retained ? 1 : 0));
            if (retained)
            {
                var call = calls.Single();
                var store = stores.Single();
                Assert.That(call.Token, Is.EqualTo(logMethod.MetadataToken));
                Assert.That(call.ResolvedMvid, Is.EqualTo(callerPe.Mvid));
                Assert.That(call.ResolvedMethod, Is.EqualTo(logMethod.ToString()));
                Assert.That(store.RawOperand, Is.EqualTo(store.OpCode == "starg.s" ? "00" : "0000"));
                Assert.That(store.Offset, Is.LessThan(call.Offset));
                Assert.That(call.Offset, Is.LessThan(divisionInstruction.Offset));
                Assert.That(targetIl.Instructions.Count(instruction =>
                    instruction.OpCode == (settings.Mixed ? "ldc.i4.0" : "ldc.i4.1")), Is.EqualTo(1));
            }
            Assert.That(attributes.Length, Is.EqualTo(settings.Mixed ? 2 : 1));
            Assert.That(attributes.Count(attribute => attribute.IsPrivate), Is.EqualTo(settings.Official ? 0 : 1));
            Assert.That(attributes.Count(attribute => attribute.IsOfficial), Is.EqualTo(settings.Official || settings.Mixed ? 1 : 0));
            Assert.That(attributes.All(attribute => attribute.IsPrivate || attribute.IsOfficial), Is.True);
            foreach (var attribute in attributes.Where(attribute => attribute.IsPrivate))
            {
                Assert.That(attribute.Mvid, Is.EqualTo(producerPe.Mvid));
                Assert.That(attribute.Argument, Is.EqualTo(settings.Mixed ? "PRIVATE_PRESENT" : "ABSENT"));
            }
            foreach (var attribute in attributes.Where(attribute => attribute.IsOfficial))
            {
                Assert.That(attribute.Argument, Is.EqualTo(settings.Mixed ? "OFFICIAL_ABSENT" : "ABSENT"));
            }
            var observations = inputs.Select(input => Invoke(target, input)).ToArray();
            await Save(directory, "clr-observations.json", new
            {
                Inputs = inputs,
                Observations = observations,
                ExpectedThrows = throws,
                HasValidPrecondition = !settings.Mixed || inputs.All(input => input == 1)
            });
            Assert.That(observations.Select(observation => observation.ExceptionType),
                Is.All.EqualTo(throws ? typeof(DivideByZeroException).FullName : null));
            Assert.That(observations.Select(observation => observation.Returned), Is.All.EqualTo(throws ? (int?)null : 10));
            Assert.That(!settings.Mixed || inputs.All(input => input == 1), Is.True);
        }
        finally
        {
            context.Unload();
        }
    }

    private static ClrObservation Invoke(Func<int, int> target, int input)
    {
        try
        {
            return new(input, target(input), null);
        }
        catch (DivideByZeroException exception)
        {
            return new(input, null, exception.GetType().FullName);
        }
    }

    private static PeObservation DescribePe(byte[] bytes)
    {
        using var image = new MemoryStream(bytes);
        using var pe = new PEReader(image);
        var metadata = pe.GetMetadataReader();
        return new(Hash(bytes), bytes.Length, metadata.GetString(metadata.GetAssemblyDefinition().Name),
            metadata.GetGuid(metadata.GetModuleDefinition().Mvid));
    }

    private static IlObservation DecodeIl(MethodInfo method, byte[] bytes)
    {
        using var image = new MemoryStream(bytes);
        using var pe = new PEReader(image);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff));
        var il = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()!;
        Assert.That(il, Is.EqualTo(method.GetMethodBody()!.GetILAsByteArray()));
        var instructions = new List<IlInstruction>();
        for (var offset = 0; offset < il.Length;)
        {
            var start = offset;
            ushort value = il[offset++];
            if (value == 0xfe)
            {
                value = (ushort)(0xfe00 | il[offset++]);
            }
            var opcode = OpCodesByValue[value];
            var size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
            Assert.That(offset + size, Is.LessThanOrEqualTo(il.Length));
            int? token = null;
            string? resolved = null;
            Guid? resolvedMvid = null;
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                token = BitConverter.ToInt32(il, offset);
                var member = method.Module.ResolveMethod(token.Value)!;
                resolved = member.ToString();
                resolvedMvid = member.Module.ModuleVersionId;
            }
            instructions.Add(new(start, opcode.Name!, Convert.ToHexStringLower(il.AsSpan(offset, size)),
                token, resolved, resolvedMvid));
            offset += size;
        }
        return new(method.MetadataToken, method.Module.ModuleVersionId, Convert.ToHexStringLower(il), instructions);
    }

    private static object[] Diagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Select(diagnostic => (object)new
        {
            diagnostic.Id,
            Severity = diagnostic.Severity.ToString(),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            Start = diagnostic.Location.SourceSpan.Start,
            Length = diagnostic.Location.SourceSpan.Length
        }).ToArray();
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name),
            JsonSerializer.Serialize(value, WorkerProtocolJson.SharedOptions) + "\n");
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static CaseSettings Settings(string scenario)
    {
        return scenario switch
        {
            "alias-only-absent" => new(false, false, false, false),
            "official-absent" => new(true, false, false, false),
            "official-present" => new(true, true, false, false),
            "alias-only-present" => new(false, true, false, false),
            "nested-name-negative" => new(false, false, true, false),
            "mixed-private-present" => new(false, true, false, true),
            "mixed-all-absent" => new(false, false, false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private sealed record CaseSettings(bool Official, bool Present, bool Nested, bool Mixed);
    private sealed record PeObservation(string Sha256, int Bytes, string AssemblyName, Guid Mvid);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand,
        int? Token, string? ResolvedMethod, Guid? ResolvedMvid);
    private sealed record IlObservation(int MethodToken, Guid Mvid, string RawIl, List<IlInstruction> Instructions);
    private sealed record ClrObservation(int Input, int? Returned, string? ExceptionType);
    private sealed record ReplayObservation(string Status, bool ConsumedApproximation,
        string? Kind, string? Site, IrSourceSpan? SourceSpan, bool IsIrThrowInstruction);
}
