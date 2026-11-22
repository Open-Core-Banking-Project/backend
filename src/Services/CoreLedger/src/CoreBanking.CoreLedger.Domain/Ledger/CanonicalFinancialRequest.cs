using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoreBanking.CoreLedger.Domain.Ledger;

/// <summary>
/// Canonical JSON and SHA-256 identity for a financial command. Object property order, insignificant
/// whitespace and equivalent decimal spellings cannot produce a second financial request identity.
/// </summary>
public sealed record CanonicalFinancialRequest
{
    private CanonicalFinancialRequest(string canonicalJson, string hash)
    {
        CanonicalJson = canonicalJson;
        Hash = hash;
    }

    public string CanonicalJson { get; }

    public string Hash { get; }

    public static CanonicalFinancialRequest Create(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        });
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteCanonical(writer, document.RootElement);
        }

        string canonicalJson = Encoding.UTF8.GetString(stream.ToArray());
        string hash = Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
        return new CanonicalFinancialRequest(canonicalJson, hash);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, element);
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, element);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new ArgumentException("Financial request contains an unsupported JSON token.", nameof(element));
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement element)
    {
        JsonProperty[] properties = element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        HashSet<string> names = new(StringComparer.Ordinal);
        if (properties.Any(property => !names.Add(property.Name)))
        {
            throw new ArgumentException("Financial request contains a duplicate JSON property.", nameof(element));
        }

        writer.WriteStartObject();
        foreach (JsonProperty property in properties)
        {
            writer.WritePropertyName(property.Name);
            WriteCanonical(writer, property.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement element)
    {
        string raw = element.GetRawText();
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
        {
            throw new ArgumentException("Financial request number is outside the supported decimal range.", nameof(element));
        }

        writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture), skipInputValidation: false);
    }
}
