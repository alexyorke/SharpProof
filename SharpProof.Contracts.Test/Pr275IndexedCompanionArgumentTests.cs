using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Testing;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class Pr275IndexedCompanionArgumentTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void NullTargetIsRejectedBeforeTheIndex(bool nullIndex)
    {
        var index = ContractForSymbolMatcher.BuildCompanionTargetIndex([]);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
            ContractForSymbolMatcher.ResolveCompanion(null!, nullIndex ? null! : index)));
        Assert.That(exception!.ParamName, Is.EqualTo("target"));
    }

    [Test]
    public void AnActualCompilerMethodRequiresAnIndex()
    {
        var target = CreateTarget(constructor: false);
        var exception = Assert.Throws<ArgumentNullException>((Action)(() =>
            ContractForSymbolMatcher.ResolveCompanion(target, null!)));
        Assert.That(exception!.ParamName, Is.EqualTo("targetIndex"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnActualCompilerMemberHasNoCompanionInTheEmptyIndex(bool constructor)
    {
        var target = CreateTarget(constructor);
        var index = ContractForSymbolMatcher.BuildCompanionTargetIndex([]);
        Assert.That(index, Is.Empty);
        var resolution = ContractForSymbolMatcher.ResolveCompanion(target, index);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolution.Method, Is.Null);
            Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        }
    }

    private static IMethodSymbol CreateTarget(bool constructor)
    {
        var compilation = TestCompilation.Create("Pr275IndexedCompanionInput",
            "public sealed class Subject { public Subject() {} public static int Target() => 0; }");
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var type = compilation.GetTypeByMetadataName("Subject")!;
        var target = constructor ? type.InstanceConstructors.Single() :
            type.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(target.ContainingAssembly, Is.SameAs(compilation.Assembly));
        Assert.That(target.MethodKind, Is.EqualTo(constructor ? MethodKind.Constructor : MethodKind.Ordinary));
        return target;
    }
}
