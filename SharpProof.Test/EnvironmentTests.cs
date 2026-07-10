using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class EnvironmentTests
{
    [Test]
    public async Task Environment_ProcessorCount_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        // Environment.ProcessorCount reads ambient process state.
        return Environment.ProcessorCount;
    }
}
";


        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(8, 16, 8, 26)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task Environment_UserName_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.UserName;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_UserDomainName_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.UserDomainName;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_CurrentManagedThreadId_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.CurrentManagedThreadId;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_Is64BitOperatingSystem_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.Is64BitOperatingSystem;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_Is64BitProcess_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.Is64BitProcess;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_UserInteractive_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Environment.UserInteractive;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_SystemPageSize_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.SystemPageSize;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_WorkingSet_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Environment.WorkingSet;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_ProcessPath_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}()
    {
        return Environment.ProcessPath;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_HasShutdownStarted_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.HasShutdownStarted;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_ExitCode_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.ExitCode;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_Version_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Version {|SP0002:TestMethod|}()
    {
        return Environment.Version;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_CommandLine_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.CommandLine;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_CurrentDirectory_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.CurrentDirectory;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_CurrentDirectorySet_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(string path)
    {
        Environment.CurrentDirectory = path;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_MachineName_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.MachineName;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_OSVersion_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public OperatingSystem {|SP0002:TestMethod|}()
    {
        return Environment.OSVersion;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_TickCount_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.TickCount;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_TickCount64_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Environment.TickCount64;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_SystemDirectory_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.SystemDirectory;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_StackTrace_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.StackTrace;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_NewLine_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.NewLine;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_GetEnvironmentVariable_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}()
    {
        return Environment.GetEnvironmentVariable(""PATH"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_GetEnvironmentVariableWithTarget_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}()
    {
        return Environment.GetEnvironmentVariable(""PATH"", EnvironmentVariableTarget.Process);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_GetEnvironmentVariables_Diagnostic()
    {
        var test = @"
using System;
using System.Collections;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IDictionary {|SP0002:TestMethod|}()
    {
        return Environment.GetEnvironmentVariables();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_GetEnvironmentVariablesWithTarget_Diagnostic()
    {
        var test = @"
using System;
using System.Collections;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IDictionary {|SP0002:TestMethod|}()
    {
        return Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_ExpandEnvironmentVariables_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.ExpandEnvironmentVariables(""%PATH%"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_GetFolderPath_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_GetFolderPathWithOptions_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_Exit_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Environment.Exit(1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_SetEnvironmentVariable_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Environment.SetEnvironmentVariable(""PATH"", ""value"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_SetEnvironmentVariableWithTarget_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Environment.SetEnvironmentVariable(""PATH"", ""value"", EnvironmentVariableTarget.Process);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Environment_ProcessId_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.ProcessId;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}