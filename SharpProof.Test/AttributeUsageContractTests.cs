using System;
using System.Linq;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class AttributeUsageContractTests
    {
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

                if (attributeType == typeof(EnsuresAttribute))
                {
                    Assert.That(usage.AllowMultiple, Is.True, $"{attributeType.FullName} should remain multi-use.");
                }
                else
                {
                    Assert.That(usage.AllowMultiple, Is.False, $"{attributeType.FullName} should be single-use.");
                }
            }
        }
    }
}
