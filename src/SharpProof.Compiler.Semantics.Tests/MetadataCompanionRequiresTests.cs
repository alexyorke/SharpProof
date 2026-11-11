using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Compiler.Semantics.Test;

// A companion in the calling compilation supplies the Requires of a callee in
// another assembly. The callee's closed attributes are bound separately, from
// its image, so the companion binding never includes them.
[TestFixture]
public sealed class MetadataCompanionRequiresTests
{
    private const string Consumer = """
        using SharpProof.Attributes;
        [ContractFor(typeof(Remote))]
        public static class RemoteContracts {
            public static int Covered(int n) { Contract.Requires(n > 0); Contract.Requires(n < 10); return 0; }
        }
        """;

    [Test]
    public void CompanionClausesBindAgainstTheMetadataParameters()
    {
        var (compilation, target) = Import("Covered");
        var factory = new IrFactory();
        var context = new TotalLoweringContext(factory, target);
        var result = new TotalContractBinder(compilation, factory).BindTotalCompanionRequires(context, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
            Assert.That(result.Clauses.Select(static clause => clause.Kind), Is.EqualTo(new[] { BoundContractKind.Requires, BoundContractKind.Requires }));
        }
        var variable = context.Parameters.Single().Entry;
        var type = factory.GetVariableInfo(variable).Type;
        var interpreter = new IrInterpreter(factory);
        foreach (var input in ImmutableArray.Create(0, 1, 9, 10))
        {
            var values = ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateIntegerValue(type, (long)input));
            Assert.That(result.Clauses.All(clause => interpreter.Evaluate(clause.Value, values).Value!.Boolean == true),
                Is.EqualTo(input is > 0 and < 10), input.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Test]
    public void AttributesAreNotPartOfTheCompanionBinding()
    {
        var (compilation, target) = Import("Attributed");
        var factory = new IrFactory();
        var context = new TotalLoweringContext(factory, target);
        var binder = new TotalContractBinder(compilation, factory);
        var companion = binder.BindTotalCompanionRequires(context, TestContext.CurrentContext.CancellationToken);
        var attributes = binder.BindTotalMetadataRequires(context, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(companion.Failure, Is.EqualTo(ContractBindingFailure.None));
            Assert.That(companion.Clauses, Is.Empty);
            Assert.That(attributes.Clauses, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void SourceTargetIsRejected()
    {
        var compilation = TestCompilation.Create("CompanionSource", "public static class Local { public static int M(int n) => n; }");
        TestCompilation.AssertNoErrors(compilation);
        var target = compilation.GetTypeByMetadataName("Local")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory();
        var result = new TotalContractBinder(compilation, factory).BindTotalCompanionRequires(new TotalLoweringContext(factory, target),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
    }

    private static (Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation, IMethodSymbol Target) Import(string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        var library = TestCompilation.Create("CompanionLibrary", """
            using SharpProof.Attributes;
            public static class Remote {
                public static int Covered(int n) => n;
                public static int Attributed([Positive] int n) => n;
            }
            """);
        TestCompilation.AssertNoErrors(library);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var placeholder = TestCompilation.Create("CompanionConsumer", "public static class Placeholder {}");
        var compilation = placeholder
            .AddReferences(MetadataReference.CreateFromImage(ImmutableArray.Create(image.GetBuffer(), 0, checked((int)image.Length))))
            .AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Consumer,
                (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)placeholder.SyntaxTrees[0].Options, path: "Consumer.cs"));
        TestCompilation.AssertNoErrors(compilation);
        var target = compilation.GetTypeByMetadataName("Remote")!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        Assert.That(target.DeclaringSyntaxReferences, Is.Empty);
        return (compilation, target);
    }
}
