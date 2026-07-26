using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.V2.Test;

[TestFixture]
[NonParallelizable]
public sealed class RuntimeFlagshipOracleTests {
    private const string Source =
        """
        #nullable disable
        using System;
        using SharpProof.Attributes;

        public static class RuntimeFlagshipFixture {
            public static int State;

            [EnforcePure]
            public static int Pure(int value) => value + 1;

            [EnforcePure]
            public static int Writes(int value) {
                State = value;
                return value;
            }

            [ZeroAllocations]
            public static int NoAllocation(int value) => value + 1;

            [ZeroAllocations]
            public static byte[] Allocate() => new byte[64];

            [DoesNotThrow]
            public static int NoThrow(int value) => value + 1;

            [DoesNotThrow]
            public static int Throws(int divisor) => 10 / divisor;

            [AllowedCapabilities(SharpProofCapability.None)]
            public static int NoCapability(int value) => value;

            [AllowedCapabilities(SharpProofCapability.None)]
            public static void DisallowedSync(object gate) {
                lock (gate) {
                }
            }

            [AllowedCapabilities(SharpProofCapability.Synchronization)]
            public static void AllowedSync(object gate) {
                lock (gate) {
                }
            }

            [AllowedExceptions(
                typeof(DivideByZeroException),
                typeof(OverflowException))]
            public static int AllowedException(int divisor) => 10 / divisor;

            [AllowedExceptions(typeof(InvalidOperationException))]
            public static int DisallowedException(int divisor) => 10 / divisor;
        }
        """;

    [Test]
    public async Task AnalyzerVerdictsMatchConcreteFlagshipEffects() {
        string[] enabledIds = ["SP0002", "SP0016", "SP0045", "SP0046"];
        var compilation = AnalyzerV2TestHost.CreateCompilation(
            Source,
            enabledIds);

        var diagnostics = await AnalyzerV2TestHost.AnalyzeAsync(
            compilation,
            "effects");

        AssertDiagnostics(
            diagnostics,
            ("SP0002", "Writes"),
            ("SP0045", "Allocate"),
            ("SP0046", "Throws"),
            ("SP0016", "DisallowedSync"),
            ("SP0046", "DisallowedException"));

        var image = AnalyzerV2TestHost.EmitImage(compilation);
        WithRuntimeAssembly(image, assembly => {
            var fixture = assembly.GetType(
                    "RuntimeFlagshipFixture",
                    throwOnError: true)!;
            var random = new Random(23063);
            var pure = Delegate<Func<int, int>>(fixture, "Pure");
            var noAllocation = Delegate<Func<int, int>>(
                fixture,
                "NoAllocation");
            var noThrow = Delegate<Func<int, int>>(fixture, "NoThrow");
            var noCapability = Delegate<Func<int, int>>(
                fixture,
                "NoCapability");
            var state = fixture.GetField(
                    "State",
                    BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException("State field is missing.");
            const int unchangedState = 23063;
            state.SetValue(null, unchangedState);
            for (var index = 0; index < 128; index++) {
                var value = random.Next(-1_000_000, 1_000_000);
                Assert.That(pure(value), Is.EqualTo(value + 1));
                Assert.That(noAllocation(value), Is.EqualTo(value + 1));
                Assert.That(noThrow(value), Is.EqualTo(value + 1));
                Assert.That(noCapability(value), Is.EqualTo(value));
            }
            Assert.That(state.GetValue(null), Is.EqualTo(unchangedState));

            var noAllocationChecksum = 0;
            var noAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 128; index++)
                noAllocationChecksum ^= noAllocation(index);
            var noAllocationBytes =
                GC.GetAllocatedBytesForCurrentThread() - noAllocationBefore;
            Assert.That(noAllocationChecksum, Is.EqualTo(128));
            Assert.That(noAllocationBytes, Is.Zero);

            var writes = Delegate<Func<int, int>>(fixture, "Writes");
            var written = random.Next(1, int.MaxValue);
            Assert.That(writes(written), Is.EqualTo(written));
            Assert.That(state.GetValue(null), Is.EqualTo(written));

            var allocate = Delegate<Func<byte[]>>(fixture, "Allocate");
            _ = allocate();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var observedLength = 0;
            for (var index = 0; index < 64; index++)
                observedLength += allocate().Length;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(observedLength, Is.EqualTo(64 * 64));
            Assert.That(allocated, Is.GreaterThanOrEqualTo(64 * 64));

            AssertThrows<DivideByZeroException>(
                Delegate<Func<int, int>>(fixture, "Throws"));
            AssertThrows<DivideByZeroException>(
                Delegate<Func<int, int>>(fixture, "AllowedException"));
            AssertThrows<DivideByZeroException>(
                Delegate<Func<int, int>>(fixture, "DisallowedException"));

            var gate = new object();
            Delegate<Action<object>>(fixture, "AllowedSync")(gate);
            Delegate<Action<object>>(fixture, "DisallowedSync")(gate);
        });
    }

    private static void AssertDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        params (string Id, string Method)[] expected) {
        var actual = diagnostics
            .Select(static diagnostic =>
                (diagnostic.Id, Method: ExtractQuotedName(
                    diagnostic.GetMessage())))
            .OrderBy(static item => item.Id, StringComparer.Ordinal)
            .ThenBy(static item => item.Method, StringComparer.Ordinal)
            .ToArray();
        var orderedExpected = expected
            .OrderBy(static item => item.Id, StringComparer.Ordinal)
            .ThenBy(static item => item.Method, StringComparer.Ordinal)
            .ToArray();
        Assert.That(actual, Is.EqualTo(orderedExpected));
    }

    private static string ExtractQuotedName(string message) {
        var first = message.IndexOf('\'');
        var second = first < 0 ? -1 : message.IndexOf('\'', first + 1);
        return first >= 0 && second > first
            ? message.Substring(first + 1, second - first - 1)
            : throw new InvalidOperationException(
                "Expected diagnostic message to contain a quoted method name.");
    }

    private static void AssertThrows<TException>(Func<int, int> method)
        where TException : Exception {
        Action invocation = () => method(0);
        Assert.Throws<TException>(invocation);
    }

    private static TDelegate Delegate<TDelegate>(
        Type fixture,
        string methodName)
        where TDelegate : Delegate =>
        (TDelegate)(fixture.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static) ??
            throw new InvalidOperationException(
                $"Runtime method '{methodName}' is missing."))
        .CreateDelegate(typeof(TDelegate));

    private static void WithRuntimeAssembly(
        byte[] image,
        Action<Assembly> action) {
        var context = new AssemblyLoadContext(
            "SharpProof.Analyzer.V2.Test.RuntimeFlagship",
            isCollectible: true);
        context.Resolving += ResolveFromDefaultContext;
        try {
            using var stream = new MemoryStream(image, writable: false);
            action(context.LoadFromStream(stream));
        }
        finally {
            context.Resolving -= ResolveFromDefaultContext;
            context.Unload();
        }
    }

    private static Assembly? ResolveFromDefaultContext(
        AssemblyLoadContext context,
        AssemblyName requestedName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(
                    candidate.GetName(),
                    requestedName));
}
