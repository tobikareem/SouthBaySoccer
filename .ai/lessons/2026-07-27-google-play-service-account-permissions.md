---
name: google-play-service-account-permissions
description: Google Cloud IAM does not grant a CI service account permission to publish in Play Console
area: tooling
created: 2026-07-27
---

**Context:** The Android release workflow authenticated with a Google service-account JSON key but
the Google Play upload action failed with `The caller does not have permission`.

**Problem:** Enabling the Android Publisher API or assigning Google Cloud IAM roles does not grant
access to a Play developer account or a specific Play app.

**Resolution:** Add the exact service-account `client_email` under Google Play Console **Users and
permissions**, grant it access to N9ja Bay (`com.pickupsoccer.n9jabay`), and grant the
**Release apps to testing tracks** permission. Keep production and financial permissions disabled.

**Takeaway:** Google Cloud authentication and Google Play authorization are separate. Every CI
service account must be configured in both systems before the Publisher API can create an edit.

Related: [[android-jarsigner-strict-upload-key]]
