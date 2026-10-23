using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;

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
