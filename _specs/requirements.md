# SouthBaySoccer — Requirements

Spec-driven requirements for the whole product. Grounded in
[`documentation/architecture.md`](../documentation/architecture.md). User stories use the
**Gherkin** (`Given / When / Then`) format for acceptance criteria.

- Every story has an ID (e.g. `RSVP-3`). `design.md` and `tasks.md` reference these IDs.
- A story is **Done** only when every scenario is covered by an automated test and the behavior
  works through the Function App (and, where relevant, the MAUI client).
- Authority rules from the architecture are restated as invariants, not re-decided here.
- **Delivery began UI-first and is now in API integration:** the MAUI/XAML client was built first
  against **seed data**. Backend features are now substantially implemented through M9, and current
  work wires screen service interfaces to the Functions API while keeping Seed mode for deterministic
  demos/tests. Backend-dependent scenarios validated against seeds should be re-verified server-side
  as each API slice lands. See `design.md` §12.

## Personas / Roles

| Role | Description |
|------|-------------|
| **Owner** | Ultimate authority; billing, configuration, all admin powers. |
| **Admin** | Manages players, sessions, finances, waivers, roles. |
| **GameAdmin** | Runs a game day: check-in, teams, live stats. |
| **Captain** | Helps assign teams and report results for a match. |
| **Player** | Registered member: pays dues, RSVPs, views own stats. |
| **Guest** | Plays without an account; stats tracked on a guest `PlayerProfile`; may later claim. |

Authorization is by **policy** (`CanManageSessions`, `CanCheckInPlayers`, `CanRecordStats`,
`CanAssignTeams`, `CanManagePlayers`, `CanViewFinancialStatus`); roles satisfy policies. Every
protected operation is authorized server-side; client-side hiding is UX only.

## Global invariants (apply to all stories)

- **INV-1** Stripe verified webhooks are the source of truth for payment/membership state; a client redirect is never proof of payment.
- **INV-2** A player must be payment-eligible **and** have a current waiver before RSVP, unless an authorized admin records an explicit override.
- **INV-3** Session capacity and waitlist order are enforced in a single serializable transaction.
- **INV-4** RSVP deadline locks normal player changes.
- **INV-5** A `Season` is explicit, never inferred only from dates.
- **INV-6** Stats attach at the `Match` grain; season/career figures aggregate `Match → Session → Season`.
- **INV-7** Only raw stat rows persist (`MatchEvent`, `PlayerRatingVote`, `PlayerLike`, `MatchAward`); totals/averages are derived on read. Own goals are not credited; ≤1 assist per goal.
- **INV-8** Players rate peers, never themselves; one vote per voter per rated player per match; Score is an integer 0–10.
- **INV-9** Teams are per-match and never persist across sessions; the only player↔team link is `TeamAssignment`.
- **INV-10** All Domain entities use `Guid` IDs, UTC timestamps, audit fields, and soft delete (security/operational records excepted).
- **INV-11** Every protected endpoint is fail-closed: exactly one of `[RequirePolicy]` or `[AllowAnonymous]`.
- **INV-12** RSVP state records attendance intent only. Actual attendance is represented by
  `CheckIn` and a derived/recorded attendance outcome, not by mutating RSVP into check-in/no-show
  states.
- **INV-13** MAUI product UI uses bundled Font Awesome Free font glyphs for pictograms. Do not use
  Unicode emoji, platform emoji fonts, or text characters as substitute icons. Icon meaning must
  also be expressed through text or semantic descriptions.

---

## Epic AUTH — Authentication & Identity

### AUTH-1 — Register an account
*As a* prospective player, *I want* to register with email and password, *so that* I can become a member.

```gherkin
Scenario: Successful registration creates an unconfirmed account and player profile
  Given no account exists for "amaka@example.com"
  When I register with a valid email and a password meeting the policy
  Then an unconfirmed ApplicationIdentityUser is created
  And a linked PlayerProfile is created with IsGuest = false
  And a confirmation email is queued via IEmailService
  And the response does not include any token

Scenario: Duplicate email is rejected
  Given an account already exists for "amaka@example.com"
  When I register with "amaka@example.com"
  Then the request is rejected with 409 Conflict
  And no second account is created

Scenario: Weak password is rejected
  Given the password policy requires length and complexity
  When I register with the password "1234"
  Then the request is rejected with 400 and a validation problem detail
```

