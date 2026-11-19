# Verdicts and Unknown reasons by source

`corpus-update` writes this file from [oss-methods.json](oss-methods.json) and [expected.canonical.snapshot](expected.canonical.snapshot); a gate test rejects a stale copy. Do not edit it by hand. Each selected method is counted once. An Unknown is listed under its diagnostics; for SP0047 the row also names the reason the analysis stopped. [Unknown reasons](../../../docs/unknown-reasons.md) explains them.

| Verdict or diagnostic | Total | `aalhour-c-sharp-algorithms` | `morelinq` | `nodatime` | `humanizer` |
| --- | ---: | ---: | ---: | ---: | ---: |
| Proven | 52 | 17 | 3 | 23 | 9 |
| Refuted | 33 | 9 | 17 | 7 | 0 |
| SP0002 | 231 | 126 | 29 | 36 | 40 |
| SP0047 `Advisory:Lowering:UnsupportedOperationKind` | 150 | 32 | 48 | 32 | 38 |
| SP0047 `Advisory:Lowering:UnsupportedControlFlow` | 19 | 8 | 0 | 2 | 9 |
| SP0047 `Advisory:ContractBinding:UnsupportedTarget` | 9 | 6 | 1 | 0 | 2 |
| SP0047 `Advisory:Lowering:ConversionMayChangeValue` | 3 | 2 | 0 | 0 | 1 |
| SP0047 `Advisory:EntryInitialization:TypeInitializer` | 1 | 0 | 0 | 0 | 1 |
| SP0047 `Advisory:Lowering:InvalidOperation` | 1 | 0 | 1 | 0 | 0 |
| SP0047 `Advisory:Lowering:UnsupportedType` | 1 | 0 | 1 | 0 | 0 |
| **Total** | 500 | 200 | 100 | 100 | 100 |
