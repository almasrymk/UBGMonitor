# فحص عمر الشاشات

شغّل من جذر المشروع:

```powershell
dotnet run --project tools/PerformanceProbe/PerformanceProbe.csproj -c Release
```

الفحص يستخدم Avalonia Headless وHTTP وهميًا يرجع 503؛ لا يتصل بخدمة حقيقية أو منصة ترخيص. ينشئ نافذة غير مرئية وينتقل 120 مرة بين الشاشات، ثم يتحقق من جمع الشاشات القديمة، وبقاء قيمة إعداد غير محفوظة، وعدم إعادة إنشاء المحرر عند رفض التنقل.

GC.Collect هنا للفحص فقط، ولا يوجد داخل البرنامج كحل للاستهلاك. الرقم المعروض لحجم managed heap لا يمثل Working Set أو ذاكرة GPU/Skia في Windows. لا تستخدمه لإعلان نسبة انخفاض RAM على الجهاز. يقاس التشغيل الحقيقي بذاكرة العملية وعدادات System.Runtime مع ثبات PID والبيانات والنسخة.

يعتمد الاختبار على [منصة Avalonia Headless](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform).
