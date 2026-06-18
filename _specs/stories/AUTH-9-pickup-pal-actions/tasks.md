# AUTH-9 — Pickup Pal actions · Tasks

AUTH-9 slice of milestone **M11** (full roadmap: [`../../tasks.md`](../../tasks.md)).

- [x] **M11.3b** (AUTH-9 slice) Add typed Pickup Pal configuration (bot number, bot URI, signup URI,
  approved callback scheme), the `IExternalLauncher` abstraction, and the `OpenPickupPalBotCommand` /
  `OpenPickupPalSignupCommand`. External-return alone must not authenticate.
  — Stories: `AUTH-9` · Projects: MAUI client · Depends on: M11.3a.

- [x] **M11.3d** (AUTH-9 slice) `Client.Tests`: bot/signup commands use typed configuration; an
  external-launch failure shows a recoverable message and keeps the user on the Welcome Back screen;
  returning from an external app does not authenticate.
  — Stories: `AUTH-9` · Depends on: M11.3c.

**Done when:** both external actions launch from typed configuration, failures are recoverable, no
external return is treated as authentication, and all AUTH-9 scenarios have passing tests.
