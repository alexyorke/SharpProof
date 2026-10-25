using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Tool;

internal static class NUnitExporter
{
    private const string WorkspaceComposeShape = """
        services:
          verify:
            image: __IMAGE__
            platform: linux/amd64
            mem_limit: ${SHARPPROOF_CONTAINER_MEMORY_LIMIT:-40g}
            cpus: ${SHARPPROOF_CONTAINER_CPU_LIMIT:-0}
            working_dir: /workspace/consumer
            environment:
              - NuGetPackageSourceCredentials_SharpProofPreview
            volumes:
              - .:/workspace/consumer
              - sharpproof-obj:/workspace/consumer/.sharpproof-obj
              - sharpproof-bin:/workspace/consumer/.sharpproof-bin
              - sharpproof-packages:/home/sharpproof/.nuget/packages
              - sharpproof-dotnet:/home/sharpproof/.dotnet
        volumes:
          sharpproof-obj:
          sharpproof-bin:
          sharpproof-packages:
          sharpproof-dotnet:
        """;

    private const string WorkspaceOutputProperties = """
        <Project>
          <PropertyGroup Condition="'$(SHARPPROOF_CONTAINER)' == '1'">
            <SharpProofProjectOutputKey>$([MSBuild]::MakeRelative('$(MSBuildThisFileDirectory)', '$(MSBuildProjectDirectory)'))/$(MSBuildProjectName)</SharpProofProjectOutputKey>
            <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory).sharpproof-obj/$(SharpProofProjectOutputKey)/</BaseIntermediateOutputPath>
            <BaseOutputPath>$(MSBuildThisFileDirectory).sharpproof-bin/$(SharpProofProjectOutputKey)/</BaseOutputPath>
          </PropertyGroup>
        </Project>
        """;

    internal static Task ExportAsync(string projectPath, EvidenceSet evidence, string claimId, string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrEmpty(claimId);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        return ExportAsync(projectPath, evidence, claimId, outputDirectory, workspaceDirectory: null, cancellationToken);
    }

    internal static async Task ExportAsync(string projectPath, EvidenceSet evidence, string claimId, string outputDirectory,
        string? workspaceDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentException.ThrowIfNullOrEmpty(claimId);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        projectPath = Path.GetFullPath(projectPath);
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (!File.Exists(projectPath))
        {
            throw new ArgumentException("The consumer project does not exist.");
        }

        RequireIndependentOutputDirectory(projectPath, outputDirectory);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
        {
            throw new IOException("Export requires a new output directory; existing files will not be overwritten.");
        }

        var workspace = workspaceDirectory == null ? null : ValidateWorkspace(workspaceDirectory, projectPath, outputDirectory, cancellationToken);
        var projectReference = RelativeProjectPath(outputDirectory, projectPath);
        var claim = evidence.Response.Manifest.Claims.SingleOrDefault(claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.ClaimId == claimId;
        }) ?? throw new ArgumentException("The claim is absent from the supplied result.");
        var result = evidence.Response.ClaimResults.Single(value =>
        {
            global::SharpProof.Internal.InputValidation.Require(value, nameof(value));
            return value.ClaimId == claimId;
        });
        if (result.Outcome != WorkerClaimOutcome.Refuted || claim.Kind != WorkerClaimKind.Postcondition || claim.Evidence != WorkerClaimEvidence.DirectClause || result.Vacuity != WorkerVacuityKind.None)
        {
            throw new InvalidOperationException("Only replayed direct scalar postcondition refutations can be exported. Unknown, companion, attribute, and effect claims are not supported.");
        }

        var directory = Path.GetDirectoryName(projectPath)!;
        var evaluated = await ToolProcess.RunAsync("dotnet", directory, cancellationToken, "msbuild", projectPath, "-nologo", "-getProperty:AssemblyName,TargetFramework,TargetFrameworks").ConfigureAwait(false);
        if (evaluated.ExitCode != 0)
        {
            throw new InvalidOperationException("The consumer project could not be evaluated.");
        }

