namespace SharpProof.Testing;

internal static class ContractIntrinsicValidationFixtures
{
    internal const string DirectContract =
        """
        using SharpProof.Attributes;

        public static class Target {
            public static int Read(int value) {
                Contract.Ensures(
                    Contract.Old(Contract.Result<int>()) == value);
                return value;
            }
        }
        """;

    internal const string CompanionContract =
        """
        using SharpProof.Attributes;

        public interface Target {
            int Read(int value);
        }

        [ContractFor(typeof(Target))]
        public static class TargetContracts {
            public static int Read(Target receiver, int value) {
                Contract.Ensures(
                    Contract.Old(Contract.Result<int>()) == value);
                return value;
            }
        }
        """;

    internal const string IndirectIntrinsicCalls =
        """
        using System;
        using SharpProof.Attributes;

        public static class Target {
            public static int Read(int value) {
                Func<int> result = Contract.Result<int>;
                Func<int, int> old = Contract.Old<int>;
                return result() + old(value);
            }
        }
        """;
}
