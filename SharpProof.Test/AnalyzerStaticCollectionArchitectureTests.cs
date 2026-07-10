using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class AnalyzerStaticCollectionArchitectureTests
    {
        private static readonly HashSet<Type> MutableGenericCollectionTypes = new()
        {
            typeof(Collection<>),
            typeof(ConcurrentDictionary<,>),
            typeof(Dictionary<,>),
            typeof(HashSet<>),
            typeof(IDictionary<,>),
            typeof(IList<>),
            typeof(ISet<>),
            typeof(List<>),
        };

        [Test]
        public void AnalyzerVisibleStaticFields_DoNotExposeMutableCollections()
        {
            var offenders = typeof(SharpProofAnalyzer).Assembly
                .GetTypes()
                .SelectMany(type => type.GetFields(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly))
                .Where(field => field.IsPublic || field.IsAssembly || field.IsFamily || field.IsFamilyOrAssembly)
                .Where(field => !field.DeclaringType!.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                .Where(field => IsMutableCollectionType(field.FieldType))
                .Select(field => $"{field.DeclaringType!.FullName}.{field.Name}: {field.FieldType.FullName}")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void ManualCatalogSets_AreImmutableAndOrdinal()
        {
            Assert.That(Constants.KnownImpureMethods.KeyComparer, Is.EqualTo(StringComparer.Ordinal));
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers.KeyComparer, Is.EqualTo(StringComparer.Ordinal));
            Assert.That(Constants.KnownPureBCLMembers.KeyComparer, Is.EqualTo(StringComparer.Ordinal));
            Assert.That(
                Constants.KnownImpureMethods,
                Does.Contain("System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(object)"));
        }

        private static bool IsMutableCollectionType(Type type)
        {
            if (type.IsArray)
            {
                return true;
            }

            return type.IsGenericType &&
                   MutableGenericCollectionTypes.Contains(type.GetGenericTypeDefinition());
        }
    }
}
