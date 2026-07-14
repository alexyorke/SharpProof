using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class StopwatchTests
{
    [Test]
    public async Task StopwatchIsRunning_NoDiagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Stopwatch stopwatch)
    {
        return stopwatch.IsRunning;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchConstructor_NoDiagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stopwatch TestMethod()
    {
        return new Stopwatch();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchElapsed_Diagnostic()
    {
        var test = @"
using System;
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|SP0002:TestMethod|}(Stopwatch stopwatch)
    {
        return stopwatch.Elapsed;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchElapsedMilliseconds_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedMilliseconds;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchElapsedTicks_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedTicks;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchGetTimestamp_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Stopwatch.GetTimestamp();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchFrequency_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Stopwatch.Frequency;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchIsHighResolution_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Stopwatch.IsHighResolution;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchStart_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Stopwatch stopwatch)
    {
        stopwatch.Start();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StopwatchStop_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Stopwatch stopwatch)
    {
        stopwatch.Stop();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}