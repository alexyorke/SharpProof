using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskCarrierOwnershipGapTests
{
    [TestCase(OptimizationLevel.Debug)]
    [TestCase(OptimizationLevel.Release)]
    public void FrameworkAsyncMetadataKeepsTheOwnedStateMachineAvailable(OptimizationLevel optimization)
    {
        var (compilation, method) = Compile(optimization, string.Empty);
        var capture = new CompilerTaskCarrierCollector(compilation).Capture(method, "root",
            TestContext.CurrentContext.CancellationToken);

        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Shapes, Is.Not.Null);
        Assert.That(capture.Shapes!.RootCallableId, Is.EqualTo("root"));
        Assert.That(capture.Shapes.MatchedCallbacks, Is.EqualTo(2));
        Assert.That(capture.Shapes.StateMachine, Is.Not.Null);
        Assert.That(capture.Shapes.StateMachine!.MetadataName, Does.StartWith("Subjects.Owner+<Root>d__"));
    }

    [TestCase(OptimizationLevel.Debug, "AsyncStateMachineAttribute")]
    [TestCase(OptimizationLevel.Release, "AsyncStateMachineAttribute")]
    [TestCase(OptimizationLevel.Debug, "CompilerGeneratedAttribute")]
    [TestCase(OptimizationLevel.Release, "CompilerGeneratedAttribute")]
    public void SourceShadowOfAsyncMetadataCannotSupplyStateMachineOwnership(
        OptimizationLevel optimization, string attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        var declaration = attribute switch
        {
            "AsyncStateMachineAttribute" => "public sealed class AsyncStateMachineAttribute : System.Attribute " +
                "{ public AsyncStateMachineAttribute(System.Type stateMachineType) {} }",
            "CompilerGeneratedAttribute" => "public sealed class CompilerGeneratedAttribute : System.Attribute {}",
            _ => throw new ArgumentOutOfRangeException(nameof(attribute))
        };
        var (compilation, method) = Compile(optimization,
            "namespace System.Runtime.CompilerServices {" + declaration + "}");
        var shadow = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices." + attribute);
        Assert.That(shadow, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(shadow!.ContainingAssembly, compilation.Assembly), Is.True,
            "The negative control must bind a real source attribute from the consumer assembly.");

        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var capture = collector.Capture(method, "root", cancellation);
        Assert.That(capture.Shapes, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierStateMachineUnavailable"));

        var callbacks = collector.CaptureCallbackActivations(method, "root", cancellation);
        Assert.That(callbacks.Activations, Is.Null);
        Assert.That(callbacks.UnknownReason, Is.EqualTo(capture.UnknownReason));
        var bodies = collector.CaptureSourceBodies(method, "root", cancellation);
        Assert.That(bodies.Inventory, Is.Null);
        Assert.That(bodies.UnknownReason, Is.EqualTo(capture.UnknownReason));
        var faultBodies = collector.CaptureEmittedFaultBodies(method, "root", cancellation);
        Assert.That(faultBodies.Inventory, Is.Null);
        Assert.That(faultBodies.UnknownReason, Is.EqualTo(capture.UnknownReason));
        var boundary = collector.CaptureExceptionBoundary(method, "root", cancellation);
        Assert.That(boundary.Boundary, Is.Null);
        Assert.That(boundary.UnknownReason, Is.EqualTo(capture.UnknownReason));
    }

    [TestCase("none")]
    [TestCase("distribution")]
    [TestCase("false-upper")]
    [TestCase("true-upper")]
    [TestCase("expected-upper")]
    public void ExpectedProjectionRejectsIncompleteTypedChecksWithoutPartialEvidence(string missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                [ResourceUpperBound(ResourceMetric.AllocatedElements, 7)]
                public static void Target([ResourceBooleanInput(1, 3)] bool selected) {
                    if (selected) {var values = new int[7];}
                    else {var values = new int[2];}
                }
            }
            """;
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellation);
        var target = preparations.Single();
        Assert.That(target.ResourceProgram, Is.Not.Null);
        Assert.That(target.Total, Is.Not.Null);
        var complete = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, ResourceCostMetric.AllocatedElements,
            target.Entry.CallableId, target.Total!, null, cancellation);
        Assert.That(complete.UnknownReason, Is.Null);
        Assert.That(complete.Distribution, Is.Not.Null);
        Assert.That(complete.FalseUpper!.ToDisplayString(), Is.EqualTo("2"));
        Assert.That(complete.TrueUpper!.ToDisplayString(), Is.EqualTo("7"));
        Assert.That(complete.ExpectedUpper!.ToDisplayString(), Is.EqualTo("4"));

        // Project accepts nullable typed checks. The current production caller passes Verify directly,
        // which returns complete costs or a reason; these record copies exercise Project's component boundary.
        var input = missing switch
        {
            "none" => complete,
            "distribution" => complete with { Distribution = null },
            "false-upper" => complete with { FalseUpper = null },
            "true-upper" => complete with { TrueUpper = null },
            "expected-upper" => complete with { ExpectedUpper = null },
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        };
        var projected = NativeResourceExpectedVerifier.Project(input);
        if (missing == "none")
        {
            Assert.That(projected.UnknownReason, Is.Null);
            Assert.That(projected.InputOrdinal, Is.Zero);
            Assert.That(projected.InputName, Is.EqualTo("selected"));
            Assert.That(projected.TrueWeight, Is.EqualTo(1));
            Assert.That(projected.FalseWeight, Is.EqualTo(3));
            Assert.That(projected.FalseUpperBound, Is.EqualTo("2"));
            Assert.That(projected.TrueUpperBound, Is.EqualTo("7"));
            Assert.That(projected.UpperBound, Is.EqualTo("4"));
            return;
        }
        Assert.That(projected.UnknownReason, Is.EqualTo("ResourceExpectedEvidenceIncomplete"));
        Assert.That(projected.InputOrdinal, Is.Null);
        Assert.That(projected.InputName, Is.Null);
        Assert.That(projected.TrueWeight, Is.Null);
        Assert.That(projected.FalseWeight, Is.Null);
        Assert.That(projected.FalseUpperBound, Is.Null);
        Assert.That(projected.TrueUpperBound, Is.Null);
        Assert.That(projected.UpperBound, Is.Null);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method) Compile(
        OptimizationLevel optimization, string additionalDeclarations)
    {
        ArgumentNullException.ThrowIfNull(additionalDeclarations);
        const string source = """
            #pragma warning disable CS0436
            using System.Threading.Tasks;
            namespace Subjects {
                public static class Owner {
                    public static async Task Root(int n) =>
                        await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(n))).ConfigureAwait(false);
                    private static void Child(int n) {}
                }
            }
            """;
        var initial = TestCompilation.Create("CarrierAsyncOwnership",
            [("Subject.cs", source + "\n" + additionalDeclarations)]);
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("Subjects.Owner")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var plan = ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan, Is.Not.Null, "Source admission must succeed before emitted ownership is checked.");
        Assert.That(plan!.ConfiguredAwait, Is.True);
        Assert.That(plan.Children, Has.Length.EqualTo(2));
        return (compilation, method);
    }
}
