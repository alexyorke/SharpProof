using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class ReferenceNullPatternFlowTests
{
    [Test]
    public async Task NullPatternGuardsStringLength()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[DoesNotThrow] public static int Target(string? s) { return s is null ? 0 : s.Length; } }";
        Assert.That(Execute(source, null), Is.Zero);
        Assert.That(Execute(source, ""), Is.Zero);
        Assert.That(Execute(source, "abc"), Is.EqualTo(3));

        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("return s is not null ? s.Length : 0;", WorkerClaimOutcome.Proven, false)]
    [TestCase("if (s is null) return 0; return s.Length;", WorkerClaimOutcome.Proven, false)]
    [TestCase("return s switch { null => 0, _ => s.Length };", WorkerClaimOutcome.Proven, false)]
    [TestCase("return s is null ? s!.Length : 0;", WorkerClaimOutcome.Refuted, true)]
    [TestCase("return s is not null ? 0 : s!.Length;", WorkerClaimOutcome.Refuted, true)]
    [TestCase("return s switch { null => s!.Length, _ => 0 };", WorkerClaimOutcome.Refuted, true)]
    [TestCase("return s is { Length: > 0 } ? s.Length : 0;", WorkerClaimOutcome.Unknown, false)]
    [TestCase("return s is \"x\" ? 0 : s!.Length;", WorkerClaimOutcome.Unknown, true)]
    public async Task RelatedReferencePatterns(string body, WorkerClaimOutcome expected, bool throwsForNull)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            "[DoesNotThrow] public static int Target(string? s) { " + body + " } }";
        if (throwsForNull)
        {
            Assert.Throws<TargetInvocationException>((Action)(() => Execute(source, null)));
        }
        else
        {
            Assert.DoesNotThrow((Action)(() => Execute(source, null)));
        }
        Assert.DoesNotThrow((Action)(() => Execute(source, "abc")));

        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected), body);
    }

    private static int Execute(string source, string? input)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("NullPatternOracle" + Guid.NewGuid().ToString("N"),
            [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var method = Assembly.Load(stream.ToArray()).GetType("C")!.GetMethod("Target")!;
        return (int)method.Invoke(null, [input])!;
    }
}
