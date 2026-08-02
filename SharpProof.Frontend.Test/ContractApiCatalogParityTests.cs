using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiCatalogParityTests
{
    [Test]
    public void CatalogExactlyMatchesTheExportedContractApi()
    {
        var methods = typeof(Contract).GetMethods(
            BindingFlags.Public |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly);
        var attributes = typeof(Contract).Assembly.GetExportedTypes()
            .Where(static type =>
                !type.IsAbstract &&
                typeof(Attribute).IsAssignableFrom(type))
            .Select(static type => type.FullName)
            .OrderBy(static name => name, StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                methods.Select(static method => method.Name)
                    .OrderBy(static name => name, StringComparer.Ordinal),
                Is.EqualTo(ContractApiMetadata.ContractMethodCandidateNames
                    .OrderBy(static name => name, StringComparer.Ordinal)));
            Assert.That(
                attributes,
                Is.EqualTo(ContractApiMetadata.AttributeMetadataNames
                    .OrderBy(static name => name, StringComparer.Ordinal)));
        }

        foreach (var descriptor in ContractApiMetadata.Methods)
        {
            var method = methods.Single(candidate =>
                candidate.Name == descriptor.Name);
            AssertMethodShape(method, descriptor);
        }
    }

    [TestCase(
        "duplicate",
        "contains duplicate property 'schemaVersion'")]
    [TestCase(
        "unknown",
        "contains unsupported property 'unknownProperty'")]
    [TestCase(
        "shape",
        "must be one of: Clause, Old, Result")]
    public async Task GeneratorRejectsMalformedCatalogs(
        string mutation,
        string expectedError)
    {
        var repository = RepositoryRoot();
        var catalog = await File.ReadAllTextAsync(Path.Combine(
            repository,
            "SharpProof.Frontend",
            "ContractApi.catalog.json"));
        catalog = mutation switch
        {
            "duplicate" => catalog.Replace(
                "\"schemaVersion\": 1,",
                "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,",
                StringComparison.Ordinal),
            "unknown" => catalog.Replace(
                "\"schemaVersion\": 1,",
                "\"schemaVersion\": 1,\n  \"unknownProperty\": true,",
                StringComparison.Ordinal),
            "shape" => catalog.Replace(
                "\"shape\": \"Clause\"",
                "\"shape\": \"Invalid\"",
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        var temporaryDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "contract-api-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var catalogPath = Path.Combine(temporaryDirectory, "catalog.json");
            var outputPath = Path.Combine(temporaryDirectory, "generated.cs");
            await File.WriteAllTextAsync(catalogPath, catalog);
            var start = new ProcessStartInfo("pwsh")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (var argument in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(repository, "scripts", "Generate-ContractApiCatalog.ps1"),
                "-CatalogPath",
                catalogPath,
                "-OutputPath",
                outputPath
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ??
                throw new InvalidOperationException(
                    "The catalog generator process did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await standardOutput + await standardError;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(process.ExitCode, Is.Not.Zero, output);
                Assert.That(output, Does.Contain(expectedError));
                Assert.That(File.Exists(outputPath), Is.False);
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static void AssertMethodShape(
        MethodInfo method,
        ContractApiMethodDescriptor descriptor)
    {
        var parameters = method.GetParameters();
        switch (descriptor.Shape)
        {
            case ContractApiMethodShape.Clause:
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
                    Assert.That(parameters, Has.Length.EqualTo(1));
                    Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(bool)));
                    Assert.That(method.IsGenericMethodDefinition, Is.False);
                    Assert.That(
                        method.GetCustomAttributes<ConditionalAttribute>()
                            .Select(static attribute => attribute.ConditionString),
                        Is.EqualTo([Contract.ConditionalSymbol]));
                }
                break;
            case ContractApiMethodShape.Old:
                AssertGenericIntrinsic(method, parameters, parameterCount: 1);
                break;
            case ContractApiMethodShape.Result:
                AssertGenericIntrinsic(method, parameters, parameterCount: 0);
                break;
            default:
                Assert.Fail("The catalog contains an unknown method shape.");
                break;
        }
    }

    private static void AssertGenericIntrinsic(
        MethodInfo method,
        ParameterInfo[] parameters,
        int parameterCount)
    {
        var generic = method.GetGenericArguments();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(method.IsGenericMethodDefinition, Is.True);
            Assert.That(generic, Has.Length.EqualTo(1));
            Assert.That(parameters, Has.Length.EqualTo(parameterCount));
            Assert.That(method.ReturnType, Is.EqualTo(generic[0]));
            if (parameterCount == 1)
            {
                Assert.That(parameters[0].ParameterType, Is.EqualTo(generic[0]));
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
