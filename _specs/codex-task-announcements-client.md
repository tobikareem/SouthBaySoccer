# Codex task — Group announcements: MAUI client presentation layer

You are implementing the **MAUI client half** of the group-announcements feature in
`D:\source\SouthBaySoccer`. Claude is implementing the backend half **in parallel, in the same
working tree**. Read this whole brief before writing code.

## Division of labour — do not cross this line

| Owner | Scope |
|---|---|
| **You (Codex)** | `SouthBaySoccer/` (the MAUI client project) and `tests/SouthBaySoccer.Client.Tests/` — controls, pages, page models, navigation, typed API client, seed clients, caching decorator, client tests. |
| **Claude (do not touch)** | `src/SouthBaySoccer.Domain`, `.Application`, `.Contracts`, `.Infrastructure`, `.Functions`, and `tests/SouthBaySoccer.{Domain,Application,Infrastructure,Functions}.Tests`. |

`src/SouthBaySoccer.Contracts/Announcements/AnnouncementDtos.cs` **already exists and is frozen** —
it is the agreed interface between the two halves. Consume it; do not edit it. If you believe a DTO
is wrong, stop and say so in your final report rather than changing it.

Both halves land in the same tree, so **only create or modify files inside your scope**. If a build
error points into `src/`, it is Claude's half — report it, don't fix it.

## The design is already decided

The authoritative design is `documentation/mobile-wireframes.html` — open it in a browser and use
the `adminbroadcast` and `notifications` screens (the wireframe is interactive; the "All screens"
toggle renders every screen at once). The implementation contract is `_specs/client-ui.md` §11 and
**§11.1**, which was just rewritten to describe exactly these two screens. Follow both. Where they
disagree, the wireframe wins and you should flag it.

Two screens:

1. **Admin broadcast composer** (`admin-broadcast`), reached from the Sessions "Admin → Broadcast"
   link. Blocks in order: *audience → message → how it lands → send*.
2. **Player announcements feed** (`announcements`), reached from the Sessions notification bell.

## Backend contract you are coding against

Base address and pipeline are already configured; add your typed client the same way the others are
(`Services/Clients/ClientServiceCollectionExtensions.cs`). All routes are relative to the existing
`BaseAddress`. All require a bearer token, which `AuthenticationHandler` already attaches.

> **Contract revision — 2026-07-28, after backend code review.** Three changes landed *after* this
> brief was first written. If you already coded against the original shapes, re-check these:
> 1. `AnnouncementFeedResponse` gained **`Guid? NextCursorId`**. Paging is a composite
>    `(NextCursorUtc, NextCursorId)` cursor — send **both** back as `before` *and* `beforeId`.
>    Sending only the timestamp permanently skips announcements that share a send time.
> 2. **`SentAnnouncementDto.RecipientCount` is no longer `GroupChatDto.MemberCount`.** It is now the
>    count of players linked to the group in our database, excluding the author — the people who can
>    actually receive an in-app announcement. The WhatsApp roster count is a larger, different
>    population and made "seen by 12 of 8" possible. **Implication for you:** the composer CTA still
>    reads `GroupChatDto.MemberCount` (that is the audience the admin recognises), so the CTA number
>    and the later read-receipt denominator can legitimately differ. Do not "fix" that by deriving
>    one from the other, and do not render the receipt as a percentage of the CTA number.
> 3. `UnreadAnnouncementsResponse.UnreadCount` is **capped at 99**. Render `99+` at the cap.

| Verb | Route | Body | Returns | Notes |
|---|---|---|---|---|
| GET | `groups/{groupId}/announcements?limit={int}&before={utcIso}&beforeId={guid}` | — | `AnnouncementFeedResponse` | Player feed, newest first. `limit` defaults 20, max 50. `before`/`beforeId` are `NextCursorUtc`/`NextCursorId` from the previous page — send both or neither. |
| POST | `groups/{groupId}/announcements/read` | — | `MarkAnnouncementsReadResponse` | Marks everything in the group read. Safe to repeat. |
| GET | `players/me/announcements/unread-count` | — | `UnreadAnnouncementsResponse` | Bell badge across all the player's groups. |
| POST | `groups/{groupId}/announcements` | `PostAnnouncementRequest` | `SentAnnouncementDto` | **Admin only.** Requires an `Idempotency-Key` header (see below). |
| GET | `players/me/announcements/sent?limit={int}` | — | `SentAnnouncementsResponse` | **Admin only.** "Recently sent" with read receipts, default 10, max 25. |

