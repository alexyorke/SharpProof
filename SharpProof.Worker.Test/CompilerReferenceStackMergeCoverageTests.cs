using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerReferenceStackMergeCoverageTests
{
    private static readonly bool[] Choices = [false, true];

    [TestCase("int")]
    [TestCase("uint")]
    public void RealConditionalCallWidensTheZeroStackSubsetAtItsJoin(string valueType)
    {
        var library = "public static class Library { public static " + valueType +
            " Target(bool choose, " + valueType + " value) => Check(choose ? value : 0); public static " +
            valueType + " Check(" + valueType + " value) => value; }";
        var source = "public static class Subject { public static " + valueType +
            " Target(bool choose, " + valueType + " value) => Library.Target(choose, value); }";
        using var subject = new MetadataTestSubject(library, source);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, null);
        var session = new TotalSourceCallSession(subject.Compilation, null, provider.Resolve, TestContext.CurrentContext.CancellationToken);
        var body = session.PrepareIl(method);
        Assert.That(body, Is.Not.Null);
        Assert.That(TotalIlStack.TryValidate(body!, session.Spend, out var shapes), Is.True);
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        var callIndex = Enumerable.Range(0, body!.Instructions.Length).Single(index => body.Instructions[index].Code == "Call");
        Assert.That(shapes[callIndex]!.Value, Has.Length.EqualTo(1));
        Assert.That(shapes[callIndex]!.Value[0].NumericWidth, Is.EqualTo(32));
        Assert.That(shapes[callIndex]!.Value[0].IsBooleanCompatibleI4, Is.False);
        foreach (var choose in Choices)
        {
            object value = valueType == "int" ? (object)17 : 17U;
            object expected = valueType == "int" ? (object)(choose ? 17 : 0) : choose ? 17U : 0U;
            Assert.That(subject.Method.Invoke(null, [choose, value]), Is.EqualTo(expected));
            Assert.That(subject.RootMethod.Invoke(null, [choose, value]), Is.EqualTo(expected));
        }
    }

    [TestCase("object", "object", "object", true)]
    [TestCase("string", "string", "string", true)]
    [TestCase("object", "string", "object", false)]
    public void SharedCallJoinPreservesReferenceAdmission(string firstType, string secondType,
        string checkType, bool admitted)
    {
        var librarySource = $$"""
            public static class Library {
                public static bool Target(bool choose, {{firstType}} first, {{secondType}} second)
                    => Check(choose ? first : second);
                public static bool Check({{checkType}} value) => value == null;
            }
            """;
        var rootSource = $$"""
            public static class Subject {
                public static bool Target(bool choose, {{firstType}} first, {{secondType}} second)
                    => Library.Target(choose, first, second);
            }
            """;
        using var subject = new MetadataTestSubject(librarySource, rootSource);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target")
            .OfType<IMethodSymbol>().Single();
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, null);
        var session = new TotalSourceCallSession(subject.Compilation, null, provider.Resolve, TestContext.CurrentContext.CancellationToken);
        var body = session.PrepareIl(method);
        Assert.That(body, Is.Not.Null, "Actual emitted reference method must decode.");
        var code = body!.Instructions;
        var callIndex = Enumerable.Range(0, code.Length).Single(index => code[index].Code == "Call");
        var call = code[callIndex];
        Assert.That(call.Method, Is.Not.Null);
        Assert.That(call.Method!.Name, Is.EqualTo("Check"));
        Assert.That(call.Method.IsStatic, Is.True);
        Assert.That(call.Method.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(call.Method.Parameters, Has.Length.EqualTo(1));
        Assert.That(call.Method.Parameters[0].Type.SpecialType,
            Is.EqualTo(checkType == "string" ? SpecialType.System_String : SpecialType.System_Object));
        Assert.That(call.Method.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(code.Any(instruction => instruction.Code is "Brtrue" or "Brfalse"), Is.True);
        Assert.That(Enumerable.Range(0, code.Length)
            .Count(index => TotalIlStack.Successors(code, index).Contains(callIndex)), Is.EqualTo(2),
            "Both actual ternary paths must join at the shared call.");
        foreach (var instruction in code)
        {
            TestContext.WriteLine($"IL {instruction.Offset}: {instruction.Code}, target={instruction.Target}, " +
                $"method={instruction.Method?.ToDisplayString()}");
        }
        Assert.That(TotalIlStack.TryValidate(body!, session.Spend, out var shapes), Is.EqualTo(admitted));
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        if (admitted)
        {
            Assert.That(shapes[callIndex], Is.Not.Null);
            Assert.That(shapes[callIndex]!.Value, Has.Length.EqualTo(1));
            Assert.That(shapes[callIndex]!.Value[0].Accepts(call.Method.Parameters[0].Type), Is.True);
        }
        object?[] firstValues = [null, firstType == "string" ? "first" : new object()];
        object?[] secondValues = [null, secondType == "string" ? "second" : new object()];
        foreach (var first in firstValues)
        {
            foreach (var second in secondValues)
            {
                foreach (var choose in Choices)
                {
                    object?[] arguments = [choose, first, second];
                    var expected = choose ? first == null : second == null;
                    Assert.That(subject.Method.Invoke(null, arguments), Is.EqualTo(expected));
                    Assert.That(subject.RootMethod.Invoke(null, arguments), Is.EqualTo(expected));
                }
            }
        }
    }
}
