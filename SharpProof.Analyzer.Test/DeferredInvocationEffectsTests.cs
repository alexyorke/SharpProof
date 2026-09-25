using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class DeferredInvocationEffectsTests
{
    [Test]
    public async Task UnobservedAsyncFaultsAndUnusedIteratorsAreNotSynchronousThrows()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Fixture {
                private static async Task AsyncFault() {
                    throw new InvalidOperationException();
                }

                private static IEnumerable<int> IteratorFault() {
                    throw new InvalidOperationException();
                    yield break;
                }

                [DoesNotThrow]
                public static Task ReturnFaultedTask() => AsyncFault();

                [DoesNotThrow]
                public static int CreateUnusedIterator() {
                    var sequence = IteratorFault();
                    return 0;
                }
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer());

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AwaitResultWaitAndIteratorEnumerationRetainDeferredThrows()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Fixture {
                private static async Task<int> AsyncFault() {
                    throw new InvalidOperationException();
                }

                private static Task ReturnFaultedTask() => AsyncFault();

                private static IEnumerable<int> IteratorFault() {
                    throw new InvalidOperationException();
                    yield break;
                }

                [DoesNotThrow]
                public static int ReadResult() => AsyncFault().Result;

                [DoesNotThrow]
                public static void WaitForResult() => AsyncFault().Wait();

                [DoesNotThrow]
                public static async Task AwaitFault() {
                    await AsyncFault();
                }

                [DoesNotThrow]
                public static async Task AwaitTaskReturnedFromFactory() {
                    await ReturnFaultedTask();
                }

                [DoesNotThrow]
                public static int EnumerateIterator() =>
                    IteratorFault().Count();
            }
            """,
            "effects",
            [],
            new SharpProofAnalyzer());

        AnalyzerTestHost.AssertIds(
            diagnostics,
            "SP0047",
            "SP0046",
            "SP0047",
            "SP0047",
            "SP0047");
    }
}
