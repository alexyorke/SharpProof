# SharpProof.Attributes

This package supplies SharpProof's supported application API: compiler-bound `Contract.Requires`, `Ensures`, `Assume`, `Result`, and `Old`, closed parameter/return attributes, effect declarations, companions, and explicit trust/reporting controls. It targets .NET Standard 2.0 and includes IntelliSense XML documentation.

The assembly is fully strong-named for strong-named .NET Framework consumers. Its stable open-source identity key is deliberately public and is not publisher authentication or a package-signing credential. Existing preview consumers must rebuild for the introduced assembly identity; exact analyzer/API payload checks remain required. See the [identity policy](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/eng/signing/README.md). Matching portable PDB/source information is supplied through its symbol package; see [debugging guidance](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/nuget-debugging.md).

For normal adoption, install **SharpProof** once. It brings Attributes transitively and enables verification automatically:

```text
dotnet add package SharpProof
```

Choose an available version from your configured feed. Installing Attributes alone does not install the analyzer or prove claims. Keep its dependency available to applications that consume or reflect your attributed library. Application target compatibility is separate from the modern verifier host; see [legacy compatibility](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/legacy-compatibility.md).

Clauses belong in a contiguous callable prologue. They are normally compiler-elided, including argument evaluation. `Result<T>` and `Old<T>` are supported only within admitted postconditions and throw if executed. Do not define the reserved `SHARPPROOF_CONTRACTS` symbol. SharpProof does not add runtime input guards or exception handlers.

`NotNull`, `Positive`, and inclusive `InRange` express admitted parameter/normal-return conditions. Effects and capabilities select separate obligations; purity does not imply zero allocation or termination. Trust requires a nonempty review rationale and a complete admitted boundary contract; it remains an assumption. Suppression changes reporting and adds no proof evidence. No string-based `Requires` attribute or arbitrary contract-string grammar is provided.

`ResourceUpperBound` declares native cumulative bounds for loop-body entries, primitive-array allocation counts, allocated elements and layout-dependent allocated bytes. Bounds are nonnegative constants or linear functions of a named entry size. AllocatedBytes uses the reviewed CoreCLR 8.0.32 x64 fresh-instance storage model and requires compiler-owned target/reference evidence plus a recorded trusted layout dependency. Unsupported cases remain Unknown; these metrics do not claim elapsed time or peak memory.

See the [public API guide](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/public-api.md), [executable tutorials](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/docs/tutorials/README.md), and [semantics](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/SEMANTICS.md). Package version/API identity must match its analysis implementation; copying attribute sources or substituting lookalike assemblies is unsupported. [MIT license](https://github.com/alexyorke/SharpProof/blob/f76a00dfc87dc9ecd4dcd1fc38d2c71f92815833/LICENSE).

SourceActivationDepth and SourceStackBytes select the declared logical source activation-record model. Eight-byte parameter/local/receiver/result slots and two bookkeeping slots describe source storage; these metrics do not estimate JIT frames, runtime/native stack usage or stack-overflow safety. Unsupported stack comparisons remain Unknown; stack refutations are not currently published.
