<!--
Nothing here is mandatory. It is a prompt, not a form — delete what doesn't apply.
-->

## What this changes

<!-- And, if it's a fix, what failure it prevents. A change described by its
     mechanism is harder to review than one described by the bug it kills. -->

## How it was verified

<!-- What you ran, and what it said. If you couldn't run something, say so —
     an honest gap is worth more than an implied guarantee.

     dotnet test -c Release
     cd oracle && node run-oracle.mjs
-->

## Anything reviewers should look at closely

<!-- Judgement calls, things you were unsure about, alternatives you rejected. -->

---

- [ ] `dotnet test -c Release` passes with no new warnings
- [ ] Browser gates run, if this touches rendering, the format, or the plugin
- [ ] Docs updated, if this changes something they describe
- [ ] If this changes the stored format, SPEC.md says so
