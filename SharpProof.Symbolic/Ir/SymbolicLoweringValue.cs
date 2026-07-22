namespace SharpProof.Symbolic.Ir;
internal static class SymbolicLoweringValue {
    internal static bool TryGet<T>(T? candidate, out T result)
        where T : class {
        result = candidate!;
        return candidate != null;
    }
}
