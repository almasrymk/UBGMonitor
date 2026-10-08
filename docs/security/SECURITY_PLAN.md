# MonitorAgent — Security Hardening Plan (for Codex)

This file is the single source of truth for the security-hardening work on this repository.
It is executed **one run at a time**. Each run is started by the owner with a short prompt from
`docs/security/RUN_PROMPTS.md`. You never start a run on your own and you never continue into the next run.

Line numbers below refer to `master` at commit `a12ad6b`. They will drift as you edit; locate code by
file and symbol, and use the line numbers only as a starting point.

---

## 0. How you work (read this before every run)

### 0.1 What this project is

MonitorAgent is a monitoring agent installed on customers' servers and PCs:

- `src/MonitorAgent.Service` — background service (Windows Service / systemd / launchd). Collects hardware,
  OS, network, process, database and website health, and exposes an HTTP API. Runs as LocalSystem / root.
- `src/MonitorAgent.Desktop` — Avalonia desktop app that shows the data and edits the service's settings
  through that API. Runs as the signed-in user.
- `src/MonitorAgent.Shared` — DTOs, routes, `SecretProtector`.
- `tests/MonitorAgent.Tests` — xUnit tests.
- `scripts/` — install / uninstall / packaging for Windows, Linux, macOS. `tools/Packager` builds tar.gz / deb.

The owner is the product's developer. He reads your output to follow what you did, step by step. He tests on
real Windows / Linux / macOS machines after each run. Your job is to make that easy.

### 0.2 The goal

Make the agent safe to install on customer machines **without breaking what already works**: every screen,
chart, report, monitor type, licensing flow and upgrade path that works today must still work, unless a
step below explicitly changes it for a security reason.

### 0.3 Execution protocol

Every run has two phases.

**Phase A — plan (no product code changes).**
1. Read this file, `docs/security/PROGRESS.md`, and the code the run touches.
2. For runs marked `PoC required`, build the smallest throwaway proof of concept that answers the open
   technical question. Keep it out of the product code (a scratch folder you delete, or a test).
3. Append a section `## Run N — خطة التنفيذ` to `PROGRESS.md` containing: the steps you will do in order
   (use the step IDs from this file, add sub-steps where needed), the files each step will touch, the tests
   each step will add, anything in this plan that does not match the code you found, and any question for
   the owner.
4. Commit only `PROGRESS.md` with message `docs(security): run N plan`.
5. **Stop.** Do not write product code until the owner starts Phase B.

**Phase B — implementation.**
1. Work through the approved steps in order. One step = one commit. Commit message format:
   `sec(R<run>-S<step>): <what changed>` — for example `sec(R2-S07): return sanitized settings DTO`.
   Every commit must build and pass the tests. When two or more steps cannot be separated without
   leaving the build or the tests broken in between, implement them in one commit, name every step in
   the message (`sec(R2-S04,R2-S06): ...`), and say in `PROGRESS.md` why they were combined. Each step
   still gets its own `PROGRESS.md` entry.
2. After each step, before the next one:
   - build the solution;
   - run the tests;
   - append the step's entry to `PROGRESS.md` (format in section 0.5) and include it in the same commit.
3. If a step cannot be completed, record why, mark it `BLOCKED` or `DONE-UNVERIFIED`, and continue with
   the next step only if it does not depend on the blocked one. Otherwise stop.
4. At the end of the run: fill the run summary in `PROGRESS.md`, open a pull request into
   `security/hardening`, paste the run summary and the owner's verification checklist into the PR
   description, and **stop**. Do not begin the next run.

### 0.4 Stop conditions

Stop and report (do not guess, do not work around) when:

- An owner input in section 2 that the step needs is still `[OWNER: ...]`.
- A PoC shows the design in this plan does not work on a platform. Describe what failed and propose
  alternatives.
- A test fails and the fix is outside the current step's scope.
- A step would require changing files or behaviour this plan does not mention.
- You find a secret, credential or personal data that this plan does not already know about. Report the
  file and field name, never the value.

### 0.5 What you write in PROGRESS.md

`PROGRESS.md` is the owner's window into your work. Write the narrative parts **in Arabic** (keep code,
file names, commands and technical terms in English). One entry per step:

```
### R2-S07 — <عنوان الخطوة>
- الحالة: DONE-VERIFIED | DONE-UNVERIFIED | BLOCKED | SKIPPED
- ماذا تغيّر ولماذا: (جملتان أو ثلاث)
- الملفات: (قائمة)
- الأوامر التي نُفذت ونتيجتها الفعلية: (الأمر + ناجح/فاشل + عدد الاختبارات)
- اختبار الأمان المضاف: (اسم الاختبار وماذا يثبت)
- اختبار عدم الانكسار المضاف: (اسم الاختبار وماذا يثبت)
- ما لم يتم التحقق منه ولماذا: (مثال: لم يُختبر على Windows لأن البيئة Linux)
- اختلاف عن الخطة: (إن وجد)
```

Status meanings — use them exactly:

- `DONE-VERIFIED` — implemented, and the tests that prove it were executed in this environment and passed.
- `DONE-UNVERIFIED` — implemented, but the proof needs a platform or resource you do not have. Say which.
- `BLOCKED` — not implemented; name the missing input or the failed assumption.
- `SKIPPED` — not applicable; say why.

Never write that a test passed unless you ran it and saw it pass. Never mark a Windows-only or macOS-only
behaviour `DONE-VERIFIED` from a Linux environment.

### 0.6 Rules that apply to every run

1. Branches: integration branch is `security/hardening` (created in Run 0 from
   `security/group1-sanitize-distribution`). Work on `security/run-<N>-<short-name>` branched from
   `security/hardening`. Never commit to `master`. Never force-push. Never rewrite history.
   If your environment forces its own branch name, keep it, but base it on `security/hardening` and target
   the PR there.
2. Never print, log, commit or paste a real secret — including the licensing secret that exists in old
   commits. Refer to secrets by file and field name only.
3. Do not contact the production licensing server or any customer system. Licensing tests use fakes
   (`FakeGateway` in `tests/MonitorAgent.Tests/LicensingTests.cs` is the existing pattern).
4. Do not rotate credentials, change repository visibility, publish packages, or deploy. Those are owner
   actions; list them in `docs/security/OWNER_ACTIONS.md`.
5. No new paid dependencies. Prefer what ships in .NET 8 / ASP.NET Core. Any new NuGet package must be
   named and justified in the Phase A plan.
6. Do not disable or bypass certificate validation, Windows Defender, Gatekeeper, SELinux, AppArmor or a
   firewall to make something work. Certificate **pinning** (comparing a fingerprint the user approved) is
   allowed and is not a bypass.
7. Never fall back to a weaker security mode automatically after a failure. A weaker mode is only ever an
   explicit, stored, visible choice.
8. Do not rename product identifiers: service name `MonitorAgent`, launchd label `com.ubg.monitoragent`,
   deb package name, folder names, JSON property names of stored settings. Renames break upgrades and are
   out of scope.
9. Do not redesign screens. UI changes are limited to what a step needs (a field, a warning, a disabled
   button, a dialog), following the existing theme and controls.
10. Do not delete or weaken an existing test to make the build pass. If a test encodes the old insecure
    behaviour, replace it with one that asserts the new behaviour and say so in `PROGRESS.md`.
11. Every security fix gets two tests: one proving the weakness is closed, one proving the original
    function still works.
12. Existing installations must keep their settings, monitor points, stored database passwords, license
    and history after upgrading. Migrations are idempotent and back up what they change.
13. Do not implement anything from section 13 (out of scope).

---

## 1. Current state of the repository

- `master` @ `a12ad6b` — the reviewed code.
- `security/group1-sanitize-distribution` @ `e4d30c1` — two commits already done:
  - `3529dd7` emptied `LicensingClient:ClientId/ClientSecret` and removed the whole `Setting` section
    (personal monitor points, machine name, database login) from `src/MonitorAgent.Service/appsettings.json`.
  - `e4d30c1` untracked the 810 files under `artifacts/`.
