# مراجعة الأسرار — R1-S01

تمت المراجعة على أساس Run 0 المدمج في master. لم تُعرض أو تُنسخ قيمة سر حقيقي.

| الملف / الحقل | التصنيف | النتيجة |
|---|---|---|
| Service/appsettings.json: LicensingClient.ClientId وClientSecret | إعداد توزيع | فارغان؛ ProductCode معرف منتج وليس سرًا |
| Service/appsettings.json: Setting وMonitorPoints | بيانات شخصية سابقة | غير موجودين بعد commit التنظيف |
| Shared/Security/SecretProtector.cs: dpapi وaes | تعريف تنسيق في الكود | prefixes فقط وليست blobs محفوظة |
| Service/Licensing/LicensingOptions.cs: ClientSecret وprefixes | قراءة إعدادات وتعريف تنسيق | لا قيمة اعتماد مثبتة؛ domain الترخيص المعروف F-19 ينتظر Run 5 |
| Tests/LicensingTests.cs وLinuxParsersTests.cs | fixtures | كلمات اختبار وتوكنات مولدة بمفتاح اختبار؛ لا خادم إنتاج |
| Models/DatabaseLogin وAgentRuntimeConfig، Config/GeneralRuntimeSettings | schema | أسماء حقول بدون اعتماد حقيقي؛ حماية runtime ضمن Run 2/4/6 |
| Desktop/Services/AppSettingsStore وAgentApiClient وMonitorPointLauncher وDatabaseLoginWindow | تعامل runtime | لا أسرار ثابتة جديدة؛ المخاطر المعروفة تبقى في جدول الخطة |
| Service/Reports وMonitoring وSystemInfo وPlatform | كود | أسماء الحقول أو CancellationToken؛ SQLite local.db محلي وليس اتصال عميل مضمنًا |
| docs/security/*.md | توثيق | أسماء حقول وأنماط بحث؛ لا قيمة السر القديم |
| appsettings.Development.json وProperties/launchSettings.json | إعداد تطوير | مستوى logging وبيئة التشغيل فقط |
| artifacts/ | ملفات توليد سابقة | git ls-files artifacts فارغ؛ لا ملفات tracked توزع منه |

الأوامر: git ls-files؛ rg -l للأنماط lcs_/lc_/dpapi:/aes:/fhrserp/mdkhl واسم الجهاز القديم؛ rg -l لأسماء Password/Secret/Token/AccessKey/ConnectionString مع استبعاد bin/obj. المراجعة لا تمحو تاريخ Git ولا تثبت إلغاء السر على المنصة.

العناوين التجريبية في RoutingOptions وappsettings.json لا تزال موجودة وتُزال في R1-S03. تنظيف history لا يعيد سرية اعتماد سبق كشفه؛ F-01 Pending حتى تأكيد الإلغاء.
