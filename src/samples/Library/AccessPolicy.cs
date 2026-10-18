using SharpProof.Attributes;

namespace SharpProof.Samples.Library;

public static class AccessPolicy {
    public static bool IsAuthorized(bool isAdministrator, bool ownsResource) {
        Contract.Ensures(
            Contract.Result<bool>() == (isAdministrator || ownsResource));
        if (isAdministrator)
            return true;
        return ownsResource;
    }

    public static bool SelectApproval(
        bool manualApproval,
        bool automatedApproval) {
        Contract.Ensures(
            Contract.Result<bool>() ==
            (manualApproval ? true : automatedApproval));
        if (manualApproval)
            return true;
        return automatedApproval;
    }
}
