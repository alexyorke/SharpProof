using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class IndexerTests
{
    [Test]
    public async Task ReadingFromGenericDictionaryBackedIndexer_WithUnresolvedKeyComparer_IsImpure()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class CustomDictionary<TKey, TValue>
{
    private Dictionary<TKey, TValue> _innerDict = new Dictionary<TKey, TValue>();

    // Read-only indexer
    public TValue this[TKey key] => _innerDict[key];
}

public class TestClass
{
    private readonly CustomDictionary<string, int> _dictionary = new CustomDictionary<string, int>();

    [EnforcePure]
    public int {|SP0002:GetValue|}(string key)
    {
        // The wrapped generic dictionary indexer may dispatch through an unresolved key comparer.
        return _dictionary[key];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task WritingToIndexer_IsImpure()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class CustomDictionary<TKey, TValue>
{
    private Dictionary<TKey, TValue> _innerDict = new Dictionary<TKey, TValue>();

    // Indexer with getter and setter
    public TValue this[TKey key]
    {
        get => _innerDict[key];
        set => _innerDict[key] = value;
    }
}

public class TestClass
{
    private readonly CustomDictionary<string, int> _dictionary = new CustomDictionary<string, int>();

    [EnforcePure]
    public void SetValue(string key, int value)
    {
        // Writing to an indexer with a setter should be impure
        _dictionary[key] = value;
    }
}";


        var expectedSetValue = VerifyCS.Diagnostic("SP0002").WithSpan(25, 17, 25, 25)
            .WithArguments("SetValue");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedSetValue);
    }

    [Test]
    public async Task ReadonlyIndexerProperty_IsPure()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class ReadOnlyCollection<T>
{
    private readonly List<T> _items = new List<T>();

    // Read-only indexer (expression-bodied)
    public T this[int index] => _items[index];
}

public class TestClass
{
    private readonly ReadOnlyCollection<string> _collection = new ReadOnlyCollection<string>();

    [EnforcePure]
    public string GetItem(int index)
    {
        // Reading from a read-only indexer should be pure
        return _collection[index];
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MixedAccessIndexer_ImpureWhenWriting()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class MixedAccessCollection<T>
{
    private readonly List<T> _items = new List<T>();

    // Indexer with getter and private setter
    public T this[int index]
    {
        [Pure] // Getter explicitly marked pure
        get => _items[index];
        private set => _items[index] = value;
    }

    // Non-pure method that uses the private setter
    public void UpdateItem(int index, T value)
    {
        this[index] = value;
    }
}

public class TestClass
{
    private readonly MixedAccessCollection<string> _collection = new MixedAccessCollection<string>();

    [EnforcePure]
    public string GetItemPure(int index)
    {
        // Reading is pure via [Pure] getter
        return _collection[index];
    }

    [EnforcePure]
    public void CallUpdateItemImpure(int index, string value)
    {
        // Calling a method that modifies state is impure
        _collection.UpdateItem(index, value);
    }
}";


        var expectedCallUpdate = VerifyCS.Diagnostic("SP0002").WithSpan(39, 17, 39, 37)
            .WithArguments("CallUpdateItemImpure");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedCallUpdate);
    }

    [Test]
    public async Task NestedIndexerAccess_IsPure()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class NestedCollection
{
    private readonly Dictionary<string, Dictionary<int, string>> _nestedDict 
        = new Dictionary<string, Dictionary<int, string>>();

    // Nested indexer - first level
    public Dictionary<int, string> this[string key] => _nestedDict[key];
}

public class TestClass
{
    private readonly NestedCollection _collection = new NestedCollection();

    [EnforcePure]
    public string GetNestedValue(string outerKey, int innerKey)
    {
        // Nested indexer access should be pure
        return _collection[outerKey][innerKey];
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task IndexerReadWithImpureIndex_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class ReadOnlyCollection
{
    public int this[int index] => index;
}

public class TestClass
{
    private readonly ReadOnlyCollection _collection = new ReadOnlyCollection();

    [EnforcePure]
    public int {|SP0002:GetItem|}()
    {
        return _collection[Console.Read()];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}