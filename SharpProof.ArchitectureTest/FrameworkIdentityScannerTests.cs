using NUnit.Framework;
namespace SharpProof.ArchitectureTest;
[TestFixture]
public sealed class FrameworkIdentityScannerTests
{
    [Test]
    public void InventoryReadsConstantAndReadonlyFrameworkNames()
    {
        const string source = """
            public static class FrameworkTypeMetadataNames
            {
                public const string Exception = "System.Exception";
                public const string Split = "System." + "String";
                public static readonly string Monitor = "System.Threading.Monitor";
            }
            """;
        Assert.That(
            FrameworkIdentityScanner.ReadInventory("inventory.cs", source),
            Is.EqualTo([
                "System.Exception",
                "System.String",
                "System.Threading.Monitor"]));
    }
    [Test]
    public void ConstantExpressionsCatchSplitAndInterpolatedIdentities()
    {
        const string source = """
            internal static class Fixture
            {
                private const string Name = "String";
                private const string Split = "Sys" + "tem." + Name;
                private const string Interpolated = $"System.{Name}";
                private static string Read() => Interpolated;
            }
            """;
        var violations = FrameworkIdentityScanner.FindViolations(
            [("fixture.cs", source)],
            ["System.String"],
            []);
        Assert.That(violations.Length, Is.EqualTo(2));
        Assert.That(violations, Has.Some.Contain("System.String"));
    }
    [Test]
    public void NonConstantInterpolatedPrefixIsStillVisible()
    {
        const string source = """
            internal static class Fixture
            {
                private static string Read(string suffix) => $"System.{suffix}";
            }
            """;
        var violations = FrameworkIdentityScanner.FindViolations(
            [("fixture.cs", source)],
            ["System.String"],
            []);
        Assert.That(violations, Has.One.Contain(
            "<interpolated System.* identity>"));
    }

    [Test]
    public void LaterInterpolatedTextAfterAHoleIsNotTreatedAsAPrefix()
    {
        const string source = """
            internal static class Fixture
            {
                private static string Read(int prefix) => $"{prefix}System.String";
            }
            """;
        var violations = FrameworkIdentityScanner.FindViolations(
            [("fixture.cs", source)],
            ["System.String"],
            []);
        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void NonConstantStringConcatenationPrefixIsStillVisible()
    {
        const string source = """
            internal static class Fixture
            {
                private static string Read(string suffix) => "System." + suffix;
            }
            """;
        var violations = FrameworkIdentityScanner.FindViolations(
            [("fixture.cs", source)],
            ["System.String"],
            []);
        Assert.That(violations, Has.One.Contain(
            "<concatenated System.* identity>"));
    }

    [Test]
    public void ConcatenationRequiresTheLeadingSegmentToBeTheFrameworkPrefix()
    {
        const string source = """
            internal static class Fixture
            {
                private static string Leading(string suffix) => "prefix." + suffix;
                private static string Later(string suffix) => suffix + "System.String";
                private static object NonString(string suffix) => "System." + suffix;
            }
            """;
        var violations = FrameworkIdentityScanner.FindViolations(
            [("fixture.cs", source)],
            ["System.String"],
            []);
        Assert.That(violations, Has.One.Contain(
            "<concatenated System.* identity>"));
    }
    [Test]
    public void ConstantHostedInAnyScannedSourceFileIsVisible()
    {
        const string authority = """
            internal static class ExternalAuthority
            {
                internal const string FrameworkId = "System.String";
            }
            """;
        const string consumer = """
            internal static class Consumer
            {
                internal static string Read() => ExternalAuthority.FrameworkId;
            }
            """;
        var violations = FrameworkIdentityScanner.FindViolations(
            [
                ("SharpProof.BuildTasks/Authority.cs", authority),
                ("SharpProof.CompilerArtifact/Consumer.cs", consumer)
            ],
            ["System.String"],
            []);
        Assert.That(violations, Has.Some.Contain(
            "SharpProof.BuildTasks/Authority.cs"));
    }
    [Test]
    public void ApprovedCatalogAndDllAssetNamesRemainOutsideTheGate()
    {
        const string catalog = """
            internal static class Catalog
            {
                internal const string FrameworkType = "System.String";
            }
            """;
        const string assets = """
            internal static class Assets
            {
                internal const string Runtime = "System.Runtime.dll";
            }
            """;
        var violations = FrameworkIdentityScanner.FindViolations(
            [
                ("SharpProof.Specs/DefaultApiSpecCatalog.generated.cs", catalog),
                ("SharpProof.BuildTasks/LauncherRuntimeCompanionInventory.generated.cs", assets)
            ],
            ["System.String", "System.Runtime.dll"],
            ["SharpProof.Specs/DefaultApiSpecCatalog.generated.cs"]);
        Assert.That(violations, Is.Empty);
    }
}
