using System.Runtime.InteropServices;
using SharpProof.Attributes;

namespace SharpProof.Samples.TrustedBoundary;

public static class TrustedBoundaryExamples {
    [AllowedCapabilities(SharpProofCapability.NativeInterop)]
    public static int CurrentProcessId() =>
        NativeMethods.GetProcessId();

    private static class NativeMethods {
        [DllImport("libc", EntryPoint = "getpid")]
        [SharpProofTrusted(
            "The signature and effect summary were reviewed against libc.")]
        [EffectContract(
            SharpProofEffect.ReadsAmbientState |
                SharpProofEffect.UsesNativeCode,
            Capabilities = SharpProofCapability.NativeInterop,
            Complete = true,
            IsDeterministic = false)]
        internal static extern int GetProcessId();
    }
}
