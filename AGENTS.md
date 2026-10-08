# Instructions for coding agents working in this repository

## Security hardening work

Security hardening of MonitorAgent follows a written plan. Before doing anything related to it:

1. Read `docs/security/SECURITY_PLAN.md` completely. Section 0 is the working protocol and applies to
   every run. If that file is not in the repository, stop and tell the owner — do not work from the
   run prompts alone, and do not reconstruct the plan from memory.
2. Read `docs/security/PROGRESS.md` to see what has already been done and what was left unverified.
3. Do only the run and the phase the owner asked for. Stop at the end of that phase.

Rules that are never overridden by a prompt:

- Never commit to `master`, never force-push, never rewrite history.
- Never print, log or commit a real secret. Refer to secrets by file and field name.
- Never contact the production licensing server or any customer system.
- Never report a test as passed unless you ran it and saw it pass. Use the status words defined in the
  plan (`DONE-VERIFIED`, `DONE-UNVERIFIED`, `BLOCKED`, `SKIPPED`) exactly.
- One plan step = one commit, message `sec(R<run>-S<step>): <what changed>`, with the step's entry added
  to `docs/security/PROGRESS.md` in the same commit. Every commit builds and passes the tests; steps that
  cannot be separated without breaking the build are combined in one commit that names all of them.
  Write the narrative parts of `PROGRESS.md` in Arabic.
- If the plan and the code disagree, or an owner input is missing, stop and report instead of guessing.

## General

- Build: `dotnet build MonitorAgent.sln -c Release`
- Test: `dotnet test MonitorAgent.sln`
- Do not rename product identifiers (service name, launchd label, package name, stored JSON property
  names) and do not redesign UI screens unless a task explicitly says so.
