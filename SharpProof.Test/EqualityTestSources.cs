namespace SharpProof.Test;

internal static class EqualityTestSources {
    internal const string ImpureEquatableMutableRecord = """
        public sealed class MutableRecord : IEquatable<MutableRecord>
        {
            public bool Equals(MutableRecord other)
            {
                Console.WriteLine("equals");
                return true;
            }
        }
        """;
}
