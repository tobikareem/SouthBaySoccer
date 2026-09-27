# Use unique business keys in shared SQL fixtures

Infrastructure tests in the same xUnit collection share one migrated SQL database for the fixture
lifetime. A test that asserts uniqueness by phone hash must use a phone number not created by earlier
tests; otherwise it passes alone but fails in the full suite. Give each scenario a distinct business
key, even when collection execution is serialized.
