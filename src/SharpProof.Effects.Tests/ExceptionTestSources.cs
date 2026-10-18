namespace SharpProof.Effects.Test;

internal static class ExceptionTestSources
{
    internal const string CommonMethods =
        """
        public static int Divide(int left, int right) => left / right;
        public static int Remainder(int left, int right) => left % right;
        public static int? NullableDivide(int? left, int? right) =>
            left / right;
        public static int? NullableRemainder(int? left, int? right) =>
            left % right;
        public static nint NativeDivide(nint left, nint right) =>
            left / right;
        public static nint NativeRemainder(nint left, nint right) =>
            left % right;
        public static nuint NativeUnsignedDivide(
            nuint left,
            nuint right) => left / right;
        public static nuint NativeUnsignedRemainder(
            nuint left,
            nuint right) => left % right;
        public static int CompoundDivide(int left, int right) {
            left /= right;
            return left;
        }
        public static int CompoundRemainder(int left, int right) {
            left %= right;
            return left;
        }
        public static int CheckedIncrement(int value) {
            checked {
                value++;
            }
            return value;
        }
        public static int[] Array(int length) => new int[length];
        public static void Lock(object gate) {
            lock (gate) {
            }
        }
        """;
}
