using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ConditionalMetadataRejectionAuditTests
{
    private static readonly int[] RetainedInputs = [-7, 0, 42];
    private static readonly int[] OmittedInputs = [0, 1];
    private static readonly int[] TwoByteColumns = [2, 2, 2];
    private static readonly ImmutableArray<string> PriorAliases = ["Prior"];
    private static readonly ImmutableArray<string> MarkerAliases = ["Marker"];
    private static readonly ImmutableArray<string> PrivateSymbols = ["PRIVATE_PRESENT"];
    private static readonly ImmutableArray<string> RealPresentSymbols = ["PRIVATE_PRESENT", "OFFICIAL_ABSENT"];
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => unchecked((ushort)code.Value))
        .ToDictionary(group => group.Key, group => group.First());
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string CallerSource = """
        extern alias Prior;

        public static class Fixture
        {
            public static int Target(int x)
            {
                Prior::Sink.Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string LogTemplate = """
        public static class Sink
        {
            [global::System.Diagnostics.Conditional("OFFICIAL_ABSENT")]
            __EXTRA_ATTRIBUTE__
            public static void Log(int value)
            {
            }
        }
        """ + "\n";

    private const string TopLevelMarkerSource = """
        namespace Other
        {
            public sealed class ConditionalAttribute : global::System.Attribute
            {
                public ConditionalAttribute(string symbol)
                {
                }
            }
        }
        """ + "\n";

    private const string NestedMarkerSource = """
        public static class MarkerOwner
        {
            public sealed class ConditionalAttribute : global::System.Attribute
            {
                public ConditionalAttribute(string symbol)
                {
                }
            }
        }
        """ + "\n";

    private const string GenericMarkerSource = """
        public sealed class MarkerAttribute<T> : global::System.Attribute
        {
            public MarkerAttribute(string symbol)
            {
            }
        }
        """ + "\n";

    [TestCase("top-level-unrelated")]
    [TestCase("same-module-nested")]
    [TestCase("external-nested")]
    [TestCase("constructed-generic")]
    [TestCase("nil-value")]
    [TestCase("malformed-ser-string")]
    [TestCase("invalid-constructor-tag")]
    [TestCase("official-present")]
    public async Task IgnoredMetadataRowsDoNotContributeConditionalSymbols(string scenario)
    {
        var settings = Settings(scenario);
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT")!;
        Assert.That(repository, Is.Not.Null.And.Not.Empty);
        Assert.That(OperatingSystem.IsLinux(), Is.True);
        Assert.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(Architecture.X64));
        var directory = Path.Combine(repository, "artifacts", "correctness", "conditional-metadata-rejection-audit",
            scenario, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Producer.cs"), settings.Producer);
        await File.WriteAllTextAsync(Path.Combine(directory, "Subject.cs"), CallerSource);
        var references = TestMetadataReferences.Platform;
        byte[]? markerBytes = null;
        if (settings.External)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Marker.cs"), NestedMarkerSource);
            markerBytes = await Emit(directory, "marker", CreateCompilation("MarkerProducer", NestedMarkerSource, [], references));
            references = references.Add(MetadataReference.CreateFromFile(Path.Combine(directory, "marker.dll"),
                MetadataReferenceProperties.Assembly.WithAliases(MarkerAliases)));
        }
        var producer = CreateCompilation("LogProducer", settings.Producer, [], references);
        var originalBytes = await Emit(directory, "original-producer", producer);
        var sourceLog = producer.GetTypeByMetadataName("Sink")!.GetMembers("Log").OfType<IMethodSymbol>().Single();
        var sourceAttributes = sourceLog.GetAttributes();
        var officialType = producer.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute")!;
        await Save(directory, "source-binding.json", new
        {
            Scenario = scenario,
            ProducerSourceSha256 = Hash(Encoding.UTF8.GetBytes(settings.Producer)),
            CallerSourceSha256 = Hash(Encoding.UTF8.GetBytes(CallerSource)),
            SourceLogIsConditional = sourceLog.IsConditional,
            SourceLogToken = sourceLog.MetadataToken,
            Attributes = sourceAttributes.Select(attribute => new
            {
                Type = attribute.AttributeClass!.ToDisplayString(),
                Assembly = attribute.AttributeClass.ContainingAssembly.Identity.ToString(),
                Constructor = attribute.AttributeConstructor!.ToDisplayString(),
                Values = attribute.ConstructorArguments.Select(argument => argument.Value).ToArray()
            }).ToArray(),
            Compiler = typeof(CSharpCompilation).Assembly.FullName,
            CompilerMvid = typeof(CSharpCompilation).Assembly.ManifestModule.ModuleVersionId,
            MetadataRuntime = typeof(MetadataReader).Assembly.FullName,
            MetadataRuntimeMvid = typeof(MetadataReader).Assembly.ManifestModule.ModuleVersionId
        });
        Assert.That(sourceLog.IsConditional, Is.True);
        Assert.That(sourceLog.DeclaringSyntaxReferences.Length, Is.EqualTo(1));
        Assert.That(sourceAttributes.Length, Is.EqualTo(2));
        var official = sourceAttributes.Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, officialType)).ToArray();
        Assert.That(official.Length, Is.EqualTo(settings.Mutation == null ? 1 : 2));
        Assert.That(official.Select(attribute => (string)attribute.ConstructorArguments.Single().Value!),
            Does.Contain("OFFICIAL_ABSENT"));
        foreach (var attribute in official)
        {
            Assert.That(attribute.AttributeConstructor!.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_String));
            Assert.That(attribute.ConstructorArguments.Single().Kind, Is.EqualTo(TypedConstantKind.Primitive));
            Assert.That(attribute.ApplicationSyntaxReference, Is.Not.Null);
        }
        var logToken = FindLogToken(originalBytes);
        if (settings.Mutation == null)
        {
            await QualifyImage(directory, originalBytes, logToken, scenario, mutated: false);
            await ObserveCaller(directory, "subject", originalBytes, markerBytes, logToken, settings.Symbols, settings.Retained);
        }
        else
        {
            await QualifyImage(directory, originalBytes, logToken, scenario, mutated: false);
            await ObserveCaller(directory, "unmutated-control", originalBytes, null, logToken, PrivateSymbols, retained: true);
            var mutated = await Mutate(directory, originalBytes, logToken, settings.Mutation);
            await QualifyImage(directory, mutated, logToken, scenario, mutated: true);
            await ObserveCaller(directory, "mutated-subject", mutated, null, logToken, PrivateSymbols, retained: false);
        }
    }

    private static CaseSettings Settings(string scenario)
    {
        var top = TopLevelMarkerSource + LogTemplate.Replace("__EXTRA_ATTRIBUTE__",
            "[global::Other.Conditional(\"PRIVATE_PRESENT\")]", StringComparison.Ordinal);
        return scenario switch
        {
            "top-level-unrelated" => new(top, false, null, PrivateSymbols, false),
            "same-module-nested" => new(NestedMarkerSource + LogTemplate.Replace("__EXTRA_ATTRIBUTE__",
                "[global::MarkerOwner.Conditional(\"PRIVATE_PRESENT\")]", StringComparison.Ordinal), false, null, PrivateSymbols, false),
            "external-nested" => new("extern alias Marker;\n" + LogTemplate.Replace("__EXTRA_ATTRIBUTE__",
                "[Marker::MarkerOwner.Conditional(\"PRIVATE_PRESENT\")]", StringComparison.Ordinal), true, null, PrivateSymbols, false),
            "constructed-generic" => new(GenericMarkerSource + LogTemplate.Replace("__EXTRA_ATTRIBUTE__",
                "[global::Marker<int>(\"PRIVATE_PRESENT\")]", StringComparison.Ordinal), false, null, PrivateSymbols, false),
            "nil-value" or "malformed-ser-string" or "invalid-constructor-tag" => new(LogTemplate.Replace("__EXTRA_ATTRIBUTE__",
                "[global::System.Diagnostics.Conditional(\"PRIVATE_PRESENT\")]", StringComparison.Ordinal), false, scenario, PrivateSymbols, false),
            "official-present" => new(top, false, null, RealPresentSymbols, true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static int FindLogToken(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var owner = reader.TypeDefinitions.Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "Sink");
        var method = reader.GetTypeDefinition(owner).GetMethods()
            .Single(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "Log");
        return MetadataTokens.GetToken(method);
    }

    private static ImageObservation DescribeImage(byte[] bytes, int logToken)
    {
        using var stream = new MemoryStream(bytes);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var handle = (MethodDefinitionHandle)MetadataTokens.Handle(logToken);
        var log = reader.GetMethodDefinition(handle);
        var rows = new List<AttributeRow>();
        foreach (var attributeHandle in log.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            var constructorKind = "";
            int? constructorToken = null;
            var ownerKind = "";
            var ownerToken = 0;
            var ownerName = "";
            var ownerNamespace = "";
            var visibility = "";
            var scopeKind = "";
            var ownerNested = false;
            var constructorName = "";
            var signature = "";
            string? constructorError = null;
            try
            {
                var constructor = attribute.Constructor;
                constructorKind = constructor.Kind.ToString();
                constructorToken = MetadataTokens.GetToken(constructor);
                EntityHandle owner;
                if (constructor.Kind == HandleKind.MethodDefinition)
                {
                    var definition = reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
                    owner = definition.GetDeclaringType();
                    constructorName = reader.GetString(definition.Name);
                    signature = Convert.ToHexStringLower(reader.GetBlobBytes(definition.Signature));
                }
                else
                {
                    var reference = reader.GetMemberReference((MemberReferenceHandle)constructor);
                    owner = reference.Parent;
                    constructorName = reader.GetString(reference.Name);
                    signature = Convert.ToHexStringLower(reader.GetBlobBytes(reference.Signature));
                }
                ownerKind = owner.Kind.ToString();
                ownerToken = MetadataTokens.GetToken(owner);
                if (owner.Kind == HandleKind.TypeDefinition)
                {
                    var definition = reader.GetTypeDefinition((TypeDefinitionHandle)owner);
                    ownerName = reader.GetString(definition.Name);
                    ownerNamespace = reader.GetString(definition.Namespace);
                    visibility = (definition.Attributes & TypeAttributes.VisibilityMask).ToString();
                    ownerNested = !definition.GetDeclaringType().IsNil;
                }
                else if (owner.Kind == HandleKind.TypeReference)
                {
                    var reference = reader.GetTypeReference((TypeReferenceHandle)owner);
                    ownerName = reader.GetString(reference.Name);
                    ownerNamespace = reader.GetString(reference.Namespace);
                    scopeKind = reference.ResolutionScope.Kind.ToString();
                }
            }
            catch (BadImageFormatException exception)
            {
                constructorError = exception.GetType().FullName;
            }
            var rawValue = attribute.Value.IsNil ? [] : reader.GetBlobBytes(attribute.Value);
            string? symbol = null;
            string? valueError = null;
            if (!attribute.Value.IsNil)
            {
                try
                {
                    var blob = reader.GetBlobReader(attribute.Value);
                    if (blob.ReadUInt16() == 1)
                    {
                        symbol = blob.ReadSerializedString();
                    }
                    else
                    {
                        valueError = "Unexpected custom-attribute prolog";
                    }
                }
                catch (BadImageFormatException exception)
                {
                    valueError = exception.GetType().FullName;
                }
            }
            rows.Add(new(MetadataTokens.GetRowNumber(attributeHandle), MetadataTokens.GetToken(attribute.Parent),
                constructorKind, constructorToken, constructorName, ownerKind, ownerToken, ownerName, ownerNamespace,
                visibility, scopeKind, ownerNested, signature, attribute.Value.IsNil, MetadataTokens.GetHeapOffset(attribute.Value),
                Convert.ToHexStringLower(rawValue), symbol, constructorError, valueError));
        }
        var tables = Enum.GetValues<TableIndex>().Select(index => index + ":" + reader.GetTableRowCount(index)).ToArray();
        return new(Hash(bytes), bytes.Length, reader.GetGuid(reader.GetModuleDefinition().Mvid),
            reader.GetString(reader.GetAssemblyDefinition().Name), logToken, reader.GetString(log.Name),
            Convert.ToHexStringLower(reader.GetBlobBytes(log.Signature)),
            Convert.ToHexStringLower(pe.GetMethodBody(log.RelativeVirtualAddress).GetILBytes()!), tables, rows);
    }

    private static async Task QualifyImage(string directory, byte[] bytes, int logToken, string scenario, bool mutated)
    {
        var image = DescribeImage(bytes, logToken);
        await Save(directory, mutated ? "mutated-image.json" : "original-image.json", image);
        Assert.That(image.LogName, Is.EqualTo("Log"));
        Assert.That(image.LogSignature, Is.EqualTo("00010108"));
        Assert.That(image.LogIl, Is.EqualTo("2a"));
        Assert.That(image.Rows.Count, Is.EqualTo(2));
        Assert.That(image.Rows.All(row => row.ParentToken == logToken), Is.True);
        var official = image.Rows.Single(row => row.Symbol == "OFFICIAL_ABSENT");
        Assert.That(official.ConstructorKind, Is.EqualTo(nameof(HandleKind.MemberReference)));
        Assert.That(official.OwnerKind, Is.EqualTo(nameof(HandleKind.TypeReference)));
        Assert.That(official.OwnerName, Is.EqualTo("ConditionalAttribute"));
        Assert.That(official.OwnerNamespace, Is.EqualTo("System.Diagnostics"));
        Assert.That(official.Signature, Is.EqualTo("2001010e"));
        var extra = image.Rows.Single(row => row.RowId != official.RowId);
        if (mutated)
        {
            if (scenario == "nil-value")
            {
                Assert.That(extra.ValueIsNil, Is.True);
                Assert.That(extra.Symbol, Is.Null);
            }
            else if (scenario == "malformed-ser-string")
            {
                Assert.That(extra.ValueIsNil, Is.False);
                Assert.That(extra.ValueError, Is.EqualTo(typeof(BadImageFormatException).FullName));
                Assert.That(extra.Symbol, Is.Null);
            }
            else
            {
                Assert.That(extra.ConstructorError, Is.EqualTo(typeof(BadImageFormatException).FullName));
                Assert.That(extra.Symbol, Is.EqualTo("PRIVATE_PRESENT"));
            }
        }
        else
        {
            Assert.That(extra.ConstructorName, Is.EqualTo(".ctor"));
            Assert.That(extra.Signature, Is.EqualTo("2001010e"));
            Assert.That(extra.Symbol, Is.EqualTo("PRIVATE_PRESENT"));
            Assert.That(extra.ConstructorError, Is.Null);
            Assert.That(extra.ValueError, Is.Null);
            if (scenario is "top-level-unrelated" or "official-present" or "same-module-nested")
            {
                Assert.That(extra.ConstructorKind, Is.EqualTo(nameof(HandleKind.MethodDefinition)));
                Assert.That(extra.OwnerKind, Is.EqualTo(nameof(HandleKind.TypeDefinition)));
                Assert.That(extra.Visibility, Is.EqualTo(scenario == "same-module-nested" ? "NestedPublic" : "Public"));
                Assert.That(extra.OwnerNested, Is.EqualTo(scenario == "same-module-nested"));
                Assert.That(extra.OwnerName, Is.EqualTo("ConditionalAttribute"));
                Assert.That(extra.OwnerNamespace, Is.EqualTo(scenario == "same-module-nested" ? "" : "Other"));
            }
            else if (scenario == "external-nested")
            {
                Assert.That(extra.ConstructorKind, Is.EqualTo(nameof(HandleKind.MemberReference)));
                Assert.That(extra.OwnerKind, Is.EqualTo(nameof(HandleKind.TypeReference)));
                Assert.That(extra.ScopeKind, Is.EqualTo(nameof(HandleKind.TypeReference)));
                Assert.That(extra.OwnerName, Is.EqualTo("ConditionalAttribute"));
            }
            else if (scenario == "constructed-generic")
            {
                Assert.That(extra.ConstructorKind, Is.EqualTo(nameof(HandleKind.MemberReference)));
                Assert.That(extra.OwnerKind, Is.EqualTo(nameof(HandleKind.TypeSpecification)));
            }
            else
            {
                Assert.That(extra.ConstructorKind, Is.EqualTo(nameof(HandleKind.MemberReference)));
                Assert.That(extra.OwnerKind, Is.EqualTo(nameof(HandleKind.TypeReference)));
                Assert.That(extra.OwnerNamespace, Is.EqualTo("System.Diagnostics"));
                Assert.That(extra.OwnerName, Is.EqualTo("ConditionalAttribute"));
            }
        }
    }

    private static async Task<byte[]> Mutate(string directory, byte[] original, int logToken, string mutation)
    {
        using var stream = new MemoryStream(original);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var attributes = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.Handle(logToken)).GetCustomAttributes();
        var selected = attributes.Single(handle => ReadSymbol(reader, reader.GetCustomAttribute(handle).Value) == "PRIVATE_PRESENT");
        var intact = attributes.Single(handle => ReadSymbol(reader, reader.GetCustomAttribute(handle).Value) == "OFFICIAL_ABSENT");
        var attribute = reader.GetCustomAttribute(selected);
        var constructor = attribute.Constructor;
        Assert.That(BitConverter.IsLittleEndian, Is.True);
        Assert.That(reader.GetTableRowSize(TableIndex.CustomAttribute), Is.EqualTo(6));
        Assert.That(constructor.Kind, Is.EqualTo(HandleKind.MemberReference));
        Assert.That(attribute.Value, Is.Not.EqualTo(reader.GetCustomAttribute(intact).Value));
        var rowOffset = pe.PEHeaders.MetadataStartOffset + reader.GetTableMetadataOffset(TableIndex.CustomAttribute) +
            (MetadataTokens.GetRowNumber(selected) - 1) * reader.GetTableRowSize(TableIndex.CustomAttribute);
        var valueOffset = MetadataTokens.GetHeapOffset(attribute.Value);
        var expectedParent = (logToken & 0x00ffffff) << 5;
        var expectedType = (MetadataTokens.GetRowNumber((MemberReferenceHandle)constructor) << 3) | 3;
        Assert.That(expectedParent, Is.LessThanOrEqualTo(ushort.MaxValue));
        Assert.That(expectedType, Is.LessThanOrEqualTo(ushort.MaxValue));
        Assert.That(valueOffset, Is.LessThanOrEqualTo(ushort.MaxValue));
        Assert.That(BitConverter.ToUInt16(original, rowOffset), Is.EqualTo(expectedParent));
        Assert.That(BitConverter.ToUInt16(original, rowOffset + 2), Is.EqualTo(expectedType));
        Assert.That(BitConverter.ToUInt16(original, rowOffset + 4), Is.EqualTo(valueOffset));
        var mutated = (byte[])original.Clone();
        int[] allowedOffsets;
        if (mutation is "nil-value" or "invalid-constructor-tag")
        {
            var cell = rowOffset + (mutation == "nil-value" ? 4 : 2);
            allowedOffsets = [cell, cell + 1];
            mutated[cell] = 0;
            mutated[cell + 1] = 0;
        }
        else
        {
            var rawValue = reader.GetBlobBytes(attribute.Value);
            var blobOffset = pe.PEHeaders.MetadataStartOffset + reader.GetHeapMetadataOffset(HeapIndex.Blob) + valueOffset;
            Assert.That(rawValue.Length, Is.LessThan(128));
            Assert.That(original[blobOffset], Is.EqualTo(rawValue.Length));
            Assert.That(original[blobOffset + 1], Is.EqualTo(1));
            Assert.That(original[blobOffset + 2], Is.Zero);
            Assert.That(original[blobOffset + 3], Is.EqualTo(Encoding.UTF8.GetByteCount("PRIVATE_PRESENT")));
            Assert.That(rawValue.Length - 3, Is.LessThan(127));
            allowedOffsets = [blobOffset + 3];
            mutated[blobOffset + 3] = 0x7f;
        }
        var changedOffsets = Enumerable.Range(0, original.Length).Where(offset => original[offset] != mutated[offset]).ToArray();
        var before = DescribeImage(original, logToken);
        var after = DescribeImage(mutated, logToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, "mutated-producer.dll"), mutated);
        await Save(directory, "mutation.json", new
        {
            Mutation = mutation,
            SelectedRow = MetadataTokens.GetRowNumber(selected),
            IntactRow = MetadataTokens.GetRowNumber(intact),
            RowOffset = rowOffset,
            RowSize = 6,
            ColumnWidths = TwoByteColumns,
            AllowedOffsets = allowedOffsets,
            ChangedOffsets = changedOffsets,
            Original = before,
            Mutated = after
        });
        Assert.That(changedOffsets, Is.Not.Empty);
        Assert.That(changedOffsets.All(offset => allowedOffsets.Contains(offset)), Is.True);
        Assert.That(mutated.Length, Is.EqualTo(original.Length));
        Assert.That(after.Mvid, Is.EqualTo(before.Mvid));
        Assert.That(after.Sha256, Is.Not.EqualTo(before.Sha256));
        Assert.That(after.Tables, Is.EqualTo(before.Tables));
        Assert.That(after.LogIl, Is.EqualTo(before.LogIl));
        Assert.That(after.Rows.Single(row => row.RowId == MetadataTokens.GetRowNumber(intact)),
            Is.EqualTo(before.Rows.Single(row => row.RowId == MetadataTokens.GetRowNumber(intact))));
        return mutated;
    }

    private static string? ReadSymbol(MetadataReader reader, BlobHandle value)
    {
        var blob = reader.GetBlobReader(value);
        Assert.That(blob.ReadUInt16(), Is.EqualTo(1));
        return blob.ReadSerializedString();
    }

    private static async Task ObserveCaller(string directory, string version, byte[] producerBytes, byte[]? markerBytes,
        int logToken, ImmutableArray<string> symbols, bool retained)
    {
        var output = Path.Combine(directory, version);
        Directory.CreateDirectory(output);
        var producerPath = Path.Combine(output, "producer.dll");
        await File.WriteAllBytesAsync(producerPath, producerBytes);
        var prior = MetadataReference.CreateFromFile(producerPath,
            MetadataReferenceProperties.Assembly.WithAliases(PriorAliases));
        var references = TestMetadataReferences.Platform.Add(prior);
        if (markerBytes != null)
        {
            var markerPath = Path.Combine(output, "marker.dll");
            await File.WriteAllBytesAsync(markerPath, markerBytes);
            references = references.Add(MetadataReference.CreateFromFile(markerPath,
                MetadataReferenceProperties.Assembly.WithAliases(MarkerAliases)));
        }
        var compilation = CreateCompilation("MetadataCaller", CallerSource, symbols, references);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var callSyntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(callSyntax)!;
        var log = invocation.TargetMethod;
        var callerBytes = await Emit(output, "caller", compilation);
        var importedReader = log.ContainingModule.GetMetadata()!.GetMetadataReader();
        var importedMvid = importedReader.GetGuid(importedReader.GetModuleDefinition().Mvid);
        var importedAssembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(prior)!;
        await Save(output, "compiler-observations.json", new
        {
            Version = version,
            SourceSha256 = Hash(Encoding.UTF8.GetBytes(CallerSource)),
            CallerSymbols = ((CSharpParseOptions)tree.Options).PreprocessorSymbolNames.ToArray(),
            Compiler = typeof(CSharpCompilation).Assembly.FullName,
            CompilerMvid = typeof(CSharpCompilation).Assembly.ManifestModule.ModuleVersionId,
            MetadataRuntime = typeof(MetadataReader).Assembly.FullName,
            MetadataRuntimeMvid = typeof(MetadataReader).Assembly.ManifestModule.ModuleVersionId,
            Reference = new { prior.FilePath, prior.Properties.Aliases },
            ProducerPe = DescribePe(producerBytes),
            CallerPe = DescribePe(callerBytes),
            Log = new
            {
                log.IsConditional,
                SyntaxReferences = log.DeclaringSyntaxReferences.Length,
                MetadataToken = log.OriginalDefinition.MetadataToken,
                ImportedMvid = importedMvid
            }
        });
        Assert.That(((CSharpParseOptions)tree.Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        Assert.That(((CSharpParseOptions)tree.Options).PreprocessorSymbolNames, Is.EqualTo(symbols));
        Assert.That(symbols, Does.Not.Contain("SHARPPROOF_CONTRACTS"));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        Assert.That(prior.Properties.Aliases, Is.EqualTo(PriorAliases));
        Assert.That(prior.FilePath, Is.EqualTo(producerPath));
        Assert.That(Hash(await File.ReadAllBytesAsync(producerPath)), Is.EqualTo(Hash(producerBytes)));
        Assert.That(SymbolEqualityComparer.Default.Equals(log.ContainingAssembly, importedAssembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(log.ContainingAssembly, compilation.Assembly), Is.False);
        Assert.That(log.IsConditional, Is.True);
        Assert.That(log.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(log.OriginalDefinition.MetadataToken, Is.EqualTo(logToken));
        Assert.That(importedMvid, Is.EqualTo(DescribePe(producerBytes).Mvid));
        Assert.That(log.IsStatic, Is.True);
        Assert.That(log.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Void));
        Assert.That(log.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var assignment = (ISimpleAssignmentOperation)invocation.Arguments.Single().Value;
        Assert.That(((IParameterReferenceOperation)assignment.Target).Parameter.Ordinal, Is.Zero);
        Assert.That(assignment.Value.ConstantValue.Value, Is.EqualTo(1));
        await ObserveEmission(output, producerBytes, markerBytes, callerBytes, logToken, retained);
        // Compiler/PE/typed CLR observations and qualifiers precede policy evaluation.
        var actualElided = new CSharpInvocationEmissionPolicy(compilation).IsElided(invocation);
        await Save(output, "policy-observations.json", new
        {
            ActualPolicyElided = actualElided,
            ExpectedPolicyElided = !retained,
            ExpectedRetainedCall = retained,
            ProducerSha256 = Hash(producerBytes),
            CallerSha256 = Hash(callerBytes)
        });
        Assert.That(actualElided, Is.EqualTo(!retained));
    }

    private static async Task ObserveEmission(string directory, byte[] producerBytes, byte[]? markerBytes,
        byte[] callerBytes, int logToken, bool retained)
    {
        var context = new AssemblyLoadContext("metadata-rejection-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            if (markerBytes != null)
            {
                using var markerStream = new MemoryStream(markerBytes);
                context.LoadFromStream(markerStream);
            }
            using var producerStream = new MemoryStream(producerBytes);
            var producer = context.LoadFromStream(producerStream);
            using var callerStream = new MemoryStream(callerBytes);
            var caller = context.LoadFromStream(callerStream);
            var targetType = caller.GetType("Fixture")!;
            var targetMethod = targetType.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var logType = producer.GetType("Sink")!;
            var logMethod = logType.GetMethod("Log", BindingFlags.Public | BindingFlags.Static)!;
            var target = targetMethod.CreateDelegate<Func<int, int>>();
            var targetIl = DecodeIl(targetMethod, callerBytes);
            var logIl = DecodeIl(logMethod, producerBytes);
            var calls = targetIl.Instructions.Where(instruction => instruction.OpCode is "call" or "callvirt").ToArray();
            var stores = targetIl.Instructions.Where(instruction => instruction.OpCode is "starg" or "starg.s").ToArray();
            var divisionInstruction = targetIl.Instructions.Single(instruction => instruction.OpCode == "div");
            var observations = (retained ? RetainedInputs : OmittedInputs).Select(input => Invoke(target, input)).ToArray();
            await Save(directory, "emission-and-clr.json", new
            {
                Producer = new
                {
                    Name = producer.GetName().Name,
                    Mvid = producer.ManifestModule.ModuleVersionId,
                    Sha256 = Hash(producerBytes)
                },
                Caller = new
                {
                    Name = caller.GetName().Name,
                    Mvid = caller.ManifestModule.ModuleVersionId,
                    Sha256 = Hash(callerBytes)
                },
                TargetIl = targetIl,
                LogIl = logIl,
                DelegateMethodToken = target.Method.MetadataToken,
                DelegateMvid = target.Method.Module.ModuleVersionId,
                DelegateTargetIsNull = target.Target == null,
                Observations = observations,
                ExpectedRetainedCall = retained
            });
            Assert.That(producer.ManifestModule.ModuleVersionId, Is.EqualTo(DescribePe(producerBytes).Mvid));
            Assert.That(caller.ManifestModule.ModuleVersionId, Is.EqualTo(DescribePe(callerBytes).Mvid));
            Assert.That(logMethod.MetadataToken, Is.EqualTo(logToken));
            Assert.That(target.Target, Is.Null);
            Assert.That(target.Method, Is.EqualTo(targetMethod));
            Assert.That(targetMethod.ReturnType, Is.EqualTo(typeof(int)));
            Assert.That(targetMethod.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(logMethod.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(logMethod.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(logIl.Instructions.Single().OpCode, Is.EqualTo("ret"));
            Assert.That(targetType.TypeInitializer, Is.Null);
            Assert.That(logType.TypeInitializer, Is.Null);
            Assert.That(targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
            Assert.That(logType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
            Assert.That(calls.Length, Is.EqualTo(retained ? 1 : 0));
            Assert.That(stores.Length, Is.EqualTo(retained ? 1 : 0));
            Assert.That(targetIl.Instructions.Count(instruction => instruction.OpCode == "ldc.i4.1"), Is.EqualTo(retained ? 1 : 0));
            Assert.That(targetIl.Instructions.Count(instruction => instruction.OpCode == "newobj"), Is.Zero);
            if (retained)
            {
                var call = calls.Single();
                var store = stores.Single();
                Assert.That(call.OpCode, Is.EqualTo("call"));
                Assert.That(call.ResolvedMvid, Is.EqualTo(producer.ManifestModule.ModuleVersionId));
                Assert.That(call.ResolvedToken, Is.EqualTo(logMethod.MetadataToken));
                Assert.That(call.ResolvedName, Is.EqualTo("Log"));
                Assert.That(call.ResolvedType, Is.EqualTo("Sink"));
                Assert.That(store.RawOperand, Is.EqualTo(store.OpCode == "starg.s" ? "00" : "0000"));
                Assert.That(store.Offset, Is.LessThan(call.Offset));
                Assert.That(call.Offset, Is.LessThan(divisionInstruction.Offset));
            }
            foreach (var observation in observations)
            {
                var throws = !retained && observation.Input == 0;
                Assert.That(observation.ExceptionType, Is.EqualTo(throws ? typeof(DivideByZeroException).FullName : null));
                Assert.That(observation.Returned, Is.EqualTo(throws ? (int?)null : 10));
            }
        }
        finally
        {
            context.Unload();
        }
    }

    private static CSharpCompilation CreateCompilation(string prefix, string source,
        ImmutableArray<string> symbols, ImmutableArray<MetadataReference> references)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: symbols);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, "Subject.cs");
        return CSharpCompilation.Create(prefix + "_" + Guid.NewGuid().ToString("N"), [tree], references,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));
    }

    private static async Task<byte[]> Emit(string directory, string name, CSharpCompilation compilation)
    {
        await Save(directory, name + "-diagnostics.json", Diagnostics(compilation.GetDiagnostics()));
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        await Save(directory, name + "-emit-diagnostics.json", Diagnostics(result.Diagnostics));
        Assert.That(result.Success, Is.True);
        Assert.That(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var bytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".dll"), bytes);
        await Save(directory, name + "-pe.json", DescribePe(bytes));
        return bytes;
    }

    private static ClrObservation Invoke(Func<int, int> target, int input)
    {
        try
        {
            return new(input, target(input), null);
        }
        catch (DivideByZeroException exception)
        {
            return new(input, null, exception.GetType().FullName);
        }
    }
    private static IlObservation DecodeIl(MethodBase method, byte[] bytes)
    {
        using var image = new MemoryStream(bytes);
        using var pe = new PEReader(image);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff));
        var il = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()!;
        Assert.That(il, Is.EqualTo(method.GetMethodBody()!.GetILAsByteArray()));
        var instructions = new List<IlInstruction>();
        for (var offset = 0; offset < il.Length;)
        {
            var start = offset;
            ushort value = il[offset++];
            if (value == 0xfe)
            {
                value = (ushort)(0xfe00 | il[offset++]);
            }
            var opcode = OpCodesByValue[value];
            var size = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
            Assert.That(offset + size, Is.LessThanOrEqualTo(il.Length));
            int? token = null;
            MethodBase? resolved = null;
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                token = BitConverter.ToInt32(il, offset);
                resolved = method.Module.ResolveMethod(token.Value)!;
            }
            instructions.Add(new(start, opcode.Name!, Convert.ToHexStringLower(il.AsSpan(offset, size)), token,
                resolved?.MetadataToken, resolved?.Module.ModuleVersionId, resolved?.DeclaringType?.FullName, resolved?.Name));
            offset += size;
        }
        return new(method.MetadataToken, method.Module.ModuleVersionId, Convert.ToHexStringLower(il), instructions);
    }

    private static PeObservation DescribePe(byte[] bytes)
    {
        using var image = new MemoryStream(bytes);
        using var pe = new PEReader(image);
        var metadata = pe.GetMetadataReader();
        return new(Hash(bytes), bytes.Length, metadata.GetString(metadata.GetAssemblyDefinition().Name),
            metadata.GetGuid(metadata.GetModuleDefinition().Mvid));
    }

    private static object[] Diagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Select(diagnostic => (object)new
        {
            diagnostic.Id,
            Severity = diagnostic.Severity.ToString(),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            Start = diagnostic.Location.SourceSpan.Start,
            Length = diagnostic.Location.SourceSpan.Length
        }).ToArray();
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value, JsonOptions) + "\n");
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private sealed record CaseSettings(string Producer, bool External, string? Mutation,
        ImmutableArray<string> Symbols, bool Retained);
    private sealed record ImageObservation(string Sha256, int Bytes, Guid Mvid, string AssemblyName,
        int LogToken, string LogName, string LogSignature, string LogIl, string[] Tables, List<AttributeRow> Rows);
    private sealed record AttributeRow(int RowId, int ParentToken, string ConstructorKind, int? ConstructorToken,
        string ConstructorName, string OwnerKind, int OwnerToken, string OwnerName, string OwnerNamespace,
        string Visibility, string ScopeKind, bool OwnerNested, string Signature, bool ValueIsNil, int ValueHeapOffset,
        string ValueHex, string? Symbol, string? ConstructorError, string? ValueError);
    private sealed record PeObservation(string Sha256, int Bytes, string AssemblyName, Guid Mvid);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, int? Token,
        int? ResolvedToken, Guid? ResolvedMvid, string? ResolvedType, string? ResolvedName);
    private sealed record IlObservation(int MethodToken, Guid Mvid, string Bytes, List<IlInstruction> Instructions);
    private sealed record ClrObservation(int Input, int? Returned, string? ExceptionType);
}
