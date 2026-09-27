namespace ClientAgent.Service.Monitoring;

internal static class IssueText
{
    public static string Stamp(string body)
        => $"{body}{Environment.NewLine}تاريخ ووقت التنبيه: {DateTime.Now:HH:mm}";

    public static string ApplicationDown(string name, string? processName)
    {
        var process = string.IsNullOrWhiteSpace(processName) ? string.Empty : $" ({processName})";
        return Stamp($"تنبيه هام: البرنامج \"{name}\"{process} غير شغال. يرجى التحقق.");
    }

    public static string DeviceDown(string name)
        => Stamp($"تنبيه هام: النقطة \"{name}\" غير متصلة. يرجى التحقق فوراً.");

    public static string WebsiteDown(string name, string address, string detail)
        => Stamp($"تنبيه هام: الموقع \"{name}\"{Environment.NewLine}العنوان: {address}{Environment.NewLine}{detail}");

    public static string WebsiteHttpsOnly(string name, string address, string httpsDetail)
        => Stamp($"تحذير: الموقع \"{name}\" يعمل عبر HTTP فقط.{Environment.NewLine}العنوان: {address}{Environment.NewLine}HTTPS غير متاح. {httpsDetail}");

    public static string CpuHigh(double usage)
        => Stamp($"تنبيه هام: استهلاك المعالج مرتفع جداً ({usage:0}%). يرجى التدخل فوراً.");

    public static string RamHigh(double usage)
        => Stamp($"تنبيه هام: استهلاك الذاكرة مرتفع جداً ({usage:0}%). يرجى التدخل فوراً.");

    public static string DiskHigh(string drive, double usage)
        => Stamp($"تنبيه هام: استهلاك القرص {drive} مرتفع جداً ({usage:0}%). يرجى التدخل فوراً.");

    public static string MadkhalDown()
        => Stamp("تنبيه هام: خادم المدخل غير متاح. يرجى الاتصال فوراً.");

    public static string DatabaseDown()
        => Stamp("تنبيه: تعذر الاتصال بقاعدة البيانات المحلية. يرجى التحقق فوراً.");

    public static string DatabasePointDown(string name, string engine, string server, string detail)
        => Stamp($"تنبيه هام: قاعدة البيانات \"{name}\" ({engine} / {server}) غير متاحة.{Environment.NewLine}{detail}");
}
