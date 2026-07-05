# Pickup Pal Phone Sign-In

Pickup Pal is the source of truth for SouthBaySoccer user identity/profile data. Sign-in now uses
SouthBaySoccer Functions to look up the submitted phone number through Pickup Pal
`GET /api/users/phone/{digits}` and, when found, syncs a local `ApplicationIdentityUser` plus
`PlayerProfile` before issuing SouthBaySoccer JWT access and rotating refresh tokens.

Persist Pickup Pal email on `ApplicationIdentityUser.Email` for future notifications. Do not store
raw phone numbers locally; store hashes and masked phone display only. SouthBaySoccer roles remain
local (`PlayerProfile.Role`) and are not overwritten by Pickup Pal profile sync.
Pickup Pal preferred soccer positions come from the full user profile endpoint `GET /api/users/{id}` at
`userInfo.sportsInfo[]` where `sport == "SOCCER"` and `isActive == true`; sync them into
`PlayerProfile.PreferredPosition` as a comma-separated string such as `st, rw, cm`.
