using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalMethodIdentityPolicyTests
{
    private static readonly ImmutableArray<string> DuplicateErrors = ["CS0111"];

    private const string CoreSource = """
        namespace System
        {
            public class Object { }
            public class String { }
            public abstract class ValueType { }
            public class Attribute { }
            public struct Void { }
            public struct Boolean { }
            public struct Int32 { }
            public struct UIntPtr { }
            public struct IntPtr
            {
                TRACE_MEMBERS
            }
        }
        namespace System.Diagnostics
        {
            public sealed class ConditionalAttribute : Attribute
            {
                public ConditionalAttribute(string symbol) { }
            }
        }
        """;

    [TestCase(false, TestName = "SourceNativeDeclarationResolvesItsActualMember")]
    [TestCase(true, TestName = "MetadataNativeDeclarationResolvesItsActualMember")]
    public void SourceAndMetadataNativeDeclarationsResolveTheirActualMember(bool metadata)
    {
        var (compilation, declaration, candidates) = CreateNative(metadata, duplicate: false);
        Assert.That(candidates, Has.Length.EqualTo(2));
        var expected = candidates.Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Parameters.IsEmpty));
        Assert.That(ConditionalMethodIdentityPolicy.ResolveNativeDeclaration(
            compilation, declaration, candidates), Is.SameAs(expected.OriginalDefinition));
        Assert.That(ConditionalMethodIdentityPolicy.ResolveNativeDeclaration(
            compilation, declaration, declaration.ContainingType.NativeIntegerUnderlyingType!),
            Is.SameAs(expected.OriginalDefinition));
    }

    [Test]
    public void RealVisualBasicCompilationRejectsNativeIdentityResolution()
    {
        var (_, declaration, _) = CreateNative(metadata: false, duplicate: false);
        var visualBasic = VisualBasicCompilation.Create("RealVisualBasicIdentityBoundary");
        Assert.That((Action)(() =>
        {
            _ = ConditionalMethodIdentityPolicy.ResolveNativeDeclaration(
                visualBasic, declaration, declaration.ContainingType.NativeIntegerUnderlyingType!);
        }),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                "Native conditional method compilation is unavailable."));
    }

    [Test]
    public void IncompatibleRealOverloadCannotResolveNativeDeclaration()
    {
        var (compilation, declaration, candidates) = CreateNative(metadata: false, duplicate: false);
        var incompatible = candidates.Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (!method.Parameters.IsEmpty));
        Assert.That((Action)(() =>
        {
            _ = ConditionalMethodIdentityPolicy.ResolveNativeDeclaration(
                compilation, declaration, [incompatible]);
        }),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                "Native conditional method declaration is not unique."));
    }

    [Test]
    public void DistinctCompilerRecoveryDeclarationsRemainAmbiguous()
    {
        var (compilation, declaration, candidates) = CreateNative(metadata: false, duplicate: true);
        Assert.That(candidates, Has.Length.EqualTo(2));
        Assert.That(candidates[0], Is.Not.SameAs(candidates[1]));
        Assert.That(candidates.All(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Parameters.IsEmpty)), Is.True);
        Assert.That((Action)(() =>
        {
            _ = ConditionalMethodIdentityPolicy.ResolveNativeDeclaration(
                compilation, declaration, candidates);
        }),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(
                "Native conditional method declaration is not unique."));
    }

    [Test]
    public void RealEmittedMethodDefinitionTokenResolvesItsHandle()
    {
        using var pe = new PEReader(EmitPlatform("Small", 1));
        var reader = pe.GetMetadataReader();
        var actual = reader.MethodDefinitions.Single();
        Assert.That(ConditionalMethodIdentityPolicy.GetMethodDefinitionHandle(
            reader, MetadataTokens.GetToken(actual)), Is.EqualTo(actual));
    }

    [Test]
    public void RealEmittedTypeDefinitionTokenIsNotAMethod()
    {
        using var pe = new PEReader(EmitPlatform("Small", 1));
        var reader = pe.GetMetadataReader();
        var actualType = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == "Subject");
        Assert.That((Action)(() =>
        {
            _ = ConditionalMethodIdentityPolicy.GetMethodDefinitionHandle(
                reader, MetadataTokens.GetToken(actualType));
        }),
            Throws.TypeOf<BadImageFormatException>().With.Message.EqualTo(
                "Conditional method token is not a MethodDef."));
    }

    [Test]
    public void RealForeignMethodDefinitionTokenOutsideModuleIsRejected()
    {
        using var bigPe = new PEReader(EmitPlatform("Big", 24));
        using var smallPe = new PEReader(EmitPlatform("Small", 1));
        var big = bigPe.GetMetadataReader();
        var small = smallPe.GetMetadataReader();
        var actualForeignMethod = big.MethodDefinitions.Last();
        Assert.That(MetadataTokens.GetRowNumber(actualForeignMethod),
            Is.GreaterThan(small.MethodDefinitions.Count));
        Assert.That((Action)(() =>
        {
            _ = ConditionalMethodIdentityPolicy.GetMethodDefinitionHandle(
                small, MetadataTokens.GetToken(actualForeignMethod));
        }),
            Throws.TypeOf<BadImageFormatException>().With.Message.EqualTo(
                "Conditional method token is outside its module."));
    }

    private static ImmutableArray<byte> EmitPlatform(string name, int count)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        var methods = string.Join(" ", Enumerable.Range(0, count)
            .Select(index => "public static void M" + index + "() { }"));
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { " + methods + " }");
        var compilation = CSharpCompilation.Create(name, [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return Emit(compilation);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Declaration,
        ImmutableArray<IMethodSymbol> Candidates) CreateNative(bool metadata, bool duplicate)
    {
        const string member = "[Diagnostics.Conditional(\"ABSENT\")] public void Trace() { }";
        var members = duplicate ? member + member : member + " public void Trace(Int32 value) { }";
        var options = new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp9);
        var coreTree = CSharpSyntaxTree.ParseText(CoreSource.Replace("TRACE_MEMBERS", members,
            StringComparison.Ordinal), options, "identity-core.cs");
        var source = duplicate
            ? "public static class Subject { public static int Target() { var value = (nint)default(System.IntPtr); return 7; } }"
            : "public static class Subject { public static int Target() { ((nint)default(System.IntPtr)).Trace(); return 7; } }";
        var callerTree = CSharpSyntaxTree.ParseText(source, options, "identity-caller.cs");
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release);
        CSharpCompilation compilation;
        if (metadata)
        {
            var core = CSharpCompilation.Create("IdentityCore", [coreTree], [], compilationOptions);
            var reference = MetadataReference.CreateFromImage(Emit(core));
            compilation = CSharpCompilation.Create("IdentityCaller", [callerTree], [reference], compilationOptions);
        }
        else
        {
            compilation = CSharpCompilation.Create("IdentitySource", [coreTree, callerTree], [], compilationOptions);
        }
        var errors = compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error))
            .Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Id)).ToImmutableArray();
        if (duplicate)
        {
            Assert.That(errors, Is.EqualTo(DuplicateErrors));
            using var rejectedImage = new MemoryStream();
            Assert.That(compilation.Emit(rejectedImage).Success, Is.False);
        }
        else
        {
            Assert.That(errors, Is.Empty);
            Assert.That(Emit(compilation), Is.Not.Empty);
        }
        var model = compilation.GetSemanticModel(callerTree);
        var cast = callerTree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();
        var native = (INamedTypeSymbol)model.GetTypeInfo(cast).Type!;
        Assert.That(native.IsNativeIntegerType, Is.True);
        var declaration = native.GetMembers("Trace").OfType<IMethodSymbol>()
            .First(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Parameters.IsEmpty)).OriginalDefinition;
        var underlying = native.NativeIntegerUnderlyingType!;
        Assert.That(underlying, Is.Not.Null);
        var candidates = underlying.GetMembers("Trace").OfType<IMethodSymbol>().ToImmutableArray();
        return (compilation, declaration, candidates);
    }

    private static ImmutableArray<byte> Emit(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, options: new EmitOptions(runtimeMetadataVersion: "v4.0.30319"));
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return ImmutableArray.Create<byte>(image.GetBuffer().AsSpan(0, checked((int)image.Length)));
    }
}
