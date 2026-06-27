using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class AmbientStateSoundnessStressTests
    {
        [Test]
        public async Task CancellationTokenIsCancellationRequested_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(CancellationToken token)
    {
        return token.IsCancellationRequested;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CancellationTokenRegister_Diagnostic()
        {
            var test = @"
using System;
using System.Threading;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public CancellationTokenRegistration {|PS0002:TestMethod|}(CancellationToken token)
    {
        return token.Register(() => { });
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CancellationTokenThrowIfCancellationRequested_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CancellationTokenSourceCancel_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(CancellationTokenSource source)
    {
        source.Cancel();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TaskIsCompleted_Diagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(Task task)
    {
        return task.IsCompleted;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TaskResult_Diagnostic()
        {
            var test = @"
using System.Threading.Tasks;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(Task<int> task)
    {
        return task.Result;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LazyValueRead_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(Lazy<int> lazy)
    {
        return lazy.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AsyncLocalValueRead_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(AsyncLocal<int> state)
    {
        return state.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AsyncLocalValueWrite_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(AsyncLocal<int> state, int value)
    {
        state.Value = value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ThreadCurrentThreadName_Diagnostic()
        {
            var test = @"
using System.Threading;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}()
    {
        return Thread.CurrentThread.Name ?? string.Empty;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
