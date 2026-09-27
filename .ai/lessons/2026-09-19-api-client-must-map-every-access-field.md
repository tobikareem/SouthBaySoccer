---
name: api-client-must-map-every-access-field
description: A server-side access flag with a permissive client default silently disappears if the API client mapper drops it; test the mapper against the production wire format
type: lesson
created: 2026-09-19
---

## What happened

GRP-1 added per-session group access (`GroupChatId`, `GroupName`, `MembershipStatus`, `CanJoin`)
to the server response and to the client DTOs, and `SessionDetailPageModel` gated RSVP on
`CanJoin`. But `ApiSessionsClient` never passed those fields from `SessionAdminResponse` into
`SessionDetailDto`/`SessionSummaryDto`. The DTO default is `CanJoin = true`, so every production
player saw an enabled RSVP on other groups' games. Seed data set the field directly, so every
seed-mode test and demo looked correct.

## Rule

- When a contract gains an access/permission field, grep every `new <Dto>(` mapper in
  `SouthBaySoccer/Services/Clients/Api*.cs` and pass it through.
- Permissive defaults (`CanJoin = true`) hide a dropped field. Add an Api-client test that feeds a
  PascalCase body (the Functions host's real casing) with the field set to the restrictive value.
