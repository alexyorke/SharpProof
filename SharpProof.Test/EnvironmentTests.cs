using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class EnvironmentTests
{
    public sealed record EnvironmentOperationCase(string Name, string Source);

    private static readonly EnvironmentOperationCase[] Cases =
    {
        new("Environment_UserName_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.UserName;
    }
}"),
        new("Environment_UserDomainName_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.UserDomainName;
    }
}"),
        new("Environment_CurrentManagedThreadId_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.CurrentManagedThreadId;
    }
}"),
        new("Environment_Is64BitOperatingSystem_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.Is64BitOperatingSystem;
    }
}"),
        new("Environment_Is64BitProcess_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.Is64BitProcess;
    }
}"),
        new("Environment_UserInteractive_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return Environment.UserInteractive;
    }
}"),
        new("Environment_SystemPageSize_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.SystemPageSize;
    }
}"),
        new("Environment_WorkingSet_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Environment.WorkingSet;
    }
}"),
        new("Environment_ProcessPath_Diagnostic", @"
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
}"),
        new("Environment_HasShutdownStarted_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.HasShutdownStarted;
    }
}"),
        new("Environment_ExitCode_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.ExitCode;
    }
}"),
        new("Environment_Version_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Version {|SP0002:TestMethod|}()
    {
        return Environment.Version;
    }
}"),
        new("Environment_CommandLine_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.CommandLine;
    }
}"),
        new("Environment_CurrentDirectory_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.CurrentDirectory;
    }
}"),
        new("Environment_CurrentDirectorySet_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(string path)
    {
        Environment.CurrentDirectory = path;
    }
}"),
        new("Environment_MachineName_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.MachineName;
    }
}"),
        new("Environment_OSVersion_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public OperatingSystem {|SP0002:TestMethod|}()
    {
        return Environment.OSVersion;
    }
}"),
        new("Environment_TickCount_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.TickCount;
    }
}"),
        new("Environment_TickCount64_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public long {|SP0002:TestMethod|}()
    {
        return Environment.TickCount64;
    }
}"),
        new("Environment_SystemDirectory_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.SystemDirectory;
    }
}"),
        new("Environment_StackTrace_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.StackTrace;
    }
}"),
        new("Environment_NewLine_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.NewLine;
    }
}"),
        new("Environment_GetEnvironmentVariable_Diagnostic", @"
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
}"),
        new("Environment_GetEnvironmentVariableWithTarget_Diagnostic", @"
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
}"),
        new("Environment_GetEnvironmentVariables_Diagnostic", @"
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
}"),
        new("Environment_GetEnvironmentVariablesWithTarget_Diagnostic", @"
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
}"),
        new("Environment_ExpandEnvironmentVariables_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.ExpandEnvironmentVariables(""%PATH%"");
    }
}"),
        new("Environment_GetFolderPath_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}"),
        new("Environment_GetFolderPathWithOptions_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
    }
}"),
        new("Environment_Exit_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Environment.Exit(1);
    }
}"),
        new("Environment_SetEnvironmentVariable_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Environment.SetEnvironmentVariable(""PATH"", ""value"");
    }
}"),
        new("Environment_SetEnvironmentVariableWithTarget_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        Environment.SetEnvironmentVariable(""PATH"", ""value"", EnvironmentVariableTarget.Process);
    }
}"),
        new("Environment_ProcessId_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return Environment.ProcessId;
    }
}"),
    };

    private static IEnumerable<TestCaseData> EnvironmentOperationCaseData()
    {
        if (Cases.Length != 33 ||
            Cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 33)
        {
            throw new InvalidOperationException("EnvironmentTests case invariants failed.");
        }

        return Cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(EnvironmentOperationCaseData))]
    public async Task EnvironmentOperationCaseCases(EnvironmentOperationCase testCase)
    {
        await VerifyCS.VerifyAnalyzerAsync(testCase.Source);
    }

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

































}
