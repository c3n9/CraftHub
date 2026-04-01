using System.Collections.Generic;
using CraftHub.Models;

namespace CraftHub.Services;

public interface IJsonService
{
    /// <summary>Parse JSON string and detect fields with inferred types.</summary>
    List<JsonFieldMapping> DetectFields(string json);

    /// <summary>Parse JSON data into rows using the given property definitions.</summary>
    List<DynamicDataRow> ParseJsonData(string json, IReadOnlyList<JsonPropertyDefinition> properties);

    /// <summary>Serialize rows to JSON string.</summary>
    string SerializeToJson(IReadOnlyList<DynamicDataRow> rows, IReadOnlyList<JsonPropertyDefinition> properties);
}
