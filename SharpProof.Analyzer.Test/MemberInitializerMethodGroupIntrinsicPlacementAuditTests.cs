using System.Globalization;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using ContractApi = SharpProof.Attributes.Contract;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class MemberInitializerMethodGroupIntrinsicPlacementAuditTests
{
    private const string SourceTemplate =
        """
        using System;
        using SharpProof.Attributes;
        public sealed class Subject
        {
            public readonly Func<int> ResultDelegate = __OWNER__.Result<int>;
            public readonly Func<int, int> OldDelegate = __OWNER__.Old<int>;
        }
        public static class Helpers
        {
            public static T Result<T>()
            {
                return default!;
            }
            public static T Old<T>(T value)
            {
                return value;
            }
        }
        """;

    [TestCase(true)]
    [TestCase(false)]
    public async Task MethodGroupInitializersPreserveIntrinsicPlacement(bool intrinsic)
    {
        var source = SourceTemplate.Replace("__OWNER__", intrinsic ? "Contract" : "Helpers", StringComparison.Ordinal);
        var filePath = intrinsic ? "initializer-method-group-intrinsics.cs" : "initializer-method-group-helpers.cs";
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: filePath);
        var compilerDiagnostics = compilation.GetDiagnostics();
        Assert.That(compilerDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var subject = compilation.GetTypeByMetadataName("Subject")!;
        var targetOwner = compilation.GetTypeByMetadataName(intrinsic ? "SharpProof.Attributes.Contract" : "Helpers")!;
        Assert.That(subject, Is.Not.Null);
        Assert.That(targetOwner, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(targetOwner.ContainingAssembly, compilation.Assembly), Is.EqualTo(!intrinsic));
        if (intrinsic)
        {
            Assert.That(targetOwner.ContainingAssembly.Identity.Name, Is.EqualTo(typeof(ContractApi).Assembly.GetName().Name));
        }
        var variables = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText is "ResultDelegate" or "OldDelegate").ToArray();
        Assert.That(variables, Has.Length.EqualTo(2));
        var expectedDiagnostics = new List<string>();
        var bindings = new List<string>();
        foreach (var variable in variables)
        {
            var field = model.GetDeclaredSymbol(variable) as IFieldSymbol;
            var initializer = model.GetOperation(variable.Initializer!) as IFieldInitializerOperation;
            Assert.That(field, Is.Not.Null);
            Assert.That(initializer, Is.Not.Null);
            Assert.That(initializer!.InitializedFields, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(initializer.InitializedFields.Single(), field), Is.True);
            var operations = initializer.Value.DescendantsAndSelf().ToArray();
            var methodReference = operations.OfType<IMethodReferenceOperation>().Single();
            var creation = operations.OfType<IDelegateCreationOperation>().Single();
            var method = methodReference.Method;
            var expectedName = variable.Identifier.ValueText == "ResultDelegate" ? "Result" : "Old";
            var expectedParameterCount = expectedName == "Result" ? 0 : 1;
            var expectedDefinition = targetOwner.GetMembers(expectedName).OfType<IMethodSymbol>()
                .Single(candidate => candidate.Arity == 1 && candidate.Parameters.Length == expectedParameterCount);
            var member = methodReference.Syntax as MemberAccessExpressionSyntax;
            var delegateType = field!.Type as INamedTypeSymbol;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(field.IsReadOnly, Is.True);
                Assert.That(field.IsStatic, Is.False);
                Assert.That(field.IsImplicitlyDeclared, Is.False);
                Assert.That(SymbolEqualityComparer.Default.Equals(field.ContainingType, subject), Is.True);
                Assert.That(SymbolEqualityComparer.Default.Equals(
                    model.GetEnclosingSymbol(methodReference.Syntax.SpanStart), field), Is.True);
                Assert.That(methodReference.Parent, Is.SameAs(creation));
                Assert.That(creation.Target, Is.SameAs(methodReference));
                Assert.That(operations.OfType<IInvocationOperation>(), Is.Empty);
                Assert.That(operations.OfType<IAnonymousFunctionOperation>(), Is.Empty);
                Assert.That(operations.OfType<ILocalFunctionOperation>(), Is.Empty);
                Assert.That(method.IsStatic, Is.True);
                Assert.That(method.MethodKind, Is.EqualTo(MethodKind.Ordinary));
                Assert.That(method.Name, Is.EqualTo(expectedName));
                Assert.That(method.Arity, Is.EqualTo(1));
                Assert.That(method.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Int32));
                Assert.That(method.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
                Assert.That(method.Parameters, Has.Length.EqualTo(expectedParameterCount));
                Assert.That(SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, expectedDefinition), Is.True);
                Assert.That(member, Is.Not.Null);
                Assert.That(delegateType, Is.Not.Null);
                Assert.That(delegateType!.TypeKind, Is.EqualTo(TypeKind.Delegate));
                Assert.That(delegateType.DelegateInvokeMethod!.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
                Assert.That(delegateType.DelegateInvokeMethod!.Parameters, Has.Length.EqualTo(expectedParameterCount));
            }
            if (expectedParameterCount == 1)
            {
                Assert.That(method.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
                Assert.That(delegateType!.DelegateInvokeMethod!.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            }
            Assert.That(member!.Expression.ToString(), Is.EqualTo(intrinsic ? "Contract" : "Helpers"));
            Assert.That(member.Name, Is.TypeOf<GenericNameSyntax>());
            Assert.That(member.Name.Identifier.ValueText, Is.EqualTo(expectedName));
            Assert.That(((GenericNameSyntax)member.Name).TypeArgumentList.Arguments.Single().ToString(), Is.EqualTo("int"));
            Assert.That(member.SyntaxTree, Is.SameAs(tree));
            Assert.That(methodReference.Syntax.Span, Is.EqualTo(variable.Initializer!.Value.Span));
            bindings.Add($"{field.Name}|owner={field}|root={initializer.Kind}|operation={methodReference.Kind}|" +
                $"delegate={creation.Type}|method={method}|apiAssembly={method.ContainingAssembly.Identity}|span={member.Span}");
            if (intrinsic)
            {
                expectedDiagnostics.Add($"SP0024|Error|{filePath}|{member.Span}|" +
                    $"SharpProof contract 'Contract.{expectedName}' has invalid argument '<placement>': " +
                    "expected use inside Contract.Ensures");
            }
        }
        var image = AnalyzerTestHost.EmitImage(compilation);
        var runtimeBinding = VerifyConstructionAndDelegates(image, intrinsic);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var actual = DescribeDiagnostics(diagnostics);
        var expected = string.Join("\n", expectedDiagnostics);
        var observation = $"sourceSHA256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))};" +
            $"peSHA256={Convert.ToHexString(SHA256.HashData(image))};compilerErrors=0\n" +
            string.Join("\n", bindings) + "\n" + runtimeBinding +
            $"\nExpected complete diagnostics: [{expected}]\nActual complete diagnostics: [{actual}]";
        await TestContext.Out.WriteLineAsync(observation);
        Assert.That(actual, Is.EqualTo(expected), observation);
    }

    private static string VerifyConstructionAndDelegates(byte[] image, bool intrinsic)
    {
        var runtime = new AssemblyLoadContext("MemberInitializerMethodGroups", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var assembly = runtime.LoadFromStream(stream);
            var subject = assembly.GetType("Subject")!;
            var instance = Activator.CreateInstance(subject);
            Assert.That(instance, Is.Not.Null);
            var result = subject.GetField("ResultDelegate")!.GetValue(instance);
            var old = subject.GetField("OldDelegate")!.GetValue(instance);
            Assert.That(result, Is.TypeOf<Func<int>>());
            Assert.That(old, Is.TypeOf<Func<int, int>>());
            var resultDelegate = (Func<int>)result!;
            var oldDelegate = (Func<int, int>)old!;
            var expectedOwner = intrinsic ? typeof(ContractApi) : assembly.GetType("Helpers")!;
            AssertDelegate(resultDelegate, expectedOwner, "Result");
            AssertDelegate(oldDelegate, expectedOwner, "Old");
            return "CLR construction succeeded; delegates were created without invocation; " +
                $"Result={resultDelegate.Method};Old={oldDelegate.Method};owner={expectedOwner.AssemblyQualifiedName}";
        }
        finally
        {
            runtime.Unload();
        }
    }

    private static void AssertDelegate(Delegate value, Type expectedOwner, string expectedName)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.Target, Is.Null);
            Assert.That(value.Method.DeclaringType, Is.EqualTo(expectedOwner));
            Assert.That(value.Method.Name, Is.EqualTo(expectedName));
            Assert.That(value.Method.IsStatic, Is.True);
            Assert.That(value.Method.IsGenericMethod, Is.True);
            Assert.That(value.Method.GetGenericArguments().Single(), Is.EqualTo(typeof(int)));
        }
    }

    private static string DescribeDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start).Select(diagnostic =>
                $"{diagnostic.Id}|{diagnostic.Severity}|{diagnostic.Location.SourceTree?.FilePath}|{diagnostic.Location.SourceSpan}|" +
                diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }
}
