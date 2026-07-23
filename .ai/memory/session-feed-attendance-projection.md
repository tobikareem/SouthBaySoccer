# Session feed attendance projection

- `GET /api/sessions`, Sessions home, and See schedule use one persisted attendance projection that
  combines local RSVP/waitlist rows with imported Pickup Pal participants.
- Identity is de-duplicated across Going and Waitlist, not independently inside each list. Local
  state wins over imported state; Going wins over Waitlist within one source.
- The serializable RSVP capacity decision must use this same combined projection. Counting only
  local `RsvpResponses` can admit players beyond capacity when Pickup Pal already fills the game.
- `CanJoinWaitlist` is server-derived and true only for a published, full session before the RSVP
  deadline when the caller is neither Going nor waitlisted.
