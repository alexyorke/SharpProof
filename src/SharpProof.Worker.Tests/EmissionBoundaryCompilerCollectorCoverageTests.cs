using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Frontend;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmissionBoundaryCompilerCollectorCoverageTests
{
    [TestCase("source")]
    [TestCase("callbacks")]
    [TestCase("exception")]
    public void TruncatedPeImagesPublishTheSpecificUnknownAndNoPartialEvidence(string boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        var fixture = CreateFixture(boundary == "exception");
        var truncated = fixture.Image[..128];
        var cancellation = TestContext.CurrentContext.CancellationToken;
        if (boundary == "source")
        {
            var result = CompilerTaskCarrierCollector.ReadSourceBodies(truncated, fixture.Activations, fixture.Shapes,
                fixture.Framework.ToString(), fixture.Framework, cancellation);
            Assert.That(result.Context, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo("TaskSourceBodyEmissionInvalid"));
        }
        else if (boundary == "callbacks")
        {
            var result = CompilerTaskCarrierCollector.ReadCallbackActivations(fixture.Method, fixture.Plan,
                fixture.Assembly, truncated, fixture.Shapes, "root", cancellation);
            Assert.That(result.Activations, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo("TaskCallbackForwarderEmissionInvalid"));
        }
        else
        {
            var result = CompilerTaskCarrierCollector.ReadExceptionBoundary(fixture.Assembly, truncated,
                fixture.Shapes, fixture.Shapes.StateMachine!, "root", fixture.Framework, 2, cancellation);
            Assert.That(result.Boundary, Is.Null);
            Assert.That(result.UnknownReason, Is.EqualTo("TaskRootExceptionBoundaryEmissionInvalid"));
        }
    }

    [TestCase("missing-root", "TaskCarrierRootMetadataUnavailable")]
    [TestCase("missing-callbacks", "TaskCarrierCallbackMetadataIncomplete")]
    public void CarrierMetadataMustContainTheAdmittedSourceRootAndEveryCallback(string mutation, string expected)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(expected);
        var fixture = CreateFixture(false);
        var emitted = Emit(mutation == "missing-root" ? "public static class Other {}" : EmptyRootSource);

        var result = CompilerTaskCarrierCollector.CaptureEmittedCarriers(fixture.Method, fixture.Plan,
            emitted.Assembly, WorkerProtocolJson.ComputeSha256([.. emitted.Image]), "root", TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Shapes, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(expected));
    }

    [TestCase(false, "TaskCallbackSourceTargetUnavailable")]
    [TestCase(true, "TaskCallbackForwarderCoverageIncomplete")]
    public void CallbackBindingRejectsAMissingSourceTargetOrMissingEmittedForwarders(bool retainChild, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var fixture = CreateFixture(false);
        var emitted = Emit(retainChild ? EmptyRootSource : """
            using System.Threading.Tasks;
            public static class Subject { public static Task Root(int n) => Task.CompletedTask; }
            """);

        var result = CompilerTaskCarrierCollector.ReadCallbackActivations(fixture.Method, fixture.Plan,
            emitted.Assembly, emitted.Image, fixture.Shapes, "root", TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Activations, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(expected));
    }

    [Test]
    public void ExceptionBindingRejectsAMissingOwnedStateMachine()
    {
        var fixture = CreateFixture(true);
        var emitted = Emit(EmptyRootSource);

        var result = CompilerTaskCarrierCollector.ReadExceptionBoundary(emitted.Assembly, emitted.Image,
            fixture.Shapes, fixture.Shapes.StateMachine!, "root", fixture.Framework, 2, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Boundary, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskRootExceptionBoundaryMethodUnavailable"));
    }

    [Test]
    public void AnUnemittedSourceMethodCannotSupplyAnOwnedMetadataDefinitionHandle()
    {
        var fixture = CreateFixture(true);
        var emitted = Emit("""
            [System.Runtime.CompilerServices.CompilerGenerated]
            public struct UnemittedBoundary { public void MoveNext() {} }
            """);
        var type = emitted.Compilation.GetTypeByMetadataName("UnemittedBoundary")!;
        var method = type.GetMembers("MoveNext").OfType<IMethodSymbol>().Single();
        Assert.That(method.MetadataToken, Is.Zero, "A source symbol has not acquired an emitted method definition.");
        var inspectShape = typeof(CompilerTaskCarrierCollector).GetMethod("Shape", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(inspectShape, Is.Not.Null);
        var shape = (CompilerTaskCarrierType?)inspectShape!.Invoke(null,
            [type, fixture.Framework.ToString(), TestContext.CurrentContext.CancellationToken]);
        Assert.That(shape, Is.Not.Null);

        var result = CompilerTaskCarrierCollector.ReadExceptionBoundary(emitted.Compilation.Assembly, emitted.Image,
            fixture.Shapes, shape!, "root", fixture.Framework, 2, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Boundary, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskRootExceptionBoundaryMethodUnavailable"));
    }

    [Test]
    public void ExceptionBindingRequiresTheFrameworkCatchTypeAuthority()
    {
        var fixture = CreateFixture(true);

        var result = CompilerTaskCarrierCollector.ReadExceptionBoundary(fixture.Assembly, fixture.Image,
            fixture.Shapes, fixture.Shapes.StateMachine!, "root", null, 2, TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Boundary, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskRootExceptionBoundaryCatchUnsupported"));
    }

    [Test]
    public void ExceptionBindingCannotCoverAChildMissingFromTheEmittedJoin()
    {
        var fixture = CreateFixture(true);
        var source = TestCompilation.Create(nameof(EmissionBoundaryCompilerCollectorCoverageTests), Source(true, 3));
        TestCompilation.AssertNoErrors(source);
        var method = source.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var sourcePlan = ResourceForkJoinPlan.TryCreate(method, source, TestContext.CurrentContext.CancellationToken);
        Assert.That(sourcePlan, Is.Not.Null);

        var result = CompilerTaskCarrierCollector.ReadExceptionBoundary(fixture.Assembly, fixture.Image,
            fixture.Shapes, fixture.Shapes.StateMachine!, "root", fixture.Framework, sourcePlan!.Children.Length,
            TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Boundary, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskRootExceptionBoundaryCallsIncomplete"));
    }

    [Test]
    public void MetadataBoundaryCancellationPropagatesInsteadOfBecomingAnUnknown()
    {
        var fixture = CreateFixture(true);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        Assert.Throws<OperationCanceledException>((Action)(() => CompilerTaskCarrierCollector.ReadSourceBodies(
            [], fixture.Activations, fixture.Shapes, fixture.Framework.ToString(), fixture.Framework, canceled.Token)));
        Assert.Throws<OperationCanceledException>((Action)(() => CompilerTaskCarrierCollector.ReadCallbackActivations(
            fixture.Method, fixture.Plan, fixture.Assembly, [], fixture.Shapes, "root", canceled.Token)));
        Assert.Throws<OperationCanceledException>((Action)(() => CompilerTaskCarrierCollector.ReadExceptionBoundary(
            fixture.Assembly, [], fixture.Shapes, fixture.Shapes.StateMachine!, "root", fixture.Framework, 2, canceled.Token)));
        Assert.Throws<OperationCanceledException>((Action)(() => CompilerTaskCarrierCollector.CaptureEmittedCarriers(
            fixture.Method, fixture.Plan, fixture.Assembly, fixture.Shapes.ModuleSha256, "root", canceled.Token)));
    }

    private const string EmptyRootSource = """
        using System.Threading.Tasks;
        public static class Subject {
            public static Task Root(int n) => Task.CompletedTask;
            private static void Child(int n) {}
        }
        """;

    private static string Source(bool configured, int children = 2)
    {
        var callbacks = string.Join(",", Enumerable.Range(0, children).Select(index => "Task.Run(() => Child(n))"));
        var root = configured ? "public static async Task Root(int n) { await Task.WhenAll(" + callbacks +
            ").ConfigureAwait(false); }" : "public static Task Root(int n) => Task.WhenAll(" + callbacks + ");";
        return "using System.Threading.Tasks;public static class Subject {" + root + "private static void Child(int n) {} }";
    }

    private static Fixture CreateFixture(bool configured)
    {
        var emitted = Emit(Source(configured));
        var method = emitted.Compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var plan = ResourceForkJoinPlan.TryCreate(method, emitted.Compilation, cancellation);
        Assert.That(plan, Is.Not.Null);
        var capture = CompilerTaskCarrierCollector.CaptureEmittedCarriers(method, plan!, emitted.Assembly,
            WorkerProtocolJson.ComputeSha256([.. emitted.Image]), "root", cancellation);
        Assert.That(capture.Shapes, Is.Not.Null, capture.UnknownReason);
        var shapes = capture.Shapes!;
        var callbacks = CompilerTaskCarrierCollector.ReadCallbackActivations(method, plan!, emitted.Assembly,
            emitted.Image, shapes, "root", cancellation);
        Assert.That(callbacks.Activations, Is.Not.Null, callbacks.UnknownReason);
        if (configured)
        { Assert.That(shapes.StateMachine, Is.Not.Null); }
        return new(emitted.Compilation, method, plan!, emitted.Image, emitted.Assembly, shapes,
            callbacks.Activations!, emitted.Compilation.GetSpecialType(SpecialType.System_Int32).ContainingAssembly.Identity);
    }

    private static Emission Emit(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create(nameof(EmissionBoundaryCompilerCollectorCoverageTests), source);
        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        var image = stream.ToArray();
        var reference = MetadataReference.CreateFromImage(image);
        var inspection = CSharpCompilation.Create("BoundaryInspection", references: compilation.References.Append(reference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithMetadataImportOptions(MetadataImportOptions.All));
        var assembly = inspection.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
        Assert.That(assembly, Is.Not.Null);
        return new(compilation, image, assembly!);
    }

    private sealed record Emission(CSharpCompilation Compilation, byte[] Image, IAssemblySymbol Assembly)
    {
        public CSharpCompilation Compilation { get; init; } = Compilation ?? throw new ArgumentNullException(nameof(Compilation));
        public byte[] Image { get; init; } = Image ?? throw new ArgumentNullException(nameof(Image));
        public IAssemblySymbol Assembly { get; init; } = Assembly ?? throw new ArgumentNullException(nameof(Assembly));
    }

    private sealed record Fixture(CSharpCompilation Compilation, IMethodSymbol Method, ResourceForkJoinPlan Plan,
        byte[] Image, IAssemblySymbol Assembly, CompilerTaskCarrierShapes Shapes,
        CompilerTaskCallbackActivation Activations, AssemblyIdentity Framework)
    {
        public CSharpCompilation Compilation { get; init; } = Compilation ?? throw new ArgumentNullException(nameof(Compilation));
        public IMethodSymbol Method { get; init; } = Method ?? throw new ArgumentNullException(nameof(Method));
        public ResourceForkJoinPlan Plan { get; init; } = Plan ?? throw new ArgumentNullException(nameof(Plan));
        public byte[] Image { get; init; } = Image ?? throw new ArgumentNullException(nameof(Image));
        public IAssemblySymbol Assembly { get; init; } = Assembly ?? throw new ArgumentNullException(nameof(Assembly));
        public CompilerTaskCarrierShapes Shapes { get; init; } = Shapes ?? throw new ArgumentNullException(nameof(Shapes));
        public CompilerTaskCallbackActivation Activations { get; init; } = Activations ?? throw new ArgumentNullException(nameof(Activations));
        public AssemblyIdentity Framework { get; init; } = Framework ?? throw new ArgumentNullException(nameof(Framework));
    }
}
