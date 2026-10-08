# برومبتات التشغيل — انسخ واحد في كل مرة

الترتيب ثابت: Run 0 ثم 1 ثم 2 … ثم 7. لا تبدأ تشغيل قبل ما تراجع وتدمج الـ PR بتاع اللي قبله في
`security/hardening`.

التشغيلات 2 و 3 و 4 و 5 و 6 لها مرحلتين: **A** خطة فقط، ثم **B** تنفيذ بعد موافقتك.
التشغيلات 0 و 1 و 7 مرحلة واحدة.

قبل كل تشغيل اتأكد إن المدخلات المطلوبة منك في قسم 2 من `SECURITY_PLAN.md` مكتوبة.

**مهم:** الملف ده برومبتات تشغيل بس. التفاصيل كلها (الثغرات، الحلول، الاختبارات، الشروط) في
`docs/security/SECURITY_PLAN.md`. الملفات الأربعة لازم تكون مرفوعة على الفرع
`security/group1-sanitize-distribution` في الريبو قبل أي برومبت، وإلا Codex مش هيشوف الخطة:

- `AGENTS.md` (في جذر الريبو)
- `docs/security/SECURITY_PLAN.md`
- `docs/security/PROGRESS.md`
- `docs/security/RUN_PROMPTS.md`

**إجراء عاجل عليك انت قبل Run 0:** إلغاء سر الترخيص المكشوف على منصة التراخيص وإصدار بديل
(قسم 1.1 في الخطة). ده مش شغل Codex ومش مستني أي تعديل في الكود.

---

## مراجعة الخطة قبل البدء (اختياري، من غير أي تعديل)

```
Do not change any file. Read AGENTS.md and docs/security/SECURITY_PLAN.md completely, then compare the
plan with the code on this branch. Tell me, in Arabic:
1. Any finding in section 3 that does not match the code you see (file, symbol, what differs).
2. Any security weakness you find in the code that section 3 does not list.
3. Any step whose design you expect to fail, and why.
4. Any dependency between steps or runs that the plan misses.
Be specific: file and symbol for every point. If docs/security/SECURITY_PLAN.md is not in the
repository, say so and stop.
```

---

## Run 0 — Baseline, tests, CI

```
Read AGENTS.md and docs/security/SECURITY_PLAN.md (all of section 0, then section 4).
Execute Run 0 completely (steps R0-S01 to R0-S05), following the protocol in section 0.
Record every step in docs/security/PROGRESS.md. Open a pull request into security/hardening and stop.
Do not start Run 1.
```

## Run 1 — Secrets and distribution cleanup

```
Read AGENTS.md, docs/security/SECURITY_PLAN.md (section 0, section 2, section 5) and
docs/security/PROGRESS.md.
Execute Run 1 completely (steps R1-S01 to R1-S08), one commit per step, following section 0.
Open a pull request into security/hardening with the run summary and the owner verification checklist,
then stop. Do not start Run 2.
```

## Run 2 — Local API

**Phase A**

```
Read AGENTS.md, docs/security/SECURITY_PLAN.md (sections 0, 2, 3 and 6) and docs/security/PROGRESS.md.
Do Run 2, Phase A only: study the code, build the proof of concept described in section 6.1, and write
"Run 2 — خطة التنفيذ" in docs/security/PROGRESS.md as section 0.3 requires, including the PoC results
and anything in the plan that does not match the code. Commit only PROGRESS.md and stop.
Do not change product code.
```

**Phase B** (بعد ما تقرأ الخطة وتوافق)

```
The Run 2 plan in docs/security/PROGRESS.md is approved. [اكتب هنا أي تعديل تريده على الخطة، أو احذف هذا السطر]
Do Run 2, Phase B: implement steps R2-S01 to R2-S11 in order, one commit per step, with the tests listed
in section 6 and a PROGRESS.md entry per step. Open a pull request into security/hardening with the run
summary and the owner verification checklist, then stop. Do not start Run 3.
```

## Run 3 — Installation and file permissions

**Phase A**

```
Read AGENTS.md, docs/security/SECURITY_PLAN.md (sections 0, 2, 3 and 7) and docs/security/PROGRESS.md.
Do Run 3, Phase A only: write "Run 3 — خطة التنفيذ" in docs/security/PROGRESS.md, including the exact
old and new path of every file that moves on each OS and how the migration is made idempotent.
Commit only PROGRESS.md and stop. Do not change product code.
```

**Phase B**

