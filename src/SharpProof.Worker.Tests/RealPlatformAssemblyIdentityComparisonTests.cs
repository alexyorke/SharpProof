using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class RealPlatformAssemblyIdentityComparisonTests
{
    [Test]
    public void ActualConsoleReferenceAcceptsItsIdentityAndRejectsDifferentRealPlatformToken()
    {
        var compilation = TestCompilation.Create("RealPlatformIdentityComparison", """
            public static class Subject
            {
                public static void Write(int value) => System.Console.WriteLine(value);
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(invocation);
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var consoleIdentity = ((IInvocationOperation)operation!).TargetMethod.ContainingAssembly.Identity;
        var coreIdentity = compilation.GetSpecialType(SpecialType.System_Int32).ContainingAssembly.Identity;

        using var emittedImage = new MemoryStream();
        var emission = compilation.Emit(emittedImage);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        var image = ImmutableArray.Create<byte>(emittedImage.GetBuffer().AsSpan(0, checked((int)emittedImage.Length)));
        using var pe = new PEReader(image);
        var reader = pe.GetMetadataReader();
        var handle = reader.AssemblyReferences.Single(candidate =>
            reader.GetString(reader.GetAssemblyReference(candidate).Name) == consoleIdentity.Name);
        var reference = reader.GetAssemblyReference(handle);
        var token = reader.GetBlobBytes(reference.PublicKeyOrToken).ToImmutableArray();
        const AssemblyFlags contentTypeMask = (AssemblyFlags)0x0e00;
        const AssemblyFlags allowed = AssemblyFlags.PublicKey | AssemblyFlags.Retargetable | contentTypeMask;
        var retargetable = (reference.Flags & AssemblyFlags.Retargetable) != 0;
        var contentType = (AssemblyContentType)(((int)reference.Flags & (int)contentTypeMask) >> 9);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(consoleIdentity, Is.Not.EqualTo(coreIdentity));
            Assert.That(reference.Flags & ~allowed, Is.EqualTo((AssemblyFlags)0));
            Assert.That(reference.Flags & AssemblyFlags.PublicKey, Is.EqualTo((AssemblyFlags)0));
            Assert.That(retargetable, Is.EqualTo(consoleIdentity.IsRetargetable));
            Assert.That(retargetable, Is.EqualTo(coreIdentity.IsRetargetable));
            Assert.That(contentType, Is.EqualTo(consoleIdentity.ContentType));
            Assert.That(contentType, Is.EqualTo(coreIdentity.ContentType));
            Assert.That(token, Is.Not.Empty);
            Assert.That(consoleIdentity.PublicKeyToken.Length, Is.EqualTo(token.Length));
            Assert.That(coreIdentity.PublicKeyToken.Length, Is.EqualTo(token.Length));
            Assert.That(token.SequenceEqual(consoleIdentity.PublicKeyToken), Is.True);
            Assert.That(token.SequenceEqual(coreIdentity.PublicKeyToken), Is.False);
        }

        Assert.That(CompilerTotalIlBodyProvider.TryReadAssemblyIdentity(
            reader, handle, consoleIdentity, out var actual), Is.True);
        Assert.That(actual, Is.EqualTo(consoleIdentity));

        // This is rejected input to the explicit comparison validator. The
        // production provider is never given this different expected identity.
        Assert.That(CompilerTotalIlBodyProvider.TryReadAssemblyIdentity(
            reader, handle, coreIdentity, out var rejected), Is.False);
        Assert.That(rejected, Is.Null);
    }
}
