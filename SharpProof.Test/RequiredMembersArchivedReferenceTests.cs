#if false // Inactive reference snapshot: active required-members coverage lives in RequiredMembersSmokeTests.
using NUnit.Framework;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using System.Threading.Tasks;
using SharpProof.Attributes; // Needed for EnforcePure

namespace SharpProof.Test
{
    [TestFixture]
    public class RequiredMembersArchivedReferenceTests
    {
        // Common attribute definitions needed for C# 11 required members feature in tests
        private const string AttributeDefinitions = @"
#nullable enable
// Required for required members feature
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

// Dummy IsExternalInit if not targeting .NET 5+ where it's built-in
namespace System.Runtime.CompilerServices { internal static class IsExternalInit {} }
";

        // --- Tests Expecting No Diagnostics (Pure Methods) ---

        [Test]
        public async Task ClassWithRequiredMembers_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SharpProof.Attributes;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class Person
    {
        public required string FirstName { get; init; }
        public required string LastName  { get; init; }
        
        [EnforcePure]
        public string GetFullName() // No diagnostic expected
        {
            // Reading init-only properties is pure
            return $""{FirstName} {LastName}"";
        }
    }

    public class Client
    {
        public string GetPersonInfo()
        {
            // Using object initializer with required members
            var person = new Person { FirstName = ""John"", LastName = ""Doe"" };
            return person.GetFullName(); // Calling pure method
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RecordWithRequiredMembers_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SharpProof.Attributes;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public record Person
    {
        public required string FirstName { get; init; }
        public required string LastName  { get; init; }
        
        [EnforcePure]
        public string GetFullName() // No diagnostic expected
        {
            // Reading init-only properties of record is pure
            return $""{FirstName} {LastName}"";
        }
    }

    public class Client
    {
        public string GetPersonInfo()
        {
            var person = new Person { FirstName = ""John"", LastName = ""Doe"" };
            return person.GetFullName();
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StructWithRequiredMembers_PureMethod_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public struct Point
    {
        public required double X { get; init; }
        public required double Y { get; init; }
        
        [EnforcePure]
        public double CalculateDistance() // No diagnostic expected
        {
            // Reading struct init-only properties and calling Math.Sqrt is pure
            return Math.Sqrt(X * X + Y * Y);
        }
    }

    public class GeometryCalculator
    {
        public double CalculatePointDistance()
        {
            var point = new Point { X = 3.0, Y = 4.0 };
            return point.CalculateDistance();
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ClassWithRequiredFields_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class Configuration
    {
        // Note: Required fields are less common than properties
        public required string ApiKey;
        public required string ApiEndpoint;
        
        [EnforcePure]
        public string GetConfigSummary() // No diagnostic expected
        {
            // Reading fields and calling string methods is pure
            return $""API Key: {ApiKey.Substring(0, 3)}***, Endpoint: {ApiEndpoint}"";
        }
    }

    public class ApiClient
    {
        public string GetConfigInfo()
        {
            var config = new Configuration
            {
                ApiKey      = ""abc123xyz456"",
                ApiEndpoint = ""https://api.example.com""
            };
            return config.GetConfigSummary();
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public Task RequiredMembersWithNullCheck_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class Document
    {
        public required string Title       { get; init; }
        public string?        Description { get; init; } // Nullable optional member
        
        [EnforcePure]
        public string GetSummary() // No diagnostic expected
        {
            // Null-coalescing operator on pure reads is pure
            return $""Document: {Title}, {Description ?? ""No description provided""}"";
        }
    }

     public class DocumentManager
    {
        public string GetDocumentInfo()
        {
            var doc = new Document
            {
                Title = ""Annual Report""
                // Description is optional, defaults to null
            };
            return doc.GetSummary();
        }
    }
}";
            // Expect no diagnostic
            return VerifyCS.VerifyAnalyzerAsync(test);
        }


        [Test]
        public async Task RequiredMembers_ReadingInPureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class Config
    {
        public required string ApiKey      { get; init; }
        public required string ApiEndpoint { get; init; }
        
        [EnforcePure]
        public string GetFullEndpoint() // No diagnostic expected
        {
            // String interpolation with pure reads is pure
            return $""{ApiEndpoint}?key={ApiKey}"";
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_InRecord_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public record User
    {
        public required string Name  { get; init; }
        public required string Email { get; init; }
        
        // Constructor satisfying required members
        [SetsRequiredMembers]
        public User(string name, string email)
        {
            Name  = name;
            Email = email;
        }
        
        [EnforcePure]
        public string GetUserInfo() // No diagnostic expected
        {
            return $""{Name} ({Email})"";
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_WithStructs_PureMethod_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public struct Point
    {
        public required int X { get; init; }
        public required int Y { get; init; }
        
        [EnforcePure]
        public double GetDistance() // No diagnostic expected
        {
            return Math.Sqrt(X * X + Y * Y);
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RequiredMembers_WithPrimaryConstructor_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    // Using C# 12 primary constructor with required members
    public class Product(string name, decimal price)
    {
        public required string  Name  { get; init; } = name;
        public required decimal Price { get; init; } = price;
        
        [EnforcePure]
        public string GetFormattedPrice() // No diagnostic expected
        {
            // String formatting is pure
            return $""{Name}: {Price:C2}""; // Use C2 for currency format
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }


        [Test]
        public async Task RequiredMembers_MultipleTypes_PureMethod_NoDiagnostic()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public record Settings
    {
        public required int TimeoutMs { get; init; }
    }

    public struct Coordinates
    {
        public required double Latitude  { get; init; }
        public required double Longitude { get; init; }
    }

    public class AppConfiguration
    {
        public required Settings    AppSettings     { get; init; }
        public required Coordinates DefaultLocation { get; init; }

        [EnforcePure]
        public string GetSummary() // No diagnostic expected
        {
            // Accessing members of nested required types is pure
            return $""Timeout: {AppSettings.TimeoutMs}ms, Location: ({DefaultLocation.Latitude}, {DefaultLocation.Longitude})"";
        }
    }
}";
            // Expect no diagnostic
            await VerifyCS.VerifyAnalyzerAsync(test);
        }


        // --- Tests Expecting Diagnostics (Impure Methods) ---

        [Test]
        public async Task ClassWithRequiredMembers_ImpureMethod()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.IO; // For File.WriteAllText
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class UserProfile
    {
        private static int _lastId = 0; // Static field access/modification is impure
        public required string Username { get; init; }
        public required string Email    { get; init; }
        
        [EnforcePure]
        public int GenerateUniqueId()
        {
            // Guid generation relies on system state/entropy
            return Guid.NewGuid().GetHashCode(); 
        }
        
        [EnforcePure]
        public void SaveProfile()
        {
            _lastId++; // Modifying static state
            // File I/O is impure
            File.WriteAllText($""{_lastId}.json"", $""{Username} - {Email}""); 
        }
    }
}";
            // Expect diagnostics SP0002 for GenerateUniqueId and SaveProfile.
            var diag1 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule) // Expect SP0002
                .WithLocation(42, 20) // Adjusted line number from error output
                .WithArguments("GenerateUniqueId"); // Argument is just the method name for SP0002
            var diag2 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule) // Expect SP0002
                .WithLocation(49, 21) // Adjusted line number from error output
                .WithArguments("SaveProfile"); // Argument is just the method name for SP0002

            await VerifyCS.VerifyAnalyzerAsync(test, diag1, diag2); // Expect 2 diagnostics now
        }


        [Test]
        public async Task MutableRequiredProperties_ImpureMethod()
        {
            var test = @"
#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SharpProof.Attributes;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class Counter
    {
        // Mutable property, even if required
        public required int Count { get; set; } 

        [EnforcePure]
        public void Increment()
        {
            {|SP0002:Count++|};
        }
    }

    public class Client
    {
        public void UseCounter()
        {
            var counter = new Counter { Count = 0 };
            counter.Increment(); // Call site is fine, method itself is impure
        }
    }
}";
            // Expect diagnostic SP0002 for Increment.
            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule) // Expect SP0002
                .WithLocation(40, 21) // Adjusted line number from error output
                .WithArguments("Increment"); // Argument is just the method name for SP0002

            await VerifyCS.VerifyAnalyzerAsync(test, expected); // Expect 1 diagnostic
        }


        [Test]
        public async Task MutableInitOnlyRequiredProperties_ImpureMethod()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class Product
    {
        public required int    Id    { get; init; } // Init-only, pure to read
        public required string Name  { get; init; } // Init-only, pure to read
        public required decimal Price { get; init; } // Init-only, pure to read

        [SetsRequiredMembers]
        public Product(int id, string name, decimal price)
        {
            Id    = id;
            Name  = name;
            Price = price;
        }

        // This method itself is pure as it only reads init-only props
        [EnforcePure] 
        public string GetProductSummary() 
        {
            return $""{Name} (ID: {Id}) - {Price:C}"";
        }
    }

    public class ProductManager
    {
        // This method is impure due to Console.WriteLine
        [EnforcePure]
        public void UpdateProductName(Product product, string newName)
        {
            // Property set is allowed in init-only, but Console.WriteLine is impure
            Console.WriteLine($""Updating name to {newName}"");
            // product.Name = newName; // Commented out: Allowed in init, but causes CS8852
        }

        public string GetProductSummary(Product product)
        {
            // Instantiation is fine
            var p = new Product(1, ""Sample"", 9.99m);
            // Calling the pure method is fine
            return p.GetProductSummary();
        }
    }
}
";
            // Expect diagnostic SP0002 for UpdateProductName.
            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule) // Expect SP0002
                .WithLocation(60, 21) // Adjusted line number from error output
                .WithArguments("UpdateProductName"); // Argument is just the method name for SP0002

            await VerifyCS.VerifyAnalyzerAsync(test, expected); // Expect 1 diagnostic for Console.WriteLine
        }


        [Test]
        public async Task RequiredMembers_TryingToModify_Diagnostic()
        {
            var test = @"
#nullable enable
using System;
using SharpProof.Attributes;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
" + AttributeDefinitions + @"

namespace TestNamespace
{
    public class UserProfile
    {
        public required string Username { get; init; } // Init-only, pure to read
        public int Age { get; set; } // Mutable property

        [SetsRequiredMembers]
        public UserProfile(string username)
        {
            Username = username;
            // Age is not required, defaults
        }

        // Impure: Modifies instance state 'Age'
        [EnforcePure]
        public void UpdateAge(int newAge)
        {
            {|SP0002:this.Age = newAge|};
        }

        // Pure: Reads init-only 'Username' and mutable 'Age'
        // Reading mutable state is considered pure if not modified within the method
        [EnforcePure]
        public string GetProfileInfo() // No diagnostic expected
        {
            return $""User: {Username}, Age: {Age}"";
        }
    }
}";
            // Expect diagnostic SP0002 for UpdateAge.
            var expectedSP0002 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule) // Expect SP0002
                .WithLocation(48, 21) // Span of 'UpdateAge' identifier
                .WithArguments("UpdateAge");
            
            await VerifyCS.VerifyAnalyzerAsync(test, expectedSP0002); // Expect 1 diagnostic
        }
    }
}
#endif // Inactive reference snapshot retained for future required-members comparisons.

