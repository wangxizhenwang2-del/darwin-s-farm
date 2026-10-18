using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Persists model fields without Unity instance IDs. Object links use stable catalog
// IDs or map coordinates and are resolved only after the complete map exists.
internal sealed class SaveFieldCodec
{
    private readonly SaveAssetCatalog catalog;
    private readonly Dictionary<BlockInfo, string> blockIds;
    private readonly Dictionary<string, BlockInfo> blocks;
    internal SaveFieldCodec(SaveAssetCatalog catalog, Dictionary<BlockInfo, string> blockIds,
        Dictionary<string, BlockInfo> blocks)
    { this.catalog = catalog; this.blockIds = blockIds; this.blocks = blocks; }

    internal JObject Capture(object value)
    {
        var result = EncodeObject(value, value.GetType());
        if (value is PopulationData population) result["$populationId"] = population.PopulationId;
        return result;
    }
    internal void Restore(JObject data, object target)
    {
        if (data == null) throw new InvalidOperationException("Missing model data");
        Populate(data, target);
        if (target is PopulationData population)
        {
            string id = (string)data["$populationId"];
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Missing population identity");
            population.AssignPopulationId(id);
        }
    }
    private JToken Encode(object value, Type type)
    {
        if (value == null) return JValue.CreateNull();
        if (type == typeof(string) || type.IsPrimitive || type == typeof(decimal)) return JToken.FromObject(value);
        if (type.IsEnum) return new JValue(Convert.ToInt32(value));
        if (typeof(SpeciesData).IsAssignableFrom(type)) return new JValue(catalog.SpeciesId((SpeciesData)value));
        if (typeof(BlockInfo).IsAssignableFrom(type))
        {
            if (!blockIds.TryGetValue((BlockInfo)value, out string id)) throw new InvalidOperationException("Unknown block reference");
            return new JValue(id);
        }
        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            throw new InvalidOperationException("Unsupported Unity object in save: " + type.Name);
        if (value is IList list)
        {
            var array = new JArray();
            Type itemType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
            foreach (object item in list) array.Add(Encode(item, itemType));
            return array;
        }
        return EncodeObject(value,type);
    }
    private JObject EncodeObject(object value,Type type)
    {
        var objectData = new JObject();
        foreach (FieldInfo field in Fields(type)) objectData[field.Name] = Encode(field.GetValue(value), field.FieldType);
        return objectData;
    }
    private object Decode(JToken data, Type type)
    {
        if (data == null || data.Type == JTokenType.Null) return null;
        if (type == typeof(string)) return (string)data;
        if (type.IsPrimitive || type == typeof(decimal)) return data.ToObject(type);
        if (type.IsEnum) return Enum.ToObject(type, (int)data);
        if (typeof(SpeciesData).IsAssignableFrom(type)) return catalog.Species((string)data);
        if (typeof(BlockInfo).IsAssignableFrom(type))
        {
            if (!blocks.TryGetValue((string)data, out BlockInfo block)) throw new InvalidOperationException("Missing saved block link");
            return block;
        }
        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            throw new InvalidOperationException("Unsupported Unity object in save: " + type.Name);
        if (type.IsArray || typeof(IList).IsAssignableFrom(type))
        {
            var source = data as JArray ?? throw new InvalidOperationException("Invalid saved list");
            Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
            IList result = type.IsArray ? Array.CreateInstance(element, source.Count) :
                (IList)Activator.CreateInstance(type);
            for (int i = 0; i < source.Count; i++)
            {
                object item = Decode(source[i], element);
                if (type.IsArray) result[i] = item; else result.Add(item);
            }
            return result;
        }
        object instance = Activator.CreateInstance(type);
        Populate(data as JObject ?? throw new InvalidOperationException("Invalid saved object"), instance);
        return instance;
    }
    private void Populate(JObject data, object target)
    {
        foreach (FieldInfo field in Fields(target.GetType()))
        {
            if (!data.TryGetValue(field.Name, out JToken token)) throw new InvalidOperationException("Missing saved field " + field.Name);
            field.SetValue(target, Decode(token, field.FieldType));
        }
    }
    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (!field.IsStatic && !field.IsInitOnly &&
                (field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField))) &&
                field.Name != "community" && field.Name != "populationId" &&
                field.Name != "neighbors" && field.Name != "waterLinks" && field.Name != "riverBanks")
                yield return field;
    }
}
