# SharpProof Attributes strong-name identity

`SharpProof.Attributes.snk` is a deliberately public RSA 2048-bit key pair used
only to give the public .NET Standard contract assembly a stable strong name.
Its public key token is `75825c3e099f5fac`.

This key pair is not a secret, publisher authentication, a package signing
credential, or proof authority. Anyone may use it to rebuild this open-source
assembly. Never reuse it for authentication, NuGet signing, Authenticode,
partial-trust permissions, or any other security purpose.

The assembly is fully signed, rather than public-signed or delay-signed, so
strong-named .NET Framework consumers can reference it. Keep the identity key
stable: changing it breaks compiled assembly references. Introducing this
identity into the preview also requires existing consumers to rebuild.

SharpProof still authenticates the exact packaged contract assembly through
its existing SHA-256/MVID policy. The public key does not replace that policy
and does not authorize source-defined lookalikes or another package version.

This follows [Microsoft's open-source strong-name guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/strong-naming).
