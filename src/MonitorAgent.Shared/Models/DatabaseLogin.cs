namespace MonitorAgent.Shared.Models;

public enum DatabaseEngine
{
    SqlServer = 0,
    PostgreSql = 1,
    MySql = 2
}

public sealed class DatabaseLogin
{
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.SqlServer;

    public string Server { get; set; } = string.Empty;

    public int Port { get; set; } = 1433;

    public string Database { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool IntegratedSecurity { get; set; }

    public DatabaseLogin Copy()
        => new()
        {
            Engine = Engine,
            Server = Server,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            IntegratedSecurity = IntegratedSecurity
        };
}
