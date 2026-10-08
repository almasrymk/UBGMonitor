# مصفوفة النتائج — 2026-10-08

Fixed تعني إثبات إصلاح الكود باختبارات محلية. Mitigated تعني وجود حماية مع خطر مقبول أو تحقق تشغيل ناقص. Pending تعني اعتمادًا أو إجراءً لم يكتمل. كل مسارات src/tests أدناه نسبية لجذر المشروع؛ تفاصيل الأوامر والـcommits في PROGRESS.md. لا يوجد ادعاء بأن الإصدار جاهز للعملاء.

| Finding | الشدة | الضعف الأصلي | المعالجة ومصدرها | دليل الأمان وعدم الانكسار / الحد | الحالة |
|---|---|---|---|---|---|
| F-01 | C | ClientSecret منشور | defaults فارغة؛ DeviceEnrollment opt-in، LicensePlatformClient؛ OWNER_ACTIONS | LicensingTests/DeviceEnrollmentClientTests؛ إلغاء السر القديم لم يؤكده المالك | Pending |
| F-02 | H | إعدادات شخصية موزعة | appsettings.json وclean install | ReleaseSettingsTests + scan publish؛ لا بيانات شخصية | Fixed |
| F-03 | M | artifacts tracked | .gitignore وإزالة tracking | git tracked-file check + publish scanner؛ التاريخ لا يُعاد كتابته | Fixed |
| F-04 | H | loopback بلا هوية | IpcHostConfiguration/LocalIpc؛ pipes/socket ACL | LocalApiSecurityTests + Windows IPC PoC؛ حساب ثانٍ وUnix غير متحققين | Mitigated |
| F-05 | H | مفتاح فارغ يسمح بالوصول | RemoteAccessManager/LocalApiHost fail closed | RemoteAccessTests رفض missing key ونجاح viewer؛ جهاز ثانٍ مطلوب | Mitigated |
| F-06 | H | settings تكشف الأسرار | SettingsContract whitelist/flags | SettingsContractTests + HTTP roundtrip؛ disk ACL بحاجة owner verification | Mitigated |
| F-07 | H | تغيير listen/key وفتح firewall | local-only remote management + explicit firewall | LocalApiSecurityTests منع TCP management وViewer؛ firewall فعلي غير متحقق | Mitigated |
| F-08 | H | decrypt oracle | DatabaseTestResolver + target binding + رفض blobs | SettingsContractTests منع redirect وقبول newly typed؛ disk protection بحاجة تحقق | Mitigated |
| F-09 | H | shared key يملك كل الصلاحيات | RequiredAgentRole metadata + separate roles/keys | كل write routes ترفض Viewer، endpoint metadata؛ OS identity كامل غير متحقق | Mitigated |
| F-10 | M | CORS/rebinding/browser POST | لا CORS، Host allowlist، auth لكل TCP | LocalApiSecurityTests Host/auth رفض + trusted request نجاح | Fixed |
| F-11 | H | TCP plain HTTP | RemoteTransport HTTPS + CertificateTrust pairing | اختبار TLS حقيقي Windows، HTTP rejected وHTTPS 200؛ باقي منصات/UI مطلوب | Mitigated |
| F-12 | M | DB بلا cert verification | DatabaseTlsMode.Verify مع Compatibility ظاهر | DatabaseTlsTests builders لكل engine/warning؛ legacy compatibility اختيار محفوظ | Mitigated |
| F-13 | L | sa health-check login | DatabaseLoginWindow low-privilege hint؛ DATABASE_TLS | source review؛ حساب محدود في DB حقيقية مطلوب | Mitigated |
| F-14 | H | SYSTEM binaries تحت ProgramData writable | Program Files + state منفصل، install-service.ps1/verifier | StateMigrationTests تحفظ history/conflicts؛ installer ACL فعلي مطلوب | Mitigated |
| F-15 | M | state readable محليًا | Windows ACL protected + Unix 700/600 | verify-permissions scripts + migration tests؛ cross-account proof مطلوب | Mitigated |
| F-16 | M | DPAPI machine/decrypt Desktop | dpapi2 CurrentUser + legacy re-protection | SecretStorageTests roundtrip + Desktop search؛ LocalSystem/standard user مطلوب | Mitigated |
| F-17 | M | remote key plaintext | hash only في remote-access.json، client per-user encrypted | RemoteAccessTests key absent/hashes/revoke؛ backups القديمة تحتاج دوران/احتفاظ آمن | Mitigated |
| F-18 | M | license keys من cache | DEVICE_ENROLLMENT/LICENSING يحددان المرحلة الآمنة | existing licensing regressions؛ JWK trust anchors لم تُعتمد، لا تثبيت تلقائي | Pending |
| F-19 | M | temporary licensing domain | URL الحالية لم تُغير حتى لا تنقطع التراخيص | production domain لم يُعتمد | Pending |
| F-20 | M | launching arbitrary scheme/path | MonitorPointLauncher/ShellOpen local path+confirmation | SettingsContractTests schemes/UNC رفض؛ launcher actual UI مطلوب | Mitigated |
| F-21 | M | save يكتب source tree | ServiceSettingsFile state-only | ReleaseSettingsTests/HTTP settings save؛ no development-copy call | Fixed |
| F-22 | M | routing defaults واتصال DB path خارجي | RoutingOptions empty، ConfigPuller fixed local.db | RoutingSecurityTests silent empty/config success/timeout/cache preserved | Fixed |
| F-23 | M | automatic clipboard password | explicit Admin reveal + audit + timed clipboard | LocalApiSecurityTests وSecretStorageTests.Sensitive_clipboard_clears_only_its_unchanged_value؛ history/cloud UI proof مطلوب | Mitigated |
| F-24 | M | samples في clean install | LocalConfigCache empty points | ReleaseSettingsTests empty default + configured points regressions | Fixed |
| F-25 | H | cp -a يترك owner عادي | Linux install chown root/go-w | bash syntax + CI fixture؛ installer/Linux live مطلوب | Mitigated |
| F-26 | L | Unix state world-readable | PrivateFile/StatePermissions/umask77 | Unix-only tests مكتوبة ومتخطاة على Windows؛ CI غير منشور | Mitigated |
| F-27 | L | uninstall يترك firewall | uninstall-service.ps1 removal | PowerShell parser؛ uninstall actual rule مطلوب | Mitigated |
| F-28 | L | customer machine compile | prebuilt -Source وBuildFromSource opt-in | script source/parser؛ customer install غير منفذ | Mitigated |
| F-29 | L | mac ad-hoc/quarantine removal | RELEASE.md يحدد replacement lines | وثيقة فقط؛ Developer ID/notarization لم تنفذ | Pending |
| F-30 | L | لا CI/audit/scanning | .github/workflows/ci.yml + scanners | local audit/publish/tests PASS؛ آخر CI لم يُنشر حسب طلب المالك | Mitigated |
| F-31 | L | third-party flows غير معلنة | DATA_FLOWS.md sourced from code | source host inventory؛ لا تغييرات لإيقاف feature خارج الخطة | Fixed |
| F-32 | — | الخدمة مرتفعة الصلاحيات | مؤجل صراحةً في الخطة | لا splitting/privilege reduction، لتجنب كسر sensors | Pending |

الاختبارات لا تتصل بالترخيص الإنتاجي أو أنظمة العملاء. الخطر C/H لا يعد مغلقًا للإصدار طالما بقي Pending أو تحقق التشغيل ناقصًا. النتائج الخاصة بمنصة LicensingPlatform في DEVICE_ENROLLMENT وPROGRESS: branch محلي، flag مغلق، والتوافق القديم محفوظ.
