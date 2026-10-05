using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;

namespace MonitorAgent.UI.Services;

/// <summary>Opens what a monitor point watches: a site in the browser, an app, a device's web page, or a database tool.</summary>
public static class MonitorPointLauncher
{
    /// <returns>A message for the user, or null when there is nothing to tell.</returns>
    public static string? Open(MonitorPoint point)
        => point.Type switch
        {
            MonitorPointType.Application => OpenApplication(point),
            MonitorPointType.Database => OpenDatabase(point),
            _ => OpenAddress(point)
        };

    private static string? OpenAddress(MonitorPoint point)
    {
        var address = point.Address.Trim();
        if (address.Length == 0)
        {
            return $"{point.DisplayName} has no address to open.";
        }

        var url = address.Contains("://", StringComparison.Ordinal) ? address : "http://" + address;
        return ShellOpen.Open(url) ? null : $"Could not open {url}.";
    }

    private static string? OpenApplication(MonitorPoint point)
    {
        var path = point.Address.Trim().Trim('"');
        var processName = string.IsNullOrWhiteSpace(point.Model)
            ? Path.GetFileNameWithoutExtension(path)
            : point.Model.Trim();
        if (BringToFront(processName))
        {
            return null;
        }

        if (path.Length == 0 || !File.Exists(path))
        {
            return $"The program for {point.DisplayName} was not found:\n{path}";
        }

        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
            })?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not start {point.DisplayName}: {ex.Message}";
        }
    }

    private static string? OpenDatabase(MonitorPoint point)
    {
        var login = point.Database ?? new DatabaseLogin();
        var server = string.IsNullOrWhiteSpace(login.Server) ? point.Address.Trim() : login.Server.Trim();
        var program = DatabaseLogos.FindProgram(login.Engine);
        if (program is null)
        {
            return $"No management program for {EngineName(login.Engine)} was found on this computer.";
        }

        var password = SecretProtector.IsProtected(login.Password)
            ? SecretProtector.Unprotect(login.Password)
            : login.Password;
        var needsPassword = !login.IntegratedSecurity && password.Length > 0;
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        var file = Path.GetFileNameWithoutExtension(program);
        var autoLogin = false;
        if (file.Equals("ssms", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add("-S");
            start.ArgumentList.Add(SqlServerAddress(server, login.Port));
            AddIfSet(start, "-d", login.Database);
            if (login.IntegratedSecurity)
            {
                start.ArgumentList.Add("-E");
                autoLogin = true;
            }
            else
            {
                AddIfSet(start, "-U", login.Username);
            }

            start.ArgumentList.Add("-nosplash");
        }
        else if (file.Equals("azuredatastudio", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add("--server");
            start.ArgumentList.Add(SqlServerAddress(server, login.Port));
            AddIfSet(start, "--database", login.Database);
            start.ArgumentList.Add("--authenticationType");
            start.ArgumentList.Add(login.IntegratedSecurity ? "Integrated" : "SqlLogin");
            if (!login.IntegratedSecurity)
            {
                AddIfSet(start, "--user", login.Username);
            }

            autoLogin = login.IntegratedSecurity;
        }
        else if (file.Equals("MySQLWorkbench", StringComparison.OrdinalIgnoreCase) && server.Length > 0)
        {
            start.ArgumentList.Add("-query");
            start.ArgumentList.Add($"{login.Username}@{server}:{(login.Port > 0 ? login.Port : 3306)}");
        }

        try
        {
            start.WorkingDirectory = Path.GetDirectoryName(program) ?? string.Empty;
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            return $"Could not start {Path.GetFileName(program)}: {ex.Message}";
        }

        if (autoLogin || !needsPassword)
        {
            return null;
        }

        return UiPlatform.SetClipboardText(password)
            ? $"{Path.GetFileNameWithoutExtension(program)} opened for {point.DisplayName}.\n\nThe password is copied - paste it (Ctrl+V) in the login box and press Connect."
            : null;
    }

    private static string SqlServerAddress(string server, int port)
        => port is > 0 and not 1433 && !server.Contains(',') && !server.Contains('\\')
            ? $"{server},{port}"
            : server;

    private static void AddIfSet(ProcessStartInfo start, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            start.ArgumentList.Add(name);
            start.ArgumentList.Add(value.Trim());
        }
    }

    private static string EngineName(DatabaseEngine engine)
        => engine switch
        {
            DatabaseEngine.PostgreSql => "PostgreSQL (pgAdmin)",
            DatabaseEngine.MySql => "MySQL (MySQL Workbench)",
            _ => "SQL Server (SSMS or Azure Data Studio)"
        };

    private static bool BringToFront(string processName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var process in processes)
            {
                var window = process.MainWindowHandle;
                if (window == IntPtr.Zero)
                {
                    continue;
                }

                if (IsIconic(window))
                {
                    ShowWindow(window, SwRestore);
                }

                SetForegroundWindow(window);
                return true;
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return false;
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
}
