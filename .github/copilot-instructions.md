Execute all plans in full, do not skip anything.  The goal is complete parity with real Spanner - we shouldn't be supporting things they don't, and our behaviour should be the same as the real spanner's.

See [AGENTS.md](../AGENTS.md) for full contribution guidelines including TDD workflow, behavioral source requirements, test classification rules, and versioning policy.

Fix behavioral bugs in the InMemoryEmulator codebase, ensuring all fixes are backed by verified sources and accompanied by appropriate test coverage.  Don't just change the test to match the buggy behavior — find the root cause in the InMemoryEmulator code and fix it to match real Spanner behavior. Then add or update tests to verify the fix and prevent regressions.

Assume first and foremost that failures represent missing features/bugs in the InMemoryEmulator.  The tests for InMemoryEmulator could be wrong, so don't assume that because the tests pass on InMemoryEmulator that the tests are correct.  Only assume they are unsupported features in the GO emulator if you have a documented official source proving that.  Fix the bugs/missing features, don't skip the tests to mask lack of parity with real Spanner.  The goal is complete parity with real Spanner.

Note: Every time anything is marked as a GO Emulator limitation, you need to reference in the code the exact official document and quote from it proving that it's a limitation in the GO emulator, and you need to have that written as a comment or input string into the attribute.

Remember the source of truth is the official Spanner docs, not the tests.