### AUTH-2 — Confirm email
*As a* registered user, *I want* to confirm my email, *so that* I can sign in.

```gherkin
Scenario: Valid confirmation token activates the account
  Given I registered and received a confirmation token
  When I submit the token before it expires
  Then my email is marked confirmed
  And I am allowed to sign in

Scenario: Expired or tampered token is rejected
  Given my confirmation token has expired
  When I submit it
  Then confirmation fails with 400
  And my email remains unconfirmed
```

### AUTH-3 — Sign in
*As a* member, *I want* to sign in, *so that* I receive access and refresh tokens.

```gherkin
Scenario: Valid credentials issue tokens
  Given my account is confirmed and not locked
  When I sign in with correct credentials
  Then I receive a short-lived JWT access token and a rotating refresh token
  And the access token contains identity and authorization claims sufficient for server-side policy evaluation

Scenario: Invalid credentials are rejected without leaking which field failed
  When I sign in with a wrong password
  Then the response is 401 with a generic message
  And a failed-attempt is recorded toward lockout
```

### AUTH-4 — Refresh tokens (rotation & reuse detection)
*As a* signed-in client, *I want* expired access tokens refreshed safely, *so that* my session continues without re-login.

```gherkin
Scenario: Refresh rotates the token family
  Given I hold a valid, unused refresh token
  When I exchange it
  Then I receive a new access token and a new refresh token
  And the previous refresh token is invalidated atomically

Scenario: Reuse of a consumed refresh token revokes the whole family
  Given a refresh token has already been used once
  When the same token is presented again
  Then the entire refresh-token family is revoked in one atomic operation
  And the response is 401

Scenario: Concurrent requests trigger only one refresh
  Given the access token is expired
  When several client requests hit the API at once
  Then exactly one refresh operation runs
  And the other requests reuse the refreshed token
```

### AUTH-5 — Password reset
```gherkin
Scenario: Reset with a valid token
  Given I requested a password reset and received a token
  When I submit the token with a policy-compliant new password
  Then my password is updated and existing refresh tokens are revoked
```

### AUTH-6 — Account lockout
```gherkin
Scenario: Repeated failures lock the account temporarily
  Given the lockout threshold is N attempts
  When I fail sign-in N times
  Then the account is locked for the configured duration
  And further attempts return 401 with a locked indication
```

### AUTH-7 — Welcome Back screen

> **Per-story spec (pilot):** the canonical AUTH-7/8/9 specs now live under
> [`stories/AUTH-7-welcome-back-screen/`](stories/AUTH-7-welcome-back-screen/requirements.md),
> [`stories/AUTH-8-continue-with-whatsapp/`](stories/AUTH-8-continue-with-whatsapp/requirements.md), and
> [`stories/AUTH-9-pickup-pal-actions/`](stories/AUTH-9-pickup-pal-actions/requirements.md). The
> summaries below remain as the overview index.

*As a* returning SouthBaySoccer player, *I want* a clear Pickup Pal phone sign-in screen, *so that* I can
connect the app to my Pickup Pal account without entering a password.

This is the first application screen and directly implements the first `signin` screen in
[`documentation/mobile-wireframes.html`](../documentation/mobile-wireframes.html).

```gherkin
Scenario: Signed-out launch displays the Welcome Back screen
  Given I do not have a valid local authenticated session
  When the MAUI application starts
  Then the Welcome Back screen is the initial route
  And the Shell flyout and authenticated tab navigation are not visible
  And the screen displays the SouthBay Soccer football mark
  And the header displays "SouthBay Soccer"
  And the header subtitle displays "Pickup soccer, organized."
  And the content displays "WELCOME BACK"
  And the primary heading displays "Your next game starts here."

Scenario: The screen matches the first mobile wireframe hierarchy
  Given the Welcome Back screen is displayed
  Then it has a Flag Green-to-Pine header with the white flag stripe and decorative motif
  And the content is a white-dominant scrollable surface with 16 device-independent-pixel side padding
  And the phone number field appears before the primary action
  And the security notice appears after the primary action
  And the Pickup Pal bot card appears before the "not on pickup pal?" divider
  And the external signup action and explanatory copy are the final content

Scenario: Iconography uses Font Awesome instead of emoji
  Given the Welcome Back screen contains football, phone, shield, and external-link pictograms
  Then each pictogram is rendered from a bundled Font Awesome Free font
  And no Unicode emoji is used
  And every informational or interactive icon has a semantic description

Scenario: Screen remains usable with large text and a narrow viewport
  Given the operating system text scale is increased
  When the Welcome Back screen is rendered on the narrowest supported phone width
  Then text is not clipped
  And content remains vertically scrollable
  And every interactive target is at least 44 device-independent pixels
```