        using var project = System.Text.Json.JsonDocument.Parse(evaluated.Output);
        var properties = project.RootElement.GetProperty("Properties");
        var snapshot = evidence.Artifact.Compilation;
        if (properties.GetProperty("AssemblyName").GetString() != snapshot.AssemblyName || properties.GetProperty("TargetFramework").GetString() != snapshot.TargetFramework || properties.GetProperty("TargetFrameworks").GetString() is { Length: > 0 })
        {
            throw new InvalidOperationException("Export requires the matching single-target consumer project.");
        }

        var compilation = Rehydrate(snapshot, directory, workspace, cancellationToken);
        var methods = compilation.SyntaxTrees.SelectMany(tree =>
        {
            global::SharpProof.Internal.InputValidation.Require(tree, nameof(tree));
            return tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Select(node =>
            {
                global::SharpProof.Internal.InputValidation.Require(node, nameof(node));
                return (Node: node, Model: SharpProof.Frontend.Host.CompilationModelProvider.GetRootSemanticModel(compilation, tree));
            });
        }).Select(pair =>
        {
            global::SharpProof.Internal.InputValidation.Require(pair, nameof(pair));
            return (pair.Node, pair.Model, Symbol: pair.Model.GetDeclaredSymbol(pair.Node));
        }).Where(pair =>
        {
            global::SharpProof.Internal.InputValidation.Require(pair, nameof(pair));
            return pair.Symbol != null && DocumentationCommentId.CreateDeclarationId(pair.Symbol) == claim.CallableId;
        }).ToArray();
        if (methods.Length != 1)
        {
            throw new InvalidOperationException("The callable identity could not be uniquely matched to the compiler-bound source.");
        }

        var method = methods[0];
        var symbol = method.Symbol!;
        if (!symbol.IsStatic || symbol.IsGenericMethod || symbol.IsAsync || symbol.DeclaredAccessibility != Accessibility.Public || !IsScalar(symbol.ReturnType) || symbol.Parameters.Any(static parameter =>
        {
            global::SharpProof.Internal.InputValidation.Require(parameter, nameof(parameter));
            return parameter.RefKind != RefKind.None || !IsScalar(parameter.Type);
        }) || ContainingTypes(symbol.ContainingType).Any(static type =>
        {
            global::SharpProof.Internal.InputValidation.Require(type, nameof(type));
            return type.IsGenericType || type.DeclaredAccessibility != Accessibility.Public;
        }))
        {
            throw new InvalidOperationException("Export supports public nongeneric synchronous static methods on public nongeneric types with Boolean/integral inputs and return values.");
        }

        var clauses = method.Node.Body?.Statements.OfType<ExpressionStatementSyntax>().Select(static statement =>
        {
            global::SharpProof.Internal.InputValidation.Require(statement, nameof(statement));
            return statement.Expression;
        }).OfType<InvocationExpressionSyntax>().Where(invocation =>
        {
            global::SharpProof.Internal.InputValidation.Require(invocation, nameof(invocation));
            return IsContract(method.Model, invocation, "Ensures") && invocation.SpanStart == claim.Location.Start;
        }).ToArray() ?? [];
        if (clauses.Length != 1 || clauses[0].ArgumentList.Arguments.Count != 1)
        {
            throw new InvalidOperationException("The direct postcondition's source span no longer matches the evidence.");
        }

        var rewriter = new ScalarContractRewriter(method.Model, symbol);
        var assertion = rewriter.Rewrite(clauses[0].ArgumentList.Arguments[0].Expression);
        var variables = new List<string>();
        for (var index = 0; index < symbol.Parameters.Length; index++)
        {
            var values = result.Model.Where(value =>
            {
                global::SharpProof.Internal.InputValidation.Require(value, nameof(value));
                return value.Variable == "parameter:" + index.ToString(CultureInfo.InvariantCulture);
            }).ToArray();
            if (values.Length != 1)
            {
                throw new InvalidDataException("The replay model does not contain exactly one value for every input.");
            }

            variables.Add($"        var _argument{index} = {ModelLiteral(values[0], symbol.Parameters[index].Type)};");
        }

