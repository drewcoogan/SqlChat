using System.Text.Json;
using SqlChat.Domain.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SqlChat.Data.Serialization;

public static class SchemaCatalogSerializer
{
    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string ToYaml(SchemaCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return _serializer.Serialize(SchemaCatalogYaml.FromDomain(catalog));
    }

    public static SchemaCatalog FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var dto = JsonSerializer.Deserialize<SchemaCatalogYaml>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize schema JSON.");
        return dto.ToDomain();
    }

    public static SchemaCatalog FromYaml(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);
        var dto = _deserializer.Deserialize<SchemaCatalogYaml>(yaml)
            ?? throw new InvalidOperationException("Failed to deserialize schema YAML.");
        return dto.ToDomain();
    }

    public static async Task SaveYamlAsync(SchemaCatalog catalog, string path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await File.WriteAllTextAsync(path, ToYaml(catalog), ct).ConfigureAwait(false);
    }

    public static async Task<SchemaCatalog> LoadYamlAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var yaml = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return FromYaml(yaml);
    }
}
