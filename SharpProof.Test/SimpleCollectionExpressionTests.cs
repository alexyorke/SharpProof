using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System.Collections.Immutable;
using System.Collections.Generic;

namespace SharpProof.Test
{
    [TestFixture]

    public class SimpleCollectionExpressionTests
    {
        [Test]
        public async Task PureMethod_CreateImmutableArray_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> GetNumbers()
    {
        // Using Create method for immutable array (pure)
        return ImmutableArray.Create(1, 2, 3, 4, 5);
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethod_CreateImmutableList_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableList<string> GetNames()
    {
        // Using Create method for immutable list (pure)
        return ImmutableList.Create(""Alice"", ""Bob"", ""Charlie"");
    }
}";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethod_MutableArrayWithArrayCreation_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] GetNumbers()
    {
        // Using new[] array creation expression
        // Returning a mutable array is considered impure
        return new[] { 1, 2, 3, 4, 5 };
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                               .WithSpan(8, 18, 8, 28)
                               .WithArguments("GetNumbers");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }







        [Test]
        public async Task PureMethod_MutableArrayCollectionExpressionSyntax_Diagnostic_1()
        {
            var test = @"
// Requires LangVersion 12+
#nullable enable
using System;
using SharpProof.Attributes;

public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] GetArray()
    {
        // Collection expression defaulting to array (impure under strict rules)
        return [1, 2, 3, 4, 5];
    }
}";
            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                               .WithSpan(10, 18, 10, 26)
                               .WithArguments("GetArray");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethod_MutableListWithCollectionExpression_Diagnostic()
        {
            var test = @"
// Requires LangVersion 12+
#nullable enable
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class CollectionExpressionExample
{
    [EnforcePure]
    public List<int> GetList()
    {
        // Using collection expression with List (impure under strict rules)
        return [1, 2, 3, 4, 5];
    }
}";
            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                               .WithSpan(11, 22, 11, 29)
                               .WithArguments("GetList");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethod_ReturningModifiedFreshLocalArray_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class CollectionExpressionExample
{
    [EnforcePure]
    public static int[] {|SP0002:GetModifiedArray|}()
    {
        int[] array = new int[5];
        array[0] = 10;
        return array;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethod_MutableArrayCollectionExpressionSyntax_Diagnostic_2()
        {
            var test = @"
// Requires LangVersion 12+
#nullable enable
using System;
using SharpProof.Attributes;

public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] GetArray()
    {
        // Impurity comes from returning a new mutable array via collection expression
        return [1, 2, 3, 4, 5];
    }
}";
            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
                               .WithSpan(10, 18, 10, 26)
                               .WithArguments("GetArray");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}
