using System;
using System.Linq;
using System.Collections.Immutable;
using NUnit.Framework;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    [Parallelizable(ParallelScope.Children)]
    public class LinqOperationsTests
    {
        private static readonly ImmutableArray<MetadataReference> LinqFrameworkReferences =
            AnalyzerTestHost.GetMinimalFrameworkReferences();

        [Test]
        public async Task SimpleLinqQuery_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;
using System.Collections.Generic;



public class TestClass
{
    [EnforcePure]
    public int TestMethod(IEnumerable<int> numbers)
    {
        return numbers
            .Where(x => x > 0)
            .Select(x => x * x)
            .OrderBy(x => x)
            .Take(5)
            .Sum();
    }
}";


            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task ComplexLinqWithMath_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;
using System.Collections.Generic;



public class TestClass
{
    [EnforcePure]
    public double TestMethod(IEnumerable<double> numbers)
    {
        return numbers
            .Where(x => x > Math.PI)
            .Select(x => Math.Pow(Math.Sin(x), 2) + Math.Pow(Math.Cos(x), 2))
            .OrderBy(x => Math.Abs(x - 1))
            .Take(5)
            .Average();
    }
}";


            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task MethodWithLazyEvaluation_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;
using System.Linq;
using System.Collections.Generic;



public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> numbers)
    {
        return numbers.Where(x => x > 0)
                     .Select(x => x * x)
                     .OrderBy(x => x);
    }
}";


            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqSourceWithImpureGetEnumerator_Diagnostic()
        {
            var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class ImpureSequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator()
    {
        Console.WriteLine(""enumerating"");
        return Enumerable.Empty<int>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(ImpureSequence numbers)
    {
        return numbers.Select(x => x);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqSourceWithImpureExplicitGetEnumerator_Diagnostic()
        {
            var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class ExplicitImpureSequence : IEnumerable<int>
{
    IEnumerator<int> IEnumerable<int>.GetEnumerator()
    {
        Console.WriteLine(""enumerating"");
        return Enumerable.Empty<int>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<int>)this).GetEnumerator();
    }
}

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(ExplicitImpureSequence numbers)
    {
        return numbers.Select(x => x);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqInterfaceLocalAssignedAfterDeclaration_WithPureEnumerator_NoDiagnostic()
        {
            var test = @"
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class PureSequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator() => new Enumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator : IEnumerator<int>
    {
        public int Current => 0;
        object IEnumerator.Current => Current;
        public bool MoveNext() => false;
        public void Reset() { }
        public void Dispose() { }
    }
}

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod()
    {
        IEnumerable<int> numbers;
        numbers = new PureSequence();
        return numbers.Select(x => x);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqInterfaceLocalAssignedAfterDeclaration_WithImpureEnumerator_Diagnostic()
        {
            var test = @"
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class ImpureSequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator() => new Enumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator : IEnumerator<int>
    {
        public int Current => 0;
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            GlobalState.Count++;
            return false;
        }

        public void Reset() { }
        public void Dispose() { }
    }
}

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}()
    {
        IEnumerable<int> numbers;
        numbers = new ImpureSequence();
        return numbers.Select(x => x);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqSourceWithImpureMoveNext_Diagnostic()
        {
            var test = @"
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator() => new Enumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class Enumerator : IEnumerator<int>
    {
        public int Current => 0;
        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            GlobalState.Count++;
            return false;
        }

        public void Reset() { }
        public void Dispose() { }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Sequence values)
    {
        return values.Any();
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDistinctWithImpureEqualityComparer_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class ImpureComparer : IEqualityComparer<int>
{
    public bool Equals(int x, int y)
    {
        Console.WriteLine(""comparing"");
        return x == y;
    }

    public int GetHashCode(int obj) => obj;
}

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> numbers)
    {
        return numbers.Distinct(new ImpureComparer());
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDistinctWithPureEqualityComparer_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class PureComparer : IEqualityComparer<int>
{
    public bool Equals(int x, int y) => x == y;

    public int GetHashCode(int obj) => obj;
}

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> numbers)
    {
        return numbers.Distinct(new PureComparer());
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDistinctDefaultEqualityDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values)
    {
        return values.Distinct();
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDistinctDefaultEqualityForBuiltinValue_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> values)
    {
        return values.Distinct();
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDistinctNullComparerDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values)
    {
        return values.Distinct(null);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDistinctDefaultComparerForBuiltinValue_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> values)
    {
        return values.Distinct(default(IEqualityComparer<int>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqReverse_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> values)
    {
        return values.Reverse();
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqTakeWhileWithPurePredicate_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> values)
    {
        return values.TakeWhile(static value => value > 0);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDeferredFactoriesAndAdapters_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int[]> TestMethod(object[] values, int[] numbers)
    {
        _ = Enumerable.Empty<int>();
        _ = Enumerable.Range(0, 4);
        _ = Enumerable.Repeat(1, 4);
        _ = values.Cast<int>();
        _ = values.OfType<string>();
        return numbers.Chunk(2);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqContainsEqualityComparerDefaultDispatchToImpureEquatable_Diagnostic()
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
        return values.Contains(value, EqualityComparer<MutableRecord>.Default);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqContainsEqualityComparerDefaultForBuiltinValue_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(IEnumerable<int> values, int value)
    {
        return values.Contains(value, EqualityComparer<int>.Default);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqScalarPredicateHelpers_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(IEnumerable<int> values, IEnumerable<int> other)
    {
        var allPositive = values.All(static value => value >= 0);
        var hasAny = values.Any();
        var containsOne = values.Contains(1);
        var same = values.SequenceEqual(other);
        return (allPositive ? 1 : 0) +
               (hasAny ? 1 : 0) +
               (containsOne ? 1 : 0) +
               (same ? 1 : 0);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqScalarElementHelpers_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(IEnumerable<int> values)
    {
        var count = values.Count();
        var first = values.First();
        var firstOrDefault = values.FirstOrDefault();
        var last = values.Last();
        var single = values.Single();
        var element = values.ElementAt(0);
        return count + first + firstOrDefault + last + single + element;
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqScalarPartitionHelpers_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(IEnumerable<int> values)
    {
        var skipped = values.Skip(1);
        var taken = values.Take(2);
        return skipped.FirstOrDefault() + taken.FirstOrDefault();
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqUnionDefaultEqualityDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> left, IEnumerable<MutableRecord> right)
    {
        return left.Union(right);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqUnionDefaultEqualityForBuiltinValue_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> left, IEnumerable<int> right)
    {
        return left.Union(right);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqExceptNullComparerDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> left, IEnumerable<MutableRecord> right)
    {
        return left.Except(right, null);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqIntersectDefaultComparerForBuiltinValue_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> left, IEnumerable<int> right)
    {
        return left.Intersect(right, default(IEqualityComparer<int>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqGroupByDefaultKeyEqualityDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<IGrouping<MutableRecord, MutableRecord>> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values)
    {
        return values.GroupBy(value => value);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqGroupByDefaultKeyEqualityForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<IGrouping<int, string>> TestMethod(IEnumerable<string> values)
    {
        return values.GroupBy(value => value.Length);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqGroupByDefaultComparerDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<IGrouping<MutableRecord, MutableRecord>> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values)
    {
        return values.GroupBy(value => value, default(IEqualityComparer<MutableRecord>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqGroupByDefaultComparerForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<IGrouping<int, string>> TestMethod(IEnumerable<string> values)
    {
        return values.GroupBy(value => value.Length, default(IEqualityComparer<int>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqToLookupDefaultKeyEqualityDispatchToImpureEquatable_Diagnostic()
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
    public ILookup<MutableRecord, MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values)
    {
        return values.ToLookup(value => value);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqToLookupDefaultKeyEqualityForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ILookup<int, string> TestMethod(IEnumerable<string> values)
    {
        return values.ToLookup(value => value.Length);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqToLookupDefaultComparerDispatchToImpureEquatable_Diagnostic()
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
    public ILookup<MutableRecord, MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> values)
    {
        return values.ToLookup(value => value, default(IEqualityComparer<MutableRecord>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqToLookupDefaultComparerForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ILookup<int, string> TestMethod(IEnumerable<string> values)
    {
        return values.ToLookup(value => value.Length, default(IEqualityComparer<int>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqJoinDefaultKeyEqualityDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> left, IEnumerable<MutableRecord> right)
    {
        return left.Join(right, l => l, r => r, (l, r) => l);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqJoinDefaultKeyEqualityForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<string> TestMethod(IEnumerable<string> left, IEnumerable<string> right)
    {
        return left.Join(right, l => l.Length, r => r.Length, (l, r) => l);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqGroupJoinDefaultComparerDispatchToImpureEquatable_Diagnostic()
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
    public IEnumerable<MutableRecord> {|SP0002:TestMethod|}(IEnumerable<MutableRecord> left, IEnumerable<MutableRecord> right)
    {
        return left.GroupJoin(right, l => l, r => r, (l, group) => l, default(IEqualityComparer<MutableRecord>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqGroupJoinDefaultComparerForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<string> TestMethod(IEnumerable<string> left, IEnumerable<string> right)
    {
        return left.GroupJoin(right, l => l.Length, r => r.Length, (l, group) => l, default(IEqualityComparer<int>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqDistinctWithInterfaceEqualityComparerParameter_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> numbers, IEqualityComparer<int> comparer)
    {
        return numbers.Distinct(comparer);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqOrderByWithImpureComparer_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class ImpureComparer : IComparer<int>
{
    public int Compare(int x, int y)
    {
        Console.WriteLine(""comparing"");
        return x.CompareTo(y);
    }
}

public class TestClass
{
    [EnforcePure]
    public IOrderedEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> numbers)
    {
        return numbers.OrderBy(value => value, new ImpureComparer());
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqOrderByDefaultComparisonDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
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
    public IOrderedEnumerable<MutableKey> {|SP0002:TestMethod|}(IEnumerable<MutableKey> values)
    {
        return values.OrderBy(value => value);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqOrderByDefaultComparisonForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IOrderedEnumerable<string> TestMethod(IEnumerable<string> values)
    {
        return values.OrderBy(value => value.Length);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqThenByDefaultComparerDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
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
    public IOrderedEnumerable<MutableKey> {|SP0002:TestMethod|}(IEnumerable<MutableKey> values)
    {
        return values.OrderBy(value => 0).ThenBy(value => value, default(IComparer<MutableKey>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqThenByDefaultComparerForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IOrderedEnumerable<string> TestMethod(IEnumerable<string> values)
    {
        return values.OrderBy(value => 0).ThenBy(value => value.Length, default(IComparer<int>));
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqOrderByComparerDefaultDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
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
    public IOrderedEnumerable<MutableKey> {|SP0002:TestMethod|}(IEnumerable<MutableKey> values)
    {
        return values.OrderBy(value => value, Comparer<MutableKey>.Default);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqOrderByComparerDefaultForBuiltinKey_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IOrderedEnumerable<string> TestMethod(IEnumerable<string> values)
    {
        return values.OrderBy(value => value.Length, Comparer<int>.Default);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqOrderByWithStringComparerOrdinal_NoDiagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IOrderedEnumerable<string> TestMethod(IEnumerable<string> values)
    {
        return values.OrderBy(value => value, StringComparer.Ordinal);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqMinDefaultComparisonDispatchToImpureComparable_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
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
    public MutableKey {|SP0002:TestMethod|}(IEnumerable<MutableKey> values)
    {
        return values.Min();
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqMaxDefaultComparisonForBuiltinValue_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(IEnumerable<int> values)
    {
        return values.Max();
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqOrderByWithInterfaceComparerParameter_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IOrderedEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> numbers, IComparer<int> comparer)
    {
        return numbers.OrderBy(value => value, comparer);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqSecondarySourceWithImpureGetEnumerator_Diagnostic()
        {
            var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class ImpureSequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator()
    {
        Console.WriteLine(""enumerating"");
        return Enumerable.Empty<int>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> left, ImpureSequence right)
    {
        return left.Concat(right);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        [Test]
        public async Task LinqSecondarySourceWithInterfaceEnumerable_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> left, IEnumerable<int> right)
    {
        return left.Concat(right);
    }
}";

            await AssertPurityDiagnosticsAsync(test);
        }

        private static async Task AssertPurityDiagnosticsAsync(string markedSource)
        {
            var (source, expectedSpanText) = StripSp0002Markup(markedSource);
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                frameworkReferences: LinqFrameworkReferences,
                concurrentAnalysis: true);
            var purityDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                .ToArray();

            if (expectedSpanText == null)
            {
                Assert.That(purityDiagnostics, Is.Empty);
                Assert.That(diagnostics, Is.Empty);
                return;
            }

            Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics, Has.Length.EqualTo(1));

            var diagnostic = purityDiagnostics[0];
            var actualSpanText = source.Substring(
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length);
            Assert.That(actualSpanText, Is.EqualTo(expectedSpanText));
        }

        private static (string Source, string? ExpectedSpanText) StripSp0002Markup(string markedSource)
        {
            const string prefix = "{|SP0002:";
            const string suffix = "|}";
            var start = markedSource.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return (markedSource, null);
            }

            var contentStart = start + prefix.Length;
            var end = markedSource.IndexOf(suffix, contentStart, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThanOrEqualTo(0), "Unterminated SP0002 markup.");

            var expectedSpanText = markedSource.Substring(contentStart, end - contentStart);
            var source = markedSource.Remove(end, suffix.Length).Remove(start, prefix.Length);
            return (source, expectedSpanText);
        }
    }
}
