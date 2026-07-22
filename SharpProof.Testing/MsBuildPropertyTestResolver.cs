using System.Xml.Linq;
namespace SharpProof.Test;
internal sealed class MsBuildPropertyTestResolver(params XDocument[] documents) {
    private readonly IReadOnlyDictionary<string, string> _properties = documents
            .SelectMany(static document => document.Descendants("PropertyGroup").Elements())
            .GroupBy(static element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last().Value.Trim(), StringComparer.Ordinal);
    public string Get(string name) => Expand(_properties[name]);
    public string Expand(string value) {
        for (var iteration = 0; iteration < _properties.Count; iteration++) {
            var expanded = _properties.Aggregate(
                value,
                static (current, pair) => current.Replace("$(" + pair.Key + ")", pair.Value, StringComparison.Ordinal));
            if (string.Equals(expanded, value, StringComparison.Ordinal)) return expanded;
            value = expanded;
        }
        return value;
    }
}
