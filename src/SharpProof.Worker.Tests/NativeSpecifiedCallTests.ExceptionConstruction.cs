using NUnit.Framework;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("System.InvalidOperationException")]
    [TestCase("System.Collections.Generic.KeyNotFoundException")]
    public async Task ConstructorMessagePreconditionsSelectOnlyTheCallbackFreePath(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "string message",
            "Contract.Requires(message != null); _ = new " + type + "(message); return 0;", true, false, true);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "string message",
            "_ = new " + type + "(message); return 0;", false, false, false);
    }

    [TestCase("string message = null; _ = new System.ArgumentNullException(message: message, paramName: message = \"parameter\"); return 0;", false)]
    [TestCase("string message = \"message\"; _ = new System.ArgumentNullException(message: message, paramName: message = null); return 0;", true)]
    public async Task ConstructorNamedArgumentOrderPreservesTheEvaluatedMessage(string body, bool callbackFree)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "", body, callbackFree, false, callbackFree);
    }

    [TestCase("new System.ArgumentNullException()")]
    [TestCase("new System.ArgumentNullException(\"parameter\")")]
    [TestCase("new System.ArgumentOutOfRangeException(\"parameter\")")]
    [TestCase("new System.InvalidOperationException()")]
    [TestCase("new System.InvalidOperationException((string)null)")]
    [TestCase("new System.Collections.Generic.KeyNotFoundException((string)null)")]
    [TestCase("new System.AggregateException()")]
    [TestCase("new System.AggregateException((string)null)")]
    [TestCase("new System.Diagnostics.UnreachableException((string)null)")]
    public async Task ResourceMessageConstructorsRetainAmbientCallbackEffects(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "",
            "_ = " + expression + "; return 0;", false, false, false);
    }

    [TestCase("new System.Exception((string)null)")]
    [TestCase("new System.InvalidOperationException(\"message\")")]
    [TestCase("new System.Collections.Generic.KeyNotFoundException(\"message\")")]
    [TestCase("new System.ArgumentException((string)null)")]
    [TestCase("new System.ArgumentException(\"message\", \"parameter\")")]
    [TestCase("new System.ArgumentNullException(\"parameter\", \"message\")")]
    [TestCase("new System.ArgumentOutOfRangeException(\"parameter\", \"message\")")]
    public async Task ExplicitMessageConstructorEffectsKeepTheirCallbackFreeControls(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "",
            "_ = " + expression + "; return 0;", true, false, true);
    }
}
