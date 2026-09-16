# Supplementary view-model assignment needs its own failure boundary

## Problem

Game Day treated recent history as optional and caught failures from its HTTP request, but projected
and assigned the result inside the main page-load `try`. A synchronous exception raised by a
platform binding/control during the property notification replaced an already-valid Today context
with the generic Game Day error screen.

## Rule

When supplementary data must not break primary content, isolate the complete operation: request,
projection, assignment, and property-change callbacks. Do not protect only the network call.
Log only a sanitized failure type and leave the successfully loaded primary state intact.
