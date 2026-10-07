#if USING_GAMESAVE
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Raccoon.Save
{
    /// <summary>
    /// Serializes UnityEngine structs (Vector2/3/4, Vector2Int, Quaternion, Color, Rect...) through JsonUtility.
    /// Newtonsoft alone loops on their computed properties (Vector3.normalized...).
    /// </summary>
    internal class UnityStructConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsValueType && !objectType.IsPrimitive && !objectType.IsEnum && objectType.Namespace == "UnityEngine";
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JObject.Parse(JsonUtility.ToJson(value)).WriteTo(writer);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return Activator.CreateInstance(objectType);
            return JsonUtility.FromJson(JObject.Load(reader).ToString(Formatting.None), objectType);
        }
    }
}
#endif
