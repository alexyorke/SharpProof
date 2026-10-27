using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ReachableGraphBoundaryControlTests
{
    private static CompilerManifestArtifact CreateArtifact()
    {
        return CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; static class Subject { [ZeroAllocations] public static int Target(int value) => Shared(value); static int Shared(int value) => value + 1; }");
    }

    [Test]
    public void BodyPublicationRejectsAnUnregisteredCapturedBody()
    {
        var body = CreateArtifact().ReachableSource!.Bodies[0];
        var collector = typeof(CompilerReachableSourceCollector);
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static;
        var update = collector.GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Prepend(collector)
            .SelectMany(type =>
            {
                ArgumentNullException.ThrowIfNull(type);
                return type.GetMethods(members);
            })
            .Single(method =>
            {
                ArgumentNullException.ThrowIfNull(method);
                return method.Name.Contains("g__UpdateRegisteredBody|", StringComparison.Ordinal);
            });
        var arguments = System.Collections.Immutable.ImmutableArray.CreateBuilder<object?>();
        foreach (var parameter in update.GetParameters())
        {
            var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
            arguments.Add(type == typeof(CompilerSourceBodyArtifact) ? body : EmptyRegistrationFrame(type));
        }
        var target = update.IsStatic ? null : EmptyRegistrationFrame(update.DeclaringType!);
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() =>
            update.Invoke(target, arguments.ToImmutable().ToArray())));
        Assert.That(failure!.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(failure.InnerException!.Message,
            Is.EqualTo("A collected body must be registered before publication."));

        static object EmptyRegistrationFrame(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            var frame = Activator.CreateInstance(type, nonPublic: true)!;
            var bodies = type.GetField("bodies", members);
            if (bodies != null)
            {
                bodies.SetValue(frame, System.Collections.Immutable.ImmutableDictionary
                    .CreateBuilder<string, CompilerSourceBodyArtifact>(StringComparer.Ordinal));
            }
            return frame;
        }
    }

    [Test]
    public void ValidReachableBodiesRetainCanonicalPayload()
    {
        var artifact = CreateArtifact();
        var payload = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = CompilerManifestArtifactJson.Deserialize(payload, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(decoded.ReachableSource!.Bodies, Has.Length.EqualTo(2));
        Assert.That(CompilerManifestArtifactJson.Serialize(decoded, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(payload));
    }

    [Test]
    public void CancellationPrecedesMalformedGraphRejection()
    {
        var artifact = CreateArtifact();
        { artifact = artifact with { ReachableSource = artifact.ReachableSource! with { Bodies = artifact.ReachableSource!.Bodies.ReplaceReference(artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)), artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)) with { Graph = artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)).Graph! with { Types = default } }) } }; }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => CompilerManifestArtifactJson.Validate(artifact, cancellation.Token)));
        Assert.Throws<OperationCanceledException>(new Action(() => CompilerManifestArtifactJson.Serialize(artifact, cancellation.Token)));
    }

    [Test]
    public void GraphRejectionRetainsOriginalDecodeCause()
    {
        var artifact = CreateArtifact();
        { artifact = artifact with { ReachableSource = artifact.ReachableSource! with { Bodies = artifact.ReachableSource!.Bodies.ReplaceReference(artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)), artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)) with { Graph = artifact.ReachableSource!.Bodies.First(body => body is null ? throw new System.ArgumentNullException(nameof(body)) : (body.Graph != null)).Graph! with { Types = default } }) } }; }
        var exception = Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.Validate(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidDataException>());
    }
}