### AUTH-8 - Pickup Pal phone sign-in from Welcome Back
*As a* returning player, *I want* to sign in with the phone number on my Pickup Pal account, *so that*
SouthBaySoccer can verify my account and issue app tokens without a password.

Current scope: this is direct phone-number sign-in backed by a Pickup Pal API lookup. WhatsApp
challenge/link authentication is deferred.

```gherkin
Scenario: Valid phone number starts password-free sign-in
  Given the Welcome Back screen is displayed
  And I enter a valid international phone number
  When I select "Sign in with phone"
  Then the client posts the phone number to the SouthBaySoccer phone sign-in endpoint
  And the primary action enters a busy state and cannot be submitted twice
  And no authenticated route opens until SouthBaySoccer returns access and refresh tokens

Scenario: Invalid phone number is rejected locally
  Given the Welcome Back screen is displayed
  When I enter a missing or invalid phone number
  And I select "Sign in with phone"
  Then an inline validation message explains the required phone format
  And no network request is sent

Scenario: Pickup Pal account is not found
  Given a valid phone number is entered
  When Pickup Pal does not have a user for that number
  Then a non-sensitive message asks me to sign up on Pickup Pal
  And no tokens are stored
  And the app remains on the Welcome Back screen

Scenario: Phone sign-in failure is recoverable
  Given a valid phone number is entered
  When the phone sign-in request fails because the service is unavailable or the device is offline
  Then a non-sensitive error message is displayed
  And the number remains available for correction or retry
  And the primary action becomes enabled again

Scenario: Pickup Pal phone match completes sign-in
  Given Pickup Pal has a user for my phone number
  When the Function App syncs the returned Pickup Pal user locally
  Then the Function App issues SouthBaySoccer access and refresh tokens
  And the tokens are stored using platform secure storage
  And the app replaces the Welcome Back route with the authenticated Sessions route
```
### AUTH-9 — Pickup Pal help and signup actions

```gherkin
Scenario: Open the configured Pickup Pal bot
  Given the Welcome Back screen displays the Pickup Pal bot card
  When I select "Open"
  Then the app opens the configured Pickup Pal WhatsApp conversation
  And the bot number is loaded from typed configuration rather than duplicated page text

Scenario: Sign up on Pickup Pal
  Given I am not registered with Pickup Pal
  When I select "Sign up on Pickup Pal"
  Then the app opens the configured HTTPS signup page in the system browser
  And the app does not treat returning from the browser as authenticated

Scenario: External application cannot be opened
  Given WhatsApp or a browser cannot handle the configured URI
  When I select an external action
  Then the app displays a recoverable explanation
  And remains on the Welcome Back screen
```

---

## Epic PROF — Player Profiles & Guests

### PROF-1 — Maintain my profile
*As a* player, *I want* to edit my profile (name, photo, preferred position, contact), *so that* admins and teammates can identify me.

```gherkin
Scenario: Update profile fields
  Given I am signed in
  When I update my display name and preferred position
  Then the changes are saved with audit fields populated
  And UTC timestamps are stored
```

### PROF-2 — Emergency contact
```gherkin
Scenario: Add an emergency contact
  Given I am signed in
  When I add an emergency contact with name and phone
  Then it is stored against my PlayerProfile
  And it is never exposed in public rosters or logs
```

### PROF-3 — Guest profile (no login)
*As a* GameAdmin, *I want* to add a drop-in guest who has no account, *so that* their stats are still recorded.

```gherkin
Scenario: Create a guest profile
  Given I have CanManagePlayers
  When I create a guest player "Tunde"
  Then a PlayerProfile is created with IsGuest = true and no ApplicationIdentityUser
  And the guest can be assigned to matches and accrue stats
```

