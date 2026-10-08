# سجل تنفيذ خطة التأمين — MonitorAgent

هذا الملف يكتبه Codex ويقرأه صاحب المشروع. كل خطوة لها مدخل واحد بالصيغة الموجودة في
`SECURITY_PLAN.md` قسم 0.5. الشرح بالعربي، وأسماء الملفات والأوامر والمصطلحات التقنية بالإنجليزي.

معاني الحالة:

- `DONE-VERIFIED` — نُفذت، والاختبارات التي تثبتها اشتغلت فعلاً في بيئة Codex ونجحت.
- `DONE-UNVERIFIED` — نُفذت، لكن إثباتها يحتاج نظام تشغيل أو مورد غير متاح. يُذكر ما هو.
- `BLOCKED` — لم تُنفذ. يُذكر المدخل الناقص أو الافتراض الذي فشل.
- `SKIPPED` — غير منطبقة. يُذكر السبب.

---

## لوحة الحالة

| Run | الموضوع | Phase A (الخطة) | Phase B (التنفيذ) | PR | تحقق صاحب المشروع |
|-----|---------|-----------------|-------------------|----|--------------------|
| 0 | Baseline + tests + CI | خطة التنفيذ أدناه | نُفذ محليًا؛ CI قيد الانتظار | https://github.com/almasrymk/UBGMonitor/pull/1 | مراجعة CI والدمج مطلوبان |
| 1 | Secrets & distribution cleanup | — | لم يبدأ | | |
| 2 | Local API | لم يبدأ | لم يبدأ | | |
| 3 | Installation & permissions | لم يبدأ | لم يبدأ | | |
| 4 | Network & database TLS | لم يبدأ | لم يبدأ | | |
| 5 | Licensing | لم يبدأ | لم يبدأ | | |
| 6 | Local data & clipboard | لم يبدأ | لم يبدأ | | |
| 7 | Tests, dependencies, release docs | — | لم يبدأ | | |

## بيئة التنفيذ

- نظام التشغيل الذي يعمل عليه Codex: Windows 10.0.26200، win-x64.
- نسخة .NET SDK: 10.0.202؛ .NET 8 runtime: 8.0.26.
- ما لا يمكن التحقق منه في هذه البيئة: Linux/macOS محليًا؛ تجرى اختباراتهما في CI، ولا تعتبر ناجحة قبل قراءة النتيجة.

## أسئلة مفتوحة لصاحب المشروع

- إلغاء سر الترخيص على المنصة ومراجعة استخدامه لم يتم تأكيدهما؛ F-01 يبقى Pending.
- D1 وD2 وD3 في الخطة لم تُملأ؛ خطوات Run 5 التابعة لها لا يمكن تنفيذها بدونها.
- D10: البيئة الفعلية Windows محليًا كما سجل الفحص؛ نص القرار في الخطة المرفقة بقي كما هو دون تعديل.
- قبل Run 1: مراجعة ودمج PR الخاص بـRun 0 والتأكد من نتائج CI للأنظمة الثلاثة.

---

<!-- يضيف Codex أقسام التشغيلات تحت هذا السطر، الأحدث في الآخر. -->

## متابعة التنفيذ على master

### توجيه أحدث — العمل المحلي فقط
بعد دمج Run 0، طلب المستخدم عدم تعديل master مباشرة وعدم نشر التغييرات تلقائيًا، وتوسيع Run 5 لدعم Device Enrollment في Agent وLicensingPlatform. تم الانتقال إلى codex/security-hardening-enrollment؛ commits اللاحقة محلية ولا push/merge جديد. commit R1-S01 كان قد تم على master محليًا قبل وصول التوجيه ولم يُنشر. نسخة المنصة المعزولة تحت .security-work/LicensingPlatform مستبعدة من تتبع مستودع Agent؛ تعديلات المنصة تبقى في مستودعها المستقل. D1=NO؛ العنوان الحالي مؤقت وليس اعتماد D2؛ endpoint JWK لا يمثل اعتماد D3. لن يُعطل Authentication أو توضع مفاتيح غير مؤكدة أو سر مشترك في Agent.

### R1-S02 — اختبارات إعدادات التوزيع
- الحالة: DONE-VERIFIED
- ماذا تغيّر ولماذا: أضيف اختبار يقرأ appsettings.json الموزع ويمنع ClientId/ClientSecret أو نقاط شخصية، واختبار يحافظ على قابلية إعداد بيانات تطوير من IConfiguration دون تثبيتها في defaults.
- الملفات: tests/MonitorAgent.Tests/DistributionSecurityTests.cs وPROGRESS.md.
- الأوامر ونتيجتها: dotnet build MonitorAgent.sln -c Release --no-restore نجح 0 أخطاء/0 تحذيرات؛ dotnet test MonitorAgent.sln -c Release --no-build --no-restore نجح 89 اختبارًا/1 متجاوزًا/0 فشل، إجمالي 90.
- اختبار الأمان: ShippedSettingsContainNoCredentialsOrPersonalMonitorPoints.
- اختبار عدم الانكسار: DeveloperCredentialsRemainConfigurableWithoutChangingProductCode.
- ما لم يتم التحقق منه: اختبار الإضافات على Linux/macOS لم يُشغّل بعد؛ لا نشر تلقائي بناءً على التوجيه الأحدث.
- اختلاف عن الخطة: العمل في فرع محلي منفصل؛ توسعة Run 5 مفوضة من المستخدم وتحتاج خطة توافق مستقلة.

- تمت إعادة استهداف PR #1 إلى master بإذن المستخدم، وإصلاح WindowsTheory دون إضعاف assertions. CI run 37755384482 نجح على الأنظمة الثلاثة، ثم دُمج PR #1 عند 8e60a995383ffd669f71e0f80b8d10d3254b4b4d.
- git merge-base --is-ancestor أثبت أن فرعي security/hardening وsecurity/run-0-baseline داخل master. لا حاجة لدمج نفس commits مرة ثانية أو حذف الفروع.
- تحديث master المحلي كان fast-forward. R0-S06: build 0 أخطاء/0 تحذيرات وtests 87 ناجحًا/1 متجاوزًا على Windows؛ CI أثبت تشغيل Linux/macOS أيضًا. وصف انتظار CI السابق تاريخي ولم يعد الحالة الحالية.

## Run 1 — خطة التنفيذ

تنفيذ R1-S01 إلى R1-S08 على master حسب الطلب الجديد. لا تغيير تصميم الواجهة. الأسرار المكتشفة لا تُعرض. الخطوات تضيف اختبارات defaults والتوجيه والكاش، توقف طلبات الخدمات غير المضبوطة، تتجاهل مسار SQLite المستورد من الشبكة، تمنع كتابة النسخ التطويرية في Release، وتضيف فحص الأسرار والتوزيع وقائمة إجراءات المالك.

### R1-S01 — مراجعة الملفات المتتبعة
- الحالة: DONE-VERIFIED
- ماذا تغيّر ولماذا: مراجعة المصدر والإعدادات والسكربتات والاختبارات والوثائق وتوثيق التصنيف بدون قيم اعتماد حقيقية.
- الملفات: docs/security/SECRETS_AUDIT.md وPROGRESS.md.
- الأوامر ونتيجتها: rg -l للأنماط والحقول نجح؛ git ls-files artifacts أعاد صفر ملفات؛ شجرة المصدر/الاختبارات/الوثائق/السكربتات المتتبعة 262 ملفًا قبل الإضافات الجديدة. إعدادات التوزيع لا تحتوي Setting وClientSecret فارغ.
- اختبار الأمان: مراجعة ثابتة؛ اختبار defaults يضاف في R1-S02.
- اختبار عدم الانكسار: آخر suite صالح 87 ناجحًا/1 متجاوزًا، ولم تتغير وظائف المنتج.
- ما لم يتم التحقق منه: إلغاء السر على المنصة لم يؤكد؛ الملفات غير المتتبعة ليست دليلًا على نظافة كل إصدار قديم.
- اختلاف عن الخطة: العمل على master بموجب طلب المستخدم؛ لا PR مستقل لكل تشغيل.

## تعديل بروتوكول التنفيذ بإذن صاحب المشروع

طلب صاحب المشروع صراحةً دمج الفروع التي أنشأها Codex في master واستكمال الخطة عليه. هذا الطلب يتقدم على قاعدة عدم العمل على master وعلى بوابات مراجعة PR بين التشغيلات. لا يجيز تغيير تصميم المنتج أو نشره أو الاتصال بمنصة الترخيص. تبقى اختبارات الخطوات وتوثيقها وتوقف الخطوات المعتمدة على مدخلات ناقصة مطلوبة. CI سيعمل أيضًا على pushes إلى master.

### R0-S06 — إصلاح تصنيف اختبار Windows بعد نتيجة CI
- الحالة: DONE-UNVERIFIED
- ماذا تغيّر ولماذا: CI run 37754782565 نجح على Windows وفشل على Linux وmacOS في حالتين داخل Shared_Folders_Do_Not_Claim_Processes. الاختبار يعتمد على Path.TrimEndingDirectorySeparator ومجلد Windows الخاص بالنظام الحالي؛ windows:true لا يغير دلالات System.IO.Path. أضيف WindowsTheory للاختبار مع الحفاظ على كل بياناته وassertions، وCI يتابع master وفق طلب صاحب المشروع.
- الملفات: PlatformFactAttributes.cs، ApplicationsTests.cs، .github/workflows/ci.yml، PROGRESS.md.
- اختبار الأمان المضاف: لا تغيير منتج؛ تصنيف اختبار المنصة الصحيح.
- اختبار عدم الانكسار المضاف: كل حالات الاختبار الأصلية تبقى فعالة على Windows؛ الاختبار الخاص بمسارات Unix يبقى فعالًا على كل الأنظمة.
- ما لم يتم التحقق منه ولماذا: إعادة CI بعد الإصلاح مطلوبة قبل وصف الأنظمة الثلاثة بأنها ناجحة.
- اختلاف عن الخطة: خطوة إضافية لإصلاح فشل ظهر بالفعل في CI؛ لا إضعاف assertions ولا تغيير كود المنتج.

## Run 0 — خطة التنفيذ

- الطلب: تنفيذ خطة التأمين مع الحفاظ على بنية البرنامج وتصميمه وتوثيق كل خطوة.
- البداية: `master` عند `a12ad6b`، شجرة العمل نظيفة. جُلب فرع `origin/security/group1-sanitize-distribution` عند `e4d30c1` وتأكد وجود `3529dd7` قبله.
- أُنشئ فرع التكامل المحلي `security/hardening` من فرع التنظيف، ثم فرع العمل `security/run-0-baseline`. لم يحدث commit على `master`.
- ملفات الخطة الأربعة أضيفت من الأرشيف الذي قدمه صاحب المشروع بدون تعديل نص الخطة. إضافة الملفات جزء من تجهيز R0-S01 بإذن الطلب الحالي؛ لا نفترض أنها موجودة مسبقًا على GitHub.
- الخطوات: R0-S01 تجهيز الفروع والملفات؛ R0-S02 تسجيل baseline؛ R0-S03 جعل الاختبارات متعددة الأنظمة وعزل ملفات الاختبارات عن بيانات الخدمة؛ R0-S04 إضافة CI؛ R0-S05 تسجيل النتيجة وفتح PR ثم التوقف للمراجعة.
- ملفات التنفيذ: مشروع الاختبارات وخصائص الاختبار حسب النظام وتهيئة بيانات الاختبار و`.github/workflows/ci.yml` وسجل التقدم. لا تعديلات على كود المنتج أو الواجهة في هذا التشغيل.
- البيئة: Windows `10.0.26200`، SDK `10.0.202`، MSBuild `18.3.3`، .NET 8 runtime `8.0.26`. لا توجد بيئة Linux/macOS محلية.
- لا يتم تشغيل الخدمة أو الاتصال بمنصة الترخيص؛ اختبارات الترخيص تستخدم FakeGateway الموجود.
- ملاحظات مراجعة الخطة السابقة ستبقى نقاطًا يجب حسمها في Phase A للتشغيل المعني، وليست تعديلًا ضمن Run 0.

