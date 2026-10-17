using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class EventInitializerConstructorAdmissionCoverageTests
{
    [TestCase("public event System.Action Changed;", true)]
    [TestCase("public event System.Action Changed = () => { };", false)]
    [TestCase("public static event System.Action Changed = () => { };", true)]
    public void ConstructorAdmissionDistinguishesInstanceAndStaticEventInitializers(string declaration, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(declaration);
        using var subject = TypedProgramSubject.Create("int Target() { _ = new Box(); return 17; }",
            "public sealed class Box { public Box() { } " + declaration + " }");
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var type = subject.Compilation.GetTypeByMetadataName("Box")!;
        var constructor = type.InstanceConstructors.Single();
        Assert.That(CSharpOperationSemantics.IsPlainConstructor(constructor, TestContext.CurrentContext.CancellationToken), Is.EqualTo(expected));
    }
}
