using System.Windows;
using System.Windows.Controls;
using ClientAgent.Shared.Models;
using ClientAgent.Shared.Security;

namespace ClientAgent.UI.Views;

public partial class DatabaseLoginWindow : Window
{
    private readonly EngineChoice[] _engines =
    [
        new(DatabaseEngine.SqlServer, "SQL Server", 1433),
        new(DatabaseEngine.PostgreSql, "PostgreSQL", 5432),
        new(DatabaseEngine.MySql, "MySQL", 3306)
    ];

    private bool _loading;

    public DatabaseLogin? Result { get; private set; }

    public DatabaseLoginWindow(DatabaseLogin? current)
    {
        InitializeComponent();
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
        PasswordInput.Password = SecretProtector.Unprotect(current?.Password);
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

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (EngineBox.SelectedItem is not EngineChoice choice)
        {
            ErrorText.Text = "Choose a database type.";
            return;
        }

        var server = ServerBox.Text.Trim();
        var database = DatabaseBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
        {
            ErrorText.Text = "Server and database are required.";
            return;
        }

        var integrated = choice.Engine == DatabaseEngine.SqlServer && IntegratedBox.IsChecked == true;
        if (!integrated && string.IsNullOrWhiteSpace(UserBox.Text))
        {
            ErrorText.Text = "Username is required.";
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), out var port) || port < 1)
        {
            port = choice.DefaultPort;
        }

        Result = new DatabaseLogin
        {
            Engine = choice.Engine,
            Server = server,
            Port = port,
            Database = database,
            Username = integrated ? string.Empty : UserBox.Text.Trim(),
            Password = integrated ? string.Empty : SecretProtector.Protect(PasswordInput.Password),
            IntegratedSecurity = integrated
        };
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

    private sealed record EngineChoice(DatabaseEngine Engine, string Label, int DefaultPort);
}
