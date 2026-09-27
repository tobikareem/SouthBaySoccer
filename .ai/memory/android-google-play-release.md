---
name: android-google-play-release
description: release/android publishes a signed N9ja Bay AAB to Google Play internal testing
type: project
created: 2026-07-27
---

The Android release workflow is `.github/workflows/release-android.yml`. A push to
`release/android` runs the portable test gate, builds `com.pickupsoccer.n9jabay`, signs it with the
upload key, verifies its certificate and manifest metadata, and publishes it to Google Play's
internal track through the `google-play-internal` GitHub environment.

**Why:** Google Play App Signing owns the app-signing key; CI must use only the registered upload
key and must generate a monotonically increasing `versionCode`.
**How to apply:** Keep the keystore, its password, and the Play service-account JSON in environment
secrets. Keep the expected upload-certificate SHA-256 fingerprint in the
`ANDROID_UPLOAD_CERT_SHA256` environment variable, and restrict the environment to
`release/android`.

Related: [[release-mobile-api-default]]
