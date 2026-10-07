using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CallableFunctionPointerWrapperIdentityAuditTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DistinctFunctionPointerOverloadsRetainDistinctManifestIdentities(bool array, bool generic)
    {
        var argument = generic ? "T" : "int";
        var genericParameters = generic ? "<T>" : string.Empty;
        var constraints = generic ? "where T : unmanaged" : string.Empty;
        var wrapper = array ? "[]" : string.Empty;
        var source = $$"""
            using SharpProof.Attributes;
            public static unsafe class Subject {
                public static int Target{{genericParameters}}(delegate*<{{argument}},int>{{wrapper}} pointer) {{constraints}} {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
                public static int Target{{genericParameters}}(delegate*<{{argument}},long>{{wrapper}} pointer) {{constraints}} {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
            }
            """;
        var compilation = TestCompilation.Create("CallableFunctionPointerIdentityAudit",
            [("Subject.cs", source)], allowUnsafe: true);
        TestCompilation.AssertNoErrors(compilation);
        var methods = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target")
            .OfType<IMethodSymbol>().ToArray();
        Assert.That(methods, Has.Length.EqualTo(2));
        foreach (var method in methods)
        {
            TestContext.Out.WriteLine($"array={array}; generic={generic}; parameter={method.Parameters[0].Type.ToDisplayString()}; referenceId={DocumentationCommentId.CreateReferenceId(method.Parameters[0].Type)}; declarationId={DocumentationCommentId.CreateDeclarationId(method)}; callableId={SemanticClaimIdentity.CreateCallableId(method)}");
        }

        var result = new ClaimManifestBuilder(compilation).Build();
        TestContext.Out.WriteLine("manifestCallableIds=" + string.Join(",", result.Manifest.Callables.Select(static entry => entry.CallableId)));
        TestContext.Out.WriteLine("manifestClaimIds=" + string.Join(",", result.Manifest.Claims.Select(static entry => entry.ClaimId)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(methods.Select(SemanticClaimIdentity.CreateCallableId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(result.Manifest.Callables, Has.Length.EqualTo(2));
            Assert.That(result.Manifest.Callables.Select(static entry => entry.CallableId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(result.Manifest.Claims, Has.Length.EqualTo(2));
            Assert.That(result.Manifest.Claims.Select(static entry => entry.ClaimId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(WorkerProtocolJson.ValidateManifest(result.Manifest).IsValid, Is.True);
        }
    }
}
