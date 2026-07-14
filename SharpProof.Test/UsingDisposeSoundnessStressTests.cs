using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class UsingDisposeSoundnessStressTests
{
    public sealed record UsingDisposeCase(string Name, string MarkedSource);

    private static readonly UsingDisposeCase[] UsingDisposeCasesPart1 =
    {
        new("UsingExistingLocalWithImpureDispose_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.ImpureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ImpureDisposable resource)
    {
        using (resource)
        {
        }
    }
}"),
        new("UsingNewImpureDisposable_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.ImpureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        using (new ImpureDisposable())
        {
        }
    }
}"),
        new("UsingNewPureDisposable_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using (new PureDisposable())
        {
        }
    }
}"),
        new("UsingVarPureDisposable_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using var resource = new PureDisposable();
    }
}"),
        new("UsingFactoryImpure_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        using var resource = Create();
    }

    private static PureDisposable Create()
    {
        _ = DateTime.Now.Millisecond;
        return new PureDisposable();
    }
}"),
        new("UsingPureResourceImpureBody_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        using (new PureDisposable())
        {
            Console.WriteLine(""impure"");
        }
    }
}"),
        new("ExplicitDoubleDisposeSameLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource.Dispose();
    }
}"),
        new("ExplicitDisposeAfterReassignment_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource = new PureDisposable();
        resource.Dispose();
    }
}"),
        new("ExplicitUseAfterDisposeSameLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        _ = resource.Use();
        return 1;
    }
}"),
        new("ExplicitPropertyReadAfterDisposeSameLocal_Diagnostic", @"
using System;
using SharpProof.Attributes;

public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    public int Value
    {
        [EnforcePure]
        get { return 1; }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        return resource.Value;
    }
}"),
        new("ExplicitFieldReadAfterDisposeThroughAlias_Diagnostic", @"
using System;
using SharpProof.Attributes;

public sealed class PureDisposable : IDisposable
{
    public readonly int Value = 1;

    [EnforcePure]
    public void Dispose()
    {
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource.Dispose();
        return alias.Value;
    }
}"),
        new("ExplicitUseAfterReassignment_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource = new PureDisposable();
        _ = resource.Use();
        resource.Dispose();
        return 1;
    }
}"),
        new("ExplicitReturnUseAfterDisposeSameLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        return resource.Use();
    }
}"),
        new("ExplicitReturnUseAfterReassignment_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var resource = new PureDisposable();
        resource.Dispose();
        resource = new PureDisposable();
        var value = resource.Use();
        resource.Dispose();
        return value;
    }
}"),
        new("MissingDisposeForOwnedLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
    }
}"),
        new("MissingDisposeForDeconstructedOwnedLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var (resource, count) = (new PureDisposable(), 1);
    }
}"),
    };

    private static IEnumerable<TestCaseData> UsingDisposeCaseData()
    {
        var cases = UsingDisposeCasesPart1
            .Concat(UsingDisposeCasesPart2)
            .Concat(UsingDisposeCasesPart3)
            .Concat(UsingDisposeCasesPart4)
            .ToArray();

        if (cases.Length != 62 ||
            cases.Select(static testCase => testCase.Name)
                .Distinct(StringComparer.Ordinal).Count() != 62)
        {
            throw new InvalidOperationException("Using/dispose case invariants failed.");
        }

        return cases.Select(static testCase => new TestCaseData(testCase).SetName(testCase.Name));
    }

    [TestCaseSource(nameof(UsingDisposeCaseData))]
    public async Task UsingDisposeCases(UsingDisposeCase testCase)
    {
        await AssertPurityDiagnosticsAsync(testCase.MarkedSource);
    }































    private static readonly UsingDisposeCase[] UsingDisposeCasesPart2 =
    {
        new("DisposeDeconstructedOwnedLocal_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var (resource, count) = (new PureDisposable(), 1);
        resource.Dispose();
    }
}"),
        new("MissingDisposeForDeconstructionAssignedOwnedLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        PureDisposable resource;
        (resource, _) = (new PureDisposable(), 1);
    }
}"),
        new("ExplicitDisposeAsyncSatisfiesOwnedLocalDisposal_NoDiagnostic", DisposableTestSources.AsyncUsings +
                   DisposableTestSources.PureAsyncDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureAsyncDisposable();
        resource.DisposeAsync();
    }
}"),
        new("MissingDisposeForOwnedAsyncLocal_Diagnostic", DisposableTestSources.AsyncUsings +
                   DisposableTestSources.PureAsyncDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
    }
}"),
        new("ExplicitDoubleDisposeAsyncSameLocal_Diagnostic", DisposableTestSources.AsyncUsings +
                   DisposableTestSources.PureAsyncDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        resource.DisposeAsync();
        resource.DisposeAsync();
    }
}"),
        new("ExplicitUseAfterDisposeAsyncSameLocal_Diagnostic", DisposableTestSources.AsyncUsings +
                   DisposableTestSources.PureAsyncDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        resource.DisposeAsync();
        return resource.Use();
    }
}"),
        new("AwaitUsingDeclarationSatisfiesOwnedAsyncLocalDisposal_NoDiagnostic", DisposableTestSources.AsyncUsings +
                   DisposableTestSources.PureAsyncDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public async Task TestMethod()
    {
        await using var resource = new PureAsyncDisposable();
    }
}"),
        new("UseAfterAwaitUsingStatementSameLocal_Diagnostic", DisposableTestSources.AsyncUsings +
                   DisposableTestSources.PureAsyncDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public async Task<int> {|SP0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        await using (resource)
        {
        }

        return resource.Use();
    }
}"),
        new("DisposeAfterAwaitUsingStatementSameLocal_Diagnostic", DisposableTestSources.AsyncUsings +
                   DisposableTestSources.PureAsyncDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public async Task {|SP0002:TestMethod|}()
    {
        var resource = new PureAsyncDisposable();
        await using (resource)
        {
        }

        resource.DisposeAsync();
    }
}"),
        new("ConditionalDisposeOnlyOneBranch_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
    }
}"),
        new("ConditionalDisposeBothBranches_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
        else
        {
            resource.Dispose();
        }
    }
}"),
        new("SwitchDisposeAllArms_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int mode)
    {
        var resource = new PureDisposable();
        switch (mode)
        {
            case 0:
                resource.Dispose();
                break;
            default:
                resource.Dispose();
                break;
        }
    }
}"),
        new("SwitchDisposeMissingDefault_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int mode)
    {
        var resource = new PureDisposable();
        switch (mode)
        {
            case 0:
                resource.Dispose();
                break;
        }
    }
}"),
        new("SwitchReturnOrDisposeAllArms_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod(int mode)
    {
        var resource = new PureDisposable();
        switch (mode)
        {
            case 0:
                return resource;
            default:
                resource.Dispose();
                return new PureDisposable();
        }
    }
}"),
        new("WhileDisposeOnly_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        while (dispose)
        {
            resource.Dispose();
            break;
        }
    }
}"),
        new("ForDisposeOnly_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        for (; dispose; )
        {
            resource.Dispose();
            break;
        }
    }
}"),
    };































    private static readonly UsingDisposeCase[] UsingDisposeCasesPart3 =
    {
        new("DoWhileDisposeSatisfiesOwnedLocalDisposal_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(bool repeat)
    {
        var resource = new PureDisposable();
        do
        {
            resource.Dispose();
        }
        while (repeat);
    }
}"),
        new("FinallyDisposeSatisfiesOwnedLocalDisposal_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        try
        {
        }
        finally
        {
            resource.Dispose();
        }
    }
}"),
        new("FinallyDisposeThroughAliasSatisfiesOwnedLocalDisposal_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        try
        {
        }
        finally
        {
            alias.Dispose();
        }
    }
}"),
        new("TryReturnOwnedLocalWithFinally_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        try
        {
            return resource;
        }
        finally
        {
        }
    }
}"),
        new("UseAfterFinallyDispose_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        try
        {
        }
        finally
        {
            resource.Dispose();
        }

        return resource.Use();
    }
}"),
        new("UseAfterConditionalDisposeBothBranches_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
        else
        {
            resource.Dispose();
        }

        return resource.Use();
    }
}"),
        new("DoubleDisposeAfterConditionalDisposeBothBranches_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(bool dispose)
    {
        var resource = new PureDisposable();
        if (dispose)
        {
            resource.Dispose();
        }
        else
        {
            resource.Dispose();
        }

        resource.Dispose();
    }
}"),
        new("ConditionalDisposeThroughOwnerOrAlias_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(bool disposeOwner)
    {
        var resource = new PureDisposable();
        var alias = resource;
        if (disposeOwner)
        {
            resource.Dispose();
        }
        else
        {
            alias.Dispose();
        }
    }
}"),
        new("UseAfterConditionalDisposeThroughOwnerOrAlias_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(bool disposeOwner)
    {
        var resource = new PureDisposable();
        var alias = resource;
        if (disposeOwner)
        {
            resource.Dispose();
        }
        else
        {
            alias.Dispose();
        }

        return resource.Use();
    }
}"),
        new("DoubleDisposeAfterConditionalDisposeThroughOwnerOrAlias_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(bool disposeOwner)
    {
        var resource = new PureDisposable();
        var alias = resource;
        if (disposeOwner)
        {
            resource.Dispose();
        }
        else
        {
            alias.Dispose();
        }

        resource.Dispose();
    }
}"),
        new("ConditionalReturnOrDispose_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod(bool giveAway)
    {
        var resource = new PureDisposable();
        if (giveAway)
        {
            return resource;
        }

        resource.Dispose();
        return new PureDisposable();
    }
}"),
        new("ConditionalReturnOnlyOneBranch_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable {|SP0002:TestMethod|}(bool giveAway)
    {
        var resource = new PureDisposable();
        if (giveAway)
        {
            return resource;
        }

        return null;
    }
}"),
        new("MissingDisposeForAliasedOwnedLocalAfterOwnerReassignment_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        resource.Dispose();
    }
}"),
        new("DisposeAliasAfterOwnerReassignment_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        resource.Dispose();
    }
}"),
        new("UseAliasAfterAliasDisposeAndOwnerReassignment_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        var value = alias.Use();
        resource.Dispose();
        return value;
    }
}"),
        new("DoubleDisposeAliasAfterOwnerReassignment_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        alias.Dispose();
        resource.Dispose();
    }
}"),
    };































    private static readonly UsingDisposeCase[] UsingDisposeCasesPart4 =
    {
        new("UseOldAliasAfterOwnerDisposeAndReassignment_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource.Dispose();
        resource = new PureDisposable();
        var value = alias.Use();
        resource.Dispose();
        return value;
    }
}"),
        new("DoubleDisposeOldAliasAfterOwnerDisposeAndReassignment_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource.Dispose();
        resource = new PureDisposable();
        alias.Dispose();
        resource.Dispose();
    }
}"),
        new("ReturnedOwnedLocalDisposable_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        return resource;
    }
}"),
        new("ReturnedAliasToOwnedLocalDisposable_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        return alias;
    }
}"),
        new("ReturnedOldAliasAfterOwnerReassignmentAndNewOwnerDisposed_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        resource.Dispose();
        return alias;
    }
}"),
        new("ReturnedNewOwnerAfterAliasDisposed_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public PureDisposable TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        resource = new PureDisposable();
        alias.Dispose();
        return resource;
    }
}"),
        new("ExplicitDisposeAliasThenDisposeOriginal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        alias.Dispose();
        resource.Dispose();
    }
}"),
        new("ExplicitDisposeAliasThenUseOriginal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        alias.Dispose();
        return resource.Use();
    }
}"),
        new("ExplicitDisposeAliasSatisfiesOwnedLocalDisposal_NoDiagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var resource = new PureDisposable();
        var alias = resource;
        alias.Dispose();
    }
}"),
        new("UseAfterUsingStatementExistingLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        using (resource)
        {
        }

        return resource.Use();
    }
}"),
        new("ExplicitDisposeAfterUsingStatementExistingLocal_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        using (resource)
        {
        }

        resource.Dispose();
    }
}"),
        new("UseAfterUsingStatementAlias_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var resource = new PureDisposable();
        var alias = resource;
        using (alias)
        {
        }

        return resource.Use();
    }
}"),
        new("UseAfterNestedUsingDeclarationAlias_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposableWithUse + @"
public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        PureDisposable resource;
        {
            using var alias = new PureDisposable();
            resource = alias;
        }

        return resource.Use();
    }
}"),
        new("ExplicitDisposeAfterNestedUsingDeclarationAlias_Diagnostic", DisposableTestSources.CommonUsings +
                   DisposableTestSources.PureDisposable + @"
public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}()
    {
        PureDisposable resource;
        {
            using var alias = new PureDisposable();
            resource = alias;
        }

        resource.Dispose();
    }
}"),
    };



























    private static async Task AssertPurityDiagnosticsAsync(string markedSource)
    {
        await AnalyzerTestHost.AssertOptionalSingleSp0002Async(markedSource, concurrentAnalysis: true);
    }
}
