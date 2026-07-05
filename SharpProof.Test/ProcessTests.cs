using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ProcessTests
    {
        [Test]
        public async Task ProcessGetCurrentProcess_Diagnostic()
        {
            var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Process {|SP0002:TestMethod|}()
    {
        return Process.GetCurrentProcess();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ProcessId_Diagnostic()
        {
            var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(Process process)
    {
        return process.Id;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ProcessStartInfo_Diagnostic()
        {
            var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ProcessStartInfo {|SP0002:TestMethod|}(Process process)
    {
        return process.StartInfo;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ProcessExitCode_Diagnostic()
        {
            var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(Process process)
    {
        return process.ExitCode;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ProcessStartString_Diagnostic()
        {
            var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Process {|SP0002:TestMethod|}()
    {
        return Process.Start(""tool"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ProcessGetProcessesByName_Diagnostic()
        {
            var test = @"
using System.Diagnostics;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Process[] {|SP0002:TestMethod|}()
    {
        return Process.GetProcessesByName(""dotnet"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
