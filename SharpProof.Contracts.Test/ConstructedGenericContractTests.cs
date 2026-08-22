using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ConstructedGenericContractTests
{
    [Test]
    public void OuterTypeParametersAreSpecializedForNestedMembers()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public sealed class Outer<T> where T : class {
                public sealed class Reader {
                    public T Read(T value) {
                        Contract.Ensures(
                            Contract.Result<T>() != null);
                        return value;
                    }
                }
            }

            public static class Caller {
                public static string Call(
                    Outer<string>.Reader reader,
                    string value) => reader.Read(value);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void NestedNamedTypesPreserveConstructedContainingTypes()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public sealed class Outer<T> {
                public sealed class Value {
                }

                public sealed class Reader {
                    public Value Read(Value value) {
                        Contract.Requires(value != null);
                        Contract.Ensures(
                            Contract.Result<Value>() != null);
                        return value;
                    }
                }
            }

            public static class Caller {
                public static Outer<string>.Value Call(
                    Outer<string>.Reader reader,
                    Outer<string>.Value value) => reader.Read(value);
            }
            """,
            expectedClauses: 2);
    }

    [Test]
    public void NamedTypeArgumentsAreRecursivelySpecialized()
    {
        AssertBinds(
            """
            using System.Collections.Generic;
            using SharpProof.Attributes;

            public interface IRepository<T> where T : class {
                List<T> Read(List<T> value);
            }

            [ContractFor(typeof(IRepository<>))]
            public static class RepositoryContracts<T> where T : class {
                public static List<T> Read(
                    IRepository<T> receiver,
                    List<T> value) {
                    Contract.Requires(value != null);
                    Contract.Ensures(
                        Contract.Result<List<T>>() != null);
                    return value;
                }
            }

            public static class Caller {
                public static List<string> Call(
                    IRepository<string> repository,
                    List<string> value) => repository.Read(value);
            }
            """,
            expectedClauses: 2);
    }

    [Test]
    public void ArrayElementTypesAreRecursivelySpecialized()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public interface IRepository<T> where T : class {
                T[] Read(T[] value);
            }

            [ContractFor(typeof(IRepository<>))]
            public static class RepositoryContracts<T> where T : class {
                public static T[] Read(
                    IRepository<T> receiver,
                    T[] value) {
                    Contract.Requires(value != null);
                    Contract.Ensures(Contract.Result<T[]>() != null);
                    return value;
                }
            }

            public static class Caller {
                public static string[] Call(
                    IRepository<string> repository,
                    string[] value) => repository.Read(value);
            }
            """,
            expectedClauses: 2);
    }

    [Test]
    public void PointerTargetTypesAreRecursivelySpecialized()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public unsafe interface IBuffer<T> where T : unmanaged {
                void Read(T* value);
            }

            [ContractFor(typeof(IBuffer<>))]
            public static unsafe class BufferContracts<T>
                where T : unmanaged {
                public static void Read(
                    IBuffer<T> receiver,
                    T* value) {
                    Contract.Requires(value != null);
                }
            }

            public static unsafe class Caller {
                public static void Call(
                    IBuffer<int> buffer,
                    int* value) => buffer.Read(value);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void FunctionPointerTargetTypesAreRecursivelySpecialized()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public unsafe interface ITransformer<T> where T : unmanaged {
                delegate*<T, T> Map(delegate*<T, T> value);
            }

            [ContractFor(typeof(ITransformer<>))]
            public static unsafe class TransformerContracts<T>
                where T : unmanaged {
                public static delegate*<T, T> Map(
                    ITransformer<T> receiver,
                    delegate*<T, T> value) {
                    Contract.Requires(value != null);
                    return value;
                }
            }

            public static unsafe class Caller {
                public static delegate*<int, int> Call(
                    ITransformer<int> transformer,
                    delegate*<int, int> value) => transformer.Map(value);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void FunctionPointerRefReadonlyModifiersSurviveConstruction()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public unsafe interface IReader<T> where T : unmanaged {
                void Read(delegate*<ref readonly T, void> callback);
            }

            [ContractFor(typeof(IReader<>))]
            public static unsafe class ReaderContracts<T>
                where T : unmanaged {
                public static void Read(
                    IReader<T> receiver,
                    delegate*<ref readonly T, void> callback) {
                    Contract.Requires(callback != null);
                }
            }

            public static unsafe class Caller {
                public static void Call(
                    IReader<int> reader,
                    delegate*<ref readonly int, void> callback) =>
                    reader.Read(callback);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void ConstructedPartialGenericMethodUsesItsImplementationBody()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public static partial class Target<T> where T : class {
                public static partial T Read(T value);
                public static partial T Read(T value) {
                    Contract.Requires(value != null);
                    return value;
                }
            }

            public static class Caller {
                public static string Call(string value) =>
                    Target<string>.Read(value);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void ConstructedPartialGenericCompanionUsesItsImplementationBody()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public interface ITarget<T> where T : class {
                T Read(T value);
            }

            [ContractFor(typeof(ITarget<>))]
            public static partial class TargetContracts<T> where T : class {
                public static partial T Read(
                    ITarget<T> receiver,
                    T value);
                public static partial T Read(
                    ITarget<T> receiver,
                    T value) {
                    Contract.Requires(value != null);
                    return value;
                }
            }

            public static class Caller {
                public static string Call(
                    ITarget<string> target,
                    string value) => target.Read(value);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void ConstructedPartialMethodTypeParametersAreSpecialized()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public static partial class Target {
                public static partial T Read<T>(T value) where T : class;
                public static partial T Read<T>(T value) where T : class {
                    Contract.Requires(value != null);
                    Contract.Ensures(Contract.Result<T>() != null);
                    return value;
                }
            }

            public static class Caller {
                public static string Call(string value) =>
                    Target.Read<string>(value);
            }
            """,
            expectedClauses: 2);
    }

    [Test]
    public void ConstructedPartialCompanionMethodTypeParametersAreSpecialized()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                T Read<T>(T value) where T : class;
            }

            [ContractFor(typeof(ITarget))]
            public static partial class TargetContracts {
                public static partial T Read<T>(
                    ITarget receiver,
                    T value) where T : class;
                public static partial T Read<T>(
                    ITarget receiver,
                    T value) where T : class {
                    Contract.Requires(value != null);
                    Contract.Ensures(Contract.Result<T>() != null);
                    return value;
                }
            }

            public static class Caller {
                public static string Call(ITarget target, string value) =>
                    target.Read<string>(value);
            }
            """,
            expectedClauses: 2);
    }

    [Test]
    public void ConstructedPartialRejectsResultInsideRequires()
    {
        AssertFailure(
            """
            using SharpProof.Attributes;

            public static partial class Target<T> where T : class {
                public static partial T Read(T value);
                public static partial T Read(T value) {
                    Contract.Requires(Contract.Result<T>() != null);
                    return value;
                }
            }

            public static class Caller {
                public static string Call(string value) =>
                    Target<string>.Read(value);
            }
            """,
            ContractBindingFailure.ResultOutsideEnsures);
    }

    [Test]
    public void ConstructedPartialCompanionRejectsResultInsideRequires()
    {
        AssertFailure(
            """
            using SharpProof.Attributes;

            public interface ITarget<T> where T : class {
                T Read(T value);
            }

            [ContractFor(typeof(ITarget<>))]
            public static partial class TargetContracts<T> where T : class {
                public static partial T Read(ITarget<T> receiver, T value);
                public static partial T Read(ITarget<T> receiver, T value) {
                    Contract.Requires(Contract.Result<T>() != null);
                    return value;
                }
            }

            public static class Caller {
                public static string Call(ITarget<string> target, string value) =>
                    target.Read(value);
            }
            """,
            ContractBindingFailure.ResultOutsideEnsures);
    }

    private static void AssertBinds(string source, int expectedClauses)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Last();
        var target = compilation.GetSemanticModel(tree)
            .GetSymbolInfo(invocation)
            .Symbol as IMethodSymbol ??
            throw new InvalidOperationException(invocation.ToString());

        var result = new ContractBinder(compilation, new IrFactory())
            .Bind(target);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(
            result.Contracts!.Clauses,
            Has.Length.EqualTo(expectedClauses));
    }

    private static void AssertFailure(
        string source,
        ContractBindingFailure expectedFailure)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Last();
        var target = compilation.GetSemanticModel(tree)
            .GetSymbolInfo(invocation)
            .Symbol as IMethodSymbol ??
            throw new InvalidOperationException(invocation.ToString());

        var result = new ContractBinder(compilation, new IrFactory())
            .Bind(target);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(expectedFailure));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: ["SHARPPROOF_CONTRACTS"]));
        var compilation = CSharpCompilation.Create(
            "ConstructedContracts_" + Guid.NewGuid().ToString("N"),
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
        return compilation;
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var paths = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Contract).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. paths.Select(static path =>
            MetadataReference.CreateFromFile(path))];
    }
}