### PROF-4 — Claim a guest profile (profile merge)
*As a* guest who later registers, *I want* my past stats merged into my account, *so that* my career history is preserved.

```gherkin
Scenario: Merge transfers career stats with an audit trail
  Given a guest PlayerProfile "Tunde" has match stats
  And I register and an admin links the guest to my account
  When the merge runs
  Then a ProfileMerge audit record is created
  And all guest match stats are transferred to my profile without duplication
  And the guest profile is retired
```

---

## Epic WAIV — Waivers & Compliance

### WAIV-1 — Accept waiver and code of conduct
```gherkin
Scenario: Accepting the current waiver records consent
  Given a current WaiverDocument version exists
  When I accept the waiver and code of conduct
  Then a WaiverAcceptance is recorded with version, timestamp (UTC), and my profile
```

### WAIV-2 — Waiver gates RSVP
```gherkin
Scenario: No current waiver blocks RSVP
  Given I have not accepted the current waiver version
  When I attempt to RSVP to a session
  Then the RSVP is rejected with a clear "waiver required" reason
  And no RsvpResponse is created
```

### WAIV-3 — New waiver version requires re-acceptance
```gherkin
Scenario: A new waiver version invalidates prior acceptance for gating
  Given I accepted waiver version 1
  And the admin publishes waiver version 2
  When I attempt to RSVP
  Then I am required to accept version 2 first
```

---

## Epic PAY — Memberships & Payments (Stripe)

### PAY-1 — Start checkout for membership/dues
*As a* player, *I want* to pay monthly dues, *so that* I stay eligible to RSVP.

```gherkin
Scenario: Function App creates a Checkout session
  Given I am signed in
  When I request to pay my membership
  Then the Function App creates a Stripe Checkout/PaymentIntent server-side
  And returns a short-lived checkout URL or client-safe data
  And no Stripe secret key is ever sent to the client
```

### PAY-2 — Process verified webhooks idempotently
```gherkin
Scenario: A new signed event updates membership atomically
  Given Stripe sends a signed "invoice.paid" webhook
  When the webhook function verifies the signature against the raw body
  Then it inserts the event ID under a unique constraint
  And updates the PaymentLedger and Membership in the same transaction
  And responds 2xx

Scenario: A duplicate event is acknowledged without double-processing
  Given an event ID has already been recorded
  When the same event is delivered again
  Then the unique-key conflict is treated as an already-processed success
  And no state is changed
  And the response is 2xx

Scenario: An out-of-order older event does not overwrite newer state
  Given membership reflects a newer Stripe event
  When an older event arrives
  Then current Stripe state is retrieved when ordering is material
  And stale state is not applied

Scenario: Invalid signature is rejected
  When a webhook arrives with an invalid signature
  Then it is rejected and no state changes
```

### PAY-3 — Membership status reflects Stripe only
```gherkin
Scenario: Admin cannot manually mark a member paid
  Given an admin views an unpaid member
  Then there is no action to set "paid" directly
  And status changes only via verified Stripe events
```

### PAY-4 — Payment history / ledger
```gherkin
Scenario: Player views their payment history
  Given I have past payments
  When I open my payment history
  Then I see ledger entries derived from verified events, newest first, paginated
```

### PAY-5 — Guest drop-in payment
```gherkin
Scenario: Guest pays a one-time drop-in
  Given a session allows drop-in payment
  When a guest pays the drop-in via hosted checkout
  Then a verified webhook records the one-time payment
  And the guest becomes eligible for that session only
```

### PAY-6 — Failed payment handling
```gherkin
Scenario: Failed invoice marks membership not-eligible
  Given Stripe sends "invoice.payment_failed"
  When it is processed
  Then membership eligibility is updated to reflect the failure
  And a dunning notification is queued
```

---

## Epic SES — Seasons, Venues & Sessions

### SES-1 — Manage seasons
```gherkin
Scenario: Create a season
  Given I have CanManageSessions
  When I create season "2026"
  Then it exists as a first-class entity
  And sessions can be attached to it explicitly
```

### SES-2 — Manage venues
```gherkin
Scenario: Create a venue with location
  Given I have CanManageSessions
  When I add a venue with name and address
  Then it is geocoded via IMapsService and stored for reuse
```