- Not yet done on that branch (handled in Run 1): `Routing` defaults still point at
  `http://madkhal.local:8080` and `https://api.central.local`; sample monitor points now leak into a
  clean install (finding F-24); no secret scanning.
- There is no CI (`.github/` does not exist).
- `tests/MonitorAgent.Tests/MonitorAgent.Tests.csproj` targets `net8.0-windows`, so the tests cannot run
  on Linux or macOS today (handled in Run 0).

### 1.1 Urgent owner action — independent of every run

The licensing client secret was published in a public repository and must be treated as compromised.
Removing it from files and packages does not undo that. **The owner revokes it on the licensing platform
and issues a replacement now, before Run 0 and without waiting for any code change**, and reviews the
platform's logs for use of the old credential. This is not something you can do; your part is that no
run ever puts a shared secret back into the product (Run 1, Run 5). Until the owner records the
revocation in `docs/security/OWNER_ACTIONS.md`, finding F-01 stays `Pending` in every report.

---

## 2. Owner inputs and decisions

Items marked `[OWNER: ...]` must be filled in by the owner before the run that needs them. Items with a
default are decided; follow them unless the owner edits this section.

| ID | Question | Value |
|----|----------|-------|
| D1 | Does the licensing platform accept the device endpoints (`activate`, `validate`, `heartbeat`, `deactivate`) **without** a client-credentials token? | `[OWNER: YES / NO]` |
| D2 | Production base URL of the licensing platform (stable domain owned by the owner). | `[OWNER: https://...]` |
| D3 | Public signing key(s) of the licensing platform as JWK (public parts only: `kty`, `crv`, `kid`, `x`, `y`). Must include the key that signs tokens today, plus a spare if one exists. | `[OWNER: paste JWK set]` |
| D4 | Local roles model. | **Default:** OS groups. Windows local groups `MonitorAgent Admins` and `MonitorAgent Viewers`; Linux/macOS groups `monitoragent-admin` and `monitoragent`. Details in Run 2. |
| D5 | What a remote caller (another computer) may do. | **Default:** Viewer only. Remote administration is off unless an Administrator enables it locally and creates a separate admin key. |
| D6 | TLS certificate for remote access. | **Default:** the service generates a self-signed certificate per installation; the Desktop app pins its SHA-256 fingerprint after the user confirms it on first connect. A customer-supplied PFX is supported as an option. |
| D7 | `Routing` defaults. | **Default:** `MadkhalServerUrl` and `CentralApiUrl` are empty in the shipped product; the Madkhal monitor and the config puller do nothing while their URL is empty. |
| D8 | Copying a stored database password to the clipboard when opening a database tool. | **Default:** kept, but only as an explicit Administrator action with a warning. Never automatic. |
| D9 | Are there installations at customers today that must be upgraded? | **Default:** assume yes — every run keeps the upgrade path working. |
| D10 | Where Codex runs. | `[OWNER: Windows locally / Linux cloud sandbox]` — decides what you can mark `DONE-VERIFIED`. |

Rules for the `[OWNER]` items:

- D1 = NO → in Run 5, stop at step R5-S04 and report; do not invent a provisioning scheme, and do not put
  a secret back into the product.
- D2 or D3 missing → Run 5 steps that need them are `BLOCKED`. Do not pin a key you fetched yourself from
  the network, and do not invent a domain.

---

## 3. Findings

Severity: C = critical, H = high, M = medium, L = low. "Run" is where it is fixed.

