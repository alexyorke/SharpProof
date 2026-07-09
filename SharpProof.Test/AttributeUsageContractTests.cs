using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class AttributeUsageContractTests
    {
        private static readonly BroadUsageAttributePolicy[] BroadUsagePolicies =
        {
            new(typeof(AllowedCapabilitiesAttribute), "[AllowedCapabilities(...)]", SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId),
            new(typeof(AllowedExceptionsAttribute), "[AllowedExceptions(...)]", SharpProofDiagnostics.MisplacedExceptionContractAttributeId),
            new(typeof(EnforcePureAttribute), "[EnforcePure]", SharpProofDiagnostics.MisplacedAttributeId),
            new(typeof(EnsuresAttribute), "[Ensures(\"condition\")]", SharpProofDiagnostics.MisplacedEnsuresAttributeId),
            new(typeof(ExpectedComplexityAttribute), "[ExpectedComplexity(...)]", SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId),
            new(typeof(DoesNotThrowAttribute), "[DoesNotThrow]", SharpProofDiagnostics.MisplacedExceptionContractAttributeId),
            new(typeof(PureAttribute), "[Pure]", SharpProofDiagnostics.MisplacedAttributeId),
            new(typeof(RequiresAttribute), "[Requires(\"condition\")]", SharpProofDiagnostics.MisplacedRequiresAttributeId),
            new(typeof(ZeroAllocationsAttribute), "[ZeroAllocations]", SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId),
        };

        [Test]
        public void PublicSharpProofAttributes_UseConsistentInheritanceAndMultiplicityPolicy()
        {
            var attributeTypes = typeof(EnforcePureAttribute).Assembly
                .GetExportedTypes()
                .Where(type => typeof(Attribute).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            Assert.That(attributeTypes, Is.Not.Empty);

            foreach (var attributeType in attributeTypes)
            {
                var usage = attributeType.GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
                    .OfType<AttributeUsageAttribute>()
                    .SingleOrDefault();

                Assert.That(usage, Is.Not.Null, $"{attributeType.FullName} is missing AttributeUsageAttribute.");
                Assert.That(usage!.Inherited, Is.False, $"{attributeType.FullName} should not be inherited.");

                if (attributeType == typeof(AllowedExceptionsAttribute) ||
                    attributeType == typeof(EnsuresAttribute) ||
                    attributeType == typeof(RequiresAttribute))
                {
                    Assert.That(usage.AllowMultiple, Is.True, $"{attributeType.FullName} should remain multi-use.");
                }
                else
                {
                    Assert.That(usage.AllowMultiple, Is.False, $"{attributeType.FullName} should be single-use.");
                }
            }
        }

        [Test]
        public void AttributeTargetsAllContractAttributes_AreIntentionallyAnalyzerValidated()
        {
            var broadUsageTypes = typeof(EnforcePureAttribute).Assembly
                .GetExportedTypes()
                .Where(type => typeof(Attribute).IsAssignableFrom(type))
                .Where(static type => GetAttributeUsage(type).ValidOn == AttributeTargets.All)
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            var policyTypes = BroadUsagePolicies
                .Select(static policy => policy.AttributeType)
                .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            Assert.That(broadUsageTypes, Is.EqualTo(policyTypes));
            foreach (var policy in BroadUsagePolicies)
            {
                var usage = GetAttributeUsage(policy.AttributeType);
                Assert.That(usage.ValidOn, Is.EqualTo(AttributeTargets.All), policy.AttributeType.FullName);
                Assert.That(policy.PlacementDiagnosticId, Does.Match("^SP[0-9]{4}$"), policy.AttributeType.FullName);
            }
        }

        [Test]
        public void BroadAttributeUsagePolicy_IsDocumentedForEachAnalyzerValidatedAttribute()
        {
            var contractsDoc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "contracts.md"));

            Assert.That(contractsDoc, Does.Contain("AttributeTargets.All"));
            Assert.That(contractsDoc, Does.Contain("compiler `CS0592`"));
            Assert.That(contractsDoc, Does.Contain("[AllowSynchronization]"));
            Assert.That(contractsDoc, Does.Contain("[PureExternal]"));
            Assert.That(contractsDoc, Does.Contain("[Impure]"));

            foreach (var policy in BroadUsagePolicies)
            {
                Assert.That(contractsDoc, Does.Contain(policy.AttributeText), policy.AttributeType.FullName);
                Assert.That(contractsDoc, Does.Contain(policy.PlacementDiagnosticId), policy.AttributeType.FullName);
            }
        }

        private static AttributeUsageAttribute GetAttributeUsage(Type attributeType)
        {
            return attributeType
                .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
                .OfType<AttributeUsageAttribute>()
                .Single();
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")) &&
                    File.Exists(Path.Combine(directory.FullName, "PLAN.md")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }

        private sealed record BroadUsageAttributePolicy(
            Type AttributeType,
            string AttributeText,
            string PlacementDiagnosticId);
    }
}
