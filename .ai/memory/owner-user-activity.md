# Owner user activity

The owner User activity feature uses a dedicated UserActivities table, not token counts or imported
player counts. See _specs/owner-user-activity.md. Initial token issuance writes one SignIn event in
the same SaveChanges as the refresh token. In-app registration changes the subject kind to SignUp;
refresh rotation never records activity. The unique session-family key protects save retries.

GET admin/user-activity is owner-only (IsSuperAdmin endpoint policy plus handler role check),
validated and paginated. It returns active players' names, UTC activity, retained recorded
first/last activity and sign-in counts, and current memberships. The MAUI screen is under Profile's
owner controls and displays local times. It excludes deleted profiles and never copies contact
details or token material into activity rows. No historical backfill or download inference.

Roll out the additive migration before new Functions. Collection works with existing installed
apps once the backend is deployed; accessing the new screen requires a new client build. First
recorded activity is not claimed to be a lifetime first login. Repeated explicit sign-ins count as
new sessions; failed network delivery after a committed issuance can still leave an activity row.
