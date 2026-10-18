using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Specs;
using SharpProof.Testing;

namespace SharpProof.Specs.Test;

public sealed partial class ApiSpecRuntimeOracleTests
{
    private static readonly string EvidenceTest = typeof(ApiSpecRuntimeOracleTests).FullName + "." + nameof(ModeledFacetsHaveExecutedTestEvidence);
    [Test]
    public void ModeledFacetsHaveExecutedTestEvidence()
    {
        var rows = CollectTestEvidence(ApiSpecTable.Default.Templates, Witnesses);
        var root = RepositoryRoot.Find(AppContext.BaseDirectory, "SharpProof.slnx", "global.json") ?? throw new InvalidOperationException("Repository root unavailable.");
        ApiTestEvidence.Write(root, "catalog-test-evidence", rows);
        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.Where(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status != "passed")).Select(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Api + "/" + row.Facet + ": " + row.Detail)), Is.Empty, "Every modeled facet/postcondition requires executed behavioral and wrong-claim evidence. See catalog-test-evidence.md.");
    }

    private static ApiTestEvidence.Row[] CollectTestEvidence(IEnumerable<ApiSpecTemplate> templates, ImmutableDictionary<string, RowWitness> registry)
    {
        global::System.ArgumentNullException.ThrowIfNull(templates);
        global::System.ArgumentNullException.ThrowIfNull(registry);
        var evidence = new List<ApiTestEvidence.Row>();
        foreach (var template in templates)
        {
            registry.TryGetValue(template.Target.WitnessIdentifier, out var row);
            var api = template.Target.DocumentationCommentId;
            foreach (var facet in ExpectedFacets(template))
            {
                var matches = row?.Facets.Where(witness => witness is null ? throw new global::System.ArgumentNullException(nameof(witness)) : (witness.Facet == facet)).ToArray() ?? [];
                evidence.Add(matches.Length == 1 ? ApiTestEvidence.Run(api, facet.ToString(), EvidenceTest, matches[0].EdgeInputs, () => matches[0].AcceptsDeclared(template), matches[0].AcceptsMutation) : new(api, facet.ToString(), EvidenceTest, "", "missing", "Expected exactly one facet witness; found " + matches.Length));
            }

            for (var index = 0; index < template.Postconditions.Length; index++)
            {
                var matches = row?.Postconditions.Where(witness => witness is null ? throw new global::System.ArgumentNullException(nameof(witness)) : (witness.Index == index)).ToArray() ?? [];
                var facet = "Postcondition[" + index + "]";
                evidence.Add(matches.Length == 1 ? ApiTestEvidence.Run(api, facet, EvidenceTest, matches[0].EdgeInputs, () => matches[0].AcceptsDeclared(template), () => matches[0].AcceptsNegatedMutation(template)) : new(api, facet, EvidenceTest, "", "missing", "Expected exactly one postcondition witness; found " + matches.Length));
            }
        }

        return evidence.ToArray();
    }

    [Test]
    public void EvidenceGateDetectsAMissingApiFacetAndPostcondition()
    {
        var template = ApiSpecTable.Default.Templates.Single(item => item is null ? throw new global::System.ArgumentNullException(nameof(item)) : (item.Target.WitnessIdentifier == "bcl.string.length"));
        var identifier = template.Target.WitnessIdentifier;
        var absentApi = CollectTestEvidence([template], Witnesses.Remove(identifier));
        Assert.That(absentApi.Select(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status)), Is.All.EqualTo("missing"));
        var incomplete = Witnesses.SetItem(identifier, Witnesses[identifier] with { Facets = Witnesses[identifier].Facets.RemoveAt(0), Postconditions = [] });
        var rows = CollectTestEvidence([template], incomplete);
        Assert.That(rows.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "missing")), Is.EqualTo(2));
        Assert.That(rows.Single(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Facet == "Postcondition[0]")).Status, Is.EqualTo("missing"));
    }

    [Test]
    public void NewCatalogFacetKindsRequireAnEvidenceRequirement()
    {
        Assert.That(typeof(ApiSpecFacets).GetProperties().Select(property => property is null ? throw new global::System.ArgumentNullException(nameof(property)) : (property.Name)), Is.EquivalentTo(Enum.GetNames<FacetKind>()), "A new production facet needs an explicit evidence requirement and runtime witness.");
    }

    [Test]
    public void EvidenceRequiresBehaviorAndWrongClaimRejectionInsteadOfJustARegistration()
    {
        var acceptsEverything = ApiTestEvidence.Run("api", "facet", EvidenceTest, "edges", () => true, () => true);
        var brokenBehavior = ApiTestEvidence.Run("api", "facet", EvidenceTest, "edges", () => false, () => false);
        var noEdges = ApiTestEvidence.Run("api", "facet", EvidenceTest, "", () => true, () => false);
        var throwingWitness = ApiTestEvidence.Run("api", "facet", EvidenceTest, "edges", () => throw new InvalidOperationException("broken probe"), () => false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(acceptsEverything.Status, Is.EqualTo("failed"));
            Assert.That(brokenBehavior.Status, Is.EqualTo("failed"));
            Assert.That(noEdges.Status, Is.EqualTo("missing"));
            Assert.That(throwingWitness.Status, Is.EqualTo("failed"));
        }
    }
}
