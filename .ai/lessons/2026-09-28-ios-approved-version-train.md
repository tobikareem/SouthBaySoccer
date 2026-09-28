# An approved iOS version cannot accept another upload

Release run 36476591796 passed tests, signing and IPA publication, then Apple rejected upload with
90062 and 90186: version 1.1 was already approved and its pre-release train was closed.
Increment ApplicationDisplayVersion to the next release (1.1.1 for this patch), not just
ApplicationVersion. CI already stamps a unique build number and does not change the marketing version.
A rerun of the old commit still contains 1.1 and will fail again; release/ios must include the version
change before a new release run. Simulator build metadata checks do not prove Apple upload acceptance.