        var call = ContainingTypeName(symbol.ContainingType) + ".@" + symbol.Name + "(" + string.Join(", ", Enumerable.Range(0, symbol.Parameters.Length).Select(static index => "_argument" + index.ToString(CultureInfo.InvariantCulture))) + ")";
        var checkedKeyword = snapshot.Options.CheckOverflow ? "checked" : "unchecked";
        var source = "// Generated from validated SharpProof claim " + System.Text.Json.JsonSerializer.Serialize(claimId) + ".\n" + "// Expected to fail until the implementation satisfies its original contract.\n" + "using NUnit.Framework;\n\n[TestFixture]\npublic sealed class SharpProofRegression\n{\n    [Test]\n    public void ReplayedInputSatisfiesPostcondition()\n    {\n" + string.Join("\n", variables) + "\n        var _sharpProofResult = " + call + ";\n" + "        Assert.That(" + checkedKeyword + "(" + assertion + "), Is.True, \"The original postcondition must hold.\");\n    }\n}\n";
        var testFramework = snapshot.TargetFramework switch
        {
            "netstandard2.0" or "netstandard2.1" or "net8.0" => "net8.0",
            "net9.0" => "net9.0",
            "net10.0" => "net10.0",
            _ => throw new InvalidOperationException("NUnit export currently supports net8.0/net9.0/net10.0 consumers and netstandard2.0/netstandard2.1 libraries. Other test runtimes are not qualified.")
        };
        var testProject = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("PropertyGroup", new XElement("TargetFramework", testFramework), new XElement("IsTestProject", "true"), new XElement("IsPackable", "false"), new XElement("ManagePackageVersionsCentrally", "false")), new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", EscapeMsBuildPath(projectReference)), new XAttribute("AdditionalProperties", ReplayBuildProperties(snapshot))), Package("Microsoft.NET.Test.Sdk", ToolVersions.TestSdk), Package("NUnit", ToolVersions.NUnit), Package("NUnit3TestAdapter", ToolVersions.NUnitAdapter))));
        var parent = Path.GetDirectoryName(outputDirectory)!;
        if (!Directory.Exists(parent))
        {
            throw new IOException("The output parent directory must already exist.");
        }

