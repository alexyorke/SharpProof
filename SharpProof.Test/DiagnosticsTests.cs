using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class DiagnosticsTests
{
    [Test]
    public async Task FileVersionInfoFileVersion_NoDiagnostic()
    {
        var test = @"
#nullable enable
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod(FileVersionInfo fileVersionInfo)
    {
        return fileVersionInfo.FileVersion;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ActivitySourceConstructor_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ActivitySource {|SP0002:TestMethod|}()
    {
        return new ActivitySource(""test"", ""1.0.0"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ActivitySourceStartActivity_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Activity? {|SP0002:TestMethod|}(ActivitySource source)
    {
        return source.StartActivity(""request"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ActivityCurrentGetter_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Activity? {|SP0002:TestMethod|}()
    {
        return Activity.Current;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ActivityCurrentSetter_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Activity activity)
    {
        Activity.Current = activity;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ActivitySetTag_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Activity activity)
    {
        activity.SetTag(""key"", ""value"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DiagnosticListenerConstructor_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DiagnosticListener {|SP0002:TestMethod|}()
    {
        return new DiagnosticListener(""test"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DiagnosticListenerWrite_Diagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(DiagnosticListener listener)
    {
        listener.Write(""event"", 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DebugAssert_NoDiagnostic()
    {
        var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Debug.Assert(true);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}