using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class EnumOperationsTests
{
    [Test]
    public async Task EnumValueAccess_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public enum Color
{
    Red,
    Green,
    Blue
}

public class TestClass
{
    [EnforcePure]
    public string TestMethod(Color color)
    {
        // Enum.ToString() should be pure.
        return color.ToString();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumValueComparison_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public enum Status
{
    Pending,
    Active,
    Completed,
    Failed
}

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Status status)
    {
        // Pure: Enum member access is like reading a constant.
        return status == Status.Active || status == Status.Pending;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumConversion_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public enum FileAccess
{
    Read = 1,
    Write = 2,
    ReadWrite = 3
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod(FileAccess access)
    {
        return (int)access;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumParsing_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public class TestClass
{
    [EnforcePure]
    public LogLevel TestMethod(string levelName)
    {
        if (Enum.TryParse<LogLevel>(levelName, true, out var level))
            return level;
        return LogLevel.Info; // Default
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumTypeBasedParse_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public class TestClass
{
    [EnforcePure]
    public LogLevel TestMethod(string levelName)
    {
        return (LogLevel)Enum.Parse(typeof(LogLevel), levelName);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumFlagOperations_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



[Flags]
public enum Permissions
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    All = Read | Write | Execute
}

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Permissions userPermissions, Permissions requiredPermissions)
    {
        return userPermissions.HasFlag(requiredPermissions);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumGetValues_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public enum Color
{
    Red,
    Green,
    Blue
}

public class TestClass
{
    [EnforcePure]
    public Array {|SP0002:TestMethod|}()
    {
        return Enum.GetValues(typeof(Color));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumGetName_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public enum Color
{
    Red,
    Green,
    Blue
}

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(Color color)
    {
        return Enum.GetName(typeof(Color), color) ?? string.Empty;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumIsDefined_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public enum Color
{
    Red,
    Green,
    Blue
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(object value)
    {
        return Enum.IsDefined(typeof(Color), value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumWithAttributes_ReflectionBody_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.ComponentModel;



public enum ErrorCode
{
    [Description(""No error occurred"")]
    None = 0,
    
    [Description(""Invalid input provided"")]
    InvalidInput = 1,
    
    [Description(""Operation timed out"")]
    Timeout = 2
}

public class TestClass
{
    [EnforcePure]
    public string TestMethod(ErrorCode code)
    {
        var field = typeof(ErrorCode).GetField(code.ToString());
        var attr = (DescriptionAttribute)Attribute.GetCustomAttribute(
            field, typeof(DescriptionAttribute));
            
        return attr?.Description ?? code.ToString();
    }
}";


        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(23, 19, 23, 29)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}