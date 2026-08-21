using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

public static class JsonFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(new SimpleFunction(
            new FunctionMetadata("TYPEOF", FunctionCategory.Json,
                "The value's kind: \"number\", \"string\", \"bool\", \"null\", \"array\", or \"object\".",
                "TYPEOF(A1) = \"number\"", new[] { new ArgSpec("value", "Value to inspect.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("TYPEOF", args, 1, 1, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                return v.IsError ? v : FormulaValue.Of(v.TypeName);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("ARRAYLEN", FunctionCategory.Json, "Number of elements in an array.",
                "ARRAYLEN(PARSEJSON(\"[1,2,3]\")) = 3", new[] { new ArgSpec("array", "Array value.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("ARRAYLEN", args, 1, 1, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                if (v.IsError) return v;
                return v.Kind == FormulaValueKind.Array
                    ? FormulaValue.Of(v.AsArray.Count)
                    : FormulaValue.Of(FormulaErrorCode.Value, $"ARRAYLEN expects an array, got {v.TypeName}.");
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("HASKEY", FunctionCategory.Json, "TRUE if the object has the given key.",
                "HASKEY(A1, \"price\")", new[] { new ArgSpec("object", "Object value."), new ArgSpec("key", "Key to look for.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("HASKEY", args, 2, 2, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                if (v.IsError) return v;
                if (!FunctionArgs.TryText(args[1], ctx, out var key, out var err)) return err;
                return v.Kind == FormulaValueKind.Object
                    ? FormulaValue.Of(v.AsObject.ContainsKey(key))
                    : FormulaValue.Of(FormulaErrorCode.Value, $"HASKEY expects an object, got {v.TypeName}.");
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("KEYS", FunctionCategory.Json, "Array of an object's key names.",
                "KEYS(A1)", new[] { new ArgSpec("object", "Object value.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("KEYS", args, 1, 1, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                if (v.IsError) return v;
                return v.Kind == FormulaValueKind.Object
                    ? FormulaValue.Of(v.AsObject.Keys.Select(FormulaValue.Of).ToList())
                    : FormulaValue.Of(FormulaErrorCode.Value, $"KEYS expects an object, got {v.TypeName}.");
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("JSONPATH", FunctionCategory.Json, "Reads a nested value via a dotted/bracketed path, e.g. \"items[0].name\".",
                "JSONPATH(A1, \"settings.tax\")",
                new[] { new ArgSpec("value", "Object/array value."), new ArgSpec("path", "Dotted/bracketed path.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("JSONPATH", args, 2, 2, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                if (v.IsError) return v;
                if (!FunctionArgs.TryText(args[1], ctx, out var path, out var err)) return err;
                return ApplyJsonPath(v, path);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("TOJSON", FunctionCategory.Json, "Serializes a value to a JSON text string.",
                "TOJSON(A1)", new[] { new ArgSpec("value", "Value to serialize.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("TOJSON", args, 1, 1, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                if (v.IsError) return v;
                if (v.IsMissing) return FormulaValue.Of(FormulaErrorCode.Value, "Cannot serialize a missing value.");
                var node = ToJsonNode(v);
                return FormulaValue.Of(node is null ? "null" : node.ToJsonString());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("PARSEJSON", FunctionCategory.Json, "Parses a JSON text string into a value.",
                "PARSEJSON(\"[1,2,3]\")", new[] { new ArgSpec("text", "JSON text.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("PARSEJSON", args, 1, 1, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var err)) return err;
                try
                {
                    return FromJsonNode(JsonNode.Parse(text));
                }
                catch (JsonException)
                {
                    return FormulaValue.Of(FormulaErrorCode.Value, "Invalid JSON text.");
                }
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("FLATTEN", FunctionCategory.Json, "Flattens nested arrays into one flat array.",
                "FLATTEN(PARSEJSON(\"[[1,2],[3]]\")) = [1,2,3]", new[] { new ArgSpec("array", "Array value.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("FLATTEN", args, 1, 1, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                if (v.IsError) return v;
                if (v.Kind != FormulaValueKind.Array)
                    return FormulaValue.Of(FormulaErrorCode.Value, $"FLATTEN expects an array, got {v.TypeName}.");

                var flat = new List<FormulaValue>();
                Walk(v, flat);
                return FormulaValue.Of(flat);

                static void Walk(FormulaValue x, List<FormulaValue> into)
                {
                    if (x.Kind == FormulaValueKind.Array)
                        foreach (var item in x.AsArray)
                            Walk(item, into);
                    else
                        into.Add(x);
                }
            }));
    }

    private static FormulaValue ApplyJsonPath(FormulaValue root, string path)
    {
        var current = root;
        var i = 0;

        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;
                var start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                current = GetMember(current, path[start..i]);
            }
            else if (path[i] == '[')
            {
                i++;
                var start = i;
                while (i < path.Length && path[i] != ']') i++;
                if (i >= path.Length) return FormulaValue.Of(FormulaErrorCode.Value, "Unterminated '[' in JSONPATH.");
                var idxText = path[start..i];
                i++; // skip ']'
                current = int.TryParse(idxText, out var idx)
                    ? GetIndex(current, idx)
                    : FormulaValue.Of(FormulaErrorCode.Value, $"'{idxText}' is not a valid index in JSONPATH.");
            }
            else
            {
                var start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                current = GetMember(current, path[start..i]);
            }

            if (current.IsError) return current;
        }

        return current;
    }

    private static FormulaValue GetMember(FormulaValue v, string key)
    {
        if (key.Length == 0) return FormulaValue.Of(FormulaErrorCode.Value, "Empty key in JSONPATH.");
        if (v.Kind != FormulaValueKind.Object)
            return FormulaValue.Of(FormulaErrorCode.Value, $"Cannot read key '{key}' of a {v.TypeName}.");
        return v.AsObject.TryGetValue(key, out var val) ? val : FormulaValue.Missing;
    }

    private static FormulaValue GetIndex(FormulaValue v, int idx)
    {
        if (v.Kind != FormulaValueKind.Array)
            return FormulaValue.Of(FormulaErrorCode.Value, $"Cannot index a {v.TypeName}.");
        var arr = v.AsArray;
        return idx >= 0 && idx < arr.Count ? arr[idx] : FormulaValue.Of(FormulaErrorCode.Ref, "JSONPATH index is out of range.");
    }

    private static JsonNode? ToJsonNode(FormulaValue v) => v.Kind switch
    {
        FormulaValueKind.Null => null,
        FormulaValueKind.Number => JsonValue.Create(v.AsNumber),
        FormulaValueKind.Boolean => JsonValue.Create(v.AsBoolean),
        FormulaValueKind.Text => JsonValue.Create(v.AsText),
        FormulaValueKind.Array => new JsonArray(v.AsArray.Select(ToJsonNode).ToArray()),
        FormulaValueKind.Object => new JsonObject(v.AsObject.Select(kv =>
            new KeyValuePair<string, JsonNode?>(kv.Key, ToJsonNode(kv.Value)))),
        _ => null
    };

    private static FormulaValue FromJsonNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return FormulaValue.Null;
            case JsonArray arr:
                return FormulaValue.Of(arr.Select(FromJsonNode).ToList());
            case JsonObject obj:
                return FormulaValue.Of(obj.ToDictionary(kv => kv.Key, kv => FromJsonNode(kv.Value)));
            case JsonValue val:
                if (val.TryGetValue<bool>(out var b)) return FormulaValue.Of(b);
                if (val.TryGetValue<decimal>(out var d)) return FormulaValue.Of(d);
                if (val.TryGetValue<string>(out var s)) return FormulaValue.Of(s);
                return FormulaValue.Of(FormulaErrorCode.Value, "Unsupported JSON value.");
            default:
                return FormulaValue.Of(FormulaErrorCode.Value, "Unsupported JSON value.");
        }
    }
}
