using System.Text.Json;
using System.Text.Json.Nodes;
using CycloneDX.Models;

namespace SbomForge.Tests;

/// <summary>
/// Provides deterministic SBOM comparison for snapshot/golden-file tests.
/// Strips non-deterministic fields (serialNumber, timestamp) and sorts arrays
/// (components, dependencies) by key to produce stable JSON output.
/// </summary>
internal static class SnapshotTestHelper
{
    private static readonly JsonSerializerOptions PrettyPrint = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Normalizes a BOM JSON string for deterministic comparison.
    /// Removes serialNumber and timestamp, sorts components by bom-ref
    /// and dependencies by ref.
    /// </summary>
    public static string NormalizeBomJson(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject root)
            return json;

        // Remove non-deterministic fields.
        root.Remove("serialNumber");
        RemoveTimestamp(root);

        // Sort components array by bom-ref for stable ordering.
        SortArrayByKey(root, "components", "bom-ref");

        // Sort dependencies array by ref, and each dependsOn sub-array.
        SortDependencies(root);

        return root.ToJsonString(PrettyPrint);
    }

    /// <summary>
    /// Compares a BOM JSON against a golden-file. If the golden file does not exist,
    /// writes the normalized JSON as the golden file and fails the test.
    /// On mismatch, writes an .actual.json sibling and fails with a diff message.
    /// </summary>
    public static void AssertMatchesGoldenFile(string actualJson, string goldenFilePath)
    {
        string normalizedActual = NormalizeBomJson(actualJson);

        if (!File.Exists(goldenFilePath))
        {
            File.WriteAllText(goldenFilePath, normalizedActual);
            Assert.Fail($"Golden file did not exist and was created at: {goldenFilePath}. " +
                        "Review the file and re-run the test.");
            return;
        }

        string goldenContent = File.ReadAllText(goldenFilePath);
        string normalizedGolden = NormalizeBomJson(goldenContent);

        if (normalizedActual == normalizedGolden)
            return;

        // Write actual file for diffing.
        string actualFilePath = Path.ChangeExtension(goldenFilePath, ".actual.json");
        File.WriteAllText(actualFilePath, normalizedActual);

        // Build a useful diff message showing the first difference.
        string diffMessage = BuildDiffMessage(normalizedGolden, normalizedActual);
        Assert.Fail($"SBOM does not match golden file.\n" +
                    $"Golden: {goldenFilePath}\n" +
                    $"Actual: {actualFilePath}\n\n{diffMessage}");
    }

    /// <summary>
    /// Serializes a <see cref="Bom"/> to its normalized JSON form.
    /// </summary>
    public static string SerializeAndNormalize(Bom bom)
    {
        string json = CycloneDX.Json.Serializer.Serialize(bom);
        return NormalizeBomJson(json);
    }

    // ──────────────────────── Internal Helpers ────────────────────────

    private static void RemoveTimestamp(JsonObject root)
    {
        if (root["metadata"] is JsonObject metadata)
        {
            metadata.Remove("timestamp");
        }
    }

    private static void SortArrayByKey(JsonObject root, string arrayProperty, string sortKey)
    {
        if (root[arrayProperty] is not JsonArray array)
            return;

        var sorted = array
            .Select(n => n!.Deserialize<JsonObject>()!)
            .OrderBy(o => o[sortKey]?.GetValue<string>() ?? "", StringComparer.Ordinal)
            .ToList();

        root[arrayProperty] = new JsonArray(sorted.Select(o => (JsonNode)JsonNode.Parse(o.ToJsonString())!).ToArray());
    }

    private static void SortDependencies(JsonObject root)
    {
        if (root["dependencies"] is not JsonArray deps)
            return;

        // Sort each dependsOn sub-array, then sort the outer array by ref.
        var sorted = deps
            .Select(n => n!.Deserialize<JsonObject>()!)
            .Select(dep =>
            {
                if (dep["dependsOn"] is JsonArray dependsOn)
                {
                    var sortedRefs = dependsOn
                        .Select(r => r!.GetValue<string>())
                        .OrderBy(r => r, StringComparer.Ordinal)
                        .ToList();
                    dep["dependsOn"] = new JsonArray(sortedRefs.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
                }
                return dep;
            })
            .OrderBy(dep => dep["ref"]?.GetValue<string>() ?? "", StringComparer.Ordinal)
            .ToList();

        root["dependencies"] = new JsonArray(sorted.Select(o => (JsonNode)JsonNode.Parse(o.ToJsonString())!).ToArray());
    }

    private static string BuildDiffMessage(string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');

        for (int i = 0; i < Math.Min(expectedLines.Length, actualLines.Length); i++)
        {
            if (expectedLines[i] != actualLines[i])
            {
                return $"First difference at line {i + 1}:\n" +
                       $"  Expected: {expectedLines[i].Trim()}\n" +
                       $"  Actual:   {actualLines[i].Trim()}";
            }
        }

        if (expectedLines.Length != actualLines.Length)
        {
            return $"Files have different lengths: expected {expectedLines.Length} lines, actual {actualLines.Length} lines.";
        }

        return "Files differ but no line-level difference found (possible whitespace issue).";
    }
}
