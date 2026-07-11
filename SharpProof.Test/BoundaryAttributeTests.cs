using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class BoundaryAttributeTests
{
    private static readonly CSharpParseOptions PreviewParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private static readonly Lazy<ImmutableArray<MetadataReference>> Net80ReferencePackReferences =
        new(CreateNet80ReferencePackReferences);

    private static readonly Lazy<MetadataReference> EnforcePureAttributeReference =
        new(() => MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));

    [Test]
    public async Task PureExternal_Method_IsTrustedAtCallSite()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [PureExternal]
    public static int TrustedBoundary() => DateTime.Now.Millisecond;

    [EnforcePure]
    public int Caller() => TrustedBoundary();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureExternal_Method_DoesNotTrustImpureArguments()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [PureExternal]
    public static int TrustedBoundary(int value) => value;

    [EnforcePure]
    public int {|SP0002:Caller|}() => TrustedBoundary(Console.Read());
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Impure_Method_IsImpureAtCallSiteEvenWithPureBody()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [Impure]
    public static int ExplicitlyImpure() => 1;

    [EnforcePure]
    public int {|SP0002:Caller|}() => ExplicitlyImpure();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Impure_WithEnforcePure_ReportsDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [Impure]
    [EnforcePure]
    public int Contradiction() => 1;
}";

        var expectedConflict = VerifyCS.Diagnostic(SharpProofDiagnostics.ConflictingPurityAttributesId)
            .WithSpan(8, 16, 8, 29)
            .WithArguments("Contradiction");
        var expectedImpurity = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(8, 16, 8, 29)
            .WithArguments("Contradiction");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedConflict, expectedImpurity);
    }

    [Test]
    public async Task PureExternal_Property_IsTrustedAtCallSite()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class Boundary
{
    [PureExternal]
    public int Value => DateTime.Now.Millisecond;
}