| ID | Sev | Finding | Where (master @ a12ad6b) | Run |
|----|-----|---------|--------------------------|-----|
| F-01 | C | Licensing `ClientSecret` committed in a public repo and shipped to every customer. Removed from the file on the security branch; still in history; must be revoked by the owner. | `src/MonitorAgent.Service/appsettings.json` `LicensingClient` | 1, 5 |
| F-02 | H | Shipped defaults contained the developer's personal settings (machine name, websites, a database point with `sa`, a DPAPI password blob). Removed on the security branch. | same file, `Setting` section | 1 |
| F-03 | M | Build outputs (DLL, EXE, PDB, config copies) tracked under `artifacts/` although ignored. Untracked on the security branch. | `artifacts/` | 1 |
| F-04 | H | Requests from the same machine are never authenticated: the key check is skipped for loopback. Any local user or process has full API access. | `LocalApi/LocalApiHost.cs` 96–111 | 2 |
| F-05 | H | When `RemoteAccessKey` is empty and the service listens beyond loopback, there is no authentication at all. The UI only warns. | `LocalApiHost.cs` 96–111; `Desktop/ViewModels/SettingsViewModel.cs` 256–257; `Desktop/Views/SettingsView.axaml` 297–309 | 2, 4 |
| F-06 | H | `GET /api/settings` returns the raw `Setting` section: `RemoteAccessKey` in clear text and the encrypted database password blobs. | `LocalApiHost.cs` 301; `Config/ServiceSettingsFile.cs` 19–32 | 2 |
| F-07 | H | `PUT /api/settings` lets any caller change the listen address and clear the key; the service then opens the firewall port itself. | `LocalApiHost.cs` 55, 167–184, 303–318; `Platform/Windows/WindowsFirewall.cs`, `Platform/Linux/LinuxFirewall.cs`, `Platform/Mac/MacFirewall.cs` | 2, 4 |
| F-08 | H | Password-decryption oracle. `POST /api/database/test` accepts a protected blob (`dpapi:` / `aes:`) and a server address from the caller, decrypts the blob and sends the password to that server. The same redirect works by saving a monitor point with the stored blob and a new server. | `LocalApiHost.cs` 275–281; `Monitoring/DatabaseMonitor.cs` 140–163, 203–231 | 2 |
| F-09 | H | No authorization model: one shared key grants read and write to everything. | all endpoints in `LocalApiHost.cs` 252–434 | 2 |
| F-10 | M | Browser reachability: body-less POST endpoints (`/api/license/deactivate`, `/api/license/refresh`, `/api/internet/speedtest`) can be triggered by any web page (no preflight); no `Host` validation (DNS rebinding); CORS allows any `localhost` / `127.0.0.1` origin on any port. | `LocalApiHost.cs` 81–92, 417–433 | 2 |
| F-11 | H | Remote access is plain HTTP only. The access key header and newly typed database passwords cross the network unencrypted. | `LocalApiHost.cs` 192–196; `Desktop/Services/AgentApiClient.cs` 40–43, 82–85; `Desktop/Views/DatabaseLoginWindow.axaml.cs` 153–163; `ServiceSettingsFile.cs` 54–67 | 4 |
| F-12 | M | Database monitor connections do not verify the server: SQL Server uses `Encrypt=Optional` + `TrustServerCertificate=true`; PostgreSQL and MySQL use driver defaults (encryption preferred, no certificate verification). | `DatabaseMonitor.cs` 206–252 | 4 |
| F-13 | L | Nothing discourages using `sa` / an owner account for a `SELECT 1` health check. | UI text, docs | 4 |
| F-14 | H | Windows: the service runs as LocalSystem from `C:\ProgramData\MonitorAgent\publish`. By default standard users can create files under `ProgramData` sub-folders, which can lead to code execution as SYSTEM (DLL planting; and `Program.cs` loads `appsettings.{Environment}.json` and Serilog settings from that folder). The service also writes its own settings, `Data\` and `Reports\` next to the executable. | `scripts/install-service.ps1` 11, 52, 62; `README.md` 51–68; `Platform/AgentPaths.cs` 23–29; `ServiceSettingsFile.cs` 17; `Config/LocalConfigCache.cs` 172; `Service/Program.cs` 21–26, 36 | 3 |
| F-15 | M | Windows: `appsettings.json` (access key, password blobs), `license.json`, logs and `reports.db` are readable by every local user. | default ACLs of the folders above | 3 |
| F-16 | M | Windows: secrets are protected with DPAPI `LocalMachine` scope and a constant entropy string, so any local user can decrypt them. The Desktop app itself decrypts stored database passwords, and protects its own access key with machine scope. | `Shared/Security/SecretProtector.cs` 16, 45, 72; `DatabaseLoginWindow.axaml.cs` 53–56; `Desktop/Services/MonitorPointLauncher.cs` 74–76; `Desktop/Services/AppSettingsStore.cs` 83–88 | 6 |
| F-17 | M | `RemoteAccessKey` is stored in clear text; only database passwords are protected on save. | `Config/GeneralRuntimeSettings.cs` 47; `ServiceSettingsFile.cs` 58–67 | 4 |
| F-18 | M | License signature trust is not anchored: signing keys are downloaded from the server and stored in `license.json` next to the token, then trusted on the next start. Whoever can write that file can supply their own key and a self-signed token. The comment claiming the file cannot extend a license is wrong. | `Licensing/LicenseService.cs` 82, 328–342, 481–496; `Licensing/LicenseStore.cs` 19; `Licensing/LicensePlatformClient.cs` 90–102 | 5 |
| F-19 | M | The licensing URL hard-coded in the binary is a temporary hosting domain. | `Licensing/LicensingOptions.cs` 13 | 5 |
| F-20 | M | The Desktop app executes what the settings say: it starts the program path of an Application monitor point with `UseShellExecute`, and opens any URL scheme of a point's address. Whoever can edit the service's settings chooses what runs on the viewer's desktop. `ShellOpen.Reveal` is also called with a path that may come from another machine. | `MonitorPointLauncher.cs` 21–62; `Desktop/Services/ShellOpen.cs`; `Desktop/ViewModels/ProcessListCardViewModel.cs` 137 | 2 |
| F-21 | M | On every settings save the service walks up the parent folders and also writes the settings into any `src/MonitorAgent.Service/appsettings.json` it finds — development convenience that is active in release builds. | `ServiceSettingsFile.cs` 40–50, 79–103 | 1 |
| F-22 | M | Shipped `Routing` defaults point at `http://madkhal.local:8080` and `https://api.central.local`. On a customer machine the Madkhal monitor raises a permanent "Madkhal unavailable" warning and the config puller polls a non-existent host every 5 minutes. The pulled config is not authenticated beyond TLS and may carry a `DatabaseConnectionString` that the service opens as a SQLite file. | `Options/RoutingOptions.cs` 7, 9; `Monitoring/MadkhalMonitor.cs` 56–103; `Config/ConfigPuller.cs` 58–78; `DatabaseMonitor.cs` 83–102, 262–277 | 1 |
| F-23 | M | Opening a database tool copies the stored password to the clipboard automatically. | `MonitorPointLauncher.cs` 133 | 6 |
| F-24 | M | Clean-install regression introduced by the cleanup commit: with no `Setting:MonitorPoints` in `appsettings.json`, `LocalConfigCache` falls back to three built-in sample device points (`main-point-a`, `regional-point-b`, `remote-point-c` at `192.0.2.1`), which are then pinged and reported. Found by reading the code — confirm with a test. | `LocalConfigCache.cs` 139–167, 169–219, 239–286 | 1 |
| F-25 | H | Linux tar.gz install: `install.sh` copies with `cp -a` as root, which preserves the owner of the extracted files. If the archive was extracted by a normal user, `/opt/monitoragent` stays owned by that user, who can then replace binaries that run as root. | `scripts/linux/install.sh` 28 | 3 |
| F-26 | L | Linux / macOS: state files other than the secret key (`license.json`, `config.json`, `Data/reports.db`, the temporary `appsettings.previous.json` written by `preinst` / the pkg `preinstall`) are created world-readable. | `LicenseStore.cs` 76–89; `LocalConfigCache.cs` 132–137; `scripts/linux/deb/preinst` 5–8; `scripts/macos/build-pkg.sh` 24–27 | 3, 6 |
| F-27 | L | Windows uninstall leaves the `MonitorAgent API` firewall rule in place (Linux and macOS remove theirs). | `scripts/uninstall-service.ps1`; `Platform/Windows/WindowsFirewall.cs` | 3 |
| F-28 | L | The Windows install script compiles from source on the target machine (`dotnet publish`), which is not how a customer install works. | `scripts/install-service.ps1` 52 | 3 |
| F-29 | L | macOS scripts remove the quarantine attribute and apply an ad-hoc signature instead of Developer ID signing + notarization. | `scripts/macos/install.sh` 32–33; `scripts/macos/make-app.sh` 16–19; `scripts/macos/build-pkg.sh` 17 | 7 (document only) |
| F-30 | L | No CI, no dependency vulnerability check, no secret scanning; tests cannot run off Windows. | repo | 0, 1, 7 |
| F-31 | L | The agent contacts third parties that the customer is not told about: public-IP lookups, speed-test hosts, connectivity checks. | `SystemInfo/NetworkService.cs` 33–35; `Shared/Monitoring/InternetSpeedTester.cs` 12–18; `Monitoring/InternetMonitor.cs` 328 | 7 (document only) |
| F-32 | — | The service runs as LocalSystem / root for everything. | `scripts/linux/monitoragent.service` 12; Windows service; launchd plist | deferred (section 12) |

Reviewed and found sound — keep as is, and keep covered by tests:

- All SQLite access is parameterized (`Reports/ReportStore.cs`).
- HTML report export encodes values (`Shared/Reports/ReportExporter.cs` 563).
- No HTTP client disables certificate validation.
- `Platform/Command.cs` is only called with constant arguments, integers, or values read from the OS; no
  API input reaches it.
- License tokens: ES256 only, product and device binding, clock-rollback detection
  (`Licensing/LicenseToken.cs`, `LicenseService.cs` 344–370, 444–447).
- Secrets are masked in the settings change log (`Config/SettingsChanges.cs` 10); product keys are logged
  by prefix only.
- Unix secret key file is created with mode 0600 (`SecretProtector.cs` 122–129); `appsettings.json` is
  `chmod 600` on Linux / macOS installs.
- Access-key comparison is constant-time (`LocalApiHost.cs` 101–103).

### 3.1 Dependencies between runs and release gates

- **Local authentication never rests on a secret stored in a file.** In Run 2 the identity of a local
  caller comes from the operating system's access check on the pipe / socket, not from a token or key
  the client reads from disk. If the Run 2 proof of concept forces a file-based credential on some
  platform, protecting that file (ACL / mode so that only the intended identities can read it) is part
  of the same step in Run 2 — it does not wait for Run 3.
- **Run 2 alone does not make stored secrets unreadable on Windows.** After Run 2 the API no longer hands
  out the remote key or the password blobs, but until Run 3 the settings file is still readable from
  disk by local users (F-15), and until Run 6 the blobs can still be decrypted by them (F-16). So F-06
  and F-08 are reported as `Mitigated`, not `Fixed`, until Run 3 is merged, and the Run 2 pull request
  says so in its description.
- **Run 2 keeps all settings-file access inside `ServiceSettingsFile`**, so that Run 3 only has to change
  where the file lives.
- Run 3 needs Run 2 (groups created by the installers). Run 4 needs Run 2 (roles, local admin endpoint).
  Run 6 needs Run 2 (sanitized DTOs) and Run 3 (folder permissions). Run 5 depends only on Run 1 and on
  D1–D3, and may be done earlier if the owner asks.
- **Release gates.** Nothing built from this work is installed at a customer until Runs 1–4 are merged
  and verified by the owner on real machines. A commercial release additionally requires Run 5, Run 6
  and Run 7. A legacy database point left in `Compatibility` mode (Run 4) is an accepted, visible,
  per-point risk — it is reported as `Mitigated`, never as `Fixed`.

---

## 4. Run 0 — Baseline and test harness

**Goal:** a branch to work on, a recorded baseline, tests that run on every OS, and CI that runs them on
Windows, Linux and macOS so platform behaviour is checked even when you work on one OS.
**Plan gate:** no (do Phase A and B in one go). **Needs:** nothing.

