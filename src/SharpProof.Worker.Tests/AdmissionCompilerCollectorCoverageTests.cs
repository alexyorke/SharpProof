using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AdmissionCompilerCollectorCoverageTests
{
    [Test]
    public void ResourceGetterRetainsItsTotalBodyWithoutInventingAMethodResourceProgram()
    {
        var compilation = Compile("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Value {
                    [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 10)]
                    get { return 1; }
                }
            }
            """);
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single(
            candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Method.MetadataName == "get_Value");

        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(
            target, TestContext.CurrentContext.CancellationToken);

        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.ResourceProgram, Is.Null);
        Assert.That(preparation.ResourceUnknownReason, Is.EqualTo("UnsupportedResourceCallable"));
    }

    [Test]
    public void ForkJoinLoweringRejectsAnEmptyCallableIdentityBeforePublishingChildren()
    {
        var compilation = Compile(RootSource);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();

        var error = Assert.Throws<ArgumentException>((Action)(() => CompilerForkJoinResourceLowering.Lower(
            method, compilation, " \t", TestContext.CurrentContext.CancellationToken)));

        Assert.That(error!.ParamName, Is.EqualTo("callableId"));
    }

    [Test]
    public void ForkJoinChildrenCannotHideEffectsInAContainingTypeInitializer()
    {
        var compilation = Compile("""
            using System;
            using System.Threading.Tasks;
            public static class Subject {
                public static Task Root(int n) => Task.WhenAll(
                    Task.Run(() => Outer.Child.Run(n)), Task.Run(() => Outer.Child.Run(3)));
            }
            public static class Outer {
                private static int State = Environment.TickCount;
                public static class Child { public static void Run(int n) {} }
            }
            """);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();

        var result = CompilerForkJoinResourceLowering.Lower(
            method, compilation, "root", TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Projection, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ForkJoinEntryInitializationUnknown"));
    }

    [Test]
    public void BodylessSourceMethodCannotSupplyAForkJoinChildControlFlowGraph()
    {
        var compilation = CSharpCompilation.Create(nameof(AdmissionCompilerCollectorCoverageTests),
            [CSharpSyntaxTree.ParseText("public static class Subject { public static void Child(int n); }")],
            TestMetadataReferences.WithSharpProof, TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Select(diagnostic => diagnostic is null
            ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Id), Does.Contain("CS0501"));
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Child").OfType<IMethodSymbol>().Single();
        var continuationRan = false;

        var result = CompilerForkJoinResourceLowering.ReadChildBody(
            compilation, method, body =>
            {
                ArgumentNullException.ThrowIfNull(body);
                continuationRan = true;
                return null;
            }, TestContext.CurrentContext.CancellationToken);

        Assert.That(continuationRan, Is.False);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Projection, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ForkJoinChildBodyUnavailable"));
    }

    [Test]
    public void ChildControlFlowContextKeepsTheOwningDeclarationAndSemanticModel()
    {
        var compilation = Compile(RootSource);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Child").OfType<IMethodSymbol>().Single();
        CompilerForkJoinResourceLowering.ChildBody? observed = null;

        var result = CompilerForkJoinResourceLowering.ReadChildBody(
            compilation, method, body =>
            {
                ArgumentNullException.ThrowIfNull(body);
                observed = body;
                return null;
            }, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.Null);
        Assert.That(observed, Is.Not.Null);
        Assert.That(observed!.Model.SyntaxTree, Is.SameAs(observed.Declaration.SyntaxTree));
        Assert.That(observed.Graph.OriginalOperation.Syntax, Is.SameAs(observed.Declaration));
    }

    [TestCase("compilation")]
    [TestCase("child")]
    [TestCase("continuation")]
    public void ChildControlFlowBoundaryRejectsMissingContext(string missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        var compilation = Compile(RootSource);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Child").OfType<IMethodSymbol>().Single();
        Func<CompilerForkJoinResourceLowering.ChildBody, CompilerForkJoinProjectionResult?> continuation = body =>
        {
            ArgumentNullException.ThrowIfNull(body);
            return null;
        };

        var error = Assert.Throws<ArgumentNullException>((Action)(() => CompilerForkJoinResourceLowering.ReadChildBody(
            missing == "compilation" ? null! : compilation, missing == "child" ? null! : method,
            missing == "continuation" ? null! : continuation, TestContext.CurrentContext.CancellationToken)));

        Assert.That(error!.ParamName, Is.EqualTo(missing));
    }

    [Test]
    public void ChildControlFlowBoundaryHonorsCancellationBeforeReadingSyntax()
    {
        var compilation = Compile(RootSource);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Child").OfType<IMethodSymbol>().Single();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        Assert.Throws<OperationCanceledException>((Action)(() => CompilerForkJoinResourceLowering.ReadChildBody(
            compilation, method, body =>
            {
                ArgumentNullException.ThrowIfNull(body);
                return null;
            }, canceled.Token)));
    }

    [Test]
    public void MissingResourceBodyKeepsTheDeclaredClaimAndAnExplicitUnknownReason()
    {
        var compilation = Compile("""
            using SharpProof.Attributes;
            public static class Subject {
                [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 10)]
                public static void Target() {}
            }
            """);
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single(
            candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Method.Name == "Target");
        var attach = typeof(CompilerManifestArtifactProducer).GetMethod(
            "AttachEffectEvidence", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(attach, Is.Not.Null);

        var artifact = (CompilerCallableArtifact)attach!.Invoke(null,
            [new CompilerCallableArtifact { CallableId = target.Entry.CallableId }, target])!;

        Assert.That(artifact.ResourceProgram, Is.Null);
        Assert.That(artifact.ResourceUnknownReason, Is.EqualTo("CompilationOrBodyUnavailable"));
        Assert.That(artifact.EffectClaims, Is.EqualTo(target.EffectClaims.Select(claim => claim is null
            ? throw new ArgumentNullException(nameof(claim)) : claim.Evidence)));
        Assert.That(artifact.EffectClaims, Has.Length.EqualTo(1));
        Assert.That(artifact.CallableId, Is.EqualTo(target.Entry.CallableId));
    }

    [Test]
    public void ForeignAsyncStateMachineAttributeCannotSupplyOwnedCarrierEvidence()
    {
        var compilation = Compile("""
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            public static class Subject {
                [AsyncStateMachine(typeof(object))]
                public static async Task Root(int n) {
                    await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(false);
                }
                private static void Child(int n) {}
            }
            """);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();

        var capture = new CompilerTaskCarrierCollector(compilation).Capture(
            method, "root", TestContext.CurrentContext.CancellationToken);

        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierStateMachineUnavailable"));
    }

    [Test]
    public void NetModuleEmissionCannotBecomeAssemblyOwnedCarrierEvidence()
    {
        var compilation = Compile(RootSource);
        compilation = compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.NetModule));
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();

        var capture = new CompilerTaskCarrierCollector(compilation).Capture(
            method, "root", TestContext.CurrentContext.CancellationToken);

        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierEmissionMetadataUnavailable"));
    }

    [Test]
    public void SerializedAttributePayloadCannotBypassTheImageBudgetWithSmallSource()
    {
        // Each distinct prefix prevents metadata blob deduplication. The input
        // text remains small while real Roslyn constant folding creates a PE
        // larger than the independently enforced serialized-image budget.
        var attributes = string.Concat(Enumerable.Range(0, 150).Select(index =>
            "[Payload(\"" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\" + Noise.Value)]"));
        var source = RootSource + """
            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class PayloadAttribute : System.Attribute {
                public PayloadAttribute(string value) {}
            }
            """ + attributes + "public static class Noise { public const string Value = \"" +
            new string('a', 128 * 1024) + "\"; }";
        Assert.That(source.Length, Is.LessThan(1024 * 1024));
        var compilation = Compile(source);
        using (var unbounded = new MemoryStream())
        {
            var emitted = compilation.Emit(unbounded, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            Assert.That(unbounded.Length, Is.GreaterThan(CompilerTaskCarrierCollector.MaximumImageBytes),
                "The same actual emitted compilation must cross the serialized-image limit.");
            var actualPe = unbounded.ToArray();
            var streamType = typeof(CompilerTaskCarrierCollector).GetNestedType("CarrierImageStream", BindingFlags.NonPublic);
            Assert.That(streamType, Is.Not.Null);
            using var bounded = (Stream)Activator.CreateInstance(streamType!, nonPublic: true)!;
            bounded.WriteByte(42);

            Assert.Throws<InvalidDataException>((Action)(() => bounded.Write(actualPe, 0, actualPe.Length)));

            Assert.That(bounded.Length, Is.EqualTo(1), "Rejecting the actual PE must preserve the existing prefix.");
            bounded.Position = 0;
            Assert.That(bounded.ReadByte(), Is.EqualTo(42));
        }
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single();

        var capture = new CompilerTaskCarrierCollector(compilation).Capture(
            method, "root", TestContext.CurrentContext.CancellationToken);

        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierEmissionFailed"));
    }

    private const string RootSource = """
        using System.Threading.Tasks;
        public static class Subject {
            public static Task Root(int n) => Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3)));
            private static void Child(int n) {}
        }
        """;

    private static CSharpCompilation Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create(nameof(AdmissionCompilerCollectorCoverageTests), source);
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }
}
