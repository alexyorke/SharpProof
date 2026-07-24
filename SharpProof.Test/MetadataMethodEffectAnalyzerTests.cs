using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class MetadataMethodEffectAnalyzerTests {
    private string _fixturePath = string.Empty;
    private string _dependencyFixturePath = string.Empty;
    private string _bridgeFixturePath = string.Empty;
    [OneTimeSetUp]
    public void BuildFixture() {
        _fixturePath = Path.Combine(Path.GetTempPath(), "SharpProof.MetadataFixture." + Guid.NewGuid().ToString("N") + ".dll");
        const string source = """
            namespace MetadataFixture;
            public sealed class MutableBox {
                public int Value;
            }
            public static class Effects {
                public static int State;
                public static volatile int VolatileState;
                public static void ElementWrite(int[] values) { values[0] = 1; }
                public static int[] FreshElementWrite() {
                    var values = new int[1];
                    values[0] = 1;
                    return values;
                }
                public static void FieldWrite(MutableBox value) { value.Value = 1; }
                public static unsafe void IndirectWrite(int* value) { *value = 1; }
                public static void CopyBlockOpcodeFixture() { VolatileState = 1; }
                private static void Helper() { State++; }
                public static void RepeatHelper() { Helper(); Helper(); }
                public static void Recursive() { Recursive(); }
                public static object Box(int value) => value;
                public static void ThrowAndRethrow() {
                    try { throw new System.Exception(); }
                    catch { throw; }
                    finally { State++; }
                }
                private static bool Filter(System.Exception exception) => exception != null;
                public static bool FilteredCatch() {
                    try { throw new System.Exception(); }
                    catch (System.Exception exception) when (Filter(exception)) { return true; }
                }
                public static unsafe bool UnsupportedOpcode() {
                    int* values = stackalloc int[1];
                    return values != null;
                }
                private static int Identity(int value) => value;
                public static unsafe int IndirectCall(int value) {
                    delegate*<int, int> target = &Identity;
                    return target(value);
                }
            }
            public class WithEffect {
                public WithEffect() { Effects.State++; }
            }
            public class VirtualBase {
                public virtual void Work() { }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "SharpProof.MetadataFixture",
            [syntaxTree],
            SymbolicSourceCompilation.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: true));
        var emit = compilation.Emit(_fixturePath);
        Assert.That(emit.Success, Is.True, string.Join(Environment.NewLine, emit.Diagnostics));
        RewriteVolatilePrefixAsCpblk(_fixturePath);
        _dependencyFixturePath = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.MetadataDependency." + Guid.NewGuid().ToString("N") + ".dll");
        _bridgeFixturePath = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.MetadataBridge." + Guid.NewGuid().ToString("N") + ".dll");
        EmitFixture(
            _dependencyFixturePath,
            "SharpProof.MetadataDependency",
            """
            namespace MetadataDependency;
            public static class PureLeaf {
                public static int State;
                public static int Increment(int value) => value + 1;
                public static void Mutate() { State++; }
            }
            public static class PureBox<T> {
                public static T Identity(T value) => value;
            }
            """);
        EmitFixture(
            _bridgeFixturePath,
            "SharpProof.MetadataBridge",
            """
            namespace MetadataBridge;
            public static class PureBridge {
                public static int Increment(int value) => MetadataDependency.PureLeaf.Increment(value);
                public static int GenericIdentity(int value) => MetadataDependency.PureBox<int>.Identity(value);
                public static void Mutate() { MetadataDependency.PureLeaf.Mutate(); }
            }
            """,
            MetadataReference.CreateFromFile(_dependencyFixturePath));
    }
    [OneTimeTearDown]
    public void DeleteFixture() {
        if (File.Exists(_fixturePath)) File.Delete(_fixturePath);
        if (File.Exists(_dependencyFixturePath)) File.Delete(_dependencyFixturePath);
        if (File.Exists(_bridgeFixturePath)) File.Delete(_bridgeFixturePath);
    }
    [Test]
    public void ElementAndIndirectWritesCannotBeCertifiedPure() {
        var element = Analyze("static void M(int[] values) => MetadataFixture.Effects.ElementWrite(values);");
        var indirect = Analyze("static unsafe void M(int* value) => MetadataFixture.Effects.IndirectWrite(value);");
        Assert.Multiple(() => {
            Assert.That(element.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(element.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(indirect.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(indirect.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void ElementWritesToFreshMetadataArraysRemainPure() {
        var effects = Analyze("static int[] M() => MetadataFixture.Effects.FreshElementWrite();");
        Assert.Multiple(() => {
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(effects.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
        });
    }
    [Test]
    public void MetadataFieldWritesThroughArgumentsAreArgumentEffects() {
        var effects = Analyze(
            "static void M(MetadataFixture.MutableBox value) => MetadataFixture.Effects.FieldWrite(value);");
        Assert.Multiple(() => {
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesReceiverState), Is.False);
        });
    }
    [Test]
    public void RepeatedHelperCallsAreMemoizedRatherThanClassifiedAsRecursion() {
        var effects = Analyze("static void M() => MetadataFixture.Effects.RepeatHelper();");
        Assert.Multiple(() => {
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
            Assert.That(effects.UnknownReasons, Has.None.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("metadata_recursive_cycle"));
        });
    }
    [Test]
    public void ActualMetadataRecursionRemainsUnknown() {
        var effects = Analyze("static void M() => MetadataFixture.Effects.Recursive();");
        Assert.That(effects.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message))
            .EqualTo("metadata_recursive_cycle"));
    }
    [Test]
    public void MetadataConstructorEffectsAreTraversed() {
        var effects = Analyze("static object M() => new MetadataFixture.WithEffect();");
        Assert.Multiple(() => {
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.Allocates), Is.True);
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
        });
    }
    [Test]
    public void BoxingAndExceptionRegionsAreConservative() {
        var boxing = Analyze("static object M(int value) => MetadataFixture.Effects.Box(value);");
        var exceptionRegion = Analyze("static void M() => MetadataFixture.Effects.ThrowAndRethrow();");
        var exceptionFilter = Analyze("static bool M() => MetadataFixture.Effects.FilteredCatch();");
        Assert.Multiple(() => {
            Assert.That(boxing.Effects.HasFlag(SharpProofEffect.Allocates), Is.True);
            Assert.That(boxing.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(exceptionRegion.DoesNotThrow, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(exceptionRegion.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("metadata_exception_regions_unsupported"));
            Assert.That(exceptionFilter.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("metadata_exception_regions_unsupported"));
        });
    }
    [Test]
    public void UnsupportedAndIndirectOpcodesRemainUnknown() {
        var unsupported = Analyze("static bool M() => MetadataFixture.Effects.UnsupportedOpcode();");
        var indirectCall = Analyze("static int M(int value) => MetadataFixture.Effects.IndirectCall(value);");
        var copyBlock = Analyze("static void M() => MetadataFixture.Effects.CopyBlockOpcodeFixture();");
        Assert.Multiple(() => {
            Assert.That(unsupported.Effects.HasFlag(SharpProofEffect.UnsupportedOperation), Is.True);
            Assert.That(unsupported.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
            Assert.That(indirectCall.Effects.HasFlag(SharpProofEffect.UnsupportedOperation), Is.True);
            Assert.That(indirectCall.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
            Assert.That(copyBlock.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(copyBlock.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("metadata_indirect_write_origin_unknown"));
        });
    }
    [Test]
    public void MetadataVirtualDispatchCannotBeProvenExact() {
        var effects = Analyze("static void M(MetadataFixture.VirtualBase value) => value.Work();");
        Assert.Multiple(() => {
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
        });
    }
    [Test]
    public void ManagedCallsAcrossReferencedAssembliesAreAnalyzedRecursively() {
        var effects = Analyze("static int M(int value) => MetadataBridge.PureBridge.Increment(value);");
        Assert.Multiple(() => {
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(effects.AllocationFree, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(effects.UnknownReasons, Has.None.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("metadata_external_call_unresolved"));
        });
    }
    [Test]
    public void EffectsFromManagedCallsAcrossReferencedAssembliesArePreserved() {
        var effects = Analyze("static void M() => MetadataBridge.PureBridge.Mutate();");
        Assert.Multiple(() => {
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
            Assert.That(effects.UnknownReasons, Has.None.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("metadata_external_call_unresolved"));
        });
    }
    [Test]
    public void CallsOnConstructedGenericTypesAreAnalyzedRecursively() {
        var effects = Analyze("static int M(int value) => MetadataBridge.PureBridge.GenericIdentity(value);");
        Assert.Multiple(() => {
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(effects.AllocationFree, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(effects.UnknownReasons, Has.None.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("metadata_external_call_unresolved"));
        });
    }
    private MethodEffects Analyze(string method) {
        var reference = MetadataReference.CreateFromFile(_fixturePath);
        var references = SymbolicSourceCompilation.GetTrustedPlatformReferences()
            .Add(reference)
            .Add(MetadataReference.CreateFromFile(_dependencyFixturePath))
            .Add(MetadataReference.CreateFromFile(_bridgeFixturePath));
        var source = "public static class Query { " + method + " }";
        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            source,
            "MetadataQuery.cs",
            "MetadataQuery.cs",
            "SharpProof.MetadataQuery",
            references,
            CancellationToken.None);
        var declaration = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var symbol = semanticModel.GetDeclaredSymbol(declaration)!;
        return new MethodEffectAnalysisSession(compilation, CancellationToken.None)
            .Analyze(symbol, declaration, semanticModel);
    }
    private static void EmitFixture(
        string path,
        string assemblyName,
        string source,
        params MetadataReference[] additionalReferences) {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = SymbolicSourceCompilation.GetTrustedPlatformReferences().AddRange(additionalReferences);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        var emit = compilation.Emit(path);
        Assert.That(emit.Success, Is.True, string.Join(Environment.NewLine, emit.Diagnostics));
    }
    private static void RewriteVolatilePrefixAsCpblk(string path) {
        var image = File.ReadAllBytes(path);
        for (var index = 0; index < image.Length - 2; index++) {
            if (image[index] != 0xFE || image[index + 1] != 0x13 || image[index + 2] != 0x80) continue;
            image[index + 1] = 0x17;
            File.WriteAllBytes(path, image);
            return;
        }
        Assert.Fail("The metadata fixture did not contain the expected volatile/stsfld IL sequence.");
    }
}
