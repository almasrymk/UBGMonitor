# مراجعة التنفيذ

## المكونات المؤكدة
- الخدمة: `src/MonitorAgent.Service/Program.cs`، Generic Host وBackgroundService على .NET 10.
- الواجهة: `MonitorAgent.Desktop`، Avalonia وCommunityToolkit MVVM؛ MainWindow ينشئ الصفحة الظاهرة فقط. توجد تعديلات سابقة غير ملتزم بها يجب الحفاظ عليها.
- النقل والصلاحيات: LocalApiHost، RequiredAgentRole، LocalAdministrationOnly، named pipes/Unix sockets وهوية نظام التشغيل؛ TCP يستخدم HTTPS ومفاتيح Viewer/Admin. لا تغيير لهذه الحدود.
- التنبيهات: MonitorHealthStore.SetIssue/ClearIssue، NotificationTrigger، NotificationEngine، IssueDataLogger، ReportStore. التاريخ SQLite داخل reports.db. لا توجد خدمة مستقلة مؤكدة لإقرار/تصعيد يدوي متعدد المستويات.
- التقارير: ReportBuilder والجدول incidents؛ تنظيف DataCleaner. إنشاء الجداول الحالي idempotent وليس إطار migrations منفصلاً.
- السحابة: ServiceCloudSource يرسل health issues إلى MonitorAgent.Cloud؛ لا يوجد عقد لاستعراض مصادر لوجات أو أحداثها. أي issue جديد لا يحمل نص اللوج.
- التغليف: scripts Windows/Linux/macOS وtools/Packager ينشران المشاريع الحالية. لا حاجة لعملية خلفية إضافية.
- الاختبارات: tests/MonitorAgent.Tests و.github/workflows/ci.yml؛ الأوامر المعتادة dotnet build MonitorAgent.sln -c Release وdotnet test MonitorAgent.sln.

## القرار
إضافة LogMonitoring داخل Shared/Service/Desktop، وإضافة جداول داخل reports.db بنفس المكتبة الحالية. وحدة مقفولة ومجلدات مسموحة فارغة افتراضياً. إدارة المسارات والمصادر محلية للأدمن فقط؛ استعراض أحداث منقحة بصلاحيات العرض الحالية. لا endpoint لقراءة مسار حر، ولا بيانات لوج خام في السحابة أو diagnostics. لا حزم جديدة.

## المخاطر وحدود التحقق
فتح ملف بعد فحص مساره يتطلب التحقق من المسار الفعلي للـ handle؛ لا يكفي prefix أو فحص links قبل الفتح. دوران copy-truncate قد يفقد bytes إذا الكاتب حذفها قبل القراءة؛ لا ادعاء exactly-once. Redaction تقديري وليس ضماناً لاكتشاف كل البيانات الشخصية. معيار الذاكرة يتطلب قياس فعلي. إعادة تشغيل الخدمة تغلق incidents القديمة حالياً؛ يجب معالجة LogMonitoring فقط دون تغيير سلوك المراقبات الأخرى.
