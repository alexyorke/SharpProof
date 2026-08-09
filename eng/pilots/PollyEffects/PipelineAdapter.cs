using Polly;
using SharpProof.Attributes;

namespace SharpProof.Pilots.PollyEffects;

public static class PipelineAdapter
{
    [DoesNotThrow]
    public static ResiliencePipeline CreatePipeline() =>
        new ResiliencePipelineBuilder().Build();

    [EnforcePure]
    [ZeroAllocations]
    [DoesNotThrow]
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Identity(int value) => value;
}
