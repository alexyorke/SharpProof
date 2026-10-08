using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class SynchronizedAutoGetterPurityAuditTests
{
    private const string SubjectTemplate = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public sealed class Subject
        {
            public Subject() { Value = 123; }

            public int Value
            {
                [EnforcePure]
                [MethodImpl(__METHOD_IMPL_OPTIONS__)]
                get;
            }
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectedAutoGetterFlagsMatchNativePurity(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized" : "no-inlining-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-auto-getter-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);

        var compilation = TestCompilation.Create("SynchronizedAutoGetterPurity_" + caseName, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var accessorSyntax = root.DescendantNodes().OfType<AccessorDeclarationSyntax>().Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var declaredGetter = semanticModel.GetDeclaredSymbol(accessorSyntax)!;
        var subjectSymbol = compilation.GetTypeByMetadataName("Subject")!;
        var propertySymbol = subjectSymbol.GetMembers("Value").OfType<IPropertySymbol>().Single();
        var getterSymbol = propertySymbol.GetMethod!;
        var sourceField = subjectSymbol.GetMembers().OfType<IFieldSymbol>().Single();
        var expectedFlags = synchronized ? 40 : 8;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accessorSyntax.Kind(), Is.EqualTo(SyntaxKind.GetAccessorDeclaration));
            Assert.That(accessorSyntax.Body, Is.Null);
            Assert.That(accessorSyntax.ExpressionBody, Is.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(declaredGetter, getterSymbol), Is.True);
            Assert.That(getterSymbol.MethodKind, Is.EqualTo(MethodKind.PropertyGet));
            Assert.That(getterSymbol.MetadataName, Is.EqualTo("get_Value"));
            Assert.That(getterSymbol.IsStatic, Is.False);
            Assert.That(getterSymbol.Parameters, Is.Empty);
            Assert.That(getterSymbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That((int)getterSymbol.MethodImplementationFlags, Is.EqualTo(expectedFlags));
            Assert.That(propertySymbol.SetMethod, Is.Null);
            Assert.That(sourceField.IsImplicitlyDeclared, Is.True);
            Assert.That(sourceField.IsReadOnly, Is.True);
            Assert.That(sourceField.IsStatic, Is.False);
            Assert.That(sourceField.MetadataName, Is.EqualTo("<Value>k__BackingField"));
            Assert.That(SymbolEqualityComparer.Default.Equals(sourceField.AssociatedSymbol, propertySymbol), Is.True);
        }

        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var target = discovery.Targets.Single().Value;
        var accessorId = target.Entry.CallableId;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(target.Method, getterSymbol), Is.True);
            Assert.That(target.Declaration, Is.TypeOf<AccessorDeclarationSyntax>());
            Assert.That(target.Declaration!.SyntaxTree, Is.SameAs(tree));
            Assert.That(target.Declaration.Span, Is.EqualTo(accessorSyntax.Span));
            Assert.That(discovery.Manifest.Callables, Has.Length.EqualTo(1));
            Assert.That(discovery.Manifest.Callables.Single().CallableId, Is.EqualTo(accessorId));
            Assert.That(discovery.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(discovery.Manifest.Claims.Single().CallableId, Is.EqualTo(accessorId));
        }
        await Save(evidenceDirectory, "source-bindings.json", new
        {
            SourceSha256 = sourceHash,
            CallableId = accessorId,
            Symbol = getterSymbol.ToDisplayString(),
            MethodKind = getterSymbol.MethodKind.ToString(),
            getterSymbol.MetadataName,
            RawImplementationFlags = (int)getterSymbol.MethodImplementationFlags,
            AccessorSyntaxKind = accessorSyntax.Kind().ToString(),
            accessorSyntax.SpanStart,
            SpanLength = accessorSyntax.Span.Length,
            AutoGetter = accessorSyntax.Body == null && accessorSyntax.ExpressionBody == null,
            SourceBackingField = sourceField.MetadataName,
            SourceBackingFieldReadOnly = sourceField.IsReadOnly,
            SelectedClaimId = discovery.Manifest.Claims.Single().ClaimId
        });

        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await Save(evidenceDirectory, "emission.json", new
        {
            SourceSha256 = sourceHash,
            Optimization = compilation.Options.OptimizationLevel.ToString(),
            emission.Success,
            Diagnostics = emission.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray()
        });
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "emitted-subject.dll"), imageBytes);
        image.Position = 0;
        var loadContext = new AssemblyLoadContext("SynchronizedAutoGetterPurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        RuntimeObservation runtimeObservation;
        try
        {
            var subject = loadContext.LoadFromStream(image).GetType("Subject")!;
            var property = subject.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var getter = property.GetMethod!;
            var fields = subject.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly);
            Assert.That(fields, Has.Length.EqualTo(1));
            var field = fields.Single();
            var il = getter.GetMethodBody()!.GetILAsByteArray()!;
            Assert.That(il, Has.Length.EqualTo(7));
            var ilFieldToken = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(2, 4));
            var resolvedField = subject.Module.ResolveField(ilFieldToken)!;
            var flags = getter.GetMethodImplementationFlags();
            var instance = Activator.CreateInstance(subject)!;
            var execute = getter.CreateDelegate<Func<int>>(instance);
            int[] returnValues = [execute(), execute(), execute()];
            runtimeObservation = new((int)flags, flags.ToString(), getter.MetadataToken, field.MetadataToken, ilFieldToken,
                field.Name, field.IsInitOnly, field.IsStatic, subject.TypeInitializer != null, returnValues,
                Convert.ToHexStringLower(il), subject.Module.ModuleVersionId.ToString("D"));
            await Save(evidenceDirectory, "runtime-observations.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                CallableId = accessorId,
                Case = caseName,
                ExpectedSynchronized = synchronized,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                TypedInvocation = "Closed Func<int> bound to the emitted Subject instance",
                Observation = runtimeObservation,
                SynchronizationBasis = "Reflected Synchronized flag and CLR runtime source semantics; no contention or timing observation.",
                ClrSource = "https://github.com/dotnet/runtime/blob/v9.0.0/src/coreclr/jit/flowgraph.cpp#L1572-L1588",
                RuntimeLimit = "Runtime identity is recorded. The reference CLR source is v9.0.0; no JIT machine-code disassembly is performed."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That(runtimeObservation.RawImplementationFlags, Is.EqualTo(expectedFlags));
                Assert.That((flags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((flags & MethodImplAttributes.Synchronized) != 0, Is.EqualTo(synchronized));
                Assert.That(getter.Name, Is.EqualTo(getterSymbol.MetadataName));
                Assert.That(getter.IsStatic, Is.False);
                Assert.That(getter.ReturnType, Is.EqualTo(typeof(int)));
                Assert.That(getter.GetParameters(), Is.Empty);
                Assert.That(property.SetMethod, Is.Null);
                Assert.That(field.Name, Is.EqualTo(sourceField.MetadataName));
                Assert.That(field.FieldType, Is.EqualTo(typeof(int)));
                Assert.That(field.IsInitOnly, Is.True);
                Assert.That(field.IsStatic, Is.False);
                Assert.That(subject.TypeInitializer, Is.Null);
                Assert.That(il[0], Is.EqualTo(0x02));
                Assert.That(il[1], Is.EqualTo(0x7b));
                Assert.That(il[6], Is.EqualTo(0x2a));
                Assert.That(ilFieldToken, Is.EqualTo(field.MetadataToken));
                Assert.That(resolvedField.MetadataToken, Is.EqualTo(field.MetadataToken));
                Assert.That(resolvedField.DeclaringType, Is.EqualTo(subject));
                Assert.That(returnValues, Has.Length.EqualTo(3));
                Assert.That(returnValues, Is.All.EqualTo(123));
            }
        }
        finally
        {
            loadContext.Unload();
        }

        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var manifestBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "compiler-manifest.json"), manifestBytes);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-request.json"),
            WorkerProtocolJson.SerializeRequest(project.Request) + "\n");
        Assert.That(Hash(manifestBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-response.json"),
            WorkerProtocolJson.SerializeResponse(response) + "\n");
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(project.Request),
            Program.ComputeExpectedInputHash(project.Request, manifestBytes), artifact.Manifest, project.Request, Program.ExpectedVersions());
        await Save(evidenceDirectory, "native-observations.json", new
        {
            SourceSha256 = sourceHash,
            EmittedPeSha256 = Hash(imageBytes),
            Case = caseName,
            CallableId = accessorId,
            SameCompilationForClrAndNative = true,
            CapturedTotal = artifact.Callables.Single().Total != null,
            ReflectedImplementationFlags = runtimeObservation.ImplementationFlags,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            response.Errors,
            ManifestClaims = artifact.Manifest.Claims.Select(claim => new
            {
                claim.ClaimId,
                claim.CallableId,
                Kind = claim.Kind.ToString(),
                ContractKind = claim.EffectContractKind.ToString()
            }).ToArray(),
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString(),
                EffectCertainty = claim.EffectCertainty.ToString(),
                Vacuity = claim.Vacuity.ToString()
            }).ToArray(),
            FrozenExpectation = synchronized ? "The selected synchronized auto getter cannot be Proven EnforcePure."
                : "The NoInlining-only selected auto getter remains Proven EnforcePure with complete native may-effect evidence."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.True, JsonSerializer.Serialize(validation.Errors));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(project.Request.Cache.Enabled, Is.False);
            Assert.That(JsonSerializer.Serialize(project.Request.Budgets), Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets())));
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Callables.Single().CallableId, Is.EqualTo(accessorId));
            Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Callables.Single().CallableId, Is.EqualTo(accessorId));
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims.Single().CallableId, Is.EqualTo(accessorId));
            Assert.That(artifact.Manifest.Claims.Single().Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
            Assert.That(response.CallableResults.Single().CallableId, Is.EqualTo(accessorId));
            var claim = response.ClaimResults.Single();
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (synchronized)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "CLR synchronization is excluded by EnforcePure, including the selected auto getter entry.");
            }
            else
            {
                Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
                Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            }
        }
    }

    private static string Hash(byte[] value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed record RuntimeObservation(int RawImplementationFlags, string ImplementationFlags, int GetterMetadataToken,
        int BackingFieldMetadataToken, int IlFieldToken, string BackingFieldName, bool BackingFieldReadOnly,
        bool BackingFieldStatic, bool HasTypeInitializer, int[] ReturnValues, string IlHex, string ModuleVersionId);
}
