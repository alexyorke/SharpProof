# Trusted boundary review

The trusted-boundary report audits complete `[EffectContract]` declarations and configuration-targeted external effect contracts. Each finding identifies the exact method, declared primitive effects and capabilities, completeness, and any conflict or unknown evidence.

Contracts can add positive facts. They cannot erase effects found in source or IL. Complete contracts supply negative facts only when no analyzable body is available.
