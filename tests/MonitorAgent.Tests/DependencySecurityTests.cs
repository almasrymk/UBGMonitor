using Microsoft.Data.Sqlite;

namespace MonitorAgent.Tests;

public sealed class DependencySecurityTests
{
    [Fact]
    public void Loaded_native_sqlite_is_patched_and_parameterized_storage_still_roundtrips()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        Assert.True(Version.Parse((string)command.ExecuteScalar()!) >= new Version(3, 50, 2), "The loaded native SQLite must include the CVE-2025-6965 fix.");
        command.CommandText = "CREATE TABLE fixture (value TEXT NOT NULL); INSERT INTO fixture(value) VALUES ($value); SELECT value FROM fixture";
        command.Parameters.AddWithValue("$value", "fixture'; DROP TABLE fixture; --");
        Assert.Equal("fixture'; DROP TABLE fixture; --", command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM fixture";
        Assert.Equal(1L, command.ExecuteScalar());
    }
}
