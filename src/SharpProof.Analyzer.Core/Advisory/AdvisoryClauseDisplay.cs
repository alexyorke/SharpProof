using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Analyzer;

// Formats caller-supplied SDK source locations for advisory diagnostic display.
internal static class AdvisoryClauseDisplay
{
    // A Requires clause is named by its condition; a closed attribute by the
    // attribute and the parameter it constrains.
    internal static string Text(Location? clause)
    {
        if (clause?.SourceTree is not { } tree)
        { return "<unknown>"; }
        var node = tree.GetRoot().FindNode(clause.SourceSpan, getInnermostNodeForTie: true);
        var text = node switch
        {
            InvocationExpressionSyntax { ArgumentList.Arguments: { Count: > 0 } arguments } => arguments[0].Expression.ToString(),
            ArgumentSyntax argument => argument.Expression.ToString(),
            AttributeSyntax { Parent.Parent: ParameterSyntax parameter } attribute => "[" + attribute + "] " + parameter.Identifier.ValueText,
            AttributeListSyntax { Parent: ParameterSyntax parameter, Attributes: { Count: > 0 } attributes } => "[" + attributes[0] + "] " + parameter.Identifier.ValueText,
            _ => node.ToString()
        };
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