public class TestClass
{
    [EnforcePure]
    public int Read(Boundary boundary) => boundary.Value;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Impure_Property_IsImpureAtCallSiteEvenWithPureBody()
    {
        var test = @"
using SharpProof.Attributes;

public class Boundary
{
    [Impure]
    public int Value => 1;
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:Read|}(Boundary boundary) => boundary.Value;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureExternal_Constructor_IsTrustedAtCallSite()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class Boundary
{
    [PureExternal]
    public Boundary()
    {
        Console.WriteLine(""trusted externally"");
    }
}

public class TestClass
{
    [EnforcePure]
    public Boundary Create() => new Boundary();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task Impure_Constructor_IsImpureAtCallSiteEvenWithPureBody()
    {
        var test = @"
using SharpProof.Attributes;

public class Boundary
{
    [Impure]
    public Boundary()
    {
    }
}

public class TestClass
{
    [EnforcePure]
    public Boundary {|SP0002:Create|}() => new Boundary();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AssemblyPureExternal_TrustsMethodsByDefault()
    {
        var test = @"
using System;
using SharpProof.Attributes;

[assembly: PureExternal]

public class Boundary
{
    public static int TrustedByAssemblyDefault() => DateTime.Now.Millisecond;
}

public class TestClass
{
    [EnforcePure]
    public int Caller() => Boundary.TrustedByAssemblyDefault();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AssemblyImpure_MarksMethodsImpureByDefault()
    {
        var test = @"
using SharpProof.Attributes;

[assembly: Impure]

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:Caller|}() => 1;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AssemblyImpure_DirectPureExternal_OverridesDefaultAtCallSite()
    {
        var boundaryReference = CreateBoundaryReference(
            "ImpureDefaultWithDirectPureExternal",
            @"
using System;
using SharpProof.Attributes;

[assembly: Impure]

public static class Boundary
{
    [PureExternal]
    public static int TrustedOverride() => DateTime.Now.Millisecond;
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller() => Boundary.TrustedOverride();
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task AssemblyPureExternal_DirectImpure_OverridesDefaultAtCallSite()
    {
        var boundaryReference = CreateBoundaryReference(
            "PureExternalDefaultWithDirectImpure",
            @"
using SharpProof.Attributes;

[assembly: PureExternal]

public static class Boundary
{
    [Impure]
    public static int ExplicitlyImpure() => 1;
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:Caller|}() => Boundary.ExplicitlyImpure();
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task MetadataOnlyExternalMethodWithoutBoundaryAttribute_IsConservative()
    {
        var boundaryReference = CreateBoundaryReference(
            "UntrustedExternalBoundary",
            @"
public static class Boundary
{
    private static int _counter;

    public static int Next() => ++_counter;
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:Caller|}() => Boundary.Next();
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task ExternalJetBrainsPureAttribute_OverridesImpureNamespaceAtCallSite()
    {
        var boundaryReference = CreateBoundaryReference(
            "JetBrainsPureBoundary",
            @"
namespace JetBrains.Annotations
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class PureAttribute : System.Attribute
    {
    }
}

namespace System.IO
{
    public static class TrustedSdk
    {
        [JetBrains.Annotations.Pure]
        public static int StableLength(string value) => value.Length;
    }
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller(string value) => System.IO.TrustedSdk.StableLength(value);
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task ExternalContractsPureAttribute_OverridesImpureNamespaceAtCallSite()
    {
        var boundaryReference = CreateBoundaryReference(
            "ContractsPureBoundary",
            @"
namespace System.Diagnostics.Contracts
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class PureAttribute : System.Attribute
    {
    }
}

namespace System.IO
{
    public static class TrustedContractsSdk
    {
        [System.Diagnostics.Contracts.Pure]
        public static int StableLength(string value) => value.Length;
    }
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller(string value) => System.IO.TrustedContractsSdk.StableLength(value);
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task ExternalJetBrainsPureGetterAttribute_OverridesImpureNamespaceAtCallSite()
    {
        var boundaryReference = CreateBoundaryReference(
            "JetBrainsPureGetterBoundary",
            @"
namespace JetBrains.Annotations
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Property)]
    public sealed class PureAttribute : System.Attribute
    {
    }
}

namespace System.IO
{
    public static class TrustedGetterSdk
    {
        public static int StableValue
        {
            [JetBrains.Annotations.Pure]
            get => 42;
        }
    }
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller() => System.IO.TrustedGetterSdk.StableValue;
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task AssemblyPureExternal_PropertyInImpureNamespace_IsTrustedAtCallSite()
    {
        var boundaryReference = CreateBoundaryReference(
            "AssemblyPureExternalPropertyBoundary",
            @"
using System;
using SharpProof.Attributes;

[assembly: PureExternal]

namespace System.IO
{
    public static class TrustedAssemblyPropertySdk
    {
        public static int StableValue => DateTime.Now.Millisecond;
    }
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller() => System.IO.TrustedAssemblyPropertySdk.StableValue;
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task PureExternalConstructorInImpureNamespace_IsTrustedAtCallSite()
    {
        var boundaryReference = CreateBoundaryReference(
            "PureExternalConstructorImpureNamespaceBoundary",
            @"
using SharpProof.Attributes;

namespace System.IO
{
    public sealed class TrustedConstructedSdk
    {
        [PureExternal]
        public TrustedConstructedSdk()
        {
        }
    }
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object Caller() => new System.IO.TrustedConstructedSdk();
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    [Test]
    public async Task SourcePureConstructorInImpureNamespace_DoesNotPoisonCaller()
    {
        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

namespace System.IO
{
    public sealed class SourceTrustedConstructedSdk
    {
        public {|SP0004:SourceTrustedConstructedSdk|}()
        {
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public object Caller() => new System.IO.SourceTrustedConstructedSdk();
}");

        await verifier.RunAsync();
    }

    [Test]
    public async Task SourceImpureConstructorInImpureNamespace_RemainsImpureAtCallSite()
    {
        var verifier = CreateVerifier(@"
using System;
using SharpProof.Attributes;

namespace System.IO
{
    public sealed class SourceImpureConstructedSdk
    {
        public SourceImpureConstructedSdk()
        {
            _ = DateTime.Now;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:Caller|}() => new System.IO.SourceImpureConstructedSdk();
}");

        await verifier.RunAsync();
    }

    [Test]
    public async Task PropertyLevelPureExternal_TrustsGetterButNotSetter()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Boundary
{
    [PureExternal]
    public int Value
    {
        get => DateTime.Now.Millisecond;
        set => Console.WriteLine(value);
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int Read(Boundary boundary) => boundary.Value;

    [EnforcePure]
    public void {|SP0002:Write|}(Boundary boundary) => boundary.Value = 1;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PropertyLevelImpure_AppliesToBothAccessors()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Boundary
{
    [Impure]
    public int Value { get; set; }
}

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:Read|}(Boundary boundary) => boundary.Value;

    [EnforcePure]
    public void {|SP0002:Write|}(Boundary boundary) => boundary.Value = 1;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AccessorLevelPolicy_AppliesOnlyToAttributedAccessor()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class GetterBoundary
{
    public int Value
    {
        [PureExternal] get => DateTime.Now.Millisecond;
        set => Console.WriteLine(value);
    }
}

public sealed class SetterBoundary
{
    public int Value
    {
        [EnforcePure] get => 1;
        [Impure] set { }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public int ReadTrustedGetter(GetterBoundary boundary) => boundary.Value;

    [EnforcePure]
    public void {|SP0002:WriteUntrustedSetter|}(GetterBoundary boundary) => boundary.Value = 1;

    [EnforcePure]
    public int ReadUnattributedGetter(SetterBoundary boundary) => boundary.Value;

    [EnforcePure]
    public void {|SP0002:WriteImpureSetter|}(SetterBoundary boundary) => boundary.Value = 1;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task MetadataEnumeratorRuntimeMembersWithoutEvidence_AreConservative()
    {
        var boundaryReference = CreateBoundaryReference(
            "ExternalEnumeratorBoundary",
            @"
using SharpProof.Attributes;

public sealed class ExternalSequence
{
    [PureExternal]
    public ExternalEnumerator GetEnumerator() => new ExternalEnumerator();
}

public sealed class ExternalEnumerator
{
    public int Current => 1;
    public bool MoveNext() => false;
}");

        var verifier = CreateVerifier(@"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:Read|}(ExternalSequence values)
    {
        foreach (var value in values)
        {
        }
    }
}");
        verifier.TestState.AdditionalReferences.Add(boundaryReference);

        await verifier.RunAsync();
    }

    private static VerifyCS.Test CreateVerifier(string source)
    {
        var verifier = new VerifyCS.Test
        {
            TestCode = source
        };

        verifier.TestState.AdditionalReferences.Add(EnforcePureAttributeReference.Value);
        return verifier;
    }

    private static MetadataReference CreateBoundaryReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, PreviewParseOptions) },
            Net80ReferencePackReferences.Value.Add(EnforcePureAttributeReference.Value),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));

        stream.Position = 0;
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static ImmutableArray<MetadataReference> CreateNet80ReferencePackReferences()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        Assert.That(runtimeDirectory, Is.Not.Null.And.Not.Empty);

        var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDirectory!, "..", "..", ".."));
        var referencePackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var net8ReferenceDirectory = Directory.GetDirectories(referencePackRoot)
            .Select(path => Path.Combine(path, "ref", "net8.0"))
            .Where(Directory.Exists)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        Assert.That(net8ReferenceDirectory, Is.Not.Null.And.Not.Empty);

        return Directory.GetFiles(net8ReferenceDirectory!, "*.dll")
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToImmutableArray();
    }
}