- **R0-S01 — Integration branch.** Create `security/hardening` from
  `security/group1-sanitize-distribution`. Confirm it contains commits `3529dd7` and `e4d30c1`. Confirm
  `docs/security/SECURITY_PLAN.md`, `PROGRESS.md`, `RUN_PROMPTS.md` and the root `AGENTS.md` are present
  (the owner adds them; if they are missing, stop).
- **R0-S02 — Baseline.** Run and record the real output of: `dotnet --info`, `dotnet restore
  MonitorAgent.sln`, `dotnet build MonitorAgent.sln -c Release`, `dotnet test MonitorAgent.sln`. Record
  the OS you are on. Failures here are baseline facts, not something to fix in this step.
- **R0-S03 — Cross-platform tests.** Change `tests/MonitorAgent.Tests` so it builds and runs on Windows,
  Linux and macOS (target `net8.0`; if something truly needs the Windows TFM, multi-target instead and say
  why). Add small attributes such as `WindowsFact` / `UnixFact` that skip a test with a clear reason on
  the wrong OS, without adding a package. All existing tests must still run on Windows.
- **R0-S04 — CI.** Add `.github/workflows/ci.yml`: on pull requests and on pushes to `security/**`, run
  restore, build (Release) and test on `windows-latest`, `ubuntu-latest`, `macos-latest`. Add
  `workflow_dispatch`. No secrets are needed by this workflow; do not add any.
- **R0-S05 — Progress file.** Record the baseline and the environment in `PROGRESS.md`.

**Acceptance:** `security/hardening` exists; tests run on the OS you are on; the workflow file is valid.
**Owner verifies:** the CI run is green on all three OSes (GitHub → Actions).

---

## 5. Run 1 — Secrets and distribution cleanup (finish)

**Goal:** nothing personal, secret or development-only ships; a clean install is truly clean; new secrets
cannot be committed unnoticed. **Plan gate:** no. **Needs:** D7.
**Fixes:** F-01 (code side), F-02, F-03, F-21, F-22, F-24, part of F-30.

- **R1-S01 — Audit the working tree.** Search source, scripts, tests, docs and config for credentials and
  personal data: prefixes `lcs_`, `lc_`, `dpapi:`, `aes:`; keys named `Password`, `Secret`, `Token`,
  `AccessKey`, `ConnectionString`; host names `mdkhl`, `fhrserp`, `madkhal`; machine name `MK`. Report
  file + field for each hit and whether it is a real value, a test fixture or code. Do not print values.
- **R1-S02 — Licensing client defaults.** Keep `LicensingClient:ClientId` and `ClientSecret` empty in the
  shipped file. `LicensingOptions.FromConfiguration` keeps reading them so a developer can supply them
  through user-secrets or environment variables. Add a test that reads the shipped
  `src/MonitorAgent.Service/appsettings.json` and fails if `ClientSecret` is non-empty or a `Setting`
  section with monitor points is present.
- **R1-S03 — Routing defaults (D7).** Set `MadkhalServerUrl` and `CentralApiUrl` to empty in
  `RoutingOptions` and `appsettings.json`.
  - `MadkhalMonitor`: while the URL is empty, do nothing — no HTTP call, no issue, no log line per cycle.
    When configured it behaves exactly as today.
  - `ConfigPuller`: while the URL is empty, do nothing. When configured, require an absolute `https` URL
    (allow `http` only for a loopback host); otherwise log one warning and stay idle.
  - Pulled config: ignore `DatabaseConnectionString` coming from the network; the local default is always
    used. Keep the other fields as today.
- **R1-S04 — Clean install has no monitor points (F-24).** First write a test that reproduces it: a
  `LocalConfigCache` with an `appsettings.json` that has no `Setting` section must return zero monitor
  points. Then fix it by removing the built-in sample points from `CreateDefault()`. Keep
  `UpgradeLegacySample` working for configs that still contain the old sample ids (it must not re-add
  samples). An existing `config.json` that contains only the three sample ids is treated as empty.
- **R1-S05 — Development copies (F-21).** Compile `DevelopmentCopies()` and its call only in `DEBUG`
  builds. A Release build writes exactly one settings file.
- **R1-S06 — Ignore rules.** Extend `.gitignore` so these can never be committed: `publish/`, `*.pfx`,
  `*.p12`, `*.snk`, `*.key`, `license.json`, `client.json`, `secret.key`, `appsettings.*.local.json`,
  `Data/`, `Reports/`. Confirm nothing currently tracked matches.
- **R1-S07 — Secret scanning.** Add a gitleaks job to CI that runs the gitleaks binary on the pull-request
  range (not the paid action), with a `.gitleaks.toml` that adds rules for the `lcs_` / `lc_` prefixes and
  for `dpapi:` / `aes:` blobs in JSON. The old commit that contains the leaked secret is allow-listed by
  commit SHA only; the secret value is never written anywhere. Add `scripts/check-release-secrets.ps1`
  (PowerShell 7, runs on all OSes) that fails if a publish folder's `appsettings.json` contains a
  non-empty secret field or a `Setting` section, and call it from CI after `dotnet publish`.
- **R1-S08 — Owner actions.** Create `docs/security/OWNER_ACTIONS.md` with, as a checklist: revoke and
  replace the leaked licensing client secret on the platform and review its usage; decide whether the
  repository stays public; optional history rewrite (explain that it does not un-leak the secret);
  enable GitHub secret scanning and push protection in the repository settings.

**Acceptance:** shipped config has no secret and no personal data; clean install shows zero monitor points
and raises no Madkhal / Central warnings; Release build writes one settings file; CI fails on a planted
fake secret (prove it in a throwaway commit that you then drop, and record the result).
**Owner verifies:** install on a clean VM → no monitor points, no warnings in "Messages & Issues".

---

## 6. Run 2 — Local API: transport, roles, redaction, database test, browser protection

**Goal:** nobody uses the API without an identity; reading and administering are separate; secrets never
leave the service; the stored-password oracle is closed; web pages cannot reach the API.
**Plan gate:** yes. **PoC required:** yes. **Needs:** D4, D5, D8.
**Fixes:** F-04, F-05 (local part), F-06, F-07 (local part), F-08, F-09, F-10, F-20.

### 6.1 Target design (validate in the PoC before building on it)

**Local transport.** The Desktop app on the same machine talks to the service over OS IPC instead of
`http://127.0.0.1:5050`:

- Windows: named pipes hosted by Kestrel (`ListenNamedPipe`, .NET 8).
- Linux / macOS: Unix domain sockets hosted by Kestrel (`ListenUnixSocket`).
- The Desktop keeps using `HttpClient`; only the connection changes. `AgentApiClient.CreateIpv4Handler`
  (481–503) already supplies a custom `SocketsHttpHandler.ConnectCallback` — extend that pattern to
  return a `NamedPipeClientStream` or a Unix-socket `NetworkStream`.

**Roles by endpoint, enforced by the OS.** The service exposes **two** local endpoints with identical
routes; which one a caller can open decides the role. The OS access check on the pipe / socket is the
authentication:

| Endpoint | Windows | Linux / macOS | Who may connect | Role |
|----------|---------|---------------|-----------------|------|
| viewer | pipe `MonitorAgent.Viewer` | `<run dir>/viewer.sock` | members of the viewers group, members of the admins group, SYSTEM / root, elevated Administrators | Viewer |
| admin | pipe `MonitorAgent.Admin` | `<run dir>/admin.sock` | members of the admins group, SYSTEM / root, elevated Administrators | Administrator |

- Windows groups (D4): local groups `MonitorAgent Admins` and `MonitorAgent Viewers`. A custom group is
  used on purpose: membership of the built-in Administrators group is filtered by UAC in a non-elevated
  process, a custom group is not, so the Desktop app does not need to run elevated. Pipe ACLs are set
  with `PipeSecurity`; resolve groups by SID at start-up. If a group does not exist, that endpoint is
  reachable by SYSTEM and elevated Administrators only, and the service logs one warning.
