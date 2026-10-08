# البيانات المحلية وحماية الأسرار

Windows: القيم الجديدة dpapi2 تستخدم DPAPI CurrentUser. عند تشغيل الخدمة كـLocalSystem فهذا حساب الخدمة، وDesktop يستعمل حساب المستخدم المسجل. قراءة dpapi القديمة متاحة للترحيل فقط؛ أول قراءة settings/license/client preferences تُعيد الحماية مع backup، دون حذف قيمة غير قابلة للقراءة. تغيير حساب الخدمة لاحقًا يحتاج إعادة إدخال كلمات المرور ومفتاح الترخيص/الشهادات المحمية. لا تنقل ملفات DPAPI لجهاز/حساب آخر باعتبارها نسخة قابلة للتشغيل.

Unix: AES-GCM بمفتاح0600 يملكه الحساب الحالي، وparent غير قابل لكتابةgroup/others، مع رفضsymlink وcreate-new؛ ملفغيرآمن يرفض بدل استخدامه/استبداله. Backups التي تحتوي secret.key تحفظ بحماية مماثلة للأصل، ومن يمتلك المفتاح والبيانات يستطيع فكها.

state/settings/config/license/device-id/firewall/reports لهاowner-only creation/repair، وWindowsACL منinstaller تمنعstandard users. Desktopclient.json ملفخاصبالمستخدم؛ لم تعدالواجهةتفكأسرارمرسلةمنالخدمة. نقطةlegacy unreadable تُحفظ لإصلاحها، ولا تُمَسح بصمت.

SQLite reports.db يحتوي readings/resources/disk/process summaries وincidents/settings changes/speed/applicationchanges، ولا حقولpassword/key مخصصة. local.db للـlocal SELECT1 health. قد تتضمن incidents وأسماءالمستخدمين/البرامج/الخوادم بياناتحساسة؛ ACL/modes والـretention القائم ضروريان. لم تُضف SQLCipher أوdependency encryption: تصعيدroot/Administrator يستطيع قراءة ذاكرةالخدمة/مفتاحها، والإضافة وحدها لا تمنع ذلك. قرارتشفيرDBمستقل يحتاج نموذج تهديدواعتمادمالك؛ disk encryption على مستوىالنظام خيارتشغيل لا يُفعَّل آليًا.

فتح أداةDB لا ينسخpassword. Copy saved password إجراءAdministrator صريح بعدwarning؛ routePOST/no-store مدققةrole/transport/result دونالقيمة؛ clipboard تمسحبعد30ثانيةفقطلومازالتنفسالقيمة. Windows يرسلformat flags تمنعhistory/cloudsync عنددعمها؛ تحققUIوالتاريخالفعليرهنجهازاختبار. Integrated authenticationلا تحتاجreveal.
