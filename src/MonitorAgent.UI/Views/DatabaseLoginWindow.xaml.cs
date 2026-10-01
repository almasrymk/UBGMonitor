using System.Windows;
using System.Windows.Controls;
using MonitorAgent.Shared.Models;
using MonitorAgent.Shared.Security;
using MonitorAgent.UI.Services;

namespace MonitorAgent.UI.Views;

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

    public DatabaseLoginWindow(DatabaseLogin? current, AgentApiClient? client = null)
    {
        InitializeComponent();
        _client = client;
        TestButton.Visibility = client is null ? Visibility.Collapsed : Visibility.Visible;
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
        _shownPassword = SecretProtector.IsProtected(_storedPassword)
            ? SecretProtector.Unprotect(_storedPassword)
            : _storedPassword;
        PasswordInput.Password = _shownPassword;
        if (_storedPassword.Length > 0 && _shownPassword.Length == 0)
        {
            PasswordNote.Text = "The saved password is encrypted on the service's computer. Leave the box empty to keep it, or type a new one.";
        }

        _loading = false;
        UpdateIntegratedState();
    }

    private void EngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

    private void IntegratedBox_Changed(object sender, RoutedEventArgs e) => UpdateIntegratedState();

    private void UpdateIntegratedState()
    {
        var sqlServer = EngineBox.SelectedItem is EngineChoice choice && choice.Engine == DatabaseEngine.SqlServer;
        IntegratedBox.Visibility = sqlServer ? Visibility.Visible : Visibility.Collapsed;
        var credentials = !sqlServer || IntegratedBox.IsChecked != true;
        UserLabel.Visibility = credentials ? Visibility.Visible : Visibility.Collapsed;
        UserBox.Visibility = credentials ? Visibility.Visible : Visibility.Collapsed;
        PasswordLabel.Visibility = credentials ? Visibility.Visible : Visibility.Collapsed;
        PasswordInput.Visibility = credentials ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The login as typed; null (with the reason shown) when something required is missing.</summary>
    private DatabaseLogin? ReadLogin()
    {
        ErrorText.Foreground = (System.Windows.Media.Brush)FindResource("AccentRedBrush");
        ErrorText.Text = string.Empty;
        if (EngineBox.SelectedItem is not EngineChoice choice)
        {
            ErrorText.Text = "Choose a database type.";
            return null;
        }

        var server = ServerBox.Text.Trim();
        var database = DatabaseBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            ErrorText.Text = "Server and database are required.";
            return null;
        }

        var integrated = choice.Engine == DatabaseEngine.SqlServer && IntegratedBox.IsChecked == true;
        if (!integrated && string.IsNullOrWhiteSpace(UserBox.Text))
        {
            ErrorText.Text = "Username is required.";
            return null;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1)
        {
            port = choice.DefaultPort;
        }

        return new DatabaseLogin
        {
            Engine = choice.Engine,
            Server = server,
            Port = port,
            Database = database,
            Username = integrated ? string.Empty : UserBox.Text.Trim(),
            Password = integrated ? string.Empty : Password(),
            IntegratedSecurity = integrated
        };
    }

    /// <summary>
    /// The saved password when it was not changed. A new one is encrypted here when the service is on this computer;
    /// for a service on another computer it is sent as typed and the service encrypts it for its own computer.
    /// </summary>
    private string Password()
    {
        var typed = PasswordInput.Password;
        if (typed == _shownPassword && _storedPassword.Length > 0)
        {
            return _storedPassword;
        }

        return _client is null || _client.IsLocal ? SecretProtector.Protect(typed) : typed;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || ReadLogin() is not { } login)
        {
            return;
        }

        TestButton.IsEnabled = false;
        ErrorText.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
        ErrorText.Text = "Testing the connection...";
        try
        {
            var result = await _client.TestDatabaseAsync(login);
            ErrorText.Foreground = (System.Windows.Media.Brush)FindResource(result.Success ? "AccentGreenBrush" : "AccentRedBrush");
            ErrorText.Text = result.Message;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ReadLogin() is not { } login)
        {
            return;
        }

        Result = login;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static int DefaultPort(DatabaseEngine engine)
        => engine switch
        {
            DatabaseEngine.PostgreSql => 5432,
            DatabaseEngine.MySql => 3306,
            _ => 1433
        };

    private sealed record EngineChoice(DatabaseEngine Engine, string Label, int DefaultPort)
    {
        public string? Logo => Services.DatabaseLogos.Base64(Engine);
    }
}
