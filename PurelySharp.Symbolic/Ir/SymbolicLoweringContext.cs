using System.Globalization;
using Microsoft.CodeAnalysis;
using PurelySharp.Symbolic.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal sealed class SymbolicLoweringContext
    {
        public SymbolicLoweringContext(
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion = null,
            SmtAnalysisService? smtAnalysis = null)
        {
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            Compilation = semanticModel.Compilation;
            CancellationToken = cancellationToken;
            GetSymbolVersion = getSymbolVersion;
            SmtAnalysis = smtAnalysis;
        }

        public SemanticModel SemanticModel { get; }

        public Compilation Compilation { get; }

        public CancellationToken CancellationToken { get; }

        public Func<ISymbol, int>? GetSymbolVersion { get; }

        public SmtAnalysisService? SmtAnalysis { get; }

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
