# AUTH-9 — Pickup Pal actions · Design

Realizes [`requirements.md`](requirements.md). Screen composition is in
[`AUTH-7 design`](../AUTH-7-welcome-back-screen/design.md) (the bot card and signup action). This
file specifies the external-launch behavior.

## Components

- `OpenPickupPalBotCommand` and `OpenPickupPalSignupCommand` on `WelcomeBackPageModel`.
- `IExternalLauncher` abstraction with `OpenPickupPalBotAsync(...)` and `OpenPickupPalSignupAsync(...)`.
- **Typed configuration** (options) for: Pickup Pal bot number, bot WhatsApp URI, HTTPS signup URI,
  and the approved deep-link callback scheme. Page text never hard-codes the number/URI — display
  values bind from configuration.

## Rules

- The signup page opens in the **system browser** over HTTPS.
- Returning from WhatsApp or the browser **never** establishes a session (authentication only via the
  `AUTH-8` verified challenge exchange).
- If WhatsApp or a browser cannot handle the configured URI, show a recoverable explanation and stay
  on the Welcome Back screen.
- Don't place sensitive values in URIs, logs, or telemetry (`NFR-Security`).

## Test design (`Client.Tests`) — AUTH-9 slice

- bot and signup commands use the typed configuration (not page text);
- an external-launch failure shows a recoverable message and keeps the user on the screen;
- returning from an external app does not flip the app into an authenticated state.
