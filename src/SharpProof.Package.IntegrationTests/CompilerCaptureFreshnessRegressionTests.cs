using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class CompilerCaptureFreshnessRegressionTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string PureSource = "using SharpProof.Attributes; public static class Subject { public static int State; [EnforcePure] public static int Target() => 0; }";
    private const string ImpureSource = "using SharpProof.Attributes; public static class Subject { public static int State; [EnforcePure] public static int Target() { State++; return State; } }";

    [Test]
    public async Task DisabledCollectorCannotPublishPriorProofForEditedImpureSource()
    {
        // Reuse the unchanged package-target consumer harness through reflection
        // so the audit remains a unique probe file and does not edit existing tests.
        var consumerType = typeof(WorkerMsBuildIntegrationTests).GetNestedType("ConsumerProject", BindingFlags.NonPublic)!;
        using var consumer = (IDisposable)consumerType.GetMethod("Create", Members)!.Invoke(null, [PureSource, false])!;
        string PathProperty(string name)
        {
            System.ArgumentNullException.ThrowIfNull(name);
            return (string)consumerType.GetProperty(name, Members)!.GetValue(consumer)!;
        }
        async Task<(int Code, string Output)> Build(ImmutableArray<(string Name, string Value)> properties)
        {
            if (properties.IsDefault)
            {
                throw new System.ArgumentException("Array must be initialized.", nameof(properties));
            }
            var task = (Task)consumerType.GetMethod("BuildAsync", Members)!.Invoke(consumer, [true, properties])!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            return ((int)result.GetType().GetProperty("ExitCode")!.GetValue(result)!,
                (string)result.GetType().GetProperty("Output")!.GetValue(result)!);
        }
        var first = await Build([("SharpProofRunAnalyzersForPackageTests", "true")]);
        await TestContext.Out.WriteLineAsync("FIRST BUILD\n" + first.Output);
        Assert.That(first.Code, Is.Zero, first.Output);
        var firstResult = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(PathProperty("ResultPath")))!;
        Assert.That(firstResult.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        var firstManifest = (await File.ReadAllBytesAsync(PathProperty("CompilerManifestPath"))).ToImmutableArray();
        string Digest(ImmutableArray<byte> bytes)
        {
            if (bytes.IsDefault)
            { throw new ArgumentNullException(nameof(bytes)); }
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.AsSpan()));
        }
        await TestContext.Out.WriteLineAsync("First source SHA256: " + Digest((await File.ReadAllBytesAsync(Path.Combine(PathProperty("Root"), "Subject.cs"))).ToImmutableArray()));
        await TestContext.Out.WriteLineAsync("First manifest SHA256: " + Digest(firstManifest));
        await TestContext.Out.WriteLineAsync("First result SHA256: " + Digest((await File.ReadAllBytesAsync(PathProperty("ResultPath"))).ToImmutableArray()));
        await TestContext.Out.WriteLineAsync("First assembly SHA256: " + Digest((await File.ReadAllBytesAsync(Path.Combine(PathProperty("Root"), "bin", "Release", "net8.0", "Consumer.dll"))).ToImmutableArray()));
        await File.WriteAllTextAsync(Path.Combine(PathProperty("Root"), "Subject.cs"), ImpureSource, new System.Text.UTF8Encoding(false));
        var second = await Build([("SharpProofRunAnalyzersForPackageTests", "true"), ("RunAnalyzersDuringBuild", "false")]);
        await TestContext.Out.WriteLineAsync("SECOND BUILD\n" + second.Output);
        var assemblyPath = Path.Combine(PathProperty("Root"), "obj", "Release", "net8.0", "Consumer.dll");
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("IncrementalPackageAuditRuntime", isCollectible: true);
        try
        {
            using var stream = File.OpenRead(assemblyPath);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            Assert.That(subject.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(1), "Compiled second-build CLR body must be the edited impure source.");
            Assert.That(subject.GetField("State")!.GetValue(null), Is.EqualTo(1));
        }
        finally { runtime.Unload(); }
        var secondResult = File.Exists(PathProperty("ResultPath"))
            ? WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(PathProperty("ResultPath"))) : null;
        await TestContext.Out.WriteLineAsync("Manifest unchanged: " + firstManifest.SequenceEqual((await File.ReadAllBytesAsync(PathProperty("CompilerManifestPath"))).ToImmutableArray()));
        await TestContext.Out.WriteLineAsync("Second outcome: " + secondResult?.ClaimResults.Single().Outcome);
        await TestContext.Out.WriteLineAsync("Second source SHA256: " + Digest((await File.ReadAllBytesAsync(Path.Combine(PathProperty("Root"), "Subject.cs"))).ToImmutableArray()));
        await TestContext.Out.WriteLineAsync("Second manifest SHA256: " + Digest((await File.ReadAllBytesAsync(PathProperty("CompilerManifestPath"))).ToImmutableArray()));
        if (secondResult != null)
        {
            await TestContext.Out.WriteLineAsync("Second result SHA256: " + Digest((await File.ReadAllBytesAsync(PathProperty("ResultPath"))).ToImmutableArray()));
        }
        await TestContext.Out.WriteLineAsync("Second assembly SHA256: " + Digest((await File.ReadAllBytesAsync(assemblyPath)).ToImmutableArray()));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Code, Is.Not.Zero, "Verification must fail closed when the compiler collector is disabled after editing source.");
            Assert.That(secondResult?.ClaimResults.Single().Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), "The edited method writes static state and cannot retain its earlier pure proof.");
        }
    }
}
