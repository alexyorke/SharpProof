using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ForeachRuntimeMemberSoundnessTests
    {
        [Test]
        public async Task ForeachImpureMoveNext_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetEnumerator() => new Enumerator();

    public sealed class Enumerator
    {
        public int Current => 1;

        public bool MoveNext()
        {
            GlobalState.Count++;
            return false;
        }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Sequence values)
    {
        foreach (var value in values)
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ForeachImpureCurrentGetter_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetEnumerator() => new Enumerator();

    public sealed class Enumerator
    {
        public int Current
        {
            get
            {
                GlobalState.Count++;
                return 1;
            }
        }

        [EnforcePure]
        public bool MoveNext() => true;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Sequence values)
    {
        foreach (var value in values)
        {
            break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ForeachImpureDispose_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetEnumerator() => new Enumerator();

    public sealed class Enumerator : IDisposable
    {
        public int Current => 1;
        [EnforcePure]
        public bool MoveNext() => false;

        public void Dispose()
        {
            GlobalState.Count++;
        }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Sequence values)
    {
        foreach (var value in values)
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ForeachPureCustomEnumerator_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetEnumerator() => new Enumerator();

    public sealed class Enumerator
    {
        public int Current => 1;
        [EnforcePure]
        public bool MoveNext() => false;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Sequence values)
    {
        foreach (var value in values)
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AwaitForeachImpureDisposeAsync_Diagnostic()
        {
            var test = @"
using System.Threading;
using System.Threading.Tasks;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetAsyncEnumerator(CancellationToken cancellationToken = default) => new Enumerator();

    public sealed class Enumerator
    {
        public int Current => 1;

        [EnforcePure]
        public ValueTask<bool> MoveNextAsync() => new ValueTask<bool>(false);

        public ValueTask DisposeAsync()
        {
            GlobalState.Count++;
            return default;
        }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public async Task {|SP0002:TestMethod|}(Sequence values)
    {
        await foreach (var value in values)
        {
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
