using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ConcurrencyOperationsTests
    {
        [Test]
        public async Task MethodWithLockStatement_Diagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    private readonly object _lock = new object();

    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        lock (_lock) // Lock statement is impure
        {
            // Some operation
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithEventSubscription_Diagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    public event EventHandler MyEvent;

    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        MyEvent += (s, e) => { }; // Event subscription is impure
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithDelegateInvocation_Diagnostic()
        {
            var testCode = @"
using System;
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    private Action _impureAction = () => Console.WriteLine(); // Impure delegate target

    [EnforcePure]
    public void TestMethod()
    {
        // Invoking a delegate whose target is impure
        _impureAction();
    }
}
";


            var expectedDiagnostic = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId).WithSpan(11, 17, 11, 27).WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(testCode, expectedDiagnostic);
        }

        [Test]
        public async Task LockImpurityDetection_Diagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;



public class TestClass
{
    private readonly object _lock = new object();

    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        lock (_lock) // Lock statement is impure -- Moved diagnostic to lock keyword
        {
            // Some operation
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithInterlockedIncrement_Diagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading;



public class TestClass
{
    private static int _counter;

    [EnforcePure]
    public void {|PS0002:TestMethod|}()
    {
        Interlocked.Increment(ref _counter); // Impure atomic operation
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithInterlockedCompareExchange_Diagnostic()
        {

            var test = @"
using System;
using PurelySharp.Attributes;
using System.Threading;



public class TestClass
{
    private static int _value;

    [EnforcePure]
    public void {|PS0002:TestMethod|}(int newValue, int comparand)
    {
        Interlocked.CompareExchange(ref _value, newValue, comparand); // Impure atomic operation
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ThreadLocalConstructor_Diagnostic()
        {
            var test = @"
using System;
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ThreadLocal<int> {|PS0002:TestMethod|}()
    {
        return new ThreadLocal<int>(() => 42);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ThreadLocalValueRead_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(ThreadLocal<int> state)
    {
        return state.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SemaphoreConstructor_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Semaphore {|PS0002:TestMethod|}()
    {
        return new Semaphore(0, 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MutexReleaseMutex_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Mutex mutex)
    {
        mutex.ReleaseMutex();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LazyConstructor_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Lazy<int> {|PS0002:TestMethod|}()
    {
        return new Lazy<int>(() => 42);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LazyInitializerEnsureInitialized_Diagnostic()
        {
            var test = @"
using System;
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(ref string value)
    {
        return LazyInitializer.EnsureInitialized(ref value, () => ""ready"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ChannelCreateUnbounded_Diagnostic()
        {
            var test = @"
using System.Threading.Channels;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Channel<int> {|PS0002:TestMethod|}()
    {
        return Channel.CreateUnbounded<int>();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ChannelReaderReadAsync_Diagnostic()
        {
            var test = @"
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ValueTask<int> {|PS0002:TestMethod|}(ChannelReader<int> reader, CancellationToken token)
    {
        return reader.ReadAsync(token);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ChannelWriterWriteAsync_Diagnostic()
        {
            var test = @"
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ValueTask {|PS0002:TestMethod|}(ChannelWriter<int> writer, CancellationToken token)
    {
        return writer.WriteAsync(1, token);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }









    }
}