Error semantics, already normalised by `ApiExceptionHandler` into the existing exception shape:
`400` validation (empty body / >500 chars), `403` caller is not an admin of that group, `404`
unknown group or caller not a member, `409` idempotency-key reuse with a different body.

**Idempotency-Key**: `POST groups/{groupId}/announcements` requires the header. Generate a
`Guid.NewGuid().ToString("N")` per *composed message* — hold it in the page model and reuse it
across retries of the same message so a retry after a timeout cannot double-post; mint a fresh one
only when the composer is reset. Match how existing mutating clients set this header (grep
`Idempotency-Key` under `SouthBaySoccer/Services/`).

The audience picker uses the **existing** `IGroupsClient.GetMyGroupsAsync()` —
`GroupChatDto.MemberCount` is the recipient count you show on the chips and the CTA. Do not invent a
second source for it; the backend snapshots that same number when it sends.

## What to build

### Controls (new, in `SouthBaySoccer/Controls/`)

`_specs/client-ui.md` §11 names four controls that do not exist yet. Build them as real reusable
controls following the existing `Controls/*.xaml(.cs)` pattern (bindable properties, no page-local
copies), and add each to `DesignSystemPage.xaml` like the existing ones:

- **`AnnouncementCard`** — the single most important piece: **the admin preview and the player feed
  row are the same control**. Bindable: `AuthorName`, `GroupName`, `TimeLabel`, `Body`, `IsUnread`,
  `ContextChipText`/`ContextChipGlyph`, `ContextCommand`/`ContextText`. The footer row renders only
  when context is supplied. Unread state = soft green border + dot (see wireframe `.ann.unread`).
- **`GroupChoiceRow`** — single-select row of group cards (name + member count). Semantically a
  radio group: set `SemanticProperties` so screen readers announce selection state.
- **`ToggleRow`** — icon + title + subtitle + switch, exposed as a switch to accessibility.
- **`PushPreview`** — dark OS-notification card; body clamped to 2 lines (`MaxLines`/`LineBreakMode`).

Reuse existing tokens and styles only (`BrandColors.xaml`, `BrandTokens.xaml`, `BrandStyles.xaml` —
`NoticeSurface`, `IconTileSurface`, `MetadataChip`, `HeroCardSurface` are styles, not controls).
**Do not add new colour literals** — if you need a value that is not a token, add the token to
`BrandColors.xaml`/`BrandTokens.xaml` and use it by key.

### Client service layer (`SouthBaySoccer/Services/Clients/`)

- `IAnnouncementsClient` + `ApiAnnouncementsClient` — mirror `IGroupsClient`/`ApiGroupsClient`
  exactly (typed `HttpClient`, `EnsureSuccessStatusCode`, `ReadFromJsonAsync`, `?? []`).
- Register it with the same three-handler pipeline
  (`CorrelationIdHandler` → `AuthenticationHandler` → `ApiExceptionHandler`).
- `CachedAnnouncementsClient` decorator following `CachedReadClients.cs`: cache the **unread count**
  (short TTL, ~60s) and the feed's first page; **invalidate the `announcements:` prefix on post and
  on mark-read**, and make sure sign-out clears it (the existing `ClientResponseCache.Clear()`
  generation guard already covers the in-flight case — verify you inherit it).
- `SeedAnnouncementsClient` + fixtures in `SouthBaySoccer/SeedData/`, matching the wireframe copy so
  Debug builds render the screens without a backend. Add it to the `ClientDataSource.Seed` branch.
  Remember `SeedData` is `<Compile Remove>`d in Release — the Api branch must not reference it.

### Pages and page models

Follow the house style precisely — study `SessionsHomePageModel.cs` and `LinkGroupPage.xaml` first:

