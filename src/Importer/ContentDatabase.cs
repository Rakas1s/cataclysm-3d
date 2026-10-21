using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cataclysm3D.Importer;

/// <summary>
/// Loads CDDA JSON definitions and resolves inheritance: <c>copy-from</c>, <c>abstract</c>,
/// <c>extend</c>, <c>delete</c>, <c>relative</c> and <c>proportional</c>.
/// </summary>
public sealed class ContentDatabase
{
    public const string ItemFamily = "ITEM";

    /// <summary>
    /// CDDA item types share one id namespace, so copy-from can cross them. Since 0.I most items
    /// use type "ITEM" with subtypes; the legacy per-kind types still appear and join the same family.
    /// </summary>
    private static readonly HashSet<string> ItemTypes = new(StringComparer.Ordinal)
    {
        "ITEM", "GENERIC", "TOOL", "GUN", "GUNMOD", "AMMO", "MAGAZINE", "ARMOR", "COMESTIBLE", "BOOK",
        "TOOLMOD", "TOOL_ARMOR", "BIONIC_ITEM", "ENGINE", "WHEEL", "PET_ARMOR", "BATTERY",
    };

    private static readonly string[] InheritanceKeys =
        { "copy-from", "abstract", "extend", "delete", "relative", "proportional" };

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<(string Family, string Id), Definition> _raw = new();
    private readonly Dictionary<(string Family, string Id), JsonObject> _resolved = new();
    private readonly List<JsonObject> _unkeyed = new();

    public List<string> Warnings { get; } = new();

    /// <summary>Definitions without an id (recipes keyed by result, migrations, etc.).</summary>
    public IReadOnlyList<JsonObject> Unkeyed => _unkeyed;

    public static string FamilyOf(string type) => ItemTypes.Contains(type) ? ItemFamily : type;