- Linux groups: `monitoragent-admin`, `monitoragent`. Sockets are owned `root:<group>` mode `0660`, in a
  run directory that is `root:root 0755` (`/run/monitoragent` via systemd `RuntimeDirectory=`; on macOS
  `/var/run/monitoragent`). Create the socket so it is never briefly world-accessible (restrictive umask
  before bind, then chown/chmod). Remove stale sockets at start.
- The request pipeline learns the role from the endpoint the connection arrived on (for example a
  connection-level item set in `ListenOptions.Use`, read through the connection items feature, or one
  `WebApplication` per endpoint). It never derives a role from an IP address or from a header the client
  sends on a local endpoint.
- The local loopback HTTP listener is removed. `http://127.0.0.1:5050` no longer exists for local use.
- Connecting to an endpoint proves the role, nothing more: every route still declares the role it needs.

**The client must also know it is talking to the real service.** An Administrator's Desktop app sends new
database passwords over this channel, so a fake endpoint created by another local user must not be
accepted:

- Windows: a standard user can create a pipe with the same name while the service is stopped (pipe
  squatting). The service creates its pipes as the first instance and fails loudly if the name is taken.
  Before sending any request, the Desktop checks that the pipe it connected to is owned by `SYSTEM` (or
  the built-in Administrators) and refuses to continue otherwise.
- Linux / macOS: the sockets live in a directory only root can write to; the Desktop checks that the
  directory and the socket are owned by root before using them.

**Remote transport** (another computer) stays TCP. In this run it only gets the minimum so the branch is
never worse than before: it never starts without a key, every request must carry the key (no loopback
exemption exists any more on TCP), and a remote caller is a **Viewer** (D5). HTTPS, hashed keys, remote
administration and the enable flow are Run 4.

**PoC questions to answer in Phase A:**
1. Kestrel named pipe with a custom `PipeSecurity` + `HttpClient` over `NamedPipeClientStream`: works for
   GET, PUT with a JSON body, and a long report response?
2. Kestrel Unix socket with `0660 root:group` + `HttpClient` over a Unix-socket stream: same checks.
3. Two endpoints in one host, with the role available to endpoint filters / middleware.
4. A non-elevated member of `MonitorAgent Admins` can open the admin pipe; a standard user cannot.
5. The client-side ownership check works: the Desktop accepts the service's pipe / socket and refuses one
   created by a standard user under the same name.
If you are not on Windows, questions 1, 4 and the Windows half of 5 cannot be verified by you: write the PoC as tests that run in
CI on `windows-latest`, and mark what remains for the owner.

### 6.2 Endpoint policy

Routes are in `Shared/Constants/ApiRoutes.cs`. Every route gets an explicit policy; a route without one
must fail a test.

| Role needed | Routes |
|-------------|--------|
| Viewer | `GET` status, snapshot, cpu, ram, network, disks/partitions, disks/physical, disks/activity, hardware, hardware/levels, hardware/levels/{n}, os, sensors, processes/top, apps/programs, apps/users, apps/services, monitorpoints, issues, notifications, internet, reports/subjects, reports/{type}, license, settings (sanitized) |
| Administrator | `PUT` settings; `POST` database/test; `POST` internet/speedtest; `POST` license/activate, license/deactivate, license/refresh; the new password-reveal action (Run 6) |

The existing "license required" behaviour (`OpenWithoutLicense`, `LocalApiHost.cs` 112–128, 234–238) is
kept as is and evaluated after the role check.

### 6.3 Steps

- **R2-S01 — Make the API testable.** Refactor `LocalApiHost` so the pipeline and the endpoint mapping can
  be hosted in tests (in-memory test server with fake services) without changing behaviour. Add
  characterization tests for the current responses of every route (status code and JSON shape). These
  tests are the regression net for the rest of the run.
- **R2-S02 — PoC** (Phase A) as described above; results go into `PROGRESS.md`.
- **R2-S03 — Roles and policies.** Add the role model and attach a policy to every route per 6.2. Add a
  test that enumerates all mapped endpoints and fails if any has no policy.
- **R2-S04 — Local IPC endpoints in the service.** Implement the two endpoints per platform, the ACLs /
  modes, group resolution, stale-socket cleanup, and remove the loopback HTTP listener. Keep the
  restart-on-settings-change loop (`ExecuteAsync`, 38–73) for the remote listener only.
- **R2-S05 — Groups in the install scripts.** Windows `install-service.ps1`: create the two local groups
  if missing and add the installing user to `MonitorAgent Admins` (tell the user a sign-out is needed for
  the membership to apply). Linux `install.sh` and `deb/postinst`: create the two groups, add
  `$SUDO_USER` to `monitoragent-admin` when present; add `RuntimeDirectory=monitoragent` to the unit.
  macOS `install.sh` and the pkg `postinstall`: same with `dseditgroup`. Uninstall scripts leave the
  groups unless `--purge` / `-Purge`.
- **R2-S06 — Desktop client.** `AgentApiClient`: "this computer" means IPC — try the admin endpoint, fall
  back to the viewer endpoint, expose the resulting role. A stored `ApiBaseUrl` of
  `http://127.0.0.1:<port>` or `http://localhost:<port>` in `client.json` is migrated to "this computer".
  UI: as Viewer, the Settings screens are read-only and administrative buttons are disabled with a short
  explanation; an access-denied error is shown as "you are not allowed — ask an administrator to add you
  to the MonitorAgent groups", clearly different from "the service is not running".
- **R2-S07 — Sanitized settings (F-06).** `GET settings` returns a DTO built field by field. It never
  contains a database password, a protected blob, a remote access key, a token or a private key. Each
  database login carries `hasPassword: true/false` instead; General carries `hasRemoteAccessKey`.
  `PUT settings` semantics:
  - database password absent or null → keep the stored one, **only if** engine, server, port and
    username are unchanged; if any of them changed and no new password is sent → `400` with a message
    asking to enter the password again (this closes the redirect half of F-08);
  - a new password arrives as plain text over the admin endpoint and is protected by the service;
  - a value that looks like a protected blob (`dpapi:` / `aes:` prefix) is rejected;
  - the remote access key is not settable through `PUT settings` any more (Run 4 adds dedicated
    actions); until then an existing key is preserved untouched and the key field in the Settings screen
    is disabled with a one-line note;
  - the whole body is validated before anything is written, and rejected with `400` and a field-level
    message otherwise: listen address is a valid IPv4 address of this computer, `127.0.0.1` or `0.0.0.0`;
    port 1–65535; intervals and retention within the ranges the UI already enforces; Website addresses
    are `http` / `https` or a bare host; string lengths and icon sizes are bounded; unknown top-level
    sections are dropped rather than stored.
  Desktop: `DatabaseLoginWindow` no longer decrypts anything — the password box is empty with "leave
  empty to keep the saved password"; remove the local-encryption branch that depends on
  `AgentApiClient.IsLocal`; remove `IsLocal`.
- **R2-S08 — Database test (F-08).** The request is one of: `{ monitorPointId }` — test the stored login
  of that point using credentials resolved inside the service; or `{ login }` with a plain-text password
  for a connection that is not saved yet; or `{ monitorPointId, login }` where the login omits the
  password and target fields equal the stored ones. Protected blobs from the client are rejected.
  Administrator only. The password is never logged and never echoed in the result message.
- **R2-S09 — Browser protection on what remains of HTTP (F-10).** Remove CORS entirely (there is no
  browser client). On the TCP listener: reject requests whose `Host` header is not one of the addresses /
  names the listener is bound to; require the key header on every request including body-less POSTs.
- **R2-S10 — Desktop launch hardening (F-20).**
  - `OpenAddress`: only `http` and `https` URLs are opened.
  - `OpenApplication`: only when the service is on this computer; the path must be absolute, on a local
    drive (no UNC), and exist. The first time a given path is launched, show a confirmation with the full
    path; remember confirmed paths per user.
  - `ShellOpen.Reveal` from the process list: only when the service is on this computer.
