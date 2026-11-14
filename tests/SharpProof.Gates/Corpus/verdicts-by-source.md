# Verdicts and Unknown reasons by source

`corpus-update` writes this file from [oss-methods.json](oss-methods.json) and [expected.canonical.snapshot](expected.canonical.snapshot); a gate test rejects a stale copy. Do not edit it by hand. Each selected method is counted once. An Unknown is listed under its diagnostics; for SP0047 the row also names the reason the analysis stopped. [Unknown reasons](../../../docs/unknown-reasons.md) explains them.

| Verdict or diagnostic | Total | `aalhour-c-sharp-algorithms` | `morelinq` | `nodatime` | `humanizer` |
| --- | ---: | ---: | ---: | ---: | ---: |
| Proven | 26 | 9 | 3 | 5 | 9 |
| Refuted | 10 | 8 | 1 | 1 | 0 |
| SP0002 | 231 | 124 | 39 | 30 | 38 |
| SP0047 `Advisory:Lowering:UnsupportedOperationKind` | 195 | 35 | 53 | 62 | 45 |
| SP0047 `Advisory:Lowering:UnsupportedControlFlow` | 10 | 6 | 0 | 1 | 3 |
| SP0047 `Advisory:ContractBinding:UnsupportedTarget` | 9 | 6 | 1 | 0 | 2 |
| SP0047 `Advisory:Lowering:UnsupportedType` | 8 | 5 | 2 | 0 | 1 |
| SP0047 `Advisory:Lowering:UnsupportedStatement` | 7 | 7 | 0 | 0 | 0 |
| SP0047 `Advisory:EntryInitialization:TypeInitializer` | 2 | 0 | 0 | 1 | 1 |
| SP0047 `Advisory:Lowering:ConversionMayChangeValue` | 1 | 0 | 0 | 0 | 1 |
| SP0047 `Advisory:Lowering:InvalidOperation` | 1 | 0 | 1 | 0 | 0 |
| **Total** | 500 | 200 | 100 | 100 | 100 |