### R0-S01 — تجهيز أساس التنفيذ
- الحالة: DONE-VERIFIED
- ماذا تغيّر ولماذا: تم تجهيز فرع التكامل وفرع العمل من أساس التنظيف المحدد وإضافة ملفات التعليمات والخطة والتقدم والبرومبتات من أرشيف صاحب المشروع.
- الملفات: `AGENTS.md`، `docs/security/SECURITY_PLAN.md`، `docs/security/PROGRESS.md`، `docs/security/RUN_PROMPTS.md`.
- الأوامر التي نُفذت ونتيجتها الفعلية: `git status --short` أظهر شجرة نظيفة؛ `git ls-remote --heads origin security/group1-sanitize-distribution security/hardening` نجح بعد إعادة التنفيذ خارج البيئة المقيدة وأظهر فرع التنظيف فقط؛ `git fetch origin security/group1-sanitize-distribution` نجح؛ إنشاء الفرعين نجح؛ `git log -3 --oneline` أثبت `e4d30c1` و`3529dd7` و`a12ad6b`.
- اختبار الأمان المضاف: لا ينطبق؛ خطوة تجهيز فروع ووثائق بدون تغيير سلوك.
- اختبار عدم الانكسار المضاف: لا ينطبق؛ سيتم تسجيل البناء والاختبارات في R0-S02.
- ما لم يتم التحقق منه ولماذا: إلغاء سر الترخيص من المنصة لم يؤكده صاحب المشروع؛ F-01 يبقى Pending. فرع التكامل وPR لم يُنشرا بعد.
- اختلاف عن الخطة: الملفات لم تكن في المستودع، فأضيفت من الأرشيف المرفق بطلب التنفيذ. لم يُعاد إنشاء أو تغيير commits التنظيف.

### R0-S02 — تسجيل baseline قبل أي تغيير في الاختبارات
- الحالة: DONE-VERIFIED
- ماذا تغيّر ولماذا: تسجيل الحالة الفعلية لبناء واختبار الأساس المنظف قبل تغيير TFM أو إعداد CI. لم يتغير كود المنتج.
- الملفات: `docs/security/PROGRESS.md`.
- الأوامر التي نُفذت ونتيجتها الفعلية:
  - `dotnet --info`: المحاولة المقيدة فشلت جزئيًا برفض الوصول إلى Service Control Manager؛ إعادة التنفيذ خارج القيود نجحت (SDK 10.0.202، Windows win-x64، runtime 8.0.26).
  - `dotnet restore MonitorAgent.sln`: المحاولة المقيدة خرجت بالكود 1 بدون تشخيص؛ إعادة التنفيذ خارج القيود نجحت واستعادت المشاريع الأربعة.
  - `dotnet build MonitorAgent.sln -c Release`: نجح؛ 0 تحذيرات، 0 أخطاء؛ الزمن 00:01:27.47.
  - `dotnet test MonitorAgent.sln`: نجح؛ 86 ناجحًا، 0 فاشلًا، 0 متجاوزًا؛ مدة الاختبارات 21 ثانية؛ البناء الأصلي `net8.0-windows`.
- اختبار الأمان المضاف: لا ينطبق؛ baseline بالاختبارات الأصلية دون تغييرها.
- اختبار عدم الانكسار المضاف: تشغيل كل الاختبارات الأصلية قبل التنفيذ.
- ما لم يتم التحقق منه ولماذا: Linux/macOS لم يُختبرا؛ baseline لا يثبت إغلاق أي ثغرة. الاختبارات الأصلية تتضمن فحص هاردوير محلي وطلبات public-IP، واختبار التقارير يستخدم مسار البيانات الافتراضي؛ سيتم عزل مسار البيانات في R0-S03.
- اختلاف عن الخطة: لا شيء؛ مشاكل البيئة المقيدة موثقة منفصلة عن نتيجة إعادة التشغيل الناجحة.

### R0-S03 — تجهيز اختبارات الأنظمة الثلاثة وعزل بياناتها
- الحالة: DONE-UNVERIFIED
- ماذا تغيّر ولماذا: تغير هدف الاختبارات من `net8.0-windows` إلى `net8.0` مع بقاء كل الاختبارات الأصلية على Windows. أضيفت `WindowsFact` و`UnixFact`، ووُسمت الاختبارات الثمانية التي تنشئ WindowsInventoryCollector أو WindowsSystemProbe أو HardwareMonitorReader بأنها Windows-only. تهيئة الاختبارات تضبط `MONITORAGENT_HOME` و`SecretProtector.KeyFile` إلى مجلد مؤقت منفصل لكل testhost قبل تهيئة المسارات، وتضبطه 0700 على Unix؛ لا تُستخدم بيانات خدمة حقيقية ولا مفتاح root الافتراضي.
- الملفات: `tests/MonitorAgent.Tests/MonitorAgent.Tests.csproj`، `PlatformFactAttributes.cs`، `TestEnvironment.cs`، `TestEnvironmentTests.cs`، `SystemInfoServiceTests.cs`، `HardwareCardServiceTests.cs`.
- الأوامر التي نُفذت ونتيجتها الفعلية: البناء الأول نجح مع 7 تحذيرات CA1416؛ أضيفت annotations صحيحة للنظام بدون تعطيل التحليل. `dotnet build MonitorAgent.sln -c Release` بعدها نجح مع 0 تحذيرات و0 أخطاء. `dotnet test MonitorAgent.sln -c Release --no-build --no-restore` نجح: 87 ناجحًا، 0 فاشلًا، 1 متجاوزًا، 88 إجماليًا؛ المتجاوز هو اختبار mode على Unix، وكل الـ86 الأصليين نجحوا على Windows.
- اختبار الأمان المضاف: `StateAndReportsUseTheIsolatedTestDirectory` يثبت عزل كل مسارات البيانات والمفتاح؛ `TestDirectoryIsPrivateOnUnix` يثبت mode 0700 عند تشغيله على Unix.
- اختبار عدم الانكسار المضاف: إعادة تشغيل جميع الاختبارات الأصلية بعد تغيير الهدف؛ لم يُحذف أو يُضعف أي assertion.
- ما لم يتم التحقق منه ولماذا: Linux/macOS غير متاحين محليًا؛ CI في R0-S04 سيكشف نتيجتهما. اختبارات الهاردوير الأصلية لا تزال تفحص الجهاز المحلي وpublic-IP؛ تنظيف المجلد المؤقت best-effort عند إغلاق testhost لأن SQLite قد يحتفظ بملف مفتوح.
- اختلاف عن الخطة: عزل بيانات الاختبارات ضروري لتشغيلها على Unix بدون صلاحيات root ولمنع تعديل بيانات خدمة مثبتة؛ تعديل في harness فقط وليس في المنتج.

