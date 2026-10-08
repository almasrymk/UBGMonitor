# إجراءات الإصدار — لا نشر تلقائي

لا إصدار تجاري قبل إغلاق blockers في RELEASE_READINESS.md. أوامر التوقيع التالية تعليمات للمالك على جهاز release موثوق؛ لم تُنفذ ولا تحتوي credentials. اعتمد hashes بعد التوقيع، وليس قبله. احتفظ بـSBOM أو قائمة NuGet ونتائج الفحوص مع كل إصدار.

## Windows

ابنِ Release self-contained من commit محدد، وامسح Service/Desktop بإجراءات CI. على جهاز توقيع بشهادة code-signing صالحة:

```powershell
signtool sign /a /fd SHA256 /tr <approved-RFC3161-timestamp-URL> /td SHA256 MonitorAgent.Service.exe
signtool sign /a /fd SHA256 /tr <approved-RFC3161-timestamp-URL> /td SHA256 MonitorAgent.exe
signtool verify /pa /all /v MonitorAgent.Service.exe
signtool verify /pa /all /v MonitorAgent.exe
```

وقّع install-service.ps1 وuninstall-service.ps1 بشهادة معتمدة عبر `Set-AuthenticodeSignature` وتحقق بـ`Get-AuthenticodeSignature`. لا تحوّل سكربتات PowerShell إلى MSI في هذه الخطة. اختبر installer بحساب Administrator على VM، ثم تحقق بحساب standard user أنه لا يستطيع تعديل binaries/config أو قراءة state. اختبر الترقية والـrollback على نسخة بيانات منزوعة الأسرار، وليس جهاز عميل.

## macOS

قبل release، استبدل المواضع التالية بعد توافر Developer ID credentials؛ لا تترك fallback ad-hoc أو إزالة quarantine:

| الملف والسطر الحالي | المطلوب |
|---|---|
| scripts/macos/install.sh:42 `xattr -dr com.apple.quarantine` | احذف السطر؛ التوزيع notarized ويجب أن يمر Gatekeeper |
| نفس الملف:43 `codesign --sign -` | لا تعِد توقيع binary على جهاز العميل؛ `codesign --verify --strict` للتوزيع الموقع |
| scripts/macos/make-app.sh:16 إزالة quarantine | احذف السطر |
| نفس الملف:17–19 ad-hoc/fallback | وقّع nested binaries من الداخل للخارج، ثم bundle باستخدام Developer ID Application مع `--options runtime --timestamp`؛ لا fallback |
| scripts/macos/build-pkg.sh:17 ad-hoc | وقّع Service مسبقًا بـDeveloper ID Application؛ package النهائي بـDeveloper ID Installer |

```bash
codesign --force --options runtime --timestamp --sign 'Developer ID Application: <identity>' <binary-or-app>
codesign --verify --deep --strict --verbose=2 <app>
productsign --sign 'Developer ID Installer: <identity>' unsigned.pkg signed.pkg
xcrun notarytool submit signed.pkg --keychain-profile '<approved-profile>' --wait
xcrun stapler staple signed.pkg
xcrun stapler validate signed.pkg
spctl --assess --type install --verbose=2 signed.pkg
```

Entitlements تُحدد من احتياجات Avalonia/.NET وhardened runtime بعد اختبار، ولا تعطل Gatekeeper. اختبر Intel وApple Silicon كلٌ على جهاز مناسب، بما فيها IPC groups والـlaunchd upgrade.

## Linux والحماية من الفيروسات

وقّع metadata مستودع APT/RPM بمفتاح إصدار إداري، وانشر المفتاح العام عبر قناة موثوقة. RPM: `rpmsign --addsign` ثم `rpm --checksig`; APT: InRelease/Release.gpg موثوق وSigned-By خاص بالمستودع، لا apt-key عام. tar.gz: detached signature وSHA256SUMS موقعان. لا مفاتيح خاصة في repo أو agent.

افحص الملفات النهائية بعد التوقيع باستخدام Defender محدث وسجل OS/definitions/date/hash/result. تحقق من driver الذي يستخدمه LibreHardwareMonitorLib 0.9.6 على VM؛ metadata تشير PawnIO لكن التشغيل لم يُتحقق. إذا ظهر false positive أرسل عينة إلى البائع عبر حساب المالك ودوّن case ID؛ لا exclusions ولا تعطيل antivirus. لا رفع بيانات عميل.

| منصة | Build/tests محليًا هنا | اختبار installer/upgrade/runtime | tested by / date |
|---|---|---|---|
| Windows | Release/tests + win-x64 publish/scans ناجحة | LocalSystem/standard-user، UI، firewall، real DB/second PC مطلوب | Codex / 2026-10-08 للكود المحلي فقط |
| Ubuntu/Debian | CI مجهز وغير منشور لهذه التغييرات | deb/tar + permissions/IPC/migration مطلوب | Pending |
| RHEL-compatible | لم يُنفذ | tar + SELinux/firewall/group required | Pending |
| macOS Intel | لم يُنفذ | package/launchd/signature/IPC required | Pending |
| macOS Apple Silicon | CI runtime osx-arm64 مجهز | نفس اختبارات التشغيل والتوقيع المطلوبة | Pending |
