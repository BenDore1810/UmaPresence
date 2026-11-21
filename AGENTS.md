# UmaPresence development workflow

## Understand and plan

- Start with the current code, issue, PR checks, and release evidence. Keep existing behavior unless the task changes it.
- For a small fix, inspect, make the smallest safe change, and run the relevant checks. For a feature, architecture change, or release, write down the scope, dependencies, acceptance criteria, and rollback path first. Use GrillMe for consequential uncertainties when requested. Resolve facts from the code or primary sources before asking about preferences.
- Keep one agent responsible for the combined result. When parallel work is requested and independent, give each subagent a narrow scope and ask for concise findings with evidence. Use Taste for UI work when it will improve an observed interaction. Use the project's existing UI conventions.

## Build and audit

- Implement one coherent change at a time. Reuse existing code. Check failure handling, legacy settings, Hachimi and Standalone compatibility, and uninstall when the change touches them.
- Run targeted checks for each phase, then test the combined build. Refactor when there is a concrete benefit and repeat affected tests and the cumulative audit.
- Keep private game files, user settings, diagnostics, credentials, and Pisa assets out of public GitHub and release artifacts. Keep Pisa builds local. Use disposable fixtures for destructive tests.
- Ask before deleting user data, changing architecture or services with material cost or disruption, modifying a live game installation, or publishing when the user has not authorized publication.

## Release

- Distinguish code review, local checks, hosted CI, archived-package verification, live acceptance, and publication. Record each result with the commit or artifact hash it covered.
- Check GitHub 2FA before operating on release assets. Build public release artifacts from the exact merged commit. Audit the binary and source ZIPs, verify a fresh extraction, and keep the prerelease in draft until all applicable acceptance checks pass. Record a reason and evidence for disabled features if the release gate supports that status.
- Keep live checks pending until there is observable evidence. If a tested payload changes, rerun the affected acceptance checks. Verify uploaded hashes against the local release artifacts.
- At a handoff, record the last verified commit and package hashes, unresolved checks, and the exact next action. Keep progress reports focused on evidence and blockers.
