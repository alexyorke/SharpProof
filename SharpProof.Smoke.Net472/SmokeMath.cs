using SharpProof.Attributes;

namespace SharpProof.Smoke.Net472;

/// <summary>
///     Minimal types exercised under .NET Framework 4.7.2 so CI/local builds verify the analyzer loads on net472
///     consumers.
/// </summary>
public static class SmokeMath {
    [EnforcePure]
    public static int Add(int a, int b) => a + b;
}
