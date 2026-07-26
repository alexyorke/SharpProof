namespace SharpProof.Effects.Test;

[TestFixture]
[NonParallelizable]
public sealed class RuntimeEffectOracleTests {
    [Test]
    public void ManagedAllocationMatchesRuntimeAllocationDelta() {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class RuntimeFixture {
                public static byte[] Allocate() => new byte[64];
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "RuntimeFixture",
                "Allocate"));
        var image = EffectTestHost.EmitImage(compilation);

        WithRuntimeAssembly(image, assembly => {
            var allocate = RequireMethod(
                    assembly,
                    "RuntimeFixture",
                    "Allocate")
                .CreateDelegate<Func<byte[]>>();
            _ = allocate();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var observedLength = 0;
            for (var iteration = 0; iteration < 64; iteration++)
                observedLength += allocate().Length;
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(observedLength, Is.EqualTo(64 * 64));
            Assert.That(allocatedBytes, Is.GreaterThanOrEqualTo(64 * 64));
        });
        Assert.That(
            result.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            result.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.Allocates));
    }

    [Test]
    public void ResolvedThrowSetContainsRuntimeException() {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class RuntimeFixture {
                public static int Divide(int divisor) => 10 / divisor;
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "RuntimeFixture",
                "Divide"));
        var image = EffectTestHost.EmitImage(compilation);
        string? observedException = null;

        WithRuntimeAssembly(image, assembly => {
            var divide = RequireMethod(
                    assembly,
                    "RuntimeFixture",
                    "Divide")
                .CreateDelegate<Func<int, int>>();
            try {
                _ = divide(0);
            }
            catch (Exception exception) {
                observedException = exception.GetType().FullName;
            }
        });

        Assert.That(observedException, Is.EqualTo("System.DivideByZeroException"));
        Assert.That(
            ResolvedThrowMetadataNames(result.Summary),
            Does.Contain(observedException));
    }

    [Test]
    public void StaticWriteMatchesObservableRuntimeStateChange() {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class RuntimeFixture {
                private static int s_value;

                public static void Write(int value) => s_value = value;
                public static int Read() => s_value;
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "RuntimeFixture",
                "Write"));
        var image = EffectTestHost.EmitImage(compilation);

        WithRuntimeAssembly(image, assembly => {
            var write = RequireMethod(
                    assembly,
                    "RuntimeFixture",
                    "Write")
                .CreateDelegate<Action<int>>();
            var read = RequireMethod(
                    assembly,
                    "RuntimeFixture",
                    "Read")
                .CreateDelegate<Func<int>>();
            var before = read();
            write(1729);
            var after = read();

            Assert.That(before, Is.Not.EqualTo(1729));
            Assert.That(after, Is.EqualTo(1729));
        });
        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
        Assert.That(
            result.Projection.Effects & SharpProofEffect.WritesStaticState,
            Is.EqualTo(SharpProofEffect.WritesStaticState));
    }

    [Test]
    public void DeclaredConsoleCapabilityMatchesObservableOutput() {
        var fixture = EffectTestHost.EmitImage(
            """
            using System;
            using SharpProof.Attributes;

            public static class RuntimeCapabilityFixture {
                [SharpProofTrusted("runtime oracle validates the implementation")]
                [EffectContract(
                    SharpProofEffect.WritesAmbientState,
                    Capabilities = SharpProofCapability.Console,
                    Complete = true)]
                public static void Touch() => Console.Write("effect-oracle");
            }
            """,
            "RuntimeCapabilityFixtureAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Invoke() => RuntimeCapabilityFixture.Touch();
            }
            """,
            fixture.Reference);
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "Invoke"));
        var originalOutput = Console.Out;
        using var observedOutput = new StringWriter();

        try {
            Console.SetOut(observedOutput);
            WithRuntimeAssembly(fixture, assembly => {
                var touch = RequireMethod(
                        assembly,
                        "RuntimeCapabilityFixture",
                        "Touch")
                    .CreateDelegate<Action>();
                touch();
            });
        }
        finally {
            Console.SetOut(originalOutput);
        }

        Assert.That(observedOutput.ToString(), Is.EqualTo("effect-oracle"));
        Assert.That(
            result.Summary.Capabilities.Contains(EffectCapabilityKind.Console),
            Is.True);
        Assert.That(
            result.Projection.Capabilities,
            Is.EqualTo(SharpProofCapability.Console));
        Assert.That(
            result.Projection.Effects & SharpProofEffect.WritesAmbientState,
            Is.EqualTo(SharpProofEffect.WritesAmbientState));
    }

    [Test]
    public void LocalAliasWriteMatchesCallerOwnedRuntimeStateChange() {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class RuntimeFixture {
                public static void Mutate(Box value) {
                    var alias = value;
                    alias.Value = 1729;
                }
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "RuntimeFixture",
                "Mutate"));
        var image = EffectTestHost.EmitImage(compilation);

        WithRuntimeAssembly(image, assembly => {
            var boxType = assembly.GetType("Box", throwOnError: true)!;
            var box = Activator.CreateInstance(boxType) ??
                      throw new AssertionException(
                          "The alias-oracle receiver could not be created.");
            RequireMethod(
                assembly,
                "RuntimeFixture",
                "Mutate").Invoke(null, [box]);
            var value = boxType.GetField("Value")?.GetValue(box);

            Assert.That(value, Is.EqualTo(1729));
        });

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.False);
    }

    [Test]
    public void ImplicitExceptionSummariesContainRuntimeEdgeCases() {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class RuntimeFixture {
                public static int Divide(int left, int right) => left / right;
                public static int Remainder(int left, int right) => left % right;
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
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var image = EffectTestHost.EmitImage(compilation);
        var cases = new[] {
            new RuntimeExceptionCase(
                "Divide",
                [int.MinValue, -1],
                "System.OverflowException"),
            new RuntimeExceptionCase(
                "Remainder",
                [int.MinValue, -1],
                "System.OverflowException"),
            new RuntimeExceptionCase(
                "CompoundDivide",
                [1, 0],
                "System.DivideByZeroException"),
            new RuntimeExceptionCase(
                "CompoundDivide",
                [int.MinValue, -1],
                "System.OverflowException"),
            new RuntimeExceptionCase(
                "CompoundRemainder",
                [1, 0],
                "System.DivideByZeroException"),
            new RuntimeExceptionCase(
                "CompoundRemainder",
                [int.MinValue, -1],
                "System.OverflowException"),
            new RuntimeExceptionCase(
                "CheckedIncrement",
                [int.MaxValue],
                "System.OverflowException"),
            new RuntimeExceptionCase(
                "Array",
                [-1],
                "System.OverflowException"),
            new RuntimeExceptionCase(
                "Lock",
                [null],
                "System.ArgumentNullException")
        };

        WithRuntimeAssembly(image, assembly => {
            foreach (var edge in cases) {
                var method = RequireMethod(
                    assembly,
                    "RuntimeFixture",
                    edge.MethodName);
                string? observed = null;
                try {
                    _ = method.Invoke(null, edge.Arguments);
                }
                catch (TargetInvocationException exception) {
                    observed = exception.InnerException?.GetType().FullName;
                }

                Assert.That(
                    observed,
                    Is.EqualTo(edge.ExceptionMetadataName),
                    edge.MethodName);
                var summary = session.Analyze(
                    EffectTestHost.RequireMethod(
                        compilation,
                        "RuntimeFixture",
                        edge.MethodName)).Summary;
                Assert.That(
                    ResolvedThrowMetadataNames(summary),
                    Does.Contain(edge.ExceptionMetadataName),
                    edge.MethodName);
            }
        });
    }

    private static void WithRuntimeAssembly(
        EmittedAssemblyImage image,
        Action<Assembly> action) {
        var context = new AssemblyLoadContext(
            "SharpProof.Effects.Test.RuntimeOracle",
            isCollectible: true);
        context.Resolving += ResolveFromDefaultContext;
        try {
            using var stream = new MemoryStream(image.Image, writable: false);
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

    private static MethodInfo RequireMethod(
        Assembly assembly,
        string typeName,
        string methodName) =>
        assembly.GetType(typeName, throwOnError: true)!
            .GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static) ??
        throw new InvalidOperationException(
            $"Runtime method '{typeName}.{methodName}' was not found.");

    private static ImmutableArray<string> ResolvedThrowMetadataNames(
        EffectSummary summary) =>
        [.. summary.Throws.Types.Select(static type =>
            type.ContainingNamespace.MetadataName + "." + type.MetadataName)];

    private sealed record RuntimeExceptionCase(
        string MethodName,
        object?[] Arguments,
        string ExceptionMetadataName);
}