```
The Run 3 plan in docs/security/PROGRESS.md is approved. [تعديلاتك إن وجدت]
Do Run 3, Phase B: implement steps R3-S01 to R3-S07 in order, one commit per step, with tests and a
PROGRESS.md entry per step. Open a pull request into security/hardening and stop. Do not start Run 4.
```

## Run 4 — Network and database TLS

**Phase A**

```
Read AGENTS.md, docs/security/SECURITY_PLAN.md (sections 0, 2, 3 and 8) and docs/security/PROGRESS.md.
Do Run 4, Phase A only: write "Run 4 — خطة التنفيذ" in docs/security/PROGRESS.md, including the exact
connection-string settings per database engine for both TLS modes and how existing stored points are
recognised as Compatibility. Commit only PROGRESS.md and stop. Do not change product code.
```

**Phase B**

```
The Run 4 plan in docs/security/PROGRESS.md is approved. [تعديلاتك إن وجدت]
Do Run 4, Phase B: implement steps R4-S01 to R4-S07 in order, one commit per step, with tests and a
PROGRESS.md entry per step. Open a pull request into security/hardening and stop. Do not start Run 5.
```

## Run 5 — Licensing

قبل التشغيل: املأ D1 و D2 و D3 في قسم 2 من الخطة.

**Phase A**

```
Read AGENTS.md, docs/security/SECURITY_PLAN.md (sections 0, 2, 3 and 9) and docs/security/PROGRESS.md.
Do Run 5, Phase A only. First check that D1, D2 and D3 in section 2 are filled in; list any that are not
and which steps they block. Write "Run 5 — خطة التنفيذ" in docs/security/PROGRESS.md.
Commit only PROGRESS.md and stop. Do not change product code. Do not contact the licensing server.
```

**Phase B**

```
The Run 5 plan in docs/security/PROGRESS.md is approved. [تعديلاتك إن وجدت]
Do Run 5, Phase B: implement the steps that are not blocked, R5-S01 to R5-S06 in order, one commit per
step, with tests and a PROGRESS.md entry per step. Open a pull request into security/hardening and stop.
Do not start Run 6.
```

## Run 6 — Local data and clipboard

**Phase A**

```
Read AGENTS.md, docs/security/SECURITY_PLAN.md (sections 0, 2, 3 and 10) and docs/security/PROGRESS.md.
Do Run 6, Phase A only: write "Run 6 — خطة التنفيذ" in docs/security/PROGRESS.md, including how each
existing protected value is migrated and what happens if the migration is interrupted.
Commit only PROGRESS.md and stop. Do not change product code.
```

**Phase B**

```
The Run 6 plan in docs/security/PROGRESS.md is approved. [تعديلاتك إن وجدت]
Do Run 6, Phase B: implement steps R6-S01 to R6-S08 in order, one commit per step, with tests and a
PROGRESS.md entry per step. Open a pull request into security/hardening and stop. Do not start Run 7.
```

## Run 7 — Tests, dependencies, release documentation

```
Read AGENTS.md, docs/security/SECURITY_PLAN.md (sections 0, 3, 11 and 14) and docs/security/PROGRESS.md.
Execute Run 7 completely (steps R7-S01 to R7-S06), one commit per step. Base every report on what
PROGRESS.md actually records; list anything DONE-UNVERIFIED or BLOCKED as not verified.
Open a pull request into security/hardening and stop.
```

---

## برومبتات مساعدة

**لو Codex وقف عند خطوة BLOCKED وجاوبت على سؤاله:**

```
The open question for step R<run>-S<step> is answered in docs/security/SECURITY_PLAN.md section 2
[أو اكتب الإجابة هنا]. Resume Run <run> from that step, following section 0. Stop at the end of the run.
```

**لو اختبار فشل عندك على جهاز حقيقي:**

```
Owner verification of Run <run> failed on <Windows/Linux/macOS>:
<الصق هنا ما فعلته وما ظهر لك بالضبط>
Find the cause, fix it as an additional step R<run>-S<next number> with a test that reproduces the
failure, record it in docs/security/PROGRESS.md, push to the same pull request and stop.
```

**لو عايز ملخص حالة من غير أي تعديل:**

```
Do not change any file. Read docs/security/PROGRESS.md and the git log of security/hardening and tell me,
in Arabic: which runs and steps are DONE-VERIFIED, DONE-UNVERIFIED, BLOCKED or not started, and what I
need to test or decide next.
```
