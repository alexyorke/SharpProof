using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private readonly struct DeconstructionAssignmentElement
    {
        public DeconstructionAssignmentElement(IOperation target, IOperation value)
        {
            Target = target;
            Value = value;
        }

        public IOperation Target { get; }

        public IOperation Value { get; }
    }
}