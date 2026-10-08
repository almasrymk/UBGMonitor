using Avalonia.Controls;
using Avalonia.Interactivity;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Services;

namespace MonitorAgent.Desktop.Views;

public partial class DatabaseLoginWindow : Window
{
    private readonly EngineChoice[] _engines =
    [
        new(DatabaseEngine.SqlServer, "SQL Server", 1433),
        new(DatabaseEngine.PostgreSql, "PostgreSQL", 5432),
        new(DatabaseEngine.MySql, "MySQL", 3306)
    ];

    private readonly AgentApiClient? _client;
    private bool _loading;

    /// <summary>The password as saved; kept when the user does not type a new one.</summary>
    private string _storedPassword = string.Empty;

    /// <summary>What the password box showed on opening (empty when the saved password is encrypted for another computer).</summary>
    private string _shownPassword = string.Empty;

    public DatabaseLogin? Result { get; private set; }

    public DatabaseLoginWindow() : this(null)
    {
    }

    private readonly string? _monitorPointId;
    private bool _hasSavedPassword;
    public DatabaseLoginWindow(DatabaseLogin? current, AgentApiClient? client = null, string? monitorPointId = null)
    {
        InitializeComponent();
        _client = client;
        _monitorPointId = monitorPointId;
        TestButton.IsVisible = client is not null;
        EngineBox.ItemsSource = _engines;
        Load(current);
    }

    private void Load(DatabaseLogin? current)
    {
        _loading = true;
        var engine = current?.Engine ?? DatabaseEngine.SqlServer;
        EngineBox.SelectedItem = _engines.First(item => item.Engine == engine);
        ServerBox.Text = current?.Server ?? string.Empty;
        PortBox.Text = (current?.Port > 0 ? current.Port : DefaultPort(engine)).ToString();
        DatabaseBox.Text = current?.Database ?? string.Empty;
        UserBox.Text = current?.Username ?? string.Empty;
        IntegratedBox.IsChecked = current?.IntegratedSecurity == true;
        _storedPassword = current?.Password ?? string.Empty;
        _hasSavedPassword = current?.HasPassword == true;
        _shownPassword = string.Empty;
        PasswordInput.Text = string.Empty;
        if (current?.HasPassword == true || _storedPassword.Length > 0)
        {
            PasswordNote.Text = "The saved password is encrypted on the service's computer. Leave the box empty to keep it, or type a new one.";
        }

        _loading = false;
        UpdateIntegratedState();
    }

    private void EngineBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || EngineBox.SelectedItem is not EngineChoice choice)
        {
            return;
        }

        if (!int.TryParse(PortBox.Text, out var port) || _engines.Any(item => item.DefaultPort == port))
        {
            PortBox.Text = choice.DefaultPort.ToString();
        }

        UpdateIntegratedState();
    }

    private void IntegratedBox_Changed(object? sender, RoutedEventArgs e) => UpdateIntegratedState();

    private void UpdateIntegratedState()
    {
        if (_loading)
        {
            return;
        }

        var sqlServer = EngineBox.SelectedItem is EngineChoice choice && choice.Engine == DatabaseEngine.SqlServer;
        IntegratedBox.IsVisible = sqlServer;
        var credentials = !sqlServer || IntegratedBox.IsChecked != true;
        UserLabel.IsVisible = credentials;
        UserBox.IsVisible = credentials;
        PasswordLabel.IsVisible = credentials;
        PasswordInput.IsVisible = credentials;
    }

    private void SetMessage(string text, string brushKey)
    {
        ErrorText.Text = text;
        ErrorText.Foreground = UiTheme.Brush(brushKey, Avalonia.Media.Colors.Gray);
    }

    /// <summary>The login as typed; null (with the reason shown) when something required is missing.</summary>
    private DatabaseLogin? ReadLogin()
    {
        SetMessage(string.Empty, "AccentRedBrush");
        if (EngineBox.SelectedItem is not EngineChoice choice)
        {
            SetMessage("Choose a database type.", "AccentRedBrush");
            return null;
        }

        var server = (ServerBox.Text ?? string.Empty).Trim();
        var database = (DatabaseBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            SetMessage("Server and database are required.", "AccentRedBrush");
            return null;
        }

        var integrated = choice.Engine == DatabaseEngine.SqlServer && IntegratedBox.IsChecked == true;
        var user = (UserBox.Text ?? string.Empty).Trim();
        if (!integrated && string.IsNullOrWhiteSpace(user))
        {
            SetMessage("Username is required.", "AccentRedBrush");
            return null;
        }

        if (!int.TryParse((PortBox.Text ?? string.Empty).Trim(), out var port) || port < 1)
        {
            port = choice.DefaultPort;
        }

        return new DatabaseLogin
        {
            Engine = choice.Engine,
            Server = server,
            Port = port,
            Database = database,
            Username = integrated ? string.Empty : user,
            Password = integrated ? string.Empty : Password(),
            HasPassword = !integrated && _hasSavedPassword,
            IntegratedSecurity = integrated
        };
    }

    /// <summary>
    /// The saved password when it was not changed. A new one is encrypted here only for a service on this Windows
    /// computer (machine-wide protection both can read); otherwise it is sent as typed and the service encrypts it
    /// with its own key, which this app cannot read on Linux or macOS.
    /// </summary>
    private string Password()
    {
        var typed = PasswordInput.Text ?? string.Empty;
        if (typed == _shownPassword && _storedPassword.Length > 0)
        {
            return string.Empty;
        }

        return typed;
    }

    private async void Test_Click(object? sender, RoutedEventArgs e)
    {
        if (_client is null || ReadLogin() is not { } login)
        {
            return;
        }

        TestButton.IsEnabled = false;
        SetMessage("Testing the connection...", "TextSecondaryBrush");
        try
        {
            var result = await _client.TestDatabaseAsync(login, monitorPointId: _monitorPointId);
            SetMessage(result.Message, result.Success ? "AccentGreenBrush" : "AccentRedBrush");
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (ReadLogin() is not { } login)
        {
            return;
        }

        Result = login;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private static int DefaultPort(DatabaseEngine engine)
        => engine switch
        {
            DatabaseEngine.PostgreSql => 5432,
            DatabaseEngine.MySql => 3306,
            _ => 1433
        };

    private sealed record EngineChoice(DatabaseEngine Engine, string Label, int DefaultPort)
    {
        public string? Logo => DatabaseLogos.Base64(Engine);
    }
}
