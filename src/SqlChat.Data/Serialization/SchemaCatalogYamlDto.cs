using SqlChat.Domain.Models;

namespace SqlChat.Data.Serialization;

internal sealed class SchemaCatalogYaml
{
    public List<TableDescriptorYaml> Tables { get; set; } = [];

    internal SchemaCatalog ToDomain() =>
        new(Tables.Select(t => t.ToDomain()).ToList());

    internal static SchemaCatalogYaml FromDomain(SchemaCatalog catalog) => new()
    {
        Tables = catalog.Tables.Select(TableDescriptorYaml.FromDomain).ToList()
    };
}

internal sealed class TableDescriptorYaml
{
    public string Name { get; set; } = "";
    public string? Schema { get; set; }
    public string? Description { get; set; }
    public List<ColumnDescriptorYaml> Columns { get; set; } = [];

    internal TableDescriptor ToDomain() =>
        new(Name, Schema, Columns.Select(c => c.ToDomain()).ToList(), Description);

    internal static TableDescriptorYaml FromDomain(TableDescriptor t) => new()
    {
        Name = t.Name,
        Schema = t.Schema,
        Description = t.Description,
        Columns = t.Columns.Select(ColumnDescriptorYaml.FromDomain).ToList()
    };
}

internal sealed class ColumnDescriptorYaml
{
    public string Name { get; set; } = "";
    public string StoreType { get; set; } = "";
    public string? Description { get; set; }
    public bool IsNullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; }
    public ForeignKeyReferenceYaml? ForeignKey { get; set; }
    public List<string> SemanticTags { get; set; } = [];

    internal ColumnDescriptor ToDomain() =>
        ColumnDescriptor.Create(Name, StoreType, Description, IsNullable, IsPrimaryKey,
            ForeignKey?.ToDomain(), SemanticTags);

    internal static ColumnDescriptorYaml FromDomain(ColumnDescriptor c) => new()
    {
        Name = c.Name,
        StoreType = c.StoreType,
        Description = c.Description,
        IsNullable = c.IsNullable,
        IsPrimaryKey = c.IsPrimaryKey,
        ForeignKey = c.ForeignKey is null ? null : ForeignKeyReferenceYaml.FromDomain(c.ForeignKey),
        SemanticTags = c.SemanticTags.ToList()
    };
}

internal sealed class ForeignKeyReferenceYaml
{
    public string PrincipalTable { get; set; } = "";
    public string? PrincipalSchema { get; set; }
    public string PrincipalColumn { get; set; } = "";

    internal ForeignKeyReference ToDomain() =>
        new(PrincipalTable, PrincipalSchema, PrincipalColumn);

    internal static ForeignKeyReferenceYaml FromDomain(ForeignKeyReference fk) => new()
    {
        PrincipalTable = fk.PrincipalTable,
        PrincipalSchema = fk.PrincipalSchema,
        PrincipalColumn = fk.PrincipalColumn
    };
}
