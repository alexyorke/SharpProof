using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

#nullable enable

namespace PurelySharp.Test
{
    [TestFixture]
    public class ApplicationModelTests
    {
        [Test]
        public async Task AppContextSetSwitch_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public object? {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}()
    {
        return AppContext.BaseDirectory;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AppContextTargetFrameworkName_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod()
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
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

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
}
