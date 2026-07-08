using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Threading.Tasks;
using System.Collections.Generic;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using System;
using SharpProof.Attributes;


namespace SharpProof.Test
{
    [TestFixture]
    public class StateModificationTests
    {

        [Test]
        public async Task ImpureMethodWithFieldAssignment_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private int _field;

    [EnforcePure]
    public void TestMethod()
    {
        _field = 42;
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(10, 17, 10, 27)
                                 .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithStaticFieldAccess_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

class TestClass
{
    static int staticField = 0;

    [EnforcePure]
    public int TestMethod()
    {
        return ++staticField;
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(10, 16, 10, 26)
                                 .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithMutableParameter_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.Add(42);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task CompoundAssignmentWithImpureRhs_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
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
using SharpProof.Attributes;

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
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ref int value)
    {
        value = 42;
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(8, 17, 8, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithListRemove_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.Remove(42);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithListAddRange_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list, IEnumerable<int> values)
    {
        list.AddRange(values);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task LinkedListAddFirst_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(LinkedList<int> list, int value)
    {
        list.AddFirst(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task LinkedListNodeValueSetter_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(LinkedListNode<int> node, int value)
    {
        node.Value = value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PriorityQueueEnqueue_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(PriorityQueue<int, int> queue, int value, int priority)
    {
        queue.Enqueue(value, priority);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PriorityQueueDequeue_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(PriorityQueue<int, int> queue)
    {
        return queue.Dequeue();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConcurrentQueueEnqueue_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ConcurrentQueue<int> queue, int value)
    {
        queue.Enqueue(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConcurrentQueueTryDequeue_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ConcurrentQueue<int> queue)
    {
        return queue.TryDequeue(out _);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryAdd_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(SortedDictionary<int, string> values)
    {
        values.Add(1, ""one"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedSetAdd_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(SortedSet<int> values)
    {
        values.Add(1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task BitArraySet_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(BitArray bits)
    {
        bits.Set(0, true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithListClear_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.Clear();
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithListSetterIndexer_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
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

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithListRemoveAt_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.RemoveAt(0);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithInterfaceCollectionAdd_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ICollection<int> collection, int value)
    {
        collection.Add(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithInterfaceCollectionClear_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ICollection<int> collection)
    {
        collection.Clear();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithInterfaceCollectionRemove_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ICollection<int> collection, int value)
    {
        return collection.Remove(value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithInterfaceListInsert_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(IList<int> list, int value)
    {
        list.Insert(0, value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodWithInterfaceListRemoveAt_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(IList<int> list)
    {
        list.RemoveAt(0);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithListCount_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
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
        public async Task MethodWithListReverse_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> list)
    {
        list.Reverse();
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithListGetterIndexer_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
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
        public async Task MethodWithListCapacitySetter_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(List<int> list)
    {
        list.Capacity = 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithListContains_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
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
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict.Add(""newKey"", 100);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithDictionaryRemove_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict.Remove(""someKey"");
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithDictionaryTryAdd_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Dictionary<string, int> dict)
    {
        return dict.TryAdd(""newKey"", 100);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ConcurrentDictionaryTryAdd_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ConcurrentDictionary<int, int> dictionary)
    {
        return dictionary.TryAdd(1, 2);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(8, 17, 8, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task BlockingCollectionAdd_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(BlockingCollection<int> values)
    {
        values.Add(1);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(8, 17, 8, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task BlockingCollectionTake_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(BlockingCollection<int> values)
    {
        return values.Take();
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(8, 16, 8, 26)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ConcurrentBagAdd_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ConcurrentBag<int> values)
    {
        values.Add(1);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(8, 17, 8, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task ConcurrentBagTryTake_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ConcurrentBag<int> values)
    {
        return values.TryTake(out _);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(8, 17, 8, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithDictionaryClear_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict.Clear();
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithDictionarySetterIndexer_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<string, int> dict)
    {
        dict[""existingKey""] = 200;
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithDictionaryContainsKey_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
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
using SharpProof.Attributes;
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
        public async Task MethodWithHashSetAdd_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HashSet<int> values)
    {
        values.Add(1);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithHashSetClear_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HashSet<int> values)
    {
        values.Clear();
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithHashSetRemove_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HashSet<int> values)
    {
        values.Remove(1);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithHashSetUnionWith_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HashSet<int> values, IEnumerable<int> other)
    {
        values.UnionWith(other);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task PureMethodWithQueuePeek_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
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
using SharpProof.Attributes;
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
        public async Task MethodWithQueueEnqueue_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Queue<int> values)
    {
        values.Enqueue(1);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithQueueDequeue_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(Queue<int> values)
    {
        return values.Dequeue();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PureMethodWithStackPeek_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
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
using SharpProof.Attributes;
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
        public async Task MethodWithStackPush_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Stack<int> values)
    {
        values.Push(1);
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(9, 17, 9, 27)
                                 .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [Test]
        public async Task MethodWithStackPop_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(Stack<int> values)
    {
        return values.Pop();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StaticReadonlyFieldModification_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static int StaticReadonlyField = 10;

    [EnforcePure]
    public void ModifyStaticReadonly()
    {
        StaticReadonlyField = 20; // Now this is a valid (impure) assignment
    }
}";

            var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
                                 .WithSpan(10, 17, 10, 37)
                                 .WithArguments("ModifyStaticReadonly");


            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}


