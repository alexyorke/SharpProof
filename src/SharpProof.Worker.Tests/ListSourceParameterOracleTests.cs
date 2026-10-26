using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class ListSourceParameterOracleTests
{
    [TestCase("17,23", 0, 1)]
    [TestCase("17,23", 0, 0)]
    [TestCase("17", 0, 1)]
    [TestCase("", -1, int.MaxValue)]
    [TestCase("17,23", -1, 1)]
    [TestCase("17,23", 2, 0)]
    public void OriginalCorpusSwapAgreesWithRuntimeThroughAListReceiver(string items, int first, int second)
    {
        ArgumentNullException.ThrowIfNull(items);
        var root = TestRepository.FindRoot();
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/SharpProof.Gates/Corpus/oss-methods.json")));
        var method = corpus.RootElement.GetProperty("methods").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "OSS0002");
        var file = corpus.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("path").GetString() == method.GetProperty("path").GetString());
        var lines = file.GetProperty("content").GetString()!.Split('\n').ToImmutableArray();
        var start = method.GetProperty("startLine").GetInt32();
        var end = method.GetProperty("endLine").GetInt32();
        var swap = string.Join("\n", lines.Skip(start - 1).Take(end - start + 1));
        var source = "using System.Collections.Generic; using SharpProof.Attributes; public static class Probe { public static int Target() { " +
            "Contract.Ensures(Contract.Result<int>() >= 0); var data = new List<int>() { " + items + " }; data.Swap(" + first + "," + second + "); " +
            "return data.Count * 10000 + (data.Count > 0 ? data[0] * 100 : 0) + (data.Count > 1 ? data[1] : 0); } " + swap + " }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        var program = preparations.Single().Total!.Program;
        var factory = program.Factory;
        var choices = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var interpreted = new IrProgramInterpreter(factory).Execute(program, null, 10000, choices);
        Assert.That(interpreted.ConsumedApproximation, Is.True, "Normal growth and ancillary fault choices are differential evidence, not native replay authority.");
        var compilation = TestCompilation.CreateExact("ListSourceParameterOracle", [("Probe.cs", source)]);
        var syntax = compilation.SyntaxTrees.Single();
        compilation = compilation.ReplaceSyntaxTree(syntax,
            syntax.WithRootAndOptions(syntax.GetRoot(), ((CSharpParseOptions)syntax.Options).WithPreprocessorSymbols(ImmutableArray<string>.Empty)));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var runtime = new AssemblyLoadContext("ListSourceParameterOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            object? actual = null;
            Exception? fault = null;
            try
            { actual = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null); }
            catch (TargetInvocationException exception)
            { fault = exception.InnerException; }
            if (fault == null)
            {
                Assert.That(interpreted.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), interpreted.Unsupported?.Detail);
                Assert.That(interpreted.ReturnValue!.Integer, Is.EqualTo(actual));
            }
            else
            {
                Assert.That(fault, Is.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(interpreted.Status, Is.EqualTo(IrProgramExecutionStatus.Exception), interpreted.Unsupported?.Detail);
                Assert.That(interpreted.Exception!.Kind, Is.EqualTo(IrExceptionKind.ArgumentOutOfRange));
            }
        }
        finally
        { runtime.Unload(); }
    }
}
