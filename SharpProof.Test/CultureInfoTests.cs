using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class CultureInfoTests
    {
        [Test]
        public async Task CultureInfoCurrentCulture_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo {|SP0002:TestMethod|}()
    {
        return CultureInfo.CurrentCulture;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CultureInfoCurrentUICulture_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo {|SP0002:TestMethod|}()
    {
        return CultureInfo.CurrentUICulture;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CultureInfoInstalledUICulture_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo {|SP0002:TestMethod|}()
    {
        return CultureInfo.InstalledUICulture;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CultureInfoDefaultThreadCurrentCulture_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo? {|SP0002:TestMethod|}()
    {
        return CultureInfo.DefaultThreadCurrentCulture;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CultureInfoDefaultThreadCurrentUICulture_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo? {|SP0002:TestMethod|}()
    {
        return CultureInfo.DefaultThreadCurrentUICulture;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CultureInfoGetCultureInfo_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo {|SP0002:TestMethod|}()
    {
        return CultureInfo.GetCultureInfo(""en-US"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CultureInfoName_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(CultureInfo culture)
    {
        return culture.Name;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RegionInfoCurrentRegion_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public RegionInfo {|SP0002:TestMethod|}()
    {
        return RegionInfo.CurrentRegion;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NumberFormatInfoCurrentInfo_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public NumberFormatInfo {|SP0002:TestMethod|}()
    {
        return NumberFormatInfo.CurrentInfo;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DateTimeFormatInfoCurrentInfo_Diagnostic()
        {
            var test = @"
using System.Globalization;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeFormatInfo {|SP0002:TestMethod|}()
    {
        return DateTimeFormatInfo.CurrentInfo;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