- **R2-S11 — Documentation.** Update `README.md`: remove the `curl http://127.0.0.1:5050` example and the
  CORS note; describe the two roles and the groups.

**Security tests (minimum):** standard user cannot open either endpoint (platform tests); viewer endpoint
gets `403` on every Administrator route; no route is unprotected; `GET settings` response contains no
`dpapi:` / `aes:` string, no key, for a config that has them; changing a point's server without a
password is refused; a protected blob sent to database test is refused; TCP request without key is `401`
even from loopback; `Host` mismatch is refused; body-less POST without key is `401`; a `file:` /
`javascript:` / custom-scheme address is not opened; a UNC application path is not started; an
out-of-range or malformed settings body is refused and nothing is written; the Desktop refuses a pipe /
socket that is not owned by the service account (platform tests).
**Regression tests (minimum):** every Viewer route returns the same shape as in R2-S01; saving settings
without touching a password keeps the stored password and the monitor keeps connecting; adding a new
database point with a password works; license activate / deactivate / refresh work as Administrator;
unlicensed behaviour unchanged.

**Acceptance:** as listed under the tests, plus: the Desktop app works end to end against a local service
as Administrator and as Viewer.
**Owner verifies (Windows):** sign in as a standard user not in either group → app shows "not allowed";
add the user to `MonitorAgent Viewers`, sign out/in → data visible, settings read-only; member of
`MonitorAgent Admins` → can edit settings without elevation; `curl http://127.0.0.1:5050/api/status` →
connection refused. Same three cases on Linux with the two groups.

---

## 7. Run 3 — Installation layout and file permissions

**Goal:** nothing a privileged service executes or reads as configuration can be written by a standard
user; sensitive files are not readable by standard users; upgrades keep all data.
**Plan gate:** yes. **Needs:** D9. **Fixes:** F-14, F-15, F-25, F-26 (install part), F-27, F-28.

- **R3-S01 — Path model.** Separate the read-only install folder from the mutable state folder in
  `AgentPaths` on every OS:
  - install folder (binaries, shipped `appsettings.json`): Windows `%ProgramFiles%\MonitorAgent\Service`,
    Linux `/opt/monitoragent`, macOS `/usr/local/monitoragent` — never written by the service;
  - state folder (already `StateFolder`): Windows `%ProgramData%\MonitorAgent`, Linux
    `/var/lib/monitoragent`, macOS `/Library/Application Support/MonitorAgent`;
  - the user-edited `Setting` section moves out of `appsettings.json` into `<state>/settings.json`
    (`ServiceSettingsFile`, `LocalConfigCache.TryReadMonitorPoints`); on Windows `Data\` and `Reports\`
    move from the executable's folder to `<state>\Data` and `<state>\Reports`.
  - `settings.json` is parsed as data only. It is never added to the host configuration and never reaches
    Serilog configuration. Only files in the install folder feed `IConfiguration`.
  - `MONITORAGENT_HOME` keeps working for tests and side-by-side copies.
- **R3-S02 — Migration.** On first start after upgrade, idempotently:
  - move the `Setting` section from the legacy location into `settings.json`
    (Windows: `%ProgramData%\MonitorAgent\publish\appsettings.json`; Linux / macOS: the legacy copy the
    installer saves in step S03/S04/S05, because the installer replaces the install folder first);
  - on Windows move `publish\Data` and `publish\Reports` into the state folder;
  - keep a timestamped backup of every file it moves or rewrites, with restrictive permissions;
  - never overwrite an existing `settings.json` with defaults or with older data.
  Unit-test the migration with temp folders: fresh install, upgrade with data, second run (no-op),
  interrupted run (re-run completes).
- **R3-S03 — Windows scripts.** `install-service.ps1`:
  - installs from a prebuilt folder given by `-Source` (customer path); building from source remains
    available for developers behind `-BuildFromSource` (F-28);
  - installs to `%ProgramFiles%\MonitorAgent\Service`, registers the service with a quoted `binPath`;
  - when given `-DesktopSource`, also installs the Desktop app to `%ProgramFiles%\MonitorAgent\Desktop`
    (today the script installs the service only);
  - sets the state folder ACL explicitly: inheritance disabled, `SYSTEM` full, `Administrators` full,
    nothing for `Users`;
  - after copying, verifies with `Get-Acl` that neither `Users` nor `Authenticated Users` nor `Everyone`
    can write to the install folder or read/write the state folder, and aborts with a clear message if so;
  - after the service has started once and migrated, removes the legacy
    `%ProgramData%\MonitorAgent\publish` binaries (keeping the backups from S02).
  `uninstall-service.ps1`: removes the `MonitorAgent API` firewall rule (F-27); keeps data unless
  `-Purge`.
- **R3-S04 — Linux scripts.** `install.sh`: after copying, `chown -R root:root /opt/monitoragent
  /opt/monitoragent-desktop` and remove group/other write (F-25); save the legacy `appsettings.json` into
  the state folder with mode `0600` for the migration instead of merging with Python. `deb/preinst`
  saves the legacy file with mode `0600` (F-26); `deb/postinst` no longer merges JSON. Unit file: add
  `StateDirectoryMode=0700`, `LogsDirectoryMode=0750`, `UMask=0077`, keep `User=root`. Do not add
  sandboxing directives that could block `dmidecode`, `smartctl`, sensors or the firewall tools.
- **R3-S05 — macOS scripts.** Same intent: files owned `root:wheel`, not group/other writable; state
  folder `0700`, logs `0750`; legacy settings saved `0600` for the migration; LaunchDaemon plist stays
  `root:wheel 0644`. Leave the ad-hoc signing lines as they are (F-29 is documented in Run 7).
- **R3-S06 — Verification scripts.** `scripts/verify-permissions.ps1` (Windows) and
  `scripts/verify-permissions.sh` (Linux / macOS) print PASS / FAIL for each protected path and exit
  non-zero on any FAIL.
- **R3-S07 — Documentation.** Update the install / uninstall sections of `README.md`.

**Security tests:** migration never produces a world-readable file (Unix test); `settings.json` content
cannot change logging configuration (test: a `Serilog` section inside `settings.json` is ignored).
**Regression tests:** upgrade fixtures keep every monitor point, password blob, general setting, license
and report history readable after migration.

**Acceptance:** verification scripts pass after a fresh install and after an upgrade.
**Owner verifies (Windows, as a standard user):** creating a file in `C:\Program Files\MonitorAgent\Service`
is denied; opening `C:\ProgramData\MonitorAgent` is denied; `icacls` on both folders shows no `Users`
write; service starts; all previous monitor points, history and license are present after upgrading a
machine that had the old layout. **Linux:** extract the tar.gz as a normal user, install with sudo,
`ls -ld /opt/monitoragent` shows `root root`; `verify-permissions.sh` passes.

---

## 8. Run 4 — Network security: remote access and database TLS

**Goal:** nothing sensitive crosses the network in clear text; remote access is an explicit, authenticated
choice; database connections verify the server by default without breaking existing points.
**Plan gate:** yes. **Needs:** D5, D6. **Fixes:** F-05, F-07, F-11, F-12, F-13, F-17.

### 8.1 Remote access to the agent

- **R4-S01 — HTTPS listener.** The TCP listener serves HTTPS only. The service creates a self-signed
  certificate (ECDSA P-256) on first enable and stores it in the state folder with the private key
  protected; an Administrator can instead point to a customer-supplied PFX. Plain HTTP is not served on
  any non-loopback address. Expose the certificate's SHA-256 fingerprint to Administrators (settings DTO
  and log line when the listener starts). Add an Administrator action to regenerate the certificate.
- **R4-S02 — Remote keys (F-17).** Replace `RemoteAccessKey` with keys the service generates (32 random
  bytes, base64url), returns once to the Administrator who asked, and stores only as SHA-256 hashes,
  compared in constant time:
  - a viewer key (required for remote access);
  - an optional admin key, only if "allow remote administration" is switched on locally (D5).
  Administrator actions (local admin endpoint only): create / rotate / revoke each key, switch remote
  administration on or off. Migration: an existing plain `RemoteAccessKey` becomes the viewer key (hash
  it, delete the plain value); remote administration starts off. State this in the migration notes.
- **R4-S03 — Enabling remote access (F-05, F-07).** Remote access can only be enabled over the local
  admin endpoint. The listener does not start unless a viewer key exists. The firewall port is opened
  only if the Administrator ticked an explicit "open the port in this computer's firewall" option, which
  is stored; it is still closed automatically when remote access is turned off. Replace the UI warning
  "without an access key…" by making the insecure state impossible.
- **R4-S04 — Abuse limits.** On the TCP listener: rate limiting with the built-in ASP.NET Core rate
  limiter (per remote address), a stricter limit and short lockout on failed authentication, a request
  body size limit that still allows settings with embedded icons, and request timeouts.
- **R4-S05 — Desktop remote client.** Remote addresses are `https://` only; typing a plain host name
  assumes HTTPS. On first connect show the server's SHA-256 fingerprint and ask the user to confirm it
  against the value shown on the server; store the pinned fingerprint with the address in `client.json`;
  later connections fail with a clear message if it changes. Validation = fingerprint equality; it is not
  a blanket "accept any certificate". If a customer certificate chains to a trusted root and matches the
  host name, accept it without pinning. Existing `http://<remote>` entries in `client.json` are kept as
  the address but require re-pairing. The Desktop's stored key uses per-user protection (Run 6 finishes
  this; do not regress it here).

