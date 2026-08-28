using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ContractApiIdentityAnalyzerTests
{
    [Test]
    public async Task SourceShadowedRuntimeClauseIsVisiblyIncomplete()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool condition) {
                        System.Console.WriteLine(condition);
                    }
                    public static void Ensures(bool condition) {
                        System.Console.WriteLine(condition);
                    }
                    public static void Assume(bool condition) {
                        System.Console.WriteLine(condition);
                    }
                }
            }
            public static class Subject {
                public static int Read(int value) {
                    SharpProof.Attributes.Contract.Ensures(value > 0);
                    return value;
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRejected(diagnostics, "Read");
    }

    [Test]
    public async Task SourceShadowedClosedAttributeIsVisiblyIncomplete()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.Parameter |
                    System.AttributeTargets.ReturnValue)]
                public sealed class NotNullAttribute :
                    System.Attribute {
                }
            }
            public static class Subject {
                [return: SharpProof.Attributes.NotNull]
                public static string Read() => "value";
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRejected(diagnostics, "Read");
    }

    [Test]
    public async Task SourceShadowedEffectAttributeIsVisiblyIncomplete()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.Method)]
                public sealed class EnforcePureAttribute :
                    System.Attribute {
                }
            }
            public static class Subject {
                [SharpProof.Attributes.EnforcePure]
                public static async System.Threading.Tasks.Task Read<T>() {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            """;

        var diagnostics = await Analyze(
            source,
            features: "effects");

        AssertRejected(diagnostics, "Read");
    }

    [Test]
    public async Task UnrelatedLookalikeAttributeRemainsUnselected()
    {
        const string source =
            """
            namespace Lookalike {
                public sealed class NotNullAttribute :
                    System.Attribute {
                }
            }
            public static class Subject {
                [return: Lookalike.NotNull]
                public static string Read() => "value";
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task MatchingPackageAttributeRemainsAdmitted()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                [return: NotNull]
                public static string Read() => "value";
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task SourceShadowedControlAttributesReportOnEveryDeclaredScope()
    {
        const string source =
            """
            [assembly: SharpProof.Attributes.SharpProofTrusted("assembly")]

            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.Assembly |
                    System.AttributeTargets.Class |
                    System.AttributeTargets.Method,
                    AllowMultiple = true)]
                public sealed class SharpProofSuppressAttribute :
                    System.Attribute {
                    public SharpProofSuppressAttribute(string reason) { }
                }

                [System.AttributeUsage(
                    System.AttributeTargets.Assembly |
                    System.AttributeTargets.Class |
                    System.AttributeTargets.Method,
                    AllowMultiple = true)]
                public sealed class SharpProofTrustedAttribute :
                    System.Attribute {
                    public SharpProofTrustedAttribute(string reason) { }
                }
            }

            [SharpProof.Attributes.SharpProofSuppress("empty")]
            public sealed class Empty { }

            [SharpProof.Attributes.SharpProofTrusted("outer")]
            public sealed class Outer {
                [SharpProof.Attributes.SharpProofSuppress("nested")]
                public sealed class Nested { }
            }

            [SharpProof.Attributes.SharpProofSuppress("partial-one")]
            public partial class Partial { }
            [SharpProof.Attributes.SharpProofTrusted("partial-two")]
            public partial class Partial { }

            [SharpProof.Attributes.SharpProofSuppress("with-method")]
            public sealed class WithMethod {
                public void Method() { }
            }
            """;

        var diagnostics = await Analyze(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(Enumerable.Repeat("SP0047", 7)));
            Assert.That(
                diagnostics.Select(diagnostic => diagnostic.GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture)),
                Has.All.Contain("ContractApiIdentityRejected"));
        }
    }

    [Test]
    public async Task ReferencedLookalikeControlAttributesReportWithoutMethods()
    {
        var lookalike = AnalyzerTestHost.EmitReference(
            """
            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.Assembly |
                    System.AttributeTargets.Class)]
                public sealed class SharpProofSuppressAttribute :
                    System.Attribute {
                    public SharpProofSuppressAttribute(string reason) { }
                }

                [System.AttributeUsage(
                    System.AttributeTargets.Assembly |
                    System.AttributeTargets.Class)]
                public sealed class SharpProofTrustedAttribute :
                    System.Attribute {
                    public SharpProofTrustedAttribute(string reason) { }
                }
            }
            """,
            "ShadowControlAttributes").WithAliases(["shadow"]);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            extern alias shadow;
            [assembly: shadow::SharpProof.Attributes.SharpProofTrusted("assembly")]

            [shadow::SharpProof.Attributes.SharpProofSuppress("type")]
            public sealed class Empty { }
            """,
            mode: null,
            enabledIds: ["SP0047"],
            additionalReferences: [lookalike],
            profile: "advisory",
            features: "contracts");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047", "SP0047"]));
    }

    [Test]
    public async Task ExactAndGeneratedControlAttributePoliciesRemainStable()
    {
        var exact = await Analyze(
            """
            using SharpProof.Attributes;
            [assembly: SharpProofTrusted("assembly")]

            [SharpProofSuppress("type")]
            public sealed class Empty { }
            """);
        var generatedLookalike = await AnalyzerTestHost.AnalyzeAsync(
            """
            namespace SharpProof.Attributes {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class SharpProofSuppressAttribute :
                    System.Attribute {
                    public SharpProofSuppressAttribute(string reason) { }
                }
            }

            [SharpProof.Attributes.SharpProofSuppress("generated")]
            internal sealed class GeneratedEmpty { }
            """,
            mode: null,
            enabledIds: ["SP0047"],
            profile: "advisory",
            features: "contracts",
            filePath: "ControlAttributes.cs");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exact, Is.Empty);
            Assert.That(
                generatedLookalike.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0047"]));
        }
    }

    [Test]
    public async Task RejectedReturnAttributeCannotProveCallerEffectTransitively()
    {
        const string source =
            """
            using SharpProof.Attributes;

            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.ReturnValue)]
                public sealed class NotNullAttribute :
                    System.Attribute {
                }
            }

            public static class Subject {
                [return: SharpProof.Attributes.NotNull]
                private static string MaybeNull(bool condition) {
                    return condition ? "" : null!;
                }

                [DoesNotThrow]
                public static int Call(bool condition) {
                    return MaybeNull(condition).Length;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: null,
            enabledIds: ["SP0046", "SP0047"],
            profile: "advisory",
            features: "all");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EquivalentTo(["SP0047", "SP0047"]));
        var messages = diagnostics.Select(static diagnostic =>
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                messages.Any(static message =>
                    message.Contains("MaybeNull", StringComparison.Ordinal) &&
                    message.Contains("ContractApiIdentityRejected", StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                messages.Any(static message =>
                    message.Contains("Call", StringComparison.Ordinal) &&
                    message.Contains("UnsupportedOperationShape", StringComparison.Ordinal)),
                Is.True);
        }
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(
        string source,
        string features = "contracts")
    {
        return AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: null,
            enabledIds: ["SP0047"],
            profile: "advisory",
            features: features);
    }

    private static void AssertRejected(
        ImmutableArray<Diagnostic> diagnostics,
        string method)
    {
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        var message = diagnostics.Single().GetMessage(
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(
            message,
            Does.Contain(method)
                .And.Contain("ContractApiIdentityRejected"));
    }

    [Test]
    public async Task UnreadableContractApiIsReportedInsteadOfSilentlyDisablingAnalysis()
    {
        const string source = """
            using SharpProof.Attributes;

            public static class Subject {
                public static long Identity(long value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """;
        var attributesPath =
            typeof(SharpProof.Attributes.Contract).Assembly.Location;
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SharpProofUnreadable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var copied = Path.Combine(directory, "SharpProof.Attributes.dll");
        File.Copy(attributesPath, copied);

        // Reference the copy, then delete it. Roslyn has already read the image,
        // so the compilation is intact, but the payload pin can no longer read
        // the path -- the same shape as an antivirus scanner or a dropped share.
        var reference = MetadataReference.CreateFromFile(copied);
        File.Delete(copied);
        Directory.Delete(directory);

        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(static path => !string.Equals(
                Path.GetFileName(path),
                "SharpProof.Attributes.dll",
                StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .Append(reference);
        var compilation = CSharpCompilation.Create(
            "UnreadableContractApiFixture",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithSpecificDiagnosticOptions(
                    new SharpProofAnalyzer().SupportedDiagnostics.ToImmutableDictionary(
                        static descriptor => descriptor.Id,
                        static descriptor => descriptor.Id == "SP0050"
                            ? ReportDiagnostic.Warn
                            : ReportDiagnostic.Suppress,
                        StringComparer.Ordinal)));

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0050"));
    }

    [Test]
    public async Task ReadableWrongPayloadReportsRejectedIdentityInsteadOfSilence()
    {
        const string source = """
            using SharpProof.Attributes;

            public static class Subject {
                public static long Identity(long value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """;
        var attributesPath =
            typeof(SharpProof.Attributes.Contract).Assembly.Location;
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SharpProofWrongPayload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var copied = Path.Combine(directory, "SharpProof.Attributes.dll");
        File.Copy(attributesPath, copied);
        await using (var stream = new FileStream(
                         copied,
                         FileMode.Append,
                         FileAccess.Write,
                         FileShare.None))
        {
            await stream.WriteAsync(new byte[] { 0x5a });
        }

        try
        {
            var wrongPayload = MetadataReference.CreateFromFile(copied);
            var trustedPlatformAssemblies =
                (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
                throw new InvalidOperationException(
                    "Trusted platform assemblies are unavailable.");
            var references = trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Where(static path => !string.Equals(
                    Path.GetFileName(path),
                    "SharpProof.Attributes.dll",
                    StringComparison.OrdinalIgnoreCase))
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .Append(wrongPayload);
            var compilation = CSharpCompilation.Create(
                "WrongPayloadContractApiFixture",
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithSpecificDiagnosticOptions(
                        new SharpProofAnalyzer().SupportedDiagnostics
                            .ToImmutableDictionary(
                                static descriptor => descriptor.Id,
                                static descriptor => descriptor.Id == "SP0047"
                                    ? ReportDiagnostic.Warn
                                    : ReportDiagnostic.Suppress,
                                StringComparer.Ordinal)));

            var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
                compilation,
                mode: null,
                profile: "advisory",
                features: "contracts");

            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0047"]));
            Assert.That(
                diagnostics.Single().GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture),
                Does.Contain("Identity")
                    .And.Contain("ContractApiIdentityRejected"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task ReadableRejectedMetadataPreconditionsAreReportedAtEveryCallSite()
    {
        var attributesPath =
            typeof(SharpProof.Attributes.Contract).Assembly.Location;
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SharpProofRejectedMetadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var copied = Path.Combine(directory, "SharpProof.Attributes.dll");
        File.Copy(attributesPath, copied);
        await using (var stream = new FileStream(
                         copied,
                         FileMode.Append,
                         FileAccess.Write,
                         FileShare.None))
        {
            await stream.WriteAsync(new byte[] { 0x5a });
        }

        try
        {
            var wrongPayload = MetadataReference.CreateFromFile(copied);
            var platform = GetPlatformReferences();
            var contractLibrary = CSharpCompilation.Create(
                "RejectedMetadataContractLibrary",
                [CSharpSyntaxTree.ParseText(
                    """
                    using SharpProof.Attributes;
                    public static class ExternalContract {
                        public static int Read([Positive] int value) => value;
                    }
                    """)],
                platform.Append(wrongPayload),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var external = MetadataReference.CreateFromImage(
                AnalyzerTestHost.EmitImage(contractLibrary));
            var consumer = CSharpCompilation.Create(
                "RejectedMetadataConsumer",
                [
                    CSharpSyntaxTree.ParseText(
                        """
                        public static class Subject {
                            public static int Read(int value) {
                                var first = ExternalContract.Read(value);
                                return first + ExternalContract.Read(value);
                            }
                        }
                        """),
                    CSharpSyntaxTree.ParseText(
                        """
                        // <auto-generated/>
                        internal static class GeneratedSubject {
                            internal static int Read(int value) =>
                                ExternalContract.Read(value);
                        }
                        """,
                        path: "Rejected.Metadata.g.cs")
                ],
                platform.Append(wrongPayload).Append(external),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithSpecificDiagnosticOptions(
                        new SharpProofAnalyzer().SupportedDiagnostics
                            .ToImmutableDictionary(
                                static descriptor => descriptor.Id,
                                static descriptor => descriptor.Id == "SP0047"
                                    ? ReportDiagnostic.Warn
                                    : ReportDiagnostic.Suppress,
                                StringComparer.Ordinal)));

            var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
                consumer,
                mode: null,
                profile: "advisory",
                features: "contracts");

            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0047", "SP0047"]));
            Assert.That(
                diagnostics.All(static diagnostic => diagnostic.Location.IsInSource),
                Is.True);
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture)),
                Has.All.Contain("ContractApiIdentityRejected"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task TrustedSourceAndUnrelatedLookalikeCallsDoNotReportRejectedApi()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            namespace Lookalike {
                public sealed class PositiveAttribute : System.Attribute { }
            }
            public static class Subject {
                private static int Trusted(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
                private static int Unrelated([Lookalike.Positive] int value) => value;
                public static int Read(int value) =>
                    Trusted(value) + Unrelated(value);
            }
            """,
            mode: null,
            enabledIds: ["SP0027", "SP0047"],
            profile: "advisory",
            features: "contracts");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0047"));
    }

    [Test]
    public async Task RejectedResultAndOldIntrinsicsAreReported()
    {
        var lookalike = AnalyzerTestHost.EmitReference(
            """
            namespace SharpProof.Attributes
            {
                public static class Contract
                {
                    public static T Result<T>() => default!;
                    public static T Old<T>(T value) => value;
                }
            }
            """,
            "RejectedIntrinsics").WithAliases(["rejected"]);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            extern alias rejected;

            public static class Subject
            {
                public static int Read(int value)
                {
                    var result = rejected::SharpProof.Attributes.Contract.Result<int>();
                    return result + rejected::SharpProof.Attributes.Contract.Old(value);
                }
            }
            """,
            mode: null,
            enabledIds: ["SP0047"],
            additionalReferences: [lookalike],
            profile: "advisory",
            features: "contracts");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        Assert.That(
            diagnostics.Single().GetMessage(
                System.Globalization.CultureInfo.InvariantCulture),
            Does.Contain("ContractApiIdentityRejected"));
    }

    private static IEnumerable<MetadataReference> GetPlatformReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(static path => !string.Equals(
                Path.GetFileName(path),
                "SharpProof.Attributes.dll",
                StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path));
    }
}
