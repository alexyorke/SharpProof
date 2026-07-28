using System.Runtime.InteropServices;
using SharpProof.Attributes;

namespace SharpProof.Samples.TrustedBoundary;

public static class TrustedBoundaryExamples {
    [AllowedCapabilities(SharpProofCapability.NativeInterop)]
    public static int CurrentProcessId() =>
        NativeMethods.GetCurrentProcessId();

    private static class NativeMethods {
        [DllImport("kernel32.dll")]
        [SharpProofTrusted(
            "The signature and effect summary were reviewed against Win32.")]
        [EffectContract(
            SharpProofEffect.ReadsAmbientState |
                SharpProofEffect.UsesNativeCode,
            Capabilities = SharpProofCapability.NativeInterop,
            Complete = true,
            IsDeterministic = false)]
        internal static extern int GetCurrentProcessId();
    }
}
