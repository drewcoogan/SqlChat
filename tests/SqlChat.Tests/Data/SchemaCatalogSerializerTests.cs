using SqlChat.Data.Schema;
using SqlChat.Data.Serialization;
using SqlChat.Domain.Builders;

namespace SqlChat.Tests.Data;

public sealed class SchemaCatalogSerializerTests
{
    private static readonly string SampleJson = """
        {
          "tables": [
            {
              "name": "users",
              "schema": "dbo",
              "description": "All users",
              "columns": [
                { "name": "id", "storeType": "int", "isPrimaryKey": true, "isNullable": false },
                { "name": "email", "storeType": "varchar", "description": "User email", "isNullable": false }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void FromJson_ValidJson_ReturnsCatalogWithCorrectShape()
    {
        var catalog = SchemaCatalogSerializer.FromJson(SampleJson);

        Assert.Single(catalog.Tables);
        var table = catalog.Tables[0];
        Assert.Equal("users", table.Name);
        Assert.Equal("dbo", table.Schema);
        Assert.Equal("All users", table.Description);
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("id", table.Columns[0].Name);
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.Equal("User email", table.Columns[1].Description);
    }

    [Fact]
    public void FromJson_CamelCaseKeys_Deserializes()
    {
        var json = """{ "tables": [{ "name": "t", "columns": [{ "name": "c", "storeType": "int", "isNullable": false }] }] }""";

        var catalog = SchemaCatalogSerializer.FromJson(json);

        Assert.Equal("t", catalog.Tables[0].Name);
    }

    [Fact]
    public void FromJson_EmptyOrWhitespace_Throws()
    {
        Assert.Throws<ArgumentException>(() => SchemaCatalogSerializer.FromJson(""));
        Assert.Throws<ArgumentException>(() => SchemaCatalogSerializer.FromJson("   "));
    }

    [Fact]
    public void FromJson_RoundTripsViaYaml()
    {
        var original = new SchemaCatalogBuilder()
            .AddTable("orders", t => t
                .AddColumn("id", "int", primaryKey: true, nullable: false)
                .AddColumn("status", "varchar", description: "Order state", nullable: false))
            .Build();

        var yaml = SchemaCatalogSerializer.ToYaml(original);
        var json = """
            {
              "tables": [
                {
                  "name": "orders",
                  "columns": [
                    { "name": "id", "storeType": "int", "isPrimaryKey": true, "isNullable": false },
                    { "name": "status", "storeType": "varchar", "description": "Order state", "isNullable": false }
                  ]
                }
              ]
            }
            """;

        var fromYaml = SchemaCatalogSerializer.FromYaml(yaml);
        var fromJson = SchemaCatalogSerializer.FromJson(json);

        Assert.Equal(fromYaml.Tables[0].Name, fromJson.Tables[0].Name);
        Assert.Equal(fromYaml.Tables[0].Columns.Count, fromJson.Tables[0].Columns.Count);

        var idFromYaml = fromYaml.Tables[0].Columns[0];
        var idFromJson = fromJson.Tables[0].Columns[0];
        Assert.Equal(idFromYaml.StoreType, idFromJson.StoreType);
        Assert.Equal(idFromYaml.IsPrimaryKey, idFromJson.IsPrimaryKey);
        Assert.Equal(idFromYaml.IsNullable, idFromJson.IsNullable);

        var statusFromYaml = fromYaml.Tables[0].Columns[1];
        var statusFromJson = fromJson.Tables[0].Columns[1];
        Assert.Equal(statusFromYaml.Description, statusFromJson.Description);
        Assert.Equal(statusFromYaml.StoreType, statusFromJson.StoreType);
        Assert.Equal(statusFromYaml.IsNullable, statusFromJson.IsNullable);
    }

    [Fact]
    public void ToYaml_RoundTrip_PreservesSchemaFkAndPk()
    {
        const string sql = """
            CREATE TABLE public.orders (
                id SERIAL NOT NULL,
                customer_id INT NOT NULL REFERENCES customers(id),
                total DECIMAL(10,2),
                CONSTRAINT orders_pkey PRIMARY KEY (id)
            );
            """;

        var original = DdlSchemaScanner.ScanSql(sql);
        var yaml = SchemaCatalogSerializer.ToYaml(original);
        var roundTripped = SchemaCatalogSerializer.FromYaml(yaml);

        Assert.Single(roundTripped.Tables);
        var table = roundTripped.Tables[0];
        Assert.Equal("orders", table.Name);
        Assert.Equal("public", table.Schema);
        Assert.Equal(3, table.Columns.Count);

        var id = table.Columns.Single(c => c.Name == "id");
        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsNullable);

        var customerId = table.Columns.Single(c => c.Name == "customer_id");
        Assert.NotNull(customerId.ForeignKey);
        Assert.Equal("customers", customerId.ForeignKey.PrincipalTable);
    }

    [Fact]
    public void ToYaml_ProducesReadableOutput()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE users (id INT NOT NULL, email VARCHAR(255) NOT NULL);");
        var yaml = SchemaCatalogSerializer.ToYaml(catalog);

        Assert.Contains("tables:", yaml);
        Assert.Contains("name: users", yaml);
        Assert.Contains("storeType:", yaml);
    }
}
