using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic.Ir
{
    internal delegate bool SymbolicInvocationTermLowerer(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicTerm term);

    internal sealed class SymbolicLoweringContext
    {
        public SymbolicLoweringContext(
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion = null,
            SmtAnalysisService? smtAnalysis = null,
            SymbolicInvocationTermLowerer? invocationTermLowerer = null)
        {
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            Compilation = semanticModel.Compilation;
            CancellationToken = cancellationToken;
            GetSymbolVersion = getSymbolVersion;
            SmtAnalysis = smtAnalysis;
            InvocationTermLowerer = invocationTermLowerer;
        }

        public SemanticModel SemanticModel { get; }

        public Compilation Compilation { get; }

        public CancellationToken CancellationToken { get; }

        public Func<ISymbol, int>? GetSymbolVersion { get; }

        public SmtAnalysisService? SmtAnalysis { get; }

        public SymbolicInvocationTermLowerer? InvocationTermLowerer { get; }

        public string GetVariableName(ISymbol symbol)
        {
            var name = SymbolicFactFactory.GetSmtVariableName(symbol);
            var version = GetSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
        }
    }
}
