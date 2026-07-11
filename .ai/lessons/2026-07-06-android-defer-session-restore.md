# Android: defer session restore until after first paint

When Android launches the MAUI app with stored auth tokens, do not start token refresh and Shell
replacement from `Window.Created`. That runs before the first screen has painted and can combine with
MAUI debug startup work to trigger an Android "Application Not Responding" dialog.

Prefer showing the lightweight startup page first, then start restore from the page lifecycle after
the page is loaded and a short dispatcher delay has elapsed. This lets Android draw a responsive
window before any token refresh, API client setup, or authenticated Shell navigation starts.
