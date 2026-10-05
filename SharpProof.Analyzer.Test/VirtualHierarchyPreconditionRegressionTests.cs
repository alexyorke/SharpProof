using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class VirtualHierarchyPreconditionRegressionTests
{

    [Test]
    public void VariableShapedStringLiteralsAreNotAlphaRenamed()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("""
            using SharpProof.Attributes;
            public class Base { public virtual void Run(string value) { Contract.Requires(value != "seed"); } }
            public class Derived : Base { public override void Run(string value) { Contract.Requires(value != "seed"); } }
            """, []);
        var factory = new SharpProof.Ir.IrFactory();
        var binder = new SharpProof.Contracts.ContractBinder(compilation, factory);
        var left = Rewrite("Base");
        var right = Rewrite("Derived");
        var compare = typeof(SharpProof.Analyzer.OverridePreconditionDiagnostics).GetMethod(
            "HaveSameRequires", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.That(compare.Invoke(null, [left, right, factory]), Is.False);

        SharpProof.Contracts.BoundMethodContracts Rewrite(string type)
        {
            var method = compilation.GetTypeByMetadataName(type)!.GetMembers("Run")
                .OfType<Microsoft.CodeAnalysis.IMethodSymbol>().Single();
            var bound = binder.BindRequires(method).Contracts!;
            var variable = bound.Variables.Single(item => item.Role == SharpProof.Contracts.BoundContractVariableRole.Parameter);
            var clause = bound.Clauses.Single();
            var condition = factory.Binary(SharpProof.Ir.IrBinaryOperator.NotEqual,
                factory.Variable(variable.Variable), factory.String("v" + variable.Variable.Value));
            var rewritten = (SharpProof.Contracts.BoundContractClause)Activator.CreateInstance(
                typeof(SharpProof.Contracts.BoundContractClause),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, [clause.Kind, condition, clause.SourceOperation,
                    clause.Evidence, clause.DiagnosticText, clause.SourceSyntax], null)!;
            return (SharpProof.Contracts.BoundMethodContracts)Activator.CreateInstance(
                typeof(SharpProof.Contracts.BoundMethodContracts),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, [bound.Target, bound.Source,
                    System.Collections.Immutable.ImmutableArray.Create(rewritten), bound.Variables, bound.UsesCompanion], null)!;
        }
    }

    [TestCase(false, false, 1)]
    [TestCase(true, false, 1)]
    [TestCase(false, true, 0)]
    public async Task EveryDispatchContractMustPermitLocalRequires(bool useBase, bool allMatching, int expected)
    {
        var open = allMatching ? "[Positive] " : "";
        var ancestor = useBase
            ? "public class Open { public virtual void Run(" + open + "int value) {} }"
            : "public interface Open { void Run(" + open + "int value); }";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; " + ancestor + """
            public interface Strict { void Run([Positive] int value); }
            public class Service : Open, Strict {
                public
            """ + (useBase ? " override " : " ") + "void Run([Positive] int value) {} }",
            "contracts", []);
        Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == "SP0024"), Is.EqualTo(expected));
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "AD0001"), Is.False);
    }

    [TestCase("v99999999999999999999", "v99999999999999999999", 0)]
    [TestCase("v3", "v6", 1)]
    [TestCase("same", "same", 0)]
    public async Task RequiresNormalizationPreservesStringLiterals(string inherited, string local, int expected)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public class Base { public virtual void Run(string original) { " +
            "Contract.Requires(original != \"" + inherited + "\"); } } " +
            "public class Derived : Base { public override void Run(string renamed) { " +
            "Contract.Requires(renamed != \"" + local + "\"); } }",
            "contracts", []);
        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == "AD0001"), Is.False);
        Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == "SP0024"), Is.EqualTo(expected));
    }
}
