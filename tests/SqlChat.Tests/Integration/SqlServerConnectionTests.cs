using Microsoft.Data.SqlClient;

namespace SqlChat.Tests.Integration;

public sealed class SqlServerConnectionTests
{
    [SqlServerIntegrationFact]
    public async Task OpenConnection_Ping_Succeeds()
    {
        await using var conn = new SqlConnection(IntegrationConfig.SqlServerConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var result = await cmd.ExecuteScalarAsync();

        Assert.Equal(1, result);
    }
}
