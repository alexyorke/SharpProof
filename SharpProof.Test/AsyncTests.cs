using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class AsyncTests
{
    [Test]
    public async Task AsyncMethod_WithTaskDelay_Diagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public async Task<int> PureAsyncMethod()
        {
            await Task.Delay(10);
            return 42;
        }
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(10, 32, 10, 47)
                .WithArguments("PureAsyncMethod"));
    }

    [Test]
    public async Task ImpureAsyncMethod_Diagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using System.IO;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public async Task ImpureAsyncMethod()
        {
            await Task.Delay(10);
            File.WriteAllText(""temp.txt"", ""impure write"");
        }
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule).WithSpan(11, 27, 11, 44)
                .WithArguments("ImpureAsyncMethod"));
    }

    [Test]
    public async Task AsyncMethodAwaitingUnknownPurityMethod_Diagnostic()
    {
        var test = @"
using System.Threading.Tasks;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public async Task<int> MethodCallingImpureAsync()
        {
            int result = await GetValueAsync();
            return result + 1;
        }

        private async Task<int> GetValueAsync()
        {
            await Task.Delay(5);
            return 100;
        }
    }
}";


        var expectedOuter = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(10, 32, 10, 56)
            .WithArguments("MethodCallingImpureAsync");


        await VerifyCS.VerifyAnalyzerAsync(test, expectedOuter);
    }

    [Test]
    public async Task AwaitCustomAwaiterPatternMembers_Diagnostic()
    {
        var test = @"
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class ImpureAwaitable
{
    public ImpureAwaiter GetAwaiter()
    {
        GlobalState.Count++;
        return new ImpureAwaiter();
    }
}

public sealed class ImpureAwaiter : INotifyCompletion
{
    public bool IsCompleted
    {
        get
        {
            GlobalState.Count++;
            return true;
        }
    }

    public void OnCompleted(Action continuation)
    {
        GlobalState.Count++;
        continuation();
    }

    public int GetResult()
    {
        GlobalState.Count++;
        return 42;
    }
}

public class TestClass
{
    [EnforcePure]
    public async Task<int> {|SP0002:TestMethod|}()
    {
        return await new ImpureAwaitable();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AwaitCustomAwaiterImpureOnCompleted_Diagnostic()
    {
        var test = @"
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Awaitable
{
    [EnforcePure]
    public Awaiter GetAwaiter()
    {
        return new Awaiter();
    }
}

public sealed class Awaiter : INotifyCompletion
{
    public bool IsCompleted
    {
        [EnforcePure]
        get { return false; }
    }

    public void OnCompleted(Action continuation)
    {
        GlobalState.Count++;
        continuation();
    }

    [EnforcePure]
    public int GetResult()
    {
        return 42;
    }
}

public class TestClass
{
    [EnforcePure]
    public async Task<int> {|SP0002:TestMethod|}()
    {
        return await new Awaitable();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AwaitCustomAwaiterAlreadyCompletedWithImpureOnCompleted_NoDiagnostic()
    {
        var test = @"
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Awaitable
{
    [EnforcePure]
    public Awaiter GetAwaiter()
    {
        return new Awaiter();
    }
}

public sealed class Awaiter : INotifyCompletion
{
    public bool IsCompleted
    {
        [EnforcePure]
        get { return true; }
    }

    public void OnCompleted(Action continuation)
    {
        GlobalState.Count++;
        continuation();
    }

    [EnforcePure]
    public int GetResult()
    {
        return 42;
    }
}

public class TestClass
{
    [EnforcePure]
    public async Task<int> TestMethod()
    {
        return await new Awaitable();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AwaitCustomAwaiterInSeparateSyntaxTree_DoesNotCrash()
    {
        var sources = new[]
        {
            ("Usage.cs", """
                          using System.Threading.Tasks;
                          using SharpProof.Attributes;

                          public static class Usage
                          {
                              [EnforcePure]
                              public static async Task<int> {|SP0002:TestMethod|}() => await new Awaitable();
                          }
                          """),
            ("Awaitable.cs", """
                              using System;
                              using System.Runtime.CompilerServices;
                              using SharpProof.Attributes;

                              public sealed class Awaitable
                              {
                                  [EnforcePure]
                                  public Awaiter GetAwaiter() => new Awaiter();
                              }

                              public sealed class Awaiter : INotifyCompletion
                              {
                                  public bool IsCompleted { [EnforcePure] get => false; }
                                  [EnforcePure] public int GetResult() => 1;
                                  public void OnCompleted(Action continuation) => continuation();
                              }
                              """)
        };

        await VerifyCS.VerifyAnalyzerAsync(sources);
    }
}
