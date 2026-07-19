using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class ExtendedNameofScopeTests
{
    [Test]
    public async Task ExtendedNameofScope_ThrowExpression_ReportsDiagnostics()
    {
        var test = @"
#nullable enable
using System;
using System.Linq.Expressions;
using SharpProof.Attributes;
using System.Reflection;
using System.ComponentModel.DataAnnotations;

public class MyModel
{
    public string? Name { get; set; } // SP0004 expected (get/set)
}

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:GetPropertyName|}<T>(Expression<Func<T>> propertyLambda)
    {
        MemberExpression member = propertyLambda.Body as MemberExpression ?? throw new ArgumentException();
        return member.Member.Name;
    }

    // Example usage propagates the throw impurity from GetPropertyName.
    [EnforcePure] 
    public string {|SP0002:GetNamePropertyName|}()
    {
        return GetPropertyName(() => new MyModel().Name);
    }
}";

        var expectedGetName = VerifyCS.Diagnostic("SP0004").WithSpan(11, 20, 11, 24)
            .WithArguments("get_Name");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetName);
    }

    [Test]
    public async Task ExtendedNameofScopeWithTypeParameter_PureMethod_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TypeHelper<T>
    {
        [EnforcePure]
        public string GetTypeName()
        {
            // C# 11 feature: Extended nameof scope can access type parameter T
            return nameof(T);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ExtendedNameofScopeWithMethodParameter_PureMethod_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class ParameterHelper
    {
        [EnforcePure]
        public string GetParameterName<TParam>(TParam parameter)
        {
            // C# 11 feature: Extended nameof scope can access method parameters
            return nameof(parameter) + "" of type "" + nameof(TParam);
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ExtendedNameofScopeWithLocalFunction_HelperDiagnosticOnly()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static string GetInfo(string info) => info; // SP0004 expected

    [EnforcePure]
    public string GetFunctionInfo()
    {
        [EnforcePure]
        string LocalFunction(string msg) => GetInfo(msg); // Pure local function
        
        return nameof(LocalFunction);
    }
}";

        var expectedGetInfo = VerifyCS.Diagnostic("SP0004").WithSpan(7, 27, 7, 34)
            .WithArguments("GetInfo");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetInfo);
    }

    [Test]
    public async Task ExtendedNameofScopeWithLambda_PureMethod_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class LambdaHelper
    {
        [EnforcePure]
        public string GetLambdaName()
        {
            // C# 11 feature: Extended nameof scope can access lambda expressions
            var lambda = (int x) => x * x;
            
            return nameof(lambda);
        }
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ExtendedNameofScopeWithRangeVariables_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;
using System.Collections.Generic;

namespace TestNamespace
{
    public class QueryHelper
    {
        [EnforcePure]
        public List<string> GetRangeVariableNames(List<int> numbers)
        {
            // C# 11 feature: Extended nameof scope can access range variables
            return numbers
                .Select(n => nameof(n))
                .ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("SP0002").WithSpan(12, 29, 12, 50)
                .WithArguments("GetRangeVariableNames"));
    }

    [Test]
    public async Task ExtendedNameofScopeWithPatternVariables_PureMethod_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class PatternHelper
    {
        [EnforcePure]
        public string GetPatternVariableName(object value)
        {
            // C# 11 feature: Extended nameof scope can access pattern variables
            if (value is int number)
            {
                return nameof(number);
            }
            
            return ""Not an int"";
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ExtendedNameofScopeImpureMethod_Diagnostic()
    {
        var test = @"
using System;
using System.IO;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class Logger
    {
        private string logFile;

        [EnforcePure]
        public void LogParameterName(string message)
        {
            // Impure operation: field assignment using nameof
            logFile = ""Log for parameter: "" + nameof(message);

            // Impure operation: file system access
            File.AppendAllText(logFile, message);
        }
    }
}
";


        await VerifyCS.VerifyAnalyzerAsync(test,
            VerifyCS.Diagnostic("SP0002").WithSpan(13, 21, 13, 37).WithArguments("LogParameterName"));
    }
}