using SqlChat.Domain.Abstractions;
using SqlChat.Domain.Exceptions;
using SqlChat.Domain.Models;
using SqlChat.Service.Security;

namespace SqlChat.Service.Engine;

public sealed class TextToSqlEngine
{
    private readonly ISchemaProvider _schemaProvider;
    private readonly ISqlSecurityValidator _sqlSecurity;
    private readonly ISqlGenerator _generator;

    public TextToSqlEngine(
        ISchemaProvider schemaProvider,
        ISqlGenerator generator,
        ISqlSecurityValidator? sqlSecurityValidator = null)
    {
        _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
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
        var sql = await _generator.GenerateAsync(naturalLanguage, schema, cancellationToken).ConfigureAwait(false);
        ValidateGeneratedSqlOrThrow(sql);
        return sql;
    }
}
