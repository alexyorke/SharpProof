using System.Globalization;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class OptionalDefaultBoxingAuditTests
{
    [TestCase("default-double", true)]
    [TestCase("explicit-double", true)]
    [TestCase("default-int", true)]
    [TestCase("default-null", false)]
    public async Task MetadataDefaultsKeepCallerBoxingEffects(string scenario, bool allocates)
    {
        var parameter = scenario switch
        {
            "default-int" => "[Optional, DefaultParameterValue(7)] object value",
            "default-null" => "object value = null",
            _ => "[Optional, DefaultParameterValue(1.25d)] object value"
        };
        var boundarySource = "using System.Runtime.InteropServices; using SharpProof.Attributes; public static class Boundary { " +
            "[SharpProofTrusted(\"Reviewed identity body: return the supplied object reference.\")] " +
            "[EffectContract(SharpProofEffect.None, Complete = true, PreconditionFree = true)] " +
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] " +
            "public static object Echo(" + parameter + ") => value; }";
        var external = TestCompilation.Create("OptionalBoxingBoundary", ("Boundary.cs", boundarySource));
        external = external.WithOptions(external.Options.WithOptimizationLevel(OptimizationLevel.Release));
        using var directory = new TempDirectory("sharpproof-optional-boxing");
        var path = Path.Combine(directory.FullName, "Boundary.dll");
        var boundaryEmit = external.Emit(path);
        Assert.That(boundaryEmit.Success, Is.True, string.Join("\n", boundaryEmit.Diagnostics));
        var argument = scenario == "explicit-double" ? "1.25d" : "";
        var callerSource = "using SharpProof.Attributes; public static class Subject { [ZeroAllocations] " +
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] " +
            "public static object Target() => Boundary.Echo(" + argument + "); }";
        var tree = CSharpSyntaxTree.ParseText(callerSource, (CSharpParseOptions)external.SyntaxTrees.Single().Options, "Subject.cs");
        var compilation = CSharpCompilation.Create("OptionalBoxingCaller", [tree],
            external.References.Append(MetadataReference.CreateFromFile(path)), external.Options);
        TestCompilation.AssertNoErrors(compilation);
        var syntax = (await tree.GetRootAsync()).DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(invocation.TargetMethod.ContainingAssembly.Identity.Name, Is.EqualTo(external.AssemblyName));
        Assert.That(invocation.TargetMethod.ContainingType.Name, Is.EqualTo("Boundary"));
        Assert.That(invocation.TargetMethod.Name, Is.EqualTo("Echo"));
        Assert.That(invocation.TargetMethod.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(invocation.TargetMethod.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Object));
        var boundArgument = invocation.Arguments.Single();
        Assert.That(boundArgument.Parameter!.Ordinal, Is.Zero);
        Assert.That(boundArgument.ArgumentKind, Is.EqualTo(scenario == "explicit-double" ? ArgumentKind.Explicit : ArgumentKind.DefaultValue));
        await TestContext.Progress.WriteLineAsync("scenario=" + scenario + "; binding=" + invocation.TargetMethod.ToDisplayString() +
            "; argument=" + Describe(boundArgument));
        using var callerImage = new MemoryStream();
        var callerEmit = compilation.Emit(callerImage);
        Assert.That(callerEmit.Success, Is.True, string.Join("\n", callerEmit.Diagnostics));
        callerImage.Position = 0;
        var runtime = new AssemblyLoadContext("OptionalBoxingOracle", isCollectible: true);
        try
        {
            using var boundaryImage = new MemoryStream(await File.ReadAllBytesAsync(path));
            var boundaryType = runtime.LoadFromStream(boundaryImage).GetType("Boundary")!;
            var echoMethod = boundaryType.GetMethod("Echo")!;
            var echo = echoMethod.CreateDelegate<Func<object, object>>();
            var calleeIl = echoMethod.GetMethodBody()!.GetILAsByteArray()!;
            Assert.That(calleeIl, Is.EqualTo(new byte[] { 0x02, 0x2A }), "Reviewed callee must only return its supplied reference.");
            var sentinel = new object();
            var calleeControl = Measure(() => echo(sentinel));
            Assert.That(calleeControl.Value, Is.SameAs(sentinel));
            Assert.That(calleeControl.Bytes, Is.Zero, "The identity callee itself must allocate nothing.");
            var targetMethod = runtime.LoadFromStream(callerImage).GetType("Subject")!.GetMethod("Target")!;
            var target = targetMethod.CreateDelegate<Func<object?>>();
            var callerIl = targetMethod.GetMethodBody()!.GetILAsByteArray()!;
            var boxOffset = scenario == "default-int" ? 1 : 9;
            var callOffset = allocates ? boxOffset + 5 : 1;
            if (allocates)
            {
                Assert.That(callerIl[boxOffset], Is.EqualTo((byte)OpCodes.Box.Value));
                var boxedType = targetMethod.Module.ResolveType(BitConverter.ToInt32(callerIl, boxOffset + 1));
                Assert.That(boxedType, Is.EqualTo(scenario == "default-int" ? typeof(int) : typeof(double)));
                var first = target();
                var second = target();
                Assert.That(first, Is.Not.SameAs(second), "Each caller invocation must expose a fresh escaping box.");
            }
            else
            {
                Assert.That(callerIl[0], Is.EqualTo((byte)OpCodes.Ldnull.Value));
            }
            Assert.That(callerIl[callOffset], Is.EqualTo((byte)OpCodes.Call.Value));
            Assert.That(targetMethod.Module.ResolveMethod(BitConverter.ToInt32(callerIl, callOffset + 1)), Is.EqualTo(echoMethod));
            Assert.That(callerIl[callOffset + 5], Is.EqualTo((byte)OpCodes.Ret.Value));
            Assert.That(callerIl.Length, Is.EqualTo(callOffset + 6));
            var measured = Measure(target);
            if (allocates)
            {
                Assert.That(measured.Bytes, Is.GreaterThan(0), "Warmed caller returns escaping boxes after 1000 warmups.");
                Assert.That(measured.Value, Is.EqualTo(scenario == "default-int" ? (object)7 : 1.25d));
            }
            else
            {
                Assert.That(measured.Bytes, Is.Zero);
                Assert.That(measured.Value, Is.Null);
            }
            await TestContext.Progress.WriteLineAsync("scenario=" + scenario + "; calleeIL=" + Convert.ToHexString(calleeIl) +
                "; callerIL=" + Convert.ToHexString(callerIl) + "; callee32=" + calleeControl.Bytes + "; caller32=" + measured.Bytes);
        }
        finally
        {
            runtime.Unload();
        }
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        var preparation = project.Snapshot.Callables.Single();
        await TestContext.Progress.WriteLineAsync("scenario=" + scenario + "; total=" + (preparation.Total != null) +
            "; failure=" + preparation.FailureReason + "; outcome=" + claim.Outcome + "; reason=" + claim.Reason);
        if (allocates)
        {
            Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                "An observed caller boxing allocation cannot have a valid allocation-free proof. " + claim.Reason);
        }
        else
        {
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
        }
    }

    private static (object? Value, long Bytes) Measure(Func<object?> target)
    {
        object? value = null;
        for (var repeat = 0; repeat < 1000; repeat++)
        {
            value = target();
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < 32; repeat++)
        {
            value = target();
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(value);
        return (value, bytes);
    }

    private static string Describe(IOperation operation)
    {
        var constant = operation.ConstantValue.HasValue
            ? Convert.ToString(operation.ConstantValue.Value, CultureInfo.InvariantCulture) ?? "null" : "<none>";
        return operation.Kind + "(" + operation.Type?.ToDisplayString() + ",implicit=" + operation.IsImplicit +
            ",constant=" + constant + ")[" + string.Join(",", operation.ChildOperations.Select(Describe)) + "]";
    }
}
