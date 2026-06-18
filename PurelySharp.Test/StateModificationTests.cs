using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using System.Collections.Generic;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;
using System;
using PurelySharp.Attributes;


namespace PurelySharp.Test
{
    [TestFixture]
    public class StateModificationTests
    {

        private const string MinimalEnforcePureAttributeSource = @"
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Constructor | System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
public sealed class EnforcePureAttribute : System.Attribute { }";

        [Test]
        public async Task ImpureMethodWithFieldAssignment_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    private int _field;

    [EnforcePure]
    public void TestMethod()
    {
        _field = 42;
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(10, 17, 10, 27)
                                 .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithStaticFieldAccess_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

class TestClass
{
    static int staticField = 0;

    [EnforcePure]
    public int TestMethod()
    {
        return ++staticField;
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(10, 16, 10, 26)
                                 .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithMutableParameter_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.Add(42);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task CompoundAssignmentWithImpureRhs_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}()
    {
        var value = 0;
        value += ReadImpure();
        return value;
    }

    private int ReadImpure()
    {
        Console.WriteLine(""impure"");
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]



        public async Task MethodWithMutableStructFieldAssignment_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public struct MutableStruct
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(MutableStruct str)
    {
        str.Value = 42;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithRefParameter_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ref int value)
    {
        value = 42;
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(8, 17, 8, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithListRemove_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.Remove(42);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithListClear_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.Clear();
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithListSetterIndexer_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        if (list.Count > 0)
            list[0] = 100;
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithListCount_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> list)
    {
        return list.Count; // Reading Count property should be pure
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithListGetterIndexer_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> list)
    {
        return list.Count > 0 ? list[0] : 0; // Reading via indexer should be pure
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithListContains_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(List<int> list, int item)
    {
        return list.Contains(item); // Calling Contains should be pure
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }



        [Test]
        public async Task MethodWithDictionaryAdd_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict.Add(""newKey"", 100);
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithDictionaryRemove_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict.Remove(""someKey"");
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithDictionaryClear_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict.Clear();
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithDictionarySetterIndexer_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict[""existingKey""] = 200;
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithDictionaryContainsKey_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Dictionary<string, int> dict, string key)
    {
        return dict.ContainsKey(key); // Calling ContainsKey should be pure
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithDictionaryGetterIndexer_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Dictionary<string, int> dict, string key)
    {
        return dict.ContainsKey(key) ? dict[key] : 0; // Reading via indexer should be pure
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithQueuePeek_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Queue<int> values)
    {
        return values.Peek();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithQueueContains_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Queue<int> values, int value)
    {
        return values.Contains(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithStackPeek_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Stack<int> values)
    {
        return values.Peek();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithStackContains_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Stack<int> values, int value)
    {
        return values.Contains(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StaticReadonlyFieldModification_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    private static int StaticReadonlyField = 10;

    [EnforcePure]
    public void ModifyStaticReadonly()
    {
        StaticReadonlyField = 20; // Now this is a valid (impure) assignment
    }
}";

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(10, 17, 10, 37)
                                 .WithArguments("ModifyStaticReadonly");


            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}


