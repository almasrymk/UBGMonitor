# UBG Monitor — Client Monitoring Agent

منظومة مراقبة مركزية (`UBGMonitor`) تشمل Windows Service لجمع بيانات الجهاز، Local API على `localhost`، ولوحة WPF.

## المسار

`D:\Work\UBG\Source\UBG Monitor`

## المشاريع

| المشروع | الدور |
|---|---|
| `ClientAgent.Service` | Worker Service + Outbox + Monitoring + Local API |
| `ClientAgent.UI` | لوحة WPF |
| `ClientAgent.Shared` | Models / DTOs / API routes |
| `ClientAgent.Tests` | اختبارات xUnit |

## المتطلبات

- .NET 8 SDK
- Windows (WMI / Performance Counters / Windows Service)

## التشغيل للتطوير

من مجلد الحل:

```powershell
dotnet restore UBGMonitor.sln
dotnet build UBGMonitor.sln
dotnet test UBGMonitor.sln
```

تشغيل الخدمة:

```powershell
dotnet run --project src/ClientAgent.Service
```

اختبار Local API (مرتبط بـ `127.0.0.1` فقط):

```powershell
curl http://127.0.0.1:5050/api/status
```

تشغيل الواجهة:

```powershell
dotnet run --project src/ClientAgent.UI
```

## تثبيت Windows Service

نفّذ PowerShell **كمسؤول** بعد نشر الخدمة:

```powershell
dotnet publish src/ClientAgent.Service -c Release -o C:\ProgramData\ClientAgent\publish

sc.exe create ClientAgentService binPath= "C:\ProgramData\ClientAgent\publish\ClientAgent.Service.exe" start= auto
sc.exe description ClientAgentService "UBG Monitor client agent"
sc.exe start ClientAgentService
```

إيقاف وإزالة:

```powershell
sc.exe stop ClientAgentService
sc.exe delete ClientAgentService
```

## المعمارية

- كل موديول مراقبة ينتج `MonitoringEvent` مستقل.
- الكتابة إلى SQLite Outbox قبل الإرسال (`INSERT OR IGNORE` على `EventId` لضمان Idempotency).
- التوجيه: Madkhal إن كان متاحاً، ثم Central API، وإلا يبقى الحدث محلياً.
- سحب الإعدادات من Central كل 5 دقائق مع كاش محلي JSON.
- Local API على `http://127.0.0.1:5050` مع CORS لـ localhost فقط.

## الإعدادات

`src/ClientAgent.Service/appsettings.json`

- `Agent` — معرف الوكيل والإصدار وHeartbeat
- `Outbox` — مسار SQLite وحجم الدفعة
- `Routing` — عناوين Madkhal و Central
- `LocalApi:Port` — الافتراضي 5050
- `Monitoring` — فترات الفحص

السجلات الافتراضية: `C:\ProgramData\ClientAgent\logs\`

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
- `GET /api/events/recent?count=100`
- `GET /api/events/pending`

## ملاحظات أمنية

لا تربط Local API على `0.0.0.0`. الخدمة تستمع على `127.0.0.1` فقط.
