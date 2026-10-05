# MonitorAgent — Client Monitoring Agent

منظومة مراقبة مركزية (`MonitorAgent`) تشمل Windows Service لجمع بيانات الجهاز، Local API على `localhost`، وتطبيق سطح مكتب (Avalonia) يعمل على Windows وLinux وmacOS.

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

اختبار Local API (مرتبط بـ `127.0.0.1` فقط):

```powershell
curl http://127.0.0.1:5050/api/status
```

تشغيل الواجهة:

```powershell
dotnet run --project src/MonitorAgent.Desktop
```

## تثبيت Windows Service

نفّذ PowerShell **كمسؤول** بعد نشر الخدمة:

```powershell
dotnet publish src/MonitorAgent.Service -c Release -o C:\ProgramData\MonitorAgent\publish

sc.exe create MonitorAgent binPath= "C:\ProgramData\MonitorAgent\publish\MonitorAgent.Service.exe" start= auto
sc.exe description MonitorAgent "MonitorAgent background service"
sc.exe start MonitorAgent
```

إيقاف وإزالة:

```powershell
sc.exe stop MonitorAgent
sc.exe delete MonitorAgent
```

## المعمارية

- موديولات المراقبة تفحص الموارد والأجهزة وقاعدة البيانات وMadkhal وتكتب النتيجة في السجلات.
- سحب الإعدادات من Central كل 5 دقائق مع كاش محلي JSON.
- Local API على `http://127.0.0.1:5050` مع CORS لـ localhost فقط.

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
