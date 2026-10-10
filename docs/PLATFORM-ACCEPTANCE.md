# Preview platform acceptance

Automated native-host tests and archive checks are necessary but are **not user
testing**. A human tester must record OS/architecture, release tag, installed
artifact SHA256, steps, result and any screenshot/diagnostic. Do not mark this
complete merely because a process starts.

On Windows, Intel Mac, Apple Silicon Mac and Linux, use a fresh document/copy:

1. Launch the exact downloaded package, confirm Merpie name/icon and no runtime
   installation requirement. Stop at any OS trust/authentication warning.
2. Create Flowchart, ER, Class and Sequence documents. Add elements/relations;
   select, edit, cancel, Undo/Redo, save, close and reopen each.
3. Shift-select nodes/groups, marquee, align/distribute, drag a nested and empty
   group; Escape must restore the prior geometry without history.
4. Import a fixed pastel/white-group diagram. Switch light/dark chrome repeatedly;
   group/caption/edge/node text must remain readable and source/coordinates unchanged.
5. Export document/light/dark appearance with filled and transparent outer canvas.
   Composite transparent SVGs on the matching light/dark destination. Authored
   fills remain, and static ink must not be claimed to recolor with a host page.
6. Try the real-Mermaid pane where its OS web engine exists; otherwise verify the
   explicit unavailable message and normal native editing. Check keyboard focus,
   menus, high-DPI resizing and the platform screen reader.

Pending gates: human reports and normal signed/notarized installation experience.
CI can prepare and verify fixtures, repeated theme switches, pixel/contrast/alpha
checks and package integrity, but cannot supply human usability approval.

## Signing gate

`scripts/check-signing-readiness.ps1` reports availability, not completion.
No valid local Windows code-signing certificate or configured signing CI
credentials were available during preview preparation.

Windows distribution needs an authorized code-signing certificate/service and
timestamping. macOS needs the user's Developer ID certificate, authorized secure
keychain setup and notarization credentials on a Mac runner. Do not commit key
material or put it in chat. Provision credentials in the chosen secure store
after an explicit user decision; acquiring credentials/paid services is not
automatically authorized by this checklist.

Actual signing must verify the entire app/package (including nested Mac binary
signatures) and staple/check notarization. Until that succeeds, release notes
must retain the unsigned/unnotarized limitation. Do not bypass trust warnings.
