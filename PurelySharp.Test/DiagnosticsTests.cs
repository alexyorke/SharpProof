using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class DiagnosticsTests
    {
        [Test]
        public async Task FileVersionInfoFileVersion_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System.Diagnostics;
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ActivitySource {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Activity? {|PS0002:TestMethod|}(ActivitySource source)
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Activity? {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Activity activity)
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Activity activity)
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DiagnosticListener {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(DiagnosticListener listener)
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
using PurelySharp.Attributes;

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
}
