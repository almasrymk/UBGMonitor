# جاهزية الإصدار — غير جاهز للإنتاج

الأجزاء الممكنة محليًا من الكود والفحوص موثقة في PROGRESS.md. وجود إصلاح لا يساوي إثبات التشغيل على كل نظام. لا customer install، push أو deployment تم.

| المجال | ما ثبت محليًا | blockers والتحقق المطلوب |
|---|---|---|
| Windows | Release build/tests، TLS حقيقي، win-x64 Service/Desktop publish/scans، parser installer، migration/DPAPI tests بالحساب الحالي | LocalSystem مقابل standard user؛ pipe groups impersonation/squatting عبر حساب مستقل؛ installer ACL ومigration upgrade؛ firewall uninstall؛ UI reveal/pairing/clipboard |
| Linux | linux-x64 cross-publish Service/Desktop وsecret scans نجحت من Windows؛ root owner/state/socket safeguards وUnix tests/CI fixture مكتوبة | Ubuntu/Debian وRHEL تشغيل فعلي؛ ACL/groups/modes/root-owned socket؛ package migration/wal/shm؛ SELinux بدون تعطيل |
| macOS | osx-arm64 cross-publish Service/Desktop وsecret scans نجحت من Windows؛ paths/groups/plist/scripts وCI مجهزة | Intel/ARM تشغيل فعلي؛ launchd/IPC/upgrade؛ Developer ID/notarization. ad-hoc scripts لم تستبدل بلا شهادات |
| Licensing | Agent opt-in enrollment + platform flag-off compatible؛ 136 tests ناجحة و7 مشاريع بلا vulnerable packages مبلغ عنها؛ لا secrets مشتركة جديدة | F-01 revoke مؤكدة؛ D2 domain إنتاج، D3 anchors من قناة مستقلة، نشر migration/enrollment بإجراء مالك. keys fetched لا تستعمل trust anchor |
| Remote API | key hashes/separate roles، local-only management، HTTPS pinning، no plain HTTP، limits/timeout tests | جهاز ثانٍ/real UI، cert import/rotation/expired failure، service account TLS، Unix، firewall policy |
| Databases | engine connection strings Verify/Compatibility، target binding، no client blobs | certificates وعمل monitoring بكل engine، owner approval للـCompatibility legacy |
| CI/release | local dependency audit/scanners/publish PASS؛ workflows مجهزة | workflow الجديدة لم تُدفع؛ CI ثلاث منصات لم يُشغل؛ signing/AV acceptance/SBOM/hash release artifacts |

## حالة الخطة

Runs 0/1: baseline وsanitization/scanners موثقة؛ CI القديمة لا تثبت تعديل اليوم. Run 2: منطق roles/settings verified محليًا، OS multi-account/Unix DONE-UNVERIFIED. Run 3: state/migration code verified، installs/permissions DONE-UNVERIFIED. Run 4: tests local verified، remote/UI/platform وDB live DONE-UNVERIFIED. Run 5: enrollment الآمن implemented؛ trust anchors/domain/revoke BLOCKED. Run 6: per-account/secret contracts tests verified محليًا؛ OS/clipboard/runtime DONE-UNVERIFIED. Run 7: matrices/reports مكتوبة، dependencies local verified، CI/release signing DONE-UNVERIFIED أو BLOCKED حسب المورد.

لا C/H finding يوصف مغلقًا للإصدار مع manual verification ناقص. موافقات التشغيل السابقة لا تعني موافقة نشر؛ أحدث تعليمات المالك تمنع master/push/deploy. التفاصيل لكل finding في SECURITY_MATRIX.md، ولكل خطوة في PROGRESS.md.
