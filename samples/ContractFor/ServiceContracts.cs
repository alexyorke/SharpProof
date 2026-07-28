using SharpProof.Attributes;

namespace SharpProof.Samples.ContractFor;

public interface IService {
    string? Find(string key);
}

[ContractFor(typeof(IService))]
public static class IServiceContracts {
    public static string? Find(IService receiver, string key) {
        Contract.Requires(receiver is not null);
        Contract.Requires(key.Length > 0);
        Contract.Ensures(Contract.Result<string?>() == null);
        return null;
    }
}
