# MonitorAgent — Client Monitoring Agent

منظومة مراقبة مركزية (`MonitorAgent`) تشمل خدمة لجمع بيانات الجهاز، Local API عبر اتصال نظام التشغيل، وتطبيق سطح مكتب (Avalonia) يعمل على Windows وLinux وmacOS.

## المسار

`D:\Work\UBG\Source\UBG Monitor`

## المشاريع

| المشروع | الدور |
|---|---|
| `MonitorAgent.Service` | Worker Service + Monitoring + Local API |
| `MonitorAgent.Desktop` | تطبيق سطح المكتب (Avalonia) لـ Windows وLinux وmacOS |
| `MonitorAgent.Shared` | Models / DTOs / API routes |
| `MonitorAgent.Tests` | اختبارات xUnit |

## المتطلبات

- .NET 8 SDK
- Windows (WMI / Performance Counters / Windows Service)

## التشغيل للتطوير

من مجلد الحل:

```powershell
dotnet restore MonitorAgent.sln
dotnet build MonitorAgent.sln
dotnet test MonitorAgent.sln
```

تشغيل الخدمة:

```powershell
dotnet run --project src/MonitorAgent.Service
```

الاتصال المحلي من الواجهة يستخدم named pipes على Windows وUnix sockets على Linux/macOS. لا يوجد HTTP loopback للاستخدام المحلي. تشغيل خدمة التطوير يحتاج حسابًا مرفوعًا حتى يقبل العميل هوية endpoint.

المجموعات المحلية: `MonitorAgent Admins` للإدارة و`MonitorAgent Viewers` للقراءة على Windows، و`monitoragent-admin` / `monitoragent` على Unix. مستخدم الإدارة على Unix ينضم إلى المجموعتين. يلزم تسجيل الخروج والدخول بعد تغيير العضوية. Viewer يرى الإعدادات دون كلمات المرور أو المفاتيح ولا يمكنه تعديلها أو إدارة الترخيص أو بدء اختبار اتصال قاعدة بيانات.

تشغيل الواجهة:

```powershell
dotnet run --project src/MonitorAgent.Desktop
```

## تثبيت Windows Service

نفّذ PowerShell **كمسؤول** مع حزمة مبنية مسبقًا؛ الجهاز العميل لا يحتاج SDK:

```powershell
.\scripts\install-service.ps1 -Source C:\Packages\Service -DesktopSource C:\Packages\Desktop
# للمطور فقط: .\scripts\install-service.ps1 -BuildFromSource
.\scripts\verify-permissions.ps1
```

إيقاف وإزالة:

```powershell
.\scripts\uninstall-service.ps1
```

البرنامج في `%ProgramFiles%\MonitorAgent\Service` والواجهة في `Desktop`؛ البيانات في `%ProgramData%\MonitorAgent`. إعدادات المستخدم في `settings.json` كبيانات فقط، دون دخولها في إعدادات logging. أول تشغيل يحفظ backups ويُرحّل إعدادات وData/Reports القديمة؛ لا يستبدل إعدادات موجودة. الإزالة العادية تحفظ البيانات والمجموعات، و`-Purge` / `--purge` لحذفها صراحة.

على Linux/macOS شغّل install.sh من الحزمة بـsudo ثم `bash verify-permissions.sh`. ملفات البرنامج root-owned بلا group/other write، state0700 وملفات الأسرار0600. قبل التحديث تُحفظ الإعدادات القديمة0600 داخل state للترحيل. راجع backups قبل حذفها؛ قد تحتوي أسرارًا قديمة.

## المعمارية

- موديولات المراقبة تفحص الموارد والأجهزة وقاعدة البيانات وMadkhal وتكتب النتيجة في السجلات.
- سحب الإعدادات من Central كل 5 دقائق مع كاش محلي JSON.
- Local API عبر IPC بهوية وصلاحيات نظام التشغيل؛ لا CORS. TCP يتطلب مفتاحًا حتى من loopback؛ remote Viewer فقط حتى اكتمال تفعيل Run4.

## الإعدادات

`src/MonitorAgent.Service/appsettings.json`

- `Agent` — معرف الوكيل والإصدار
- `Routing` — عناوين Madkhal و Central
- `LocalApi:Port` — الافتراضي 5050
- `Monitoring` — فترات الفحص

السجلات الافتراضية: `C:\ProgramData\MonitorAgent\logs\`

## Endpoints المحلية

- `GET /api/status`
- `GET /api/snapshot`
- `GET /api/cpu`
- `GET /api/ram`
- `GET /api/network`
- `GET /api/disks/partitions`
- `GET /api/disks/physical`
- `GET /api/hardware`
- `GET /api/os`
- `GET /api/processes/top?count=10`
- `GET /api/monitorpoints`

## ملاحظات أمنية

لا تربط Local API على `0.0.0.0`. الخدمة تستمع على `127.0.0.1` فقط.
