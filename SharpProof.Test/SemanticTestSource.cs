namespace SharpProof.Test;
internal static class SemanticTestSource {
    internal static string Method(string returnType, string parameters, string body, string? usings = null, string? prefix = null) => Class(
        "public " + returnType + " TestMethod(" + parameters + ")\n{\n" + Indent(body, 4) + "\n}",
        usings,
        prefix);
    internal static string Class(string members, string? usings = null, string? prefix = null) =>
        "\n" + (string.IsNullOrWhiteSpace(usings) ? string.Empty : usings.TrimEnd() + "\n\n") +
        (string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.TrimEnd() + "\n\n") +
        "public class TestClass\n{\n" + Indent(members, 4) + "\n}";
    private static string Indent(string value, int spaces) {
        var prefix = new string(' ', spaces);
        return string.Join("\n", value.Replace("\r", string.Empty).Split('\n').Select(line => prefix + line));
    }
}
