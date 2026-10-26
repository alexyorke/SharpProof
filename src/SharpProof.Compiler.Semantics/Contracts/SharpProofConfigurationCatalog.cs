namespace SharpProof.Contracts;

internal static class SharpProofConfigurationCatalog
{
    internal const string ProfileKey = "sharpproof_profile";
    internal const string ProfileBuildPropertyName = "SharpProofProfile";
    internal const string ProfileAdvisory = "advisory";
    internal const string ProfileStrict = "strict";
    internal const string ProfileOff = "off";
    internal const string DefaultProfile = ProfileAdvisory;
    internal const string FeaturesKey = "sharpproof_features";
    internal const string FeaturesBuildPropertyName = "SharpProofFeatures";
    internal const string FeaturesEffects = "effects";
    internal const string FeaturesContracts = "contracts";
    internal const string FeaturesAll = "all";
    internal const string DefaultFeatures = FeaturesAll;
}
