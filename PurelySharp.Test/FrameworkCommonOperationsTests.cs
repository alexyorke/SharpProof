using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Configuration;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using PurelySharp.Attributes;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class FrameworkCommonOperationsTests
    {

        [Test]
        public async Task GUI_SetButtonContent_Diagnostic()
        {
            var test = @"
#nullable enable // To handle EventHandler? warning
using System;
using System.Threading.Tasks;
using MockFramework;
using PurelySharp.Attributes;

namespace MockFramework
{
    public class Button { public string Content { get; set; } = """"; public event System.EventHandler? Click; }
    public class TextBox { public string Text { get; set; } = """"; } // Keep
    public class MessageBox { public static void Show(string text) {} } // Keep
}



public class TestClass
    {
        [EnforcePure]
        public void UpdateUI(Button button)
        {
            button.Content = ""Clicked""; // Impure: UI Side Effect.
        }
}";

            var expectedGetContent = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(10, 41, 10, 48).WithArguments("get_Content");
            var expectedGetText = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 42, 11, 46).WithArguments("get_Text");
            var expectedShow = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(12, 50, 12, 54).WithArguments("Show");
            var expectedUpdateUI = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(20, 21, 20, 29).WithArguments("UpdateUI");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetContent, expectedGetText, expectedShow, expectedUpdateUI });
        }

        [Test]
        public async Task GUI_GetTextBoxText_ReportsMockMemberDiagnosticsOnly()
        {
            var test = @"
#nullable enable
using System;
using System.Threading.Tasks;
using MockFramework;
using PurelySharp.Attributes;

namespace MockFramework
{
    public class Button { public string Content { get; set; } = """"; public event System.EventHandler? Click; } // Keep
    public class TextBox { public string Text { get; set; } = """"; }
    public class MessageBox { public static void Show(string text) {} } // Keep
}



public class TestClass
{
    [EnforcePure]
    public string GetInput(TextBox textBox)
    {
        return textBox.Text; // Intended as a pure read; expect PS0004 only on the mock members.
    }
}";

            var expectedGetContent = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(10, 41, 10, 48).WithArguments("get_Content");
            var expectedGetText = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 42, 11, 46).WithArguments("get_Text");
            var expectedShow = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(12, 50, 12, 54).WithArguments("Show");
            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetContent, expectedGetText, expectedShow);
        }




        [Test]
        public async Task PureMethod_ReadConfiguration_UnannotatedInterfaceReadsDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using MockFramework;
using PurelySharp.Attributes;

namespace MockFramework 
{
    // Simplified mock for IConfiguration
    public interface IConfiguration { string? this[string key] { get; } IConfigurationSection GetSection(string key); }
    public interface IConfigurationSection { string? Value { get; } }
    public class MockConfigurationSection : IConfigurationSection { public string? Value => ""someValue""; }
    public class MockConfiguration : IConfiguration 
    {
        public string? this[string key] => key == ""MyKey:MyValue"" ? ""someValue"" : null;
        public IConfigurationSection GetSection(string key) => new MockConfigurationSection();
    } 
}



public class TestClass
{
    [EnforcePure]
    public string? {|PS0002:ReadConfigIndexer|}(IConfiguration config)
    {
        return config[""MyKey:MyValue""]; // Unannotated interface dispatch remains conservative.
    }

    [EnforcePure]
    public string? {|PS0002:ReadConfigGetSection|}(IConfiguration config)
    {
        return config.GetSection(""MyKey"").Value; // Unannotated interface dispatch remains conservative.
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethod_ReadVirtualPropertyThroughStableConcreteLocal_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public abstract class BaseValue
{
    public abstract int Value { get; }
}

public sealed class PureValue : BaseValue
{
    public override int Value => 42;
}

public class TestClass
{
    [EnforcePure]
    public int ReadValue()
    {
        BaseValue value;
        value = new PureValue();
        return value.Value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethod_ReadConfigurationManagerAppSettings_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Configuration;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|PS0002:ReadAppSetting|}()
    {
        return ConfigurationManager.AppSettings[""MyKey""];
    }
}";

            var verifier = new VerifyCS.Test
            {
                TestCode = test,
            };

            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));
            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.PureAttribute).Assembly.Location));
            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(ConfigurationManager).Assembly.Location));

            await verifier.RunAsync();
        }

        [Test]
        public async Task PureMethod_ReadConfigurationManagerConnectionStrings_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Configuration;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConnectionStringSettingsCollection {|PS0002:ReadConnectionStrings|}()
    {
        return ConfigurationManager.ConnectionStrings;
    }
}";

            var verifier = new VerifyCS.Test
            {
                TestCode = test,
            };

            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));
            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.PureAttribute).Assembly.Location));
            verifier.TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(ConfigurationManager).Assembly.Location));

            await verifier.RunAsync();
        }














































    }
}
