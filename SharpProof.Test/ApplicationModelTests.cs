using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ApplicationModelTests
{
    [Test]
    public async Task AppContextSetSwitch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        AppContext.SetSwitch(""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"", true);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppContextSetData_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        AppContext.SetData(""TestKey"", ""value"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppContextTryGetSwitch_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return AppContext.TryGetSwitch(""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"", out _);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppContextGetData_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object? {|SP0002:TestMethod|}()
    {
        return AppContext.GetData(""APP_CONTEXT_BASE_DIRECTORY"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppContextBaseDirectory_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return AppContext.BaseDirectory;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppContextTargetFrameworkName_Diagnostic()
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
        return AppContext.TargetFrameworkName;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppDomainCurrentDomain_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public AppDomain TestMethod()
    {
        return AppDomain.CurrentDomain;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppDomainBaseDirectory_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return AppDomain.CurrentDomain.BaseDirectory;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppDomainFriendlyName_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}()
    {
        return AppDomain.CurrentDomain.FriendlyName;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AppDomainId_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return AppDomain.CurrentDomain.Id;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}