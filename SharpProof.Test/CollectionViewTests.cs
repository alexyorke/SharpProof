using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class CollectionViewTests
{
    public sealed record CollectionViewOperationCase(string Name, string Source);

    private static readonly CollectionViewOperationCase[] Cases =
    {
        new("DictionaryKeys_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Dictionary<int, string>.KeyCollection {|SP0002:TestMethod|}(Dictionary<int, string> values)
    {
        return values.Keys;
    }
}"),
        new("DictionaryValues_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Dictionary<int, string>.ValueCollection {|SP0002:TestMethod|}(Dictionary<int, string> values)
    {
        return values.Values;
    }
}"),
        new("SortedDictionaryKeys_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public SortedDictionary<int, string>.KeyCollection {|SP0002:TestMethod|}(SortedDictionary<int, string> values)
    {
        return values.Keys;
    }
}"),
        new("SortedDictionaryValues_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public SortedDictionary<int, string>.ValueCollection {|SP0002:TestMethod|}(SortedDictionary<int, string> values)
    {
        return values.Values;
    }
}"),
        new("IDictionaryKeys_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ICollection<int> {|SP0002:TestMethod|}(IDictionary<int, string> values)
    {
        return values.Keys;
    }
}"),
        new("IDictionaryValues_Diagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ICollection<string> {|SP0002:TestMethod|}(IDictionary<int, string> values)
    {
        return values.Values;
    }
}"),
        new("QueueSynchronized_Diagnostic", @"
using System.Collections;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Queue {|SP0002:TestMethod|}(Queue values)
    {
        return Queue.Synchronized(values);
    }
}"),
        new("ArrayListAdapter_Diagnostic", @"
using System.Collections;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ArrayList {|SP0002:TestMethod|}(IList values)
    {
        return ArrayList.Adapter(values);
    }
}"),
        new("ListAsReadOnly_Diagnostic", @"
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|SP0002:TestMethod|}(List<int> values)
    {
        return values.AsReadOnly();
    }
}"),
        new("ArrayAsReadOnly_Diagnostic", @"
using System;
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|SP0002:TestMethod|}(int[] values)
    {
        return Array.AsReadOnly(values);
    }
}"),
        new("ArrayAsReadOnlyFreshLocalArray_NoDiagnostic", @"
using System;
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return Array.AsReadOnly(values);
    }
}"),
        new("ArrayAsReadOnlyArrayEmpty_NoDiagnostic", @"
using System;
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> TestMethod()
    {
        return Array.AsReadOnly(Array.Empty<int>());
    }
}"),
        new("ReadOnlyCollectionCtorFreshArray_NoDiagnostic", @"
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> TestMethod()
    {
        return new ReadOnlyCollection<int>(new[] { 1, 2, 3 });
    }
}"),
        new("ReadOnlyCollectionCtorExistingArray_Diagnostic", @"
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|SP0002:TestMethod|}(int[] values)
    {
        return new ReadOnlyCollection<int>(values);
    }
}"),
        new("ReadOnlyCollectionCtorExistingList_Diagnostic", @"
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|SP0002:TestMethod|}(List<int> values)
    {
        return new ReadOnlyCollection<int>(values);
    }
}"),
        new("ArrayAsReadOnlySpan_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<int> {|SP0002:TestMethod|}(int[] values)
    {
        return values.AsSpan();
    }
}"),
        new("OwnedArrayAsReadOnlySpan_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<int> TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return values.AsSpan();
    }
}"),
        new("ArrayAsReadOnlySpanViaLocal_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<int> {|SP0002:TestMethod|}(int[] values)
    {
        var span = values.AsSpan();
        return span;
    }
}"),
        new("ArrayAsReadOnlySpanSlice_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<int> {|SP0002:TestMethod|}(int[] values)
    {
        return values.AsSpan().Slice(1);
    }
}"),
        new("SpanToReadOnlySpanImplicitConversion_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<int> TestMethod(Span<int> values)
    {
        return values;
    }
}"),
        new("ArrayAsReadOnlyViaLocal_Diagnostic", @"
using System;
using System.IO;
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|SP0002:TestMethod|}(int[] values)
    {
        var view = Array.AsReadOnly(values);
        return view;
    }
}"),
        new("ArrayAsReadOnlyImpureArraySource_Diagnostic", @"
using System;
using System.IO;
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<string> {|SP0002:TestMethod|}(string path)
    {
        return Array.AsReadOnly(Directory.GetFiles(path));
    }
}"),
        new("ReadOnlyMemoryCtorCallerOwnedArray_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyMemory<int> {|SP0002:TestMethod|}(int[] values)
    {
        return new ReadOnlyMemory<int>(values);
    }
}"),
        new("ReadOnlyMemoryCtorOwnedArray_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyMemory<int> TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return new ReadOnlyMemory<int>(values);
    }
}"),
        new("ReadOnlySpanCtorImpureArraySource_Diagnostic", @"
using System;
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<string> {|SP0002:TestMethod|}(string path)
    {
        return new ReadOnlySpan<string>(Directory.GetFiles(path));
    }
}"),
        new("ReadOnlyMemoryViaLocalCallerOwnedArray_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyMemory<int> {|SP0002:TestMethod|}(int[] values)
    {
        var memory = new ReadOnlyMemory<int>(values);
        return memory;
    }
}"),
        new("MemoryToReadOnlyMemoryImplicitConversion_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyMemory<int> TestMethod(Memory<int> values)
    {
        return values;
    }
}"),
        new("CollectionsMarshalAsSpan_Diagnostic", @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Span<int> {|SP0002:TestMethod|}(List<int> values)
    {
        return CollectionsMarshal.AsSpan(values);
    }
}"),
    };

    private static IEnumerable<TestCaseData> CollectionViewOperationCaseData()
    {
        if (Cases.Length != 28 ||
            Cases.Select(static testCase => testCase.Name).Distinct(StringComparer.Ordinal).Count() != 28)
        {
            throw new InvalidOperationException("CollectionViewTests case invariants failed.");
        }

        return Cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(CollectionViewOperationCaseData))]
    public async Task CollectionViewOperationCaseCases(CollectionViewOperationCase testCase)
    {
        await VerifyCS.VerifyAnalyzerAsync(testCase.Source);
    }























    [TestCase("Span<int>", "new Span<int>(values)")]
    [TestCase("ReadOnlySpan<int>", "new ReadOnlySpan<int>(values)")]
    [TestCase("Span<int>", "new Span<int>(values, 0, values.Length)")]
    [TestCase("ReadOnlySpan<int>", "new ReadOnlySpan<int>(values, 0, values.Length)")]
    public async Task SpanAndReadOnlySpanCtorCallerOwnedArray_Diagnostic(string returnType, string ctorExpression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public " + returnType + @" {|SP0002:TestMethod|}(int[] values)
    {
        return " + ctorExpression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [TestCase("Span<int>", "new Span<int>(values)")]
    [TestCase("ReadOnlySpan<int>", "new ReadOnlySpan<int>(values)")]
    [TestCase("Span<int>", "new Span<int>(values, 0, values.Length)")]
    [TestCase("ReadOnlySpan<int>", "new ReadOnlySpan<int>(values, 0, values.Length)")]
    public async Task SpanAndReadOnlySpanCtorOwnedArray_NoDiagnostic(string returnType, string ctorExpression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public " + returnType + @" TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return " + ctorExpression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }






    [TestCase("ReadOnlyMemory<int>", "new ReadOnlyMemory<int>(values, 0, values.Length)")]
    [TestCase("Memory<int>", "new Memory<int>(values, 0, values.Length)")]
    public async Task ReadOnlyMemoryAndMemorySliceCtorCallerOwnedArray_Diagnostic(string returnType,
        string ctorExpression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public " + returnType + @" {|SP0002:TestMethod|}(int[] values)
    {
        return " + ctorExpression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [TestCase("ReadOnlyMemory<int>", "new ReadOnlyMemory<int>(values, 0, values.Length)")]
    [TestCase("Memory<int>", "new Memory<int>(values, 0, values.Length)")]
    public async Task ReadOnlyMemoryAndMemorySliceCtorOwnedArray_NoDiagnostic(string returnType, string ctorExpression)
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public " + returnType + @" TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return " + ctorExpression + @";
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