### SES-3 — Create recurring sessions (idempotent)
```gherkin
Scenario: Timer creates upcoming sessions once
  Given a RecurrenceRule produces a weekly occurrence
  When the timer trigger runs (including retries or scaled instances)
  Then each occurrence is created at most once
  And duplicate creation is prevented by a unique occurrence key
```

### SES-4 — Edit or cancel a session
```gherkin
Scenario: Cancelling notifies RSVP'd players
  Given a session has confirmed RSVPs
  When an admin cancels it with a reason
  Then the session is marked cancelled
  And affected players are queued a cancellation notification
```

### SES-5 — Configure capacity and deadline
```gherkin
Scenario: Capacity and deadline are validated
  Given I configure a session
  When I set capacity <= 0 or a deadline after the start time
  Then the request is rejected with 400 validation errors
```

---

## Epic RSVP — RSVP & Waitlist

### RSVP-1 — Submit an eligible RSVP
```gherkin
Scenario: Eligible player claims a spot
  Given I have a current waiver and eligible payment state
  And the session has open capacity and the deadline has not passed
  When I RSVP "Going"
  Then an RsvpResponse is created in the confirmed list

Scenario: Ineligible player is blocked (INV-2)
  Given my membership is not eligible
  When I RSVP
  Then the RSVP is rejected with an eligibility reason
  And no spot is consumed
```

### RSVP-2 — Capacity enforced transactionally (INV-3)
```gherkin
Scenario: Concurrent requests for the final slot
  Given exactly one open spot remains
  When two eligible players RSVP simultaneously
  Then exactly one is confirmed
  And the other is offered the waitlist
  And capacity is never exceeded
```

### RSVP-3 — Join the waitlist
```gherkin
Scenario: Full session adds to ordered waitlist
  Given the session is at capacity
  When an eligible player RSVPs
  Then a WaitlistEntry is created with the next position
```

### RSVP-4 — Auto-promote on cancellation
```gherkin
Scenario: Cancellation promotes the next eligible waitlisted player
  Given the session is full and has a waitlist
  When a confirmed player cancels before the deadline
  Then the next eligible waitlisted player is promoted atomically
  And a PlayerWaitlistPromoted domain event is raised
  And the promoted player is notified

Scenario: Skipped if next waitlisted player is no longer eligible
  Given the next waitlisted player lost eligibility
  When promotion runs
  Then that player is skipped and the following eligible player is promoted
```

### RSVP-5 — Cancel my RSVP
```gherkin
Scenario: Cancel before deadline frees a spot
  Given I am confirmed and the deadline has not passed
  When I cancel
  Then my spot is released and waitlist promotion is attempted
```

### RSVP-6 — Deadline lock (INV-4)
```gherkin
Scenario: After the deadline, players cannot change RSVP
  Given the RSVP deadline has passed
  When a player tries to RSVP or cancel
  Then the change is rejected
  And only an authorized admin may modify the roster
```

### RSVP-7 — Admin override
```gherkin
Scenario: Admin adds an ineligible player with override
  Given I have the appropriate policy
  When I add a player despite ineligibility and record an override reason
  Then the RSVP is created with an auditable override
```

---

## Epic CHK — Check-in

### CHK-1 — Check in on game day
```gherkin
Scenario: GameAdmin checks in an arriving player
  Given I have CanCheckInPlayers
  And a player has a confirmed RSVP
  When I mark them present
  Then a CheckIn is recorded with timestamp
```

### CHK-2 — No-show tracking
```gherkin
Scenario: Confirmed but not checked-in is a no-show
  Given a player was confirmed but never checked in
  When the session closes
  Then their attendance outcome is recorded as NoShow for reliability reporting
  And the original RSVP intent remains auditable
```


### GDAY-1 - Game-day check-in tab

> **Per-story spec:** [`stories/GDAY-1-game-day-check-in-tab/`](stories/GDAY-1-game-day-check-in-tab/requirements.md)
> defines the player Game Day tab, 7:30 PM-7:45 PM check-in window, server-authoritative timestamp,
> and GameAdmin late override audit.

---

## Epic TEAM — Teams & Matches

