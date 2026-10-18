internal static class WorkerTestSources
{
    internal const string UnsupportedEffectCallables =
        """
        using System;
        using System.Threading.Tasks;
        using SharpProof.Attributes;

        public static class Subject {
            [ZeroAllocations]
            public static object Generic<T>() =>
                new object();

            [ZeroAllocations]
            public static async Task<object> Async() {
                await Task.Yield();
                return new object();
            }

            [ZeroAllocations]
            public static object DelegateCall(
                Func<object> factory) =>
                new object();
        }
        """;

    internal const string UnsupportedContractCallables =
        """
        using System.Threading.Tasks;
        using SharpProof.Attributes;

        public static class Subject {
            public static int Generic<T>() {
                Contract.Ensures(
                    Contract.Result<int>() == 1);
                return 1;
            }

            public static async Task<int> Async() {
                Contract.Ensures(true);
                await Task.Yield();
                return 1;
            }
        }
        """;
}
