using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System;

#nullable enable

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class DataAnnotationsTests
    {

        public class PureModel
        {
            [Required(ErrorMessage = "Name is required")]
            public string? Name { get; set; }

            [System.ComponentModel.DataAnnotations.Range(0, 100, ErrorMessage = "Value must be between 0 and 100")]
            public int Value { get; set; }
        }

        public class ImpureValidationAttribute : ValidationAttribute
        {
            protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
            {
                Console.WriteLine("Performing impure validation...");
                return ValidationResult.Success;
            }
        }

        public class ImpureModel
        {
            [ImpureValidation]
            public string? Data { get; set; }
        }

        public class MyValidatableObject : IValidatableObject
        {
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }

            [EnforcePure]
            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {

                if (EndDate < StartDate)
                {
                    yield return new ValidationResult("End date must be after start date.");
                }
            }
        }

        public class MyObject { public string? DisplayName { get; set; } }



        [Test]
        public async Task AttributeConstructors_ReportsMissingAttributeAndPurityDiagnostics()
        {
            var test = @"
#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using SharpProof.Attributes;

// Define attribute used (even if simple)
// Keep only one CS8618 markup pair for ErrorMessage
public class MyLengthAttribute : StringLengthAttribute { public MyLengthAttribute(int len): base(len) {} public string ErrorMessage { get; } public int MinimumLength { get; } }

public class TestClass
{
    [EnforcePure]
    public ValidationAttribute[] TestMethod() // Line 13 in test string
    {
        // Expect SP0004 on the custom attribute members and SP0002 on the wrapper method.
        return new ValidationAttribute[]
        {
            new RequiredAttribute(),
            new MyLengthAttribute(10) // Use defined attribute
        };
    }
}";

            var expectedCS8618 = DiagnosticResult.CompilerError("CS8618")
                                     .WithSpan(9, 65, 9, 82).WithSpan(9, 120, 9, 132)
                                     .WithArguments("property", "ErrorMessage");
            var expectedSP0004GetterErr = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                           .WithSpan(9, 120, 9, 132)
                                           .WithArguments("get_ErrorMessage");
            var expectedSP0004GetterMin = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                           .WithSpan(9, 153, 9, 166)
                                           .WithArguments("get_MinimumLength");
            var expectedSP0002 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                                  .WithSpan(14, 34, 14, 44)
                                  .WithArguments("TestMethod");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] {
                expectedCS8618,
                expectedSP0004GetterErr,
                expectedSP0004GetterMin,
                expectedSP0002
            });
        }


        [Test]
        public async Task Validator_TryValidateObject_PureAttributes_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SharpProof.Attributes;

public class PureRangeAttribute : ValidationAttribute
{
    public int Min { get; } // SP0004 expected
    public int Max { get; } // SP0004 expected
    public PureRangeAttribute(int min, int max) { Min = min; Max = max; } // SP0004 expected

    [EnforcePure]
    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        if (value is int i && i >= Min && i <= Max) return ValidationResult.Success;
        return new ValidationResult(""Value out of range."");
    }
}

public class MyModel
{
    [PureRange(0, 100)]
    public int Value { get; set; } // SP0004 expected (get/set)
}

public class TestRunner
{
    [EnforcePure]
    public bool TestMethod(MyModel model)
    {
        var context = new ValidationContext(model, null, null);
        var results = new List<ValidationResult>();
        // Validator.TryValidateObject is assumed impure
        bool isValid = Validator.TryValidateObject(model, context, results, true);
        return isValid;
    }
}
";

            var expectedGetMin = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(9, 16, 9, 19).WithArguments("get_Min");
            var expectedGetMax = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(10, 16, 10, 19).WithArguments("get_Max");
            var expectedGetValue = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(24, 16, 24, 21).WithArguments("get_Value");
            var expectedTestMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(30, 17, 30, 27).WithArguments("TestMethod");


            await VerifyCS.VerifyAnalyzerAsync(test, new[] {
                expectedGetMin,
                expectedGetMax,
                expectedGetValue,
                expectedTestMethod
            });
        }

        [Test]
        public async Task IValidatableObject_Validate_IsPure()
        {
            var test = @"
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SharpProof.Attributes;

public class MyValidatableObject : IValidatableObject // Line 7
{
    public DateTime StartDate { get; set; } // Line 9
    public DateTime EndDate { get; set; } // Line 10

    [EnforcePure] // Should still be flagged as SP0002 if Validate becomes impure
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Pure validation logic
        if (EndDate < StartDate)
        {
            yield return new ValidationResult(""End date must be after start date."");
        }
    }
}
";


            var expectedGetStart = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(10, 21, 10, 30).WithArguments("get_StartDate");

            var expectedGetEnd = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(11, 21, 11, 28).WithArguments("get_EndDate");


            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetStart, expectedGetEnd);
        }

        [Test]
        public async Task ValidationContext_Items_IsImpure()
        {
            var test = @"
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SharpProof.Attributes;

public class MyObject { public string DisplayName { get; set; } } // Line 7

public class TestClass
{
    [EnforcePure] // Will be flagged because ValidationContext.Items is Dictionary (impure)
    public bool TestMethod(MyObject instance) // Line 11
    {
        var context = new ValidationContext(instance);
        context.Items[""key""] = ""value""; // Modifying Items dictionary is impure
        return true;
    }
}
";
            var expectedSP0002 = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId).WithSpan(13, 17, 13, 27).WithArguments("TestMethod");
            var expectedGetDisplay = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId).WithSpan(8, 39, 8, 50).WithArguments("get_DisplayName");
            var compilerError = DiagnosticResult.CompilerError("CS8618").WithSpan(8, 39, 8, 50).WithSpan(8, 39, 8, 50).WithArguments("property", "DisplayName");

            await VerifyCS.VerifyAnalyzerAsync(test, compilerError, expectedGetDisplay, expectedSP0002);
        }

        [Test]
        public async Task RegularExpressionAttributeConstructor_Diagnostic()
        {
            var test = @"
using System.ComponentModel.DataAnnotations;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public RegularExpressionAttribute {|SP0002:TestMethod|}()
    {
        return new RegularExpressionAttribute(""^[a-z]+$"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CoreDataAnnotationsConstructors_Diagnostic()
        {
            var test = @"
using System.ComponentModel.DataAnnotations;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public RequiredAttribute {|SP0002:RequiredMethod|}()
    {
        return new RequiredAttribute();
    }

    [EnforcePure]
    public StringLengthAttribute {|SP0002:StringLengthMethod|}()
    {
        return new StringLengthAttribute(10);
    }

    [EnforcePure]
    public RangeAttribute {|SP0002:RangeMethod|}()
    {
        return new RangeAttribute(0d, 1d);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }


    }
}
