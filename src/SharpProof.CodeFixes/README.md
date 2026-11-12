# SharpProof.CodeFixes

Optional editor support for [SharpProof](https://github.com/alexyorke/SharpProof),
which checks C# contracts and effects at build time.

This package adds suggestions and code fixes to the IDE:

- generate a companion contract class for a type you do not own;
- add the members a companion is missing;
- fix a `Contract.Result<T>()` whose type does not match the method's return type.

It contains analyzers and code fixes only. It does not verify anything on its
own: reference the `SharpProof` package for the
contract API, the analyzers and the verifier.

```xml
<PackageReference Include="SharpProof.CodeFixes" Version="<same version as SharpProof>" PrivateAssets="all" />
```

See the [developer experience guide](https://github.com/alexyorke/SharpProof/blob/master/docs/developer-experience.md)
for the suggestions and when they appear.
