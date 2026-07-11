using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class ObjectEqualsDispatchTests
{
    [Test]
    public async Task ObjectEqualsOnObjectParameter_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(object left, object right)
    {
        return left.Equals(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task IEquatableDispatchToImpureImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(MutableRecord left, MutableRecord right)
    {
        return ((IEquatable<MutableRecord>)left).Equals(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task IEquatableDispatchToPureImplementation_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        return other != null;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(MutableRecord left, MutableRecord right)
    {
        return ((IEquatable<MutableRecord>)left).Equals(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EqualityComparerDefaultEqualsDispatchToImpureImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }

    public override bool Equals(object value)
    {
        return value is MutableRecord other && Equals(other);
    }

    public override int GetHashCode()
    {
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(MutableRecord left, MutableRecord right)
    {
        return EqualityComparer<MutableRecord>.Default.Equals(left, right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EqualityComparerDefaultGetHashCodeDispatchToImpureOverride_Diagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(MutableRecord value)
    {
        return EqualityComparer<MutableRecord>.Default.GetHashCode(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EqualityComparerDefaultEqualsForFloatingAndDecimalValues_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(double left, double right, decimal first, decimal second)
    {
        return EqualityComparer<double>.Default.Equals(left, right) &&
            EqualityComparer<decimal>.Default.Equals(first, second);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NullableEqualsDispatchToImpureImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public struct MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(MutableRecord? left, MutableRecord? right)
    {
        return Nullable.Equals(left, right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NullableEqualsForBuiltinValueTypes_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int? left, int? right, decimal? first, decimal? second)
    {
        return Nullable.Equals(left, right) &&
            Nullable.Equals(first, second);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EqualityComparerDefaultGetHashCodeForFloatingAndDecimalValues_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(double value, decimal amount)
    {
        return EqualityComparer<double>.Default.GetHashCode(value) +
            EqualityComparer<decimal>.Default.GetHashCode(amount);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashCodeCombineDispatchToImpureGetHashCode_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(MutableRecord value)
    {
        return HashCode.Combine(value, 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashCodeCombineForBuiltinValueTypes_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value, long other)
    {
        return HashCode.Combine(value, other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ListContainsDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(List<MutableRecord> values, MutableRecord value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ListContainsForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(List<int> values, int value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayIndexOfDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(MutableRecord[] values, MutableRecord value)
    {
        return Array.IndexOf(values, value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayIndexOfForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values, int value)
    {
        return Array.IndexOf(values, value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayLastIndexOfDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(MutableRecord[] values, MutableRecord value)
    {
        return Array.LastIndexOf(values, value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayLastIndexOfForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values, int value)
    {
        return Array.LastIndexOf(values, value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqContainsDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values, MutableRecord value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqContainsForBuiltinValueEquality_UnknownExternalEnumerator_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<int> values, int value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetContainsDispatchToImpureGetHashCode_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(HashSet<MutableRecord> values, MutableRecord value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetContainsForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(HashSet<int> values, int value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetTryGetValueDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(HashSet<MutableRecord> values, MutableRecord value)
    {
        return values.TryGetValue(value, out var actual) && actual != null;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetTryGetValueForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(HashSet<int> values, int value)
    {
        return values.TryGetValue(value, out _);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetSetEqualsDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(HashSet<MutableRecord> values, HashSet<MutableRecord> other)
    {
        return values.SetEquals(other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetSetEqualsWithImpureSecondaryEnumerator_Diagnostic()
    {
        var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class ImpureSequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator()
    {
        Console.WriteLine(""enumerate"");
        return ((IEnumerable<int>)Array.Empty<int>()).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(HashSet<int> values, ImpureSequence other)
    {
        return values.SetEquals(other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetOverlapsForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(HashSet<int> values, HashSet<int> other)
    {
        return values.Overlaps(other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetIsSubsetOfForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(HashSet<int> values, HashSet<int> other)
    {
        return values.IsSubsetOf(other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryContainsKeyDispatchToImpureGetHashCode_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Dictionary<MutableRecord, int> values, MutableRecord value)
    {
        return values.ContainsKey(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryTryGetValueDispatchToImpureGetHashCode_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Dictionary<MutableRecord, int> values, MutableRecord value)
    {
        return values.TryGetValue(value, out var result) && result > 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryTryGetValueForBuiltinKey_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Dictionary<string, int> values, string key)
    {
        return values.TryGetValue(key, out var result) && result > 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryContainsValueDispatchToImpureEqualsOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override bool Equals(object obj)
    {
        Console.WriteLine(""equals"");
        return obj is MutableRecord;
    }

    public override int GetHashCode() => 0;
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Dictionary<string, MutableRecord> values, MutableRecord value)
    {
        return values.ContainsValue(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryContainsValueForBuiltinValue_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Dictionary<string, int> values, int value)
    {
        return values.ContainsValue(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SortedDictionaryContainsValueDispatchToImpureEqualsOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override bool Equals(object obj)
    {
        Console.WriteLine(""equals"");
        return obj is MutableRecord;
    }

    public override int GetHashCode() => 0;
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(SortedDictionary<int, MutableRecord> values, MutableRecord value)
    {
        return values.ContainsValue(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SortedDictionaryContainsValueForBuiltinValue_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(SortedDictionary<int, int> values, int value)
    {
        return values.ContainsValue(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryIndexerDispatchToImpureGetHashCode_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(Dictionary<MutableRecord, int> values, MutableRecord value)
    {
        return values[value];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryIndexerForBuiltinKey_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Dictionary<string, int> values, string key)
    {
        return values[key];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryIndexerWithBuiltinKeyAndImpureComparer_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    private sealed class ImpureStringComparer : IEqualityComparer<string>
    {
        bool IEqualityComparer<string>.Equals(string x, string y) => x == y;

        int IEqualityComparer<string>.GetHashCode(string obj)
        {
            Console.WriteLine(""hash"");
            return obj.GetHashCode();
        }
    }

    public sealed class ImpureStringDictionary : Dictionary<string, int>
    {
        [EnforcePure]
        public {|SP0002:ImpureStringDictionary|}() : base(new ImpureStringComparer()) { }
    }

    [EnforcePure]
    public int {|SP0002:TestMethod|}(ImpureStringDictionary values, string key)
    {
        return values[key];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryIndexerWithBuiltinKeyAndDirectImpureComparer_Diagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    private sealed class ImpureStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y) => x == y;

        public int GetHashCode(string obj)
        {
            Console.WriteLine(""hash"");
            return obj.GetHashCode();
        }
    }

    private readonly Dictionary<string, int> _values =
        new Dictionary<string, int>(new ImpureStringComparer()) { [""x""] = 1 };

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return _values[""x""];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DictionaryIndexerWithBuiltinKeyAndDirectPureComparer_NoDiagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    private sealed class PureStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y) => x == y;

        public int GetHashCode(string obj) => obj.Length;
    }

    private readonly Dictionary<string, int> _values =
        new Dictionary<string, int>(new PureStringComparer()) { [""x""] = 1 };

    [EnforcePure]
    public int TestMethod()
    {
        return _values[""x""];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task HashSetContainsWithReceiverSubtypeImpureComparer_Diagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using System;
using System.Collections.Generic;
using SharpProof.Attributes;

public sealed class ImpureStringComparer : IEqualityComparer<string>
{
    public bool Equals(string x, string y) => x == y;

    public int GetHashCode(string obj)
    {
        Console.WriteLine(obj);
        return obj.GetHashCode();
    }
}

public sealed class ImpureStringSet : HashSet<string>
{
    public ImpureStringSet() : base(new ImpureStringComparer()) { }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImpureStringSet values)
    {
        return values.Contains(""x"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqSequenceEqualDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<MutableRecord> left, IEnumerable<MutableRecord> right)
    {
        return left.SequenceEqual(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqSequenceEqualForBuiltinValueEquality_UnknownExternalEnumerator_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<int> left, IEnumerable<int> right)
    {
        return left.SequenceEqual(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqSequenceEqualWithStringComparerOrdinalIgnoreCase_UnknownExternalEnumerator_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<string> left, IEnumerable<string> right)
    {
        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqContainsNullComparerDispatchesToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values, MutableRecord value)
    {
        return values.Contains(value, null);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqContainsNullComparerForBuiltinValueEquality_UnknownExternalEnumerator_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<int> values, int value)
    {
        return values.Contains(value, null);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqSequenceEqualDefaultComparerDispatchesToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<MutableRecord> left, IEnumerable<MutableRecord> right)
    {
        return left.SequenceEqual(right, default(IEqualityComparer<MutableRecord>));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqSequenceEqualDefaultComparerForBuiltinValueEquality_UnknownExternalEnumerator_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IEnumerable<int> left, IEnumerable<int> right)
    {
        return left.SequenceEqual(right, default(IEqualityComparer<int>));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanSequenceEqualDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ReadOnlySpan<MutableRecord> left, ReadOnlySpan<MutableRecord> right)
    {
        return left.SequenceEqual(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanSequenceEqualForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ReadOnlySpan<int> left, ReadOnlySpan<int> right)
    {
        return left.SequenceEqual(right);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanContainsDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ReadOnlySpan<MutableRecord> values, MutableRecord value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanContainsForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ReadOnlySpan<int> values, int value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanIndexOfDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(ReadOnlySpan<MutableRecord> values, MutableRecord value)
    {
        return values.IndexOf(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanIndexOfForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySpan<int> values, int value)
    {
        return values.IndexOf(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanStartsWithDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ReadOnlySpan<MutableRecord> values, ReadOnlySpan<MutableRecord> prefix)
    {
        return values.StartsWith(prefix);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanStartsWithForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ReadOnlySpan<int> values, ReadOnlySpan<int> prefix)
    {
        return values.StartsWith(prefix);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanEndsWithDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ReadOnlySpan<MutableRecord> values, ReadOnlySpan<MutableRecord> suffix)
    {
        return values.EndsWith(suffix);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SpanEndsWithForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ReadOnlySpan<int> values, ReadOnlySpan<int> suffix)
    {
        return values.EndsWith(suffix);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableListContainsDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableList<MutableRecord> values, MutableRecord value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableListContainsForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableList<int> values, int value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetContainsDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableHashSet<MutableRecord> values, MutableRecord value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetContainsForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableHashSet<int> values, int value)
    {
        return values.Contains(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetTryGetValueDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableHashSet<MutableRecord> values, MutableRecord value)
    {
        return values.TryGetValue(value, out var actual) && actual != null;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetTryGetValueForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableHashSet<int> values, int value)
    {
        return values.TryGetValue(value, out _);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetIsSubsetOfDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableHashSet<MutableRecord> values, ImmutableHashSet<MutableRecord> other)
    {
        return values.IsSubsetOf(other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetSetEqualsForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableHashSet<int> values, ImmutableHashSet<int> other)
    {
        return values.SetEquals(other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableListRemoveDispatchToImpureEquatableImplementation_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord : IEquatable<MutableRecord>
{
    public bool Equals(MutableRecord other)
    {
        Console.WriteLine(""equals"");
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public ImmutableList<MutableRecord> {|SP0002:TestMethod|}(ImmutableList<MutableRecord> values, MutableRecord value)
    {
        return values.Remove(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableListRemoveForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableList<int> TestMethod(ImmutableList<int> values, int value)
    {
        return values.Remove(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetSetEqualsWithImpureSecondaryEnumerator_Diagnostic()
    {
        var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class ImpureSequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator()
    {
        Console.WriteLine(""enumerate"");
        return ((IEnumerable<int>)Array.Empty<int>()).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableHashSet<int> values, ImpureSequence other)
    {
        return values.SetEquals(other);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableDictionaryContainsKeyDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ImmutableDictionary<MutableRecord, int> values, MutableRecord value)
    {
        return values.ContainsKey(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableDictionaryContainsKeyForBuiltinKey_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ImmutableDictionary<string, int> values, string key)
    {
        return values.ContainsKey(key);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableDictionaryIndexerDispatchToImpureGetHashCode_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(ImmutableDictionary<MutableRecord, int> values, MutableRecord value)
    {
        return values[value];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableDictionaryIndexerForBuiltinKey_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ImmutableDictionary<string, int> values, string key)
    {
        return values[key];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetAddDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public ImmutableHashSet<MutableRecord> {|SP0002:TestMethod|}(ImmutableHashSet<MutableRecord> values, MutableRecord value)
    {
        return values.Add(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetAddForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableHashSet<int> TestMethod(ImmutableHashSet<int> values, int value)
    {
        return values.Add(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetRemoveDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public ImmutableHashSet<MutableRecord> {|SP0002:TestMethod|}(ImmutableHashSet<MutableRecord> values, MutableRecord value)
    {
        return values.Remove(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableHashSetRemoveForBuiltinValueEquality_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableHashSet<int> TestMethod(ImmutableHashSet<int> values, int value)
    {
        return values.Remove(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableDictionarySetItemDispatchToImpureHashCodeOverride_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class MutableRecord
{
    public override int GetHashCode()
    {
        Console.WriteLine(""hash"");
        return 0;
    }
}

public class TestClass
{
    [EnforcePure]
    public ImmutableDictionary<MutableRecord, int> {|SP0002:TestMethod|}(ImmutableDictionary<MutableRecord, int> values, MutableRecord key)
    {
        return values.SetItem(key, 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableDictionarySetItemForBuiltinKey_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableDictionary<string, int> TestMethod(ImmutableDictionary<string, int> values, string key)
    {
        return values.SetItem(key, 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
