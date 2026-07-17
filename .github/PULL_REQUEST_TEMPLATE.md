## User outcome

What problem does this solve, and what should be different for a Worklings user?

## Approach

Summarize the implementation and the important tradeoffs. Include explicit non-goals when they help keep the change reviewable.

## Risk review

- [ ] No save-format or persistence behavior changes
- [ ] No new data collection, system permission, or privacy-boundary changes
- [ ] No accessibility or Reduce Motion impact
- [ ] No new dependency

Explain every unchecked item, including compatibility or migration behavior.

## Verification

- [ ] `swift build`
- [ ] `swift run CompanionCoreChecks`
- [ ] New or updated checks cover changed core behavior
- [ ] Manual verification notes are included for AppKit or SwiftUI behavior
- [ ] `docs/changelog.md` is updated
- [ ] Relevant documentation is updated

List manual test cases and their results:

## Visual evidence

Add screenshots or a short recording for visible changes. Write "Not applicable" for non-visual changes.