### 8.2 Database TLS

- **R4-S06 — Per-point TLS mode (F-12).** Add a TLS mode to `DatabaseLogin`, stored per monitor point:
  - `Verify` — SQL Server: `Encrypt=Mandatory`, `TrustServerCertificate=false`; PostgreSQL (Npgsql):
    `SslMode=VerifyFull`; MySQL (MySqlConnector): `SslMode=VerifyFull`. Optional per-point "trusted root
    certificate file" for private CAs, if all three drivers support it cleanly.
  - `Compatibility` — exactly today's behaviour of each driver.
  - A stored point with **no** TLS mode (every existing point) is `Compatibility`, so nothing breaks on
    upgrade. Points created or edited to `Verify` in the UI are stored explicitly. New points default to
    `Verify` in the UI.
  - Never switch a point's mode automatically. When `Verify` fails because of the certificate, the
    message says so and names the two real options (fix the certificate / choose compatibility
    explicitly). Update `Hint()` accordingly.
  - UI (`DatabaseLoginWindow`): a selector, and a confirmation with a clear warning when choosing
    `Compatibility`. Add a short hint next to the username field recommending a dedicated low-privilege
    login instead of `sa` (F-13).
  - Keeping old points in `Compatibility` preserves monitoring but leaves the weakness open for those
    points, so it must never be silent:
    - every point in `Compatibility` shows a visible "server identity not verified" mark in the monitor
      points list, on its dashboard card and in its settings row;
    - while at least one such point exists, the service keeps one Warning-level entry in
      "Messages & Issues" that names the points (one entry in total, not one alert per check cycle);
    - the status / settings DTO exposes each point's TLS mode so reports and the security matrix can
      count them;
    - an Administrator action "Test with verification" tries the point's stored login in `Verify` mode
      without changing what is stored, reports the result, and on success offers to switch the point to
      `Verify`. Switching is always the Administrator's explicit choice, in either direction;
    - `docs/security/DATABASE_TLS.md` explains what the customer must do on the server side (a
      certificate that chains to a trusted root and matches the server name) to move a point to `Verify`.
- **R4-S07 — Other outbound connections.** Confirm (tests) that `ConfigPuller` refuses non-HTTPS URLs as
  implemented in R1-S03, that timeouts apply, and that no handler in the solution sets a certificate
  callback other than the pinning one from R4-S05.

**Security tests:** HTTP request to the remote port is not served; request without / with wrong key is
`401` and is rate-limited; viewer key gets `403` on Administrator routes; admin key works only when
remote administration is on; no plain key is present in any stored file after migration; a `Verify` point
does not connect to a server with an untrusted certificate (unit-test the connection-string builders for
all three engines, since a live server may not be available); no automatic downgrade path exists.
A point in `Compatibility` produces the "not verified" mark and the single Warning entry; "Test with
verification" never changes the stored mode by itself.
**Regression tests:** an existing point without a TLS mode builds the same connection string as before;
remote Viewer can read every Viewer route over HTTPS; settings with icons still save.

**Owner verifies:** from a second PC, `http://<server>:5050/api/status` fails; the Desktop app pairs over
HTTPS after confirming the fingerprint; an existing SQL Server point keeps working after upgrade and
shows "not verified"; a new point against a server with a self-signed certificate fails with the
certificate message and works after explicitly choosing compatibility.

---

## 9. Run 5 — Licensing

**Goal:** license trust is anchored in the application; the product does not depend on a temporary domain
or on a shared secret. **Plan gate:** yes. **Needs:** D1, D2, D3 — without them the affected steps are
`BLOCKED`. **Fixes:** F-01 (design side), F-18, F-19.

- **R5-S01 — Production URL (D2).** Replace the temporary URL in `LicensingOptions` with the owner's
  production URL. It stays non-configurable in release builds. Tests inject their own.
- **R5-S02 — Pinned signing keys (D3, F-18).** Embed the owner's public JWK set in the application.
  Token verification trusts **only** these keys. Keys downloaded from the server and keys found in
  `license.json` are never trust anchors: `SigningKeysJson` is no longer written, and is ignored when
  read from an older file. A token whose `kid` is not pinned is not valid offline, with the message
  "this version cannot verify the license — install the latest version". Correct the misleading comment
  in `LicenseService.OfflineUntil`.
  Encrypting `license.json` or restricting its permissions is **not** the fix and must not be presented
  as one: as long as the verification keys and the token can be replaced together in a file the machine's
  administrator controls, the token proves nothing. The trust anchor is the key set compiled into the
  program, and nothing read from disk or from the network can add to it.
- **R5-S03 — Key rotation.** The pinned set may hold several keys. Document the procedure in
  `docs/security/LICENSING.md`: ship the next public key in a release first, start signing with it only
  after that release is deployed. No server-side chain is assumed.
- **R5-S04 — Client credentials (D1).** If D1 = YES: the product ships with no client secret and calls
  the device endpoints without a token; remove the dead credential path from release builds or leave it
  inert. If D1 = NO: stop and report — the fix needs a change on the platform and an owner decision.
- **R5-S05 — Existing behaviour.** Keep device binding (`DeviceFingerprint`, device check at
  `LicenseService` 362), product binding, offline grace, clock-rollback detection, and the 1–5 minute
  check interval. Existing valid installations stay licensed after the upgrade as long as their token is
  signed by a pinned key.
- **R5-S06 — Documentation.** In `docs/security/LICENSING.md`, state the limits honestly: a customer who
  administers the machine can still patch the program; the design stops file edits and key substitution,
  not binary modification.

**Security tests:** a token signed by an unpinned key is rejected even when that key is present in
`license.json`; a `license.json` with a forged token + forged key set + blocked network does not license
the product; `alg` other than ES256 rejected (existing).
**Regression tests:** all existing tests in `LicensingTests.cs` pass after being adapted to inject the
pinned set; offline grace and restart survival unchanged.

**Owner verifies:** activation, heartbeat, deactivation against the real platform on the production URL;
offline start after activation still works; editing `license.json` by hand does not extend a license.

---

## 10. Run 6 — Local data, secrets at rest, clipboard

**Goal:** stored secrets can be read only by the service; the Desktop app never holds a stored secret
unless an Administrator explicitly asks for it. **Plan gate:** yes. **Needs:** D8.
**Fixes:** F-16, F-23, F-26 (runtime part).

