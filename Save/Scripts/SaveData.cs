using System;
using System.Collections.Generic;
#if USING_GAMESAVE
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
#endif

namespace Raccoon.Save
{
    /// <summary>
    /// Key / value store backed by a JSON object. GameSave wraps one instance, migrations receive it directly.
    /// Values are stored as JSON, so Get always returns a new copy: modify it then Set it back.
    /// Without USING_GAMESAVE (Newtonsoft JSON not installed) it stays empty: Get returns the default, Set does nothing.
    /// </summary>
    public class SaveData
    {
#if USING_GAMESAVE
        internal static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            //Keeps date-looking strings as strings instead of turning them into DateTime
            DateParseHandling = DateParseHandling.None,
            Converters = { new UnityStructConverter() },
        });

        private readonly JObject values;

        public SaveData() : this(new JObject()) { }

        internal SaveData(JObject values)
        {
            this.values = values;
        }

        public int Count => values.Count;
        public ICollection<string> Keys => ((IDictionary<string, JToken>)values).Keys;

        public bool HasKey(string key) => key != null && values.ContainsKey(key);

        public T Get<T>(string key, T defaultValue = default)
        {
            return TryGet(key, out T value) ? value : defaultValue;
        }

        public bool TryGet<T>(string key, out T value)
        {
            value = default;
            if (key == null || !values.TryGetValue(key, out JToken token)) return false;
            try
            {
                value = token.ToObject<T>(Serializer);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSave] Can't read '{key}' as {typeof(T).Name}: {e.Message}");
                return false;
            }
        }

        //Returns true when the stored value changed
        public bool Set<T>(string key, T value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            JToken token;
            try
            {
                token = value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameSave] Can't save '{key}' ({typeof(T).Name}): {e.Message}");
                return false;
            }
            return SetToken(key, token);
        }

        public bool Delete(string key) => key != null && values.Remove(key);

        public void Clear() => values.RemoveAll();

        #region Raw JSON (editor tools)
        internal JTokenType GetTokenType(string key) => values.TryGetValue(key, out JToken token) ? token.Type : JTokenType.None;

        internal string GetJson(string key, bool indented)
        {
            return values.TryGetValue(key, out JToken token) ? token.ToString(indented ? Formatting.Indented : Formatting.None) : null;
        }

        internal bool TrySetJson(string key, string json, out bool changed, out string error)
        {
            changed = false;
            try
            {
                changed = SetToken(key, Parse(json));
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
        #endregion

        #region Envelope
        //File content: {"version":1,"savedAt":<unix ms UTC>,"data":{...}}
        internal string ToEnvelopeJson(int version, bool indented)
        {
            using (StringWriter sw = new StringWriter())
            using (JsonTextWriter writer = new JsonTextWriter(sw))
            {
                writer.Formatting = indented ? Formatting.Indented : Formatting.None;
                writer.WriteStartObject();
                writer.WritePropertyName("version");
                writer.WriteValue(version);
                writer.WritePropertyName("savedAt");
                writer.WriteValue(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                writer.WritePropertyName("data");
                values.WriteTo(writer);
                writer.WriteEndObject();
                writer.Flush();
                return sw.ToString();
            }
        }

        internal static bool TryParseEnvelope(string json, out SaveData data, out int version)
        {
            data = null;
            version = 0;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                if (!(Parse(json) is JObject root) || !(root["data"] is JObject values)) return false;
                version = root["version"]?.Type == JTokenType.Integer ? root["version"].Value<int>() : 1;
                data = new SaveData(values);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        #endregion

        private bool SetToken(string key, JToken token)
        {
            if (values.TryGetValue(key, out JToken old) && JToken.DeepEquals(old, token)) return false;
            values[key] = token;
            return true;
        }

        private static JToken Parse(string json)
        {
            using (JsonTextReader reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                return JToken.ReadFrom(reader);
        }
#else
        public int Count => 0;
        public ICollection<string> Keys => Array.Empty<string>();
        public bool HasKey(string key) => false;
        public T Get<T>(string key, T defaultValue = default) => defaultValue;
        public bool TryGet<T>(string key, out T value) { value = default; return false; }
        public bool Set<T>(string key, T value) => false;
        public bool Delete(string key) => false;
        public void Clear() { }

        internal string ToEnvelopeJson(int version, bool indented) => null;
        internal static bool TryParseEnvelope(string json, out SaveData data, out int version) { data = null; version = 0; return false; }
#endif
    }
}
