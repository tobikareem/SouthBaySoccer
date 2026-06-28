# M4 Profiles And Waivers

M4 backend profile identity is anchored on `PlayerProfile`, not `ApplicationIdentityUser`.
Guests are `PlayerProfile` records with `IsGuest = true`, `Role = Guest`, and no identity user id.
Emergency-contact phone numbers are private and persisted only as hash plus masked display value.

Current waiver eligibility is based on the single current published `WaiverDocument` and an
acceptance for that exact version. New waiver versions require a new `WaiverAcceptance`; RSVP work
should call the waiver eligibility use case/repository instead of checking any historical waiver.
