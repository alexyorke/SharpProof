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
public sealed class ConditionalAttributeCompilerAuthorityBoundaryTests
{
    private static readonly int[] RetainedInputs = [-7, 0, 42];
    private static readonly int[] OmittedInputs = [0, 1];
    private static readonly ImmutableArray<string> ShadowAliases = ["Shadow"];
    private static readonly ImmutableArray<string> PriorAliases = ["Prior"];
    private static readonly ImmutableArray<string> PresentSymbols = ["PRIVATE_PRESENT"];
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => unchecked((ushort)code.Value))
        .ToDictionary(group => group.Key, group => group.First());

    private const string ObjectProducer = """
        namespace System.Diagnostics
        {
            public sealed class ConditionalAttribute : global::System.Attribute
            {
                public ConditionalAttribute(object symbol)
                {
                }
            }
        }
        """ + "\n";

    private const string StringProducer = """
        namespace System.Diagnostics
        {
            public sealed class ConditionalAttribute : global::System.Attribute
            {
                public ConditionalAttribute(string symbol)
                {
                }
            }
        }
        """ + "\n";

    private const string OptionalProducer = """
        namespace System.Diagnostics
        {
            public sealed class ConditionalAttribute : global::System.Attribute
            {
                public ConditionalAttribute(string symbol, int ignored = 0)
                {
                }
            }
        }
        """ + "\n";

    private const string OneArgumentCaller = """
        extern alias Shadow;
        using SharpProof.Attributes;

        public static class Fixture
        {
            [Shadow::System.Diagnostics.Conditional("ABSENT")]
            public static void Log(int value)
            {
            }

            [DoesNotThrow]
            public static int Target(int x)
            {
                Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string TwoArgumentCaller = """
        extern alias Shadow;
        using SharpProof.Attributes;

        public static class Fixture
        {
            [Shadow::System.Diagnostics.Conditional("ABSENT", 0)]
            public static void Log(int value)
            {
            }

            [DoesNotThrow]
            public static int Target(int x)
            {
                Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string MixedTwoArgumentCaller = """
        extern alias Shadow;
        using SharpProof.Attributes;

        public static class Fixture
        {
            [global::System.Diagnostics.Conditional("OFFICIAL_ABSENT")]
            [Shadow::System.Diagnostics.Conditional("PRIVATE_PRESENT", 0)]
            public static void Log(int value)
            {
            }

            [DoesNotThrow]
            public static int Target(int x)
            {
                Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string MixedOneArgumentCaller = """
        extern alias Shadow;
        using SharpProof.Attributes;

        public static class Fixture
        {
            [global::System.Diagnostics.Conditional("OFFICIAL_ABSENT")]
            [Shadow::System.Diagnostics.Conditional("PRIVATE_PRESENT")]
            public static void Log(int value)
            {
            }

            [DoesNotThrow]
            public static int Target(int x)
            {
                Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string ImportedCaller = """
        extern alias Prior;

        public static class ImportedCaller
        {
            public static int Target(int x)
            {
                Prior::Fixture.Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    private const string OverrideCaller = """
        extern alias Shadow;

        public class Base
        {
            [global::System.Diagnostics.Conditional("BASE_ABSENT")]
            public virtual void Log(int value)
            {
            }
        }

        public sealed class Derived : Base
        {
            [Shadow::System.Diagnostics.Conditional("PRIVATE_PRESENT")]
            public override void Log(int value)
            {
            }
        }

        public static class Fixture
        {
            public static int Target(int x)
            {
                new Derived().Log(x = 1);
                return 10 / x;
            }
        }
        """ + "\n";

    [TestCase("source-object-string")]
    [TestCase("source-optional-supplied-one")]
    [TestCase("source-optional-supplied-two")]
    [TestCase("source-official-plus-private-supplied-two")]
    [TestCase("imported-object-method")]
    [TestCase("imported-string-method")]
    [TestCase("imported-official-plus-private-object")]
    [TestCase("override-private-present")]
    public async Task CompilerEmissionMatchesPolicy(string scenario)
    {
        var settings = Settings(scenario);
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT")!;
        Assert.That(repository, Is.Not.Null.And.Not.Empty);
        Assert.That(OperatingSystem.IsLinux(), Is.True);
        Assert.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(Architecture.X64));
        var directory = Path.Combine(repository, "artifacts", "correctness",
            "conditional-compiler-authority-boundary-audit", scenario, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Producer.cs"), settings.Producer);
        await File.WriteAllTextAsync(Path.Combine(directory, "Subject.cs"), settings.Source);
        var producer = CreateCompilation("BoundaryProducer", settings.Producer, [], TestMetadataReferences.Platform);
        var producerBytes = await Emit(directory, "producer", producer);
        var shadow = MetadataReference.CreateFromFile(Path.Combine(directory, "producer.dll"),
            MetadataReferenceProperties.Assembly.WithAliases(ShadowAliases));
        var references = TestMetadataReferences.WithSharpProof.Add(shadow);
        byte[]? priorBytes = null;
        IMethodSymbol? originalSourceLog = null;
        if (settings.Imported)
        {
            var priorSource = settings.Mixed ? MixedOneArgumentCaller : OneArgumentCaller;
            await File.WriteAllTextAsync(Path.Combine(directory, "Prior.cs"), priorSource);
            var prior = CreateCompilation("BoundaryPrior", priorSource, settings.Symbols, references);
            priorBytes = await Emit(directory, "prior", prior);
            originalSourceLog = prior.GetTypeByMetadataName("Fixture")!.GetMembers("Log")
                .OfType<IMethodSymbol>().Single();
            var priorPrivate = ((IAssemblySymbol)prior.GetAssemblyOrModuleSymbol(shadow)!)
                .GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute")!;
            await Save(directory, "prior-source-symbol.json", new
            {
                originalSourceLog.IsConditional,
                SourceSha256 = Hash(Encoding.UTF8.GetBytes(priorSource)),
                Attributes = DescribeAttributes(originalSourceLog),
                Pe = DescribePe(priorBytes)
            });
            Assert.That(originalSourceLog.IsConditional, Is.True);
            var priorAttributes = originalSourceLog.GetAttributes();
            QualifyPrivateAttribute(priorAttributes.Single(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, priorPrivate)), priorPrivate,
                settings.ObjectConstructor, settings.StringConstructor, 1, settings.Mixed ? "PRIVATE_PRESENT" : "ABSENT");
            if (settings.Mixed)
            {
                var priorOfficial = prior.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute")!;
                QualifyOfficialAttribute(priorAttributes.Single(attribute =>
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, priorOfficial)), priorOfficial, "OFFICIAL_ABSENT");
            }
            references = references.Add(MetadataReference.CreateFromFile(Path.Combine(directory, "prior.dll"),
                MetadataReferenceProperties.Assembly.WithAliases(PriorAliases)));
        }
        var compilation = CreateCompilation("BoundaryCaller", settings.Source, settings.Symbols, references);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var callSyntax = targetSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)model.GetOperation(callSyntax)!;
        var log = invocation.TargetMethod;
        var privateAssembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(shadow)!;
        var privateType = privateAssembly.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute")!;
        var officialType = compilation.GetTypeByMetadataName("System.Diagnostics.ConditionalAttribute")!;
        var actualElided = new CSharpInvocationEmissionPolicy(compilation).IsElided(invocation);
        var callerBytes = await Emit(directory, "caller", compilation);
        await Save(directory, "compiler-observations.json", new
        {
            Scenario = scenario,
            SourceSha256 = Hash(Encoding.UTF8.GetBytes(settings.Source)),
            ProducerSourceSha256 = Hash(Encoding.UTF8.GetBytes(settings.Producer)),
            CallerSymbols = ((CSharpParseOptions)tree.Options).PreprocessorSymbolNames.ToArray(),
            Compiler = typeof(CSharpCompilation).Assembly.FullName,
            CompilerMvid = typeof(CSharpCompilation).Assembly.ManifestModule.ModuleVersionId,
            ProducerPe = DescribePe(producerBytes),
            PriorPe = priorBytes == null ? null : DescribePe(priorBytes),
            CallerPe = DescribePe(callerBytes),
            ShadowReference = new { shadow.FilePath, shadow.Properties.Aliases },
            Log = new
            {
                Identity = log.ToDisplayString(),
                log.IsConditional,
                SyntaxReferenceCount = log.DeclaringSyntaxReferences.Length,
                MetadataToken = log.OriginalDefinition.MetadataToken,
                Attributes = DescribeAttributes(log),
                Overridden = log.OverriddenMethod?.ToDisplayString(),
                InheritedAttributes = log.OverriddenMethod == null ? null : DescribeAttributes(log.OverriddenMethod)
            },
            SourceLogWasConditional = originalSourceLog?.IsConditional,
            ActualPolicyElided = actualElided,
            ExpectedPolicyElided = settings.Elided,
            ExpectedRetainedCall = settings.Retained
        });
        Assert.That(((CSharpParseOptions)tree.Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        Assert.That(((CSharpParseOptions)tree.Options).PreprocessorSymbolNames, Is.EqualTo(settings.Symbols));
        Assert.That(((CSharpParseOptions)tree.Options).PreprocessorSymbolNames, Does.Not.Contain("SHARPPROOF_CONTRACTS"));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        Assert.That(shadow.Properties.Aliases, Is.EqualTo(ShadowAliases));
        Assert.That(shadow.FilePath, Is.EqualTo(Path.Combine(directory, "producer.dll")));
        Assert.That(privateType.ContainingSymbol, Is.InstanceOf<INamespaceSymbol>());
        Assert.That(privateType.ContainingNamespace.ToDisplayString(), Is.EqualTo("System.Diagnostics"));
        Assert.That(SymbolEqualityComparer.Default.Equals(privateType, officialType), Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(privateType.ContainingAssembly, privateAssembly), Is.True);
        Assert.That(log.IsConditional, Is.EqualTo(settings.Conditional));
        Assert.That(log.DeclaringSyntaxReferences.Length, Is.EqualTo(settings.Imported ? 0 : 1));
        var attributes = log.GetAttributes();
        Assert.That(attributes.Length, Is.EqualTo(settings.Mixed ? 2 : 1));
        var privateAttribute = attributes.Single(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, privateType));
        QualifyPrivateAttribute(privateAttribute, privateType, settings.ObjectConstructor, settings.StringConstructor,
            settings.SuppliedArguments, settings.Override ? "PRIVATE_PRESENT" : settings.Mixed ? "PRIVATE_PRESENT" : "ABSENT");
        if (settings.Mixed)
        {
            QualifyOfficialAttribute(attributes.Single(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, officialType)), officialType, "OFFICIAL_ABSENT", settings.Imported);
        }
        if (settings.Override)
        {
            Assert.That(log.IsOverride, Is.True);
            Assert.That(log.ContainingType.Name, Is.EqualTo("Derived"));
            Assert.That(log.OverriddenMethod, Is.Not.Null);
            Assert.That(log.OverriddenMethod!.ContainingType.Name, Is.EqualTo("Base"));
            Assert.That(log.OverriddenMethod.IsConditional, Is.True);
            QualifyOfficialAttribute(log.OverriddenMethod.GetAttributes().Single(), officialType, "BASE_ABSENT");
        }
        if (settings.Imported)
        {
            Assert.That(SymbolEqualityComparer.Default.Equals(log.ContainingAssembly, compilation.Assembly), Is.False);
            var importedMetadata = log.ContainingModule.GetMetadata()!;
            var reader = importedMetadata.GetMetadataReader();
            Assert.That(reader.GetGuid(reader.GetModuleDefinition().Mvid), Is.EqualTo(DescribePe(priorBytes!).Mvid));
            Assert.That(log.OriginalDefinition.MetadataToken, Is.GreaterThan(0));
            Assert.That(privateAttribute.ApplicationSyntaxReference, Is.Null);
        }
        var assignment = (ISimpleAssignmentOperation)invocation.Arguments.Single().Value;
        Assert.That(assignment.Target, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(((IParameterReferenceOperation)assignment.Target).Parameter.Ordinal, Is.EqualTo(0));
        Assert.That(assignment.Value.ConstantValue.Value, Is.EqualTo(1));
        await ObserveEmission(directory, producerBytes, priorBytes, callerBytes, settings, log.OriginalDefinition.MetadataToken);
        // All actual compiler, PE and typed CLR qualifiers precede the policy oracle.
        Assert.That(actualElided, Is.EqualTo(settings.Elided));
    }
    private static void QualifyPrivateAttribute(AttributeData attribute, INamedTypeSymbol privateType,
        bool objectConstructor, bool stringConstructor, int supplied, string symbol)
    {
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, privateType), Is.True);
        var constructor = attribute.AttributeConstructor!;
        var singleParameter = objectConstructor || stringConstructor;
        Assert.That(SymbolEqualityComparer.Default.Equals(constructor.ContainingType, privateType), Is.True);
        Assert.That(constructor.Parameters.Length, Is.EqualTo(singleParameter ? 1 : 2));
        Assert.That(constructor.Parameters[0].Type.SpecialType,
            Is.EqualTo(objectConstructor ? SpecialType.System_Object : SpecialType.System_String));
        Assert.That(attribute.ConstructorArguments.Length, Is.EqualTo(singleParameter ? 1 : 2));
        Assert.That(attribute.ConstructorArguments[0].Kind, Is.EqualTo(TypedConstantKind.Primitive));
        Assert.That(attribute.ConstructorArguments[0].Type!.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(attribute.ConstructorArguments[0].Value, Is.EqualTo(symbol));
        Assert.That(attribute.NamedArguments, Is.Empty);
        if (!singleParameter)
        {
            Assert.That(constructor.Parameters[1].Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(constructor.Parameters[1].IsOptional, Is.True);
            Assert.That(constructor.Parameters[1].HasExplicitDefaultValue, Is.True);
            Assert.That(constructor.Parameters[1].ExplicitDefaultValue, Is.EqualTo(0));
            Assert.That(attribute.ConstructorArguments[1].Value, Is.EqualTo(0));
        }
        if (attribute.ApplicationSyntaxReference == null)
        {
            Assert.That(supplied, Is.Zero);
        }
        else
        {
            var syntax = (AttributeSyntax)attribute.ApplicationSyntaxReference.GetSyntax();
            Assert.That(syntax.ArgumentList!.Arguments.Count(argument => argument.NameEquals == null), Is.EqualTo(supplied));
        }
    }

    private static void QualifyOfficialAttribute(AttributeData attribute, INamedTypeSymbol officialType, string symbol, bool imported = false)
    {
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, officialType), Is.True);
        Assert.That(attribute.AttributeConstructor!.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(attribute.ConstructorArguments.Single().Value, Is.EqualTo(symbol));
        if (imported)
        {
            Assert.That(attribute.ApplicationSyntaxReference, Is.Null);
        }
        else
        {
            var syntax = (AttributeSyntax)attribute.ApplicationSyntaxReference!.GetSyntax();
            Assert.That(syntax.ArgumentList!.Arguments.Count, Is.EqualTo(1));
        }
    }

    private static object[] DescribeAttributes(IMethodSymbol method)
    {
        return method.GetAttributes().Select(attribute => (object)new
        {
            Type = attribute.AttributeClass!.ToDisplayString(),
            Assembly = attribute.AttributeClass.ContainingAssembly.Identity.ToString(),
            OwnerKind = attribute.AttributeClass.ContainingSymbol.Kind.ToString(),
            Namespace = attribute.AttributeClass.ContainingNamespace.ToDisplayString(),
            Constructor = attribute.AttributeConstructor!.ToDisplayString(),
            Parameters = attribute.AttributeConstructor.Parameters.Select(parameter => new
            {
                Type = parameter.Type.ToDisplayString(),
                SpecialType = parameter.Type.SpecialType.ToString(),
                parameter.IsOptional,
                parameter.HasExplicitDefaultValue,
                DefaultValue = parameter.HasExplicitDefaultValue ? parameter.ExplicitDefaultValue : null
            }).ToArray(),
            Arguments = attribute.ConstructorArguments.Select(argument => new
            {
                Kind = argument.Kind.ToString(),
                Type = argument.Type?.ToDisplayString(),
                argument.Value
            }).ToArray(),
            SuppliedArguments = attribute.ApplicationSyntaxReference == null ? (int?)null :
                ((AttributeSyntax)attribute.ApplicationSyntaxReference.GetSyntax()).ArgumentList!.Arguments
                    .Count(argument => argument.NameEquals == null)
        }).ToArray();
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

    private static CaseSettings Settings(string scenario)
    {
        return scenario switch
        {
            "source-object-string" => new(ObjectProducer, OneArgumentCaller, [], true, false, true, 1, ObjectConstructor: true),
            "source-optional-supplied-one" => new(OptionalProducer, OneArgumentCaller, [], true, false, true, 1),
            "source-optional-supplied-two" => new(OptionalProducer, TwoArgumentCaller, [], false, true, false, 2),
            "source-official-plus-private-supplied-two" =>
                new(OptionalProducer, MixedTwoArgumentCaller, PresentSymbols, true, false, true, 2, Mixed: true),
            "imported-object-method" => new(ObjectProducer, ImportedCaller, [], false, true, false, 0, Imported: true, ObjectConstructor: true),
            "imported-string-method" => new(StringProducer, ImportedCaller, [], true, false, true, 0, Imported: true, StringConstructor: true),
            "imported-official-plus-private-object" =>
                new(ObjectProducer, ImportedCaller, PresentSymbols, true, false, true, 0, Imported: true, Mixed: true, ObjectConstructor: true),
            "override-private-present" =>
                new(ObjectProducer, OverrideCaller, PresentSymbols, true, true, false, 1, Override: true, ObjectConstructor: true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private sealed record CaseSettings(string Producer, string Source, ImmutableArray<string> Symbols,
        bool Conditional, bool Retained, bool Elided, int SuppliedArguments,
        bool Imported = false, bool Override = false, bool Mixed = false, bool ObjectConstructor = false, bool StringConstructor = false);
    private static async Task ObserveEmission(string directory, byte[] producerBytes, byte[]? priorBytes,
        byte[] callerBytes, CaseSettings settings, int symbolToken)
    {
        var context = new AssemblyLoadContext("conditional-boundary-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var producerStream = new MemoryStream(producerBytes);
            var producer = context.LoadFromStream(producerStream);
            Assembly? prior = null;
            if (priorBytes != null)
            {
                using var priorStream = new MemoryStream(priorBytes);
                prior = context.LoadFromStream(priorStream);
            }
            using var callerStream = new MemoryStream(callerBytes);
            var caller = context.LoadFromStream(callerStream);
            var targetType = caller.GetType(settings.Imported ? "ImportedCaller" : "Fixture")!;
            var targetMethod = targetType.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var logAssembly = prior ?? caller;
            var logType = logAssembly.GetType(settings.Override ? "Derived" : "Fixture")!;
            var logMethod = logType.GetMethod("Log", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)!;
            var target = targetMethod.CreateDelegate<Func<int, int>>();
            var targetIl = DecodeIl(targetMethod, callerBytes);
            var logIl = DecodeIl(logMethod, priorBytes ?? callerBytes);
            var calls = targetIl.Instructions.Where(instruction => instruction.OpCode is "call" or "callvirt").ToArray();
            var stores = targetIl.Instructions.Where(instruction => instruction.OpCode is "starg" or "starg.s").ToArray();
            var creations = targetIl.Instructions.Where(instruction => instruction.OpCode == "newobj").ToArray();
            var divisionInstruction = targetIl.Instructions.Single(instruction => instruction.OpCode == "div");
            var inputs = settings.Retained ? RetainedInputs : OmittedInputs;
            var observations = inputs.Select(input => Invoke(target, input)).ToArray();
            await Save(directory, "emission-and-clr.json", new
            {
                Producer = new { Name = producer.GetName().Name, Mvid = producer.ManifestModule.ModuleVersionId },
                Prior = prior == null ? null : new { Name = prior.GetName().Name, Mvid = prior.ManifestModule.ModuleVersionId },
                Caller = new { Name = caller.GetName().Name, Mvid = caller.ManifestModule.ModuleVersionId },
                TargetIl = targetIl,
                LogIl = logIl,
                RuntimeLogAttributes = logMethod.GetCustomAttributesData().Select(attribute => new
                {
                    Type = attribute.AttributeType.FullName,
                    Assembly = attribute.AttributeType.Assembly.GetName().Name,
                    Mvid = attribute.AttributeType.Module.ModuleVersionId,
                    ConstructorToken = attribute.Constructor.MetadataToken,
                    ParameterTypes = attribute.Constructor.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray(),
                    Arguments = attribute.ConstructorArguments.Select(argument => new
                    {
                        Type = argument.ArgumentType.FullName,
                        argument.Value
                    }).ToArray()
                }).ToArray(),
                DelegateMethodToken = target.Method.MetadataToken,
                DelegateMvid = target.Method.Module.ModuleVersionId,
                DelegateTargetIsNull = target.Target == null,
                Observations = observations,
                ExpectedRetainedCall = settings.Retained
            });
            Assert.That(producer.ManifestModule.ModuleVersionId, Is.EqualTo(DescribePe(producerBytes).Mvid));
            Assert.That(caller.ManifestModule.ModuleVersionId, Is.EqualTo(DescribePe(callerBytes).Mvid));
            if (priorBytes != null)
            {
                Assert.That(prior!.ManifestModule.ModuleVersionId, Is.EqualTo(DescribePe(priorBytes).Mvid));
                Assert.That(logMethod.MetadataToken, Is.EqualTo(symbolToken));
            }
            Assert.That(target.Target, Is.Null);
            Assert.That(target.Method, Is.EqualTo(targetMethod));
            Assert.That(targetMethod.ReturnType, Is.EqualTo(typeof(int)));
            Assert.That(targetMethod.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(logMethod.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(logMethod.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(logMethod.IsStatic, Is.EqualTo(!settings.Override));
            Assert.That(logIl.Instructions.Single().OpCode, Is.EqualTo("ret"));
            Assert.That(targetType.TypeInitializer, Is.Null);
            Assert.That(logType.TypeInitializer, Is.Null);
            Assert.That(targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
            Assert.That(logType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static), Is.Empty);
            Assert.That(calls.Length, Is.EqualTo(settings.Retained ? 1 : 0));
            Assert.That(stores.Length, Is.EqualTo(settings.Retained ? 1 : 0));
            Assert.That(creations.Length, Is.EqualTo(settings.Override ? 1 : 0));
            if (settings.Retained)
            {
                var call = calls.Single();
                var store = stores.Single();
                Assert.That(call.ResolvedMvid, Is.EqualTo(logMethod.Module.ModuleVersionId));
                Assert.That(call.ResolvedName, Is.EqualTo("Log"));
                Assert.That(store.RawOperand, Is.EqualTo(store.OpCode == "starg.s" ? "00" : "0000"));
                Assert.That(store.Offset, Is.LessThan(call.Offset));
                Assert.That(call.Offset, Is.LessThan(divisionInstruction.Offset));
                Assert.That(targetIl.Instructions.Count(instruction => instruction.OpCode == "ldc.i4.1"), Is.EqualTo(1));
                if (settings.Override)
                {
                    var baseLog = caller.GetType("Base")!.GetMethod("Log")!;
                    var resolvedCall = (MethodInfo)targetMethod.Module.ResolveMethod(call.Token!.Value)!;
                    Assert.That(call.OpCode, Is.EqualTo("callvirt"));
                    Assert.That(logMethod.GetBaseDefinition(), Is.EqualTo(baseLog));
                    Assert.That(resolvedCall.GetBaseDefinition(), Is.EqualTo(baseLog));
                    Assert.That(call.ResolvedToken, Is.EqualTo(logMethod.MetadataToken).Or.EqualTo(baseLog.MetadataToken));
                    Assert.That(creations.Single().ResolvedType, Is.EqualTo("Derived"));
                    Assert.That(creations.Single().ResolvedToken, Is.EqualTo(logType.GetConstructor(Type.EmptyTypes)!.MetadataToken));
                    Assert.That(creations.Single().Offset, Is.LessThan(store.Offset));
                    Assert.That(DecodeIl(baseLog, callerBytes).Instructions.Single().OpCode, Is.EqualTo("ret"));
                }
                else
                {
                    Assert.That(call.ResolvedToken, Is.EqualTo(logMethod.MetadataToken));
                    Assert.That(call.ResolvedType, Is.EqualTo("Fixture"));
                }
            }
            foreach (var observation in observations)
            {
                var throws = !settings.Retained && observation.Input == 0;
                Assert.That(observation.ExceptionType, Is.EqualTo(throws ? typeof(DivideByZeroException).FullName : null));
                Assert.That(observation.Returned, Is.EqualTo(throws ? (int?)null : 10));
            }
        }
        finally
        {
            context.Unload();
        }
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

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value, JsonOptions) + "\n");
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private sealed record PeObservation(string Sha256, int Bytes, string AssemblyName, Guid Mvid);
    private sealed record IlInstruction(int Offset, string OpCode, string RawOperand, int? Token,
        int? ResolvedToken, Guid? ResolvedMvid, string? ResolvedType, string? ResolvedName);
    private sealed record IlObservation(int MethodToken, Guid Mvid, string Bytes, List<IlInstruction> Instructions);
    private sealed record ClrObservation(int Input, int? Returned, string? ExceptionType);
}