        var temporary = Path.Combine(parent, ".sharpproof-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(temporary, "Regression.cs"), source, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(temporary, "SharpProof.Regression.csproj"), testProject.ToString() + "\n", new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            if (workspace != null)
            {
                await File.WriteAllTextAsync(Path.Combine(temporary, "README.md"), WorkspaceReadme(workspace, outputDirectory),
                    new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            }

            Directory.Move(temporary, outputDirectory);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static XElement Package(string id, string version)
    {
        global::SharpProof.Internal.InputValidation.Require(id, nameof(id));
        global::SharpProof.Internal.InputValidation.Require(version, nameof(version));
        return new("PackageReference", new XAttribute("Include", id), new XAttribute("Version", version));
    }

    internal static string ReplayBuildProperties(CompilerCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var first = snapshot.SyntaxTrees[0];
        foreach (var tree in snapshot.SyntaxTrees)
        {
            if (tree.LanguageVersion != first.LanguageVersion || !tree.PreprocessorSymbols.SequenceEqual(first.PreprocessorSymbols))
            { throw new InvalidOperationException("Export requires uniform recorded language and preprocessor settings across source trees."); }
        }
        var language = Enum.Parse<LanguageVersion>(first.LanguageVersion);
        var nullable = snapshot.Options.NullableContext switch
        {
            CompilerNullableContext.Disable => "disable",
            CompilerNullableContext.Warnings => "warnings",
            CompilerNullableContext.Annotations => "annotations",
            CompilerNullableContext.Enable => "enable",
            _ => throw new InvalidDataException("Invalid recorded nullable context.")
        };
        // Escape list separators so the symbols reach one MSBuild global property.
        var symbols = string.Join(";", first.PreprocessorSymbols);
        var constants = symbols.Length == 0 ? string.Empty : EscapeMsBuildPath(symbols);
        return "SharpProofProfile=advisory;SharpProofVerify=false;" +
            "DefineConstants=" + constants + ";LangVersion=" + LanguageVersionFacts.ToDisplayString(language) +
            ";CheckForOverflowUnderflow=" + (snapshot.Options.CheckOverflow ? "true" : "false") +
            ";AllowUnsafeBlocks=" + (snapshot.Options.AllowUnsafe ? "true" : "false") +
            ";Optimize=" + (snapshot.Options.OptimizationLevel == CompilerOptimizationLevel.Release ? "true" : "false") +
            ";Nullable=" + nullable;
    }

    private static string RelativeProjectPath(string directory, string projectPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        var relative = Path.GetRelativePath(directory, projectPath);

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string EscapeMsBuildPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var escaped = new StringBuilder(path.Length);
        foreach (var character in path)
        {
            if (character is '%' or '$' or '@' or '\'' or '(' or ')' or ';' or '?' or '*')
            {
                escaped.Append('%').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                escaped.Append(character);
            }
        }

        return escaped.ToString();
    }

    private static string ValidateWorkspace(string workspaceDirectory, string projectPath, string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceDirectory));
        if (!Directory.Exists(root))
        {
            throw new ArgumentException("The export workspace must be an existing directory.", nameof(workspaceDirectory));
        }

        RequireWorkspacePath(root, projectPath);
        RequireWorkspacePath(root, outputDirectory);
        var compose = EvidenceSet.ReadBounded(Path.Combine(root, "compose.yaml"), 64 * 1024, cancellationToken);
        var lines = compose.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(static line =>
        {
            ArgumentNullException.ThrowIfNull(line);
            return !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#');
        }).Select(static line => (line ?? throw new ArgumentNullException(nameof(line))).TrimEnd()).ToArray();
        const string imagePrefix = "    image: ";
        var imageLine = lines.FirstOrDefault(static line =>
            (line ?? throw new ArgumentNullException(nameof(line))).StartsWith(imagePrefix, StringComparison.Ordinal));
        if (imageLine == null || !SetupInitializer.IsPinnedConsumerImage(imageLine[imagePrefix.Length..]) ||
            string.Join("\n", lines).Replace(imageLine, "    image: __IMAGE__", StringComparison.Ordinal) != WorkspaceComposeShape)
        {
            throw new InvalidDataException("Workspace compose.yaml must match the SharpProof workspace template's digest-pinned Linux amd64 verify service and isolated root mounts.");
        }

        var properties = EvidenceSet.ReadBounded(Path.Combine(root, "Directory.Build.props"), 64 * 1024, cancellationToken);
        try
        {
            if (!XNode.DeepEquals(XDocument.Parse(properties).Root, XDocument.Parse(WorkspaceOutputProperties).Root))
            {
                throw new InvalidDataException("Workspace Directory.Build.props must match the SharpProof workspace template's isolated compiler output paths.");
            }
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Workspace Directory.Build.props must be valid XML.", exception);
        }

        return root;
    }

    private static void RequireWorkspacePath(string workspaceDirectory, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var relative = Path.GetRelativePath(workspaceDirectory, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("The consumer project and regression output must both stay inside the export workspace.");
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var current = path; ; current = Path.GetDirectoryName(current)!)
        {
            if (new FileInfo(current).LinkTarget != null)
            {
                throw new ArgumentException("Export workspace paths must not traverse symbolic links.");
            }

            if (current.Equals(workspaceDirectory, comparison))
            {
                break;
            }
        }
    }

