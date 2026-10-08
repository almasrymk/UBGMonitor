# Run 5 — خطة Device Enrollment المحلية

## التفويض والحدود

المستخدم طلب العمل على فرع مستقل وعدم النشر، والحفاظ على Authentication والتراخيص الحالية. D1=NO؛ D2 عنوان مؤقت غير معتمد؛ D3 لم تُعتمد مفاتيح عامة. الاتصال الوحيد بالعنوان الحالي هو GET للمفاتيح العامة الذي طلبه المستخدم صراحة؛ لا تفعيل/إلغاء/تدوير/ترحيل إنتاج. Run 1 متوقف مؤقتًا بعد R1-S02 لحين إنجاز توسعة Run 5 التي طلبها المستخدم.

## التصميم المقترح للتنفيذ الآمن

Enrollment إداري صريح لكل تثبيت باستخدام API Clients الموجودة: مسؤول المنصة المصرح له ينشئ اعتمادًا عشوائيًا منفصلًا مربوطًا بترخيص واحد وdeviceId واحد. الجهاز لا يحتفظ باعتماد مشترك موزع مع البرنامج. المسارات الحالية وRequireScope تبقى؛ إصدار bearer من client-token الحالي يستخدم الاعتماد المنفصل. الخدمة تتحقق من حالة العميل وربطه عند كل إجراء حتى بعد إصدار JWT، فلا يستطيع العميل تغيير deviceId/productKey أو استخدام revoke قديم.

إصدار الاعتماد ليس تفعيل ترخيص ولا يحجز activation slot. العميل يحتاج activate الحالي الذي يطبق القيود الموجودة. إعادة إصدار credential لجهاز لا تعطل أي ترخيص؛ تدوير/إلغاء الاعتماد يتم عبر API Clients الحالية. التسليم يدوي إداري عبر قناة آمنة، بدون anonymous enrollment أو إدراج مفاتيح bootstrap داخل البرنامج. يلزم لاحقًا سياسة دعوات/تسجيل تلقائي إذا أراد المستخدم تجربة pairing تلقائية.

## خطوات التوسعة

1. E01: إضافة ربط اختياري إلى ApiClient وترحيل إضافي nullable يحافظ على الصفوف الحالية، وسياسة نطاقين فقط للعملاء المرتبطين.
2. E02: endpoint إداري جديد feature-gated، مع tenant isolation، تحقق الترخيص والجهاز، وإرجاع السر مرة واحدة وتسجيل audit بدون سر.
3. E03: فحص حالة العميل وربط الجهاز/الترخيص قبل activate/validate/heartbeat/deactivate وقبل idempotency replay؛ API clients القديمة غير المرتبطة تبقى متوافقة.
4. E04: Agent يدعم مصدر اعتماد منفصل لكل جهاز بدون إلغاء مسار الإعدادات الحالي، ويمنع استخدام بيانات enrollment تخص جهازًا/منتجًا/خادمًا مختلفًا. لا تفعيل API لإدخال الاعتماد عبر LocalApi غير المؤمَّن.
5. E05: security tests للحماية والربط والعزل والإلغاء، regression tests للترخيص القديم والتفعيل المتكرر، وbuild للمشروعين. لا migrations ضد قاعدة حقيقية.

## بوابات الإنتاج

- اعتماد تصميم التسجيل الإداري اليدوي أو تحديد تجربة pairing المطلوبة.
- اعتماد دومين HTTPS دائم ومجموعة JWK من قناة إدارية مستقلة قبل تنفيذ pinning إنتاجي.
- مراجعة migration وتجربتها على نسخة قاعدة staging؛ feature enrollment مغلقة افتراضيًا.
- حماية state والاعتمادات على الجهاز ضمن Run 3/6 ومصادقة الإدارة المحلية ضمن Run 2 قبل دمج onboarding بالواجهة.
- لا يوصف F-18 أو Run 5 كـFixed حتى اعتماد trust anchors؛ عدم تغيير التحقق القديم مقصود للحفاظ على التراخيص الحالية.

## تفاصيل التنفيذ والمراجعة

