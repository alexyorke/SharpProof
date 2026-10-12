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
    [TestCase("dynamic", true)]
    [TestCase("object", false)]
    public void DynamicSignatureIdentityDoesNotCollapseIntoObject(string companionType, bool matches)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public interface IReader { int Read(dynamic value, bool condition); }
            [ContractFor(typeof(IReader))]
            public static class ReaderContracts {
                public static int Read(IReader receiver, {{companionType}} value, bool condition) {
                    Contract.Requires(condition);
                    return 0;
                }
            }
            public static class Caller {
                public static int Call(IReader reader, object value, bool condition) => reader.Read(value, condition);
            }
            """;
        if (matches)
        {
            AssertBinds(source, expectedClauses: 1);
        }
        else
        {
            AssertFailure(source, ContractBindingFailure.CompanionSignatureMismatch);
        }
    }

    [TestCase("delegate*<int, (T Left, T Right)>")]
    [TestCase("delegate*<(T Left, T Right), void>")]
    [TestCase("System.Collections.Generic.List<(T Left, T Right)>")]
    [TestCase("Outer<(T Left, T Right)>.Item")]
    [TestCase("(T Left, T Right)[]")]
    public void UnmappedTupleTypesInsideGenericClausesRemainUnsupported(string clauseType)
    {
        AssertFailure(
            $$"""
            using SharpProof.Attributes;
            public sealed class Outer<T> { public sealed class Item {} }
            public static unsafe class Reader<T> where T : class {
                public static int Read(int value) {
                    Contract.Requires(value > 0 && default({{clauseType}}) == null);
                    return value;
                }
            }
            public static class Caller {
                public static int Call(int value) => Reader<string>.Read(value);
            }
            """,
            ContractBindingFailure.UnsupportedExpression);
    }

    [TestCase("Outer<T>.Item")]
    [TestCase("Outer<T>.Item<T>")]
    public void NestedGenericTypesUsedOnlyInClausesPreserveTheScalarCondition(string clauseType)
    {
        var compilation = CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            public sealed class Outer<T> {
                public sealed class Item {}
                public sealed class Item<U> {}
            }
            public static class Reader<T> where T : class {
                public static void Read(int value) {
                    Contract.Requires(value > 0 && default({{clauseType}}) == null);
                }
            }
            public static class Caller {
                public static void Call(int value) => Reader<string>.Read(value);
            }
            """);
        var target = GetConstructedTargets(compilation, "Reader<string>.Read").Single();
        var factory = new IrFactory();
        var result = new ContractBinder(compilation, factory).Bind(target);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var contracts = result.Contracts!;
        Assert.That(contracts.Clauses, Has.Length.EqualTo(1));
        var parameter = contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.Parameter);
        var type = factory.GetVariableInfo(parameter.Variable).Type;
        var interpreter = new IrInterpreter(factory);
        foreach (var input in new[] { -1, 1 })
        {
            var values = new Dictionary<IrVarId, IrValue> { [parameter.Variable] = factory.CreateIntegerValue(type, input) };
            Assert.That(interpreter.Evaluate(contracts.Clauses[0].Condition, values).Value!.Boolean, Is.EqualTo(input > 0));
        }
    }

    [TestCase("owner")]
    [TestCase("nullable")]
    [TestCase("typekind")]
    public void DistinctGenericCallerSignaturesRemainRejected(string mismatch)
    {
        var targetType = mismatch == "nullable" ? "TLeft?" : "TLeft";
        var companionType = mismatch switch
        {
            "owner" => "TRight",
            "nullable" => "TLeft",
            "typekind" => "int",
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        AssertFailure(
            $$"""
            using SharpProof.Attributes;
            public interface IReader {
                int Read<TLeft, TRight>({{targetType}} value, TRight other, bool condition)
                    where TLeft : class where TRight : class;
            }
            [ContractFor(typeof(IReader))]
            public static class ReaderContracts {
                public static int Read<TLeft, TRight>(IReader receiver, {{companionType}} value, TRight other, bool condition)
                    where TLeft : class where TRight : class {
                    Contract.Requires(condition);
                    return 0;
                }
            }
            public static class Caller {
                public static int Call<TLeft, TRight>(IReader reader, TLeft value, TRight other, bool condition)
                    where TLeft : class where TRight : class => reader.Read(value, other, condition);
            }
            """,
            ContractBindingFailure.CompanionSignatureMismatch);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void GenericCompanionConstraintOrderTriage(bool reorder, bool concrete)
    {
        var companionConstraints = reorder ? "ICloneable, IDisposable" : "IDisposable, ICloneable";
        var caller = concrete
            ? "public static int Call(IReader reader, Value value, bool condition) => reader.Read(value, condition);"
            : "public static int Call<T>(IReader reader, T value, bool condition) where T : class, IDisposable, ICloneable => reader.Read(value, condition);";
        AssertBinds(
            $$"""
            using System;
            using SharpProof.Attributes;
            public interface IReader {
                int Read<T>(T value, bool condition) where T : class, IDisposable, ICloneable;
            }
            [ContractFor(typeof(IReader))]
            public static class ReaderContracts {
                public static int Read<T>(IReader receiver, T value, bool condition) where T : class, {{companionConstraints}} {
                    Contract.Requires(condition);
                    return 0;
                }
            }
            public sealed class Value : IDisposable, ICloneable {
                public void Dispose() {}
                public object Clone() => new Value();
            }
            public static class Caller { {{caller}} }
            """,
            expectedClauses: 1);
    }

    [TestCase("T*")]
    [TestCase("delegate*<T, T>")]
    public void CompoundPointerTypesInsideGenericClausesCannotBecomeProofEvidence(string type)
    {
        AssertFailure(
            $$"""
            using SharpProof.Attributes;
            public static unsafe class Reader<T> where T : unmanaged {
                public static int Read(int value) {
                    Contract.Requires(default({{type}}) == null);
                    return value;
                }
            }
            public static class Caller {
                public static int Call(int value) => Reader<int>.Read(value);
            }
            """,
            ContractBindingFailure.UnsupportedExpression);
    }

    [TestCase("Cdecl", "Cdecl, SuppressGCTransition")]
    [TestCase("Cdecl, SuppressGCTransition", "Cdecl")]
    public void FunctionPointerCompanionMustPreserveEveryCallingConvention(
        string targetConventions, string companionConventions)
    {
        AssertFailure(
            $$"""
            using SharpProof.Attributes;
            public unsafe interface IReader {
                void Read(delegate* unmanaged[{{targetConventions}}]<int, int> callback);
            }
            [ContractFor(typeof(IReader))]
            public static unsafe class ReaderContracts {
                public static void Read(IReader receiver, delegate* unmanaged[{{companionConventions}}]<int, int> callback) {}
            }
            public static unsafe class Caller {
                public static void Call(IReader reader, delegate* unmanaged[{{targetConventions}}]<int, int> callback) => reader.Read(callback);
            }
            """,
            ContractBindingFailure.CompanionSignatureMismatch);
    }

    [Test]
    public void GenericCompanionConstraintTypeMustMatch()
    {
        AssertFailure(
            """
            using System;
            using SharpProof.Attributes;
            public interface IReader {
                int Read<T>(T value) where T : IDisposable;
            }
            [ContractFor(typeof(IReader))]
            public static class ReaderContracts {
                public static int Read<T>(IReader receiver, T value) where T : ICloneable {
                    Contract.Requires(value != null);
                    return 0;
                }
            }
            public static class Caller {
                public static int Call<T>(IReader reader, T value) where T : IDisposable => reader.Read(value);
            }
            """,
            ContractBindingFailure.CompanionSignatureMismatch);
    }

    [Test]
    public void GenericCompanionConstraintOrderDoesNotChangeContractIdentity()
    {
        AssertBinds(
            """
            using System;
            using SharpProof.Attributes;
            public interface IReader {
                int Read<T>(T value, bool condition) where T : class, IDisposable, ICloneable;
            }
            [ContractFor(typeof(IReader))]
            public static class ReaderContracts {
                public static int Read<T>(IReader receiver, T value, bool condition) where T : class, ICloneable, IDisposable {
                    Contract.Requires(condition);
                    return 0;
                }
            }
            public static class Caller {
                public static int Call<T>(IReader reader, T value, bool condition) where T : class, IDisposable, ICloneable => reader.Read(value, condition);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void InParameterCompanionPreservesReadOnlySignature()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public interface IReader {
                int Read(in int value);
            }

            [ContractFor(typeof(IReader))]
            public static class ReaderContracts {
                public static int Read(IReader receiver, in int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }

            public static class Caller {
                public static int Call(IReader reader, in int value) =>
                    reader.Read(in value);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void RefParameterCompanionCannotReplaceReadOnlyInput()
    {
        AssertFailure(
            """
            using SharpProof.Attributes;

            public interface IReader {
                int Read(in int value);
            }

            [ContractFor(typeof(IReader))]
            public static class ReaderContracts {
                public static int Read(IReader receiver, ref int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }

            public static class Caller {
                public static int Call(IReader reader, in int value) =>
                    reader.Read(in value);
            }
            """,
            ContractBindingFailure.CompanionSignatureMismatch);
    }

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
    public void PointerContractExpressionsAbstain()
    {
        AssertFailure(
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
            ContractBindingFailure.UnsupportedExpression);
    }

    [Test]
    public void FunctionPointerContractExpressionsAbstain()
    {
        AssertFailure(
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
            ContractBindingFailure.UnsupportedExpression);
    }

    [Test]
    public void FunctionPointerRefReadonlyContractExpressionsAbstain()
    {
        AssertFailure(
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
            ContractBindingFailure.UnsupportedExpression);
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
    public void NotNullTypeParametersAreAdmittedAfterSpecialization()
    {
        AssertBinds(
            """
            using SharpProof.Attributes;

            public static class Target {
                public static T[] Read<T>(T[] value) where T : notnull {
                    Contract.Requires(value.Length >= 0);
                    return value;
                }
            }

            public static class Caller {
                public static string[] Call(string[] value) =>
                    Target.Read<string>(value);
            }
            """,
            expectedClauses: 1);
    }

    [Test]
    public void NotNullGenericValueSpecializationFailsClosed()
    {
        var compilation = CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Target {
                public static void Read<T>([NotNull] T value) { }
            }

            public static class Caller {
                public static void Call() => Target.Read<int>(1);
            }
            """);
        var target = GetConstructedTargets(compilation, "Target.Read")
            .Single();

        var result = new ContractBinder(compilation, new IrFactory())
            .Bind(target);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(
            result.Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
    }

    [Test]
    public void BindingCachePreservesConstructedMethodNullability()
    {
        var fixture = CreateConstructedMethodFixture();

        var first = fixture.Binder.Bind(fixture.Targets[0]);
        var second = fixture.Binder.Bind(fixture.Targets[1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsSuccess, Is.True, first.Failure.ToString());
            Assert.That(second.IsSuccess, Is.True, second.Failure.ToString());
            Assert.That(
                first.Contracts!.Target.TypeArguments[0].NullableAnnotation,
                Is.EqualTo(NullableAnnotation.NotAnnotated));
            Assert.That(
                second.Contracts!.Target.TypeArguments[0].NullableAnnotation,
                Is.EqualTo(NullableAnnotation.Annotated));
        }
    }

    [Test]
    public void ClauseInventoryCachePreservesConstructedMethodNullability()
    {
        var fixture = CreateConstructedMethodFixture();

        var first = fixture.Binder.GetClauseInventory(fixture.Targets[0]);
        var second = fixture.Binder.GetClauseInventory(fixture.Targets[1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Callable.TypeArguments[0].NullableAnnotation,
                Is.EqualTo(NullableAnnotation.NotAnnotated));
            Assert.That(
                second.Callable.TypeArguments[0].NullableAnnotation,
                Is.EqualTo(NullableAnnotation.Annotated));
        }
    }

    private static ConstructedMethodFixture CreateConstructedMethodFixture()
    {
        var compilation = CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Target {
                public static T Echo<T>(T value) {
                    Contract.Requires(true);
                    return value;
                }
            }

            public static class Caller {
                public static void Call() {
                    _ = Target.Echo<string>("");
                    _ = Target.Echo<string?>(null);
                }
            }
            """);
        return new(
            compilation,
            GetConstructedTargets(compilation, "Target.Echo"),
            new ContractBinder(compilation, new IrFactory()));
    }

    [Test]
    public void SharedCompanionResolutionCachePreservesMethodNullability()
    {
        var compilation = CreateCompilation(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                T Echo<T>(T value);
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static T Echo<T>(ITarget receiver, T value) {
                    Contract.Requires(true);
                    return value;
                }
            }

            public static class Caller {
                public static void Call(ITarget target) {
                    _ = target.Echo<string>("");
                    _ = target.Echo<string?>(null);
                }
            }
            """);
        var targets = GetConstructedTargets(compilation, "target.Echo");

        var first = new ContractBinder(compilation, new IrFactory())
            .Bind(targets[0]);
        var second = new ContractBinder(compilation, new IrFactory())
            .Bind(targets[1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsSuccess, Is.True, first.Failure.ToString());
            Assert.That(second.IsSuccess, Is.True, second.Failure.ToString());
            Assert.That(
                first.Contracts!.Source.TypeArguments[0].NullableAnnotation,
                Is.EqualTo(NullableAnnotation.NotAnnotated));
            Assert.That(
                second.Contracts!.Source.TypeArguments[0].NullableAnnotation,
                Is.EqualTo(NullableAnnotation.Annotated));
        }
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

    private static IMethodSymbol[] GetConstructedTargets(
        CSharpCompilation compilation,
        string expressionPrefix)
    {
        var tree = compilation.SyntaxTrees.Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        return [.. tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression.ToString()
                .StartsWith(expressionPrefix, StringComparison.Ordinal))
            .Select(invocation => semanticModel.GetSymbolInfo(invocation).Symbol)
            .OfType<IMethodSymbol>()];
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return TestCompilation.Create(
            "ConstructedContracts",
            source,
            allowUnsafe: true);
    }

    private sealed record ConstructedMethodFixture(
        CSharpCompilation Compilation,
        IMethodSymbol[] Targets,
        ContractBinder Binder);

}
