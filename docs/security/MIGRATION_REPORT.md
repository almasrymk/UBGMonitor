# تقرير الترحيل

التغييرات على branch محلي؛ لم يُثبّت البرنامج ولم تُغيّر إعدادات العملاء. التشغيل الأول ينفذ ترحيلًا idempotent مع backups قبل التغيير. لا حذف تاريخ Git ولا بيانات licensing الحالية.

1. Windows installer يضع binaries في Program Files ويحتفظ بـstate في ProgramData. الإعدادات التشغيلية تصبح settings.json منفصلة عن appsettings.json الخاص بتكوين الخدمة/Serilog. Unix يستعمل state paths السابقة بصلاحيات أكثر تقييدًا.
2. StateMigration يقرأ legacy Setting/Ui أو settings section، ويحفظ نقاط المراقبة والإعدادات. الموجودة في state لا تُستبدل بتوزيع جديد. يرفض symlinks، ولا يدمج تعارض بيانات بصمت.
3. Windows legacy Data/Reports تحت publish تُنسخ عبر streaming مع backup. لو الملفان يختلفان يوقف الترحيل ويحفظ الاثنين للمراجعة؛ partial identical copy يُستكمل بأمان. marker لا يُكتب إلا بعد النجاح، وinstaller لا ينظف legacy publish قبل نجاحه.
4. license.json وdevice.id والتاريخ لا يعاد إنشاؤها لمجرد upgrade. DPAPI legacy يُقرأ ويُعاد حمايته إلى dpapi2 تحت حساب الخدمة مع backup. blob غير قابل للقراءة لا يختلق replacement. تغيير service account لاحقًا يحتاج إدخال passwords من جديد.
5. GET settings لا يرجع passwords/blobs/key/private metadata. ordinary PUT يحفظ الأسرار وmetadata داخليًا؛ تغيير target database يحتاج password جديدًا. edit عام لا يمسح monitor points أو stored login. Conditions المعيبة ترفض بلا overwrite.
6. RemoteAccessKey السابق يصبح Viewer hash فقط؛ key القديم يمسح من settings الحالية بعد حفظ backup. remote الإدارة والجدار الناري لا يفعّلان تلقائيًا. HTTP remote المحفوظ في Desktop يحتاج HTTPS وpairing؛ الشهادات لا تقبل بلا trust system أو بصمة أكدها المستخدم. backups القديمة تُعامل كأسرار ودوّر المفتاح بعد التحقق.
7. Desktop client.json يصبح per-user protected للـaccess key وcertificate pins؛ الترحيل يحفظ backup. لا database password يُفك داخل Desktop. reveal الوحيد action إداري صريح مسجل في audit.
8. DB legacy يحتفظ بـCompatibility ظاهر؛ DB جديدة من dialog تبدأ Verify. لا fallback بعد TLS failure. existing licenses لا تُقطع بربط trust anchor تجريبي؛ D2/D3 بقيتا Pending.

الاختبارات Windows تثبت حفظ الإعدادات والتعارض والتكرار وHTTP roundtrip وDPAPI تحت الحساب الحالي. لم يُثبت الترحيل كخدمة LocalSystem أو Unix installer. قبل الاعتماد: backup كامل محفوظ بصلاحيات إدارية، تجربة upgrade على VM، مقارنة عدد النقاط/التاريخ/license/device ID، اتصال DB، ثم verify-permissions. لا rollback بإرجاع binaries وحدها بعد secret scope migration؛ استخدم snapshot متسقة للـstate/binaries وحساب الخدمة، ولا تخفّض الصلاحيات لحل مشكلة.
