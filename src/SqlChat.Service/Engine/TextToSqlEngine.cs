using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Models;
using SqlChat.Service.Generation;
using SqlChat.Service.Security;

namespace SqlChat.Service.Engine;

/// <summary>
/// Coordinates schema retrieval, security validation of generated SQL, and natural language to SQL generation.
/// Flow: produce SQL → <see cref="ISqlSecurityValidator.ValidateGeneratedSql"/> → return.
/// </summary>
public sealed class TextToSqlEngine
{
    private readonly ISchemaProvider _schemaProvider;
    private readonly ISqlSecurityValidator _sqlSecurity;

    public TextToSqlEngine(
        ISchemaProvider schemaProvider,
        ISqlSecurityValidator? sqlSecurityValidator = null)
    {
        _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
        _sqlSecurity = sqlSecurityValidator ?? new SqlSecurityValidator();
    }

    public async Task<SchemaCatalog> GetSchemaAsync(CancellationToken cancellationToken = default) =>
        await _schemaProvider.GetSchemaAsync(cancellationToken).ConfigureAwait(false);

    public SqlValidationResult ValidateGeneratedSql(string sql) =>
        _sqlSecurity.ValidateGeneratedSql(sql);

    public void ValidateGeneratedSqlOrThrow(string sql)
    {
        var result = ValidateGeneratedSql(sql);
        if (result.IsValid)
            return;

        throw new SqlSecurityException(result.RejectionReason ?? "Generated SQL failed security validation.");
    }

    public async Task<string> GenerateSqlAsync(string naturalLanguage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalLanguage);

        var schema = await GetSchemaAsync(cancellationToken).ConfigureAwait(false);
        var sql = SimpleTextToSqlGenerator.Generate(naturalLanguage, schema);
        ValidateGeneratedSqlOrThrow(sql);
        return sql;
    }
}