- Page models are `ObservableObject` with primary constructors, **no MAUI types**;
  `IQueryAttributable` and any `Shell` use go in a separate `*.Navigation.cs` partial.
- `[ObservableProperty]` / `[RelayCommand]`; `[RelayCommand(AllowConcurrentExecutions = false)]` on
  the `Appearing` command; `CanExecute` + `NotifyCanExecuteChanged` for the disabled-send state.
- Navigation goes through a new `IAnnouncementsNavigator` abstraction (see
  `Services/Navigation/ShellSessionsNavigator.cs`) so page models stay unit-testable.
- Error convention, copied exactly from `SessionsHomePageModel` (~lines 194-205): rethrow
  `OperationCanceledException` when it is our own token, `HttpRequestException` → `ViewState.Offline`,
  everything else → `ViewState.Error`, reset `IsRefreshing` in `finally`, and expose the
  title/message strings as constants so tests can assert on them.
- Wrap both screens in `StateView` for Loading / Empty / Error / Offline. The player feed's empty
  state is "You're all caught up."
- Register routes in `MauiProgram.cs` with `AddTransientWithShellRoute<TPage, TPageModel>("…")` —
  `announcements` and `admin-broadcast`. Both are pushed pages, not tabs.

**Composer page model behaviour** (`AdminBroadcastPageModel`):
- Loads the admin's groups; preselects the primary group; selecting a group updates preview text,
  push-preview title, and the CTA label ("Broadcast to {n} members") together — one state change,
  not three independent ones.
- Live character count against a 500 max; send disabled while the body is empty or whitespace, and
  while a send is in flight.
- Validate client-side *and* surface the server's 400 message; the inline error must be associated
  with the editor for accessibility.
- On success: lock the composer (audience, editor, push toggle, CTA), show the sent state, and
  prepend the new item to "Recently sent" without a refetch.

**Feed page model behaviour** (`AnnouncementsPageModel`):
- All / Unread filter; the Unread tab label carries the count.
- Day grouping (Today / Earlier) computed from `SentAtUtc` — convert to local time **only here, at
  the UI boundary**; the whole pipeline below is UTC.
- `MarkAllRead` updates the cards, the tab count, and the bell badge together, and is a no-op (no
  request) when the unread count is already zero.
- Read counts are **admin-only** — the player feed must never render "seen by N of M".

### The Sessions bell

`Pages/SessionsHomePage.xaml:64-75` currently has a **dead, non-interactive** bell icon. Wire it:
tap → navigate to `announcements`; unread dot bound to the unread count; and give it
`SemanticProperties.Description` that includes the count (e.g. "Notifications, 3 unread") — the
visual dot alone is not accessible. Fetch the count as part of the existing Sessions dashboard load;
**do not add a second network round-trip on that screen** — `ScreenRequestCountTests` asserts
request counts per screen and will fail you.

## Performance requirements — these are the point of the task

1. **No N+1 and no request-per-item.** One feed request per page, one unread-count call folded into
   the existing dashboard load.
2. **Virtualize the feed.** Use `CollectionView` with `LinearItemsLayout`, not
   `BindableLayout` inside a `VerticalStackLayout` (which the rest of the app uses and which
   materialises every row). Set `ItemSizingStrategy`, `ItemsUpdatingScrollMode="KeepScrollOffset"`,
   and `RemainingItemsThreshold` for incremental paging via `NextCursorUtc`.
3. **Compiled bindings everywhere** — `x:DataType` on every `DataTemplate` and on the page root. An
   un-typed `DataTemplate` in this feature is a defect.
4. **Filtering must not refetch.** All/Unread is a client-side view over the loaded page.
5. Cache the unread count so tab-switching back to Sessions does not re-hit the network within the
   TTL.
6. Do not block the UI thread: no `.Result`/`.Wait()`, and no `async void` outside event handlers.

## Correctness and safety requirements

- **UTC everywhere except the render boundary.** No `DateTime.Now` — the client uses the injected
  `TimeProvider` (see how `SessionsHomePageModel` takes it), which is also what makes the tests
  deterministic.