    private static string WorkspaceReadme(string workspaceDirectory, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        var project = RelativeProjectPath(workspaceDirectory, Path.Combine(outputDirectory, "SharpProof.Regression.csproj"));
        var powershellPath = "'" + project.Replace("'", "''", StringComparison.Ordinal) + "'";
        var shellPath = "'" + project.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        const string options = " -p:SharpProofProfile=advisory -p:SharpProofVerify=false";
        return "# SharpProof regression\n\nThis test replays the captured input and is expected to fail until the implementation satisfies its original postcondition.\n\n" +
            "Run from the workspace root containing compose.yaml. The workspace's verify service mounts the entire tree at /workspace/consumer and configures compiler output paths in its named volumes.\n\n" +
            "PowerShell:\n\n```powershell\ndocker compose run --rm verify test " + powershellPath + options + "\n```\n\n" +
            "Bash:\n\n```bash\ndocker compose run --rm verify test " + shellPath + options + "\n```\n\n" +
            "Keep the consumer and regression directories together when moving this workspace; their ProjectReference and these commands use relative paths.\n";
    }

    internal static void RequireIndependentOutputDirectory(string projectPath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var output = Path.GetFullPath(outputDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (output.Equals(directory, comparison) || output.StartsWith(prefix, comparison))
        {
            throw new IOException("Export requires an output directory outside the consumer project directory so generated tests are not compiled into the consumer. Choose a sibling directory.");
        }
    }

    private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol type)
    {
        global::SharpProof.Internal.InputValidation.Require(type, nameof(type));
        for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
        {
            yield return current;
        }
    }

