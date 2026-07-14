using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class GenericsInteractionTests
{
    [Test]
    public async Task GenericClassWithPureOperations_UnknownPurityDiagnostics()
    {
        var test = @"
using SharpProof.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

public class Repository<T>
{
    private readonly List<T> _items;
    public Repository(IEnumerable<T> initialItems) { _items = new List<T>(initialItems ?? Enumerable.Empty<T>()); }

    [EnforcePure]
    public T FindItem(Predicate<T> match) => _items.Find(match);

    [EnforcePure]
    public int GetCount() => _items.Count;

    [EnforcePure]
    public IEnumerable<T> GetAll() => _items.ToList();

    [EnforcePure] // Analyzer considers List<T>.Contains pure
    public bool ContainsItem(T item) => _items.Contains(item);
}

public class GenericTestManager
{
    private readonly Repository<string> _stringRepo = new Repository<string>(new[] { ""apple"", ""banana"", ""cherry"" });
    private readonly Repository<int> _intRepo = new Repository<int>(new[] { 1, 2, 3, 5, 8 });

    [EnforcePure]
    public string FindStringStartingWithB() => _stringRepo.FindItem(s => s.StartsWith(""b""));

    [EnforcePure]
    public int GetTotalItemCount() => _stringRepo.GetCount() + _intRepo.GetCount();

    [EnforcePure]
    public bool HasBanana()
    {
        var allStrings = _stringRepo.GetAll();
        return allStrings.Contains(""banana"");
    }
}
";

        var expectedGetAll = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(19, 27, 19, 33)
            .WithArguments("GetAll");
        var expectedHasBanana = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(37, 17, 37, 26)
            .WithArguments("HasBanana");
        var expectedFindItem = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(13, 14, 13, 22)
            .WithArguments("FindItem");
        var expectedFindStringStartingWithB = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(31, 19, 31, 42)
            .WithArguments("FindStringStartingWithB");
        var expectedContainsItem = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedRule)
            .WithSpan(22, 17, 22, 29)
            .WithArguments("ContainsItem");

        await VerifyCS.VerifyAnalyzerAsync(
            test,
            expectedGetAll,
            expectedHasBanana,
            expectedFindItem,
            expectedFindStringStartingWithB,
            expectedContainsItem);
    }

    [Test]
    public async Task GenericRepositoryWithImpureAction_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System;
using System.Collections.Generic;

public class Repository<T>
{
    private readonly List<T> _items = new List<T>();

    [EnforcePure]
    public IEnumerable<T> GetAll() => _items; // Returns collection state from a mutable backing field.

    [EnforcePure]
    public bool ContainsItem(T item) => _items.Contains(item); // List<T>.Contains is tracked as environment/collection-state sensitive.

    [EnforcePure] // New method with impurity
    public void AddAndLog(T item)
    {
        _items.Add(item); // Impure list modification
        Console.WriteLine($""Added item: {item}""); // Impure logging
    }
}
";


        var expectedAddAndLog = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(17, 17, 17, 26)
            .WithArguments("AddAndLog");
        var expectedContainsItem = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(14, 17, 14, 29)
            .WithArguments("ContainsItem");

        await VerifyCS.VerifyAnalyzerAsync(test,
            expectedAddAndLog,
            expectedContainsItem);
    }
}