using System.Threading.Tasks;
using PurelySharp.Attributes;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class RequiredMembersSmokeTests
    {
        [Test]
        public async Task RequiredInitOnlyProperties_ReportPureGetterSuggestions()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public class Person
{
    public required string {|PS0004:FirstName|} { get; init; }
    public required string {|PS0004:LastName|} { get; init; }

    [EnforcePure]
    public string GetFullName()
    {
        return $""{FirstName} {LastName}"";
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_ObjectInitializerInPureMethod_NoExtraDiagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public class Person
{
    public required string {|PS0004:FirstName|} { get; init; }
    public required string {|PS0004:LastName|} { get; init; }

    [EnforcePure]
    public string GetFullName()
    {
        return $""{FirstName} {LastName}"";
    }
}

public class Client
{
    [EnforcePure]
    public string BuildDisplayName()
    {
        var person = new Person
        {
            FirstName = ""John"",
            LastName = ""Doe"",
        };

        return person.GetFullName();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MutableRequiredProperty_ReportsGetterSuggestionAndImpureMethod()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public class Counter
{
    public required int {|PS0004:Count|} { get; set; }

    [EnforcePure]
    public void {|PS0002:Increment|}()
    {
        Count++;
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredFields_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public class Configuration
{
    public required string ApiKey;
    public required string ApiEndpoint;

    [EnforcePure]
    public string GetConfigSummary()
    {
        return $""API Key: {ApiKey.Substring(0, 3)}***, Endpoint: {ApiEndpoint}"";
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredFields_ObjectInitializerInPureMethod_NoExtraDiagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public class Configuration
{
    public required string ApiKey;
    public required string ApiEndpoint;

    [EnforcePure]
    public string GetConfigSummary()
    {
        return $""API Key: {ApiKey.Substring(0, 3)}***, Endpoint: {ApiEndpoint}"";
    }
}

public class Client
{
    [EnforcePure]
    public string BuildConfigSummary()
    {
        var config = new Configuration
        {
            ApiKey = ""abc123xyz456"",
            ApiEndpoint = ""https://api.example.com"",
        };

        return config.GetConfigSummary();
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_OnRecord_ReportPureGetterSuggestions()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public record Person
{
    public required string {|PS0004:FirstName|} { get; init; }
    public required string {|PS0004:LastName|} { get; init; }

    [EnforcePure]
    public string GetFullName()
    {
        return $""{FirstName} {LastName}"";
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_WithPrimaryConstructor_ReportPureGetterSuggestions()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public class Product(string name, decimal price)
{
    public required string {|PS0004:Name|} { get; init; } = name;
    public required decimal {|PS0004:Price|} { get; init; } = price;

    [EnforcePure]
    public string GetFormattedPrice()
    {
        return $""{Name}: {Price}"";
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_WithSetsRequiredMembersConstructor_ReportPureSuggestions()
        {
            var test = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;
using PurelySharp.Attributes;

namespace TestNamespace;

public record User
{
    public required string {|PS0004:Name|} { get; init; }
    public required string {|PS0004:Email|} { get; init; }

    [SetsRequiredMembers]
    public User(string name, string email)
    {
        Name = name;
        Email = email;
    }

    [EnforcePure]
    public string GetUserInfo()
    {
        return $""{Name} ({Email})"";
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_WithNullCheck_ReportPureGetterSuggestions()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public class Document
{
    public required string {|PS0004:Title|} { get; init; }
    public string? {|PS0004:Description|} { get; init; }

    [EnforcePure]
    public string GetSummary()
    {
        return $""Document: {Title}, {Description ?? ""No description provided""}""; 
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_ModifyingMutableProperty_ReportsExpectedDiagnostics()
        {
            var test = @"
#nullable enable
using System.Diagnostics.CodeAnalysis;
using PurelySharp.Attributes;

namespace TestNamespace;

public class UserProfile
{
    public required string {|PS0004:Username|} { get; init; }
    public int {|PS0004:Age|} { get; set; }

    [SetsRequiredMembers]
    public UserProfile(string username)
    {
        Username = username;
    }

    [EnforcePure]
    public void {|PS0002:UpdateAge|}(int newAge)
    {
        Age = newAge;
    }

    [EnforcePure]
    public string GetProfileInfo()
    {
        return $""User: {Username}, Age: {Age}"";
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_WithImpureMethods_ReportExpectedDiagnostics()
        {
            var test = @"
#nullable enable
using System;
using System.IO;
using PurelySharp.Attributes;

namespace TestNamespace;

public class UserProfile
{
    private static int _lastId = 0;

    public required string {|PS0004:Username|} { get; init; }
    public required string {|PS0004:Email|} { get; init; }

    [EnforcePure]
    public int {|PS0002:GenerateUniqueId|}()
    {
        return Guid.NewGuid().GetHashCode();
    }

    [EnforcePure]
    public void {|PS0002:SaveProfile|}()
    {
        _lastId++;
        File.WriteAllText($""{_lastId}.json"", $""{Username} - {Email}"");
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_NestedTypes_ReportPureGetterSuggestions()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

namespace TestNamespace;

public record Settings
{
    public required int {|PS0004:TimeoutMs|} { get; init; }
}

public struct Coordinates
{
    public required double {|PS0004:Latitude|} { get; init; }
    public required double {|PS0004:Longitude|} { get; init; }
}

public class AppConfiguration
{
    public required Settings {|PS0004:AppSettings|} { get; init; }
    public required Coordinates {|PS0004:DefaultLocation|} { get; init; }

    [EnforcePure]
    public string GetSummary()
    {
        return $""Timeout: {AppSettings.TimeoutMs}ms, Location: ({DefaultLocation.Latitude}, {DefaultLocation.Longitude})"";
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_InitOnlyProduct_WithImpureUpdater_ReportsExpectedDiagnostics()
        {
            var test = @"
#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using PurelySharp.Attributes;

namespace TestNamespace;

public class Product
{
    public required int {|PS0004:Id|} { get; init; }
    public required string {|PS0004:Name|} { get; init; }
    public required decimal {|PS0004:Price|} { get; init; }

    [SetsRequiredMembers]
    public Product(int id, string name, decimal price)
    {
        Id = id;
        Name = name;
        Price = price;
    }

    [EnforcePure]
    public string {|PS0002:GetProductSummary|}()
    {
        return $""{Name} (ID: {Id}) - {Price:C}"";
    }
}

public class ProductManager
{
    [EnforcePure]
    public void {|PS0002:UpdateProductName|}(Product product, string newName)
    {
        Console.WriteLine($""Updating name to {newName}"");
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_OnStruct_ReportPureGetterSuggestions()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

namespace TestNamespace;

public struct Point
{
    public required double {|PS0004:X|} { get; init; }
    public required double {|PS0004:Y|} { get; init; }

    [EnforcePure]
    public double CalculateDistance()
    {
        return Math.Sqrt(X * X + Y * Y);
    }
}
";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
