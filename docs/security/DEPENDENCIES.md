# Dependency audit — 2026-10-08

الأوامر الفعلية: `dotnet list MonitorAgent.sln package --vulnerable --include-transitive --format json --output-version 1`، ونفس الأمر مع `--outdated`. النتائج الكاملة محفوظة محليًا في TestResults؛ تشمل مسارات جهاز التطوير، لذلك لا توزع مع البرنامج.

| التنبيه الأول | الإجراء | نتيجة الفحص اللاحق |
|---|---|---|
| SQLitePCLRaw.lib.e_sqlite3 2.1.6، High، GHSA-2m69-gcr7-jv3q | SQLitePCLRaw.bundle_e_sqlite3 3.0.5 override، مع إبقاء Microsoft.Data.Sqlite 8.0.11 وschema | اختفى |
| System.Net.Http 4.3.0، High، GHSA-7jgj-8wvc-jh57، tests | xUnit 2.9.3 بدل 2.5.3 | اختفى |
| System.Text.RegularExpressions 4.3.0، High، GHSA-cmhx-cq75-c4mj، tests | نفس ترقية xUnit، بلا override لحزمة framework قديمة | اختفى |

كل ترقية في commit مستقل، وبُنيت واختُبرت: 154 نجاح، 3 Unix-only skip، بلا فشل أو تحذيرات build. الإضافات مجانية؛ override SQLite إصلاح أمني يحافظ على API والبيانات، وليس إضافة تشفير. [مصدر SQLite الرسمي](https://github.com/ericsink/SQLitePCL.raw) يوضح توزيع SourceGear.sqlite3 المجاني وbundle المتوافق. [تنبيه SQLite](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)، [HTTP](https://github.com/advisories/GHSA-7jgj-8wvc-jh57)، [regex](https://github.com/advisories/GHSA-cmhx-cq75-c4mj).

فحص outdated أظهر إصدارات أحدث، منها Microsoft.Data.Sqlite 10.0.12 وSqlClient 7.1.1. لم تجرِ ترقيات major جماعية؛ إصدار أحدث وحده لا يثبت ضرورة تعديل آمن. الفحص النهائي لم يبلغ عن vulnerabilities في المشاريع الأربعة. CI يرفض High/Critical؛ لا يمثل ذلك ضمانًا لغياب ثغرات غير منشورة.

LibreHardwareMonitorLib بقي 0.9.6. [مصدر الإصدار الرسمي PawnIo.cs](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/PawnIo/PawnIo.cs) يفتح `GLOBALROOT\Device\PawnIO` المثبت على Windows ويرسل إليه embedded modules عبر DeviceIoControl، ويقرأ نسخة PawnIO من registry. لم تُشغل الخدمة أو driver، لذلك installed version/hash ونتيجة Microsoft Defender الحالية غير متحققين. يلزم فحص التوزيع النهائي على جهاز اختبار وتسجيل النتيجة بتاريخها؛ لا تغيير للمكتبة أو تعطيل Defender.

فحص منصة LicensingPlatform المحلية الإضافي شمل 7 مشاريع ولم يبلغ عن vulnerabilities؛ نتائجها في TestResults/platform-vulnerabilities.json. regression/security suite للمنصة: 136 نجاح/0 فشل/0 skip، مع تحذير CS0108 قائم في MediaController.User خارج نطاق enrollment. لا إنتاج أو نشر.
