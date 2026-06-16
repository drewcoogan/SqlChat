using SqlChat.Data.Schema;
using SqlChat.Domain.Models;

namespace SqlChat.Tests.Schema;

public sealed class DdlSchemaScannerTests
{
    // -------------------------------------------------------------------------
    // Basic table parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanSql_SimpleTable_ParsesNameAndColumns()
    {
        const string sql = """
            CREATE TABLE users (
                id INT NOT NULL,
                email VARCHAR(255) NOT NULL,
                name VARCHAR(100)
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        Assert.Single(catalog.Tables);
        var table = catalog.Tables[0];
        Assert.Equal("users", table.Name);
        Assert.Null(table.Schema);
        Assert.Equal(3, table.Columns.Count);
    }

    [Fact]
    public void ScanSql_SchemaQualifiedTable_ParsesSchemaAndName()
    {
        const string sql = """
            CREATE TABLE public.orders (
                id SERIAL NOT NULL
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        var table = catalog.Tables[0];
        Assert.Equal("orders", table.Name);
        Assert.Equal("public", table.Schema);
    }

    [Fact]
    public void ScanSql_IfNotExists_ParsesTable()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS products (
                id INT NOT NULL
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        Assert.Single(catalog.Tables);
        Assert.Equal("products", catalog.Tables[0].Name);
    }

    [Fact]
    public void ScanSql_MultipleTables_ParsesAll()
    {
        const string sql = """
            CREATE TABLE customers (id INT NOT NULL, email VARCHAR(255) NOT NULL);
            CREATE TABLE orders (id INT NOT NULL, customer_id INT NOT NULL);
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        Assert.Equal(2, catalog.Tables.Count);
        Assert.Equal("customers", catalog.Tables[0].Name);
        Assert.Equal("orders", catalog.Tables[1].Name);
    }

    // -------------------------------------------------------------------------
    // Nullability
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanSql_NotNullColumn_IsNullableFalse()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE t (id INT NOT NULL);");

        Assert.False(catalog.Tables[0].Columns[0].IsNullable);
    }

    [Fact]
    public void ScanSql_NullableColumn_IsNullableTrue()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE t (name VARCHAR(100));");

        Assert.True(catalog.Tables[0].Columns[0].IsNullable);
    }

    // -------------------------------------------------------------------------
    // Primary keys
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanSql_InlinePrimaryKey_IsPrimaryKeyTrue()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE t (id INT PRIMARY KEY, name TEXT);");

        var id = catalog.Tables[0].Columns.Single(c => c.Name == "id");
        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsNullable);
    }

    [Fact]
    public void ScanSql_TableLevelPrimaryKey_IsPrimaryKeyTrue()
    {
        const string sql = """
            CREATE TABLE orders (
                id INT NOT NULL,
                status TEXT,
                CONSTRAINT orders_pkey PRIMARY KEY (id)
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        var id = catalog.Tables[0].Columns.Single(c => c.Name == "id");
        Assert.True(id.IsPrimaryKey);
    }

    [Fact]
    public void ScanSql_NonPkColumn_IsPrimaryKeyFalse()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE t (id INT PRIMARY KEY, email TEXT NOT NULL);");

        var email = catalog.Tables[0].Columns.Single(c => c.Name == "email");
        Assert.False(email.IsPrimaryKey);
    }

    // -------------------------------------------------------------------------
    // Foreign keys
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanSql_InlineForeignKey_ParsesReference()
    {
        const string sql = """
            CREATE TABLE order_items (
                id INT PRIMARY KEY,
                order_id INT NOT NULL REFERENCES orders(id)
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        var col = catalog.Tables[0].Columns.Single(c => c.Name == "order_id");
        Assert.NotNull(col.ForeignKey);
        Assert.Equal("orders", col.ForeignKey.PrincipalTable);
        Assert.Equal("id", col.ForeignKey.PrincipalColumn);
    }

    [Fact]
    public void ScanSql_TableLevelForeignKey_ParsesReference()
    {
        const string sql = """
            CREATE TABLE order_items (
                id INT NOT NULL,
                order_id INT NOT NULL,
                CONSTRAINT fk_order FOREIGN KEY (order_id) REFERENCES orders(id)
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        var col = catalog.Tables[0].Columns.Single(c => c.Name == "order_id");
        Assert.NotNull(col.ForeignKey);
        Assert.Equal("orders", col.ForeignKey.PrincipalTable);
        Assert.Equal("id", col.ForeignKey.PrincipalColumn);
    }

    [Fact]
    public void ScanSql_ColumnWithoutFk_ForeignKeyNull()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE t (id INT PRIMARY KEY, name TEXT);");

        Assert.All(catalog.Tables[0].Columns, c => Assert.Null(c.ForeignKey));
    }

    // -------------------------------------------------------------------------
    // Comments
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanSql_LineComments_Ignored()
    {
        const string sql = """
            -- This is a comment
            CREATE TABLE t (
                id INT NOT NULL -- primary key
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        Assert.Single(catalog.Tables);
        Assert.Single(catalog.Tables[0].Columns);
    }

    [Fact]
    public void ScanSql_BlockComments_Ignored()
    {
        const string sql = """
            /* header comment */
            CREATE TABLE t (
                id INT NOT NULL /* inline */
            );
            """;

        var catalog = DdlSchemaScanner.ScanSql(sql);

        Assert.Single(catalog.Tables);
        Assert.Single(catalog.Tables[0].Columns);
    }

    // -------------------------------------------------------------------------
    // Store type preservation
    // -------------------------------------------------------------------------

    [Fact]
    public void ScanSql_TypeWithPrecision_PreservedInStoreType()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE t (total DECIMAL(10,2) NOT NULL);");

        Assert.Equal("DECIMAL(10,2)", catalog.Tables[0].Columns[0].StoreType);
    }

    [Fact]
    public void ScanSql_VarcharType_PreservedInStoreType()
    {
        var catalog = DdlSchemaScanner.ScanSql("CREATE TABLE t (email VARCHAR(255) NOT NULL);");

        Assert.Equal("VARCHAR(255)", catalog.Tables[0].Columns[0].StoreType);
    }

}
