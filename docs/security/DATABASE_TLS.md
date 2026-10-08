# التحقق من هوية خادم قاعدة البيانات

نقاط التثبيت القديم بلا TlsMode تبقى Compatibility حفاظًا على المراقبة، وتظهر «server identity not verified» في الإعدادات والقائمة وبطاقة dashboard، مع Warning واحد في Messages & Issues. هذا تخفيف للخطر وليس إغلاقه لهذه النقاط.

الاتصالات الجديدة في نافذة إعداد قاعدة البيانات تبدأ Verify. SQL Server يستخدم Encrypt=Mandatory وTrustServerCertificate=false؛ PostgreSQL/MySQL يستخدمان VerifyFull. لا تخفيض تلقائي عند فشل الشهادة. الاختبار «Test with verification» لا يحفظ تغييرًا؛ نجاحه يعرض خيار اختيار Verify ثم يلزم حفظ الإعدادات.

على الخادم: فعّل TLS بشهادة صالحة تتضمن اسم الخادم في SAN، وصادرة من جهة يثق بها حساب الخدمة على جهاز MonitorAgent. عند استعمال CA داخلية، ثبّت جذرها في مخزن الثقة المناسب للنظام وحساب الخدمة. اتصل بالاسم المطابق للشهادة. لا تستعمل sa/owner؛ أنشئ حسابًا مخصصًا بأقل صلاحية تكفي SELECT 1.

اختباراتنا تفحص connection builders للـdrivers الثلاثة ولا تتصل بخادم عميل. يلزم تحقق المالك على قاعدة اختبار بشهادة صحيحة وأخرى غير موثوقة، وعلى نقطة legacy قبل الإصدار. دعم trusted-root-file لكل نقطة مؤجل؛ استعمل system trust store حتى يكون السلوك متسقًا بين الثلاثة.

المراجع الأصلية: [SqlClient](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation)، [Npgsql](https://www.npgsql.org/doc/security.html)، [MySqlConnector](https://mysqlconnector.net/connection-options/).