    public void LoadDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            LoadJsonText(File.ReadAllText(file), file);
    }

    public void LoadFile(string path) => LoadJsonText(File.ReadAllText(path), path);

    public void LoadJsonText(string json, string source)
    {
        var root = JsonNode.Parse(json, documentOptions: ParseOptions);
        var objects = root switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject single => new[] { single },
            _ => Enumerable.Empty<JsonObject>(),
        };
        foreach (var obj in objects)
            Add(obj, source);
    }

    private void Add(JsonObject obj, string source)
    {
        if (obj["type"]?.GetValue<string>() is not { } type)
        {
            Warnings.Add($"{source}: definition without \"type\"");
            return;
        }

        var ids = IdsOf(obj).ToList();
        if (ids.Count == 0)
        {
            _unkeyed.Add(obj);
            return;
        }

        string family = FamilyOf(type);
        foreach (var id in ids)
        {
            var key = (family, id);
            if (_raw.ContainsKey(key))
                Warnings.Add($"{source}: {family} \"{id}\" redefined; later definition wins");
            _raw[key] = new Definition(obj, source);
            _resolved.Remove(key);
        }
    }

    private static IEnumerable<string> IdsOf(JsonObject obj)
    {
        switch (obj["id"] ?? obj["abstract"])
        {
            case JsonValue v when v.TryGetValue(out string? s):
                yield return s;
                break;
            case JsonArray a:
                foreach (var n in a)
                    if (n is JsonValue nv && nv.TryGetValue(out string? s2))
                        yield return s2;
                break;
        }
    }

    public bool Contains(string type, string id) => _raw.ContainsKey((FamilyOf(type), id));

    public IEnumerable<string> Families => _raw.Keys.Select(k => k.Family).Distinct();

    /// <summary>All resolved, non-abstract definitions of a family ("item", "MONSTER", "terrain", …).</summary>
    public IEnumerable<(string Id, JsonObject Data)> All(string family)
    {
        foreach (var ((fam, id), def) in _raw)
            if (fam == family && def.Data["abstract"] is null)
                yield return (id, Resolve(fam, id));
    }

    /// <summary>The definition with inheritance applied. Returned object is a fresh copy owned by the cache.</summary>
    public JsonObject Get(string type, string id) => Resolve(FamilyOf(type), id);

    private JsonObject Resolve(string family, string id, HashSet<string>? visiting = null)
    {
        if (_resolved.TryGetValue((family, id), out var cached))
            return cached;
        if (!_raw.TryGetValue((family, id), out var def))
            throw new KeyNotFoundException($"No {family} \"{id}\"");

        visiting ??= new HashSet<string>();
        if (!visiting.Add(id))
            throw new InvalidOperationException($"copy-from cycle at {family} \"{id}\"");

        JsonObject result;
        if (def.Data["copy-from"]?.GetValue<string>() is { } parentId)
        {
            result = (JsonObject)Resolve(family, parentId, visiting).DeepClone();
            result.Remove("abstract");
            foreach (var (key, value) in def.Data)
                if (!InheritanceKeys.Contains(key))
                    result[key] = value?.DeepClone();
            ApplyRelative(result, def.Data["relative"] as JsonObject, proportional: false, $"{family} {id}");
            ApplyRelative(result, def.Data["proportional"] as JsonObject, proportional: true, $"{family} {id}");
        }
        else
        {
            result = (JsonObject)def.Data.DeepClone();
            foreach (var key in new[] { "copy-from", "relative", "proportional" })
                result.Remove(key);
        }

        ApplyExtend(result, def.Data["extend"] as JsonObject);
        ApplyDelete(result, def.Data["delete"] as JsonObject);
        result.Remove("extend");
        result.Remove("delete");

        if (IdsOf(def.Data).Count() > 1 || def.Data["abstract"] is not null)
            result["id"] = id;

        _resolved[(family, id)] = result;
        return result;
    }

    private void ApplyRelative(JsonObject target, JsonObject? changes, bool proportional, string context)
    {
        if (changes is null)
            return;
        foreach (var (key, change) in changes)
        {
            if (change is JsonObject nested)
            {
                switch (target[key])
                {
                    // Damage-style entries are matched by type: { "damage_type": "bullet", "amount": 0.9 }.
                    case var t when nested["damage_type"] is not null:
                        ApplyToDamage(t, nested, proportional, $"{context} {key}");
                        break;
                    case JsonObject nestedTarget:
                        ApplyRelative(nestedTarget, nested, proportional, context);
                        break;
                    default:
                        Warnings.Add($"{context}: relative/proportional \"{key}\" has no object to modify");
                        break;
                }
                continue;
            }
            if (!TryApply(target[key], change, proportional, out var updated))
            {
                Warnings.Add($"{context}: unsupported relative/proportional \"{key}\"");
                continue;
            }
            target[key] = updated;
        }
    }

    private void ApplyToDamage(JsonNode? target, JsonObject change, bool proportional, string context)
    {
        string damageType = change["damage_type"]!.GetValue<string>();
        var entries = target switch
        {
            JsonArray a => a.OfType<JsonObject>(),
            JsonObject o => new[] { o },
            _ => Enumerable.Empty<JsonObject>(),
        };
        var entry = entries.FirstOrDefault(e => e["damage_type"]?.GetValue<string>() == damageType);
        if (entry is null)
        {
            Warnings.Add($"{context}: no \"{damageType}\" damage to modify");
            return;
        }
        foreach (var (field, delta) in change)
        {
            if (field == "damage_type")
                continue;
            if (TryApply(entry[field], delta, proportional, out var updated))
                entry[field] = updated;
            else
                Warnings.Add($"{context}: unsupported {damageType} damage \"{field}\"");
        }
    }

    /// <summary>
    /// Applies one relative (add) or proportional (multiply) change. Handles plain numbers and
    /// CDDA unit strings ("250 g" * 0.75 → "187500 mg"). A relative change to a missing number starts from 0.
    /// </summary>
    private static bool TryApply(JsonNode? current, JsonNode? change, bool proportional, out JsonNode? result)
    {
        result = null;
        if (change is not JsonValue cv)
            return false;

        double? currentNumber = current is JsonValue tv && tv.TryGetValue(out double n) ? n : null;
        string? currentText = current is JsonValue ts && ts.TryGetValue(out string? s) ? s : null;

        if (cv.TryGetValue(out double delta))
        {
            if (currentNumber is { } value)
            {
                result = proportional ? value * delta : value + delta;
                return true;
            }
            if (current is null && !proportional)
            {
                result = delta;
                return true;
            }
            if (proportional && currentText is not null && Units.TryParseAny(currentText, out var dim, out double quantity))
            {
                result = Units.Format(quantity * delta, dim);
                return true;
            }
            return false;
        }

        // A unit-string delta, e.g. "relative": { "weight": "10 g" }.
        if (!proportional && cv.TryGetValue(out string? deltaText) && Units.TryParseAny(deltaText, out var deltaDim, out double deltaValue))
        {
            double baseValue = 0;
            if (currentText is not null && !Units.TryParse(currentText, deltaDim, out baseValue))
                return false;
            if (currentNumber is { } raw)
                baseValue = raw;
            result = Units.Format(baseValue + deltaValue, deltaDim);
            return true;
        }
        return false;
    }

    private static void ApplyExtend(JsonObject target, JsonObject? extend)
    {
        if (extend is null)
            return;
        foreach (var (key, values) in extend)
        {
            if (target[key] is not JsonArray array)
                target[key] = array = new JsonArray();
            foreach (var v in values is JsonArray va ? va : new JsonArray(values?.DeepClone()))
                array.Add(v?.DeepClone());
        }
    }

    private static void ApplyDelete(JsonObject target, JsonObject? delete)
    {
        if (delete is null)
            return;
        foreach (var (key, values) in delete)
        {
            if (target[key] is not JsonArray array)
                continue;
            var remove = (values is JsonArray va ? va : new JsonArray(values?.DeepClone()))
                .Select(v => v?.ToJsonString()).ToHashSet();
            for (int i = array.Count - 1; i >= 0; i--)
                if (remove.Contains(array[i]?.ToJsonString()))
                    array.RemoveAt(i);
        }
    }

    private sealed record Definition(JsonObject Data, string Source);
}
