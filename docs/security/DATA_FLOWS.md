# تدفقات البيانات الخارجية

مراجعة المصدر بتاريخ 2026-10-08؛ لم تُنفذ الاتصالات أثناء المراجعة. البيانات المحلية والتقارير لا تُرفع تلقائيًا إلى خدمة telemetry جديدة.

| المصدر | الوجهة | الغرض وما يرسل | الإيقاف الموجود حاليًا |
|---|---|---|---|
| Service/SystemInfo/NetworkService.cs | api.ipify.org، ifconfig.me/ip، icanhazip.com | GET لمعرفة public IP؛ الوجهة ترى source IP وHTTP metadata، مع cache خمس دقائق | لا مفتاح تعطيل مخصص في الكود؛ network view/report قد يطلبها. الإيقاف الكامل يتطلب سياسة شبكة أو تغييرًا لاحقًا |
| نفس الملف | 8.8.8.8 | ICMP latency/packet loss؛ source IP وping payload | لا اختيار target/تعطيل مستقل |
| Service/Monitoring/InternetMonitor.cs | 1.1.1.1، 8.8.8.8 | ICMP connectivity check | لا مفتاح تعطيل مستقل |
| نفس الملف | http://www.msftconnecttest.com/connecttest.txt، http://clients3.google.com/generate_204 | HTTP captive portal/connectivity detection؛ source IP/HTTP metadata، بلا credentials | لا مفتاح تعطيل مستقل؛ HTTP هنا كشف portal وليس remote API أو licensing |
| Shared/Monitoring/InternetSpeedTester.cs | speed.cloudflare.com/__down، nbg1-speed.hetzner.com/100MB.bin، ash-speed.hetzner.com/100MB.bin، proof.ovh.net/files/100Mb.dat | تحميل بيانات لقياس السرعة؛ source IP والطلب وكمية transfer | SpeedTestIntervalSeconds=0 يوقف التلقائي؛ عدم الضغط على الاختبار اليدوي |
| نفس الملف | speed.cloudflare.com/__up | POST بيانات قياس upload مولدة، بلا credentials أو تقارير | نفس تعطيل اختبار السرعة |
| Service/Licensing/LicensePlatformClient.cs + LicensingOptions | almasrymk-001-site15.etempurl.com | token/enrollment opt-in؛ activate ترسل product key/device ID/device name/product/OS والإصدار، وcheck/heartbeat ما يحتاجه endpoint؛ signing-keys GET. Client credential يستخدم فقط لو configured/enrolled | لا تعطل الترخيص للتحايل؛ عدم activation على clean install لا يولّد heartbeat مرخصًا. domain الحالي مؤقت وPending اعتماد |
| Service/Config/ConfigPuller.cs | Routing.CentralApiUrl، فارغ افتراضيًا | agent config GET، agentId query؛ HTTPS أو loopback HTTP فقط، local SQLite path ثابت | empty CentralApiUrl يوقف الطلبات بلا warnings مستمرة |
| Service/Monitoring/MadkhalMonitor.cs | Routing.MadkhalServerUrl، فارغ افتراضيًا | health request إلى خادم اختاره المالك؛ مصدر الاتصال وHTTP metadata | empty MadkhalServerUrl يوقفه |
| WebsiteMonitor/DeviceMonitor/DatabaseMonitor/ApplicationMonitor | عناوين monitor points التي يضيفها المستخدم | HTTP website/ICMP device/DB SELECT 1 مع login المحفوظ/App local؛ وفق النوع | حذف النقطة أو تعطيلها حسب إعدادات النوع؛ تأكد من إعدادات legacy قبل التشغيل |
| Desktop/Services/SiteLogoCache.cs | صفحة Website المضافة وfavicon أو icon URLs المكتشفة بما فيها redirects | تحميل صورة للعرض، User-Agent browser ثابت؛ الوجهات ترى IP جهاز Desktop | عدم استدعاء capture؛ لا مفتاح global مخصص |
| Desktop/Services/AgentApiClient.cs | عنوان Agent الذي اختاره المستخدم | HTTPS remote key/API؛ IPC محلي بلا key، pinned certificate مشفر في client.json | تعطيل remote محليًا، أو استعمال local IPC |

أيقونات HTML/تقارير SVG تحتوي URLs namespace فقط؛ ليست اتصال شبكة. إعداد عناوين نقاط أو Routing قد يؤدي إلى وجهات إضافية يحددها المستخدم وredirects في HTTP monitoring؛ القائمة الثابتة وحدها لا تصف كل تركيب عميل. تفاصيل domain/JWK واعتمادات التفعيل في OWNER_ACTIONS.md وLICENSING.md.
