using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using PurelySharp.Attributes;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

#nullable enable

namespace PurelySharp.Test
{
    [TestFixture]
    public class RegexTests
    {


        [Test]
        public async Task Regex_IsMatch_Static_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(string input)
    {
        return Regex.IsMatch(input, ""^[a-z]+$"");
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_Match_Static_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public Match {|PS0002:TestMethod|}(string input)
    {
        return Regex.Match(input, ""^[a-z]+$"");
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_EscapeAndUnescape_Static_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(string input)
    {
        return Regex.Unescape(Regex.Escape(input));
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }














        [Test]
        public async Task Regex_Constructor_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public Regex {|PS0002:TestMethod|}()
    {
        return new Regex(""^[a-z]+$"");
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_IsMatch_Instance_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(Regex regex, string input)
    {
        return regex.IsMatch(input);
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_Match_Instance_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public Match {|PS0002:TestMethod|}(Regex regex, string input)
    {
        return regex.Match(input);
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_Replace_Static_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(string input)
    {
        return Regex.Replace(input, ""[a-z]"", ""x"");
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_Replace_Instance_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(Regex regex, string input)
    {
        return regex.Replace(input, ""x"");
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_Split_Instance_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public string[] {|PS0002:TestMethod|}(Regex regex, string input)
    {
        return regex.Split(input);
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Regex_Split_Static_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Text.RegularExpressions;

public class TestClass
{
    [EnforcePure]
    public string[] {|PS0002:TestMethod|}(string input)
    {
        return Regex.Split(input, ""[ ,;]"");
    }
}
";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
