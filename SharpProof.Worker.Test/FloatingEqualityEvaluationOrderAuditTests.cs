using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class FloatingEqualityEvaluationOrderAuditTests
{
    private static readonly ArgumentKind[] ExplicitArgumentKinds = [ArgumentKind.Explicit, ArgumentKind.Explicit];
    private static readonly BinaryOperatorKind[] FloatingComparisonKinds = [BinaryOperatorKind.Equals, BinaryOperatorKind.NotEquals];

    private const string SourceTemplate = """
        #undef SHARPPROOF_CONTRACTS
        using SharpProof.Attributes;
        public static class Subject {
            private static __VALUE_TYPE__ Tap(__VALUE_TYPE__ value, int marker) => value;
            public static int Target(__VALUE_TYPE__ value) {
                Contract.Ensures(Contract.Result<int>() == 1234);
                int marker = 0;
                _ = (Tap(value, marker = marker * 10 + 1) == Tap(value, marker = marker * 10 + 2))
                 != (Tap(value, marker = marker * 10 + 3) != Tap(value, marker = marker * 10 + 4));
                return marker;
            }
        }
        """ + "\n";

    [TestCase("double")]
    [TestCase("float")]
    public async Task NestedFloatingEqualityPreservesOperandEvaluationOrder(string valueType)
    {
        var source = SourceTemplate.Replace("__VALUE_TYPE__", valueType, StringComparison.Ordinal);
        var compilation = TestCompilation.Create("FloatingEqualityEvaluationOrder_" + valueType, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var expectedType = valueType == "double" ? SpecialType.System_Double : SpecialType.System_Single;
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(syntax => syntax.Expression is IdentifierNameSyntax { Identifier.ValueText: "Tap" }).ToArray();
        Assert.That(calls, Has.Length.EqualTo(4));
        foreach (var syntax in calls)
        {
            var call = (IInvocationOperation)model.GetOperation(syntax)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingAssembly, compilation.Assembly), Is.True);
            Assert.That(call.TargetMethod.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            Assert.That(call.TargetMethod.IsStatic, Is.True);
            Assert.That(call.TargetMethod.ReturnType.SpecialType, Is.EqualTo(expectedType));
            Assert.That(call.TargetMethod.Parameters.Select(parameter => parameter.Type.SpecialType),
                Is.EqualTo(new[] { expectedType, SpecialType.System_Int32 }));
            Assert.That(call.Arguments.Select(argument => argument.ArgumentKind),
                Is.EqualTo(ExplicitArgumentKinds));
        }
        var comparisons = root.DescendantNodes().OfType<BinaryExpressionSyntax>()
            .Select(syntax => model.GetOperation(syntax)).OfType<IBinaryOperation>()
            .Where(operation => operation.LeftOperand.Type?.SpecialType == expectedType &&
                operation.RightOperand.Type?.SpecialType == expectedType).ToArray();
        Assert.That(comparisons.Select(operation => operation.OperatorKind),
            Is.EquivalentTo(FloatingComparisonKinds));
        Assert.That(comparisons.All(operation => operation.OperatorMethod == null && !operation.IsLifted), Is.True);

        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "floating-equality-evaluation-order-audit",
            "runtime", valueType + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, "Subject.cs"), source);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "oracle.dll"), imageBytes);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("FloatingEqualityEvaluationOrder_" + valueType, isCollectible: true);
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(method.ReturnType, Is.EqualTo(typeof(int)));
            Assert.That(method.GetParameters().Single().ParameterType,
                Is.EqualTo(valueType == "double" ? typeof(double) : typeof(float)));
            object[] witnesses = valueType == "double" ? [double.NaN, 1.25d] : [float.NaN, 1.25f];
            var observations = new List<string>();
            foreach (var witness in witnesses)
            {
                var result = (int)method.Invoke(null, [witness])!;
                var observation = $"type={valueType}; witness={witness}; CLR={result}; " +
                    $"IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}; " +
                    $"sourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(source))}; " +
                    $"PE_SHA256={WorkerProtocolJson.ComputeSha256(imageBytes)}";
                observations.Add(observation);
                Assert.That(result, Is.EqualTo(1234), observation);
            }
            await File.WriteAllTextAsync(Path.Combine(evidence, "clr.txt"), string.Join("\n", observations) + "\n");
        }
        finally
        {
            runtime.Unload();
        }

        // The proof artifact and emitted CLR oracle use this same compilation.
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        var callableId = artifact.Manifest.Claims.Single().CallableId;
        var captured = artifact.Callables.Single(callable => callable.CallableId == callableId);
        Assert.That(captured.Total, Is.Not.Null, "Expected retained compiler-captured Total for the floating-parameter method.");
        Assert.That(captured.Total!.IsBodyAbstraction, Is.False);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        var nativeObservation = $"type={valueType}; integerPostcondition=1234; native={claim.Outcome}; reason={claim.Reason}; " +
            $"vacuity={claim.Vacuity}; evidence={evidence}";
        await File.WriteAllTextAsync(Path.Combine(evidence, "observation.txt"), nativeObservation + "\n");
        TestContext.WriteLine(nativeObservation);
        Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
            "The integer marker postcondition is independent of the floating comparison result. " + claim.Reason);
    }
}
