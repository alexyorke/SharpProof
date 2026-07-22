using SharpProof.Attributes;
public static class AnalyzerNativeSmtProbe {
    public static int KnownDiagnostic(int value) => value + 1;
    [Ensures("result >= 0")]
    public static int Normalize(int value) {
        if (value < 0) return -value;
        return value;
    }
}
