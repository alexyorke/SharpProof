using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskCarrierAdmissionGapTests
{
    [Test]
    public void CompilerErrorsRetainAnExplicitResourceGapThroughTheArtifactBoundary()
    {
        const string source = "using SharpProof.Attributes; public static class C {" +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)] public static void Target() {Missing();}}";
        var initial = TestCompilation.Create("DiagnosticResourceGap", [("Subject.cs", source.Replace("Missing();", "", StringComparison.Ordinal))]);
        var tree = initial.SyntaxTrees.Single();
        var compilation = initial.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(source, (CSharpParseOptions)tree.Options, tree.FilePath));
        Assert.That(compilation.GetDiagnostics().Any(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Id == "CS0103"), Is.True);
        var (artifact, target) = RoundTrip(compilation);
        Assert.That(artifact.CompilerDiagnostics.Any(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Code == "compiler.CS0103"), Is.True);
        Assert.That(artifact.Callables.Single().ResourceUnknownReason, Is.EqualTo("CompilationOrBodyUnavailable"));
        Assert.That(target.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        Assert.That(target.ResourceUnknownReason, Is.EqualTo("CompilationOrBodyUnavailable"));
        Assert.That(target.ResourceProgram, Is.Null);
        Assert.That(target.Total, Is.Null);
        Assert.That(target.ForkJoinProjection, Is.Null);
        Assert.That(target.EffectClaims.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnExactAccessorBodyCannotBorrowOrdinaryMethodResourceAdmission(bool accessor)
    {
        const string attribute = "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)]";
        var declaration = accessor ? "public static int Value {" + attribute + "get {return 7;}}" :
            attribute + "public static int Target() {return 7;}";
        var compilation = TestCompilation.Create("ResourceCallableKind", [("Subject.cs",
            "using SharpProof.Attributes; public static class C {" + declaration + "}")]);
        var (_, target) = RoundTrip(compilation);
        Assert.That(target.Total, Is.Not.Null);
        Assert.That(target.Total!.IsBodyAbstraction, Is.False);
        if (accessor)
        {
            Assert.That(target.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
            Assert.That(target.ResourceProgram, Is.Null);
            Assert.That(target.ResourceUnknownReason, Is.EqualTo("UnsupportedResourceCallable"));
            Assert.That(target.EffectClaims.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
            Assert.That(target.EffectClaims.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        }
        else
        {
            Assert.That(target.FailureReason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(target.ResourceProgram, Is.Not.Null);
            Assert.That(target.ResourceUnknownReason, Is.Null);
        }
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public void ASourceExceptionShadowFailsEmissionBeforePublishingCarrierEvidence(OptimizationLevel optimization, bool shadow)
    {
        var source = "#pragma warning disable CS0436\nusing System.Threading.Tasks; " +
            (shadow ? "namespace System {public sealed class Exception : SystemException {}} " : "") +
            "public static class C {public static async Task Root(int n) => await " +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))).ConfigureAwait(false); static void Child(int n) {}}";
        var initial = TestCompilation.Create("BoundaryFrameworkIdentity", [("Subject.cs", source)]);
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var exception = compilation.GetTypeByMetadataName("System.Exception")!;
        Assert.That(SymbolEqualityComparer.Default.Equals(exception.ContainingAssembly, compilation.Assembly), Is.EqualTo(shadow));
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        using var emitted = new MemoryStream();
        var emission = compilation.Emit(emitted, cancellationToken: cancellation);
        var errors = emission.Diagnostics.Where(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (shadow)
        {
            Assert.That(emission.Success, Is.False);
            Assert.That(errors.Any(static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) :
                diagnostic.Id == "CS0656" && diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                    .Contains("AsyncTaskMethodBuilder.SetException", StringComparison.Ordinal)), Is.True,
                string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic is null ?
                    throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.ToString())));
        }
        else
        {
            Assert.That(emission.Success, Is.True);
            Assert.That(errors, Is.Empty);
        }
        var collector = new CompilerTaskCarrierCollector(compilation);
        var carriers = collector.Capture(method, "root", cancellation);
        var callbacks = collector.CaptureCallbackActivations(method, "root", cancellation);
        var bodies = collector.CaptureSourceBodies(method, "root", cancellation);
        var boundary = collector.CaptureExceptionBoundary(method, "root", cancellation);
        if (shadow)
        {
            Assert.That(carriers.Shapes, Is.Null);
            Assert.That(carriers.UnknownReason, Is.EqualTo("TaskCarrierEmissionFailed"));
            Assert.That(callbacks.Activations, Is.Null);
            Assert.That(callbacks.UnknownReason, Is.EqualTo(carriers.UnknownReason));
            Assert.That(bodies.Inventory, Is.Null);
            Assert.That(bodies.UnknownReason, Is.EqualTo(carriers.UnknownReason));
            Assert.That(boundary.Boundary, Is.Null);
            Assert.That(boundary.UnknownReason, Is.EqualTo(carriers.UnknownReason));
            var faults = collector.CaptureEmittedFaultBodies(method, "root", cancellation);
            Assert.That(faults.Inventory, Is.Null);
            Assert.That(faults.UnknownReason, Is.EqualTo(carriers.UnknownReason));
        }
        else
        {
            Assert.That(carriers.UnknownReason, Is.Null);
            Assert.That(carriers.Shapes!.MatchedCallbacks, Is.EqualTo(2));
            Assert.That(callbacks.UnknownReason, Is.Null);
            Assert.That(callbacks.Activations!.ModuleSha256, Is.EqualTo(carriers.Shapes.ModuleSha256));
            Assert.That(bodies.UnknownReason, Is.Null);
            Assert.That(bodies.Inventory!.ModuleSha256, Is.EqualTo(carriers.Shapes.ModuleSha256));
            Assert.That(boundary.UnknownReason, Is.Null);
            Assert.That(boundary.Boundary!.ModuleSha256, Is.EqualTo(carriers.Shapes.ModuleSha256));
        }
    }

    [TestCase("")]
    [TestCase(" \t")]
    public void AValidSourceProjectionRequiresANonBlankOwningCallableIdentity(string callableId)
    {
        ArgumentNullException.ThrowIfNull(callableId);
        var compilation = TestCompilation.Create("ProjectionIdentity", [("Subject.cs",
            "using System.Threading.Tasks; public static class C {public static Task Root(int n) => " +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int n) {}}")]);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var control = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", cancellation);
        Assert.That(control.UnknownReason, Is.Null);
        Assert.That(control.Projection!.Children, Has.Length.EqualTo(2));
        Action lower = () => { _ = CompilerForkJoinResourceLowering.Lower(method, compilation, callableId, cancellation); };
        Assert.That(Assert.Throws<ArgumentException>(lower)!.ParamName, Is.EqualTo("callableId"));
    }

    [Test]
    public void BoundaryValidationRejectsANilMethodHandleInAValidImage()
    {
        var (bytes, framework) = CompileBoundaryConstructor(false);
        using var stream = new MemoryStream(bytes, false);
        using var image = new PEReader(stream);
        var method = BoundaryConstructor(image.GetMetadataReader());
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(CompilerTaskCarrierCollector.ValidateExceptionBoundary(image, method, framework, 2, cancellation), Is.Null);
        Assert.That(CompilerTaskCarrierCollector.ValidateExceptionBoundary(image,
            MetadataTokens.MethodDefinitionHandle(0), framework, 2, cancellation),
            Is.EqualTo("TaskRootExceptionBoundaryMethodUnavailable"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConstructorBoundaryStorageRequiresAWritableOwnedField(bool readOnly)
    {
        var (bytes, framework) = CompileBoundaryConstructor(readOnly);
        using var stream = new MemoryStream(bytes, false);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var method = BoundaryConstructor(metadata);
        var definition = metadata.GetMethodDefinition(method);
        var field = metadata.GetTypeDefinition(definition.GetDeclaringType()).GetFields().Single();
        Assert.That((metadata.GetFieldDefinition(field).Attributes & System.Reflection.FieldAttributes.InitOnly) != 0,
            Is.EqualTo(readOnly));
        var body = image.GetMethodBody(definition.RelativeVirtualAddress);
        var region = body.ExceptionRegions.Single();
        Assert.That(region.Kind, Is.EqualTo(ExceptionRegionKind.Catch));
        var code = body.GetILBytes()!;
        var cursor = 0;
        var ownsStoreBeforeTry = false;
        while (cursor < region.TryOffset)
        {
            Assert.That(CompilerScalarIlDecoder.TryReadMetadataInstruction(code, ref cursor, out var instruction), Is.True);
            ownsStoreBeforeTry |= instruction.RawCode == "Stfld" && instruction.Operand == MetadataTokens.GetToken(field);
        }
        Assert.That(ownsStoreBeforeTry, Is.True, "The real constructor must write its owned field outside the protected region.");
        // These are component body checks on real constructors, not generated
        // MoveNext ownership or a task-root runtime bound.
        Assert.That(CompilerTaskCarrierCollector.ValidateExceptionBoundary(image, method, framework, 2,
            TestContext.CurrentContext.CancellationToken),
            Is.EqualTo(readOnly ? "TaskRootExceptionBoundarySourceEscape" : null));
    }

    private static (byte[] Bytes, AssemblyIdentity Framework) CompileBoundaryConstructor(bool readOnly)
    {
        var source = "using System; using System.Threading.Tasks; using System.Runtime.CompilerServices; public struct C {public " +
            (readOnly ? "readonly " : "") + "int Value; public C(int ignored) {Value=7; AsyncTaskMethodBuilder builder=default; " +
            "try {Task.WhenAll(Task.Run(Helper),Task.Run(Helper)).ConfigureAwait(false).GetAwaiter().GetResult();} " +
            "catch(Exception exception) {builder.SetException(exception);} builder.SetResult();} static void Helper() {}}";
        var initial = TestCompilation.Create("ConstructorBoundary", [("Subject.cs", source)]);
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(OptimizationLevel.Debug));
        TestCompilation.AssertNoErrors(compilation);
        using var emitted = new MemoryStream();
        var emission = compilation.Emit(emitted, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        return (emitted.ToArray(), compilation.GetTypeByMetadataName("System.Exception")!.ContainingAssembly.Identity);
    }

    private static MethodDefinitionHandle BoundaryConstructor(MetadataReader metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var owner = metadata.TypeDefinitions.Single(handle => metadata.GetString(metadata.GetTypeDefinition(handle).Name) == "C");
        return metadata.GetTypeDefinition(owner).GetMethods().Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == ".ctor");
    }

    private static (CompilerManifestArtifact Artifact, CompilerCallablePreparation Target) RoundTrip(CSharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellation);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellation);
        return (artifact, preparations.Single());
    }
}
