namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ExceptionHandlerReachabilityTests
{
    [Test]
    public void ClosedVirtualDispatchUsesTheExactExceptionSet()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public class Base {
                public virtual void Call() { }
            }

            public class MethodSealed : Base {
                public sealed override void Call() { }
            }

            public sealed class TypeSealed : Base {
                public override void Call() { }
            }

            public static class Sample {
                public static void SealedMethod(MethodSealed value) {
                    try {
                        value.Call();
                    }
                    catch (ApplicationException) {
                    }
                }

                public static void SealedType(TypeSealed value) {
                    try {
                        value.Call();
                    }
                    catch (ApplicationException) {
                    }
                }

                public static void OpenDispatch(Base value) {
                    try {
                        value.Call();
                    }
                    catch (ApplicationException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                IsCatchReachable(compilation, session, "SealedMethod"),
                Is.False);
            Assert.That(
                IsCatchReachable(compilation, session, "SealedType"),
                Is.False);
            Assert.That(
                IsCatchReachable(compilation, session, "OpenDispatch"),
                Is.True);
        }
    }

    [Test]
    public void OnlyAuthenticatedRuntimeRefLikeAccessorsAreNonthrowing()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using System;

            namespace External;

            public readonly ref struct ThrowingView {
                public int Value =>
                    throw new InvalidOperationException();
            }
            """,
            "ExternalRefLikeAccessors");
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using External;

            public static class Sample {
                public static void ReadExternal(ThrowingView value) {
                    try {
                        _ = value.Value;
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void ReadRuntime(Span<int> value) {
                    try {
                        _ = value.Length;
                    }
                    catch (Exception) {
                    }
                }
            }
            """,
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                IsCatchReachable(compilation, session, "ReadExternal"),
                Is.True);
            Assert.That(
                IsCatchReachable(compilation, session, "ReadRuntime"),
                Is.False);
        }
    }

    [Test]
    public void RepeatedAndRecursiveCallsPreserveCatchReachability()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static void Leaf() {
                    throw new InvalidOperationException();
                }

                public static void Repeated() {
                    try {
                        Leaf();
                        Leaf();
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void First() {
                    Second();
                }

                public static void Second() {
                    First();
                    throw new ApplicationException();
                }

                public static void Indirect() {
                    try {
                        First();
                    }
                    catch (ApplicationException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                IsCatchReachable(compilation, session, "Repeated"),
                Is.True);
            Assert.That(
                IsCatchReachable(compilation, session, "Indirect"),
                Is.True);
        }
    }

    [Test]
    public void RecursiveMethodWithUsingDeclarationKeepsCycleGuard()
    {
        // Resolving the using declaration's disposal asks whether the rest of
        // the block can exit abruptly. That query must stay in the active
        // traversal, or re-entering Recursive recurses until stack overflow.
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Resource : IDisposable {
                public void Dispose() { }
            }

            public static class Sample {
                public static void Recursive(bool again) {
                    using var resource = new Resource();
                    if (again) {
                        Recursive(false);
                    }

                    throw new ApplicationException();
                }

                public static void Guarded() {
                    try {
                        Recursive(true);
                    }
                    catch (ApplicationException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        Assert.That(
            IsCatchReachable(compilation, session, "Guarded"),
            Is.True);
    }

    [Test]
    public void RecursiveCatchHandlerUnderFinallyKeepsCycleGuard()
    {
        // Deciding whether the finally is reachable asks whether the catch
        // handler can exit abruptly; that query must stay in the active
        // traversal, or the handler's recursive call never terminates.
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static void Recursive(bool again) {
                    try {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException) {
                        if (again) {
                            Recursive(false);
                        }

                        throw new ApplicationException();
                    }
                    finally {
                    }
                }

                public static void Guarded() {
                    try {
                        Recursive(true);
                    }
                    catch (ApplicationException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        Assert.That(
            IsCatchReachable(compilation, session, "Guarded"),
            Is.True);
    }

    [Test]
    public void ConstructorMemberInitializersReachMatchingCatch()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class FieldInitialized {
                private readonly int _value = Fail();

                public FieldInitialized() { }

                private static int Fail() =>
                    throw new InvalidOperationException();
            }

            public sealed class ImplicitInitialized {
                private readonly int _value = Fail();

                private static int Fail() =>
                    throw new InvalidOperationException();
            }

            public sealed class PropertyInitialized {
                public int Value { get; } = Fail();

                public PropertyInitialized() { }

                private static int Fail() =>
                    throw new InvalidOperationException();
            }

            public sealed class ChainedInitializer {
                private readonly int _value = Fail();

                public ChainedInitializer() { }

                public ChainedInitializer(int value) : this() { }

                private static int Fail() =>
                    throw new InvalidOperationException();
            }

            public struct StructInitialized {
                private int _value = Fail();

                public StructInitialized() { }

                private static int Fail() =>
                    throw new InvalidOperationException();
            }

            public static class Sample {
                public static void Field() {
                    try {
                        _ = new FieldInitialized();
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void Implicit() {
                    try {
                        _ = new ImplicitInitialized();
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void Property() {
                    try {
                        _ = new PropertyInitialized();
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void Chained() {
                    try {
                        _ = new ChainedInitializer(1);
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void Struct() {
                    try {
                        _ = new StructInitialized();
                    }
                    catch (InvalidOperationException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                IsCatchReachable(compilation, session, "Field"),
                Is.True);
            Assert.That(
                IsCatchReachable(compilation, session, "Implicit"),
                Is.True);
            Assert.That(
                IsCatchReachable(compilation, session, "Property"),
                Is.True);
            Assert.That(
                IsCatchReachable(compilation, session, "Chained"),
                Is.True);
            Assert.That(
                IsCatchReachable(compilation, session, "Struct"),
                Is.True);
        }
    }

    [Test]
    public void ImplicitRecordCopyConstructorKeepsCatchReachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public record BaseRecord {
                protected BaseRecord(BaseRecord other) {
                    throw new InvalidOperationException();
                }
            }

            public record DerivedRecord : BaseRecord;

            public static class Sample {
                public static void Copy(DerivedRecord value) {
                    try {
                        _ = value with { };
                    }
                    catch (InvalidOperationException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        Assert.That(
            IsCatchReachable(compilation, session, "Copy"),
            Is.True);
    }

    [Test]
    public void CallableExceptionWalkHonorsDepthCutoff()
    {
        const int lastForwardingMethod = 34;
        var forwardingMethods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, lastForwardingMethod)
                .Select(index =>
                    $"public static void M{index}() {{ M{index + 1}(); }}"));
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
                {{forwardingMethods}}
                public static void M{{lastForwardingMethod}}() { }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var reachability = EffectTestHost.CreateHandlerReachability(
            compilation,
            EffectTestHost.SampleMethod(compilation, "M0"),
            session);

        Assert.That(
            reachability.CanMethodThrow(
                EffectTestHost.SampleMethod(compilation, "M0")),
            Is.True);
    }

    private static bool IsCatchReachable(
        Compilation compilation,
        EffectAnalysisSession session,
        string methodName)
    {
        var method = EffectTestHost.SampleMethod(compilation, methodName);
        return EffectTestHost.CreateHandlerReachability(
                compilation,
                method,
                session)
            .IsReachable(
                EffectTestHost.CatchClauseIn(method),
                inFilter: false);
    }
}
