// golden-scenario: vc-exception-multi-entry
// E branches literally true to A; B branches literally true to R.
// The syntactic false branches make DFS cut A -> B inside the exception SCC.
// Original execution returns 1; neither false Ensures nor normal vacuity holds.
