using Npgsql;

namespace SqlChat.Tests.Integration;

public sealed class PostgresConnectionTests
{
    [IntegrationFact]
    public async Task OpenConnection_Ping_Succeeds()
    {
        await using var conn = new NpgsqlConnection(IntegrationConfig.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var result = await cmd.ExecuteScalarAsync();

        Assert.Equal(1, result);
    }
}