### TEAM-1 — Create matches within a session
```gherkin
Scenario: A game day produces multiple matches
  Given checked-in players exist
  When a GameAdmin starts a match
  Then a Match is created under the Session
  And it can hold two MatchTeams
```

### TEAM-2 — Assign balanced teams per match (INV-9)
```gherkin
Scenario: Assign players to two sides for one match
  Given I have CanAssignTeams
  When I draft players into MatchTeam A and B for a Match
  Then each player has a TeamAssignment scoped to that Match only
  And no team reference is stored on the PlayerProfile
```

### TEAM-3 — Record match result
```gherkin
Scenario: Record the score line
  Given a Match has two MatchTeams
  When the result is recorded
  Then a MatchResult stores goals for/against and outcome (W/D/L) per MatchTeam
  And scores cannot be negative
```


### TEAM-4 - Assign captains and draft teams

> **Per-story spec:** [`stories/TEAM-4-captain-assignment-and-draft/`](stories/TEAM-4-captain-assignment-and-draft/requirements.md)
> defines admin selection of 2, 3, or 4 captains, session-scoped `TeamDraft.PickPlayer` permission, and
> captain checkbox selection from the confirmed game list.

---

## Epic STAT — Stats

### STAT-1 — Record match events
```gherkin
Scenario: Record a goal and its assist
  Given I have CanRecordStats and a Match in progress
  When I record a Goal for player A assisted by player B
  Then a MatchEvent(Goal) and a linked MatchEvent(Assist) are stored
  And at most one assist is linked to that goal

Scenario: Own goal is not credited to a scorer (INV-7)
  When I record an OwnGoal
  Then it is stored as OwnGoal and credited to no player's goal tally
```

### STAT-2 — Player match stats row
```gherkin
Scenario: Every participant has a stats row
  Given a Match has assigned players including guests
  When the match is recorded
  Then each participant has exactly one PlayerMatchStats row (appearance, minutes, started, GK flag)
```

### STAT-3 — Peer rating votes (INV-8)
```gherkin
Scenario: Rate a peer 0–10
  Given I played in a Match
  When I rate teammate B with score 8
  Then a PlayerRatingVote(MatchId, me, B, 8) is stored

Scenario: Self-vote is rejected
  When I try to rate myself
  Then the vote is rejected

Scenario: Duplicate vote for the same peer in the same match is rejected
  Given I already rated B for this Match
  When I rate B again
  Then the unique (MatchId, Voter, Rated) constraint rejects it

Scenario: Match rating shows from the first vote with no quorum
  Given player B received one vote of 8 and later a vote of 6
  Then B's match rating is 8 after the first vote and 7 after the second
  And a match with zero votes does not contribute to B's career average
```

### STAT-4 — Likes
```gherkin
Scenario: Like a peer once per match
  When I like player C for a Match
  Then a PlayerLike is stored, deduped per rater per match
```

### STAT-5 — MVP award
```gherkin
Scenario: Record an explicit MVP
  Given I have CanRecordStats
  When I award MVP to player A for a Match
  Then a MatchAward(MVP) is recorded as a distinct leaderboard axis
```

### STAT-6 — Lock stats and correct via audit
```gherkin
Scenario: Locked match stats require an audited correction
  Given a Match's stats are published and locked
  When an admin adjusts a stat
  Then a StatCorrection audit record is created
  And the raw rows are amended only through that correction, never silently
```


### STAT-9 - Captain approval and team results

> **Per-story spec:** [`stories/STAT-9-captain-approval-and-results/`](stories/STAT-9-captain-approval-and-results/requirements.md)
> defines post-game captain approval of submitted goals/assists, conflict review, W/D/L result
> recording, and recent-form derivation from `TeamAssignment` + `MatchResult`.

---

## Epic LEAD — Leaderboards & Career stats

### LEAD-1 — Season leaderboards (read projections, INV-6/7)
```gherkin
Scenario: Top scorers computed from raw rows
  Given matches with recorded goals across a Season
  When I open the season leaderboard
  Then top scorers, assists, appearances, average rating, likes, and MVP counts
       are computed by aggregating raw rows across Match → Session → Season
  And no maintained totals table is used
```

### LEAD-2 — Career / profile stats
```gherkin
Scenario: Profile shows career figures
  Given a player has stats across multiple seasons
  When I view their profile
  Then career G/A/MP, average rating, total likes, and MVP count are derived on read
```

