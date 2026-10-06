using System.Runtime.InteropServices;
using SharpProof.Attributes;
public static class NativeBoundary
{
    [SharpProofTrusted("Reviewed libc getpid signature and declared effects.")]
    [EffectContract(
        SharpProofEffect.ReadsAmbientState | SharpProofEffect.UsesNativeCode,
        Capabilities = SharpProofCapability.NativeInterop,
        Complete = true,
        PreconditionFree = true,
        IsDeterministic = false)]
    [DllImport("libc", EntryPoint = "getpid")]
    public static extern int GetProcessId();
}
