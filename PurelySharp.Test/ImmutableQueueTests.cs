using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ImmutableQueueTests
    {
        [Test]
        public async Task ImmutableQueueEnqueue_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableQueue<int> EnqueueValue(ImmutableQueue<int> queue, int value)
    {
        return queue.Enqueue(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableQueueClear_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableQueue<int> ClearQueue(ImmutableQueue<int> queue)
    {
        return queue.Clear();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
