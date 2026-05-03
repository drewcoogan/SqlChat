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
        Assert.Equal(fromYaml.Tables[0].Columns[1].Description, fromJson.Tables[0].Columns[1].Description);
    }
}