    internal static bool IsScalar(ITypeSymbol type)
    {
        global::SharpProof.Internal.InputValidation.Require(type, nameof(type));
        return type.SpecialType is SpecialType.System_Boolean or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    private static CSharpCompilation Rehydrate(CompilerCompilationSnapshot snapshot, string directory, string? workspaceDirectory, CancellationToken cancellationToken)
    {
        global::SharpProof.Internal.InputValidation.Require(snapshot, nameof(snapshot));
        global::SharpProof.Internal.InputValidation.Require(directory, nameof(directory));
        if (snapshot.Options == null || snapshot.SyntaxTrees.IsDefault || snapshot.References.IsDefault || snapshot.SyntaxTrees.Length == 0)
        {
            throw new InvalidDataException("Export requires the captured source, compiler options, and reference metadata.");
        }
        var trees = new List<SyntaxTree>();
        foreach (var tree in snapshot.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tree == null || tree.TextLength < 0 || tree.TextLength > CompilerManifestArtifactFile.MaximumBytes)
            {
                throw new InvalidDataException("The captured source metadata exceeds export limits.");
            }
            var path = Remap(tree.Path, snapshot.ProjectDirectory, directory);
            if (workspaceDirectory != null)
            {
                RequireWorkspacePath(workspaceDirectory, path);
            }

            if (new FileInfo(path).Length > 4L * tree.TextLength + 4)
            {
                throw new InvalidDataException("Source size changed since verification: " + Path.GetFileName(path));
            }
            var text = File.ReadAllText(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))), tree.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Source changed since verification: " + Path.GetFileName(path));
            }

            if (!Enum.TryParse<LanguageVersion>(tree.LanguageVersion, out var language) || tree.Kind != "Regular")
            {
                throw new InvalidOperationException("The recorded source parse mode is not supported by the exporter.");
            }

            trees.Add(CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(language, preprocessorSymbols: tree.PreprocessorSymbols), tree.Path, cancellationToken: cancellationToken));
        }

        var references = new List<MetadataReference>();
        long referenceBytes = 0;
        if (snapshot.References.Length > CompilerReferenceLimits.MaximumModuleCount)
        { throw new InvalidDataException("Too many captured references."); }
        foreach (var reference in snapshot.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference == null || reference.Modules.IsDefault || reference.Aliases.IsDefault)
            { throw new InvalidDataException("Invalid captured reference metadata."); }
            if (reference.Modules.Length != 1 || reference.Aliases.Any(static alias =>
            {
                global::SharpProof.Internal.InputValidation.Require(alias, nameof(alias));
                return alias != "global";
            }) || reference.EmbedInteropTypes)
            {
                throw new InvalidOperationException("Export requires ordinary single-module assembly references.");
            }

            var module = reference.Modules[0];
            if (module == null || module.SizeBytes <= 0 || module.SizeBytes > CompilerReferenceLimits.MaximumModuleBytes ||
                referenceBytes > CompilerReferenceLimits.MaximumClosureBytes - module.SizeBytes)
            {
                throw new InvalidDataException("Captured references exceed the export size limits.");
            }
            referenceBytes += module.SizeBytes;
            var path = Remap(module.Path, snapshot.ProjectDirectory, directory);
            using var stream = File.OpenRead(path);
            if (stream.Length != module.SizeBytes)
            { throw new InvalidDataException("A referenced binary changed size since verification."); }
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), module.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A referenced binary changed since verification: " + module.Name);
            }

            references.Add(MetadataReference.CreateFromFile(path));
        }

        var outputKind = snapshot.Options.OutputKind switch
        {
            CompilerOutputKind.ConsoleApplication => OutputKind.ConsoleApplication,
            CompilerOutputKind.WindowsApplication => OutputKind.WindowsApplication,
            CompilerOutputKind.DynamicallyLinkedLibrary => OutputKind.DynamicallyLinkedLibrary,
            _ => throw new InvalidOperationException("The recorded compilation output kind is not supported by the exporter.")
        };
        var compilation = CSharpCompilation.Create(snapshot.AssemblyName, trees, references, new CSharpCompilationOptions(outputKind, checkOverflow: snapshot.Options.CheckOverflow, allowUnsafe: snapshot.Options.AllowUnsafe, usings: snapshot.Options.Usings, nullableContextOptions: (NullableContextOptions)snapshot.Options.NullableContext));
        if (compilation.GetDiagnostics(cancellationToken).Any(static diagnostic =>
        {
            global::SharpProof.Internal.InputValidation.Require(diagnostic, nameof(diagnostic));
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }))
        {
            throw new InvalidOperationException("Recorded source and references could not be bound without compiler errors.");
        }

        return compilation;
    }

    internal static string Remap(string path, string oldDirectory, string newDirectory)
    {
        global::SharpProof.Internal.InputValidation.Require(oldDirectory, nameof(oldDirectory));
        global::SharpProof.Internal.InputValidation.Require(newDirectory, nameof(newDirectory));
        ArgumentException.ThrowIfNullOrEmpty(path);
        var normalized = path.Replace('\\', '/');
        var old = oldDirectory.Replace('\\', '/').TrimEnd('/') + "/";
        if (normalized.StartsWith(old, StringComparison.Ordinal))
        {
            var mapped = Path.GetFullPath(Path.Combine(newDirectory, normalized[old.Length..]));
            var root = Path.GetFullPath(newDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (mapped.StartsWith(root, comparison) && File.Exists(mapped))
            {
                return mapped;
            }

            throw new FileNotFoundException("A recorded project input is unavailable in the consumer project.", Path.GetFileName(path));
        }

        if (File.Exists(path))
        {
            return path;
        }

        const string marker = "/.nuget/packages/";
        var offset = normalized.IndexOf(marker, StringComparison.Ordinal);
        if (offset >= 0)
        {
            var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var mapped = Path.Combine(packages, normalized[(offset + marker.Length)..]);
            if (File.Exists(mapped))
            {
                return mapped;
            }
        }

        throw new FileNotFoundException("A recorded compiler input is unavailable. Export in the original canonical build workspace.", Path.GetFileName(path));
    }

    internal static string ModelLiteral(WorkerModelValue model, ITypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(type);
        if (type.SpecialType == SpecialType.System_Boolean)
        {
            if (model.Kind != "Boolean" || !bool.TryParse(model.Value, out var boolean))
            {
                throw new InvalidDataException("Invalid Boolean replay value.");
            }

            return boolean ? "true" : "false";
        }

        if (!System.Numerics.BigInteger.TryParse(model.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            throw new InvalidDataException("The replay value is not an integral literal.");
        }

        if (model.Kind != "Integer")
        {
            throw new InvalidDataException("The replay kind does not match an integral parameter.");
        }

        var bounds = type.SpecialType switch
        {
            SpecialType.System_SByte => ((System.Numerics.BigInteger)sbyte.MinValue, (System.Numerics.BigInteger)sbyte.MaxValue),
            SpecialType.System_Byte => (byte.MinValue, (System.Numerics.BigInteger)byte.MaxValue),
            SpecialType.System_Int16 => (short.MinValue, (System.Numerics.BigInteger)short.MaxValue),
            SpecialType.System_UInt16 or SpecialType.System_Char => (ushort.MinValue, (System.Numerics.BigInteger)ushort.MaxValue),
            SpecialType.System_Int32 => (int.MinValue, (System.Numerics.BigInteger)int.MaxValue),
            SpecialType.System_UInt32 => (uint.MinValue, (System.Numerics.BigInteger)uint.MaxValue),
            SpecialType.System_Int64 => (long.MinValue, (System.Numerics.BigInteger)long.MaxValue),
            SpecialType.System_UInt64 => (ulong.MinValue, (System.Numerics.BigInteger)ulong.MaxValue),
            _ => throw new InvalidOperationException("Only scalar integral replay values can be exported.")
        };
        if (integer < bounds.Item1 || integer > bounds.Item2)
        {
            throw new InvalidDataException("Replay value exceeds the parameter's integral range.");
        }

        return "(" + ScalarTypeName(type) + ")(" + integer.ToString(CultureInfo.InvariantCulture) + (type.SpecialType == SpecialType.System_UInt64 ? "UL" : "L") + ")";
    }

    internal static bool IsContract(SemanticModel model, InvocationExpressionSyntax invocation, string method)
    {
        global::SharpProof.Internal.InputValidation.Require(model, nameof(model));
        global::SharpProof.Internal.InputValidation.Require(invocation, nameof(invocation));
        global::SharpProof.Internal.InputValidation.Require(method, nameof(method));
        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        return symbol?.Name == method && SymbolEqualityComparer.Default.Equals(symbol.ContainingType,
            model.Compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")) && symbol.ContainingAssembly.Name == "SharpProof.Attributes";
    }

    internal static string ScalarTypeName(ITypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.SpecialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Byte => "byte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Char => "char",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            _ => throw new InvalidOperationException("Only Boolean and integral scalar type names can be exported.")
        };
    }

    private static string ContainingTypeName(INamedTypeSymbol type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var parts = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType)
        { parts.Push("@" + current.Name); }
        for (var current = type.ContainingNamespace; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        { parts.Push("@" + current.Name); }
        return "global::" + string.Join(".", parts);
    }

    internal sealed class ScalarContractRewriter
    {
        private readonly SemanticModel _model;
        private readonly IMethodSymbol _method;
        private readonly HashSet<ISymbol> _unobservableParameters = new(SymbolEqualityComparer.Default);
        internal ScalarContractRewriter(SemanticModel model, IMethodSymbol method)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(method);
            _model = model;
            _method = method;
        }
        internal string Rewrite(ExpressionSyntax expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            var body = expression.FirstAncestorOrSelf<MethodDeclarationSyntax>()?.Body ??
                throw new InvalidOperationException("Postcondition export requires a method body.");

            _unobservableParameters.Clear();
            _unobservableParameters.UnionWith(GetUnobservableStorage(_model.AnalyzeDataFlow(body)));
            var operation = _model.GetOperation(expression) ?? throw new InvalidOperationException("Contract expression could not be bound.");
            return Translate(operation);
        }

        internal static IEnumerable<ISymbol> GetUnobservableStorage(DataFlowAnalysis? dataFlow)
        {
            if (dataFlow is not { Succeeded: true })
            {
                throw new InvalidOperationException("Parameter storage could not be analyzed for postcondition export.");
            }

            return dataFlow.WrittenInside.Concat(dataFlow.UnsafeAddressTaken);
        }

        private string Translate(IOperation operation)
        {
            switch (operation)
            {
                case IParameterReferenceOperation parameter when SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, _method):
                    if (_unobservableParameters.Contains(parameter.Parameter))
                    {
                        throw new InvalidOperationException("Postcondition export cannot observe an assigned parameter's exit value; only Contract.Old entry values can be replayed. No test was written.");
                    }

                    return "_argument" + parameter.Parameter.Ordinal.ToString(CultureInfo.InvariantCulture);
                case ILiteralOperation literal when literal.ConstantValue.HasValue && literal.Type != null && IsScalar(literal.Type):
                    return Constant(literal.ConstantValue.Value!, literal.Type);
                case IFieldReferenceOperation field when field.Field.HasConstantValue && IsScalar(field.Field.Type):
                    return Constant(field.Field.ConstantValue!, field.Field.Type);
                case IConversionOperation conversion when conversion.OperatorMethod == null && conversion.Type != null && IsScalar(conversion.Type):
                    return (conversion.IsChecked ? "checked" : "unchecked") + "((" + ScalarTypeName(conversion.Type) + ")(" + Translate(conversion.Operand) + "))";
                case IBinaryOperation binary when binary.OperatorMethod == null && binary.Syntax is BinaryExpressionSyntax syntax && binary.Type != null && IsScalar(binary.Type) && !binary.IsLifted:
                    return (binary.IsChecked ? "checked" : "unchecked") + "(" + Translate(binary.LeftOperand) + " " + syntax.OperatorToken.Text + " " + Translate(binary.RightOperand) + ")";
                case IUnaryOperation unary when unary.OperatorMethod == null && unary.Syntax is PrefixUnaryExpressionSyntax syntax && !unary.IsLifted:
                    return (unary.IsChecked ? "checked" : "unchecked") + "(" + syntax.OperatorToken.Text + "(" + Translate(unary.Operand) + "))";
                case IConditionalOperation conditional when conditional.WhenFalse != null:
                    return "(" + Translate(conditional.Condition) + " ? " + Translate(conditional.WhenTrue) + " : " + Translate(conditional.WhenFalse) + ")";
                case IInvocationOperation invocation when invocation.Syntax is InvocationExpressionSyntax syntax && IsContract(_model, syntax, "Result") && invocation.Arguments.Length == 0:
                    return "_sharpProofResult";
                case IInvocationOperation invocation when invocation.Syntax is InvocationExpressionSyntax syntax && IsContract(_model, syntax, "Old") && invocation.Arguments.Length == 1 && invocation.Arguments[0].Value is IParameterReferenceOperation parameter && SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, _method):
                    return "_argument" + parameter.Parameter.Ordinal.ToString(CultureInfo.InvariantCulture);
                default:
                    throw new InvalidOperationException("Postcondition export does not support operation " + operation.Kind + ". No test was written.");
            }
        }

        private static string Constant(object value, ITypeSymbol type)
        {
            global::SharpProof.Internal.InputValidation.Require(value, nameof(value));
            global::SharpProof.Internal.InputValidation.Require(type, nameof(type));
            return ModelLiteral(new WorkerModelValue { Kind = type.SpecialType == SpecialType.System_Boolean ? "Boolean" : "Integer", Value = value is char character ? ((int)character).ToString(CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture)! }, type);
        }
    }
}
