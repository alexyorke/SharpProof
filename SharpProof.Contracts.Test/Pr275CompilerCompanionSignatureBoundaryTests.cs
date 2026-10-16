using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class Pr275CompilerCompanionSignatureBoundaryTests
{
    [TestCase("delegate* unmanaged[Cdecl]<int, int>", "delegate* unmanaged[Stdcall]<int, int>")]
    [TestCase("delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int>", "delegate* unmanaged[Cdecl]<int, int>")]
    [TestCase("delegate* unmanaged[Cdecl]<int, int>", "delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int>")]
    [TestCase("delegate*<int, int>", "delegate* unmanaged<int, int>")]
    [TestCase("delegate* unmanaged<int, int>", "delegate* unmanaged[Cdecl]<int, int>")]
    [TestCase("delegate*<int, int>", "delegate*<long, int>")]
    [TestCase("delegate*<int, int>", "delegate*<int, long>")]
    [TestCase("delegate*<ref int, int>", "delegate*<out int, int>")]
    [TestCase("delegate*<in int, int>", "delegate*<ref int, int>")]
    [TestCase("delegate*<int, ref int>", "delegate*<int, int>")]
    [TestCase("delegate*<int, ref readonly int>", "delegate*<int, ref int>")]
    [TestCase("delegate*<int, int>", "delegate*<int, int, int>")]
    [TestCase("(int first, int second)", "(int first, int other)")]
    [TestCase("(int first, int second)", "(int first, long second)")]
    [TestCase("int[]", "int[,]")]
    [TestCase("int*", "long*")]
    [TestCase("int*", "int")]
    [TestCase("delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int>", "delegate* unmanaged[Stdcall, SuppressGCTransition]<int, int>")]
    public void ActualCompilerCompanionRejectsChangedSignatureAndAcceptsExactControl(
        string targetType, string companionType)
    {
        var rejected = CompileAndBind(targetType, companionType);
        Assert.That(rejected.IsSuccess, Is.False);
        Assert.That(rejected.Failure, Is.EqualTo(ContractBindingFailure.CompanionSignatureMismatch));
        Assert.That(rejected.Contracts, Is.Null);

        var accepted = CompileAndBind(targetType, targetType);
        Assert.That(accepted.IsSuccess, Is.True, accepted.Failure.ToString());
        Assert.That(accepted.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(accepted.Contracts, Is.Not.Null);
        Assert.That(accepted.Contracts!.UsesCompanion, Is.True);
        Assert.That(accepted.Contracts.Clauses, Is.Empty);
    }

    private static ContractBindingResult CompileAndBind(string targetType, string companionType)
    {
        var compilation = TestCompilation.Create("Pr275ActualCompanionSignature", $$"""
            using SharpProof.Attributes;
            public unsafe interface Target {
                int Map({{targetType}} value);
            }
            [ContractFor(typeof(Target))]
            public static unsafe class TargetContracts {
                public static int Map(Target receiver, {{companionType}} value) => 0;
            }
            """, allowUnsafe: true);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        var target = compilation.GetTypeByMetadataName("Target")!.GetMembers("Map")
            .OfType<IMethodSymbol>().Single();
        return new ContractBinder(compilation, new IrFactory()).Bind(target);
    }
}