### LEAD-3 — Tie-breakers
```gherkin
Scenario: Golden boot tie-break
  Given two players have equal goals
  Then the ranking breaks the tie by fewer appearances, then more assists
```

---

## Epic NOTIF — Notifications

### NOTIF-1 — Transactional email
```gherkin
Scenario: Queue an email through the outbox
  Given an event requires notifying a player
  When the business transaction commits
  Then an outbox message is written in the same transaction
  And an idempotent dispatcher later sends it via SendGrid and marks it delivered
```

### NOTIF-2 — SMS
```gherkin
Scenario: Send an SMS via Twilio behind ISmsService
  Given a player opted into SMS
  When a reminder is due
  Then an SMS is queued and sent via ISmsService
  And no message is sent twice on dispatcher retry
```

### NOTIF-3 — Reminders
```gherkin
Scenario: RSVP and dues reminders
  Given a session approaches its deadline or dues are due
  When the reminder timer runs
  Then eligible players receive a single reminder per cycle
```

---

## Epic ADMIN — Admin & Live Game

### ADMIN-1 — Admin dashboard
```gherkin
Scenario: Dashboard summarizes operations
  Given I have admin policies
  When I open the dashboard
  Then I see active members, dues collected (from verified events), upcoming sessions,
       RSVP counts, and no-show trends
  And all figures come from server-side queries, paginated where large
```

### ADMIN-2 — Live game mode
```gherkin
Scenario: Record stats live, tolerant of poor connectivity
  Given I am running a match on a phone with intermittent signal
  When I record goals/assists offline
  Then entries are queued with idempotency keys
  And sync to the server when connectivity returns without duplicating events
```

### ADMIN-3 — Roles & policies
```gherkin
Scenario: Assign a role
  Given I have CanManagePlayers
  When I grant "GameAdmin" to a player
  Then their subsequent tokens carry the role and satisfy the mapped policies
```

### ADMIN-4 - Create and publish session

> **Per-story spec:** [`stories/ADMIN-4-create-session-publish/`](stories/ADMIN-4-create-session-publish/requirements.md)
> defines the admin flow for creating a dated/location-based session and publishing it to the team
> so players can RSVP.

---

## Non-functional requirements (NFR)

- **NFR-Security** — HTTPS only; no provider secret keys in the MAUI package; no secrets/PII in URLs, logs, or telemetry; Key Vault via managed identity; least-privilege identities. (architecture §8)
- **NFR-AuthZ** — Fail-closed endpoint classification (INV-11); every protected operation re-authorized server-side.
- **NFR-Reliability** — Outbox + idempotent dispatcher; serializable RSVP/waitlist transaction with bounded retry then 409; idempotent webhooks and timers. (architecture §15)
- **NFR-Observability** — Correlation IDs across client/Functions/providers; structured logs; Application Insights; dead-letter alerting. (architecture §15)
- **NFR-Performance** — Large tables (RSVPs, matches, stats, rating votes ~ participants²) always filtered and paginated; never expose `IQueryable`. (architecture §13)
- **NFR-Offline** — Client SQLite only as an intentional, expiring cache; never determines payment/waiver/role/RSVP eligibility. (architecture §6)
- **NFR-Accessibility** — Semantic descriptions, contrast, keyboard/focus, mobile touch targets. (architecture §6)
- **NFR-Iconography** — MAUI pictograms use bundled, licensed Font Awesome Free glyphs with typed
  constants and accessible names; Unicode emoji are prohibited in product UI. (INV-13)
- **NFR-Time** — UTC everywhere; convert to local only at the UI boundary. (INV-10)
- **NFR-Migration** — Each increment leaves the solution buildable; do not mix sample removal with new feature behavior. (architecture §18)

## Traceability

`tasks.md` maps each milestone/task to the story IDs above; `design.md` maps each epic to the
architecture layers and components. The high-risk scenarios in architecture §17 correspond to:
AUTH-3/4, RSVP-2/4, PAY-2, SES-3, STAT-3, LEAD-1, PROF-4, and NOTIF-1.

The first-screen client trace is `AUTH-7/8/9 + INV-13` → `design.md` §11 →
`tasks.md` M11.0a and M11.3a–M11.3d.

