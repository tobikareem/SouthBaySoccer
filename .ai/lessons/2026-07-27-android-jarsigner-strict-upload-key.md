---
name: android-jarsigner-strict-upload-key
description: Tolerate only jarsigner strict warning category 4 for a pinned Android upload certificate
area: build
created: 2026-07-27
---

**Context:** The Android release workflow verified its signed AAB before uploading to Google Play.

**Problem:** `jarsigner -verify -strict` returned exit code 4 even though the AAB signature was
valid. Android upload certificates are self-signed and the bundle signature is normally
untimestamped, so strict mode promoted those expected warnings to fatal signer errors.

**Resolution:** Capture the `jarsigner -verify -strict` exit status and tolerate only status 4, the
known signer-error category caused by the self-signed upload-certificate chain. Reject every other
nonzero status, then independently extract the AAB signer certificate and compare its SHA-256
fingerprint with the configured upload certificate.

**Takeaway:** For Android AAB release automation, do not equate a public-CA trust chain with a valid
upload signature. Keep strict verification, allow only the proven self-signed-chain status, and
enforce signer identity with a pinned certificate fingerprint.

Related: [[android-google-play-release]]