- **Group scoping is a security boundary, not a filter.** Every call is scoped to one `groupId`;
  never build a screen that shows announcements from a group the player is not linked to, and never
  let the composer post to a group missing from `GetMyGroupsAsync()`.
- Cancellation: every async command takes and honours a `CancellationToken`.
- No secrets, phone numbers, or message bodies in logs.

## Do not introduce regressions

The client test project **does not reference the MAUI csproj** — it `<Compile Include>`s individual
files by link (`tests/SouthBaySoccer.Client.Tests/SouthBaySoccer.Client.Tests.csproj:31-93`). Every
new page model and service you want covered needs a line added there, and XAML files asserted by
`BrandUiLibraryTests`/`SessionScreensXamlTests` are copied as content — check whether those
structural tests need a new entry for your XAML.

Existing tests you must keep green (they are the regression net):
`ScreenRequestCountTests`, `ApiPipelineTests`, `BrandUiLibraryTests`, `SessionScreensXamlTests`,
`SessionsHomePageModelTests`.

## Tests you must write

In `tests/SouthBaySoccer.Client.Tests/`, following the existing conventions (xUnit +
FluentAssertions + Moq, `Method_Scenario_ExpectedOutcome`, seed clients as real fakes for happy
paths, `Mock<T>(MockBehavior.Strict)` for navigators, a private `CreatePageModel(...)` factory with
an injected fake `TimeProvider`):

- Composer: group selection updates preview + count + CTA together; send disabled on empty/whitespace
  and while in flight; 500-char boundary; success locks the composer and prepends to Recently sent;
  the same idempotency key is reused across a retry of one message and a new key is minted after reset.
- Feed: unread filtering; Today/Earlier grouping across a local-midnight boundary (this is where a
  UTC bug shows up — write the test that would catch it); mark-all-read updates cards, tab count and
  badge and issues no request when already zero; paging appends rather than replaces.
- Error mapping: `HttpRequestException` → Offline, other failures → Error, cancellation rethrows.
- A `CountingHttpMessageHandler` test proving the feed screen issues exactly one request on load and
  that the unread count is served from cache on the second call.

## Definition of done

1. `dotnet build` on the solution (`SouthBaySoccer.slnx`) — **zero warnings**. For the client alone:
   `dotnet build .\SouthBaySoccer\SouthBaySoccer.csproj -f net10.0-windows10.0.19041.0`.
2. `dotnet test .\tests\SouthBaySoccer.Client.Tests\` — all green, including the pre-existing tests.
3. Both screens visually match `documentation/mobile-wireframes.html`.
4. `_specs/client-ui.md` §11 mapping table updated if any control name you shipped differs from what
   the spec predicted (edit the spec to match reality — the spec is a live document).
5. **Spin off a staff-level code reviewer** on completion. This repo ships a `maui-xaml-reviewer`
   subagent (`.claude/agents/maui-xaml-reviewer.md`) and a Codex code-review skill at
   `.agents/skills/source-command-code-review/SKILL.md`. Run the review over your diff, at staff
   engineer standard: correctness, virtualization and binding cost, accessibility, thread-safety of
   the cache decorator, control reuse vs page-local duplication, and regression risk to the existing
   Sessions screen. Fix what it finds, then report both the findings and the fixes.
6. In your final report, state explicitly: files added/changed, the build and test output you
   actually observed (not what you expect), anything you deliberately left out, and any place the
   frozen Contracts DTOs fought you.

## House rules that override your defaults

From `CLAUDE.md` / `AGENTS.md` at the repo root — read them, they are binding:

- **Simplicity first, minimal impact.** Touch only what the task needs. No opportunistic refactors of
  the existing Sessions screen beyond wiring the bell.
- **No laziness.** Root-cause fixes only; no workarounds deferred with a TODO.
- **Never mark work complete without proving it** — paste real build/test output.
- Start by skimming `.ai/memory/INDEX.md` and `.ai/lessons/INDEX.md`; read
  `.ai/memory/group-broadcast-notifications.md` in full, it encodes decisions you must not relitigate
  (notably: read counts are admin-facing only, and the admin preview and player row share one card).
- The brand is green/white Nigerian-flag (`skills/brand-design-kit`, primary `#008751`).