- **R6-S01 — Service secrets on Windows (F-16).** Protect service secrets so only the service account can
  unprotect them. Evaluate DPAPI `CurrentUser` scope under the LocalSystem account with a new prefix
  (for example `dpapi2:`); keep reading the legacy `dpapi:` values and re-protect them on first read
  (idempotent, with the Run 3 backup rule). Record in `docs/security/OWNER_ACTIONS.md` that changing the
  service account later requires re-entering stored passwords. Verify on Windows; from Linux this is
  `DONE-UNVERIFIED`.
- **R6-S02 — Desktop secrets.** The Desktop app's own secrets (remote key, pinned data in `client.json`)
  use per-user protection: DPAPI `CurrentUser` on Windows, the existing per-user key file on Linux /
  macOS (`Desktop/Program.cs` 16). `SecretProtector` gets an explicit way to select the scope per
  process, like `KeyFile`. Migrate the existing machine-scope value on first load.
- **R6-S03 — No stored secret in the Desktop.** Confirm by test and by search that the Desktop no longer
  calls `SecretProtector.Unprotect` on anything received from the service, and that no DTO it receives
  can contain a protected blob.
- **R6-S04 — Unix key file.** On load verify owner and mode (`0600`, owned by the service user) and that
  the parent folder is not writable by others; refuse to use a key that fails the check and log how to
  fix it. Create the key with create-new semantics so an existing file or symlink is never overwritten.
  Document that backups of the state folder contain the key.
- **R6-S05 — State file permissions at write time (F-26).** On Linux / macOS create `license.json`,
  `config.json`, `settings.json`, `device.id`, `firewall-port`, the remote certificate / key files, and
  SQLite files with owner-only permissions, independent of the process umask. Fix existing files on
  start.
- **R6-S06 — SQLite.** Confirm `reports.db`, `-wal`, `-shm` and `local.db` are covered by S05 and by the
  Windows ACL from Run 3. Write a short decision record in `docs/security/DATA_AT_REST.md` on whether
  database encryption is warranted, based on what the tables actually hold (`ReportStore.cs` schema);
  do not add an encryption dependency unless the record justifies it and the owner agrees.
- **R6-S07 — Logs.** Review every log statement that takes settings, logins, URLs or exception text
  (start with `[Database]` lines in `LocalApiHost.cs` and `DatabaseMonitor.cs`). Add a redaction helper
  where a driver exception could include connection details, and a test that feeds a connection failure
  containing a password and asserts it is absent from the log sink and from the API result. Add an
  administrative audit line (who: role + transport, what, result) for: settings saved, license actions,
  key create / rotate / revoke, remote access on / off, password reveal. `DataCleaner` retention is
  unchanged and must cover any new log file.
- **R6-S08 — Clipboard (F-23, D8).** Opening a database tool no longer copies anything. A separate,
  explicit "Copy password" action exists for Administrators only: it calls a new Administrator-only
  route that returns the stored password of one monitor point, shows a warning first, writes an audit
  line, and clears the clipboard after 30 seconds if it still holds that value. On Windows, mark the
  clipboard data so it is excluded from clipboard history and cloud sync where the platform allows. For
  SSMS / Azure Data Studio keep the existing integrated-security path that needs no password.

**Security tests:** Viewer gets `403` on password reveal; reveal writes an audit line without the
password; legacy `dpapi:` values are readable after upgrade and are stored in the new form afterwards;
key file with wrong mode is refused (Unix); state files are owner-only after a save (Unix).
**Regression tests:** database monitoring keeps working across the secret migration; Desktop still
remembers its remote key after the scope migration.

**Owner verifies (Windows):** as a standard user, a small PowerShell `ProtectedData.Unprotect` attempt on
a copied blob fails; database points still connect after upgrade; "Copy password" asks for confirmation
and the clipboard clears.

---

## 11. Run 7 — Tests, dependencies, CI and release documentation

**Goal:** the fixes stay fixed, and the owner has a written release procedure. **Plan gate:** no.
**Needs:** nothing. **Fixes:** F-29 (document), F-30, F-31 (document).

- **R7-S01 — Coverage matrix.** `docs/security/SECURITY_MATRIX.md`: one row per finding F-01…F-32 with
  severity, original weakness, remediation, source files, the tests that prove it, and status
  `Fixed / Mitigated / Pending / Not applicable`. Add any missing test.
- **R7-S02 — Dependencies.** Run `dotnet list package --vulnerable --include-transitive` and
  `--outdated`; record the output. Upgrade to patched versions where a vulnerability is reported, one
  package per commit, with the tests re-run. For `LibreHardwareMonitorLib`, record which kernel driver the
  referenced version loads on Windows and whether current Microsoft Defender flags it; do not change the
  sensor library in this run — report it.
- **R7-S03 — CI.** Extend the workflow: vulnerable-package check that fails on high severity, gitleaks,
  the release-secret check from R1-S07 on a real `dotnet publish` output for each runtime, and the
  permission-verification script on the Linux runner after a scripted install where feasible.
- **R7-S04 — Release procedures (documentation only; you do not have the certificates).**
  `docs/security/RELEASE.md`: Windows Authenticode signing of service, desktop and installer and how to
  verify it; macOS Developer ID signing and notarization replacing the ad-hoc signature and quarantine
  removal in `scripts/macos/*` (F-29), with the exact lines to change once credentials exist; Linux
  package / repository signing; antivirus testing and vendor false-positive submission; the platform test
  matrix (Windows, Ubuntu/Debian, a RHEL-compatible distribution, macOS Intel, macOS Apple Silicon) with
  a column for "tested by / date".
- **R7-S05 — Data-flow disclosure (F-31).** `docs/security/DATA_FLOWS.md`: every external host the agent
  contacts, why, what is sent, and how to turn it off if it can be turned off. Source it from the code,
  not from memory.
- **R7-S06 — Final reports.** In `docs/security/`: `IMPLEMENTATION_REPORT.md` (what was fixed, files,
  architecture changes, remaining risks, manual actions, known compatibility issues),
  `MIGRATION_REPORT.md` (what changes on an upgraded machine and how each kind of data is preserved),
  `RELEASE_READINESS.md` (Windows / Linux / macOS / licensing / remote API, each with blockers). Base
  every statement on `PROGRESS.md`; anything `DONE-UNVERIFIED` is listed as not yet verified.

**Acceptance:** CI green on three OSes; every finding has a row and a status; no report claims a test
that was not run.

---

## 12. Deferred — do not implement

**Service privilege reduction (F-32).** Running the monitoring under a restricted account and splitting
out the parts that need elevation (hardware sensors, SMART, `dmidecode`, firewall changes) is an
architectural change with a real risk of breaking hardware monitoring. It is planned separately after
the runs above are released. Do not start it. If a step above seems to need it, stop and report.

## 13. Out of scope — do not implement

These belong to the future cloud platform, not to the agent as it exists: tenant isolation, platform user
authentication and MFA, remote command execution and approval, replay protection for commands,
centralized audit logging, telemetry upload, auto-update, device enrollment and platform device identity.
If you find existing code that already does one of these, report it; do not extend it.

Also out of scope: renaming the product or its identifiers, UI redesign, replacing the sensor library,
building an MSI or a new installer technology, rewriting Git history.

---

## 14. Definition of done for the whole plan

- Runs 0–7 are each `DONE-VERIFIED`, or their unverified and blocked items are listed by name with the
  reason, in `PROGRESS.md` and in `RELEASE_READINESS.md`.
- No secret or personal data ships in defaults or packages.
- No caller reaches the API without an OS identity (local) or a key over HTTPS (remote).
- A Viewer cannot change anything or obtain a secret.
- Stored secrets never leave the service except through the audited Administrator reveal action.
- A standard user cannot modify what the service executes or loads, or read its state.
- New database points verify the server; existing points keep working and are visibly marked.
- License verification trusts only keys compiled into the application.
- Every finding has a security test and a regression test, or a stated reason why it cannot have one.
- Nothing is described as ready for production while a C or H finding is open or unverified.
