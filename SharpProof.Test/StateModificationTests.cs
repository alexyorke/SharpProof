using NUnit.Framework;
using SharpProof.Analyzer;


namespace SharpProof.Test;

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

    public sealed record StateModificationCase(string Name, string Source);

    private static readonly StateModificationCase[] StateModificationCasesPart1 =
    {
        new("CompoundAssignmentWithImpureRhs_Diagnostic", @"
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
}"),
        new("MethodWithMutableStructFieldAssignment_NoDiagnostic", @"
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
}"),
        new("LinkedListAddFirst_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(LinkedList<int> list, int value)
    {
        list.AddFirst(value);
    }
}"),
        new("LinkedListNodeValueSetter_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(LinkedListNode<int> node, int value)
    {
        node.Value = value;
    }
}"),
        new("PriorityQueueEnqueue_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(PriorityQueue<int, int> queue, int value, int priority)
    {
        queue.Enqueue(value, priority);
    }
}"),
        new("PriorityQueueDequeue_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(PriorityQueue<int, int> queue)
    {
        return queue.Dequeue();
    }
}"),
        new("ConcurrentQueueEnqueue_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ConcurrentQueue<int> queue, int value)
    {
        queue.Enqueue(value);
    }
}"),
        new("ConcurrentQueueTryDequeue_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Concurrent;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ConcurrentQueue<int> queue)
    {
        return queue.TryDequeue(out _);
    }
}"),
        new("SortedDictionaryAdd_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(SortedDictionary<int, string> values)
    {
        values.Add(1, ""one"");
    }
}"),
        new("SortedSetAdd_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(SortedSet<int> values)
    {
        values.Add(1);
    }
}"),
        new("BitArraySet_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(BitArray bits)
    {
        bits.Set(0, true);
    }
}"),
        new("MethodWithInterfaceCollectionAdd_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ICollection<int> collection, int value)
    {
        collection.Add(value);
    }
}"),
        new("MethodWithInterfaceCollectionClear_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ICollection<int> collection)
    {
        collection.Clear();
    }
}"),
        new("MethodWithInterfaceCollectionRemove_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ICollection<int> collection, int value)
    {
        return collection.Remove(value);
    }
}"),
    };

    private static IEnumerable<TestCaseData> StateModificationCaseData()
    {
        var cases = StateModificationCasesPart1
            .Concat(StateModificationCasesPart2)
            .ToArray();

        if (cases.Length != 28 ||
            cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 28)
        {
            throw new InvalidOperationException("StateModification case invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(StateModificationCaseData))]
    public async Task StateModificationCases(StateModificationCase testCase)
    {
        await VerifyCS.VerifyAnalyzerAsync(testCase.Source);
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







    private static readonly StateModificationCase[] StateModificationCasesPart2 =
    {
        new("MethodWithInterfaceListInsert_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(IList<int> list, int value)
    {
        list.Insert(0, value);
    }
}"),
        new("MethodWithInterfaceListRemoveAt_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(IList<int> list)
    {
        list.RemoveAt(0);
    }
}"),
        new("PureMethodWithListCount_NoDiagnostic", @"
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
}"),
        new("PureMethodWithListGetterIndexer_NoDiagnostic", @"
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
}"),
        new("MethodWithListCapacitySetter_Diagnostic", @"
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
}"),
        new("PureMethodWithListContains_NoDiagnostic", @"
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
}"),
        new("PureMethodWithDictionaryContainsKey_NoDiagnostic", @"
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
}"),
        new("PureMethodWithDictionaryGetterIndexer_NoDiagnostic", @"
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
}"),
        new("PureMethodWithQueuePeek_NoDiagnostic", @"
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
}"),
        new("PureMethodWithQueueContains_NoDiagnostic", @"
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
}"),
        new("MethodWithQueueDequeue_Diagnostic", @"
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
}"),
        new("PureMethodWithStackPeek_NoDiagnostic", @"
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
}"),
        new("PureMethodWithStackContains_NoDiagnostic", @"
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
}"),
        new("MethodWithStackPop_Diagnostic", @"
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
}"),
    };





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
