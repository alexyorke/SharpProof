using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CallableFunctionPointerWrapperIdentityControlTests
{
    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    [TestCase(4, false)]
    [TestCase(4, true)]
    public void WrappedFunctionPointerOverloadsRetainDistinctPreparedIdentities(
        int wrapperKind, bool generic)
    {
        var wrapper = wrapperKind switch
        {
            0 => "$*",
            1 => "$[][]",
            2 => "$[,]",
            3 => "System.Collections.Generic.List<$[]>",
            4 => "Box<$[]>.Inner",
            _ => throw new ArgumentOutOfRangeException(nameof(wrapperKind))
        };
        var argument = generic ? "T" : "int";
        var firstType = wrapper.Replace("$", $"delegate*<{argument},int>", StringComparison.Ordinal);
        var secondType = wrapper.Replace("$", $"delegate*<{argument},long>", StringComparison.Ordinal);
        var genericParameters = generic ? "<T>" : string.Empty;
        var constraints = generic ? "where T : unmanaged" : string.Empty;
        var compilation = TestCompilation.Create("CallableFunctionPointerWrapperControls", [("Subject.cs", $$"""
            using SharpProof.Attributes;
            public sealed class Box<TItem> { public sealed class Inner { } }
            public static unsafe class Subject {
                public static int Target{{genericParameters}}({{firstType}} pointer) {{constraints}} {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
                public static int Target{{genericParameters}}({{secondType}} pointer) {{constraints}} {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
            }
            """)], allowUnsafe: true);
        TestCompilation.AssertNoErrors(compilation);
        var methods = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target")
            .OfType<IMethodSymbol>().ToArray();
        foreach (var method in methods)
        {
            TestContext.Out.WriteLine($"wrapper={wrapper}; generic={generic}; parameter={method.Parameters[0].Type}; referenceId={DocumentationCommentId.CreateReferenceId(method.Parameters[0].Type)}; callableId={SemanticClaimIdentity.CreateCallableId(method)}");
        }
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(methods, Has.Length.EqualTo(2));
            Assert.That(methods.Select(SemanticClaimIdentity.CreateCallableId), Is.Unique);
            Assert.That(discovery.Manifest.Callables, Has.Length.EqualTo(2));
            Assert.That(discovery.Manifest.Callables.Select(static entry => entry.CallableId), Is.Unique);
            Assert.That(discovery.Manifest.Claims, Has.Length.EqualTo(2));
            Assert.That(discovery.Manifest.Claims.Select(static entry => entry.ClaimId), Is.Unique);
            Assert.That(WorkerProtocolJson.ValidateManifest(discovery.Manifest).IsValid, Is.True);
        }
        var expectedTotalIds = wrapperKind is 3 or 4
            ? discovery.Manifest.Callables.Select(static entry => entry.CallableId).ToHashSet(StringComparer.Ordinal)
            : null;
        AssertUnsupportedArtifactRoundTrip(compilation, discovery, expectedTotalIds);
    }

    [TestCase("int[]", "M:Subject.Target(System.Int32[])~System.Int32",
        "spc1:1f87a3ea0bf1b071338f419b6ee4d8c9db300e2ceab37da108efc81da309c048")]
    [TestCase("int[,]", "M:Subject.Target(System.Int32[,])~System.Int32",
        "spc1:539160bd776ac508ddbb66f34a41fdbee210ff26559b3242018c8ae1a8eda2d1")]
    [TestCase("int*", "M:Subject.Target(System.Int32*)~System.Int32",
        "spc1:8e1f897a00f7e5fb2b296be0c9b7c2cb7ad281058e6c20fc3550a7ec66b49883")]
    [TestCase("System.Collections.Generic.List<int[]>", "M:Subject.Target(System.Collections.Generic.List{System.Int32[]})~System.Int32",
        "spc1:694fcf28e9420f31f457fba0fc73ee902232f022424f69aecfd688fc1f0453f8")]
    public void OrdinaryWrapperIdentitiesRemainStable(
        string parameterType, string expectedCallableId, string expectedClaimId)
    {
        var compilation = TestCompilation.Create("CallableOrdinaryWrapperStability", [("Subject.cs", $$"""
            using SharpProof.Attributes;
            public sealed class Box<TItem> { public sealed class Inner { } }
            public static unsafe class Subject {
                public static int Target({{parameterType}} pointer) {
                    Contract.Ensures(pointer != null);
                    return 1;
                }
            }
            """)], allowUnsafe: true).WithAssemblyName("CallableOrdinaryWrapperStability");
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Manifest.Callables.Single().CallableId, Is.EqualTo(expectedCallableId));
        Assert.That(discovery.Manifest.Claims.Single().ClaimId, Is.EqualTo(expectedClaimId));
        Assert.That(WorkerProtocolJson.ValidateManifest(discovery.Manifest).IsValid, Is.True);
        TestContext.Out.WriteLine($"ordinary={parameterType}; callableId={discovery.Manifest.Callables.Single().CallableId}; claimId={discovery.Manifest.Claims.Single().ClaimId}");
    }

    [TestCase(false, "spm1:b1bcdfb6f2bad20b9f3591b3dbe3a30affcd7fe65a745ad526bd7df1c2dc90c4")]
    [TestCase(true, "spm1:1c28b710fd5b3ca4b42b4cd144dcb16c50ddd295b47f39487905b9cf1963a057")]
    public void DirectFunctionPointerIdentitiesRemainStable(bool generic, string expectedCallableId)
    {
        var argument = generic ? "T" : "int";
        var genericParameters = generic ? "<T>" : string.Empty;
        var constraints = generic ? "where T : unmanaged" : string.Empty;
        var compilation = TestCompilation.Create("CallableDirectPointerStability", [("Subject.cs", $$"""
            using SharpProof.Attributes;
            public sealed class Box<TItem> { public sealed class Inner { } }
            public static unsafe class Subject {
                public static int Target{{genericParameters}}(delegate*<{{argument}},int> pointer) {{constraints}} {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
            }
            """)], allowUnsafe: true);
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Manifest.Callables, Has.Length.EqualTo(1));
        Assert.That(discovery.Manifest.Callables.Single().CallableId, Is.EqualTo(expectedCallableId));
        Assert.That(WorkerProtocolJson.ValidateManifest(discovery.Manifest).IsValid, Is.True);
        TestContext.Out.WriteLine($"directGeneric={generic}; callableId={discovery.Manifest.Callables.Single().CallableId}");
        AssertUnsupportedArtifactRoundTrip(compilation, discovery);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DifferentWrapperShapesRetainDistinctIdentities(bool generic)
    {
        var argument = generic ? "T" : "int";
        var pointer = $"delegate*<{argument},int>";
        string[] types = [pointer, pointer + "*", pointer + "[]", pointer + "[][]", pointer + "[,]",
            $"System.Collections.Generic.List<{pointer}[]>", $"System.Collections.Generic.LinkedList<{pointer}[]>",
            $"Box<{pointer}[]>.Inner"];
        var genericParameters = generic ? "<T>" : string.Empty;
        var constraints = generic ? "where T : unmanaged" : string.Empty;
        var declarations = string.Join(Environment.NewLine, types.Select(type => $$"""
            public static int Target{{genericParameters}}({{type}} pointer) {{constraints}} {
                Contract.Ensures(Contract.Result<int>() == 1);
                return 1;
            }
            """));
        var compilation = TestCompilation.Create("CallablePointerWrapperShapeControls", [("Subject.cs", $$"""
            using SharpProof.Attributes;
            public sealed class Box<TItem> { public sealed class Inner { } }
            public static unsafe class Subject {
                {{declarations}}
            }
            """)], allowUnsafe: true);
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(discovery.Manifest.Callables, Has.Length.EqualTo(types.Length));
            Assert.That(discovery.Manifest.Callables.Select(static entry => entry.CallableId), Is.Unique);
            Assert.That(discovery.Manifest.Claims, Has.Length.EqualTo(types.Length));
            Assert.That(discovery.Manifest.Claims.Select(static entry => entry.ClaimId), Is.Unique);
            Assert.That(WorkerProtocolJson.ValidateManifest(discovery.Manifest).IsValid, Is.True);
        }
        var expectedTotalIds = discovery.Targets.Values
            .Where(static target => target.Method.Parameters[0].Type is INamedTypeSymbol)
            .Select(static target => target.Entry.CallableId).ToHashSet(StringComparer.Ordinal);
        Assert.That(expectedTotalIds, Has.Count.EqualTo(3));
        AssertUnsupportedArtifactRoundTrip(compilation, discovery, expectedTotalIds);
    }

    private static void AssertUnsupportedArtifactRoundTrip(
        CSharpCompilation compilation, ClaimManifestBuildResult discovery,
        HashSet<string>? expectedTotalIds = null)
    {
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        foreach (var entry in prepared)
        {
            TestContext.Out.WriteLine($"prepared={entry.Entry.CallableId}; reason={entry.FailureReason}; hasTotal={entry.Total != null}");
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded.Manifest.Callables.Select(static entry => entry.CallableId),
                Is.EqualTo(discovery.Manifest.Callables.Select(static entry => entry.CallableId)));
            Assert.That(decoded.Manifest.Claims.Select(static entry => entry.ClaimId),
                Is.EqualTo(discovery.Manifest.Claims.Select(static entry => entry.ClaimId)));
            Assert.That(prepared.Select(static entry => entry.Entry.CallableId),
                Is.EqualTo(decoded.Manifest.Callables.Select(static entry => entry.CallableId)));
            Assert.That(prepared.All(static entry =>
                entry.FailureReason == WorkerClaimReason.UnsupportedCallable), Is.True);
        }
        foreach (var entry in prepared)
        {
            Assert.That(entry.Total != null,
                Is.EqualTo(expectedTotalIds?.Contains(entry.Entry.CallableId) ?? false), entry.Entry.CallableId);
        }
        foreach (var entry in decoded.Manifest.Callables)
        {
            Assert.That(entry.ClaimIds, Is.EqualTo(decoded.Manifest.Claims
                .Where(claim => claim.CallableId == entry.CallableId).Select(static claim => claim.ClaimId)));
        }
    }
}