- Agent: `codex/security-hardening-enrollment`. المنصة: `codex/device-enrollment` داخل `.security-work/LicensingPlatform`، مستودع مستقل متجاهَل في مستودع Agent. لا push ولا PR ولا deploy.
- E01: ApiClient يحمل BoundDeviceId وBoundProductKeyHash اختياريين. migration `20261008062853_DeviceClientBindings` تضيف عمودين NULL فقط؛ لا تعديل الصفوف القديمة. SetScopes يمنع إضافة issue/read للعميل المرتبط. Design factory يولد SQL Server model بدون بدء المضيف أو قراءة بيانات نشر أو الاتصال بقاعدة.
- E02: POST `/api/v1/api-clients/device-enrollments` يحتاج صلاحيتَي ApiClientsManage وLicensesManage وبوابة DeviceEnrollment:Enabled، الافتراضي false. الطلب licenseId/deviceId/name. الرد يحتوي licenseId/deviceId/productCode وcredentials.client وcredentials.clientSecret مرة واحدة؛ Cache-Control:no-store. الخادم يخزن hash فقط ويكتب audit بدون secret. لا slot محجوز حتى activate. كل إصدار يولد clientId/secret مختلفين؛ التكرار الإداري ينشئ اعتمادًا جديدًا ولا يلغي القديم تلقائيًا.
- E03: التحقق من حالة API Client وربطه قبل الإجراءات الأربعة وقبل إعادة نتيجة idempotency. الاعتماد القديم غير المرتبط يبقى tenant-scoped كما كان. إلغاء/تعطيل العميل يمنع استخدام JWT صدر قبله. تغيير deviceId أو productKey لا يسمح باستخدام الاعتماد لجهاز/ترخيص آخر.
- E04: LicensingOptions.FromConfiguration يقرأ DeviceEnrollment عند Enabled=true فقط. البيانات المطلوبة: DeviceId، ProductCode، PlatformUrl، ClientId، ClientSecret. الخادم المقصود يجب أن يطابق عنوان البرنامج وبـHTTPS؛ اختلاف الجهاز/المنتج/العنوان يمنع إرسال أي بيانات اعتماد، ولا يعود إلى legacy credentials. نجاح bearer/token reuse ما زال كما كان. غياب/رفض credential يعطي Unavailable ولا يرسل device request بلا مصادقة، فيبقى قرار offline في LicenseService الحالي.
- الاعتماد الجديد runtime configuration فقط؛ لا installer/import endpoint ولا حفظ تلقائي ولا بيانات اعتماد في إعدادات التوزيع. عند تجربة staging، يحقنه مسؤول الخدمة من مصدر إعدادات محمي بصلاحيات الحساب، مثل environment خاص بالخدمة؛ لا يوضع في git أو command history أو إعدادات موزعة. حماية المصدر/الحفظ الدائم وتوصيله بالواجهة مؤجلان لـRun2/3/6، ولذلك onboarding ليس جاهزًا للإنتاج.
- E05: اختبارات المنصة تغطي default-off، anonymous/client forbidden، tenant isolation، سر فريد غير مخزن/مسجل، عدم حجز slot، منع scope escalation، mismatch في الإجراءات الأربعة، دورة الترخيص الكاملة، revoke مع idempotency، وlegacy activation. اختبارات Agent تغطي اختيار opt-in، mismatch بلا شبكة/بلا fallback، bearer reuse، legacy، ورفض المصادقة بلا anonymous request.

## المفاتيح العامة — نسخة غير موثوقة

GET `/api/v1/signing-keys` بتاريخ 2026-10-08 أعاد أربع مفاتيح EC/P-256/ES256: active واحدة وretiring ثلاث. النسخة المحلية `.security-work/public-jwks-unverified.json` متجاهَلة ولا تُحمَّل بواسطة المنتج. SHA256 للملف: `1B0147A40503EF3AA6803CFD6F0F5743FCCC6C3E8B74EE8404FD1DA21B3687ED`. بصمة الملف دليل مراجعة للنسخة فقط وليست اعتمادًا للجهة الموقعة. يلزم مطابقة kid/x/y لكل مفتاح مقصود مع المسؤول عبر قناة مستقلة، وحسم دعم مفاتيح retiring للحفاظ على التراخيص القائمة.

## حدود التحقق

الاختبارات المحلية تستخدم SQLite منفصلة وTestServer أو HTTP fake، ولا تثبت ترقية SQL Server فعلية ولا ACL إنتاجية ولا تشغيل Linux/macOS للتغيير الجديد. SQL idempotent وmodel snapshot تم توليدهما وفحصهما دون تطبيقهما. Run5-S01 وS02 معطلتان بانتظار D2/D3؛ S04 استُبدلت بتوسعة التسجيل المصرح بها، مع بقاء Authentication. لا ادعاء بإغلاق F-18/F-19 أو انتهاء الخطة كلها.
