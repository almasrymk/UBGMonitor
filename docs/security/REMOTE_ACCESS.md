# الوصول البعيد الآمن

الوصول المحلي يستعمل IPC بهوية نظام التشغيل. الوصول البعيد يستعمل HTTPS فقط، ويظل مغلقًا حتى يوجد مفتاح قراءة ويُفعّل من اتصال Administrator محلي. إنشاء مفتاح إدارة مستقل لا يفعّل الإدارة تلقائيًا. فتح منفذ الجدار الناري اختيار مستقل وصريح.

من الإعدادات المحلية: أنشئ مفتاح Viewer، اختر عنوان IPv4 من عناوين الجهاز، ثم احفظ بزر Save الموجود. الحفظ يطبّق إعدادات الخدمة وHTTPS والإدارة البعيدة والجدار الناري؛ لا يوجد زر تطبيق منفصل. المفتاح يظهر مرة واحدة؛ الخدمة تحفظ SHA-256 فقط. إنشاء مفتاح جديد يلغي القديم. إلغاء مفتاح القراءة يغلق الوصول البعيد، وإلغاء مفتاح الإدارة يعطل الإدارة. تغييرات التفعيل والمفاتيح والشهادة لا تُقبل من TCP حتى بمفتاح إدارة.

Save يتحقق من وجود المفاتيح المطلوبة قبل كتابة الإعدادات، ويطبّق الوصول البعيد بعد نجاح حفظها. إذا فشل الجزء الثاني بعد حفظ الإعدادات، يوضح الفشل الجزئي ولا يعرض نجاحًا كاملًا؛ يمكن إعادة Save. تغييرات checkboxs مشمولة في تتبع التعديلات وReset.

الشهادة الذاتية تُنشأ بمفتاح ECDSA P-256، ومفتاحها الخاص محفوظ مشفرًا في remote-access.json. يمكن استيراد PFX محليًا بدلًا منها. Desktop يقبل الشهادة الموثوقة من النظام، أو بصمة SHA-256 وافق عليها المستخدم بعد مقارنتها بالبصمة المعروضة محليًا على الخادم عبر قناة مستقلة. لا يرسل طلب API قبل الموافقة على شهادة ذاتية. تغير البصمة يُرفض؛ Reset pairing يتطلب إجراءً صريحًا. لا يوجد رجوع إلى HTTP.

الترحيل يحول RemoteAccessKey القديم إلى hash لمفتاح قراءة، ولا يمنحه الإدارة، ولا يفتح الجدار الناري تلقائيًا. نسخة settings الاحتياطية القديمة قد تحتوي المفتاح القديم؛ تحفظ داخل state المحمي. بعد التحقق من الترحيل، دوّر المفتاح واحفظ النسخ وفق سياسة احتفاظ آمنة. لا تحذف النسخة قبل التأكد من حفظ باقي الإعدادات.

حد الطلب 8 MiB، مهلة الطلب 30 ثانية، والرؤوس 10 ثوانٍ. خمس محاولات مفتاح خاطئ خلال دقيقة تمنع محاولات إضافية من عنوان المصدر، ومحدد الطلبات يسمح بـ600 طلب/دقيقة لتغطية polling. لا يعتمد على X-Forwarded-For. Host مقيد، ولا CORS. هذه حدود إساءة استخدام وليست حماية من هجوم موزع.

التحقق المحلي: اختبار TLS حقيقي على Windows يثبت نجاح HTTPS ورفض المفتاح الغائب وتعديل Viewer وHTTP على منفذ HTTPS. اختبارات أخرى تثبت فصل المفاتيح وإلغاءها ورفض شهادة متغيرة. ما زال مطلوبًا: تجربة جهاز ثانٍ، UI pairing، LocalSystem، Linux/macOS، الجدار الناري الفعلي، واستيراد شهادة إنتاج معتمدة. لا تشغيل أو نشر تم تلقائيًا.

Local key display: Viewer is required and its checkbox is fixed. The optional Admin checkbox controls administration when Save is used. Opening the key panel creates missing keys only; existing keys are preserved. Each row supports right-click copy and confirmed regeneration. Newly created keys are cached encrypted for the signed-in local user in remote-key-display.json, and are displayed/copied only when their SHA-256 matches the local administrator status response. Existing hashes cannot recover old key text; manual regeneration is required to display those keys. The service still stores hashes only. Copy uses the existing sensitive clipboard handling.

Updated layout: Allow access (HTTPS) controls remote access; Viewer and Admin rows remain directly underneath it. Viewer is fixed checked; Admin alone controls remote administration. Enabling access creates missing keys without granting Admin automatically. Loading service status never generates keys. Existing keys remain valid until explicit regeneration.

Owner correction: Enable remote HTTPS access remains an independent visible option. The separate Allow remote administration checkbox and its Viewer/Admin rows appear only for non-loopback listening addresses. Admin is editable when administration is selected; Viewer stays fixed. Missing keys are generated when administration is selected, not when HTTPS is toggled.

Selecting a network listen address now prepares missing Viewer/Admin keys after the network confirmation is accepted. Existing keys are preserved and displayed from the matching encrypted local cache; selecting the address does not enable Admin or apply remote configuration. Cancel creates no keys. Keys created before the display cache existed still require explicit regeneration to show their text.
