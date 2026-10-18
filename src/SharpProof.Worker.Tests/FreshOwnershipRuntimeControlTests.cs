using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class FreshOwnershipRuntimeControlTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void CompiledCallerOwnedStateMatchesPinnedPath(bool array, bool handler, bool rebind)
    {
        var type = array ? "int[]" : "Cell";
        var fresh = array ? "new int[1]" : "new Cell()";
        var write = array ? "local[0] = 7;" : "local.Value = 7;";
        var transfer = handler
            ? "try { if (flag) throw new System.Exception(); } catch (System.Exception) { local = input; }"
            : "if (flag) local = input;";
        var source = "using SharpProof.Attributes; public sealed class Cell { public int Value; } public static class C { " +
            "[EnforcePure] public static int Target(" + type + " input, bool flag) { Contract.Requires(input != null); " +
            (array ? "Contract.Requires(input.Length == 1); " : "") +
            "Contract.Requires(flag == " + (rebind ? "true" : "false") + "); var local = " + fresh + "; " + transfer +
            write + " return 0; } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("FreshOwnershipRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("FreshOwnershipRuntime", isCollectible: true);
        try
        {
            var assembly = runtime.LoadFromStream(image);
            var input = array ? (object)new int[1] : Activator.CreateInstance(assembly.GetType("Cell")!)!;
            Assert.That(assembly.GetType("C")!.GetMethod("Target")!.Invoke(null, [input, rebind]), Is.EqualTo(0));
            Assert.That(array ? ((int[])input)[0] : (int)input.GetType().GetField("Value")!.GetValue(input)!, Is.EqualTo(rebind ? 7 : 0));
        }
        finally { runtime.Unload(); }
    }
}
