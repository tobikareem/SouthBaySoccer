# Full games must still accept waitlist requests

The session detail page disabled RSVP at capacity even though the backend accepts Going intent
and assigns Waitlisted atomically when the game is full. This prevented approved members from
joining the waitlist while the RSVP window remained open.

Keep membership, cancellation, and deadline checks as eligibility gates. Capacity determines the
action label and server-assigned outcome, not eligibility. After a successful mutation, reload the
confirmed session and roster rather than assuming a Going request produced a Going result.

Regression tests must cover a full game with no existing spot, successful waitlisting and
withdrawal, closed deadlines, membership restrictions, and failure to refresh after submission.
Seed-backed tests must model this same behavior rather than codify disabled full-game buttons.
