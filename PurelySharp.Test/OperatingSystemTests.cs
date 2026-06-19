using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class OperatingSystemTests
    {
        [TestCase("OperatingSystem.IsWindows()")]
        [TestCase("OperatingSystem.IsLinux()")]
        [TestCase("OperatingSystem.IsMacOS()")]
        [TestCase("OperatingSystem.IsFreeBSD()")]
        [TestCase("OperatingSystem.IsAndroid()")]
        [TestCase("OperatingSystem.IsIOS()")]
        [TestCase("OperatingSystem.IsBrowser()")]
        [TestCase("OperatingSystem.IsTvOS()")]
        [TestCase("OperatingSystem.IsWatchOS()")]
        [TestCase("OperatingSystem.IsWasi()")]
        [TestCase("OperatingSystem.IsMacCatalyst()")]
        [TestCase("OperatingSystem.IsOSPlatform(\"windows\")")]
        [TestCase("OperatingSystem.IsAndroidVersionAtLeast(1, 0, 0, 0)")]
        [TestCase("OperatingSystem.IsFreeBSDVersionAtLeast(1, 0, 0, 0)")]
        [TestCase("OperatingSystem.IsIOSVersionAtLeast(1, 0, 0)")]
        [TestCase("OperatingSystem.IsMacCatalystVersionAtLeast(1, 0, 0)")]
        [TestCase("OperatingSystem.IsMacOSVersionAtLeast(1, 0, 0)")]
        [TestCase("OperatingSystem.IsTvOSVersionAtLeast(1, 0, 0)")]
        [TestCase("OperatingSystem.IsWatchOSVersionAtLeast(1, 0, 0)")]
        public async Task OperatingSystemStaticHelpers_NoDiagnostic(string expression)
        {
            var test = $$"""
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return {{expression}};
    }
}
""";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestCase("OperatingSystem.IsOSVersionAtLeast(10, 0, 0, 0)")]
        [TestCase("OperatingSystem.IsWindowsVersionAtLeast(10, 0, 0, 0)")]
        [TestCase("OperatingSystem.IsOSPlatformVersionAtLeast(\"windows\", 10, 0, 0, 0)")]
        public async Task OperatingSystemVersionProbeHelpers_Diagnostic(string expression)
        {
            var test = $$"""
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}()
    {
        return {{expression}};
    }
}
""";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task OperatingSystemPlatform_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public PlatformID TestMethod(OperatingSystem operatingSystem)
    {
        return operatingSystem.Platform;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task OperatingSystemVersion_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Version TestMethod(OperatingSystem operatingSystem)
    {
        return operatingSystem.Version;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
