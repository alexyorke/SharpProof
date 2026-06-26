using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class CollectionViewTests
    {
        [Test]
        public async Task DictionaryKeys_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Dictionary<int, string>.KeyCollection {|PS0002:TestMethod|}(Dictionary<int, string> values)
    {
        return values.Keys;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DictionaryValues_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Dictionary<int, string>.ValueCollection {|PS0002:TestMethod|}(Dictionary<int, string> values)
    {
        return values.Values;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryKeys_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public SortedDictionary<int, string>.KeyCollection {|PS0002:TestMethod|}(SortedDictionary<int, string> values)
    {
        return values.Keys;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SortedDictionaryValues_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public SortedDictionary<int, string>.ValueCollection {|PS0002:TestMethod|}(SortedDictionary<int, string> values)
    {
        return values.Values;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task IDictionaryKeys_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ICollection<int> {|PS0002:TestMethod|}(IDictionary<int, string> values)
    {
        return values.Keys;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task IDictionaryValues_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ICollection<string> {|PS0002:TestMethod|}(IDictionary<int, string> values)
    {
        return values.Values;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task QueueSynchronized_Diagnostic()
        {
            var test = @"
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Queue {|PS0002:TestMethod|}(Queue values)
    {
        return Queue.Synchronized(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArrayListAdapter_Diagnostic()
        {
            var test = @"
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ArrayList {|PS0002:TestMethod|}(IList values)
    {
        return ArrayList.Adapter(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ListAsReadOnly_NoDiagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> TestMethod(List<int> values)
    {
        return values.AsReadOnly();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArrayAsReadOnly_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|PS0002:TestMethod|}(int[] values)
    {
        return Array.AsReadOnly(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArrayAsReadOnlyFreshLocalArray_NoDiagnostic()
        {
            var test = @"
using System;
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return Array.AsReadOnly(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArrayAsReadOnlyArrayEmpty_NoDiagnostic()
        {
            var test = @"
using System;
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> TestMethod()
    {
        return Array.AsReadOnly(Array.Empty<int>());
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReadOnlyCollectionCtorFreshArray_NoDiagnostic()
        {
            var test = @"
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> TestMethod()
    {
        return new ReadOnlyCollection<int>(new[] { 1, 2, 3 });
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReadOnlyCollectionCtorExistingArray_Diagnostic()
        {
            var test = @"
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|PS0002:TestMethod|}(int[] values)
    {
        return new ReadOnlyCollection<int>(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReadOnlyCollectionCtorExistingList_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyCollection<int> {|PS0002:TestMethod|}(List<int> values)
    {
        return new ReadOnlyCollection<int>(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ArrayAsReadOnlySpan_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<int> {|PS0002:TestMethod|}(int[] values)
    {
        return values.AsSpan();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task OwnedArrayAsReadOnlySpan_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlySpan<int> TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return values.AsSpan();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReadOnlyMemoryCtorCallerOwnedArray_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyMemory<int> {|PS0002:TestMethod|}(int[] values)
    {
        return new ReadOnlyMemory<int>(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReadOnlyMemoryCtorOwnedArray_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ReadOnlyMemory<int> TestMethod()
    {
        var values = new[] { 1, 2, 3 };
        return new ReadOnlyMemory<int>(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestCase("ReadOnlyMemory<int>", "new ReadOnlyMemory<int>(values, 0, values.Length)")]
        [TestCase("Memory<int>", "new Memory<int>(values, 0, values.Length)")]
        public async Task ReadOnlyMemoryAndMemorySliceCtorCallerOwnedArray_Diagnostic(string returnType, string ctorExpression)
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public " + returnType + @" {|PS0002:TestMethod|}(int[] values)
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
using PurelySharp.Attributes;

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

        [Test]
        public async Task CollectionsMarshalAsSpan_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Span<int> {|PS0002:TestMethod|}(List<int> values)
    {
        return CollectionsMarshal.AsSpan(values);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
