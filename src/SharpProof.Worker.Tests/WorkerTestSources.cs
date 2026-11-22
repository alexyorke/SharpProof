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

            // `async void` stays outside the admitted async shapes.
            [ZeroAllocations]
            public static async void Async() {
                await Task.Yield();
                _ = new object();
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

            // `async void` stays outside the admitted async shapes.
            public static async void Async() {
                Contract.Ensures(true);
                await Task.Yield();
            }
        }
        """;
}
