using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class InitializerIntrinsicBindingControlAuditTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ValidConstructorPrologueStillBinds(bool thisInitializer)
    {
        var initializer = thisInitializer ? "this(value, true)" : "base(value)";
        var compilation = TestCompilation.Create("InitializerIntrinsicBindingControlAudit", $$"""
            using SharpProof.Attributes;
            public class Parent { public Parent(int value) { } }
            public sealed class Subject : Parent {
                public Subject(int value, bool ignored) : base(value) { }
                public Subject(int value) : {{initializer}} {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Old(value) == value);
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var constructor = compilation.GetTypeByMetadataName("Subject")!.InstanceConstructors
            .Single(method => method.Parameters.Length == 1);
        var inventory = new ContractClauseInventoryBuilder(compilation).Create(constructor);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binder = new ContractBinder(compilation, factory);
        var legacy = binder.Bind(constructor);
        var total = binder.BindTotal(new TotalLoweringContext(factory, constructor));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.HasPlacementErrors, Is.False);
            Assert.That(inventory.Clauses, Has.Length.EqualTo(2));
            Assert.That(inventory.Clauses.All(clause => clause.IsValid), Is.True);
            Assert.That(legacy.IsSuccess, Is.True, legacy.Failure.ToString());
            Assert.That(legacy.Contracts!.Clauses, Has.Length.EqualTo(2));
            Assert.That(total.IsSuccess, Is.True, total.Failure.ToString());
            Assert.That(total.Clauses, Has.Length.EqualTo(2));
        }
    }
}
