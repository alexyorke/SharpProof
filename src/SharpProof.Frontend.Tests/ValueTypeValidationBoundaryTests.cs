using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ValueTypeValidationBoundaryTests
{
    private static readonly string[] CSharpError = ["CS0246"];
    private static readonly string[] VisualBasicError = ["BC30002"];
    [Test]
    public void StructParameterConstantRejectsActualIntSpecialization()
    {
        var compilation = CompileCSharp("public static class Subject { public static void F<T>(T value) where T : struct { } }");
        AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("F").OfType<IMethodSymbol>().Single();
        var source = method.TypeParameters.Single();
        var integer = compilation.GetSpecialType(SpecialType.System_Int32);
        var constructed = method.Construct(integer);
        var specialized = constructed.TypeArguments.Single();
        Assert.That(source.IsValueType, Is.True);
        Assert.That(source.SpecialType, Is.EqualTo(SpecialType.None));
        Assert.That(source.HasValueTypeConstraint, Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(constructed.OriginalDefinition, method), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(specialized, integer), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(constructed.Parameters.Single().Type, specialized), Is.True);
        var factory = new IrFactory();
        var rejection = Assert.Throws<FrontendLoweringRejectionException>((Action)(() =>
            RoslynValueTypeValidation.ResolveConstantType(new RoslynTypeMapper(factory), source, specialized)));
        Assert.That(rejection!.Reason, Is.EqualTo(FrontendAbstention.UnsupportedType));
    }

    [Test]
    public void ScalarConstantTypeRetainsMapping()
    {
        var compilation = CompileCSharp("public class Subject { }");
        AssertNoErrors(compilation);
        var integer = compilation.GetSpecialType(SpecialType.System_Int32);
        var factory = new IrFactory();
        Assert.That(RoslynValueTypeValidation.ResolveConstantType(new RoslynTypeMapper(factory), integer, integer),
            Is.EqualTo(factory.IntegerType));
    }

    [Test]
    public void ErrorSourceConstantRejects()
    {
        var compilation = CompileCSharp("public class Subject { public Missing Value; }");
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)).Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Id)),
            Is.EqualTo(CSharpError));
        var error = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Value").OfType<IFieldSymbol>().Single().Type;
        Assert.That(error.TypeKind, Is.EqualTo(TypeKind.Error));
        var rejection = Assert.Throws<FrontendLoweringRejectionException>((Action)(() =>
            RoslynValueTypeValidation.ResolveConstantType(new RoslynTypeMapper(new IrFactory()), error, error)));
        Assert.That(rejection!.Reason, Is.EqualTo(FrontendAbstention.UnsupportedType));
    }

    [Test]
    public void PointerConstantTypeRejects()
    {
        var compilation = CompileCSharp("public unsafe class Subject { public int* Value; }");
        AssertNoErrors(compilation);
        var pointer = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Value").OfType<IFieldSymbol>().Single().Type;
        Assert.That(pointer.TypeKind, Is.EqualTo(TypeKind.Pointer));
        var rejection = Assert.Throws<FrontendLoweringRejectionException>((Action)(() =>
            RoslynValueTypeValidation.ResolveConstantType(new RoslynTypeMapper(new IrFactory()), pointer, pointer)));
        Assert.That(rejection!.Reason, Is.EqualTo(FrontendAbstention.UnsupportedType));
    }

    [Test]
    public void InvalidLocalStorageRejects()
    {
        var compilation = CompileVisualBasic("Module Subject\n Sub F()\n Dim broken As Missing\n End Sub\nEnd Module");
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)).Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Id)),
            Is.EqualTo(VisualBasicError));
        var local = Local(compilation, "broken");
        Assert.That(local.Type.TypeKind, Is.EqualTo(TypeKind.Error));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("broken", factory.ObjectType);
        var rejection = Assert.Throws<FrontendLoweringRejectionException>((Action)(() =>
            RoslynValueTypeValidation.LocalStorageDefault(factory, local.Type, variable)));
        Assert.That(rejection!.Reason, Is.EqualTo(FrontendAbstention.UnsupportedType));
    }

    [Test]
    public void ProgramLoweringRetainsTypeRefusalForBoundErrorLocalStorage()
    {
        var compilation = CompileVisualBasic("Module Subject\n Sub F()\n Dim broken As Missing\n System.GC.KeepAlive(broken)\n End Sub\nEnd Module");
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : d.Severity == DiagnosticSeverity.Error)
            .Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : d.Id), Is.EqualTo(VisualBasicError));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodBlockSyntax>().Single();
        var graph = ControlFlowGraph.Create(method, model)!;
        var target = (IMethodSymbol)model.GetDeclaredSymbol(method.SubOrFunctionStatement)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var local = Local(compilation, "broken");
        Assert.That(local.Type.TypeKind, Is.EqualTo(TypeKind.Error));
        // Retain an authentic compiler local already bound in the lowering
        // context, so the whole-program route must refuse its default storage.
        context.Variable(local);

        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);

        Assert.That(lowered.IsExact, Is.False);
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void ScalarLocalStorageStartsAtZero()
    {
        var compilation = CompileVisualBasic(StorageSource);
        AssertNoErrors(compilation);
        var local = Local(compilation, "integerValue");
        Assert.That(local.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = new RoslynTypeMapper(factory).GetTypeId(local.Type);
        var variable = factory.CreateVariable(local.Name, type);
        var value = RoslynValueTypeValidation.LocalStorageDefault(factory, local.Type, variable);
        Assert.That(value, Is.InstanceOf<IrIntegerTerm>());
        Assert.That(value.Type, Is.EqualTo(type));
        Assert.That(((IrIntegerTerm)value).Value, Is.Zero);
        Assert.That(((IrIntegerTerm)value).Width, Is.EqualTo(32));
    }

    [TestCase("textValue")]
    [TestCase("opaqueValue")]
    public void ReferenceAndOpaqueLocalStorageRetainDefaults(string name)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        var compilation = CompileVisualBasic(StorageSource);
        AssertNoErrors(compilation);
        var local = Local(compilation, name);
        Assert.That(local.Type.TypeKind, Is.Not.EqualTo(TypeKind.Error));
        Assert.That(CSharpOperationSemantics.IsValueDomain(local.Type), Is.True);
        Assert.That(CSharpOperationSemantics.IsOpaqueDomain(local.Type), Is.EqualTo(name == "opaqueValue"));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = new RoslynTypeMapper(factory).GetTypeId(local.Type);
        var variable = factory.CreateVariable(name, type);
        var value = RoslynValueTypeValidation.LocalStorageDefault(factory, local.Type, variable);
        Assert.That(value, Is.InstanceOf<IrNullTerm>());
        Assert.That(value.Type, Is.EqualTo(type));
    }

    private const string StorageSource = """
        Public Structure Opaque
            Public Value As Integer
        End Structure
        Public Module Subject
            Public Sub F()
                Dim integerValue As Integer
                Dim textValue As String
                Dim opaqueValue As Opaque
            End Sub
        End Module
        """;

    private static CSharpCompilation CompileCSharp(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        return CSharpCompilation.Create("ValueTypeBoundary", [CSharpSyntaxTree.ParseText(source)], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    private static VisualBasicCompilation CompileVisualBasic(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        return VisualBasicCompilation.Create("StorageTypeBoundary", [VisualBasicSyntaxTree.ParseText(source)], TestMetadataReferences.Platform,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
    }

    private static ILocalSymbol Local(VisualBasicCompilation compilation, string name)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<ModifiedIdentifierSyntax>()
            .Single(identifier => identifier is null ? throw new System.ArgumentNullException(nameof(identifier)) : (identifier.Identifier.ValueText == name));
        return (ILocalSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(syntax)!;
    }
}
