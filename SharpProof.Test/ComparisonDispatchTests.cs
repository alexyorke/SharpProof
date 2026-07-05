using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ComparisonDispatchTests
    {
        [Test]
        public async Task SortedDictionaryContainsKeyDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(SortedDictionary<MutableKey, int> values, MutableKey key)
    {
        return values.ContainsKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryTryGetValueDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(SortedDictionary<MutableKey, int> values, MutableKey key)
    {
        return values.TryGetValue(key, out var result) && result > 0;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryContainsKeyForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(SortedDictionary<int, string> values, int key)
    {
        return values.ContainsKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryTryGetValueForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(SortedDictionary<int, string> values, int key)
    {
        return values.TryGetValue(key, out var value) && value is not null;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryContainsKeyWithDirectImpureComparer_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class ImpureStringComparer : IComparer<string>
{
    public int Compare(string x, string y)
    {
        Console.WriteLine(""compare"");
        return StringComparer.Ordinal.Compare(x, y);
    }
}

public sealed class TestClass
{
    private readonly SortedDictionary<string, int> _values =
        new SortedDictionary<string, int>(new ImpureStringComparer())
        {
            [""x""] = 1
        };

    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string key)
    {
        return _values.ContainsKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ListBinarySearchDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(List<MutableKey> values, MutableKey key)
    {
        return values.BinarySearch(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ListBinarySearchForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> values, int key)
    {
        return values.BinarySearch(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SpanBinarySearchDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(ReadOnlySpan<MutableKey> values, MutableKey key)
    {
        return values.BinarySearch(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SpanBinarySearchForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySpan<int> values, int key)
    {
        return values.BinarySearch(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArrayBinarySearchDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(MutableKey[] values, MutableKey key)
    {
        return Array.BinarySearch(values, key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArrayBinarySearchForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values, int key)
    {
        return Array.BinarySearch(values, key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SpanSequenceCompareToDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(ReadOnlySpan<MutableKey> left, ReadOnlySpan<MutableKey> right)
    {
        return left.SequenceCompareTo(right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SpanSequenceCompareToForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        return left.SequenceCompareTo(right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ComparerDefaultCompareDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(MutableKey left, MutableKey right)
    {
        return Comparer<MutableKey>.Default.Compare(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ComparerDefaultCompareForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int left, int right)
    {
        return Comparer<int>.Default.Compare(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NullableCompareDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public struct MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(MutableKey? left, MutableKey? right)
    {
        return Nullable.Compare(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NullableCompareForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int? left, int? right)
    {
        return Nullable.Compare(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryIndexerDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(SortedDictionary<MutableKey, int> values, MutableKey key)
    {
        return values[key];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryIndexerForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedDictionary<int, int> values, int key)
    {
        return values[key];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedListContainsKeyDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(SortedList<MutableKey, int> values, MutableKey key)
    {
        return values.ContainsKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedListContainsKeyForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(SortedList<int, string> values, int key)
    {
        return values.ContainsKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedListIndexOfKeyDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(SortedList<MutableKey, int> values, MutableKey key)
    {
        return values.IndexOfKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedListIndexOfKeyForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedList<int, int> values, int key)
    {
        return values.IndexOfKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedListIndexerDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(SortedList<MutableKey, int> values, MutableKey key)
    {
        return values[key];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedListIndexerForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedList<int, int> values, int key)
    {
        return values[key];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedSetTryGetValueDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(SortedSet<MutableKey> values, MutableKey key)
    {
        return values.TryGetValue(key, out var actual) && actual != null;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedSetTryGetValueForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(SortedSet<int> values, int key)
    {
        return values.TryGetValue(key, out _);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedDictionaryContainsKeyDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableSortedDictionary<MutableKey, int> values, MutableKey key)
    {
        return values.ContainsKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedDictionaryContainsKeyForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableSortedDictionary<int, string> values, int key)
    {
        return values.ContainsKey(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedDictionaryIndexerDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(ImmutableSortedDictionary<MutableKey, int> values, MutableKey key)
    {
        return values[key];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedDictionaryIndexerForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ImmutableSortedDictionary<int, int> values, int key)
    {
        return values[key];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedSetContainsDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableSortedSet<MutableKey> values, MutableKey key)
    {
        return values.Contains(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedSetContainsForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableSortedSet<int> values, int key)
    {
        return values.Contains(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedSetTryGetValueDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableSortedSet<MutableKey> values, MutableKey key)
    {
        return values.TryGetValue(key, out var actual) && actual != null;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedSetTryGetValueForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableSortedSet<int> values, int key)
    {
        return values.TryGetValue(key, out _);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedSetAddDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public ImmutableSortedSet<MutableKey> {|SP0002:TestMethod|}(ImmutableSortedSet<MutableKey> values, MutableKey key)
    {
        return values.Add(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedSetAddForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableSortedSet<int> TestMethod(ImmutableSortedSet<int> values, int key)
    {
        return values.Add(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedDictionarySetItemDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine(""compare"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public ImmutableSortedDictionary<MutableKey, int> {|SP0002:TestMethod|}(ImmutableSortedDictionary<MutableKey, int> values, MutableKey key)
    {
        return values.SetItem(key, 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableSortedDictionarySetItemForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableSortedDictionary<int, int> TestMethod(ImmutableSortedDictionary<int, int> values, int key)
    {
        return values.SetItem(key, 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringComparerOrdinalCompare_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string left, string right)
    {
        return StringComparer.Ordinal.Compare(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringComparerOrdinalEquals_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string left, string right)
    {
        return StringComparer.Ordinal.Equals(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