### R0-S04 — إضافة CI للأنظمة الثلاثة
- الحالة: DONE-UNVERIFIED
- ماذا تغيّر ولماذا: أضيف workflow يشغل restore وRelease build والاختبارات على windows-latest وubuntu-latest وmacos-latest عند PR أو push إلى security/** أو يدويًا. الصلاحيات `contents: read`، لا أسرار، ومهلة 20 دقيقة لكل job؛ فشل نظام لا يلغي بقية الأنظمة. تُرفع ملفات TRX لكل نظام للاطلاع على نتائج التجاوز والفشل.
- الملفات: `.github/workflows/ci.yml`، `docs/security/PROGRESS.md`.
- الأوامر التي نُفذت ونتيجتها الفعلية: `dotnet build MonitorAgent.sln -c Release --no-restore` فشل داخل القيود بدون تشخيص ثم نجح خارجها: 0 تحذيرات و0 أخطاء. `dotnet test MonitorAgent.sln -c Release --no-build --no-restore --logger trx --results-directory TestResults` نجح: 87 ناجحًا، 1 متجاوزًا، 0 فاشلًا، وأنتج ملف TRX. `git diff --check` نجح.
- اختبار الأمان المضاف: لا اختبار منتج؛ workflow لا يطلب أو يستخدم secrets ويمنح صلاحيات قراءة فقط.
- اختبار عدم الانكسار المضاف: نفس suite الأصلية وجميع الاختبارات المضافة تُشغل في matrix، وتم تنفيذ أمر CI فعليًا على Windows.
- ما لم يتم التحقق منه ولماذا: قبول ملف YAML وتشغيله على GitHub ونتائج Linux/macOS لم يتم التحقق منها بعد؛ لا نساوي قراءة workflow بنتيجة تشغيله.
- اختلاف عن الخطة: رفع TRX أضيف لتوفير دليل قابل للمراجعة. فحص الثغرات والحزم والأسرار ليس جزءًا من Run 0 وسيضاف في التشغيلات المحددة بالخطة.

### R0-S05 — نشر ملخص التشغيل وقائمة التحقق
- الحالة: DONE-VERIFIED
- ماذا تغيّر ولماذا: تحديث لوحة الحالة وبيئة التنفيذ والأسئلة المفتوحة، وإضافة ملخص التشغيل. نشر فرع التكامل عند أساس التنظيف وفرع العمل، وفتح PR للمراجعة بدل الدمج أو البدء في Run 1.
- الملفات: `docs/security/PROGRESS.md`.
- الأوامر التي نُفذت ونتيجتها الفعلية: `git push origin security/hardening security/run-0-baseline` نجح. إنشاء PR بواسطة موصل GitHub نجح: https://github.com/almasrymk/UBGMonitor/pull/1، head `security/run-0-baseline` وbase `security/hardening`. أُرفق PR بهذه المحادثة. أول فحص workflow للـcommit `7a9a39d` أظهر run `37754627205` بحالة queued بدون conclusion؛ لا نجاح مزعوم لـCI.
- اختبار الأمان المضاف: لا ينطبق؛ خطوة توثيق وتسليم.
- اختبار عدم الانكسار المضاف: لا تغييرات منتج إضافية؛ آخر build ناجح واختبارات 87 ناجحًا و1 متجاوزًا موثقة في R0-S04.
- ما لم يتم التحقق منه ولماذا: نتائج CI ومراجعة صاحب المشروع والدمج وإلغاء السر ليست مكتملة. GitHub CLI المحلي ليس مسجل الدخول؛ استخدم موصل GitHub المتاح لإنشاء PR بدون قراءة أي token.
- اختلاف عن الخطة: لا شيء في تسليم التشغيل؛ تفاصيل التجهيز من الأرشيف موثقة في R0-S01.

## ملخص Run 0

- R0-S01 وR0-S02 وR0-S05: DONE-VERIFIED. R0-S03 وR0-S04: DONE-UNVERIFIED على Linux/macOS وCI حتى ظهور نتيجة فعلية.
- baseline: 86 ناجحًا، 0 فاشلًا، 0 متجاوزًا. النتيجة بعد التعديل على Windows: 87 ناجحًا، 0 فاشلًا، 1 متجاوزًا، إجمالي 88.
- Release build النهائي: 0 أخطاء و0 تحذيرات. جميع اختبارات Windows الأصلية لا تزال تعمل.
- لا تغييرات على وظائف الخدمة أو تصميم الواجهة أو أسماء المنتج أو إعدادات عملاء حقيقيين؛ لم يتم تشغيل الخدمة أو الاتصال بمنصة الترخيص.
- التنظيف السابق `3529dd7` و`e4d30c1` هو أساس التشغيل؛ لا ندعي إنجاز Run 1 أو إغلاق F-01 حتى تأكيد الإلغاء على المنصة.
- PR: https://github.com/almasrymk/UBGMonitor/pull/1.

### قائمة تحقق صاحب المشروع قبل Run 1

- [ ] مراجعة تفاصيل PR وسجل كل خطوة.
- [ ] CI أخضر على Windows وLinux وmacOS؛ مراجعة أسباب التجاوز ونتائج TRX.
- [ ] تأكيد إلغاء سر الترخيص المكشوف ومراجعة استخدامه.
- [ ] دمج PR في `security/hardening` قبل بدء Run 1 كما تنص `RUN_PROMPTS.md`.
- [ ] حسم ملاحظات مراجعة تصميم Run 2/4 عند Phase A: سياسات LocalAdministrator، فرض TLS على النقاط الجديدة من الخدمة، واختبار اتصال TLS فعلي، وتفاصيل هوية IPC وسحب الصلاحيات.

## Run 5 — توسعة تسجيل الأجهزة حسب طلب صاحب المشروع (2026-10-08)

الطلب الأحدث يتقدم على طلب متابعة master السابق: لا تعديلات إضافية عليه، لا نشر تلقائي، لا إزالة Authentication، لا ClientSecret مشترك موزع، وتنفيذ الأجزاء الآمنة مع تسجيل ما يحتاج اعتمادًا. فروع العمل codex/security-hardening-enrollment وcodex/device-enrollment. D1=NO؛ D2/D3 غير معتمدين. توسعة enrollment مفوضة صراحة رغم قاعدة التوقف الأصلية في S04. الخطوات E01/E02/E03/E05 مترابطة داخل المنصة وجُمعت لأن استخدام الحقول بدون migration والفحص بدون اختبارات لا ينتج تغييرًا قابلًا للتسليم؛ E04/E05 مترابطتان داخل Agent.

### R5-E01 — نموذج الربط والترحيل
- الحالة: DONE-UNVERIFIED
- ماذا تغير ولماذا: ApiClient جديد مرتبط بجهاز وhash ترخيص وبنطاق activate/validate فقط؛ migration إضافية nullable لا تغير العملاء الموجودين. Design factory يمنع تشغيل host أو قراءة إعدادات النشر عند توليد migration.
- الملفات: ApiClient.cs، Configurations.cs، AppDbContextDesignFactory.cs، DeviceClientBindings migration/designer/snapshot داخل مستودع المنصة.
- الأوامر الفعلية: ef migrations add نجح؛ has-pending-model-changes نجح وأكد عدم وجود اختلاف؛ migrations script --idempotent نجح. SQL المولد يحتوي ADD للعمودين فقط داخل transaction مع migration history؛ لم يُطبق على قاعدة.
- اختبار الأمان: scope escalation للعميل المرتبط يُرفض؛ سر الاعتماد لا يساوي hash المخزن ولا يظهر في audit.
- اختبار عدم الانكسار: العملاء غير المرتبطين يعملون في اختبار legacy ودورة suite الأصلية.
- غير المتحقق: ترقية SQL Server قديمة فعلية؛ اختبارات المنصة تستخدم SQLite EnsureCreated. يلزم staging وbackup واعتماد migration.
- اختلاف عن الخطة: توسعة المنصة الجديدة مفوضة من المستخدم؛ لا pinning غير معتمد.

### R5-E02 — إصدار اعتماد إداري مستقل
- الحالة: DONE-VERIFIED
- ماذا تغير ولماذا: endpoint بصلاحيتَي إدارة الترخيص/API Client، default-off، tenant filtering، تحقق active license/device/name، no-store، secret مرة واحدة وhash فقط في DB.
- الملفات: ApiClientService.cs، BusinessControllers.cs، appsettings.json، DeviceEnrollmentTests.cs.
- الأوامر الفعلية: build Release نجح؛ suite المنصة النهائي 136 ناجحًا، 0 فاشلًا، 0 متجاوزًا.
- اختبار الأمان: anonymous/client forbidden، tenant mismatch لا يجد الترخيص، default-off، secrets فريدة وغير مسجلة، منع scopes زائدة.
- اختبار عدم الانكسار: enrollment لا يحجز activation، ودورة activate/validate/heartbeat/deactivate تعمل.
- غير المتحقق: صلاحيات النشر/قناة تسليم الاعتماد؛ لا endpoint anonymous ولا واجهة onboarding جديدة.
- اختلاف عن الخطة: تسجيل إداري يدوي، لا اعتماد bootstrap مشترك.

### R5-E03 — فرض الربط عند الاستخدام
- الحالة: DONE-VERIFIED
- ماذا تغير ولماذا: فحص client active وربطه بالجهاز والترخيص قبل الإجراءات الأربعة وقبل idempotency replay؛ يبقى legacy غير المرتبط متوافقًا. JWT العميل الملغى لا يصل لإعادة رد قديم.
- الملفات: DeviceLicensingService.cs، LicensingController.cs، DeviceEnrollmentTests.cs.
- الأوامر: suite المنصة النهائي 136 ناجحًا. أول build للاختبارات احتاج using إضافية؛ صُححت أخطاء compilation ولم تُضعف assertions.
- اختبار الأمان: تغيير deviceId/productKey يُرفض في الأربعة؛ revoke ثم replay يُرفض.
- اختبار عدم الانكسار: lifecycle للعميل الجديد وتفعيل legacy القديم ناجحان.
- غير المتحقق: اختبار staging/عملاء حقيقيين، لم يتم الاتصال بمسارات licensing الإنتاجية.
- اختلاف عن الخطة: لا شيء خارج توسعة التسجيل المصرح بها.

### R5-E04 — استهلاك اعتماد الجهاز في Agent
- الحالة: DONE-UNVERIFIED
- ماذا تغير ولماذا: opt-in runtime configuration منفصل للجهاز، مع تحقق device/product/HTTPS/platform قبل إرسال السر؛ لا legacy fallback عند mismatch ولا anonymous device request عند غياب/رفض token. خيارات legacy والتوكنات الحالية وoffline logic محفوظة.
- الملفات: LicensingOptions.cs، LicensePlatformClient.cs، DeviceEnrollmentClientTests.cs.
- الأوامر: dotnet build MonitorAgent.sln -c Release --no-restore نجح 0 أخطاء/0 تحذيرات؛ dotnet test -c Release --no-build --no-restore نجح 96/0 فشل/1 Unix skip، الإجمالي97.
- اختبار الأمان: mismatch يرسل صفر طلبات حتى مع legacy configured؛ refusal يرسل token request فقط؛ ToString لا يطبع secret؛ التشغيل يحتاج opt-in.
- اختبار عدم الانكسار: bearer token reuse ودورة الطلبات ومسار legacy ناجحة؛ جميع LicensingTests الأصلية ناجحة.
- غير المتحقق: حفظ دائم/تسليم آمن تحت حساب الخدمة يحتاج Run2/3/6؛ لا importer ولا LocalApi جديد ولا onboarding إنتاجي. Linux/macOS للتغيير الجديد غير مختبرين لأن النشر ممنوع.
- اختلاف عن الخطة: إعداد runtime اختياري قابل للتجربة بدل حذف auth؛ لا shared secret في التوزيع.

### R5-E05 — التحقق المشترك
- الحالة: DONE-VERIFIED
- الأوامر الفعلية: Release build للمشروعين ناجح؛ Agent97 إجمالي (96 pass/1skip)، المنصة136 pass. آخر build incremental للمنصة0 warning؛ build الكامل السابق أظهر CS0108 الموجودة مسبقًا في MediaController ولم تُعدل خارج النطاق. git diff --check للمشروعين ناجح. EF tools10.0.9 أظهر تنبيه إصدار مقابل runtime10.0.12 لكنه ولد migration/script بنجاح.
- التفاصيل والأدلة: DEVICE_ENROLLMENT.md؛ نتائج TRX وSQL داخل TestResults في كل مستودع. اختبارات المنصة HTTP TestServer/SQLite؛ Agent HTTP fake. لا دليل end-to-end فعلي بين binary Agent ومنصة SQL Server؛ لا ندعي ذلك.
- ملحوظة عامة: لم يُحذف اختبار ولم يتغير UI أو product identifiers؛ لا CI جديد بدون push.

### R5-S01/R5-S02/R5-S05 — بوابات الإنتاج
- الحالة: BLOCKED
- السبب: الدومين الدائم وJWK المستقلة لم تُعتمد؛ لن نثبت keys جُلبت من نفس الخادم كـtrust anchors ولن نغير التحقق الحالي بما يعطل license صالحًا. قواعد binding/offline/restart/rollback الأصلية ظلت ونجحت اختبارات Agent؛ نجاح pinning عبر ترقية حالية لا يمكن إثباته قبل D3.
- S04: توسعة E01–E04 هي البديل المفوض من المستخدم لـD1=NO؛ التنفيذ الآمن موجود والتفعيل الإنتاجي مشروط ببوابات موثقة.

### R5-S03/R5-S06 — توثيق التدوير وحدود الحماية
- الحالة: DONE-VERIFIED
- الملفات: LICENSING.md، DEVICE_ENROLLMENT.md، PROGRESS.md.
- توثيق ship-public-key-first ثم بدء توقيع المنصة، وعدم ادعاء أن ACL/encryption يحل key substitution أو binary patching. لا تدوير أو deploy فعلي.
- GET العام الوحيد المصرح به استخرج 4 JWK (active1/retiring3) وحفظ نسخة متجاهَلة unverified؛ لم تستخدم في code ولا config ولا tests كـtrust roots. الـSHA256 وموقعها في DEVICE_ENROLLMENT.md.

## ملخص التسليم المحلي لـRun 5

الأجزاء الآمنة وتوسعة التسجيل مجهزة ومختبرة محليًا؛ Run5 كامل وF18/F19 لم ينتهوا. لا push/PR/merge/deploy/migration إنتاج؛ master لم يتغير بعد طلب المستخدم الأحدث. Run1 يتوقف مؤقتًا عند S02، وباقي التشغيلات ليست منجزة. المطلوب قبل التفعيل: قرار registration UX، دومين/JWK معتمدان، staging migration/backup، حماية provisioning/state، تأكيد انتقال وإلغاء الاعتماد المكشوف، واعتماد الدمج والنشر منفصلين. لا حاجة لأي سر خاص في المحادثة.

## Run 1 — استئناف التنفيذ المحلي
طلب صاحب المشروع «كمل» يستأنف باقي خطوات Run1 المصرح بتنفيذها، ثم التحضير الفني للتشغيل التالي؛ كل العمل على codex/security-hardening-enrollment وبدون نشر. لا تغيير أسماء المنتج أو تصميمه.

### R1-S03 — التوجيه الآمن والإعدادات النظيفة
- الحالة: DONE-VERIFIED
- ماذا تغير ولماذا: Routing defaults فارغة في الكود والتوزيع؛ Madkhal/Central فارغان يبقيان بلا طلب أو log أو issue. Central يقبل HTTPS أو HTTP loopback فقط؛ القيمة غير الصالحة تحذر مرة واحدة دون عرض العنوان. DatabaseConnectionString الشبكية يتم تجاهلها واستخدام local.db في StateFolder دائمًا مع بقاء باقي الحقول.
- الملفات: RoutingOptions.cs، appsettings.json، MadkhalMonitor.cs، ConfigPuller.cs، RoutingSecurityTests.cs، DistributionSecurityTests.cs.
- الأوامر ونتائجها: Release build ناجح 0 أخطاء/0 تحذيرات؛ suite104 pass/1 Unix skip/0fail. أول build كشف اسم test logger متعارضًا مع Log؛ أُصلح ثم أُعيد البناء والاختبار ولم تُضعف assertions.
- اختبار الأمان: defaults فارغة؛ Empty_routing_is_silent؛ Unsafe_central_url_is_idle_and_warns_once؛ network DB path لا يطبق.
- اختبار عدم الانكسار: HTTPS وHTTP localhost/127.0.0.1 تحفظ fields؛ Madkhal المضبوط يحدّث الصحة بنجاح.
- غير المتحقق: لا خدمة عميل أو منصة إنتاج؛ اختبارات HTTP fake محلية؛ Linux/macOS الجديد لم يشغلا.
- اختلاف عن الخطة: رفض userinfo/query/fragment في base URL لتجنب التباس تركيب مسار config؛ غير المضبوط لا يعود لقيمة أضعف.

### R1-S04 — تثبيت نظيف بلا نقاط تجريبية
- الحالة: DONE-VERIFIED
- ماذا تغير ولماذا: CreateDefault بلا monitor points. cache يحتوي فقط IDs العينات الحالية يتحول إلى قائمة فارغة؛ مسار UpgradeLegacySample السابق يستمر دون إعادة إضافة العينات. الإعدادات/الحدود/هوية/version/DB الحالية تحفظ. أضيفت مسارات اختيارية لاختبار cache/settings في ملفات مؤقتة دون تغيير مسارات المنتج الافتراضية.
- الملفات: LocalConfigCache.cs، LocalConfigCacheSecurityTests.cs.
- الأوامر الفعلية: اختبار reproducer قبل الإصلاح4 حالات:3 فشل و1 نجاح (التثبيت النظيف والعينات القديمة/الحالية أعادت نقاطًا). بعد الإصلاح Release build0 أخطاء/0تحذيرات؛ suite108 pass/1skip/0fail.
- اختبار الأمان: Clean_install_without_Setting_has_zero_monitor_points؛ إزالة sample-only cache وثباتها عند إعادة القراءة.
- اختبار عدم الانكسار: Real_monitor_points_and_existing_settings_survive_cache_reload يحافظ على النقطة الحقيقية وDB/version/address؛ migration تحفظ الهوية والحدود.
- غير المتحقق: تثبيت VM حقيقي غير منفذ؛ الاختبارات تستعمل مجلدات مؤقتة؛ legacy camera rule القديمة ظلت كما كانت.
- اختلاف عن الخطة: dependency paths اختيارية لعزل الاختبارات فقط؛ samples لا تحذف من قائمة مخلوطة بنقاط عميل حقيقية.

### R1-S05 — منع كتابة نسخ التطوير في Release
- الحالة: DONE-VERIFIED
- ماذا تغير ولماذا: DevelopmentCopies والنداء لها داخل DEBUG فقط؛ Release لا تحمل هذه الدالة وتكتب PrimaryPath فقط. Debug يبقى على السلوك التطويري السابق.
- الملفات: ServiceSettingsFile.cs، ReleaseSettingsTests.cs.
- الأوامر الفعلية: build Release0 أخطاء/0 تحذيرات؛ suite109pass/1skip/0fail.
- اختبار الأمان: reflection يثبت غياب DevelopmentCopies من assembly Release.
- اختبار عدم الانكسار: كتابة/قراءة settings الأساسية تنجح مع حفظ واسترجاع نسخة ملف الاختبار الأصلية في finally؛ لا لمس إعدادات عميل.
- غير المتحقق: تثبيت VM غير منفذ؛ الاختبار Release-only مقصود، Debug لم يُختبر في هذه الخطوة.
- اختلاف عن الخطة: لا شيء.

### R1-S06 — تجاهل ملفات الأسرار والحالة والتوزيع
- الحالة: DONE-VERIFIED
- ماذا تغير ولماذا: منع publish/ وcert/key/state/local-config من الإضافة العرضية. Data/ وReports/ مربوطتان بجذر المستودع حتى لا تخفي مجلدات Reports التي تحتوي كودًا حقيقيًا.
- الملفات: .gitignore.
- الأوامر الفعلية: git check-ignore نجح لـ11 مسارًا تمثيليًا؛ git ls-files -ci --exclude-standard صفر؛ build Release0 أخطاء/0تحذيرات؛ suite109pass/1skip/0fail.
- اختبار الأمان: أسماء secret.key وlicense/client JSON وpfx/p12/snk/key وlocal appsettings متجاهلة حتى لو غير موجودة.
- اختبار عدم الانكسار: لا source متتبع أصبح ignored؛ مجلدات Reports المصدر ما زالت متتبعة.
- غير المتحقق: ignore ليس منعًا لـgit add -f ولا بديلًا عن scanning.
- اختلاف عن الخطة: root anchoring لبيانات Reports/Data ضروري بسبب وجود source directories بنفس الاسم.

### R1-S07 — فحص الأسرار في المصدر والتوزيع
- الحالة: DONE-UNVERIFIED
- ماذا تغير ولماذا: job مستقل يستخدم Gitleaks8.30.1 binary مجاني مع SHA256 ثابت؛ PR range من merge-base إلى head، push range من before إلى head، manual/new branch آخر commit. فحص التوزيع بعد publish على OS matrix؛ فاحص PowerShell7 يرفض credentials غير الفارغة/Setting/Ui/MonitorPoints والـblobs في كل appsettings*.json ويطبع file/field فقط.
- الملفات: .gitleaks.toml، ci.yml، check-release-secrets.ps1، test-release-secrets.ps1.
- الأوامر الفعلية: إصدار Gitleaks الرسمي والتحقق من checksum نجح؛ git scan e4d30c1..HEAD وstaged scan code0. throwaway repository به credential وهمي في commit ثم scanner بنفس config/flags code1؛ rule monitoragent-licensing-credential في fixture.json. أزيل repo المؤقت بعد التحقق من مسار cleanup؛ تاريخ المنتج لم يُعد كتابته. فاحص publish المحلي نجح2 settings files. self-tests9 حالات نجحت، ومنها secret في ملف environment إضافي، والقيم لا تظهر في المخرجات. build0 أخطاء/0تحذيرات؛ suite109pass/1skip/0fail؛ diff check ناجح.
- اختبار الأمان: scanners ترفض fake secret، protected blob، nested password/key، Setting/MonitorPoints وJSON غير صالح؛ gitleaks default rules مفعلة مع قواعد lcs/lc وdpapi/aes JSON.
- اختبار عدم الانكسار: clean settings مقبولة؛ نشر Service Release وفحص إعداداته نجح؛ suite كامل.
- غير المتحقق: CI المعدلة لم تُنشر حسب أمر المستخدم، فلا ادعاء بنجاح GitHub job/Unix PowerShell. المسح لا يدعي تنظيف التاريخ كله. SHA القديم0c66b6a مستثنى فقط، لا secret-value allowlist؛ الإلغاء الإداري يظل Pending.
- اختلاف عن الخطة: إثبات رفض commit تم محليًا بنفس binary/flags في repo مؤقت مستقل بدل push؛ لا CI حقيقية بدون نشر. scan manual آخر commit لتجنب مسح تاريخ التسريب القديم غير المعالج بالكامل.
- مرجع الأداة: https://github.com/gitleaks/gitleaks (الوثائق الأصلية؛ download8.30.1/checksums الرسمية).

### R1-S08 — إجراءات المالك وتسليم Run1 محليًا
- الحالة: DONE-VERIFIED
- ماذا تغير ولماذا: checklist للإلغاء والانتقال الآمن ومراجعة الاستخدام/visibility/history/secret scanning/push protection، بالإضافة إلى خطوات staging واعتماد Run5؛ بلا secret values.
- الملفات: OWNER_ACTIONS.md وPROGRESS.md.
- الأوامر الفعلية: build Release0أخطاء/0تحذيرات؛ suite109pass/1skip/0fail؛ لا إجراءات إدارية على المنصة أو GitHub.
- اختبار الأمان وعدم الانكسار: لا تغيير منتج في الخطوة؛ suite كامل ناجح. تنفيذ checklist نفسه Pending عند المالك.
- غير المتحقق: إلغاء السر المكشوف ومراجعة logs وصلاحيات مستودع GitHub؛ لا يدعي التوثيق تنفيذها.
- اختلاف عن الخطة: إضافة بوابات enrollment الجديدة المصرح بها إلى نفس checklist.

## ملخص Run 1 الحالي
S01–S06 وS08 DONE-VERIFIED محليًا؛ S07 DONE-UNVERIFIED في CI/Unix مع إثبات رفض fake credential محليًا. defaults بلا بيانات شخصية/secret، cache الجديد صفر points، routing الفارغ صامت، network DB path مهملة، Release بلا development copies، المصدر لا يتتبع state/cert، فحص publish2 ملفات ناجح/self-tests9 حالات. F01 Pending لإلغاء credential خارج المستودع. لا push/PR/merge/deploy حسب أمر المستخدم؛ يلزم clean-VM/upgrade/OS matrix قبل إصدار.

## Run 2 — خطة التنفيذ (Phase A)

### R2-S02 — تجربة النقل المحلي قبل تغيير المنتج
- الحالة: DONE-UNVERIFIED
- الأوامر الفعلية: dotnet run --project .security-work/ipc-poc/IpcPoc.csproj -c Release نجح على Windows/.NET8:8 checks. التجربة خارج المنتج بأسماء pipes فريدة وACL للحساب التجريبي فقط، لا تثبيت خدمة ولا تعديل groups ولا endpoints الإنتاجية.
- ما ثبت: GET عبر HttpClient/NamedPipeClientStream؛ PUT JSON64KiB؛ report1MiB؛ مضيفان Viewer/Admin؛ Viewer PUT403 حتى مع role header مزور؛ قراءة owner ترفض pipe المملوكة لحساب المستخدم مقابل SYSTEM/Administrators؛ Kestrel FirstPipeInstance يرفض اسمًا محجوزًا عند start.
- ما لم يثبت: مستخدم standard مستقل/عضوية custom groups غير مرفوعة؛ قبول endpoint حقيقية يملكها SYSTEM؛ Unix socket/root:group/modes/macOS؛ تطبيق production client ownership check. لا ندعي أن PoC الحساب الحالي يثبت access denial لحساب آخر.
- مراجع الفحص: local ASP.NET8 reference XML أكد PipeSecurity global options؛ المصدر الرسمي للنسخة8.0.26 أكد FirstPipeInstance والحفاظ على instance قبل تسليم connection: https://raw.githubusercontent.com/dotnet/aspnetcore/v8.0.26/src/Servers/Kestrel/Transport.NamedPipes/src/Internal/NamedPipeConnectionListener.cs .
- اختلاف مؤثر: .NET8 لا توفر per-endpoint CreateNamedPipeServerStream callback الموجودة في runtime10؛ لا ترقية runtime. نستخدم WebApplication منفصلة لكل local role، وهو بديل مسموح في6.1. ACL production تمنح ReadWrite+ReadPermissions للمجموعات حتى يستطيع العميل قراءة owner، ولا FullControl؛ FullControl في PoC للحساب التجريبي فقط.

### خطوات Phase B المقترحة وترتيبها
1. R2-S01: استخراج بناء pipeline/map من LocalApiHost إلى host قابل للاختبار دون تغيير العقود؛ characterization لكل route من ApiRoutes ومخرجاتها وlicense-required وأخطاء reports/hardware/processes. fake services وsettings مؤقتة بلا HTTP خارجي أو DB عميل. حزمة اختبار جديدة متوقعة Microsoft.AspNetCore.TestHost8.0.26 (مجانية، test-only) إن لم تكفِ Kestrel IPC tests؛ لا dependency منتج جديدة للنقل لأن NamedPipes ضمن ASP.NET8 framework.
2. R2-S03: Role metadata Viewer/Admin صريحة لكل route؛ deny default وtest enumerates endpoint data sources. license gate بعد auth/role؛ OpenWithoutLicense لا تعني anonymous.
3. R2-S04/R2-S06: service IPC ومصدر الدور من المضيف/client transport يتغيران معًا حتى لا تنقطع الواجهة أثناء الانتقال. Windows ACL protected/SIDs، missing group fail-closed SYSTEM/elevated admin، owner check قبل HTTP. Unix socket في root-owned directory وcreate modes آمنة؛ لا role من header/IP. admin-first/viewer fallback فقط عند رفض صلاحية admin، وليس عند فشل ownership check أو خطأ أمني. expose role/local transport كحالة اتصال؛ لا IsLocal كإذن لفك الأسرار.
4. R2-S05: تعديل install Windows/Linux/macOS/deb/pkg لإنشاء groups المطلوبة وشرح sign-out؛ حفظ groups عند uninstall العادي؛ runtime dir حسب النظام. لا تشغيل installer هنا. Unix أمين:0660 يمكنه منح group واحدة؛ مستخدم الإدارة يجب أن يكون عضوًا في admin+viewer للحصول على الوصول لقناتَي الجدول، أو نحتاج POSIX ACL policy مستقلة. يلزم حسم العضوية المشتركة عند التثبيت/توثيق إضافة الأعضاء قبل التنفيذ، ولا افتراض أن Unix تدعم nested groups.
5. R2-S07: DTO settings whitelist field-by-field وhasPassword/hasRemoteAccessKey؛ لا secrets/blobs. validate كامل قبل write، ثم merge server-side فقط عند identity/target unchanged. تشمل target identity Engine/Server/Port/Database/Username/IntegratedSecurity حتى لا يُحوّل password لسياق مختلف. legacy key محفوظ ولا يقبل تغييره من PUT. UI password box فارغة وnull/absent تعني keep؛ مسار إدخال جديد plain over admin IPC دون Desktop decrypt. حدود مستمدة من controls الحالية ومحددة في validator tests؛ ServicePort0 القديمة تُعرض كeffective default ضمن ترقية الإدخال ولا تُرفض ملفات legacy عند مجرد قراءتها.
6. R2-S08: typed request monitorPointId/login؛ resolving stored secret داخل الخدمة ومقارنة target قبل use؛ reject protected client input؛ response عام لا driver exception/secret. tests لا اتصال قواعد حقيقية.
7. R2-S09: TCP viewer-only/must-key حتى loopback؛ لا remote listener بلا key؛ Host allowlist/remove CORS؛ لا fallback listener أضعف. TLS/hash/admin remote فيRun4؛ لا نعلن remote جاهزًا قبلها.
8. R2-S10: URLs http/https فقط؛ applications من خدمة محلية وعنوان absolute local موجود، confirmation per-user؛ UNC مرفوض؛ reveal محلي فقط. لا تعديل شكل الواجهات أو أسماء المنتج.
9. R2-S11: تحديث README/الدور/مجموعات OS وowner test matrix؛ إزالة curl loopback/CORS القديمة، توثيق failure messages وعدم الخلط بين access-denied/service down.

### الملفات المتوقع تغييرها
Service LocalApiHost/LocalApi pipeline/policies/IPC + Config settings writer/validation + DatabaseMonitor test boundary؛ Shared DTO/contracts؛ Desktop AgentApiClient/AppSettingsStore/SettingsViewModel/DatabaseLoginWindow/MonitorPointLauncher/ShellOpen + controls bindings للأوامر الإدارية؛ scripts install/uninstall/unit/pkg/deb؛ tests characterization/security/regression/Windows transport/Unix transport؛ README وsecurity docs. لا تغييرات على sensor backend أو reports schemas أو تراخيص الإنتاج.

### الاختبارات وبوابات التنفيذ
- كل route policy؛ Viewer403 لكل admin action؛ unlicensed contract محفوظ؛ status/read routes shapes ثابتة باستثناء settings DTO المقصود.
- settings GET لا secrets/blobs، كل input malformed/out-of-range لا write؛ target change بلا password400؛ new database point يعمل وuntouched stored credential يبقى؛ database test protected blob مرفوض.
- TCP missing key401 حتى loopback/bodyless POST؛ Host mismatch rejected؛ zero CORS.
- launch file/javascript/custom/UNC ممنوع؛ remote app/reveal ممنوع.
- Windows namedpipe owner/first instance/GET/PUT/report وACL cross-account؛ Unix root-owned dir/socket/0660 واستثناء symlinks/stale files دون حذف مسار مهاجم؛ قياسات حقيقية على OS المقصود فقط.
- عضوية وإدارة groups تحت حساب عادي وroot/SYSTEM، login refresh/sign-out، وDesktop end-to-end تحتاج VM/أنظمة حقيقية. CI الجديدة ما زالت محلية بدون push.

### حالة Phase A
خطة التنفيذ وتجربة Windows الجزئية مكتملتان. Run2 security fixes لم تنفذ بعد ولا ثغرة API مغلقة بمجرد PoC. بوابة0.3 تقول: "Do not write product code until the owner starts Phase B." لا deploy أو تغييرات حسابات/خدمة من Phase A. توضيح المرحلة للمراجعة قبل الانتقال لتنفيذ التغيير الذي يبدل الاتصال والصلاحيات.

## تفويض الاستمرار دون توقف بين المراحل
طلب صاحب المشروع تنفيذ كل ما يمكن إنجازه من الكود دون التوقف عند بوابات المراحل. هذا يفوض Phase B وعمليات Run التالية المحلية، ويتقدم على توقف0.3/PR لكل Run. تبقى بيانات الإنتاج الناقصة والتحقق على أنظمة غير متاحة موثقة، ولا نشر أو master أو تشغيل installer/customer service.

### R2-S01 — جعل Pipeline قابلة للاختبار
- الحالة: DONE-UNVERIFIED
- الملفات: LocalApiHost.cs، LocalApiSecurityTests.cs، مشروع الاختبارات.
- استخراج ConfigureServices/ConfigureApplication دون تغيير السلوك؛ TestHost8.0.26 test-only المذكورة في Phase A. اختبارات enumerate كل route غير مفتوحة بدون license وتثبت403/code، status/license JSON shape ومسارات invalid queries.
- restore/build0 أخطاء/0 تحذيرات؛ suite112pass/1skip/0fail. فحوص القراءة الناجحة لكل telemetry/report route ليست مكتملة بعد؛ سنوسعها خلال التنفيذ، فلا ادعاء بكامل characterization net.

### R2-S03 — سياسة صريحة لكل مسار
- الحالة: DONE-VERIFIED
- التغيير: metadata Viewer/Administrator لكل route، رفض المسارات بلا سياسة، role من المضيف فقط؛ license gate بعدها. Default role=None حتى لا يمنح استدعاء pipeline غير مضبوط إدارة.
- الملفات: LocalApiHost، LocalIpc، LocalApiSecurityTests.
- التحقق: Release build صفر أخطاء/تحذيرات؛ 129pass/1skip/0fail. Every_route_has_a_policy_and_viewers_cannot_run_administrator_actions يعدد السياسات ويرفض كل POST/PUT الإداري حتى مع role header مزور. Status/license regression وlicense-required ناجحة.
- غير المتحقق: characterization لكل telemetry/report ناجح ما زال مطلوبًا؛ S01 لا يصبح verified بهذا الاختبار وحده.

### R2-S04 — قنوات IPC والخدمة
- الحالة: DONE-UNVERIFIED
- التغيير: مضيف لكل role، namedpipe ACL protected يملكها Administrators مع SYSTEM/group SID؛ Unix root-owned socket و0755 directory، إنشاء0077 ثم0660. رفض symlink/regular file/live endpoint عند تنظيف stale socket، حذف فقط بعد ConnectionRefused. لا HTTP loopback محلي.
- الملفات: IpcHostConfiguration، LocalIpc، LocalApiHost.
- التحقق: build/suite أعلاه؛ Windows PoC السابق8 checks. لا تشغيل service/install أو تغيير groups هنا.
- غير المتحقق: قبول SYSTEM endpoint وACL cross-account وUnix root/socket/end-to-end تحتاج أجهزة حقيقية؛ لا ادعاء إثباتها من TestServer.

### R2-S05 — مجموعات التثبيت
- الحالة: DONE-UNVERIFIED
- التغيير: Windows ينشئ Admins/Viewers ويضيف installing user؛ Linux/deb وmacOS/pkg ينشئان المجموعتين، SUDO_USER ينضم إليهما لتفادي غياب nested groups. systemd RuntimeDirectory0755. uninstall العادي يبقي groups؛ purge يحذفها. رسالة sign-out/in.
- الملفات: install/uninstall Windows/Linux/macOS وdeb/postinst/postrm وbuild-pkg/unit.
- التحقق: build/suite ناجحان؛ scripts لم تُنفذ لأنها تثبت خدمة وتغير حسابات الجهاز.
- غير المتحقق: syntax/runtime Unix/package وstandard-user membership؛ لا claims عن تثبيت فعلي.

### R2-S06 — عميل IPC وصلاحية الواجهة
- الحالة: DONE-UNVERIFIED
- التغيير: loopback preferences تعني IPC، admin-first/viewer fallback عند access/unavailable فقط؛ SecurityException لا تسمح fallback. يتحقق owner SYSTEM/Administrators أوroot قبل HTTP. exposing Role/CanAdminister/IsLocalTransport؛ Settings read-only وDB configure/license/speedtest guards، خطأ access denied منفصل عن توقف الخدمة.
- الملفات: AgentApiClient وSettings/License/Network/MainViewModels وSettingsView.
- التحقق: build0/0 وسuite129pass/1skip؛ اختبارات role service ناجحة.
- غير المتحقق: Desktop UI تفاعلية وlogin/ACL على الأجهزة؛ رسالة endpoint identity وUnix fail-closed تحتاج owner matrix.

### R2-S07 — عقد إعدادات بلا أسرار
- الحالة: DONE-VERIFIED
- التغيير: whitelist typed roots، حذف password/key وextensible metadata من GET وإرجاع flags؛ metadata محفوظة داخليًا عند تعديل DB العادي. رفض protected input، تحقق IPv4/port/interval/retention/IDs/types/length/icon/count قبل write. المفتاح القديم محفوظ وغير قابل للتغيير عبر PUT. password absent أو HasPassword مع empty يحتفظ به فقط إذا كامل target unchanged، وإلا400؛ الواجهة لا تفك password ولا ترسل stored blob.
- الملفات: SettingsContract، DatabaseLogin، DatabaseLoginWindow، LocalApiHost، SettingsContractTests.
- التحقق: public-secret exclusion، ordinary edit preservation، تغيير server/user/database مرفوض، plain password جديد مقبول، invalid ranges لا تغير الأصل؛ build0/0 وسuite129pass/1skip.
- اختلاف: interval1..86400 وspeed0..86400 حدود صريحة لأن controls السابقة لم تفرض Max/Min لكل حقل؛ ServicePort0 legacy محفوظ. أسرار disk ما زالت F15/F16 حتى Run3/6، فلا نعلن إغلاقها نهائيًا.

### R2-S08 — إغلاق اختبار فك كلمة المرور
- الحالة: DONE-VERIFIED
- التغيير: typed monitorPointId/login، resolving inside service؛ protected input مرفوض، target identity كاملة عند reuse، driver failure لا يُرجع للعميل ولا يُسجل؛ النتائج العامة تحافظ على success/message.
- الملفات: DatabaseTestRequest، DatabaseTestResolver، API/client/window/tests.
- التحقق: نفس tests ترفض protected/redirect وتسمح stored-ID/plain new بدون اتصال قاعدة بيانات حقيقية؛ license action regression ناجح. build/suite أعلاه.
- غير المتحقق: live DB customer غير متصل عمدًا.

### R2-S09 — حماية TCP والمتصفح
- الحالة: DONE-VERIFIED
- التغيير: key مطلوب حتى loopback وbodyless POST، Host allowlist، zero CORS، Viewer-only TCP، listener لا يبدأ بدون key. TLS/hash في Run4 فلا remote production-ready الآن.
- التحقق: Tcp_checks_key_even_on_loopback_and_rejects_wrong_host_and_bodyless_posts يثبت401/400/403 وGET الصحيح200؛ build/suite أعلاه.

### R2-S10 — فتح العناوين والبرامج
- الحالة: DONE-UNVERIFIED
- التغيير: http/https فقط، Application يحتاج local transport وabsolute existing local-drive path (رفض UNC/network drives) وتأكيد كامل المسار محفوظ بملف خاص بالمستخدم؛ remote process reveal ممنوع. فتح DB لا يفك/ينسخ password؛ integrated security محفوظة.
- الملفات: MonitorPointLauncher، ApplicationLaunchApprovals، MainViewModel، ProcessListCardViewModel، tests.
- التحقق: unsafe scheme/remote application/UNC رفض ناجح؛ build/suite أعلاه. لا تشغيل executable أو متصفح من الاختبارات.
- غير المتحقق: confirmation dialog وlaunch/clipboard/database-tool end-to-end تحتاج UI owner.

### R2-S11 — توثيق الاتصال والصلاحيات
- الحالة: DONE-VERIFIED
- README أزال curl/CORS السابقين وشرح IPC/groups/role/sign-out. توثيق install القديم سيستبدل فيRun3.
- جمع S03–S11 في commit واحد: تغيير النقل وعقد password/policies وعميل الواجهة يجب أن يصل للطرفين معًا؛ فصلها كان يترك عميلًا يرسل blobs أو loopback مجهولًا بعد تغيير الخدمة. كل خطوة موثقة مستقلة؛ لم تُغير master ولم تُنشر تغييرات.

## Run 3 — خطة التنفيذ
المضي بتفويض المستخدم السابق: State settings/data/report منفصلة عن install، Atomic owner-only writes وmigration backup قبل أي تغيير؛ temp-folder tests fresh/upgrade/re-run/interrupted وحماية logging config. Windows installer prebuilt/default ProgramFiles وACL/check/source switch/desktop؛ Unix ownership/modes/legacy snapshot بدل merge. Verification scripts وREADME. لا تشغيل installer على الجهاز الحالي؛ تحقق أجهزة OS/customer يظل DONE-UNVERIFIED.

### R3-S01 — فصل install/state
- الحالة: DONE-VERIFIED
- الملفات: AgentPaths، ServiceSettingsFile، LocalConfigCache، Program، PrivateFile.
- التغيير: Data/Reports/settings.json داخل state على كل OS وMONITORAGENT_HOME محفوظ؛ ملفات install لا تكتبها الخدمة. settings.json direct data-root لا تدخل IConfiguration/Serilog. atomic replacement مع Flush(true)، Unix0600 creation/0700 parent دون الاعتماد على umask. لم يبق نداء development writer حتىDebug.
- التحقق: Release build0أخطاء/0تحذيرات؛ suite133pass/2skip/0fail. HTTP settings roundtrip ناجح وinvalid PUT لا يكتب؛ tests تستخدم state مؤقتة فقط. test host استكمل ReportStore وfake notification/logger بعد أن كشف roundtrip الأول نقص DI؛ لم يُضعف test لإخفاء الفشل.
- غير المتحقق: installer ACL protection وحساب LocalSystem لا تثبتها temp-folder tests.

### R3-S02 — الترحيل والنسخ الاحتياطية
- الحالة: DONE-UNVERIFIED
- التغيير: legacy snapshot/publish/appsettings Setting أوUi ->settings.json فقط إن غير موجود؛ backups timestamp+GUID قبل نقل/كتابة. Windows Data/Reports per-file streaming/durable copy ثمdelete؛ conflict يحفظ النسختين ويوقف؛ partial identical destination يستكمل. license/config/history الأخرى لا تُمس. successful layout marker فقط بعد انتهاء النقل.
- الملفات: StateMigration، PrivateFile، StateMigrationTests، Program.
- التحقق: upgrade/password/license/history/idempotence، fresh install، existing-settings priority، interrupted copy/conflict نجحت؛ suite133pass/2skip. ملفات logging legacy لا تنتقل لعقد settings. Unix owner-only migration test موجود وتخطى على Windows.
- غير المتحقق: نسخة DB كبيرة/real customer upgrade/Unix permissions والـowner verification. لا migration على installation حقيقية.

### R3-S03 — تثبيت Windows من حزمة وحماية ACL
- الحالة: DONE-UNVERIFIED
- التغيير: -Source prebuilt، -BuildFromSource opt-in، -DesktopSource؛ ProgramFiles Service/Desktop وquoted service binary path. stateSYSTEM/Administrators فقط مع protected inheritance علىtree؛ binaries read-onlyUsers وreject reparse points؛ verifier قبلstart؛ حذفpublish القديم فقط بعد migration marker. uninstall يغلق MonitorAgent API ويحتفظ بالبيانات إلاPurge validated absolute target.
- الملفات: install/uninstall-service.ps1، verify-permissions.ps1.
- التحقق: PowerShell parser ناجح، build/suite أعلاه. Script mutation لم تُنفذ؛ service manager/groups/ACL/customer folders غير ملموسة.
- اختلاف: installer يطلب elevated shell بدل self-elevation الذي يفقد parameters؛ fail clear message. legacy ClientAgent rename قديم لا يُستحدث؛ المنتج الحالي لا يُعاد تسميته.

### R3-S04 — Linux ownership/state/layout
- الحالة: DONE-UNVERIFIED
- التغيير: root:root binaries/Desktop، go+rX/go-w؛ state0700/log0750، legacy snapshot0600 بدل Python merge؛ debpreinst/postinst يحفظان migration source؛ unitStateDirectoryMode0700/LogsDirectoryMode0750/UMask0077 معUserroot كماهو.
- التحقق: bash -n عبرGit Bash ناجح install/uninstall/debpreinst/postinst/postrm؛ .NET build/tests أعلاه. لم تُثبت حزمة أوsystemd ولم تُغير firewall.
- غير المتحقق: Linux runtime/install/owner matrix.

### R3-S05 — macOS ownership/state/layout
- الحالة: DONE-UNVERIFIED
- التغيير: root:wheel/go-w وstate0700/log0750؛ snapshot0600 بدلmerge فيtar/pkg؛ launchdplist root:wheel0644. signing/quarantine القديمة لم تُغير حسبRun7 documentation gate.
- التحقق: bash -n install/uninstall/build-pkg ناجح؛ build/suite أعلاه. لاMac أوlaunchd/package هنا.

### R3-S06 — سكربتات تحقق الصلاحيات
- الحالة: DONE-UNVERIFIED
- الملفات: verify-permissions.ps1/.sh.
- التغيير: PASS/FAIL لكلpath؛ منعreparse/symlink وowners غيرالموثوقين وwrite grants للـinstall/read-write state. Unixroot وموداتstateوالbinary تفحص؛ nonzero عندالفشل.
- التحقق: syntaxPowerShell/Bash ناجح فقط؛ لاادعاءPASS بعدfresh/upgrade لأن installer لمينفذ.

### R3-S07 — توثيق install/migration
- الحالة: DONE-VERIFIED
- README يوضحprebuilt paths وdata-onlysettings/backups/verification/purge. build0/0وسuite133pass/2skip.
- جمع S01–S07 لأن writer/location/legacy snapshot/installer cleanup عقود مترابطة؛ فصل install القديم عن writerالجديد كان يعيدإدخالsettingsداخلbinaryfolder أويفقدmigration source. لاpush أوmaster.

## Run 4 — خطة التنفيذ
تنفيذ مستقل محليًا: TLS-mode perDB أولًا معlegacyCompatibility وnewVerify وتحذيرظاهروtest-withverification؛ ثمremoteHTTPS/cert/keyhash/localadminenable/firewallopt-in/limits وDesktoppairing. certificates self-generatedper-install لاproductionlicensinganchors؛ لاكتابةPFX/secretللمصدر؛ اختباراتTLSconnectionbuilders/httpfakes/tempstate، liveDB/remotePCوتأكيدfingerprint الحقيقيowner-verification. Run5D2/D3 يبقيانBLOCKED لحيناعتمادإداري.

### R4-S06 — TLS لكل نقطة قاعدة بيانات
- الحالة: DONE-UNVERIFIED
- التغيير: TlsMode absent=Compatibility، new UI connection=Verify؛ SQLMandatory/TrustServerCertificatefalse وNpgsql/MySqlVerifyFull. Compatibility يحافظ علىdriverdefaults السابقة بلاauto downgrade؛ settings summary/list target/dashboard label وتحذيرWarning واحد أسماءالنقاط يتحدث فقط عندتغيرالقائمة. DTOيعرضTlsMode. اختيارCompatibility يحذر؛ Test with verification يستعملstored loginداخلservice ولايغيرالمحفوظ، ويعرضاختيارVerifyبعدالنجاح. low-privilege hint وDATABASE_TLS.md.
- الملفات: DatabaseLogin/TestRequest/Resolver/Monitor/SettingsContract/StatusDto/StatusBuilder/MonitorPointText؛ Desktopwindow/client/rows/dashboard؛ DatabaseTlsTests.
- التحقق: buildRelease0أخطاء/0تحذيرات؛ suite138pass/2skip/0fail. TLSbuilder3enginesيثبتverifyوالـlegacydefaults؛ verifytestيحفظstoredmode/password؛ Warningrecordonce/clearبعداختيارVerify ناجح؛ unsafeinput tests مستمرة. أولfixtureNpgsqlبلاhostرفضهاdriverقبلفتحأيconnection؛ أضيفhostfixture.test ثمsuiteنجحت.
- غيرالمتحقق: شهادةDBحية وUI/dialog/card علىالأجهزة؛ لذلكلاتوصفنقاطCompatibilityFixed. No customer/network DBconnections.
- اختلاف: trusted-root-fileاختياراختياري لمينفذ؛ systemtruststoreموثقومشتركبينdrivers.

## Run 6 — خطة التنفيذ
سننجز scope/key/state foundations قبلremotecert storage لأنRun2/3متوفران: WindowsCurrentUsernewprefixمعlegacyread/backup-on-migration، Desktopownprefsفقط، Unixkeyowner/mode/parent/no-symlink/create-new، owner-onlystatewrites/SQLitepermissions؛ سپسredaction/audit/reveal-explicitclipboard. لا تغييرserviceaccountولاproductionstate؛ testsWindowsprofileفقطلاتثبتLocalSystemcross-account. لاDBencryptiondependency؛ توثيقschemasوالقرار.

### R6-S01 — أسرار الخدمة بحساب الخدمة
- الحالة: DONE-UNVERIFIED
- التغيير: newdpapi2CurrentUser؛ LocalSystemعندتشغيلالخدمةفعليًا. legacydpapiReadableثمReprotectعلىsettings/licensefirstreadبـbackup، إعادةالقراءةidempotent، غيرالقابللفكهمحفوظدونerase. scannersوعقدPUTيعرفانprefixالجديد.
- الملفات: SecretProtector/ServiceSettingsFile/LocalConfigCache/LicenseStore/scanners/tests/OWNER_ACTIONS.
- التحقق: Windowsprofiletestlegacyread/reprotect/current-userroundtripنجح؛ build0/0وسuite144pass/3skip. اختبارافترضUnprotectLocalMachineيرفضCurrentUserblobلميرفضلأنsameaccountوUnprotectيقرأblobscope؛ صُححالاختبارليثبتroundtripوالترحيل، ولايدعيcross-accountisolation.
- غيرالمتحقق: LocalSystem↔standarduserisolatedVM؛ تغييرserviceaccountيتطلبإعادةإدخالالمحميةوموثق.

### R6-S02 — أسرار Desktop الخاصة بالمستخدم
- الحالة: DONE-UNVERIFIED
- التغيير: WindowsCurrentUserوالـUnixKeyFileper-userالقائم؛ legacyclientkeyتُقرأثمbackup/saveللنطاقالجديد، client.jsonowner-onlyatomic. لاservicepassworddecryptفيDesktop.
- التحقق: scope/keyroundtriptestsوعملياتbuild/suiteأعلاه؛ لاclient.jsonحقيقيةمعدلة.
- غيرالمتحقق: preferenceupgradeUIعلىUnixوprofileمستقل؛ failreadيحفظlegacybackupولايدعيإلغاءالسرالقديم.

### R6-S03 — لا أسرار محفوظة تصل للواجهة
- الحالة: DONE-VERIFIED
- التحقق: rg SecretProtector.Unprotect DesktopأظهرنداءواحدًاClientPreferences.StoredAccessKeyفقط؛ publicsettingssecret-exclusionوالـprotectedclientrejectناجحان. newlytypedunsavedpasswordفقطيمكنأنيبقىداخلdialogحتىالحفظ؛ noUnprotectمنserviceDTO. openingDBtoolلاdecrypt/copy.

### R6-S04 — Unixkeyowner/mode/create-new
- الحالة: DONE-UNVERIFIED
- التغيير: ownershipgeteuid/stat، key0600exact، parentغيرgroup/otherwritable، symlinkrefusal، create-newحتىالملفغيرالصالحلايستبدل. cachedkeyلاتتجاوزpermissioncheck؛ تغيرKeyFileيمسحcache. startيتحقققبلstatepermissionrepairحتىلايخفيwrongmode.
- التحقق: Unixtestmode/symlink/parentوثّقومتخطىWindows؛ build/suiteأعلاه. لاclaimsعنUnixlive.

### R6-S05 — حمايةstateوقتwrite
- الحالة: DONE-UNVERIFIED
- التغيير: atomicPrivateFileلملفاتsettings/config/license/device.id/firewall/reportHTML/issueJSON؛ Unixcreation600/parent700؛ startuprepairموجودومنعlinks، umask0077قبلnativecreation. WinACLمنRun3installer.
- الملفات: statewriters/StatePermissions/Program/PrivateFile.
- التحقق: settings/migration/regressionWindowsنجح؛ Unixatomicmigrationtestمتخطى؛ لاinstaller/Unixruntime.

### R6-S06 — SQLite وقرارالتشفير
- الحالة: DONE-UNVERIFIED
- التغيير: reports/localDBprecreate600؛ reportsDB/wal/shmrepairبعدwrite، stateparentمحميوstartupmaskلـnativeSQLite. DATA_AT_RESTموثقمنReportStoreschema؛ لاencryptiondependencyلغياباعتمادوعدممنعrootmemoryaccess.
- التحقق: ReportStoreيعملبـtesthostالمؤقتوsettingschangesناجحة؛ Unixwal/shmactualpermissionverificationمازالتPending.

### R6-S07 — redaction/audit
- الحالة: DONE-UNVERIFIED
- التغيير: Databasefailureلاechodrivertext، يحافظعلىhintsثابتةبلاcredentials؛ LogRedactionللـconnectionfields/URLuserinfoفيsettingschangevalues؛ databasecycleerrorنوعفقط. administrativepipelineيسجلrole/IPC-or-TCP/route/statusبلاbody/key/passwordوبنفسlogretention؛ exceptionيسجل500وليس200. settingsIOerrorعام.
- التحقق: driverexceptionfixturepassword/tokenغائبانعنresult، redactionناجح؛ revealLogSinkيثبتauditrole/IPCوبلاقيمة؛ suite144pass/3skip/build0/0. لمتُراجعكلpossiblethird-partylogtextعنplatformlicensing؛ هذهالجزئيةتظلغيرمتحققة.

### R6-S08 — نسخPasswordبإجراءصريح
- الحالة: DONE-UNVERIFIED
- التغيير: POSTpassword/revealAdministrator-only/no-store/resolveinside، زرCopyبعدwarning؛ clear30ثانيةإذاالقيمةلمتتغير، WindowsnativeformatsDWORD0لـhistory/cloudsyncعبرAvaloniaDataTransferAPI. Integratedsecurityلاreveal؛ noautomaticcopy.
- التحقق: Viewer403/admin200/auditبلاقيمة/publicsettingsلاpassword؛ clipboardunchangedclear/changedpreserveunit2حالات؛ build0/0وسuite144pass/3skip.
- غيرالمتحقق: DesktopUI/Windowsclipboardhistory/cloudsyncوالتوقيتالفعلية؛ لاclipboardالمستخدممعدلةمنالاختبارات.
- جمع الخطوات لأنprefix/servicewriter/publiccontract/desktop/reveal/scannersأجزاءترحيلمشتركة؛ كلentryمنفصلة، ولاmaster/push/install. اختبارrelease-scanner9casesأعيدتشغيلهحسبoutputالتالي؛ لاادعاءحتىاكتماله.

### R4-S01 — مفاتيح قراءة وإدارة قوية ومستقلة
- الحالة: DONE-UNVERIFIED
- ترتيب التنفيذ: توليد 32 random bytes لكل مفتاح، إعادة القيمة مرة واحدة فقط، SHA-256 داخل state، مقارنة ثابتة الزمن، ودوران وإلغاء مستقلان؛ Legacy key لا يحصل على دور Administrator.
- التغيير: HTTPS فقط، مفاتيح قراءة وإدارة منفصلة محفوظة كـ hash، تفعيل وإلغاء ودوران من IPC Administrator فقط، شهادة محمية وبصمة pairing صريحة في Desktop، حدود طلبات ومحاولات ومهل. تفاصيل التشغيل في REMOTE_ACCESS.md.
- الملفات: RemoteAccessManager.cs، RemoteTransport.cs، RemoteAbuseGuard.cs، LocalApiHost.cs، AgentApiClient.cs، CertificateTrust.cs، SettingsViewModel.cs وSettingsView.axaml.
- التحقق: Release build نجح بلا تحذيرات أو أخطاء؛ 154 اختبارًا ناجحًا، 3 Unix-only متخطاة، 0 فشل. اختبار HTTPS فعلي على Windows ناجح بعد إصلاح تحميل مفتاح Schannel باستخدام UserKeySet مؤقت.
- اختبار الأمان: RemoteAccessTests وLocalApiSecurityTests يثبتان رفض مفتاح غائب، حدود المحاولات، منع Viewer ومنع إدارة إعداد الوصول البعيد من TCP، ورفض بصمة متغيرة.
- اختبار عدم الانكسار: HTTPS فعلي بمفتاح قراءة صالح يرجع 200؛ مفاتيح مستقلة ودوران وإلغاء واستخراج حالة صحيحة.
- غير المتحقق: جهاز ثانٍ وDesktop UI وLocalSystem وUnix والجدار الناري واستيراد شهادة إنتاج؛ لذلك لا ادعاء بجاهزية إنتاجية.
- جمع الخطوات لأن transport/authentication/pairing عقد اتصال واحد؛ فصلها يكسر اتصال Desktop. R4-S06 موثق في commit مستقل سابقًا.

### R4-S02 — تفعيل بعيد وجدار ناري بإجراء محلي صريح
- الحالة: DONE-UNVERIFIED
- ترتيب التنفيذ: إضافة GET/POST local-only لإدارة الوصول البعيد؛ remote admin لا يستطيع تغيير listen address/port أو مفاتيح الاتصال. Enabled وAllowAdministration وOpenFirewall اختيارات مستقلة، والخدمة لا تفتح منفذًا بوجود عنوان غير محلي وحده.
- التغيير: HTTPS فقط، مفاتيح قراءة وإدارة منفصلة محفوظة كـ hash، تفعيل وإلغاء ودوران من IPC Administrator فقط، شهادة محمية وبصمة pairing صريحة في Desktop، حدود طلبات ومحاولات ومهل. تفاصيل التشغيل في REMOTE_ACCESS.md.
- الملفات: RemoteAccessManager.cs، RemoteTransport.cs، RemoteAbuseGuard.cs، LocalApiHost.cs، AgentApiClient.cs، CertificateTrust.cs، SettingsViewModel.cs وSettingsView.axaml.
- التحقق: Release build نجح بلا تحذيرات أو أخطاء؛ 154 اختبارًا ناجحًا، 3 Unix-only متخطاة، 0 فشل. اختبار HTTPS فعلي على Windows ناجح بعد إصلاح تحميل مفتاح Schannel باستخدام UserKeySet مؤقت.
- اختبار الأمان: RemoteAccessTests وLocalApiSecurityTests يثبتان رفض مفتاح غائب، حدود المحاولات، منع Viewer ومنع إدارة إعداد الوصول البعيد من TCP، ورفض بصمة متغيرة.
- اختبار عدم الانكسار: HTTPS فعلي بمفتاح قراءة صالح يرجع 200؛ مفاتيح مستقلة ودوران وإلغاء واستخراج حالة صحيحة.
- غير المتحقق: جهاز ثانٍ وDesktop UI وLocalSystem وUnix والجدار الناري واستيراد شهادة إنتاج؛ لذلك لا ادعاء بجاهزية إنتاجية.
- جمع الخطوات لأن transport/authentication/pairing عقد اتصال واحد؛ فصلها يكسر اتصال Desktop. R4-S06 موثق في commit مستقل سابقًا.

### R4-S03 — شهادة HTTPS محمية وتوليد واستيراد محلي
- الحالة: DONE-UNVERIFIED
- ترتيب التنفيذ: توليد ECDSA P-256 مع server-auth EKU، تشفير PKCS12 داخل state، استيراد PFX bounded من الواجهة؛ عدم قبول شهادة منتهية أو مستقبلية وعدم تبديل السليمة عند فشل الاستيراد.
- التغيير: HTTPS فقط، مفاتيح قراءة وإدارة منفصلة محفوظة كـ hash، تفعيل وإلغاء ودوران من IPC Administrator فقط، شهادة محمية وبصمة pairing صريحة في Desktop، حدود طلبات ومحاولات ومهل. تفاصيل التشغيل في REMOTE_ACCESS.md.
- الملفات: RemoteAccessManager.cs، RemoteTransport.cs، RemoteAbuseGuard.cs، LocalApiHost.cs، AgentApiClient.cs، CertificateTrust.cs، SettingsViewModel.cs وSettingsView.axaml.
- التحقق: Release build نجح بلا تحذيرات أو أخطاء؛ 154 اختبارًا ناجحًا، 3 Unix-only متخطاة، 0 فشل. اختبار HTTPS فعلي على Windows ناجح بعد إصلاح تحميل مفتاح Schannel باستخدام UserKeySet مؤقت.
- اختبار الأمان: RemoteAccessTests وLocalApiSecurityTests يثبتان رفض مفتاح غائب، حدود المحاولات، منع Viewer ومنع إدارة إعداد الوصول البعيد من TCP، ورفض بصمة متغيرة.
- اختبار عدم الانكسار: HTTPS فعلي بمفتاح قراءة صالح يرجع 200؛ مفاتيح مستقلة ودوران وإلغاء واستخراج حالة صحيحة.
- غير المتحقق: جهاز ثانٍ وDesktop UI وLocalSystem وUnix والجدار الناري واستيراد شهادة إنتاج؛ لذلك لا ادعاء بجاهزية إنتاجية.
- جمع الخطوات لأن transport/authentication/pairing عقد اتصال واحد؛ فصلها يكسر اتصال Desktop. R4-S06 موثق في commit مستقل سابقًا.

### R4-S04 — حصر TCP في HTTPS وإزالة استثناء loopback
- الحالة: DONE-UNVERIFIED
- ترتيب التنفيذ: Kestrel Listen.UseHttps فقط لكل TCP، والتحقق من المفتاح يشمل loopback؛ نقل الاتصال المحلي إلى pipes/sockets يظل بلا credentials ملفات.
- التغيير: HTTPS فقط، مفاتيح قراءة وإدارة منفصلة محفوظة كـ hash، تفعيل وإلغاء ودوران من IPC Administrator فقط، شهادة محمية وبصمة pairing صريحة في Desktop، حدود طلبات ومحاولات ومهل. تفاصيل التشغيل في REMOTE_ACCESS.md.
- الملفات: RemoteAccessManager.cs، RemoteTransport.cs، RemoteAbuseGuard.cs، LocalApiHost.cs، AgentApiClient.cs، CertificateTrust.cs، SettingsViewModel.cs وSettingsView.axaml.
- التحقق: Release build نجح بلا تحذيرات أو أخطاء؛ 154 اختبارًا ناجحًا، 3 Unix-only متخطاة، 0 فشل. اختبار HTTPS فعلي على Windows ناجح بعد إصلاح تحميل مفتاح Schannel باستخدام UserKeySet مؤقت.
- اختبار الأمان: RemoteAccessTests وLocalApiSecurityTests يثبتان رفض مفتاح غائب، حدود المحاولات، منع Viewer ومنع إدارة إعداد الوصول البعيد من TCP، ورفض بصمة متغيرة.
- اختبار عدم الانكسار: HTTPS فعلي بمفتاح قراءة صالح يرجع 200؛ مفاتيح مستقلة ودوران وإلغاء واستخراج حالة صحيحة.
- غير المتحقق: جهاز ثانٍ وDesktop UI وLocalSystem وUnix والجدار الناري واستيراد شهادة إنتاج؛ لذلك لا ادعاء بجاهزية إنتاجية.
- جمع الخطوات لأن transport/authentication/pairing عقد اتصال واحد؛ فصلها يكسر اتصال Desktop. R4-S06 موثق في commit مستقل سابقًا.

### R4-S05 — اعتماد بصمة الشهادة وتدوير pairing في Desktop
- الحالة: DONE-UNVERIFIED
- ترتيب التنفيذ: CertificateTrust لا يقبل self-signed بلا بصمة وافق عليها المستخدم؛ البصمة محفوظة مشفرة per-user. تغيرها يرفض تلقائيًا، Reset pairing صريح. remote HTTP قديم يرفض بلا fallback.
- التغيير: HTTPS فقط، مفاتيح قراءة وإدارة منفصلة محفوظة كـ hash، تفعيل وإلغاء ودوران من IPC Administrator فقط، شهادة محمية وبصمة pairing صريحة في Desktop، حدود طلبات ومحاولات ومهل. تفاصيل التشغيل في REMOTE_ACCESS.md.
- الملفات: RemoteAccessManager.cs، RemoteTransport.cs، RemoteAbuseGuard.cs، LocalApiHost.cs، AgentApiClient.cs، CertificateTrust.cs، SettingsViewModel.cs وSettingsView.axaml.
- التحقق: Release build نجح بلا تحذيرات أو أخطاء؛ 154 اختبارًا ناجحًا، 3 Unix-only متخطاة، 0 فشل. اختبار HTTPS فعلي على Windows ناجح بعد إصلاح تحميل مفتاح Schannel باستخدام UserKeySet مؤقت.
- اختبار الأمان: RemoteAccessTests وLocalApiSecurityTests يثبتان رفض مفتاح غائب، حدود المحاولات، منع Viewer ومنع إدارة إعداد الوصول البعيد من TCP، ورفض بصمة متغيرة.
- اختبار عدم الانكسار: HTTPS فعلي بمفتاح قراءة صالح يرجع 200؛ مفاتيح مستقلة ودوران وإلغاء واستخراج حالة صحيحة.
- غير المتحقق: جهاز ثانٍ وDesktop UI وLocalSystem وUnix والجدار الناري واستيراد شهادة إنتاج؛ لذلك لا ادعاء بجاهزية إنتاجية.
- جمع الخطوات لأن transport/authentication/pairing عقد اتصال واحد؛ فصلها يكسر اتصال Desktop. R4-S06 موثق في commit مستقل سابقًا.

### R4-S07 — حدود الطلبات والمحاولات والمهل والتحقق من clients
- الحالة: DONE-UNVERIFIED
- ترتيب التنفيذ: 8 MiB body، 10 seconds headers، 30 seconds request timeout، 600 requests/minute/IP و5 failed keys/minute. CentralTimeout يلغى عبر CancellationToken واختباره fake بلا شبكة.
- التغيير: HTTPS فقط، مفاتيح قراءة وإدارة منفصلة محفوظة كـ hash، تفعيل وإلغاء ودوران من IPC Administrator فقط، شهادة محمية وبصمة pairing صريحة في Desktop، حدود طلبات ومحاولات ومهل. تفاصيل التشغيل في REMOTE_ACCESS.md.
- الملفات: RemoteAccessManager.cs، RemoteTransport.cs، RemoteAbuseGuard.cs، LocalApiHost.cs، AgentApiClient.cs، CertificateTrust.cs، SettingsViewModel.cs وSettingsView.axaml.
- التحقق: Release build نجح بلا تحذيرات أو أخطاء؛ 154 اختبارًا ناجحًا، 3 Unix-only متخطاة، 0 فشل. اختبار HTTPS فعلي على Windows ناجح بعد إصلاح تحميل مفتاح Schannel باستخدام UserKeySet مؤقت.
- اختبار الأمان: RemoteAccessTests وLocalApiSecurityTests يثبتان رفض مفتاح غائب، حدود المحاولات، منع Viewer ومنع إدارة إعداد الوصول البعيد من TCP، ورفض بصمة متغيرة.
- اختبار عدم الانكسار: HTTPS فعلي بمفتاح قراءة صالح يرجع 200؛ مفاتيح مستقلة ودوران وإلغاء واستخراج حالة صحيحة.
- غير المتحقق: جهاز ثانٍ وDesktop UI وLocalSystem وUnix والجدار الناري واستيراد شهادة إنتاج؛ لذلك لا ادعاء بجاهزية إنتاجية.
- جمع الخطوات لأن transport/authentication/pairing عقد اتصال واحد؛ فصلها يكسر اتصال Desktop. R4-S06 موثق في commit مستقل سابقًا.

### R7-S02a — إصلاح SQLite
- الحالة: DONE-VERIFIED
- الفحص كشف SQLitePCLRaw.lib.e_sqlite3 2.1.6 بثغرة High (GHSA-2m69-gcr7-jv3q). أضيف override مباشر للحزمة المجانية SQLitePCLRaw.bundle_e_sqlite3 3.0.5، وفق توصية maintainer باستعمال توزيع SourceGear.sqlite3 الحديث. Microsoft.Data.Sqlite وschema لم يتغيرا.
- الملفات: MonitorAgent.Service.csproj. ملفات نتائج الفحص المؤقتة في TestResults غير موزعة.
- الأوامر: restore، Release build بلا warnings/errors، tests: 154 نجاح/3 skip/0 فشل. إعادة vulnerable audit أثبتت اختفاء تنبيه SQLite وبقاء حزمتين في الاختبارات لإصلاح لاحق.
- اختبار عدم الانكسار: ReportStore وSQLite المحلي ضمن suite الحالية. Unix native runtimes ما زالت تحتاج CI.
- مصادر: https://github.com/advisories/GHSA-2m69-gcr7-jv3q وhttps://github.com/ericsink/SQLitePCL.raw .

### R7-S02b — إزالة تبعيات الاختبار المصابة
- الحالة: DONE-VERIFIED
- التغيير: xUnit 2.5.3 إلى 2.9.3 فقط؛ يحافظ على واجهة اختبارات xUnit 2 ولا يغير runner. الحزمتان System.Net.Http 4.3.0 وSystem.Text.RegularExpressions 4.3.0 لم تعودا في vulnerable dependency graph.
- التحقق: restore ناجح، Release build بلا warnings/errors؛ 154 نجاح/3 Unix skip/0 فشل. vulnerable audit النهائي لجميع المشاريع بلا أي vulnerabilities مبلغ عنها. التقرير TestResults/dependency-vulnerabilities-final.json محلي فقط.
- الحد: خلو الفحص لا يثبت غياب جميع الثغرات، والاختبار عبر Unix لم يُشغّل محليًا.

### R7-S01 — اختبارات إضافية لحفظ الإعدادات والمهلة
- الحالة: DONE-VERIFIED
- أصلح SettingsContract حفظ metadata الخاصة لنقاط غير قواعد البيانات، ويرفض Conditions غير الصحيحة بدل إسقاطها بصمت. PublicSettings يظل لا يكشف metadata.
- الاختبارات: Ordinary_edit_preserves_non_database_metadata_and_rejects_malformed_conditions وCentral_request_timeout_cancels_transport_and_preserves_cached_configuration. لا شبكة فعلية؛ transport ينتظر cancellation.
- التحقق: Release build بلا warnings/errors؛ 156 نجاح/3 Unix skip/0 فشل. لا تغييرات في تصميم الواجهة أو master.

### R7-S03 — CI وفحص التوزيع والتبعيات
- الحالة: DONE-UNVERIFIED
- التغيير: CI runtime matrix win-x64/linux-x64/osx-arm64 مع self-contained service وDesktop publish؛ scans لكل توزيع؛ check-vulnerable-packages.ps1 يرفض High/Critical. أضيف isolated Linux permission fixture موجب وسالب، لا تشغيل خدمة أو اتصال ترخيص.
- الملفات: ci.yml، check-vulnerable-packages.ps1، check-release-secrets.ps1 (Desktop صريح يتطلب MonitorAgent.dll)، verify-permissions.sh (roots اختبار اختيارية).
- التحقق المحلي: vulnerable check PASS؛ scanner 9 حالات PASS؛ publish win-x64 Service وDesktop ناجحان والفحص PASS؛ bash syntax وPowerShell parser وgit diff --check ناجحة.
- غير المتحقق: workflow غير منشور حسب طلبك؛ Unix runtime/permission fixture وCI الجديدة لم تُشغل. fixture لا تدعي scripted install كاملًا؛ installer يحتاج جهاز مخصص وصلاحيات.
- توثيق التبعيات: DEPENDENCIES.md، sensor library ثابتة وDefender live result غير متاح.

### R2-S01 / R7-S01 — إثبات كل مسارات القراءة
- الحالة: DONE-VERIFIED
- التغيير: OfflineTelemetry test-only DispatchProxy يرجع fixtures دون بدء sensors أو network؛ ReportBuilder/ReportStore حقيقيان داخل testhost.
- الاختبار: Licensed_viewer_can_read_every_read_route_with_offline_telemetry_and_real_report_store يمر على كل GET ذو Viewer metadata، يستبدل route parameters بقيم صحيحة، ويتحقق من success وJSON؛ لا مخارج سرية أو خدمة إنتاج.
- التحقق: الاختبار المنفرد ناجح، والـsuite الكاملة نتيجتها في output هذا commit. Build السابق 0 warnings/errors. لا ادعاء بأن telemetry hardware الفعلية اختبرت.

### R6-S07 — استكمال مراجعة logs الترخيص وrouting
- الحالة: DONE-VERIFIED
- التغيير: رسائل exception ونص رد المنصة غير الموثوق لا تسجل في LicensePlatformClient/LicenseService/LicenseStore أو ConfigPuller؛ تسجل error type أو رسالة ثابتة بلا body/URL credentials. المسار أو device id لا يطبعان في رسالة بدء الترخيص.
- اختبار الأمان: Signing_key_failure_does_not_log_exception_secrets_and_next_request_can_succeed باستخدام HttpRequestException تحتوي fixture password وuserinfo؛ log sink لا يحتوي أي منها.
- عدم الانكسار: طلب signing keys التالي ينجح من fake، licensing/routing regressions ناجحة؛ لا اتصال بالمنصة.
- التحقق: أصلح تعارض اسم test logger الذي أظهره build الأول؛ إعادة Release build 0 warnings/errors، suite 158 نجاح/3 Unix skip/0 فشل. التحقق من log محتوى runtime لكل driver/OS ما زال مطلوبًا.

### R4-S03 / R4-S05 — حالات فشل الشهادة والاستيراد
- الحالة: DONE-UNVERIFIED
- التغيير: corrupt/unreadable remote state أو فشل تحميل الشهادة يغلق TCP/firewall ويترك IPC متاحًا؛ لا fallback. PFX المستقبلي/المنتهي يرفض قبل تغيير الحالة؛ Desktop يرفض expired/future certificate حتى لو بصمتها معتمدة. استيراد PFX يقرأ bounded stream ولا يفترض Seek.
- الاختبارات: Invalid_certificate_import_does_not_replace_working_certificate يثبت رفض مستقبلية وبقاء الشهادة الصالحة وprivate key؛ pin expired/future رفض، وباقي real HTTPS/regressions ناجحة.
- التحقق: Release build 0 warnings/errors؛ suite 159 نجاح/3 Unix skip/0 فشل. خدمة LocalSystem وUI stream provider وفشل cert أثناء runtime غير متحققة فعليًا.
- لا تغييرات اعتماد أو شهادات إنتاج؛ لا master/push/install.

### R7-S02 — إثبات SQLite المحمل وفحص المنصة
- الحالة: DONE-VERIFIED
- الاختبار Loaded_native_sqlite_is_patched_and_parameterized_storage_still_roundtrips يقرأ sqlite_version() من DLL المحملة ويشترط >=3.50.2، ثم يثبت roundtrip parameterized SQL دون تنفيذ قيمة fixture كأوامر.
- التحقق: Release build 0 warnings/errors؛ 160 نجاح/3 Unix skip/0 فشل. منصة LicensingPlatform أعيد اختبارها: 136 نجاح/0 فشل/0 skip، مع CS0108 قائم في MediaController.User. Audit المنصة 7 مشاريع بلا vulnerabilities مبلغ عنها.
- تحقق مصدر sensors: الإصدار الرسمي v0.9.6 يستخدم PawnIO device وembedded modules؛ لا تشغيل driver أو Defender test. تفاصيل ومصادر في DEPENDENCIES.md.
- التوثيق: عناوين Run4 وترتيب كل خطوة أوضح دون تغيير الحالات أو ادعاء نجاح manual checks.

### R7-S01 — مصفوفة 32 finding
- الحالة: DONE-VERIFIED
- SECURITY_MATRIX.md يحتوي صفًا لكل F-01 إلى F-32 مع الشدة والضعف والمعالجة والمصدر ودليل الأمان/regression والحد المتبقي. تحقق محلي من 32 ID فريدًا ولا missing ID.
- لا تُرفع C/H ذات owner/runtime proof ناقص إلى Fixed. F-01/F-18/F-19/F-29/F-32 Pending. تفاصيل each-step verification تبقى في سجلها.
- آخر build/tests للكود: 0 warnings/errors، 160 نجاح/3 Unix skip/0 فشل؛ الوثيقة لا تغير runtime.

### R7-S04 — إجراءات التوقيع والإصدار
- الحالة: DONE-UNVERIFIED
- RELEASE.md يوثق Windows Authenticode للملفات والسكربتات والتحقق، macOS Developer ID/notarization مع السطور الدقيقة لاستبدال ad-hoc/quarantine، Linux package/repository signatures وAV acceptance ومصفوفة OS/tested-by-date.
- تحقق source line numbers بالـrg، ولا signing credentials متاحة. لا signtool/codesign/notarytool أو Defender driver test منفذ؛ لا ادعاء نجاح ولا bypass.
- آخر suite: 160 نجاح/3 Unix skip/0 فشل، build بلا تحذيرات/أخطاء؛ documentation-only.

### R7-S05 — إعلان تدفقات البيانات
- الحالة: DONE-VERIFIED
- DATA_FLOWS.md مأخوذ من NetworkService/InternetMonitor/InternetSpeedTester/LicensePlatformClient/ConfigPuller/MadkhalMonitor/WebsiteMonitor/SiteLogoCache/AgentApiClient؛ كل host ثابت والغرض وما يرسل والتعطيل الموجود أو عدم وجوده.
- لا ادعاء بإيقاف public IP/connectivity ولا telemetry controls غير موجودة؛ default routing فارغة وspeedtest 0 يوقف التلقائي فقط. deviceName في activation موثق.
- التحقق: rg/source review بدون تشغيل اتصالات؛ last suite 160 نجاح/3 skip/0 فشل. لا وثيقة تستبدل runtime consent controls؛ step document-only حسب الخطة.

### R7-S06 — تقارير التنفيذ والترحيل والجاهزية
- الحالة: DONE-VERIFIED
- IMPLEMENTATION_REPORT.md وMIGRATION_REPORT.md وRELEASE_READINESS.md تعتمد على هذا السجل وتفصل build/proof عن التشغيل غير المتحقق، البيانات المحفوظة وتعارضات الترحيل، compatibility وإجراءات المالك.
- تحقق build/tests: 160 نجاح/3 Unix skip/0 فشل، 0 warnings/errors للـAgent. المنصة 136 نجاح مع CS0108 قائم. Vulnerability audit: Agent 4 مشاريع/Platform 7 بلا vulnerabilities مبلغ عنها.
- publish/scans: Service/Desktop win-x64/linux-x64/osx-arm64 ناجحة من Windows؛ ليس إثبات Unix runtime. Release scanner 9 حالات ناجحة. Gitleaks السابق no leaks، والفحص النهائي بعد commits سيضاف كسجل منفصل إن استدعى تعديلًا.
- خطة الكود الممكنة محليًا مكتملة بهذا السجل؛ D2/D3 وإلغاء السر القديم والتوقيع والتحقق OS/customer ما زالت مطلوبات مسماة. لا customer install ولا master edit/push/deploy؛ F-32 مؤجل صراحةً.
- Definition of done لا تعني ready for production مع Pending C/H أو manual verification ناقص.

### R7-S03 — استكمال جميع runtimes والتحقق النهائي
- الحالة: DONE-UNVERIFIED
- CI يشمل win-x64/linux-x64/linux-arm64/osx-x64/osx-arm64 المطابقة لسكربتات التوزيع؛ artifact names فريدة لكل runtime لمنع تصادم upload. verifier يرفض world-readable Unix logs بالإضافة إلى state؛ fixture موجب ثم world-readable logs وunsafe state كحالتين سالبتين.
- تحقق محلي فعلي: self-contained publish Service وDesktop لكل runtimes الخمسة وsecret scans ناجحة؛ bash -n ناجح؛ Release build 0 warnings/errors، tests 160 نجاح/3 Unix-only skip/0 فشل. Gitleaks قبل هذا commit: 27 commit/no leaks، vulnerable checker PASS؛ master ثابت عند 62ee696fc38f5fb1d879f42727fd860371019595 والبرانش المحلي نظيف قبل تحديث CI الأخير.
- الحدود: cross-publish لا يثبت Unix execution؛ CI Linux permission fixture لم تُشغل لعدم نشر workflow؛ التحقق live/signing/domain/JWK/revoke ما زال حسب RELEASE_READINESS.
- تحديث تقارير التنفيذ والجاهزية ليشمل runtimes الخمسة؛ لا master/push/deploy.

### R4-S05 — حذف خانة المفتاح القديمة بطلب المالك
- الحالة: DONE-VERIFIED
- التغيير: حذف عنوان Access key for other computers وخانته المعطلة من SettingsView.axaml لأنها من طريقة إدارة المفتاح القديمة وتسبب التباسًا. إنشاء ودوران المفاتيح يظل من الأزرار الحالية، وخانة اتصال This app مستقلة.
- التحقق: dotnet build MonitorAgent.sln -c Release --no-restore نجح بلا تحذيرات/أخطاء؛ dotnet test MonitorAgent.sln -c Release --no-build --no-restore: 160 نجاح/3 Unix-only skip/0 فشل.
- التغيير محدود بالواجهة، بلا تغيير JSON أو compatibility؛ لم تُختبر الشاشة تفاعليًا ولم تُنشر أو تُثبّت نسخة جديدة.
